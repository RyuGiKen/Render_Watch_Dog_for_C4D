using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using RenderServerGui.Models;

namespace RenderServerGui.Services
{
    /// <summary>
    /// 单帧逐帧调度控制器，Cinema 4D 与 Commandline 严格共用同一套逻辑，仅进程/异常记录路径不同。
    /// 时间参数：轮询间隔、杀进程后释放等待、重试前缓冲都用同一个「检查间隔」值；帧间冷却与单帧超时各自独立。
    /// 崩溃报告沿用 Team Render 看门狗逻辑：在时间窗内被更新即计一次异常，连续累计达上限才杀进程，停止更新则清零（不逐帧另立基线）。
    /// 无响应/挂起统一交给「单帧超时」闸门；无窗口进程（Commandline.exe）不检测 Responding。
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
            LogInfo($"帧间冷却 {_p.CooldownSeconds}s，单帧超时 {_p.FrameTimeoutSeconds}s，检查间隔 {_p.FrameCheckIntervalSeconds}s，最大重试 {_p.MaxRetryPerFrame}，失败{(_p.OnFail == OnFailBehaviour.Skip ? "跳过" : "停止")}");

            _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "FrameRenderController" };
            _worker.Start();
        }

        public void Stop()
        {
            if (!IsRunning) return;
            _stopRequested = true;
            RaiseStatus(RunnerStatus.Stopping);
            LogInfo("正在停止…（将结束当前渲染进程树）");
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

                    if (FrameScanner.IsFrameRendered(_p.OutputTemplate, frame))
                    {
                        completed++;
                        RaiseProgress(completed, total, frame, "已存在，跳过");
                        LogInfo($"帧 {frame} 输出已存在且非空，跳过。");
                        continue;
                    }

                    bool done = false;
                    bool skipped = false;
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
                                LogWarn($"帧 {frame} 连续失败超过重试上限，跳过继续。");
                                skipped = true;
                            }
                            break;
                        }

                        LogWarn($"帧 {frame} 第 {attempt}/{_p.MaxRetryPerFrame} 次重试，等待 {_p.FrameCheckIntervalSeconds}s。");
                        RaiseProgress(completed, total, frame, $"重试中({attempt})");
                        if (!SleepCancellable(_p.FrameCheckIntervalSeconds)) break;
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
                try
                {
                    string outDir = Path.GetDirectoryName(FrameScanner.FramePath(_p.OutputTemplate, frame));
                    if (!string.IsNullOrEmpty(outDir)) Directory.CreateDirectory(outDir);
                }
                catch { }

                var crashWindow = TimeSpan.FromSeconds(_p.FrameCheckIntervalSeconds * (_p.MaxRetryPerFrame + 1.5)); // 与 Team Render 同款判定时窗
                process = Process.Start(psi);
                LogInfo($"启动渲染 帧 {frame}（第 {attempt + 1} 次）PID {process.Id}  参数: {psi.Arguments}");

                var start = DateTime.Now;
                int crashStreak = 0;         // 崩溃报告连续异常计数
                bool loggedNoResp = false;   // 无响应提示只记一次，避免刷屏

                while (true)
                {
                    if (_stopRequested)
                    {
                        KillTree(process);
                        return FrameResult.Aborted;
                    }

                    if (process.HasExited)
                    {
                        if (FrameScanner.IsFrameRendered(_p.OutputTemplate, frame))
                        {
                            LogInfo($"帧 {frame} 渲染完成（退出码 {SafeExitCode(process)}，产物存在）。");
                            return FrameResult.Success;
                        }
                        int code = SafeExitCode(process);
                        string why = ProcessHealth.IsBugReportRecent(_p.ReportPath, crashWindow) ? "且崩溃报告在时窗内更新" : "但产物缺失/为空";
                        LogWarn($"帧 {frame} 进程已退出（码 {code}），{why}，判为失败并重试。");
                        return FrameResult.Abnormal; // 闪退/报错：进程已终结，直接算一次失败
                    }

                    double elapsed = (DateTime.Now - start).TotalSeconds;

                    try { process.Refresh(); } catch { }
                    bool notResponding = process.MainWindowHandle != IntPtr.Zero
                                         && !ProcessHealth.IsResponding(process);

                    // 无响应/挂起统一由"单帧超时"这一闸门处理：未超时绝不因无响应杀进程
                    if (elapsed > _p.FrameTimeoutSeconds)
                    {
                        if (FrameScanner.IsFrameRendered(_p.OutputTemplate, frame))
                        {
                            LogWarn($"帧 {frame} 超时 {elapsed:F0}s 但产物已生成，判为渲完未退出，结束进程并计成功。");
                            KillTree(process);
                            return FrameResult.Success;
                        }
                        string cause = notResponding ? "持续无响应" : "长时间未完成";
                        LogWarn($"帧 {frame} {cause} 直至超过单帧超时 {_p.FrameTimeoutSeconds}s 且无产物，判定挂起，杀进程重试。");
                        KillTree(process);
                        return FrameResult.Abnormal;
                    }

                    // 无响应仅作提示，不计数、不杀（高负载常态）
                    if (notResponding && !loggedNoResp)
                    {
                        LogInfo($"帧 {frame} 当前无响应（高负载常见），不单独处理，持续至超时再判（已运行 {elapsed:F0}s / 上限 {_p.FrameTimeoutSeconds}s）。");
                        loggedNoResp = true;
                    }
                    else if (!notResponding && loggedNoResp)
                    {
                        LogInfo($"帧 {frame} 进程恢复响应。");
                        loggedNoResp = false;
                    }

                    // 崩溃报告：沿用 Team Render 时窗判定，命中即计数，达上限才杀；停止命中则清零
                    if (ProcessHealth.IsBugReportRecent(_p.ReportPath, crashWindow))
                    {
                        crashStreak++;
                        LogWarn($"帧 {frame} 崩溃报告在时窗内，连续异常 {crashStreak}/{_p.MaxRetryPerFrame}。");
                        if (crashStreak >= _p.MaxRetryPerFrame)
                        {
                            LogWarn($"帧 {frame} 连续 {crashStreak} 次异常达上限，判定崩溃，杀进程重试。");
                            KillTree(process);
                            return FrameResult.Abnormal;
                        }
                    }
                    else if (crashStreak > 0)
                    {
                        crashStreak = 0;
                        LogInfo($"帧 {frame} 崩溃报告已不在时窗内，异常计数清零。");
                    }

                    if (!SleepCancellable(_p.FrameCheckIntervalSeconds))
                    {
                        KillTree(process);
                        return FrameResult.Aborted;
                    }
                }
            }
            catch (Exception ex)
            {
                LogError($"启动/渲染 帧 {frame} 出错: {ex.Message}");
                KillTree(process);
                return FrameResult.Abnormal;
            }
            finally
            {
                try { process?.Dispose(); } catch { }
            }
        }

        // ---------- 进程与参数 ----------

        private string BuildArguments(int frame)
        {
            string scene = (_p.SceneFile ?? string.Empty).Trim();
            return scene.Length > 0
                ? $"-render \"{scene}\" -frame {frame}"
                : $"-render -frame {frame}";
        }

        /// <summary>杀掉整棵进程树。.NET Framework 无 Kill(entireProcessTree)，用 taskkill /F /T /PID 兜底并补杀，随后固定较长冷却等待句柄释放。</summary>
        private void KillTree(Process proc)
        {
            int pid = 0;
            try { if (proc != null) pid = proc.Id; } catch { }

            if (pid > 0) RunTaskKill($"/F /T /PID {pid}");
            try { if (proc != null && !proc.HasExited) { proc.Kill(); proc.WaitForExit(5000); } } catch { }
            if (pid > 0) RunTaskKill($"/F /T /PID {pid}"); // 补杀 taskkill 之后才拉起的子进程

            SleepCancellable(_p.FrameCheckIntervalSeconds);   // 杀后释放等待，与检查间隔同值
        }

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
                LogWarn($"taskkill 执行失败({arguments}): {ex.Message}");
            }
        }

        // ---------- 辅助 ----------

        private static int SafeExitCode(Process p)
        {
            try { return p.ExitCode; } catch { return int.MinValue; }
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
            LogInfo(status == RunnerStatus.Error ? "调度已因错误停止。" : "调度已停止。");
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
