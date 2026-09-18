using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using RenderServerGui.Models;

namespace RenderServerGui.Services
{
    /// <summary>
    /// 单帧逐帧调度控制器，Cinema 4D 与 Commandline 严格共用同一段代码（差异只在 exe 与崩溃记录路径）。
    ///
    /// 分层：
    ///  · 外层 WorkerLoop：按帧推进；"启动前空载冷却 → 启动进程 → 观察 → (成功/尝试失败) → 帧失败计数 → 重启/跳过/停止 → 下一帧"。
    ///  · 内层 RunAttempt：只管"这一个进程启动后"的轮询判断，返回 成功 / 尝试失败 / 启动错误 / 停止。
    ///
    /// 判断（每 检查间隔 轮询，自上而下）：
    ///  1) 产物存在且非空且距最后写入已过"冷却"→ 判成功（进程若还活着则杀）。
    ///  2) 否则计"异常计数"（沿用 Team Render 去抖）：崩溃报告在时窗内 / 单帧超时无产物 / 无响应且超时 / 已退出且无产物 → +1；
    ///     纯无响应未超时、或运行正常 → 清零；进程已退出但产物仍在写(未落定) → 挂起等待，不计异常也不误判成功。
    ///     异常计数达"最大异常次数" → 判本次尝试失败。
    ///  3) 外层：尝试失败 → 帧失败计数+1；达"帧最大失败次数" → 按策略跳过/停止；否则重启同一帧。
    ///
    /// 时间参数：启动后首轮以"帧间冷却"作加载宽限(期间不检测)，之后每轮=检查间隔；产物落定需距最后写入≥冷却秒；单帧超时独立。
    /// 无窗口进程（Commandline.exe）不检测 Responding；挂起统一由单帧超时收口。
    /// </summary>
    public class FrameRenderController : IRenderController
    {
        private enum AttemptOutcome { Success, AttemptFailed, LaunchError, Abort }

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
            LogInfo($"检查间隔 {_p.FrameCheckIntervalSeconds}s，单帧超时 {_p.FrameTimeoutSeconds}s，帧间冷却 {_p.CooldownSeconds}s，最大异常 {_p.MaxAbnormalCount}，帧最大失败 {_p.MaxFrameFailCount}，失败{(_p.OnFail == OnFailBehaviour.Skip ? "跳过" : "停止")}");

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

        // ================= 外层：逐帧推进 =================

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
            RunnerStatus final = RunnerStatus.Stopped;

            try
            {
                for (int frame = _p.StartFrame; frame <= _p.EndFrame; frame++)
                {
                    if (_stopRequested) break;

                    // 断点续渲：已落定的成品直接跳过（无渲染发生，不额外冷却）
                    if (FrameScanner.IsFrameSettled(_p.OutputTemplate, frame, _p.CooldownSeconds))
                    {
                        completed++;
                        RaiseProgress(completed, total, frame, "已存在，跳过");
                        LogInfo($"帧 {frame} 输出已存在且非空，跳过。");
                        continue;
                    }

                    int frameFail = 0;
                    bool frameDone = false;    // 本帧最终成功
                    bool frameSkipped = false; // 本帧被放弃

                    while (!frameDone && !frameSkipped)
                    {
                        if (_stopRequested) break;

                        AttemptOutcome outcome = RunAttempt(frame, frameFail);

                        if (outcome == AttemptOutcome.Abort) { _stopRequested = true; break; }
                        if (outcome == AttemptOutcome.Success) { frameDone = true; break; }
                        if (outcome == AttemptOutcome.LaunchError)
                        {
                            LogError($"帧 {frame} 进程无法启动（路径/授权/参数问题），停止整场调度。");
                            final = RunnerStatus.Error;
                            _stopRequested = true;
                            break;
                        }

                        // AttemptFailed
                        frameFail++;
                        if (frameFail >= _p.MaxFrameFailCount)
                        {
                            if (_p.OnFail == OnFailBehaviour.Stop)
                            {
                                LogError($"帧 {frame} 连续 {frameFail} 次尝试失败达上限，停止调度。");
                                final = RunnerStatus.Error;
                                _stopRequested = true;
                                break;
                            }
                            LogWarn($"帧 {frame} 连续 {frameFail} 次尝试失败达上限，按策略跳过该帧。");
                            frameSkipped = true;
                        }
                        else
                        {
                            LogWarn($"帧 {frame} 第 {frameFail}/{_p.MaxFrameFailCount} 次尝试失败，冷却后重启本帧。");
                        }
                    }

                    if (_stopRequested && !frameDone && !frameSkipped) break; // 停止/致命错误中断

                    if (frameDone || frameSkipped)
                    {
                        completed++;
                        RaiseProgress(completed, total, frame, frameDone ? "完成" : "跳过");
                    }
                }
            }
            catch (Exception ex)
            {
                LogError($"调度异常终止: {ex.Message}");
                final = RunnerStatus.Error;
            }

            if (final != RunnerStatus.Error)
            {
                LogInfo(_stopRequested
                    ? $"调度被停止，已完成/跳过 {completed}/{total}。"
                    : $"全部帧调度完成，成功/跳过 {completed}/{total}。");
            }
            Finish(final);
        }

        // ================= 内层：单个进程启动后的观察 =================

        private AttemptOutcome RunAttempt(int frame, int priorFails)
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

                process = Process.Start(psi);
                LogInfo($"启动渲染 帧 {frame}（累计失败 {priorFails} 次）PID {process.Id}  参数: {psi.Arguments}");

                var start = DateTime.Now;
                var crashWindow = TimeSpan.FromSeconds(_p.FrameCheckIntervalSeconds * (_p.MaxAbnormalCount + 1.5)); // 与 Team Render 同款时窗
                int abnormal = 0;
                bool loggedNoResp = false;
                bool firstTick = true; // 启动后首轮用"冷却"跳过加载期，期间不检测

                while (true)
                {
                    // 首轮=冷却宽限(加载期不检测)，之后每轮=检查间隔；等待期间收到停止即中止
                    if (!SleepIdle(firstTick ? _p.CooldownSeconds : _p.FrameCheckIntervalSeconds))
                    {
                        KillTree(process);
                        return AttemptOutcome.Abort;
                    }
                    if (firstTick)
                    {
                        firstTick = false;
                        if (_stopRequested) { KillTree(process); return AttemptOutcome.Abort; }
                        continue; // 加载期跳过，进入下一轮才正式检测
                    }
                    if (_stopRequested)
                    {
                        KillTree(process);
                        return AttemptOutcome.Abort;
                    }

                    bool exited;
                    try { exited = process.HasExited; } catch { exited = true; }
                    bool settled = FrameScanner.IsFrameSettled(_p.OutputTemplate, frame, _p.CooldownSeconds);

                    // 1) 产物已落定 → 成功（进程若活着则收口杀掉）
                    if (settled)
                    {
                        if (!exited)
                        {
                            LogInfo($"帧 {frame} 产物已落定但进程未退出，结束进程并计成功。");
                            KillTree(process);
                        }
                        else
                        {
                            LogInfo($"帧 {frame} 渲染完成（退出码 {SafeExitCode(process)}）。");
                        }
                        return AttemptOutcome.Success;
                    }

                    double elapsed = (DateTime.Now - start).TotalSeconds;
                    bool exists = FrameScanner.IsFrameRendered(_p.OutputTemplate, frame);
                    bool crash = ProcessHealth.IsBugReportRecent(_p.ReportPath, crashWindow);
                    bool timedOut = elapsed > _p.FrameTimeoutSeconds;
                    bool unresponsive = false;
                    if (!exited)
                    {
                        try { process.Refresh(); } catch { }
                        unresponsive = process.MainWindowHandle != IntPtr.Zero && !ProcessHealth.IsResponding(process);
                    }

                    // 2) 去抖计数
                    if (exited)
                    {
                        if (exists)
                        {
                            abnormal = 0; // 已退出但产物仍在写、未落定：挂起等待，不误判也不计异常
                        }
                        else
                        {
                            abnormal++;
                            LogWarn($"帧 {frame} 进程已退出且无产物（闪退/崩溃），异常计数 {abnormal}/{_p.MaxAbnormalCount}。");
                        }
                    }
                    else if (crash || (unresponsive && timedOut))
                    {
                        abnormal++;
                        string tag = crash ? "崩溃报告在时窗内" : "无响应且超时";
                        LogWarn($"帧 {frame} {tag}，异常计数 {abnormal}/{_p.MaxAbnormalCount}。");
                    }
                    else if (unresponsive)
                    {
                        if (abnormal > 0) { abnormal = 0; LogInfo($"帧 {frame} 无响应（高负载常态），视为正常，异常计数清零。"); }
                        else if (!loggedNoResp)
                        {
                            LogInfo($"帧 {frame} 当前无响应（高负载常态），不单独处理，持续至超时再判（已运行 {elapsed:F0}s / 上限 {_p.FrameTimeoutSeconds}s）。");
                            loggedNoResp = true;
                        }
                    }
                    else if (timedOut)
                    {
                        abnormal++;
                        LogWarn($"帧 {frame} 超过单帧超时 {_p.FrameTimeoutSeconds}s 仍无产物，异常计数 {abnormal}/{_p.MaxAbnormalCount}。");
                    }
                    else
                    {
                        if (abnormal > 0) { abnormal = 0; LogInfo($"帧 {frame} 运行正常，异常计数清零。"); }
                        loggedNoResp = false;
                    }

                    // 3) 达上限 → 本次尝试失败
                    if (abnormal >= _p.MaxAbnormalCount)
                    {
                        LogWarn($"帧 {frame} 连续异常达 {_p.MaxAbnormalCount} 次，结束进程，判本次尝试失败。");
                        KillTree(process);
                        return AttemptOutcome.AttemptFailed;
                    }
                }
            }
            catch (Exception ex)
            {
                LogError($"启动/监控 帧 {frame} 出错: {ex.Message}");
                try { if (process != null && !process.HasExited) KillTree(process); } catch { }
                return AttemptOutcome.LaunchError;
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

        /// <summary>杀整棵进程树。杀后不在此等待——由外层"启动前空载冷却"承担句柄释放与降温。</summary>
        private void KillTree(Process proc)
        {
            int pid = 0;
            try { if (proc != null) pid = proc.Id; } catch { }

            if (pid > 0) RunTaskKill($"/F /T /PID {pid}");
            try { if (proc != null && !proc.HasExited) { proc.Kill(); proc.WaitForExit(5000); } } catch { }
            if (pid > 0) RunTaskKill($"/F /T /PID {pid}");
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
                using (var pr = Process.Start(psi)) { pr?.WaitForExit(8000); }
            }
            catch (Exception ex) { LogWarn($"taskkill 执行失败({arguments}): {ex.Message}"); }
        }

        // ---------- 辅助 ----------

        private static int SafeExitCode(Process p)
        {
            try { return p.ExitCode; } catch { return int.MinValue; }
        }

        /// <summary>可中断的空载等待；返回 false 表示期间收到停止请求。</summary>
        private bool SleepIdle(double seconds)
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
