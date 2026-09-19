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
    ///  · 内层 RunBlock：只观察这一个进程。启动后每"检查间隔"轮询：
    ///    块内全部落定→（进程还活着则杀）成功；已退出且有部分完成→有进展失败；已退出无产物/崩溃报告在时窗/无进展超时→异常计数+1，
    ///    达"最大异常次数"→杀进程判异常失败；纯无响应(未超时)只提示一次、异常清零、不杀（无窗口进程恒不触发）。
    ///
    /// 时间参数：轮询=检查间隔；"杀进程→下次启动"之间按结束原因插入空载冷却（自杀成功1s / 自然退出或有进展→30s检查间隔 / 秒退·崩溃·无进展异常或跳帧→60s帧间冷却 / 清残留→3s），此间无进程真正降温；产物落定需距最后写入≥帧间冷却秒；无进展超时/崩溃时窗独立；挂起由"无进展超时"收口。
    /// 全部帧号/块端点均夹到 [StartFrame,EndFrame]。
    /// </summary>
    public class FrameRenderController : IRenderController
    {
        /// <summary>单块结束原因，用于决定"杀进程→下次启动"的空载间隔。</summary>
        private enum BlockOutcome
        {
            /// <summary>全落定且进程被我杀。</summary>
            SuccessKilled,
            /// <summary>全落定且进程已正常退出。</summary>
            SuccessExited,
            /// <summary>进程退出但仅部分落定（有前进）。</summary>
            FailedProgress,
            /// <summary>秒退/崩溃/无进展，达最大异常被杀。</summary>
            FailedAbnormal,
            /// <summary>用户停止或进程起不来。</summary>
            Abort
        }

        /// <summary>连续多少块"原地卡起点即失败"判为全局问题并熔断。</summary>
        private const int CircuitBreakerBlocks = 3;
        /// <summary>块产物已全落定、被我杀掉的进程，到下次启动的极短空载秒数。</summary>
        private const int PostKillIdleSeconds = 1;
        /// <summary>清理残留进程后到首次启动的等待秒数。</summary>
        private const int PreexistingKillSeconds = 3;

        /// <summary>后台调度线程。</summary>
        private Thread _worker;
        /// <summary>停止请求标志，令所有可中断等待尽快返回。</summary>
        private volatile bool _stopRequested;
        /// <summary>本次运行的模式参数快照。</summary>
        private ModeProfile _p;

        /// <summary>控制器是否正在运行。</summary>
        public bool IsRunning { get; private set; }

        /// <summary>产生一条日志时触发。</summary>
        public event EventHandler<LogEntry> Log;
        /// <summary>运行状态变化时触发。</summary>
        public event EventHandler<RunnerStatus> StatusChanged;
        /// <summary>进度更新时触发。</summary>
        public event EventHandler<FrameProgressInfo> FrameProgressChanged;

        /// <summary>首次启动前若发现同名渲染进程已在运行，回调 UI 询问如何处理（返回决策）。为 null 时按“停止队列”保守处理。</summary>
        public Func<int, PreexistingChoice> PreexistingHandler { get; set; }

        /// <summary>启动分块调度：打印参数概览并拉起后台线程。</summary>
        public void Start(ModeProfile profile)
        {
            if (IsRunning) return;

            _p = profile;
            _stopRequested = false;
            IsRunning = true;
            RaiseStatus(RunnerStatus.Running);

            int total = Math.Max(0, _p.EndFrame - _p.StartFrame + 1);
            LogInfo(Localizer.Tf("log.fr.startCmd", "分块渲染启动：{0}  -render \"{1}\"", _p.ExePath, _p.SceneFile));
            LogInfo(Localizer.Tf("log.fr.range", "帧范围 {0}–{1}（共 {2} 帧），模板 {3}，最大分块 {4}", _p.StartFrame, _p.EndFrame, total, _p.OutputTemplate, _p.MaxChunkLength));
            LogInfo(Localizer.Tf("log.fr.params", "检查间隔 {0}s，无进展超时 {1}s，帧间冷却 {2}s，最大异常 {3}，帧最大失败 {4}，失败{5}",
                _p.FrameCheckIntervalSeconds, _p.FrameTimeoutSeconds, _p.CooldownSeconds, _p.MaxAbnormalCount, _p.MaxFrameFailCount,
                _p.OnFail == OnFailBehaviour.Skip ? Localizer.T("log.fr.failSkip", "跳过") : Localizer.T("log.fr.failStop", "停止")));

            _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "FrameRenderController" };
            _worker.Start();
        }

        /// <summary>请求停止调度（结束当前渲染进程后收尾）。</summary>
        public void Stop()
        {
            if (!IsRunning) return;
            _stopRequested = true;
            RaiseStatus(RunnerStatus.Stopping);
            LogInfo(Localizer.T("log.fr.stopping", "正在停止…（将结束当前渲染进程）"));
        }

        // ================= 外层：游标 + 分块推进 =================

        /// <summary>外层主循环：单调游标逐块推进，按块结果决定游标前进/跳帧/停止与各档启动前空载冷却。</summary>
        private void WorkerLoop()
        {
            int start = _p.StartFrame, end = _p.EndFrame;
            int total = end - start + 1;
            if (total <= 0)
            {
                LogError(Localizer.T("log.fr.noFrames", "结束帧小于起始帧，无帧可渲染。"));
                Finish(RunnerStatus.Error);
                return;
            }

            // 首次启动前：若已有同名渲染进程在跑，弹窗让用户决定“停止队列”或“杀残留再启动”
            if (!HandlePreexisting())
            {
                LogWarn(Localizer.T("log.fr.userStop", "用户选择停止，未启动渲染。"));
                Finish(RunnerStatus.Stopped);
                return;
            }

            int N = Math.Max(1, _p.MaxChunkLength);
            int cursor = start;
            int consecStall = 0;
            int accounted = 0;
            int preIdle = 0; // 下次启动前的空载冷却（无进程时降温）；0=首次直接启动
            RunnerStatus final = RunnerStatus.Stopped;

            try
            {
                while (cursor <= end && !_stopRequested)
                {
                    // 跳过已落定的帧（续渲/重叠）
                    while (cursor <= end && IsSettled(cursor)) cursor++;
                    if (cursor > end) break;

                    int fa = cursor;
                    int fb = Math.Min(fa + N - 1, end);
                    int focusFail = 0;

                    while (true)
                    {
                        if (_stopRequested) break;

                        // 启动前空载冷却（此间无渲染进程）
                        if (preIdle > 0 && !SleepIdle(preIdle)) break;

                        BlockOutcome oc = RunBlock(fa, fb);
                        if (oc == BlockOutcome.Abort) { _stopRequested = true; break; }

                        if (oc == BlockOutcome.SuccessKilled) // 全落定但进程赖着，被我杀 → 只需极短
                        {
                            consecStall = 0; preIdle = PostKillIdleSeconds; cursor = fb + 1; break;
                        }
                        if (oc == BlockOutcome.SuccessExited)  // 全落定且进程正常退出
                        {
                            consecStall = 0; preIdle = _p.FrameCheckIntervalSeconds; cursor = fb + 1; break;
                        }

                        // 失败：按实际产物决定游标
                        int newFirst = FirstUnsettled(fa, fb);
                        if (newFirst < 0) { consecStall = 0; preIdle = _p.FrameCheckIntervalSeconds; cursor = fb + 1; break; }

                        if (newFirst > fa) // 有进展：游标推到缺口，重取块
                        {
                            consecStall = 0;
                            cursor = newFirst;
                            preIdle = oc == BlockOutcome.FailedProgress ? _p.FrameCheckIntervalSeconds : _p.CooldownSeconds;
                            break;
                        }

                        // 原地卡起点帧：秒退/崩溃/无进展/跳帧，均给帧间冷却降温
                        preIdle = _p.CooldownSeconds;
                        focusFail++;
                        if (focusFail >= _p.MaxFrameFailCount)
                        {
                            if (_p.OnFail == OnFailBehaviour.Stop)
                            {
                                LogWarn(Localizer.Tf("log.fr.frameStop", "帧 {0} 连续 {1} 次尝试无进展，按策略停止调度。", fa, focusFail));
                                cursor = end + 1; // 退出外层 while
                                break;
                            }
                            consecStall++;
                            LogWarn(Localizer.Tf("log.fr.frameSkip", "帧 {0} 连续 {1} 次尝试无进展，跳过该帧。", fa, focusFail));
                            cursor = fa + 1;
                            break;
                        }
                        LogWarn(Localizer.Tf("log.fr.frameRetry", "帧 {0} 第 {1}/{2} 次尝试无进展，重启本块。", fa, focusFail, _p.MaxFrameFailCount));
                    }

                    accounted = Clamp(cursor - start, 0, total);
                    RaiseProgress(accounted, total, Math.Min(cursor, end + 1), "");

                    if (_stopRequested) break;
                    if (consecStall >= CircuitBreakerBlocks)
                    {
                        LogError(Localizer.Tf("log.fr.circuitBreak", "连续 {0} 块都在起点即失败、毫无进展，疑似资产缺失/未烘缓存/授权等全局问题，停止调度。", consecStall));
                        final = RunnerStatus.Error;
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                LogError(Localizer.Tf("log.fr.exception", "调度异常终止: {0}", ex.Message));
                final = RunnerStatus.Error;
            }

            if (final != RunnerStatus.Error)
                LogInfo(Localizer.Tf("log.fr.summaryDone", "调度结束：已推进 {0}/{1} 帧（缺帧可重开工具填范围续渲）。", Clamp(cursor - start, 0, total), total));
            Finish(final);
        }

        // ================= 内层：一个进程渲 [a,b] 的观察 =================

        /// <summary>内层：启动一个进程渲块 [a,b]，按检查间隔轮询，返回细分结束原因。异常去抖沿用 Team Render 时窗判定。</summary>
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
                LogInfo(Localizer.Tf("log.fr.blockStart", "启动渲染 块[{0},{1}] PID {2}  参数: {3}", a, b, process.Id, psi.Arguments));

                var startT = DateTime.Now;
                var crashWindow = TimeSpan.FromSeconds(_p.FrameCheckIntervalSeconds * (_p.MaxAbnormalCount + 1.5)); // 与 Team Render 同款时窗

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
                        if (alive)
                        {
                            LogInfo(Localizer.Tf("log.fr.doneKilled", "块[{0},{1}] 全部落定但进程未退出，结束进程并计成功。", a, b));
                            KillTree(process);
                            return BlockOutcome.SuccessKilled;
                        }
                        LogInfo(Localizer.Tf("log.fr.doneExit", "块[{0},{1}] 渲染完成（退出码 {2}）。", a, b, SafeExitCode(process)));
                        return BlockOutcome.SuccessExited;
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
                        LogWarn(Localizer.Tf("log.fr.exitedPartial", "块[{0},{1}] 进程退出、已完成 {2}/{3} 帧，判本次尝试失败，外层从缺口续渲。", a, b, settled, blockLen));
                        return BlockOutcome.FailedProgress;
                    }

                    string badReason = null;
                    if (exited && settled == 0) badReason = Localizer.T("log.fr.reasonSnappy", "已退出无产物(秒退)");
                    else if (crash) badReason = Localizer.T("log.fr.reasonCrash", "崩溃报告在时窗内");
                    else if (noProgress) badReason = Localizer.Tf("log.fr.reasonNoProgress", "{0}s 无新帧落定", _p.FrameTimeoutSeconds);

                    if (badReason != null)
                    {
                        abnormal++;
                        LogWarn(Localizer.Tf("log.fr.abnCount", "块[{0},{1}] {2}，异常计数 {3}/{4}。", a, b, badReason, abnormal, _p.MaxAbnormalCount));
                        if (abnormal >= _p.MaxAbnormalCount)
                        {
                            LogWarn(Localizer.Tf("log.fr.abnMax", "块[{0},{1}] 连续异常达 {2} 次，结束进程，判本次尝试失败。", a, b, _p.MaxAbnormalCount));
                            KillTree(process);
                            return BlockOutcome.FailedAbnormal;
                        }
                    }
                    else
                    {
                        if (abnormal > 0) { abnormal = 0; LogInfo(Localizer.Tf("log.fr.recovered", "块[{0},{1}] 恢复正常，异常计数清零。", a, b)); }
                        if (unresponsive)
                        {
                            if (!loggedNoResp)
                            {
                                LogInfo(Localizer.Tf("log.fr.noRespInfo", "块[{0},{1}] 当前无响应（高负载常态），不单独处理，持续至无进展超时再判（本块已运行 {2}s）。", a, b, (DateTime.Now - startT).TotalSeconds.ToString("F0")));
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
                LogError(Localizer.Tf("log.fr.launchErr", "启动/监控 块[{0},{1}] 出错: {2}", a, b, ex.Message));
                try { if (process != null && !process.HasExited) KillTree(process); } catch { }
                return BlockOutcome.Abort; // 起不来视为终止（多为配置/路径问题），停止整场
            }
            finally
            {
                try { process?.Dispose(); } catch { }
            }
        }

        // ---------- 帧扫描辅助（全部夹在 [Start,End] 内） ----------

        /// <summary>某帧产物是否已落定（存在·非空·距写入≥帧间冷却秒）。</summary>
        private bool IsSettled(int frame)
            => FrameScanner.IsFrameSettled(_p.OutputTemplate, frame, _p.CooldownSeconds);

        /// <summary>统计区间 [a,b] 内已落定的帧数。</summary>
        private int CountSettled(int a, int b)
        {
            int n = 0;
            for (int f = a; f <= b; f++) if (IsSettled(f)) n++;
            return n;
        }

        /// <summary>返回区间 [a,b] 内第一个未落定的帧号；全部落定返回 -1。</summary>
        private int FirstUnsettled(int a, int b)
        {
            for (int f = a; f <= b; f++) if (!IsSettled(f)) return f;
            return -1;
        }

        /// <summary>把 v 夹到 [lo,hi]。</summary>
        private static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);

        // ---------- 进程与参数 ----------

        /// <summary>构造渲染参数：N=1（a==b）用 `-frame a`，否则渲范围 `-frame a b 1`；带工程文件路径并加引号。</summary>
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
                LogWarn(Localizer.Tf("log.fr.preexistStop", "检测到 {0} 个 {1} 进程在运行；按选择停止调度，未启动、也不动这些进程。", count, _p.ProcessName));
                foreach (var p in leftovers) { try { p.Dispose(); } catch { } }
                return false;
            }

            LogWarn(Localizer.Tf("log.fr.preexistKill", "检测到 {0} 个残留 {1} 进程，正在结束…", count, _p.ProcessName));
            foreach (var p in leftovers)
            {
                try { KillTree(p); }
                finally { try { p.Dispose(); } catch { } }
            }
            SleepIdle(PreexistingKillSeconds); // 稍候确保句柄释放
            LogInfo(Localizer.T("log.fr.preexistCleared", "残留进程已清理，开始渲染。"));
            return true;
        }

        /// <summary>调用 taskkill 执行参数（.NET Framework 无进程树 Kill 的兜底）。</summary>
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
            catch (Exception ex) { LogWarn(Localizer.Tf("log.taskkillFail", "taskkill 执行失败({0}): {1}", arguments, ex.Message)); }
        }

        /// <summary>安全读取进程是否已退出（异常按已退出处理）。</summary>
        private static bool SafeExited(Process p)
        {
            try { return p == null || p.HasExited; } catch { return true; }
        }

        /// <summary>安全读取退出码（读不到返回 int.MinValue）。</summary>
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

        /// <summary>结束运行：置运行标志、抛最终状态并记一条收尾日志。</summary>
        private void Finish(RunnerStatus status)
        {
            IsRunning = false;
            RaiseStatus(status);
            LogInfo(status == RunnerStatus.Error ? Localizer.T("log.fr.errStop", "调度已因错误停止。") : Localizer.T("log.fr.stopped", "调度已停止。"));
        }

        /// <summary>抛出一帧进度更新。</summary>
        private void RaiseProgress(int completed, int total, int frame, string stage)
            => FrameProgressChanged?.Invoke(this,
                new FrameProgressInfo { Completed = completed, Total = total, CurrentFrame = frame, Stage = stage });

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
