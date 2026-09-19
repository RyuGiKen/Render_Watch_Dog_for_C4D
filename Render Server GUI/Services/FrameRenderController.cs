using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using RenderServerGui.Models;

namespace RenderServerGui.Services
{
    /// <summary>首次启动前发现同名渲染进程已在运行时的处理选择。</summary>
    public enum PreexistingChoice
    {
        /// <summary>停止本次调度，不动已有进程。</summary>
        Abort,
        /// <summary>杀掉残留进程树后再开始新渲染。</summary>
        KillAndStart
    }

    /// <summary>
    /// 单帧/分块调度控制器，Cinema 4D 与 Commandline 严格共用同一段代码（差异只在 exe 与崩溃记录路径）。
    ///
    /// 分块模型（N=MaxChunkLength，≥1，执行时夹到任务范围；N=1 即单帧）：
    ///  · 外层 WorkerLoop：单调游标 cursor 只前进。每轮先跳过"已落定"的帧，取块 [a,b]=[cursor, min(cursor+N-1,末)]，
    ///    启动进程渲这一段；成功→cursor=b+1；崩了→重算块内第一个未落定帧 newFirst：
    ///      newFirst>a 视为有进展，cursor=newFirst（重置焦点失败计数）；
    ///      newFirst==a 原地卡起点，焦点失败+1，达帧最大失败次数则跳过起点帧 cursor=a+1；
    ///    连续多块都"原地卡起点即失败"→ 熔断停止（防整片被静默跳过）。跨重启不留痕，由用户手填缺帧范围续渲。
    ///  · 内层 RunBlock：只观察这一个进程。启动后首轮以"帧间冷却"跳过加载期(不检测)，之后每"检查间隔"轮询：
    ///    块内全部落定→成功；已退出且有部分完成→立即判尝试失败；已退出无产物/崩溃报告在时窗/无进展超时→异常计数+1，
    ///    达"最大异常次数"→杀进程判尝试失败；纯无响应(未超时)只提示一次、异常清零、不杀（无窗口进程恒不触发）。
    ///
    /// 时间参数：轮询=检查间隔；启动后首轮以"帧间冷却"作加载宽限(此间不检测)，产物落定亦需距写入≥冷却秒；无进展超时/崩溃时窗独立；挂起由"无进展超时"收口。
    /// 全部帧号/块端点均夹到 [StartFrame,EndFrame]。
    /// </summary>
    public class FrameRenderController : IRenderController
    {
        private enum BlockOutcome { Success, AttemptFailed, Abort }

        private const int CircuitBreakerBlocks = 3; // 连续多少块"原地卡起点即失败"判为全局问题

        private Thread _worker;
        private volatile bool _stopRequested;
        private ModeProfile _p;

        public bool IsRunning { get; private set; }

        public event EventHandler<LogEntry> Log;
        public event EventHandler<RunnerStatus> StatusChanged;
        public event EventHandler<FrameProgressInfo> FrameProgressChanged;

        /// <summary>首次启动前若发现同名渲染进程已在运行，回调 UI 询问如何处理（返回决策）。为 null 时按“停止队列”保守处理。</summary>
        public Func<int, PreexistingChoice> PreexistingHandler { get; set; }

        public void Start(ModeProfile profile)
        {
            if (IsRunning) return;

            _p = profile;
            _stopRequested = false;
            IsRunning = true;
            RaiseStatus(RunnerStatus.Running);

            int total = Math.Max(0, _p.EndFrame - _p.StartFrame + 1);
            LogInfo($"分块渲染启动：{_p.ExePath}  -render \"{_p.SceneFile}\"");
            LogInfo($"帧范围 {_p.StartFrame}–{_p.EndFrame}（共 {total} 帧），模板 {_p.OutputTemplate}，最大分块 {_p.MaxChunkLength}");
            LogInfo($"检查间隔 {_p.FrameCheckIntervalSeconds}s，无进展超时 {_p.FrameTimeoutSeconds}s，帧间冷却 {_p.CooldownSeconds}s，最大异常 {_p.MaxAbnormalCount}，帧最大失败 {_p.MaxFrameFailCount}，失败{(_p.OnFail == OnFailBehaviour.Skip ? "跳过" : "停止")}");

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

        // ================= 外层：游标 + 分块推进 =================

        private void WorkerLoop()
        {
            int start = _p.StartFrame, end = _p.EndFrame;
            int total = end - start + 1;
            if (total <= 0)
            {
                LogError("结束帧小于起始帧，无帧可渲染。");
                Finish(RunnerStatus.Error);
                return;
            }

            // 首次启动前：若已有同名渲染进程在跑，弹窗让用户决定“停止队列”或“杀残留再启动”
            if (!HandlePreexisting())
            {
                LogWarn("用户选择停止，未启动渲染。");
                Finish(RunnerStatus.Stopped);
                return;
            }

            int N = Math.Max(1, _p.MaxChunkLength);
            int cursor = start;
            int consecStall = 0;
            int accounted = 0;
            RunnerStatus final = RunnerStatus.Stopped;

            try
            {
                while (cursor <= end && !_stopRequested)
                {
                    // 跳过已落定的帧（续渲/重叠），不额外冷却
                    while (cursor <= end && IsSettled(cursor)) cursor++;
                    if (cursor > end) break;

                    int fa = cursor;
                    int fb = Math.Min(fa + N - 1, end);
                    int focusFail = 0;

                    while (true)
                    {
                        if (_stopRequested) break;

                        BlockOutcome oc = RunBlock(fa, fb);
                        if (oc == BlockOutcome.Abort) { _stopRequested = true; break; }

                        if (oc == BlockOutcome.Success)
                        {
                            consecStall = 0;
                            cursor = fb + 1;
                            break;
                        }

                        // 尝试失败：看块内有无前进
                        int newFirst = FirstUnsettled(fa, fb);
                        if (newFirst < 0) // 其实全落定
                        {
                            consecStall = 0;
                            cursor = fb + 1;
                            break;
                        }
                        if (newFirst > fa) // 有进展：游标推到首个未落定，重取块
                        {
                            consecStall = 0;
                            cursor = newFirst;
                            break;
                        }

                        // 原地卡起点帧
                        focusFail++;
                        if (focusFail >= _p.MaxFrameFailCount)
                        {
                            if (_p.OnFail == OnFailBehaviour.Stop)
                            {
                                LogWarn($"帧 {fa} 连续 {focusFail} 次尝试无进展，按策略停止调度。");
                                cursor = end + 1; // 退出外层 while
                                break;
                            }
                            consecStall++;
                            LogWarn($"帧 {fa} 连续 {focusFail} 次尝试无进展，跳过该帧。");
                            cursor = fa + 1;
                            break;
                        }
                        LogWarn($"帧 {fa} 第 {focusFail}/{_p.MaxFrameFailCount} 次尝试无进展，重启本块。");
                    }

                    accounted = Clamp(cursor - start, 0, total);
                    RaiseProgress(accounted, total, Math.Min(cursor, end + 1), "");

                    if (_stopRequested) break;
                    if (consecStall >= CircuitBreakerBlocks)
                    {
                        LogError($"连续 {consecStall} 块都在起点即失败、毫无进展，疑似资产缺失/未烘缓存/授权等全局问题，停止调度。");
                        final = RunnerStatus.Error;
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                LogError($"调度异常终止: {ex.Message}");
                final = RunnerStatus.Error;
            }

            if (final != RunnerStatus.Error)
                LogInfo($"调度结束：已推进 {Clamp(cursor - start, 0, total)}/{total} 帧（缺帧可重开工具填范围续渲）。");
            Finish(final);
        }

        // ================= 内层：一个进程渲 [a,b] 的观察 =================

        private BlockOutcome RunBlock(int a, int b)
        {
            Process process = null;
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = _p.ExePath,
                    Arguments = BuildArguments(a, b),
                    UseShellExecute = false
                };
                try { psi.WorkingDirectory = Path.GetDirectoryName(_p.ExePath); } catch { }
                try
                {
                    string outDir = Path.GetDirectoryName(FrameScanner.FramePath(_p.OutputTemplate, a));
                    if (!string.IsNullOrEmpty(outDir)) Directory.CreateDirectory(outDir);
                }
                catch { }

                process = Process.Start(psi);
                LogInfo($"启动渲染 块[{a},{b}] PID {process.Id}  参数: {psi.Arguments}");

                var startT = DateTime.Now;
                var crashWindow = TimeSpan.FromSeconds(_p.FrameCheckIntervalSeconds * (_p.MaxAbnormalCount + 1.5)); // 与 Team Render 同款时窗

                // 启动后首轮：以帧间冷却跳过加载期，期间不检测
                if (!SleepIdle(_p.CooldownSeconds))
                {
                    KillTree(process);
                    return BlockOutcome.Abort;
                }
                if (_stopRequested) { KillTree(process); return BlockOutcome.Abort; }

                int abnormal = 0;
                int lastSettled = 0;
                var lastProgress = startT;
                bool loggedNoResp = false;
                int blockLen = b - a + 1;

                while (true)
                {
                    if (_stopRequested) { KillTree(process); return BlockOutcome.Abort; }

                    int settled = CountSettled(a, b);
                    bool allSettled = settled >= blockLen;
                    if (allSettled)
                    {
                        bool alive = !SafeExited(process);
                        if (alive) LogInfo($"块[{a},{b}] 全部落定但进程未退出，结束进程并计成功。");
                        else LogInfo($"块[{a},{b}] 渲染完成（退出码 {SafeExitCode(process)}）。");
                        if (alive) KillTree(process);
                        return BlockOutcome.Success;
                    }

                    if (settled > lastSettled) { lastSettled = settled; lastProgress = DateTime.Now; }

                    bool exited = SafeExited(process);
                    bool noProgress = (DateTime.Now - lastProgress).TotalSeconds > _p.FrameTimeoutSeconds;
                    bool crash = ProcessHealth.IsBugReportRecent(_p.ReportPath, crashWindow);

                    bool unresponsive = false;
                    if (!exited)
                    {
                        try { process.Refresh(); } catch { }
                        unresponsive = process.MainWindowHandle != IntPtr.Zero && !ProcessHealth.IsResponding(process);
                    }

                    // 已退出且已有部分落定：终态且能续，立即判尝试失败（让外层前进到缺口）
                    if (exited && settled > 0 && !allSettled)
                    {
                        LogWarn($"块[{a},{b}] 进程退出、已完成 {settled}/{blockLen} 帧，判本次尝试失败，外层从缺口续渲。");
                        return BlockOutcome.AttemptFailed;
                    }

                    string badReason = null;
                    if (exited && settled == 0) badReason = "已退出无产物(秒退)";
                    else if (crash) badReason = "崩溃报告在时窗内";
                    else if (noProgress) badReason = $"{_p.FrameTimeoutSeconds}s 无新帧落定";

                    if (badReason != null)
                    {
                        abnormal++;
                        LogWarn($"块[{a},{b}] {badReason}，异常计数 {abnormal}/{_p.MaxAbnormalCount}。");
                        if (abnormal >= _p.MaxAbnormalCount)
                        {
                            LogWarn($"块[{a},{b}] 连续异常达 {_p.MaxAbnormalCount} 次，结束进程，判本次尝试失败。");
                            KillTree(process);
                            return BlockOutcome.AttemptFailed;
                        }
                    }
                    else
                    {
                        if (abnormal > 0) { abnormal = 0; LogInfo($"块[{a},{b}] 恢复正常，异常计数清零。"); }
                        if (unresponsive)
                        {
                            if (!loggedNoResp)
                            {
                                LogInfo($"块[{a},{b}] 当前无响应（高负载常态），不单独处理，持续至无进展超时再判（本块已运行 {(DateTime.Now - startT).TotalSeconds:F0}s）。");
                                loggedNoResp = true;
                            }
                        }
                        else loggedNoResp = false;
                    }

                    if (!SleepIdle(_p.FrameCheckIntervalSeconds)) { KillTree(process); return BlockOutcome.Abort; }
                }
            }
            catch (Exception ex)
            {
                LogError($"启动/监控 块[{a},{b}] 出错: {ex.Message}");
                try { if (process != null && !process.HasExited) KillTree(process); } catch { }
                return BlockOutcome.Abort; // 起不来视为终止（多为配置/路径问题），停止整场
            }
            finally
            {
                try { process?.Dispose(); } catch { }
            }
        }

        // ---------- 帧扫描辅助（全部夹在 [Start,End] 内） ----------

        private bool IsSettled(int frame)
            => FrameScanner.IsFrameSettled(_p.OutputTemplate, frame, _p.CooldownSeconds);

        private int CountSettled(int a, int b)
        {
            int n = 0;
            for (int f = a; f <= b; f++) if (IsSettled(f)) n++;
            return n;
        }

        private int FirstUnsettled(int a, int b)
        {
            for (int f = a; f <= b; f++) if (!IsSettled(f)) return f;
            return -1;
        }

        private static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);

        // ---------- 进程与参数 ----------

        private string BuildArguments(int a, int b)
        {
            string scene = (_p.SceneFile ?? string.Empty).Trim();
            string head = scene.Length > 0 ? $"-render \"{scene}\"" : "-render";
            // N=1（a==b）沿用单帧旧格式 "-frame a"，否则渲范围 "-frame a b 1"
            return b > a ? $"{head} -frame {a} {b} 1" : $"{head} -frame {a}";
        }

        /// <summary>杀整棵进程树。杀后立即返回，由游标推进/重试决定下一步；不做额外空载等待。</summary>
        private void KillTree(Process proc)
        {
            int pid = 0;
            try { if (proc != null) pid = proc.Id; } catch { }

            if (pid > 0) RunTaskKill($"/F /T /PID {pid}");
            try { if (proc != null && !proc.HasExited) { proc.Kill(); proc.WaitForExit(5000); } } catch { }
            if (pid > 0) RunTaskKill($"/F /T /PID {pid}");
        }

        /// <summary>首次启动前检测同名残留进程：无→继续；有→按 UI 决策“杀干净继续”或“停止不启动”。</summary>
        private bool HandlePreexisting()
        {
            Process[] leftovers;
            try { leftovers = Process.GetProcessesByName(_p.ProcessName); }
            catch { return true; }

            if (leftovers == null || leftovers.Length == 0) return true;

            int count = leftovers.Length;
            PreexistingChoice choice = PreexistingHandler != null
                ? PreexistingHandler(count)
                : PreexistingChoice.Abort;

            if (choice == PreexistingChoice.Abort)
            {
                LogWarn($"检测到 {count} 个 {(_p.ProcessName)} 进程在运行；按选择停止调度，未启动、也不动这些进程。");
                foreach (var p in leftovers) { try { p.Dispose(); } catch { } }
                return false;
            }

            LogWarn($"检测到 {count} 个残留 {(_p.ProcessName)} 进程，正在结束…");
            foreach (var p in leftovers)
            {
                try { KillTree(p); }
                finally { try { p.Dispose(); } catch { } }
            }
            SleepIdle(3); // 稍候确保句柄释放
            LogInfo("残留进程已清理，开始渲染。");
            return true;
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

        private static bool SafeExited(Process p)
        {
            try { return p == null || p.HasExited; } catch { return true; }
        }

        private static int SafeExitCode(Process p)
        {
            try { return p.ExitCode; } catch { return int.MinValue; }
        }

        /// <summary>可中断等待；返回 false 表示期间收到停止请求。</summary>
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
