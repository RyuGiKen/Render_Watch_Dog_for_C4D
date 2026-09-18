using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using RenderServerGui.Models;
using RenderServerGui.Services;

namespace RenderServerGui.UI
{
    /// <summary>
    /// 主窗体：模式切换、参数编辑、启动/停止、日志与进度显示、配置持久化。
    /// 阶段一为框架 + UI；引擎逻辑在 TeamRenderController / FrameRenderController 中，当前为占位。
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
            SetRange(numFrRetry, 0, 999);
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
            btnFrOutput.Click += (s, e) => BrowseOutputFolder();

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
                txtFrProc.Text = p.ProcessName;
                txtFrReport.Text = p.ReportPath;
                txtFrOutput.Text = p.OutputTemplate;
                numFrStart.Value = Clamp(p.StartFrame, numFrStart);
                numFrEnd.Value = Clamp(p.EndFrame, numFrEnd);
                numFrCooldown.Value = Clamp(p.CooldownSeconds, numFrCooldown);
                numFrTimeout.Value = Clamp(p.FrameTimeoutSeconds, numFrTimeout);
                numFrInterval.Value = Clamp(p.FrameCheckIntervalSeconds, numFrInterval);
                numFrRetry.Value = Clamp(p.MaxRetryPerFrame, numFrRetry);
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
                p.ProcessName = txtFrProc.Text.Trim();
                p.ReportPath = txtFrReport.Text.Trim();
                p.OutputTemplate = txtFrOutput.Text.Trim();
                p.StartFrame = (int)numFrStart.Value;
                p.EndFrame = (int)numFrEnd.Value;
                p.CooldownSeconds = (int)numFrCooldown.Value;
                p.FrameTimeoutSeconds = (int)numFrTimeout.Value;
                p.FrameCheckIntervalSeconds = (int)numFrInterval.Value;
                p.MaxRetryPerFrame = (int)numFrRetry.Value;
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
            _controller.Log += OnControllerLog;
            _controller.StatusChanged += OnControllerStatus;
            _controller.FrameProgressChanged += OnControllerProgress;

            SetRunningUi(true);
            _controller.Start(p);
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
                if (p.StartFrame > p.EndFrame) { error = "起始帧不能大于结束帧。"; return false; }
                if (string.IsNullOrWhiteSpace(p.OutputTemplate)) { error = "输出模板为空。"; return false; }
                if (!Regex_HasToken(p.OutputTemplate)) { error = "输出模板缺少帧号占位符，形如 Image_[xxxx].png。"; return false; }
            }
            return true;
        }

        private static bool Regex_HasToken(string template)
        {
            return System.Text.RegularExpressions.Regex.IsMatch(template, @"\[x+\]");
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
                if (string.IsNullOrEmpty(file)) file = "Image_[xxxx].png";
                txtFrOutput.Text = Path.Combine(dlg.SelectedPath, file);
            }
        }

        private static string initialDirOf(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            try { return Path.GetDirectoryName(path); } catch { return null; }
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
