using System;
using System.Collections.Generic;
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
    ///  · 任务启动时（无渲染进程、机器空载）一次目录枚举建立 FrameStateMap（0=范围外/1=未落定/2=已落定），
    ///    落定口径：存在·非空·距写入≥帧间冷却（沿用 CooldownSeconds，防孤儿进程写出的半成品被误认）。
    ///    运行期游标推进与块尾限位只查内存，不再逐帧 stat。
    ///  · 外层 WorkerLoop：单调游标 cursor 只前进。每轮先跳过表中"已落定"的帧，取块
    ///    [a,b]=[a, 下一个已落定帧之前，且 ≤ min(a+N-1,末)]。块尾收缩是必需的：C4D 的 -frame a b 1
    ///    是连续区间，越过已落定帧会覆盖重渲（浪费算力且可能损坏已有成品）。
    ///    成功→cursor=b+1 并整块置 2；失败→按块内实际产物结算状态表，再取块内首个未落定帧 newFirst：
    ///      newFirst>a 视为有进展，cursor=newFirst（重置焦点失败计数）；
    ///      newFirst==a 原地卡起点，焦点失败+1，达帧最大失败次数则跳过起点帧 cursor=a+1；
    ///    连续多块都"原地卡起点即失败"→ 熔断跳过当前任务、继续队列下一个。
    ///  · 内层 RunBlock：只观察这一个进程。启动后每"检查间隔"做一次块内目录枚举，
    ///    同时得到"存在且非空"帧数 written 与块内最新写入时刻：
    ///    written 齐且进程已退出→成功；written 齐、进程仍在且写入已静默≥帧间冷却→杀进程计成功；
    ///    written 齐但仍在写→继续轮询；已退出且只写了部分→有进展失败；
    ///    无进展判定：written 增加或块内产物 mtime 前进都会重置计时；
    ///    进程活着但持续无任何产物写入、超过"无进展超时"→ 首现记录、下一轮复查仍超时即杀；
    ///    已退出无产物/崩溃报告在时窗→异常计数+1，达"最大异常次数"→杀进程判异常失败；纯无响应只提示、异常清零、不杀。
    ///  · 任务收尾：复核表中"已落定"帧在磁盘上是否仍有有效产物，缺失只记告警（下次启动重新扫描并补渲）。
    ///
    /// 时间参数：轮询=检查间隔；"块结束→下次启动"之间按结束原因插入空载冷却：
    ///   自杀成功1s / 自然退出→帧间冷却+检查间隔(默认60+30=90s，散热，不得缩短) / 有进展→30s /
    ///   秒退·崩溃·无进展异常或跳帧→60s / 清残留→3s。此间无渲染进程，是真降温。
    ///   帧间冷却另用于启动扫描的历史产物判定与 SuccessKilled 写入静默期；无进展超时/崩溃时窗独立。
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
            /// <summary>秒退/崩溃达最大异常被杀，或无进展超时复查后被杀。</summary>
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
        /// <summary>本次运行的模式参数快照（全局参数）。</summary>
        private ModeProfile _p;
        /// <summary>当前正在执行的任务（逐帧参数从这里读）。</summary>
        private RenderTask _currentTask;
        /// <summary>当前任务的帧状态表（任务启动时建，运行期只查内存，块结束后增量更新）。</summary>
        private FrameStateMap _frameMap;

        /// <summary>控制器是否正在运行。</summary>
        public bool IsRunning { get; private set; }

        /// <summary>产生一条日志时触发。</summary>
        public event EventHandler<LogEntry> Log;
        /// <summary>运行状态变化时触发。</summary>
        public event EventHandler<RunnerStatus> StatusChanged;
        /// <summary>进度更新时触发。</summary>
        public event EventHandler<FrameProgressInfo> FrameProgressChanged;

        /// <summary>首次启动前若发现同名渲染进程已在运行，回调 UI 询问如何处理（返回决策）。为 null 时按"停止队列"保守处理。</summary>
        public Func<int, PreexistingChoice> PreexistingHandler { get; set; }

        /// <summary>启动分块调度：打印参数概览并拉起后台线程。</summary>
        public void Start(ModeProfile profile)
        {
            if (IsRunning) return;

            _p = profile;
            _stopRequested = false;
            IsRunning = true;
            RaiseStatus(RunnerStatus.Running);

            var tasks = _p.Tasks;
            if (tasks == null || tasks.Count == 0)
            {
                LogError(Localizer.T("log.fr.noTasks", "任务列表为空，无法启动。"));
                Finish(RunnerStatus.Error);
                return;
            }

            LogInfo(Localizer.Tf("log.fr.startCmd", "分块渲染启动：{0}  共 {1} 个任务", _p.ExePath, tasks.Count));
            for (int i = 0; i < tasks.Count; i++)
            {
                var t = tasks[i];
                LogInfo(Localizer.Tf("log.fr.taskBrief", "  {2}（{0}/{1}）分块≤{3}  超时{4}s",
                    i + 1, tasks.Count, t.DisplayLabel, t.MaxChunkLength, t.FrameTimeoutSeconds));
            }
            LogInfo(Localizer.Tf("log.fr.params", "全局：检查间隔 {0}s，帧间冷却 {1}s，最大异常 {2}，帧最大失败 {3}，失败{4}",
                _p.FrameCheckIntervalSeconds, _p.CooldownSeconds, _p.MaxAbnormalCount, _p.MaxFrameFailCount,
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

        /// <summary>外层主循环：遍历任务列表，每个任务内单调游标逐块推进。</summary>
        private void WorkerLoop()
        {
            var tasks = _p.Tasks;
            int taskCount = tasks.Count;

            // 首次启动前：若已有同名渲染进程在跑，弹窗让用户决定"停止队列"或"杀残留再启动"
            if (!HandlePreexisting())
            {
                LogWarn(Localizer.T("log.fr.userStop", "用户选择停止，未启动渲染。"));
                Finish(RunnerStatus.Stopped);
                return;
            }

            RunnerStatus final = RunnerStatus.Stopped;

            try
            {
                for (int ti = 0; ti < taskCount && !_stopRequested; ti++)
                {
                    _currentTask = tasks[ti];

                    // 启动进程前：工程文件必须存在。路径错误/文件丢失/误删除时 C4D 会打开空工程
                    // 卡在非渲染状态（CPU/GPU 均低、显存不涨），此处直接跳过该任务并记日志
                    if (string.IsNullOrWhiteSpace(_currentTask.SceneFile) || !File.Exists(_currentTask.SceneFile))
                    {
                        LogWarn(Localizer.Tf("log.fr.sceneMissing", "工程文件不存在，跳过 {0}：{1}", _currentTask.DisplayLabel, _currentTask.SceneFile));
                        continue;
                    }

                    // 任务间冷却（第一个任务前不等待）
                    if (ti > 0 && _p.CooldownSeconds > 0)
                    {
                        LogInfo(Localizer.Tf("log.fr.taskCooldown", "任务间冷却 {0}s…", _p.CooldownSeconds));
                        if (!SleepIdle(_p.CooldownSeconds)) break;
                    }

                    LogInfo(Localizer.Tf("log.fr.taskStart", "═══ {0}（{1}/{2}）═══", _currentTask.DisplayLabel, ti + 1, taskCount));
                    bool taskOk = RunSingleTask(ti, taskCount);
                    if (!taskOk && final != RunnerStatus.Error)
                    {
                        // 熔断跳过当前任务，继续下一个
                        LogWarn(Localizer.Tf("log.fr.taskSkipped", "{0} 被跳过（熔断）。", _currentTask.DisplayLabel));
                    }
                }
            }
            catch (Exception ex)
            {
                LogError(Localizer.Tf("log.fr.exception", "调度异常终止: {0}", ex.Message));
                final = RunnerStatus.Error;
            }

            if (final != RunnerStatus.Error)
                LogInfo(Localizer.Tf("log.fr.allDone", "全部 {0} 个任务处理完毕。", taskCount));
            Finish(final);
        }

        /// <summary>
        /// 执行单个任务的分块渲染循环。返回 true=正常完成，false=熔断跳过。
        /// </summary>
        private bool RunSingleTask(int taskIndex, int taskCount)
        {
            int start = _currentTask.StartFrame, end = _currentTask.EndFrame;
            int total = end - start + 1;
            if (total <= 0)
            {
                LogError(Localizer.T("log.fr.noFrames", "结束帧小于起始帧，无帧可渲染。"));
                return true; // 不算熔断，直接跳过
            }

            // 任务启动、渲染进程尚未拉起、机器空载：一次目录枚举建立帧状态表。
            // 落定判定沿用 CooldownSeconds，避免把孤儿 C4D 进程仍在写的半成品算作成品。
            _frameMap = new FrameStateMap(start, end);
            _frameMap.Load(FrameScanner.ScanSettledStates(_currentTask.OutputTemplate, start, end, _p.CooldownSeconds));
            int settledAtStart = _frameMap.CountSettled(start, end);
            LogInfo(Localizer.Tf("log.fr.stateScan", "帧状态扫描：{0}/{1} 帧已落定，{2} 帧待渲。",
                settledAtStart, total, total - settledAtStart));

            int N = Math.Max(1, _currentTask.MaxChunkLength);
            int cursor = start;
            int consecStall = 0;
            int accounted = 0;
            int preIdle = 0;

            while (cursor <= end && !_stopRequested)
            {
                // 跳过已落定的帧（续渲/重叠）——查表，不再逐帧 stat
                while (cursor <= end && _frameMap.IsSettled(cursor)) cursor++;
                if (cursor > end) break;

                int fa = cursor;
                // 块尾必须停在下一个已落定帧之前：C4D 的 -frame a b 1 是连续区间，
                // 无法跳过区间内已完成帧，越过就会覆盖重渲（浪费算力，且可能损坏已有成品）。
                // hi 先按 N 收敛上限，避免全缺时一路扫到末帧。
                int hi = Math.Min(end, fa + N - 1);
                int fb = fa;
                while (fb < hi && !_frameMap.IsSettled(fb + 1)) fb++;
                int focusFail = 0;

                while (true)
                {
                    if (_stopRequested) break;

                    // 启动前空载冷却（此间无渲染进程）
                    if (preIdle > 0 && !SleepIdle(preIdle)) break;

                    BlockOutcome oc = RunBlock(fa, fb);
                    ApplyBlockResult(fa, fb, oc);   // 按实际产物更新帧状态表
                    if (oc == BlockOutcome.Abort) { _stopRequested = true; break; }

                    if (oc == BlockOutcome.SuccessKilled)
                    {
                        consecStall = 0; preIdle = PostKillIdleSeconds; cursor = fb + 1; break;
                    }
                    if (oc == BlockOutcome.SuccessExited)
                    {
                        // 块间散热：帧间冷却 + 一个检查间隔（默认 60+30=90s，无渲染进程、空载）。
                        // 原实现的散热由"落定需距写入≥帧间冷却"在块尾自然形成；改造去掉该判据后
                        // 必须显式保留同等时长，不得缩短。CooldownSeconds 的注释本就是「避免过热」。
                        consecStall = 0;
                        preIdle = _p.CooldownSeconds + _p.FrameCheckIntervalSeconds;
                        cursor = fb + 1; break;
                    }

                    // 失败：按帧状态表决定游标
                    int newFirst = _frameMap.FirstUnsettled(fa, fb);
                    if (newFirst < 0) { consecStall = 0; preIdle = _p.FrameCheckIntervalSeconds; cursor = fb + 1; break; }

                    if (newFirst > fa) // 有进展：游标推到缺口，重取块
                    {
                        consecStall = 0;
                        cursor = newFirst;
                        preIdle = oc == BlockOutcome.FailedProgress ? _p.FrameCheckIntervalSeconds : _p.CooldownSeconds;
                        break;
                    }

                    // 原地卡起点帧
                    preIdle = _p.CooldownSeconds;
                    focusFail++;
                    if (focusFail >= _p.MaxFrameFailCount)
                    {
                        if (_p.OnFail == OnFailBehaviour.Stop)
                        {
                            LogWarn(Localizer.Tf("log.fr.frameStop", "帧 {0} 连续 {1} 次尝试无进展，按策略停止当前任务。", fa, focusFail));
                            cursor = end + 1;
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
                RaiseProgress(accounted, total, Math.Min(cursor, end + 1),
                    Localizer.Tf("log.fr.taskProgress", "任务 {0}/{1}", taskIndex + 1, taskCount));

                if (_stopRequested) break;
                if (consecStall >= CircuitBreakerBlocks)
                {
                    LogError(Localizer.Tf("log.fr.circuitBreak", "连续 {0} 块都在起点即失败、毫无进展，疑似资产缺失/未烘缓存/授权等全局问题，跳过当前任务。", consecStall));
                    return false; // 熔断，跳任务
                }
            }

            VerifyFinishedFrames(start, end);

            LogInfo(Localizer.Tf("log.fr.taskDone", "{0} 完成：已推进 {1}/{2} 帧。",
                _currentTask.DisplayLabel, Clamp(cursor - start, 0, total), total));
            return true;
        }

        // ================= 内层：一个进程渲 [a,b] 的观察 =================

        /// <summary>内层：启动一个进程渲块 [a,b]，按检查间隔轮询，返回细分结束原因。无进展按产物写入活动判定；秒退/崩溃报告保留异常计数去抖。</summary>
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
                    string outDir = Path.GetDirectoryName(FrameScanner.FramePath(_currentTask.OutputTemplate, a));
                    if (!string.IsNullOrEmpty(outDir)) Directory.CreateDirectory(outDir);
                }
                catch { }

                process = Process.Start(psi);
                LogInfo(Localizer.Tf("log.fr.blockStart", "启动渲染 块[{0},{1}] PID {2}  参数: {3}", a, b, process.Id, psi.Arguments));

                var startT = DateTime.Now;
                var crashWindow = TimeSpan.FromSeconds(_p.FrameCheckIntervalSeconds * (_p.MaxAbnormalCount + 1.5));

                int abnormal = 0;
                int lastWritten = 0;
                var lastProgress = startT;
                var lastWriteUtc = DateTime.MinValue;   // 块内产物最近见到的写入时刻；mtime 前进 = 渲染在正常产出
                DateTime? noProgressSince = null;       // 无进展超时首现时刻（下一轮复查仍超时才杀）
                bool loggedNoResp = false;
                int blockLen = b - a + 1;

                while (true)
                {
                    if (_stopRequested) { KillTree(process); return BlockOutcome.Abort; }

                    // 一次块内目录枚举，同时得到"存在且非空"的帧数与块内最新写入时刻，
                    // 替代原来的逐帧 CountSettled + 全目录 GetLatestWriteTimeUtc 两次扫描。
                    var scan = FrameScanner.ScanBlock(_currentTask.OutputTemplate, a, b);
                    int written = scan.NonEmptyCount;
                    bool allWritten = written >= blockLen;
                    bool exited = SafeExited(process);
                    // 静默期：块内产物最近一次写入距今 ≥ 帧间冷却，用于确认进程被杀时写入确已停止。
                    bool quiet = scan.LatestWriteUtc > DateTime.MinValue &&
                                 (DateTime.UtcNow - scan.LatestWriteUtc).TotalSeconds >= _p.CooldownSeconds;

                    if (allWritten && exited)
                    {
                        LogInfo(Localizer.Tf("log.fr.doneExit", "块[{0},{1}] 渲染完成（退出码 {2}）。", a, b, SafeExitCode(process)));
                        return BlockOutcome.SuccessExited;
                    }
                    if (allWritten && !exited && quiet)
                    {
                        LogInfo(Localizer.Tf("log.fr.doneKilled", "块[{0},{1}] 全部产出且写入已停止，但进程未退出，结束进程并计成功。", a, b));
                        KillTree(process);
                        return BlockOutcome.SuccessKilled;
                    }
                    // allWritten 但进程仍在写（!quiet）：继续轮询，不杀、也不算完成

                    // 进展判定：非空产物数增加，或块内产物 mtime 前进（正在写盘也算正常产出），二者任一即重置无进展计时
                    if (written > lastWritten) { lastWritten = written; lastProgress = DateTime.Now; }
                    if (scan.LatestWriteUtc > lastWriteUtc) { lastWriteUtc = scan.LatestWriteUtc; lastProgress = DateTime.Now; }

                    bool noProgress = (DateTime.Now - lastProgress).TotalSeconds > _currentTask.FrameTimeoutSeconds;
                    bool crash = ProcessHealth.IsBugReportRecent(_p.ReportPath, crashWindow);

                    bool unresponsive = false;
                    if (!exited)
                    {
                        try { process.Refresh(); } catch { }
                        unresponsive = process.MainWindowHandle != IntPtr.Zero && !ProcessHealth.IsResponding(process);
                    }

                    // 已退出且已有部分产物：终态且能续，立即判尝试失败（让外层前进到缺口）
                    if (exited && written > 0 && !allWritten)
                    {
                        LogWarn(Localizer.Tf("log.fr.exitedPartial", "块[{0},{1}] 进程退出、已完成 {2}/{3} 帧，判本次尝试失败，外层从缺口续渲。", a, b, written, blockLen));
                        return BlockOutcome.FailedProgress;
                    }

                    // 无进展超时：进程活着但持续无任何产物写入活动 → 下轮复查仍超时即杀（不走异常去抖）
                    if (noProgress && noProgressSince == null)
                    {
                        noProgressSince = DateTime.Now;
                        LogWarn(Localizer.Tf("log.fr.noProgressFirst", "块[{0},{1}] 无进展超时首现（{2}s 内无产物写入），下一轮复查。", a, b, _currentTask.FrameTimeoutSeconds));
                    }
                    else if (noProgress && noProgressSince != null)
                    {
                        LogWarn(Localizer.Tf("log.fr.noProgressKill", "块[{0},{1}] 复查仍无产物写入，按无进展超时（{2}s）结束进程重试。", a, b, _currentTask.FrameTimeoutSeconds));
                        KillTree(process);
                        return BlockOutcome.FailedAbnormal;
                    }
                    else if (!noProgress)
                    {
                        noProgressSince = null; // 出现写入活动，恢复计时
                    }

                    // 秒退/崩溃报告属瞬时信号，保留异常计数去抖
                    string badReason = null;
                    if (exited && written == 0) badReason = Localizer.T("log.fr.reasonSnappy", "已退出无产物(秒退)");
                    else if (crash) badReason = Localizer.T("log.fr.reasonCrash", "崩溃报告在时窗内");

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
                return BlockOutcome.Abort;
            }
            finally
            {
                try { process?.Dispose(); } catch { }
            }
        }

        // ---------- 帧状态表（任务级唯一真值） ----------

        /// <summary>
        /// 块结束后把实际产物写回帧状态表。
        /// 成功（进程已退出，或静默期确认写入已停止）→ 整块置已落定；
        /// 失败 → 一次块内枚举，首个"不存在或为空"之前的帧置已落定，其余保持未落定待重渲。
        /// 结算口径用"存在且非空"而非冷却：进程已退出即写入完成，冷却只用于启动扫描与 SuccessKilled 静默期。
        /// </summary>
        private void ApplyBlockResult(int a, int b, BlockOutcome oc)
        {
            if (oc == BlockOutcome.Abort || _frameMap == null) return;

            if (oc == BlockOutcome.SuccessExited || oc == BlockOutcome.SuccessKilled)
            {
                _frameMap.Mark(a, b, FrameStateMap.Settled);
                return;
            }

            var scan = FrameScanner.ScanBlock(_currentTask.OutputTemplate, a, b);
            int first = scan.FirstMissing();
            if (first < 0) _frameMap.Mark(a, b, FrameStateMap.Settled);
            else
            {
                _frameMap.Mark(a, first - 1, FrameStateMap.Settled);
                _frameMap.Mark(first, b, FrameStateMap.Missing);
            }
        }

        /// <summary>
        /// 任务收尾：复核表中已落定帧在磁盘上是否仍有有效产物。
        /// 渲染中被外部删除或改成 0 字节时状态表不感知，这里把静默缺帧变成可见告警；
        /// 不自愈（回退 cursor 需改循环结构），下次启动会重新扫描并补渲。
        /// 复核用 minAge=0，只核存在与大小，与时间无关。
        /// </summary>
        private void VerifyFinishedFrames(int start, int end)
        {
            if (_frameMap == null) return;
            byte[] actual = FrameScanner.ScanSettledStates(_currentTask.OutputTemplate, start, end, 0);
            var missing = new List<int>();
            for (int f = start; f <= end && f < actual.Length; f++)
                if (_frameMap.IsSettled(f) && actual[f] != FrameStateMap.Settled) missing.Add(f);
            if (missing.Count == 0) return;

            int shown = Math.Min(50, missing.Count);
            var parts = new string[shown];
            for (int i = 0; i < shown; i++) parts[i] = missing[i].ToString();
            string list = string.Join(", ", parts);
            if (missing.Count > shown) list += " …";
            LogWarn(Localizer.Tf("log.fr.missingOnFinish",
                "任务完成但检测到 {0} 帧产物缺失/为空（表中已落定、磁盘无有效文件）：{1}；下次启动会重新识别并补渲。",
                missing.Count, list));
        }

        /// <summary>把 v 夹到 [lo,hi]。</summary>
        private static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);

        // ---------- 进程与参数 ----------

        /// <summary>构造渲染参数：N=1（a==b）用 `-frame a`，否则渲范围 `-frame a b 1`；带工程文件路径并加引号。</summary>
        private string BuildArguments(int a, int b)
        {
            string scene = (_currentTask.SceneFile ?? string.Empty).Trim();
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

        /// <summary>首次启动前检测同名残留进程：无→继续；有→按 UI 决策"杀干净继续"或"停止不启动"。</summary>
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
