using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using RenderServerGui.Models;

namespace RenderServerGui.Services
{
    /// <summary>
    /// Team Render 客户端看门狗控制器：忠实移植自 Team Render Watch Dog 的主循环，
    /// 仅把硬编码常量改为从 ModeProfile 读取，并把阻塞式 Thread.Sleep 换成可中断休眠以便停止。
    /// 逻辑保持完全一致：找进程 / 过热休息 / 三重异常判定(Responding+端口+BugReport) /
    /// 挂起计数达上限杀进程重启 / 启动前清缓存。
    /// </summary>
    public class TeamRenderController : IRenderController
    {
        private Thread _worker;
        private volatile bool _stopRequested;
        private ModeProfile _p;

        // 与原程序一致的运行期状态
        private Dictionary<int, int> _processHangCount = new Dictionary<int, int>();
        private DateTime _lastStartTime = DateTime.Now;

        public bool IsRunning { get; private set; }

        public event EventHandler<LogEntry> Log;
        public event EventHandler<RunnerStatus> StatusChanged;

        // 看门狗无逐帧概念，此事件保留但不触发（故抑制 CS0067）。
#pragma warning disable 0067
        public event EventHandler<FrameProgressInfo> FrameProgressChanged;
#pragma warning restore 0067

        public void Start(ModeProfile profile)
        {
            if (IsRunning) return;

            _p = profile;
            _processHangCount = new Dictionary<int, int>();
            _lastStartTime = DateTime.Now;
            _stopRequested = false;
            IsRunning = true;
            RaiseStatus(RunnerStatus.Running);

            LogInfo($"开始监控进程: {_p.ProcessName}");
            LogInfo($"端口号: {_p.Port}");
            LogInfo($"目标路径: {_p.ExePath}");
            LogInfo($"异常路径: {_p.ReportPath}");
            LogInfo($"检查间隔: {_p.CheckIntervalSeconds}秒");
            LogInfo($"连续挂起{_p.MaxHangCount}次后将终止进程");
            LogInfo($"连续运行{_p.WorkingMinutes}分钟后将休息{_p.RestMinutes}分钟");

            _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "TeamRenderController" };
            _worker.Start();
        }

        public void Stop()
        {
            if (!IsRunning) return;
            _stopRequested = true;
            RaiseStatus(RunnerStatus.Stopping);
            LogInfo("正在停止监控…");
        }

        private void WorkerLoop()
        {
            try
            {
                while (!_stopRequested)
                {
                    try
                    {
                        Process target = FindTargetProcess();

                        TimeSpan worked = DateTime.Now - _lastStartTime;
                        if (target != null && worked.TotalMinutes > _p.WorkingMinutes)
                        {
                            LogInfo($"连续工作{worked:hh\\:mm\\:ss}，休息降温");
                            KillProcess(target, false);
                            target = null;
                            if (!SleepCancellable(_p.RestMinutes * 60.0)) break;
                            LogInfo("休息结束");
                        }

                        if (target == null)
                        {
                            target = StartProcess();
                        }

                        CheckAndMonitorProcess(target);
                    }
                    catch (Exception ex)
                    {
                        LogError($"检查过程中发生错误: {ex.Message}");
                    }

                    if (!SleepCancellable(_p.CheckIntervalSeconds)) break;
                }
            }
            finally
            {
                IsRunning = false;
                RaiseStatus(RunnerStatus.Stopped);
                LogInfo("监控已停止。");
            }
        }

        private void CheckAndMonitorProcess(Process target)
        {
            bool portUsing = ProcessHealth.IsPortUsed(_p.Port);
            if (target == null)
            {
                _processHangCount = new Dictionary<int, int>();
                LogInfo($"目标进程不存在，端口[{_p.Port}]：{(portUsing ? "占用" : "断开")}");
                return;
            }

            try
            {
                bool responding = ProcessHealth.IsResponding(target);
                // 与原程序一致的有效窗口 = 间隔 *(最大挂起 + 1.5)
                var window = TimeSpan.FromSeconds(_p.CheckIntervalSeconds * (_p.MaxHangCount + 1.5));
                bool newBugReport = ProcessHealth.IsBugReportRecent(_p.ReportPath, window);

                if (!responding || !portUsing || newBugReport)
                {
                    int hangCount = GetAndIncrementHangCount(target.Id);
                    string tag = newBugReport ? "异常! " : "无响应! ";
                    LogWarn($"进程{tag}PID: {target.Id} 端口[{_p.Port}]：{(portUsing ? "占用" : "断开")}，挂起次数: {hangCount}/{_p.MaxHangCount}");

                    if (hangCount >= _p.MaxHangCount)
                    {
                        KillProcess(target, true);
                        _processHangCount.Remove(target.Id);
                    }
                }
                else
                {
                    if (_processHangCount.ContainsKey(target.Id))
                    {
                        _processHangCount.Remove(target.Id);
                        LogInfo($"进程恢复正常，重置挂起计数. PID: {target.Id} 端口[{_p.Port}]：{(portUsing ? "占用" : "断开")}");
                    }
                    else
                    {
                        TimeSpan worked = DateTime.Now - _lastStartTime;
                        LogInfo($"进程[{target.Id}]正常运行，端口[{_p.Port}]：{(portUsing ? "占用" : "断开")}，已工作{worked:hh\\:mm\\:ss}");
                    }
                }
            }
            finally
            {
                target.Dispose();
            }
        }

        private Process FindTargetProcess()
        {
            foreach (Process process in Process.GetProcessesByName(_p.ProcessName))
            {
                // 与原程序一致：按进程名命中即返回，不校验完整路径
                return process;
            }
            return null;
        }

        private int GetAndIncrementHangCount(int processId)
        {
            if (!_processHangCount.ContainsKey(processId))
                _processHangCount[processId] = 0;
            return ++_processHangCount[processId];
        }

        private void KillProcess(Process process, bool restart)
        {
            if (process != null)
            {
                try
                {
                    LogInfo($"正在终止进程… PID: {process.Id}");
                    process.Kill();
                    if (process.WaitForExit(5000))
                        LogInfo($"进程已成功终止. PID: {process.Id}");
                    else
                        LogWarn($"进程终止超时，但已发送终止信号. PID: {process.Id}");
                }
                catch (Exception ex)
                {
                    LogError($"终止进程失败: {ex.Message}");
                }

                SleepCancellable(_p.CheckIntervalSeconds * 2.0);
            }
            if (restart)
                StartProcess();
        }

        private Process StartProcess()
        {
            if (_p.ClearCache)
                ClearCache();

            Process process = null;
            try
            {
                process = Process.Start(_p.ExePath);
                _lastStartTime = DateTime.Now;
                LogInfo($"启动进程成功. PID: {process.Id}");
            }
            catch (Exception ex)
            {
                LogError($"启动进程失败: {ex.Message}");
            }

            SleepCancellable(_p.CheckIntervalSeconds);
            return process;
        }

        private void ClearCache()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_p.CachePath) || !Directory.Exists(_p.CachePath))
                    return;

                foreach (string file in Directory.GetFiles(_p.CachePath))
                {
                    try { File.Delete(file); }
                    catch (Exception ex) { LogError($"删除文件失败: {file} - {ex.Message}"); }
                }
                foreach (string dir in Directory.GetDirectories(_p.CachePath))
                {
                    try { Directory.Delete(dir, true); }
                    catch (Exception ex) { LogError($"删除目录失败: {dir} - {ex.Message}"); }
                }
                LogInfo("清空缓存完成");
            }
            catch (Exception ex)
            {
                LogError($"清空缓存异常: {ex.Message}");
            }
        }

        /// <summary>可中断休眠；返回 false 表示期间收到停止请求。</summary>
        private bool SleepCancellable(double seconds)
        {
            if (seconds <= 0) return !_stopRequested;
            var until = DateTime.Now.AddSeconds(seconds);
            while (DateTime.Now < until)
            {
                if (_stopRequested) return false;
                int chunk = (int)Math.Min(200, (until - DateTime.Now).TotalMilliseconds);
                Thread.Sleep(chunk < 0 ? 0 : chunk);
            }
            return !_stopRequested;
        }

        private void LogInfo(string m) => Log?.Invoke(this, new LogEntry(LogLevel.Info, m));
        private void LogWarn(string m) => Log?.Invoke(this, new LogEntry(LogLevel.Warn, m));
        private void LogError(string m) => Log?.Invoke(this, new LogEntry(LogLevel.Error, m));
        private void RaiseStatus(RunnerStatus s) => StatusChanged?.Invoke(this, s);
    }
}
