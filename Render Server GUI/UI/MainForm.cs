using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
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
        /// <summary>内存中的整体配置（三模式参数 + 当前模式）。</summary>
        private AppConfig _cfg;
        /// <summary>当前运行中的控制器实例。</summary>
        private IRenderController _controller;
        /// <summary>UI 是否已就绪（就绪前忽略模式/语言切换事件以免覆盖初值）。</summary>
        private bool _uiReady;
        /// <summary>语言下拉对应的语言码（与 Items 同序）。</summary>
        private readonly System.Collections.Generic.List<string> _langCodes = new System.Collections.Generic.List<string>();
        /// <summary>最近一次运行状态，切换语言时据此刷新状态文字。</summary>
        private RunnerStatus _lastStatus = RunnerStatus.Stopped;

        /// <summary>初始化窗体：装配语言与数值范围、事件，载入配置并应用界面语言。</summary>
        public MainForm()
        {
            InitializeComponent();
            ConfigureNumericRanges();
            BuildLanguageMenu();
            WireEvents();
            LoadConfigIntoUi();

            // 应用持久化语言
            int li = _langCodes.IndexOf(_cfg.Language);
            if (li < 0) li = 0;
            string code = _langCodes.Count > 0 ? _langCodes[li] : Localizer.DefaultLang;
            Localizer.SetLanguage(code);
            cmbLang.SelectedIndex = li;
            ApplyLanguage();

            _uiReady = true;
        }

        // ---------- 初始化 ----------

        /// <summary>设置各数字输入框的取值范围。</summary>
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

        /// <summary>设置单个 NumericUpDown 的最小/最大值，整数、无千分位。</summary>
        private static void SetRange(NumericUpDown num, int min, int max)
        {
            num.Minimum = min;
            num.Maximum = max;
            num.DecimalPlaces = 0;
            num.ThousandsSeparator = false;
        }

        /// <summary>集中订阅界面事件（模式切换、按钮、路径浏览、帧范围刷新、关闭）。</summary>
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

            numFrStart.ValueChanged += (s, e) => UpdateRangeCount();
            numFrEnd.ValueChanged += (s, e) => UpdateRangeCount();
            cmbLang.SelectedIndexChanged += (s, e) => OnLanguageChanged();

            FormClosing += OnFormClosingHandler;
        }

        /// <summary>刷新"帧范围"右侧的帧数量显示（只读，随起止帧自动更新；结束小于起始则显示 0）。</summary>
        private void UpdateRangeCount()
        {
            long n = (long)numFrEnd.Value - (long)numFrStart.Value + 1;
            if (n < 0) n = 0;
            lblRangeCount.Text = Localizer.Tf("ui.count", "共 {0} 帧", n);
        }

        // ---------- 多语言 ----------

        /// <summary>用扫描到的语言填充下拉（各语言显示其本名，不随界面语言变）。</summary>
        private void BuildLanguageMenu()
        {
            _langCodes.Clear();
            cmbLang.Items.Clear();
            var langs = Localizer.Languages;
            if (langs == null || langs.Count == 0)
            {
                _langCodes.Add(Localizer.DefaultLang);
                cmbLang.Items.Add("简体中文");
                return;
            }
            foreach (var kv in langs)
            {
                _langCodes.Add(kv.Key);
                cmbLang.Items.Add(kv.Value);
            }
        }

        /// <summary>语言下拉切换：应用并持久化所选语言。</summary>
        private void OnLanguageChanged()
        {
            if (!_uiReady) return;
            int i = cmbLang.SelectedIndex;
            if (i < 0 || i >= _langCodes.Count) return;
            string code = _langCodes[i];
            Localizer.SetLanguage(code);
            _cfg.Language = code;
            _cfg.Save(out _);
            ApplyLanguage();
        }

        /// <summary>把界面所有可见文字按当前语言重设；下拉项重填并保留选择。</summary>
        private void ApplyLanguage()
        {
            Text = Localizer.T("ui.title", "Render Server GUI");
            lblMode.Text = Localizer.T("ui.mode", "模式");
            RebuildCombo(cmbMode, new[]
            {
                Localizer.T("ui.mode.team", "Team Render（看门狗）"),
                Localizer.T("ui.mode.c4d", "Cinema 4D（单帧）"),
                Localizer.T("ui.mode.cl", "Commandline（单帧）")
            });
            btnStart.Text = Localizer.T("ui.start", "启动");
            btnStop.Text = Localizer.T("ui.stop", "停止");
            lblLang.Text = Localizer.T("ui.lang", "语言");

            // Team Render 分组
            grpTeamRender.Text = Localizer.T("ui.trGroup", "Team Render 参数");
            lblTrExe.Text = Localizer.T("ui.exe", "主程序路径");
            lblTrProc.Text = Localizer.T("ui.proc", "进程名称");
            lblTrReport.Text = Localizer.T("ui.report", "异常记录文件");
            lblTrCache.Text = Localizer.T("ui.cache", "缓存目录");
            lblTrPort.Text = Localizer.T("ui.port", "端口");
            lblTrInterval.Text = Localizer.T("ui.checkInterval", "检查间隔(秒)");
            lblTrHang.Text = Localizer.T("ui.maxHang", "最大挂起次数");
            lblTrWork.Text = Localizer.T("ui.workMin", "连续工作(分)");
            lblTrRest.Text = Localizer.T("ui.restMin", "休息时长(分)");
            chkTrClearCache.Text = Localizer.T("ui.clearCache", "启动前清空缓存目录");

            // 单帧分组
            grpFrame.Text = Localizer.T("ui.frGroup", "单帧调度参数");
            lblFrExe.Text = Localizer.T("ui.exe", "主程序路径");
            lblFrProc.Text = Localizer.T("ui.proc", "进程名称");
            lblFrReport.Text = Localizer.T("ui.report", "异常记录文件");
            lblFrScene.Text = Localizer.T("ui.scene", "工程文件");
            lblFrOutput.Text = Localizer.T("ui.output", "输出模板");
            lblFrStart.Text = Localizer.T("ui.range", "帧范围(起~止)");
            lblMaxChunk.Text = Localizer.T("ui.maxChunk", "最大分块长度");
            lblFrCooldown.Text = Localizer.T("ui.cooldown", "帧间冷却(秒)");
            lblFrTimeout.Text = Localizer.T("ui.noProgressTimeout", "无进展超时(秒)");
            lblFrInterval.Text = Localizer.T("ui.checkInterval", "检查间隔(秒)");
            lblFrRetry.Text = Localizer.T("ui.maxAbnormal", "最大异常次数");
            lblFrFail.Text = Localizer.T("ui.maxFail", "帧最大失败次数");
            lblFrOnFail.Text = Localizer.T("ui.onFail", "失败时");
            RebuildCombo(cmbFrOnFail, new[]
            {
                Localizer.T("ui.onFailStop", "停止并告警"),
                Localizer.T("ui.onFailSkip", "跳过继续")
            });
            btnFrPreview.Text = Localizer.T("ui.preview", "预览输出文件名并校验目录");
            lblLogTitle.Text = Localizer.T("ui.logTitle", "运行日志");
            btnClearLog.Text = Localizer.T("ui.clearLog", "清空日志");

            SetStatusText();
            UpdateRangeCount();
        }

        /// <summary>按当前语言与最近状态刷新状态文字。</summary>
        private void SetStatusText()
        {
            switch (_lastStatus)
            {
                case RunnerStatus.Running: lblStatusText.Text = Localizer.T("ui.statusRunning", "运行中"); break;
                case RunnerStatus.Stopping: lblStatusText.Text = Localizer.T("ui.statusStopping", "停止中"); break;
                case RunnerStatus.Error: lblStatusText.Text = Localizer.T("ui.statusError", "异常"); break;
                default: lblStatusText.Text = Localizer.T("ui.statusStopped", "已停止"); break;
            }
        }

        /// <summary>用新文本重填下拉项并尽量保留当前选择索引。</summary>
        private static void RebuildCombo(ComboBox cmb, string[] items)
        {
            int sel = cmb.SelectedIndex;
            cmb.BeginUpdate();
            cmb.Items.Clear();
            foreach (var it in items) cmb.Items.Add(it);
            if (sel >= 0 && sel < cmb.Items.Count) cmb.SelectedIndex = sel;
            cmb.EndUpdate();
        }

        // ---------- 模式 <-> 索引 ----------

        /// <summary>模式 → 下拉框索引。</summary>
        private static int ModeToIndex(RenderMode mode)
        {
            switch (mode)
            {
                case RenderMode.TeamRender: return 0;
                case RenderMode.Cinema4D: return 1;
                default: return 2;
            }
        }

        /// <summary>下拉框索引 → 模式。</summary>
        private static RenderMode IndexToMode(int index)
        {
            switch (index)
            {
                case 0: return RenderMode.TeamRender;
                case 1: return RenderMode.Cinema4D;
                default: return RenderMode.Commandline;
            }
        }

        /// <summary>是否为单帧/分块模式（Cinema 4D 或 Commandline）。</summary>
        private static bool IsFrameMode(RenderMode mode)
            => mode == RenderMode.Cinema4D || mode == RenderMode.Commandline;

        // ---------- 配置 <-> UI ----------

        /// <summary>从磁盘载入配置并灌入界面。</summary>
        private void LoadConfigIntoUi()
        {
            _cfg = AppConfig.Load();
            cmbMode.SelectedIndex = ModeToIndex(_cfg.Mode);
            ApplyModeToUi(_cfg.Mode);
        }

        /// <summary>模式下拉切换：先提交旧模式编辑，再载入新模式参数。</summary>
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
                UpdateRangeCount();
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

        /// <summary>把整数值夹到某 NumericUpDown 的 [Min,Max]，用于安全赋值。</summary>
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

        /// <summary>启动：提交并校验参数、落盘、按模式建控制器并启动。</summary>
        private void StartCurrent()
        {
            CommitCurrentProfile();

            ModeProfile p = _cfg.ProfileOf(_cfg.Mode);
            if (!ValidateProfile(p, out string err))
            {
                AppendLog(new LogEntry(LogLevel.Error, Localizer.T("msg.badPrefix", "参数校验未通过：") + err));
                MessageBox.Show(err, Localizer.T("msg.cantStartTitle", "无法启动"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
            string body = Localizer.Tf("dlg.preexistBody",
                "检测到已有 {0} 个「{1}」进程在运行。\n\n选“是”＝结束这些残留进程后再开始队列；\n选“否”＝停止本次调度（不启动、也不动这些进程）。",
                count, name);
            var r = MessageBox.Show(this, body,
                Localizer.T("dlg.preexistTitle", "已存在渲染进程"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            return r == DialogResult.Yes ? PreexistingChoice.KillAndStart : PreexistingChoice.Abort;
        }

        /// <summary>请求停止当前控制器。</summary>
        private void StopCurrent()
        {
            if (_controller != null && _controller.IsRunning)
            {
                _controller.Stop();
            }
        }

        /// <summary>校验参数是否可启动，返回是否通过并给出错误文本。</summary>
        private bool ValidateProfile(ModeProfile p, out string error)
        {
            error = null;
            if (p == null) { error = Localizer.T("msg.badNull", "未选择模式。"); return false; }
            if (string.IsNullOrWhiteSpace(p.ExePath)) { error = Localizer.T("msg.badExe", "主程序路径为空。"); return false; }
            if (string.IsNullOrWhiteSpace(p.ProcessName)) { error = Localizer.T("msg.badProc", "进程名称为空。"); return false; }
            if (string.IsNullOrWhiteSpace(p.ReportPath)) { error = Localizer.T("msg.badReport", "异常记录文件路径为空。"); return false; }

            if (IsFrameMode(_cfg.Mode))
            {
                if (string.IsNullOrWhiteSpace(p.SceneFile)) { error = Localizer.T("msg.badScene", "工程文件为空。"); return false; }
                if (p.StartFrame > p.EndFrame) { error = Localizer.T("msg.badRange", "起始帧不能大于结束帧。"); return false; }
                if (string.IsNullOrWhiteSpace(p.OutputTemplate)) { error = Localizer.T("msg.templateEmpty", "输出模板为空。"); return false; }
                if (!Regex_HasToken(p.OutputTemplate)) { error = Localizer.T("msg.templateNoToken", "输出模板缺少帧号占位符，应形如 Image_****.png（星号个数=补零位数）。"); return false; }
            }
            return true;
        }

        /// <summary>判断输出模板是否含星号帧号占位符。</summary>
        private static bool Regex_HasToken(string template)
        {
            return System.Text.RegularExpressions.Regex.IsMatch(template, @"\*+");
        }

        /// <summary>运行中禁用参数编辑与模式切换，停止后恢复。</summary>
        private void SetRunningUi(bool running)
        {
            btnStart.Enabled = !running;
            btnStop.Enabled = running;
            cmbMode.Enabled = !running;
            grpTeamRender.Enabled = !running;
            grpFrame.Enabled = !running;
        }

        // ---------- 控制器事件（回主线程） ----------

        /// <summary>控制器日志事件：转投 UI 线程追加到日志框。</summary>
        private void OnControllerLog(object sender, LogEntry entry)
        {
            if (IsDisposed) return;
            try { BeginInvoke(new Action(() => AppendLog(entry))); } catch { }
        }

        /// <summary>控制器状态事件：转投 UI 线程更新状态灯。</summary>
        private void OnControllerStatus(object sender, RunnerStatus status)
        {
            if (IsDisposed) return;
            try { BeginInvoke(new Action(() => ApplyStatus(status))); } catch { }
        }

        /// <summary>控制器进度事件：转投 UI 线程更新进度条。</summary>
        private void OnControllerProgress(object sender, FrameProgressInfo info)
        {
            if (IsDisposed) return;
            try { BeginInvoke(new Action(() => ApplyProgress(info))); } catch { }
        }

        /// <summary>按级别着色追加一行日志，并在超限处裁剪。</summary>
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

        /// <summary>按运行状态更新状态灯颜色/文字并同步按钮可用性。</summary>
        private void ApplyStatus(RunnerStatus status)
        {
            _lastStatus = status;
            switch (status)
            {
                case RunnerStatus.Running:
                    lblStatusDot.BackColor = Color.ForestGreen;
                    break;
                case RunnerStatus.Stopping:
                    lblStatusDot.BackColor = Color.DarkOrange;
                    break;
                case RunnerStatus.Error:
                    lblStatusDot.BackColor = Color.Firebrick;
                    SetRunningUi(false);
                    break;
                default:
                    lblStatusDot.BackColor = Color.Gray;
                    SetRunningUi(false);
                    break;
            }
            SetStatusText();
        }

        /// <summary>按进度信息更新进度条。</summary>
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
                MessageBox.Show(this, Localizer.T("msg.needFrameMode", "工程文件与输出模板用于单帧/分块模式，请先切到 Cinema 4D 或 Commandline 模式。"),
                    Localizer.T("msg.previewTitleShort", "预览输出名"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            CommitCurrentProfile();
            ModeProfile p = _cfg.ProfileOf(_cfg.Mode);

            if (string.IsNullOrWhiteSpace(p.OutputTemplate))
            {
                MessageBox.Show(this, Localizer.T("msg.templateEmpty", "输出模板为空。"),
                    Localizer.T("msg.previewTitleShort", "预览输出名"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!Regex_HasToken(p.OutputTemplate))
            {
                MessageBox.Show(this, Localizer.T("msg.templateNoToken", "输出模板缺少帧号占位符，应形如 Image_****.png（星号个数=补零位数）。"),
                    Localizer.T("msg.previewTitleShort", "预览输出名"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string startPath = FrameScanner.FramePath(p.OutputTemplate, p.StartFrame);
            string endPath = FrameScanner.FramePath(p.OutputTemplate, p.EndFrame);
            var sb = new StringBuilder();
            sb.AppendLine(Localizer.Tf("dlg.preStartFrame", "起始帧 {0} → {1}", p.StartFrame, startPath));
            sb.AppendLine(Localizer.Tf("dlg.preEndFrame", "结束帧 {0} → {1}", p.EndFrame, endPath));
            sb.AppendLine();

            string dir = Path.GetDirectoryName(startPath);
            bool dirOk = !string.IsNullOrEmpty(dir) && Directory.Exists(dir);
            if (!dirOk)
            {
                sb.AppendLine(Localizer.T("dlg.preDirNotExist", "输出目录尚不存在："));
                sb.AppendLine("  " + dir);
                sb.AppendLine(Localizer.T("dlg.preWillCreate", "（开始渲染时会自动创建，属正常情况）"));
            }
            else
            {
                sb.AppendLine(Localizer.Tf("dlg.preDir", "输出目录：{0}", dir));
                var frames = FrameScanner.EnumerateRenderedFrames(p.OutputTemplate);
                if (frames.Count == 0)
                {
                    sb.AppendLine(CountFiles(dir) > 0
                        ? Localizer.T("dlg.preNoMatchWarn", "⚠ 目录里有文件，但没有匹配命名规则的帧号：多半前缀/补零位数/扩展名与工程内实际输出名不一致，请对照修正模板。")
                        : Localizer.T("dlg.preEmpty", "（输出目录为空——全新任务属正常）"));
                }
                else
                {
                    string segs = BuildFrameRanges(Path.GetFileName(p.OutputTemplate), frames);
                    sb.AppendLine(Localizer.Tf("dlg.preCount", "{0} 等 {1} 个文件", segs, frames.Count));
                }
            }

            MessageBox.Show(this, sb.ToString(), Localizer.T("dlg.previewTitle", "输出文件预览 / 校验"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ---------- 浏览对话框 ----------

        /// <summary>浏览选择可执行文件（主程序）。</summary>
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

        /// <summary>浏览选择异常记录文件（_BugReport.txt，允许不存在）。</summary>
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

        /// <summary>浏览选择 Cinema 4D 工程文件（.c4d，允许不存在）。</summary>
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

        /// <summary>浏览选择文件夹并写入目标文本框。</summary>
        private void BrowseFolder(TextBox target)
        {
            using (var dlg = new FolderBrowserDialog())
            {
                if (Directory.Exists(target.Text)) dlg.SelectedPath = target.Text;
                if (dlg.ShowDialog(this) == DialogResult.OK)
                    target.Text = dlg.SelectedPath;
            }
        }

        /// <summary>选择输出目录，仅替换模板的目录部分、保留含占位符的文件名。</summary>
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

        /// <summary>取路径的目录部分（用于设置对话框初始目录），无效返回 null。</summary>
        private static string initialDirOf(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            try { return Path.GetDirectoryName(path); } catch { return null; }
        }

        /// <summary>统计目录内文件数（异常返回 0）。</summary>
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

        /// <summary>打开文件对话框（可设初始目录），确定后把所选路径写入目标框。</summary>
        private void TryFill(TextBox target, OpenFileDialog dlg, string initialDir)
        {
            if (initialDir != null && Directory.Exists(initialDir)) dlg.InitialDirectory = initialDir;
            if (dlg.ShowDialog(this) == DialogResult.OK)
                target.Text = dlg.FileName;
        }

        // ---------- 关闭 ----------

        /// <summary>关窗前停止运行中的控制器、提交当前编辑并落盘配置。</summary>
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
