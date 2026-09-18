using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using RenderServerGui.Models;

namespace RenderServerGui.Services
{
    /// <summary>
    /// 单帧逐帧调度控制器，供 Cinema 4D 与 Commandline 两种模式共用（差异只在 ModeProfile 里的 exe 路径与异常记录路径）。
    /// 流程：展开帧模板判已完成→跳过；否则启动 exe -render 帧号→轮询(退出码/产物文件/单帧超时/_BugReport 基线变化/有窗口时 Responding)
    /// →异常杀进程重启本帧→达重试上限按策略停止或跳过→成功后帧间冷却→下一帧。
    /// 注：无窗口进程（Commandline.exe）的 Responding 恒为 false，故仅在进程确有主窗口时才用 Responding 判活。
    /// </summary>
    public class FrameRenderController : IRenderController
    {
        private enum FrameResult { Success, Abnormal, Aborted }

        private Thread _worker;
        private volatile bool _stopRequested;
        private ModeProfile _p;

        public bool IsRunning { get; private set; }

        public event EventHandler<LogEntry> Log;
        public event EventHandler<RunnerStatus> StatusChanged;
        public event EventHandler<FrameProgressInfo> FrameProgressChanged;

        public void Start(ModeProfile profile)
        {
            if (IsRunning) return;

            _p = profile;
            _stopRequested = false;
            IsRunning = true;
            RaiseStatus(RunnerStatus.Running);

            LogInfo($"逐帧渲染启动：{_p.ExePath} -render \"{_p.SceneFile}\" -frame [帧]");
            LogInfo($"帧范围 {_p.StartFrame}–{_p.EndFrame}，输出模板 {_p.OutputTemplate}");
            LogInfo($"帧间冷却 {_p.CooldownSeconds}s，单帧超时 {_p.FrameTimeoutSeconds}s，检查间隔 {_p.FrameCheckIntervalSeconds}s，最大重试 {_p.MaxRetryPerFrame}");

            _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "FrameRenderController" };
            _worker.Start();
        }

        public void Stop()
        {
            if (!IsRunning) return;
            _stopRequested = true;
            RaiseStatus(RunnerStatus.Stopping);
            LogInfo("正在停止…（将结束当前渲染进程）");
        }

        private void WorkerLoop()
        {
            int total = _p.EndFrame - _p.StartFrame + 1;
            if (total <= 0)
            {
                LogError("结束帧小于起始帧，无帧可渲染。");
                Finish(RunnerStatus.Error);
                return;
            }

            int completed = 0;
            bool fatalStop = false;

            try
            {
                for (int frame = _p.StartFrame; frame <= _p.EndFrame; frame++)
                {
                    if (_stopRequested) break;

                    if (FrameScanner.IsFrameComplete(_p.OutputTemplate, frame))
                    {
                        completed++;
                        RaiseProgress(completed, total, frame, "已存在，跳过");
                        LogInfo($"帧 {frame} 输出已存在，跳过。");
                        continue;
                    }

                    bool done = false;   // 本帧成功产出
                    bool skipped = false; // 本帧被放弃但继续
                    int attempt = 0;

                    while (true)
                    {
                        FrameResult r = RenderOneFrame(frame, attempt);
                        if (_stopRequested) break;

                        if (r == FrameResult.Success) { done = true; break; }

                        attempt++;
                        if (attempt > _p.MaxRetryPerFrame)
                        {
                            if (_p.OnFail == OnFailBehaviour.Stop)
                            {
                                LogError($"帧 {frame} 连续失败超过重试上限，停止调度。");
                                fatalStop = true;
                            }
                            else
                            {
                                LogWarn($"帧 {frame} 重试仍失败，按策略跳过继续。");
                                skipped = true;
                            }
                            break;
                        }

                        LogWarn($"帧 {frame} 第 {attempt}/{_p.MaxRetryPerFrame} 次重试。");
                        RaiseProgress(completed, total, frame, $"重试中({attempt})");
                        // 重启前的短暂冷却（当作过热缓冲），可中断
                        if (!SleepCancellable(Math.Min(_p.CooldownSeconds, 15))) break;
                    }

                    if (_stopRequested) break;
                    if (fatalStop) break;

                    if (done || skipped)
                    {
                        completed++;
                        RaiseProgress(completed, total, frame, done ? "完成" : "跳过");
                        if (done && frame < _p.EndFrame && _p.CooldownSeconds > 0)
                        {
                            LogInfo($"帧间冷却 {_p.CooldownSeconds}s…");
                            if (!SleepCancellable(_p.CooldownSeconds)) break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogError($"调度异常终止: {ex.Message}");
                fatalStop = true;
            }

            if (fatalStop) Finish(RunnerStatus.Error);
            else if (_stopRequested) Finish(RunnerStatus.Stopped);
            else
            {
                LogInfo($"全部帧调度完成，成功/跳过 {completed}/{total}。");
                Finish(RunnerStatus.Stopped);
            }
        }

        private FrameResult RenderOneFrame(int frame, int attempt)
        {
            Process process = null;
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = _p.ExePath,
                    Arguments = BuildArguments(frame),
                    UseShellExecute = false
                };
                try { psi.WorkingDirectory = Path.GetDirectoryName(_p.ExePath); } catch { }

                DateTime baseline = ReadReportWriteTime(); // 崩溃基线，避免旧文件误判
                process = Process.Start(psi);
                LogInfo($"启动渲染 帧 {frame}（第 {attempt + 1} 次）PID {process.Id}");

                var start = DateTime.Now;
                int notRespondingStreak = 0;

                while (true)
                {
                    if (_stopRequested)
                    {
                        KillAndWait(process);
                        return FrameResult.Aborted;
                    }

                    if (process.HasExited)
                    {
                        // 退出码为 0，给产物文件最多 ~3s 落盘缓冲
                        if (WaitForFrameFile(frame, 3000))
                        {
                            LogInfo($"帧 {frame} 渲染完成。");
                            return FrameResult.Success;
                        }
                        int code = SafeExitCode(process);
                        if (ReportChangedAfter(baseline))
                            LogWarn($"帧 {frame} 进程退出且检测到崩溃报告更新（退出码 {code}），未产出文件。");
                        else
                            LogWarn($"帧 {frame} 进程退出但未产出文件（退出码 {code}）。");
                        return FrameResult.Abnormal;
                    }

                    double elapsed = (DateTime.Now - start).TotalSeconds;
                    if (elapsed > _p.FrameTimeoutSeconds)
                    {
                        LogWarn($"帧 {frame} 渲染超时 {elapsed:F0}s（上限 {_p.FrameTimeoutSeconds}s），判定挂起。");
                        KillAndWait(process);
                        return FrameResult.Abnormal;
                    }

                    if (ReportChangedAfter(baseline))
                    {
                        LogWarn($"帧 {frame} 检测到异常记录更新，判定崩溃。");
                        KillAndWait(process);
                        return FrameResult.Abnormal;
                    }

                    // 仅对确有主窗口的进程用 Responding 判活（连续多次不响应才处理）
                    try { process.Refresh(); } catch { }
                    if (process.MainWindowHandle != IntPtr.Zero)
                    {
                        if (!ProcessHealth.IsResponding(process))
                        {
                            notRespondingStreak++;
                            if (notRespondingStreak >= 3)
                            {
                                LogWarn($"帧 {frame} 进程持续无响应，判定挂起。");
                                KillAndWait(process);
                                return FrameResult.Abnormal;
                            }
                        }
                        else
                        {
                            notRespondingStreak = 0;
                        }
                    }

                    if (!SleepCancellable(_p.FrameCheckIntervalSeconds))
                    {
                        KillAndWait(process);
                        return FrameResult.Aborted;
                    }
                }
            }
            catch (Exception ex)
            {
                LogError($"启动/渲染 帧 {frame} 出错: {ex.Message}");
                try { if (process != null && !process.HasExited) process.Kill(); } catch { }
                return FrameResult.Abnormal;
            }
            finally
            {
                process?.Dispose();
            }
        }

        // ---------- 辅助 ----------

        private bool WaitForFrameFile(int frame, int maxWaitMs)
        {
            int waited = 0;
            while (waited < maxWaitMs)
            {
                if (FrameScanner.IsFrameComplete(_p.OutputTemplate, frame)) return true;
                Thread.Sleep(300);
                waited += 300;
            }
            return FrameScanner.IsFrameComplete(_p.OutputTemplate, frame);
        }

        private DateTime ReadReportWriteTime()
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(_p.ReportPath) && File.Exists(_p.ReportPath))
                    return File.GetLastWriteTimeUtc(_p.ReportPath);
            }
            catch { }
            return DateTime.MinValue;
        }

        private bool ReportChangedAfter(DateTime baseline)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_p.ReportPath) || !File.Exists(_p.ReportPath))
                    return false;
                return File.GetLastWriteTimeUtc(_p.ReportPath) > baseline;
            }
            catch
            {
                return false;
            }
        }

        private static int SafeExitCode(Process p)
        {
            try { return p.ExitCode; } catch { return int.MinValue; }
        }

        /// <summary>构造渲染参数： -render "工程.c4d" -frame 帧号。</summary>
        private string BuildArguments(int frame)
        {
            string scene = (_p.SceneFile ?? string.Empty).Trim();
            return scene.Length > 0
                ? $"-render \"{scene}\" -frame {frame}"
                : $"-render -frame {frame}";
        }

        private void KillAndWait(Process process)
        {
            try
            {
                if (process != null && !process.HasExited)
                {
                    process.Kill();
                    process.WaitForExit(5000);
                }
            }
            catch (Exception ex)
            {
                LogError($"终止渲染进程失败: {ex.Message}");
            }
        }

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

        private void Finish(RunnerStatus status)
        {
            IsRunning = false;
            RaiseStatus(status);
            if (status == RunnerStatus.Error) LogError("调度已因错误停止。");
            else LogInfo("调度已停止。");
        }

        private void RaiseProgress(int completed, int total, int frame, string stage)
            => FrameProgressChanged?.Invoke(this,
                new FrameProgressInfo { Completed = completed, Total = total, CurrentFrame = frame, Stage = stage });

        private void LogInfo(string m) => Log?.Invoke(this, new LogEntry(LogLevel.Info, m));
        private void LogWarn(string m) => Log?.Invoke(this, new LogEntry(LogLevel.Warn, m));
        private void LogError(string m) => Log?.Invoke(this, new LogEntry(LogLevel.Error, m));
        private void RaiseStatus(RunnerStatus s) => StatusChanged?.Invoke(this, s);
    }
}
