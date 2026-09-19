using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using RenderServerGui.Models;
using RenderServerGui.Services;

namespace RenderServerGui.UI
{
    /// <summary>
    /// 主窗体：模式切换、参数编辑、启动/停止、日志与进度显示、配置持久化。
    /// 判定/渲染逻辑在 TeamRenderController 与 FrameRenderController（分块/逐帧游标）中，本文件只做界面与调度编排。
    /// </summary>
    public partial class MainForm : Form
    {
        private AppConfig _cfg;
        private IRenderController _controller;
        private bool _uiReady;

        public MainForm()
        {
            InitializeComponent();
            ConfigureNumericRanges();
            WireEvents();
            LoadConfigIntoUi();
            _uiReady = true;
        }

        // ---------- 初始化 ----------

        private void ConfigureNumericRanges()
        {
            SetRange(numTrPort, 1, 65535);
            SetRange(numTrInterval, 1, 86400);
            SetRange(numTrHang, 1, 999);
            SetRange(numTrWork, 1, 1440);
            SetRange(numTrRest, 0, 1440);

            SetRange(numFrStart, 0, 999999);
            SetRange(numFrEnd, 0, 999999);
            SetRange(numFrCooldown, 0, 86400);
            SetRange(numFrTimeout, 1, 86400);
            SetRange(numFrInterval, 1, 86400);
            SetRange(numFrRetry, 1, 999);
            SetRange(numFrFail, 1, 999);
            SetRange(numMaxChunk, 1, 1000000);
        }

        private static void SetRange(NumericUpDown num, int min, int max)
        {
            num.Minimum = min;
            num.Maximum = max;
            num.DecimalPlaces = 0;
            num.ThousandsSeparator = false;
        }

        private void WireEvents()
        {
            cmbMode.SelectedIndexChanged += (s, e) => OnModeChanged();
            btnStart.Click += (s, e) => StartCurrent();
            btnStop.Click += (s, e) => StopCurrent();
            btnClearLog.Click += (s, e) => rtbLog.Clear();

            btnTrExe.Click += (s, e) => BrowseExe(txtTrExe);
            btnFrExe.Click += (s, e) => BrowseExe(txtFrExe);
            btnTrReport.Click += (s, e) => BrowseReport(txtTrReport);
            btnFrReport.Click += (s, e) => BrowseReport(txtFrReport);
            btnTrCache.Click += (s, e) => BrowseFolder(txtTrCache);
            btnFrScene.Click += (s, e) => BrowseScene(txtFrScene);
            btnFrOutput.Click += (s, e) => BrowseOutputFolder();
            btnFrPreview.Click += (s, e) => OnPreviewOutput();

            FormClosing += OnFormClosingHandler;
        }

        // ---------- 模式 <-> 索引 ----------

        private static int ModeToIndex(RenderMode mode)
        {
            switch (mode)
            {
                case RenderMode.TeamRender: return 0;
                case RenderMode.Cinema4D: return 1;
                default: return 2;
            }
        }

        private static RenderMode IndexToMode(int index)
        {
            switch (index)
            {
                case 0: return RenderMode.TeamRender;
                case 1: return RenderMode.Cinema4D;
                default: return RenderMode.Commandline;
            }
        }

        private static bool IsFrameMode(RenderMode mode)
            => mode == RenderMode.Cinema4D || mode == RenderMode.Commandline;

        // ---------- 配置 <-> UI ----------

        private void LoadConfigIntoUi()
        {
            _cfg = AppConfig.Load();
            cmbMode.SelectedIndex = ModeToIndex(_cfg.Mode);
            ApplyModeToUi(_cfg.Mode);
        }

        private void OnModeChanged()
        {
            if (!_uiReady) return;
            // 先把当前界面值写回旧模式配置，避免编辑丢失
            CommitCurrentProfile();
            _cfg.Mode = IndexToMode(cmbMode.SelectedIndex);
            ApplyModeToUi(_cfg.Mode);
        }

        /// <summary>按模式切换分组可见性，并把该模式已存参数灌入控件。</summary>
        private void ApplyModeToUi(RenderMode mode)
        {
            bool frame = IsFrameMode(mode);
            grpTeamRender.Visible = !frame;
            grpFrame.Visible = frame;
            progressBar.Visible = frame;

            ModeProfile p = _cfg.ProfileOf(mode);
            if (frame)
            {
                txtFrExe.Text = p.ExePath;
                txtFrScene.Text = p.SceneFile;
                txtFrProc.Text = p.ProcessName;
                txtFrReport.Text = p.ReportPath;
                txtFrOutput.Text = p.OutputTemplate;
                numFrStart.Value = Clamp(p.StartFrame, numFrStart);
                numFrEnd.Value = Clamp(p.EndFrame, numFrEnd);
                numFrCooldown.Value = Clamp(p.CooldownSeconds, numFrCooldown);
                numFrTimeout.Value = Clamp(p.FrameTimeoutSeconds, numFrTimeout);
                numFrInterval.Value = Clamp(p.FrameCheckIntervalSeconds, numFrInterval);
                numFrRetry.Value = Clamp(p.MaxAbnormalCount, numFrRetry);
                numFrFail.Value = Clamp(p.MaxFrameFailCount, numFrFail);
                numMaxChunk.Value = Clamp(p.MaxChunkLength, numMaxChunk);
                cmbFrOnFail.SelectedIndex = p.OnFail == OnFailBehaviour.Stop ? 0 : 1;
            }
            else
            {
                txtTrExe.Text = p.ExePath;
                txtTrProc.Text = p.ProcessName;
                numTrPort.Value = Clamp(p.Port, numTrPort);
                numTrInterval.Value = Clamp(p.CheckIntervalSeconds, numTrInterval);
                numTrHang.Value = Clamp(p.MaxHangCount, numTrHang);
                numTrWork.Value = Clamp(p.WorkingMinutes, numTrWork);
                numTrRest.Value = Clamp(p.RestMinutes, numTrRest);
                txtTrReport.Text = p.ReportPath;
                txtTrCache.Text = p.CachePath;
                chkTrClearCache.Checked = p.ClearCache;
            }
        }

        private static decimal Clamp(int v, NumericUpDown num)
        {
            if (v < (int)num.Minimum) return num.Minimum;
            if (v > (int)num.Maximum) return num.Maximum;
            return v;
        }

        /// <summary>把当前界面值写回当前模式的配置对象。</summary>
        private void CommitCurrentProfile()
        {
            RenderMode mode = _cfg.Mode;
            ModeProfile p = _cfg.ProfileOf(mode) ?? ModeProfile.ForPreset(mode);

            if (IsFrameMode(mode))
            {
                p.ExePath = txtFrExe.Text.Trim();
                p.SceneFile = txtFrScene.Text.Trim();
                p.ProcessName = txtFrProc.Text.Trim();
                p.ReportPath = txtFrReport.Text.Trim();
                p.OutputTemplate = txtFrOutput.Text.Trim();
                p.StartFrame = (int)numFrStart.Value;
                p.EndFrame = (int)numFrEnd.Value;
                p.CooldownSeconds = (int)numFrCooldown.Value;
                p.FrameTimeoutSeconds = (int)numFrTimeout.Value;
                p.FrameCheckIntervalSeconds = (int)numFrInterval.Value;
                p.MaxAbnormalCount = (int)numFrRetry.Value;
                p.MaxFrameFailCount = (int)numFrFail.Value;
                p.MaxChunkLength = (int)numMaxChunk.Value;
                p.OnFail = cmbFrOnFail.SelectedIndex == 1 ? OnFailBehaviour.Skip : OnFailBehaviour.Stop;
            }
            else
            {
                p.ExePath = txtTrExe.Text.Trim();
                p.ProcessName = txtTrProc.Text.Trim();
                p.Port = (int)numTrPort.Value;
                p.CheckIntervalSeconds = (int)numTrInterval.Value;
                p.MaxHangCount = (int)numTrHang.Value;
                p.WorkingMinutes = (int)numTrWork.Value;
                p.RestMinutes = (int)numTrRest.Value;
                p.ReportPath = txtTrReport.Text.Trim();
                p.CachePath = txtTrCache.Text.Trim();
                p.ClearCache = chkTrClearCache.Checked;
            }

            _cfg.SetProfile(mode, p);
        }

        // ---------- 启动 / 停止 ----------

        private void StartCurrent()
        {
            CommitCurrentProfile();

            ModeProfile p = _cfg.ProfileOf(_cfg.Mode);
            if (!ValidateProfile(p, out string err))
            {
                AppendLog(new LogEntry(LogLevel.Error, "参数校验未通过：" + err));
                MessageBox.Show(err, "无法启动", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _cfg.Save(out _); // 启动前落盘一份

            _controller = IsFrameMode(_cfg.Mode)
                ? (IRenderController)new FrameRenderController()
                : new TeamRenderController();
            if (_controller is FrameRenderController frc)
                frc.PreexistingHandler = AskPreexisting;
            _controller.Log += OnControllerLog;
            _controller.StatusChanged += OnControllerStatus;
            _controller.FrameProgressChanged += OnControllerProgress;

            SetRunningUi(true);
            _controller.Start(p);
        }

        /// <summary>首次启动前发现同名进程：在 UI 线程弹窗询问“杀残留再启动 / 停止队列”，返回决策。由后台线程经 Invoke 调来。</summary>
        private PreexistingChoice AskPreexisting(int count)
        {
            if (InvokeRequired)
                return (PreexistingChoice)Invoke(new Func<int, PreexistingChoice>(AskPreexisting), count);

            string name = _cfg.ProfileOf(_cfg.Mode)?.ProcessName ?? "渲染";
            var r = MessageBox.Show(this,
                $"检测到已有 {count} 个「{name}」进程在运行。\n\n" +
                "选“是”＝结束这些残留进程后再开始队列；\n" +
                "选“否”＝停止本次调度（不启动、也不动这些进程）。",
                "已存在渲染进程", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            return r == DialogResult.Yes ? PreexistingChoice.KillAndStart : PreexistingChoice.Abort;
        }

        private void StopCurrent()
        {
            if (_controller != null && _controller.IsRunning)
            {
                _controller.Stop();
            }
        }

        private bool ValidateProfile(ModeProfile p, out string error)
        {
            error = null;
            if (p == null) { error = "未选择模式。"; return false; }
            if (string.IsNullOrWhiteSpace(p.ExePath)) { error = "主程序路径为空。"; return false; }
            if (string.IsNullOrWhiteSpace(p.ProcessName)) { error = "进程名称为空。"; return false; }
            if (string.IsNullOrWhiteSpace(p.ReportPath)) { error = "异常记录文件路径为空。"; return false; }

            if (IsFrameMode(_cfg.Mode))
            {
                if (string.IsNullOrWhiteSpace(p.SceneFile)) { error = "工程文件为空。"; return false; }
                if (p.StartFrame > p.EndFrame) { error = "起始帧不能大于结束帧。"; return false; }
                if (string.IsNullOrWhiteSpace(p.OutputTemplate)) { error = "输出模板为空。"; return false; }
                if (!Regex_HasToken(p.OutputTemplate)) { error = "输出模板缺少帧号占位符，形如 Image_****.png（星号个数=补零位数）。"; return false; }
            }
            return true;
        }

        private static bool Regex_HasToken(string template)
        {
            return System.Text.RegularExpressions.Regex.IsMatch(template, @"\*+");
        }

        private void SetRunningUi(bool running)
        {
            btnStart.Enabled = !running;
            btnStop.Enabled = running;
            cmbMode.Enabled = !running;
            grpTeamRender.Enabled = !running;
            grpFrame.Enabled = !running;
        }

        // ---------- 控制器事件（回主线程） ----------

        private void OnControllerLog(object sender, LogEntry entry)
        {
            if (IsDisposed) return;
            try { BeginInvoke(new Action(() => AppendLog(entry))); } catch { }
        }

        private void OnControllerStatus(object sender, RunnerStatus status)
        {
            if (IsDisposed) return;
            try { BeginInvoke(new Action(() => ApplyStatus(status))); } catch { }
        }

        private void OnControllerProgress(object sender, FrameProgressInfo info)
        {
            if (IsDisposed) return;
            try { BeginInvoke(new Action(() => ApplyProgress(info))); } catch { }
        }

        private void AppendLog(LogEntry entry)
        {
            Color color = entry.Level == LogLevel.Error ? Color.Firebrick
                        : entry.Level == LogLevel.Warn ? Color.DarkOrange
                        : Color.Black;
            rtbLog.SelectionStart = rtbLog.TextLength;
            rtbLog.SelectionLength = 0;
            rtbLog.SelectionColor = color;
            rtbLog.AppendText($"[{entry.Time:HH:mm:ss}] {entry.Message}\n");
            rtbLog.SelectionColor = Color.Black;
            rtbLog.ScrollToCaret();
            TrimLog();
        }

        // 日志上限：超过行数或字符数就裁掉最旧的一部分，避免长跑占用过多内存。
        private const int MaxLogLines = 1500;
        private const int MaxLogChars = 150000;
        private const int LogTrimKeep = 1000;

        private void TrimLog()
        {
            if (rtbLog.Lines.Length <= MaxLogLines && rtbLog.TextLength <= MaxLogChars) return;

            // 保留末尾 LogTrimKeep 行：定位要删除的截断点（第 lines-keep 行的行首）
            int lines = rtbLog.Lines.Length;
            int keep = Math.Min(LogTrimKeep, lines / 2);
            int drop = lines - keep;
            if (drop <= 0) return;

            int cutIndex = rtbLog.GetFirstCharIndexFromLine(drop);
            if (cutIndex <= 0) return;

            rtbLog.Select(0, cutIndex);
            rtbLog.SelectedText = string.Empty;
            rtbLog.Select(rtbLog.TextLength, 0);
        }

        private void ApplyStatus(RunnerStatus status)
        {
            switch (status)
            {
                case RunnerStatus.Running:
                    lblStatusDot.BackColor = Color.ForestGreen;
                    lblStatusText.Text = "运行中";
                    break;
                case RunnerStatus.Stopping:
                    lblStatusDot.BackColor = Color.DarkOrange;
                    lblStatusText.Text = "停止中";
                    break;
                case RunnerStatus.Error:
                    lblStatusDot.BackColor = Color.Firebrick;
                    lblStatusText.Text = "异常";
                    SetRunningUi(false);
                    break;
                default:
                    lblStatusDot.BackColor = Color.Gray;
                    lblStatusText.Text = "已停止";
                    SetRunningUi(false);
                    break;
            }
        }

        private void ApplyProgress(FrameProgressInfo info)
        {
            if (info.Total <= 0) return;
            progressBar.Maximum = info.Total;
            progressBar.Value = Math.Min(info.Completed, info.Total);
        }

        // ---------- 输出名预览/校验 ----------

        private void OnPreviewOutput()
        {
            if (!IsFrameMode(_cfg.Mode))
            {
                MessageBox.Show(this, "工程文件与输出模板用于单帧模式，请先切到 Cinema 4D 或 Commandline 模式。",
                    "预览输出名", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            CommitCurrentProfile();
            ModeProfile p = _cfg.ProfileOf(_cfg.Mode);

            if (string.IsNullOrWhiteSpace(p.OutputTemplate))
            {
                MessageBox.Show(this, "输出模板为空。", "预览输出名", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!Regex_HasToken(p.OutputTemplate))
            {
                MessageBox.Show(this, "输出模板缺少帧号占位符，应形如 Image_****.png（星号个数=补零位数）。",
                    "预览输出名", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string startPath = FrameScanner.FramePath(p.OutputTemplate, p.StartFrame);
            string endPath = FrameScanner.FramePath(p.OutputTemplate, p.EndFrame);
            var sb = new StringBuilder();
            sb.AppendLine($"起始帧 {p.StartFrame} → {startPath}");
            sb.AppendLine($"结束帧 {p.EndFrame} → {endPath}");
            sb.AppendLine();

            string dir = Path.GetDirectoryName(startPath);
            bool dirOk = !string.IsNullOrEmpty(dir) && Directory.Exists(dir);
            if (!dirOk)
            {
                sb.AppendLine("输出目录尚不存在：");
                sb.AppendLine("  " + dir);
                sb.AppendLine("（开始渲染时会自动创建，属正常情况）");
            }
            else
            {
                sb.AppendLine($"输出目录：{dir}");
                var frames = FrameScanner.EnumerateRenderedFrames(p.OutputTemplate);
                if (frames.Count == 0)
                {
                    sb.AppendLine(CountFiles(dir) > 0
                        ? "⚠ 目录里有文件，但没有匹配命名规则的帧号：多半前缀/补零位数/扩展名与工程内实际输出名不一致，请对照修正模板。"
                        : "（输出目录为空——全新任务属正常）");
                }
                else
                {
                    string fileName = Path.GetFileName(p.OutputTemplate);
                    string segs = BuildFrameRanges(Path.GetFileName(string.IsNullOrWhiteSpace(fileName) ? p.OutputTemplate : fileName), frames);
                    sb.AppendLine();
                    sb.AppendLine($"识别到{segs} 等 {frames.Count} 个文件");
                    //int expect = p.EndFrame - p.StartFrame + 1;
                    //if (p.StartFrame >= 0 && p.EndFrame >= p.StartFrame && frames.Count < expect)
                    //    sb.AppendLine($"（任务范围 [{p.StartFrame},{p.EndFrame}] 应有 {expect} 帧，当前识别 {frames.Count} 帧）");
                }
            }

            MessageBox.Show(this, sb.ToString(), "输出文件预览 / 校验", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ---------- 浏览对话框 ----------

        private void BrowseExe(TextBox target)
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = "选择主程序";
                dlg.Filter = "可执行文件 (*.exe)|*.exe|所有文件 (*.*)|*.*";
                dlg.CheckFileExists = false;
                TryFill(target, dlg, initialDirOf(target.Text));
            }
        }

        private void BrowseReport(TextBox target)
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = "选择异常记录文件";
                dlg.Filter = "BugReport (_BugReport.txt)|_BugReport.txt|所有文件 (*.*)|*.*";
                dlg.CheckFileExists = false;
                TryFill(target, dlg, initialDirOf(target.Text));
            }
        }

        private void BrowseScene(TextBox target)
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = "选择工程文件";
                dlg.Filter = "Cinema 4D 工程 (*.c4d)|*.c4d|所有文件 (*.*)|*.*";
                dlg.CheckFileExists = false;
                TryFill(target, dlg, initialDirOf(target.Text));
            }
        }

        private void BrowseFolder(TextBox target)
        {
            using (var dlg = new FolderBrowserDialog())
            {
                if (Directory.Exists(target.Text)) dlg.SelectedPath = target.Text;
                if (dlg.ShowDialog(this) == DialogResult.OK)
                    target.Text = dlg.SelectedPath;
            }
        }

        private void BrowseOutputFolder()
        {
            using (var dlg = new FolderBrowserDialog())
            {
                string dir = initialDirOf(txtFrOutput.Text);
                if (dir != null && Directory.Exists(dir)) dlg.SelectedPath = dir;
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                // 仅替换模板的目录部分，保留文件名（含帧号占位符）
                string file = Path.GetFileName(txtFrOutput.Text.Trim());
                if (string.IsNullOrEmpty(file)) file = "Image_****.png";
                txtFrOutput.Text = Path.Combine(dlg.SelectedPath, file);
            }
        }

        private static string initialDirOf(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            try { return Path.GetDirectoryName(path); } catch { return null; }
        }

        private static string[] SafeGetFiles(string dir, string pattern)
        {
            try { return Directory.GetFiles(dir, pattern); }
            catch { return new string[0]; }
        }

        private static int CountFiles(string dir)
        {
            try { return Directory.GetFiles(dir).Length; }
            catch { return 0; }
        }

        /// <summary>把升序帧号合并成连续段，替换模板占位符显示，如 Image_[0,59].png，Image_[61,100].png。</summary>
        private static string BuildFrameRanges(string template, List<int> sortedFrames)
        {
            var parts = new List<string>();
            int runStart = sortedFrames[0], runPrev = sortedFrames[0];
            for (int i = 1; i <= sortedFrames.Count; i++)
            {
                bool continues = i < sortedFrames.Count && sortedFrames[i] == runPrev + 1;
                if (continues) { runPrev = sortedFrames[i]; continue; }

                string rep = runStart == runPrev ? $"[{runStart}]" : $"[{runStart},{runPrev}]";
                parts.Add(new System.Text.RegularExpressions.Regex(@"\*+").Replace(template, rep, 1)); // 只替换首个占位段
                if (i < sortedFrames.Count) { runStart = sortedFrames[i]; runPrev = sortedFrames[i]; }
            }
            return string.Join("，", parts);
        }

        private void TryFill(TextBox target, OpenFileDialog dlg, string initialDir)
        {
            if (initialDir != null && Directory.Exists(initialDir)) dlg.InitialDirectory = initialDir;
            if (dlg.ShowDialog(this) == DialogResult.OK)
                target.Text = dlg.FileName;
        }

        // ---------- 关闭 ----------

        private void OnFormClosingHandler(object sender, FormClosingEventArgs e)
        {
            _uiReady = false;
            if (_controller != null && _controller.IsRunning)
            {
                _controller.Stop();
                System.Threading.Thread.Sleep(200); // 给后台线程一点时间收尾
            }
            CommitCurrentProfile();
            _cfg.Save(out _);
        }
    }
}
