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
    /// 挂起计数达上限杀进程重启 / 启动前清缓存。日志文案经 Localizer 本地化（缺配置回退中文）。
    /// </summary>
    public class TeamRenderController : IRenderController
    {
        /// <summary>后台监控线程。</summary>
        private Thread _worker;
        /// <summary>停止请求标志，令所有可中断等待尽快返回。</summary>
        private volatile bool _stopRequested;
        /// <summary>本次运行的模式参数快照。</summary>
        private ModeProfile _p;

        /// <summary>按进程 ID 记录连续挂起次数（与原看门狗一致）。</summary>
        private Dictionary<int, int> _processHangCount = new Dictionary<int, int>();
        /// <summary>上次启动/重启客户端的时间，用于判定连续工作时长。</summary>
        private DateTime _lastStartTime = DateTime.Now;

        /// <summary>控制器是否正在运行。</summary>
        public bool IsRunning { get; private set; }

        /// <summary>产生一条日志时触发。</summary>
        public event EventHandler<LogEntry> Log;
        /// <summary>运行状态变化时触发。</summary>
        public event EventHandler<RunnerStatus> StatusChanged;

        /// <summary>进度事件（看门狗无逐帧概念，保留但不触发，故抑制 CS0067）。</summary>
#pragma warning disable 0067
        public event EventHandler<FrameProgressInfo> FrameProgressChanged;
#pragma warning restore 0067

        /// <summary>启动监控：打印参数概览并拉起后台监控线程。</summary>
        public void Start(ModeProfile profile)
        {
            if (IsRunning) return;

            _p = profile;
            _processHangCount = new Dictionary<int, int>();
            _lastStartTime = DateTime.Now;
            _stopRequested = false;
            IsRunning = true;
            RaiseStatus(RunnerStatus.Running);

            LogInfo(Localizer.Tf("log.tr.monitor", "开始监控进程: {0}", _p.ProcessName));
            LogInfo(Localizer.Tf("log.tr.port", "端口号: {0}", _p.Port));
            LogInfo(Localizer.Tf("log.tr.target", "目标路径: {0}", _p.ExePath));
            LogInfo(Localizer.Tf("log.tr.report", "异常路径: {0}", _p.ReportPath));
            LogInfo(Localizer.Tf("log.tr.interval", "检查间隔: {0}秒", _p.CheckIntervalSeconds));
            LogInfo(Localizer.Tf("log.tr.hangMax", "连续挂起{0}次后将终止进程", _p.MaxHangCount));
            LogInfo(Localizer.Tf("log.tr.workRest", "连续运行{0}分钟后将休息{1}分钟", _p.WorkingMinutes, _p.RestMinutes));

            _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "TeamRenderController" };
            _worker.Start();
        }

        /// <summary>请求停止监控。</summary>
        public void Stop()
        {
            if (!IsRunning) return;
            _stopRequested = true;
            RaiseStatus(RunnerStatus.Stopping);
            LogInfo(Localizer.T("log.tr.stopping", "正在停止监控…"));
        }

        /// <summary>后台监控主循环：每检查间隔处理一轮（过热休息 / 缺失启动 / 健康判定）。</summary>
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
                            LogInfo(Localizer.Tf("log.tr.resting", "连续工作{0}，休息降温", worked.ToString(@"hh\:mm\:ss")));
                            KillProcess(target, false);
                            target = null;
                            if (!SleepCancellable(_p.RestMinutes * 60.0)) break;
                            LogInfo(Localizer.T("log.tr.restEnd", "休息结束"));
                        }

                        if (target == null)
                        {
                            target = StartProcess();
                        }

                        CheckAndMonitorProcess(target);
                    }
                    catch (Exception ex)
                    {
                        LogError(Localizer.Tf("log.tr.errCheck", "检查过程中发生错误: {0}", ex.Message));
                    }

                    if (!SleepCancellable(_p.CheckIntervalSeconds)) break;
                }
            }
            finally
            {
                IsRunning = false;
                RaiseStatus(RunnerStatus.Stopped);
                LogInfo(Localizer.T("log.tr.ended", "监控已停止。"));
            }
        }

        /// <summary>三重健康判定（Responding / 端口占用 / 崩溃报告），命中累加挂起计数、达上限杀并重启，恢复则清零。</summary>
        private void CheckAndMonitorProcess(Process target)
        {
            bool portUsing = ProcessHealth.IsPortUsed(_p.Port);
            if (target == null)
            {
                _processHangCount = new Dictionary<int, int>();
                LogInfo(Localizer.Tf("log.tr.notFound", "目标进程不存在，端口[{0}]：{1}", _p.Port, PortWord(portUsing)));
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
                    string tag = newBugReport ? Localizer.T("log.tagAbnormal", "异常! ") : Localizer.T("log.tagNoResp", "无响应! ");
                    LogWarn(Localizer.Tf("log.tr.abnormal", "进程{0}PID: {1} 端口[{2}]：{3}，挂起次数: {4}/{5}",
                        tag, target.Id, _p.Port, PortWord(portUsing), hangCount, _p.MaxHangCount));

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
                        LogInfo(Localizer.Tf("log.tr.recovered", "进程恢复正常，重置挂起计数. PID: {0} 端口[{1}]：{2}",
                            target.Id, _p.Port, PortWord(portUsing)));
                    }
                    else
                    {
                        TimeSpan worked = DateTime.Now - _lastStartTime;
                        LogInfo(Localizer.Tf("log.tr.normal", "进程[{0}]正常运行，端口[{1}]：{2}，已工作{3}",
                            target.Id, _p.Port, PortWord(portUsing), worked.ToString(@"hh\:mm\:ss")));
                    }
                }
            }
            finally
            {
                target.Dispose();
            }
        }

        /// <summary>端口状态词（占用/断开），本地化。</summary>
        private static string PortWord(bool used)
            => used ? Localizer.T("net.used", "占用") : Localizer.T("net.free", "断开");

        /// <summary>按进程名查找目标客户端进程（与原程序一致，不校验完整路径），找不到返回 null。</summary>
        private Process FindTargetProcess()
        {
            foreach (Process process in Process.GetProcessesByName(_p.ProcessName))
            {
                // 与原程序一致：按进程名命中即返回，不校验完整路径
                return process;
            }
            return null;
        }

        /// <summary>取并自增某进程的挂起计数，返回自增后的值。</summary>
        private int GetAndIncrementHangCount(int processId)
        {
            if (!_processHangCount.ContainsKey(processId))
                _processHangCount[processId] = 0;
            return ++_processHangCount[processId];
        }

        /// <summary>杀目标进程树（taskkill /F /T + Kill 兜底），等待释放后按需重启客户端。</summary>
        private void KillProcess(Process process, bool restart)
        {
            if (process != null)
            {
                int pid = 0;
                try { pid = process.Id; } catch { }
                try
                {
                    LogInfo(Localizer.Tf("log.tr.killing", "正在终止进程树… PID: {0}", pid));
                    if (pid > 0) RunTaskKill($"/F /T /PID {pid}");
                    try { if (!process.HasExited) { process.Kill(); } } catch { }
                    if (process.WaitForExit(5000))
                        LogInfo(Localizer.Tf("log.tr.killedOk", "进程已成功终止. PID: {0}", pid));
                    else
                        LogWarn(Localizer.Tf("log.tr.killTimeout", "进程终止超时，但已发送终止信号. PID: {0}", pid));
                    if (pid > 0) RunTaskKill($"/F /T /PID {pid}");
                }
                catch (Exception ex)
                {
                    LogError(Localizer.Tf("log.tr.killErr", "终止进程失败: {0}", ex.Message));
                }

                SleepCancellable(_p.CheckIntervalSeconds * 2.0);
            }
            if (restart)
                StartProcess();
        }

        /// <summary>调用 taskkill 执行参数（.NET Framework 无进程树 Kill 的兜底手段）。</summary>
        private void RunTaskKill(string arguments)
        {
            try
            {
                var psi = new ProcessStartInfo("taskkill")
                {
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (var pr = Process.Start(psi))
                {
                    pr?.WaitForExit(8000);
                }
            }
            catch (Exception ex)
            {
                LogWarn(Localizer.Tf("log.taskkillFail", "taskkill 执行失败({0}): {1}", arguments, ex.Message));
            }
        }

        /// <summary>按开关清缓存后启动客户端，记录启动时间并等待一段启动缓冲，返回进程（失败为 null）。</summary>
        private Process StartProcess()
        {
            if (_p.ClearCache)
                ClearCache();

            Process process = null;
            try
            {
                process = Process.Start(_p.ExePath);
                _lastStartTime = DateTime.Now;
                LogInfo(Localizer.Tf("log.tr.startOk", "启动进程成功. PID: {0}", process.Id));
            }
            catch (Exception ex)
            {
                LogError(Localizer.Tf("log.tr.startErr", "启动进程失败: {0}", ex.Message));
            }

            SleepCancellable(_p.CheckIntervalSeconds);
            return process;
        }

        /// <summary>清空缓存目录下的文件与子目录（保留目录本身），逐个删除并容错。</summary>
        private void ClearCache()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_p.CachePath) || !Directory.Exists(_p.CachePath))
                    return;

                foreach (string file in Directory.GetFiles(_p.CachePath))
                {
                    try { File.Delete(file); }
                    catch (Exception ex) { LogError(Localizer.Tf("log.tr.cacheFileFail", "删除文件失败: {0} - {1}", file, ex.Message)); }
                }
                foreach (string dir in Directory.GetDirectories(_p.CachePath))
                {
                    try { Directory.Delete(dir, true); }
                    catch (Exception ex) { LogError(Localizer.Tf("log.tr.cacheDirFail", "删除目录失败: {0} - {1}", dir, ex.Message)); }
                }
                LogInfo(Localizer.T("log.tr.cacheCleared", "清空缓存完成"));
            }
            catch (Exception ex)
            {
                LogError(Localizer.Tf("log.tr.cacheErr", "清空缓存异常: {0}", ex.Message));
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

        /// <summary>抛出一条 Info 日志。</summary>
        private void LogInfo(string m) => Log?.Invoke(this, new LogEntry(LogLevel.Info, m));
        /// <summary>抛出一条 Warn 日志。</summary>
        private void LogWarn(string m) => Log?.Invoke(this, new LogEntry(LogLevel.Warn, m));
        /// <summary>抛出一条 Error 日志。</summary>
        private void LogError(string m) => Log?.Invoke(this, new LogEntry(LogLevel.Error, m));
        /// <summary>抛出运行状态变化事件。</summary>
        private void RaiseStatus(RunnerStatus s) => StatusChanged?.Invoke(this, s);
    }
}
