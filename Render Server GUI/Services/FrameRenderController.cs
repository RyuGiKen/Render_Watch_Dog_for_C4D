using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using RenderServerGui.Models;

namespace RenderServerGui.Services
{
    /// <summary>
    /// 单帧逐帧调度控制器，供 Cinema 4D 与 Commandline 两种模式共用（差异只在 ModeProfile 里的 exe 路径与异常记录路径）。
    /// 流程：展开帧模板判"文件存在且非空"→跳过；否则启动 exe -render "工程" -frame 帧号→轮询
    ///   · 进程退出：产物非空即成功，否则异常（含 _BugReport 基线变化=崩溃）
    ///   · 进程仍在但超过单帧超时：若产物已生成→判"渲完未退出"→杀进程树算成功；否则判挂起→杀进程树重试
    /// 异常后用 taskkill 杀整棵进程树，重试；达最大重试按策略跳过/停止；成功后帧间冷却→下一帧。
    /// 注：无窗口进程（Commandline.exe）Responding 恒 false，仅在进程确有主窗口时才用 Responding 判活。
    /// </summary>
    public class FrameRenderController : IRenderController
    {
        private enum FrameResult { Success, Abnormal, Aborted }

        private const int KillSettleSeconds = 15;   // 杀进程后等待，令 GPU 驱动/文件句柄充分释放

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

                        LogWarn($"帧 {frame} 第 {attempt}/{_p.MaxRetryPerFrame} 次重试。");
                        RaiseProgress(completed, total, frame, $"重试中({attempt})");
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
                try
                {
                    string outDir = Path.GetDirectoryName(FrameScanner.FramePath(_p.OutputTemplate, frame));
                    if (!string.IsNullOrEmpty(outDir)) Directory.CreateDirectory(outDir);
                }
                catch { }

                DateTime baseline = ReadReportWriteTime(); // 崩溃基线，避免旧报告误判
                process = Process.Start(psi);
                LogInfo($"启动渲染 帧 {frame}（第 {attempt + 1} 次）PID {process.Id}  参数: {psi.Arguments}");

                var start = DateTime.Now;
                int notRespondingStreak = 0;

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
                        string why = ReportChangedAfter(baseline) ? "且检测到崩溃报告更新" : "但产物缺失/为空";
                        LogWarn($"帧 {frame} 进程退出（码 {code}），{why}。");
                        return FrameResult.Abnormal;
                    }

                    double elapsed = (DateTime.Now - start).TotalSeconds;

                    // 项③：超过单帧超时——若产物已生成，判"渲完未退出"→杀树算成功；否则判挂起→杀树重试
                    if (elapsed > _p.FrameTimeoutSeconds)
                    {
                        if (FrameScanner.IsFrameRendered(_p.OutputTemplate, frame))
                        {
                            LogWarn($"帧 {frame} 超时 {elapsed:F0}s 但产物已生成，判为渲完未退出，结束进程并计成功。");
                            KillTree(process);
                            return FrameResult.Success;
                        }
                        LogWarn($"帧 {frame} 渲染超时 {elapsed:F0}s（上限 {_p.FrameTimeoutSeconds}s）且无产物，判定挂起。");
                        KillTree(process);
                        return FrameResult.Abnormal;
                    }

                    // 崩溃报告更新（进程仍活）→ 判异常
                    if (ReportChangedAfter(baseline))
                    {
                        LogWarn($"帧 {frame} 检测到异常记录更新，判定崩溃。");
                        KillTree(process);
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
                                KillTree(process);
                                return FrameResult.Abnormal;
                            }
                        }
                        else notRespondingStreak = 0;
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

            SleepCancellable(KillSettleSeconds);
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
                if (string.IsNullOrWhiteSpace(_p.ReportPath) || !File.Exists(_p.ReportPath)) return false;
                return File.GetLastWriteTimeUtc(_p.ReportPath) > baseline;
            }
            catch { return false; }
        }

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
