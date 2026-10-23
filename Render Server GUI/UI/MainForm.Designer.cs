using System.Drawing;
using System.Windows.Forms;

namespace RenderServerGui.UI
{
    partial class MainForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows 窗体设计器生成的代码

        // ── 布局工厂辅助（减少重复，保证一致的锚定/边距）──
        private static Label MakeLabel(string name, string text)
        {
            var l = new Label();
            l.AutoSize = true; l.Name = name; l.Text = text;
            l.Anchor = AnchorStyles.Top | AnchorStyles.Left; l.Margin = new Padding(3, 7, 6, 3); return l;
        }
        private static Label MakeHeader(string name, string text)
        {
            var l = new Label();
            l.AutoSize = true; l.Name = name; l.Text = text;
            l.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            l.Anchor = AnchorStyles.Top | AnchorStyles.Left; l.Margin = new Padding(3, 8, 3, 3); return l;
        }
        private static TextBox MakeText()
        {
            var t = new TextBox();
            t.Dock = DockStyle.Fill; t.Margin = new Padding(3, 6, 3, 3); return t;
        }
        private static Button MakeBrowse()
        {
            var b = new Button();
            b.Text = "…"; b.Size = new Size(30, 25); b.Dock = DockStyle.Fill; b.Margin = new Padding(3, 5, 0, 3); b.UseVisualStyleBackColor = true; return b;
        }
        private static NumericUpDown MakeNum(int w)
        {
            var n = new NumericUpDown();
            n.Size = new Size(w, 23); n.Anchor = AnchorStyles.Top | AnchorStyles.Left; n.Margin = new Padding(3, 6, 3, 3); return n;
        }
        private static Button MakeToolBtn(string name, string text)
        {
            var b = new Button();
            b.Name = name; b.Text = text; b.Size = new Size(30, 26); b.Margin = new Padding(2); b.UseVisualStyleBackColor = true; return b;
        }

        private void InitializeComponent()
        {
            this.panelTop = new FlowLayoutPanel();
            this.lblMode = new Label();
            this.cmbMode = new ComboBox();
            this.btnStart = new Button();
            this.btnStop = new Button();
            this.lblStatusDot = new Panel();
            this.lblStatusText = new Label();
            this.progressBar = new ProgressBar();
            this.chkKeepAwake = new CheckBox();
            this.lblLang = new Label();
            this.cmbLang = new ComboBox();

            this.panelParams = new Panel();
            this.grpTeamRender = new GroupBox();
            this.pnlTrScroll = new Panel();
            this.tflTr = new TableLayoutPanel();
            this.lblTrExe = new Label();
            this.txtTrExe = new TextBox();
            this.btnTrExe = new Button();
            this.lblTrProc = new Label();
            this.txtTrProc = new TextBox();
            this.lblTrReport = new Label();
            this.txtTrReport = new TextBox();
            this.btnTrReport = new Button();
            this.lblTrCache = new Label();
            this.txtTrCache = new TextBox();
            this.btnTrCache = new Button();
            this.lblTrPort = new Label();
            this.numTrPort = new NumericUpDown();
            this.lblTrInterval = new Label();
            this.numTrInterval = new NumericUpDown();
            this.lblTrHang = new Label();
            this.numTrHang = new NumericUpDown();
            this.lblTrWork = new Label();
            this.numTrWork = new NumericUpDown();
            this.lblTrRest = new Label();
            this.numTrRest = new NumericUpDown();
            this.chkTrClearCache = new CheckBox();

            this.grpFrame = new GroupBox();
            this.pnlFrameScroll = new Panel();
            this.tflFrame = new TableLayoutPanel();
            this.lblFrExe = new Label();
            this.txtFrExe = new TextBox();
            this.btnFrExe = new Button();
            this.lblFrProc = new Label();
            this.txtFrProc = new TextBox();
            this.lblFrReport = new Label();
            this.txtFrReport = new TextBox();
            this.btnFrReport = new Button();
            this.lblFrCooldown = new Label();
            this.numFrCooldown = new NumericUpDown();
            this.lblFrInterval = new Label();
            this.numFrInterval = new NumericUpDown();
            this.lblFrRetry = new Label();
            this.numFrRetry = new NumericUpDown();
            this.lblFrFail = new Label();
            this.numFrFail = new NumericUpDown();
            this.lblFrOnFail = new Label();
            this.cmbFrOnFail = new ComboBox();
            this.lblTaskList = new Label();
            this.pnlTaskList = new Panel();
            this.lstTasks = new ListBox();
            this.tflTaskBtns = new TableLayoutPanel();
            this.btnTaskAdd = new Button();
            this.btnTaskRemove = new Button();
            this.btnTaskUp = new Button();
            this.btnTaskDown = new Button();
            this.lblTaskDetail = new Label();
            this.lblFrScene = new Label();
            this.txtFrScene = new TextBox();
            this.btnFrScene = new Button();
            this.lblFrOutput = new Label();
            this.txtFrOutput = new TextBox();
            this.btnFrOutput = new Button();
            this.lblFrStart = new Label();
            this.pnlRange = new Panel();
            this.numFrStart = new NumericUpDown();
            this.lblFrEnd = new Label();
            this.numFrEnd = new NumericUpDown();
            this.lblRangeCount = new Label();
            this.lblMaxChunk = new Label();
            this.numMaxChunk = new NumericUpDown();
            this.lblFrTimeout = new Label();
            this.numFrTimeout = new NumericUpDown();
            this.btnFrPreview = new Button();

            this.panelLog = new Panel();
            this.rtbLog = new RichTextBox();
            this.lblLogTitle = new Label();
            this.btnClearLog = new Button();

            ((System.ComponentModel.ISupportInitialize)(this.numTrPort)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numTrInterval)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numTrHang)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numTrWork)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numTrRest)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numFrCooldown)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numFrInterval)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numFrRetry)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numFrFail)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numFrStart)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numFrEnd)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numMaxChunk)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numFrTimeout)).BeginInit();
            this.panelTop.SuspendLayout();
            this.panelParams.SuspendLayout();
            this.grpTeamRender.SuspendLayout();
            this.pnlTrScroll.SuspendLayout();
            this.tflTr.SuspendLayout();
            this.grpFrame.SuspendLayout();
            this.pnlFrameScroll.SuspendLayout();
            this.tflFrame.SuspendLayout();
            this.pnlTaskList.SuspendLayout();
            this.tflTaskBtns.SuspendLayout();
            this.pnlRange.SuspendLayout();
            this.panelLog.SuspendLayout();
            this.pnlRange.SuspendLayout();
            this.SuspendLayout();

            // panelTop（顶部工具条：FlowLayoutPanel，宽度不足自动换到第二行）
            this.panelTop.Controls.Add(this.lblMode);
            this.panelTop.Controls.Add(this.cmbMode);
            this.panelTop.Controls.Add(this.btnStart);
            this.panelTop.Controls.Add(this.btnStop);
            this.panelTop.Controls.Add(this.lblStatusDot);
            this.panelTop.Controls.Add(this.lblStatusText);
            this.panelTop.Controls.Add(this.progressBar);
            this.panelTop.Controls.Add(this.chkKeepAwake);
            this.panelTop.Controls.Add(this.lblLang);
            this.panelTop.Controls.Add(this.cmbLang);
            this.panelTop.Dock = DockStyle.Top;
            this.panelTop.Name = "panelTop";
            this.panelTop.FlowDirection = FlowDirection.LeftToRight;
            this.panelTop.WrapContents = true;
            this.panelTop.AutoSize = true;
            this.panelTop.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            this.panelTop.Padding = new Padding(8, 12, 8, 8);
            this.panelTop.MinimumSize = new Size(0, 48);
            this.panelTop.Size = new Size(1100, 48);

            this.lblMode.AutoSize = false; this.lblMode.TextAlign = ContentAlignment.MiddleCenter;
            this.lblMode.Location = new Point(12, 20); this.lblMode.Name = "lblMode"; this.lblMode.Size = new Size(48, 17); this.lblMode.Text = "模式"; this.lblMode.Margin = new Padding(4, 12, 4, 3);

            this.cmbMode.DropDownStyle = ComboBoxStyle.DropDownList; this.cmbMode.FormattingEnabled = true;
            this.cmbMode.Items.AddRange(new object[] { "Team Render（看门狗）", "Cinema 4D（单帧）", "Commandline（单帧）" });
            this.cmbMode.Name = "cmbMode"; this.cmbMode.Size = new Size(200, 25); this.cmbMode.Margin = new Padding(3, 9, 8, 3);

            this.btnStart.Name = "btnStart"; this.btnStart.Size = new Size(84, 28); this.btnStart.Text = "启动"; this.btnStart.UseVisualStyleBackColor = true; this.btnStart.Margin = new Padding(3, 8, 3, 3);
            this.btnStop.Enabled = false; this.btnStop.Name = "btnStop"; this.btnStop.Size = new Size(84, 28); this.btnStop.Text = "停止"; this.btnStop.UseVisualStyleBackColor = true; this.btnStop.Margin = new Padding(3, 8, 12, 3);
            this.lblStatusDot.BackColor = Color.Gray; this.lblStatusDot.BorderStyle = BorderStyle.FixedSingle;
            this.lblStatusDot.Name = "lblStatusDot"; this.lblStatusDot.Size = new Size(18, 18); this.lblStatusDot.Margin = new Padding(3, 12, 3, 3);
            this.lblStatusText.AutoSize = true; this.lblStatusText.Name = "lblStatusText"; this.lblStatusText.Text = "已停止"; this.lblStatusText.Margin = new Padding(3, 13, 12, 3);
            this.progressBar.Name = "progressBar"; this.progressBar.Size = new Size(150, 20); this.progressBar.Visible = false; this.progressBar.Margin = new Padding(3, 12, 8, 3);
            this.chkKeepAwake.AutoSize = true; this.chkKeepAwake.Checked = true; this.chkKeepAwake.CheckState = CheckState.Checked;
            this.chkKeepAwake.Name = "chkKeepAwake"; this.chkKeepAwake.Text = "阻止睡眠"; this.chkKeepAwake.UseVisualStyleBackColor = true; this.chkKeepAwake.Margin = new Padding(3, 13, 12, 3);
            this.lblLang.AutoSize = true; this.lblLang.TextAlign = ContentAlignment.MiddleCenter;
            this.lblLang.Name = "lblLang"; this.lblLang.Text = "语言"; this.lblLang.Margin = new Padding(3, 13, 4, 3);
            this.cmbLang.DropDownStyle = ComboBoxStyle.DropDownList; this.cmbLang.FormattingEnabled = true;
            this.cmbLang.Name = "cmbLang"; this.cmbLang.Size = new Size(130, 25); this.cmbLang.Margin = new Padding(3, 9, 3, 3);

            // panelParams（左侧参数列，超高自动滚动）
            this.panelParams.Controls.Add(this.grpTeamRender);
            this.panelParams.Controls.Add(this.grpFrame);
            this.panelParams.Dock = DockStyle.Left;
            this.panelParams.AutoScroll = true;
            this.panelParams.Name = "panelParams";
            this.panelParams.Size = new Size(568, 616);

            // grpTeamRender：TableLayout [AutoSize | 100% | AutoSize]，一字段一行
            this.grpTeamRender.Dock = DockStyle.Fill;
            this.grpTeamRender.Name = "grpTeamRender";
            this.grpTeamRender.Text = "Team Render 参数";
            this.grpTeamRender.Padding = new Padding(10, 6, 10, 8);
            this.grpTeamRender.Controls.Add(this.pnlTrScroll);

            this.pnlTrScroll.Dock = DockStyle.Fill; this.pnlTrScroll.AutoScroll = true; this.pnlTrScroll.Name = "pnlTrScroll";
            this.pnlTrScroll.Controls.Add(this.tflTr);

            this.tflTr.Dock = DockStyle.Top; this.tflTr.Name = "tflTr"; this.tflTr.AutoSize = true;
            this.tflTr.ColumnCount = 3;
            this.tflTr.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            this.tflTr.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            this.tflTr.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            for (int i = 0; i < 10; i++) this.tflTr.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            this.lblTrExe = MakeLabel("lblTrExe", "主程序路径"); this.tflTr.Controls.Add(this.lblTrExe, 0, 0);
            this.txtTrExe = MakeText(); this.tflTr.Controls.Add(this.txtTrExe, 1, 0);
            this.btnTrExe = MakeBrowse(); this.btnTrExe.Name = "btnTrExe"; this.tflTr.Controls.Add(this.btnTrExe, 2, 0);

            this.lblTrProc = MakeLabel("lblTrProc", "进程名称"); this.tflTr.Controls.Add(this.lblTrProc, 0, 1);
            this.txtTrProc = MakeText(); this.tflTr.Controls.Add(this.txtTrProc, 1, 1); this.tflTr.SetColumnSpan(this.txtTrProc, 2);

            this.lblTrReport = MakeLabel("lblTrReport", "异常记录文件"); this.tflTr.Controls.Add(this.lblTrReport, 0, 2);
            this.txtTrReport = MakeText(); this.tflTr.Controls.Add(this.txtTrReport, 1, 2);
            this.btnTrReport = MakeBrowse(); this.btnTrReport.Name = "btnTrReport"; this.tflTr.Controls.Add(this.btnTrReport, 2, 2);

            this.lblTrCache = MakeLabel("lblTrCache", "缓存目录"); this.tflTr.Controls.Add(this.lblTrCache, 0, 3);
            this.txtTrCache = MakeText(); this.tflTr.Controls.Add(this.txtTrCache, 1, 3);
            this.btnTrCache = MakeBrowse(); this.btnTrCache.Name = "btnTrCache"; this.tflTr.Controls.Add(this.btnTrCache, 2, 3);

            this.lblTrPort = MakeLabel("lblTrPort", "端口"); this.tflTr.Controls.Add(this.lblTrPort, 0, 4);
            this.numTrPort = MakeNum(120); this.numTrPort.Name = "numTrPort"; this.tflTr.Controls.Add(this.numTrPort, 1, 4);

            this.lblTrInterval = MakeLabel("lblTrInterval", "检查间隔(秒)"); this.tflTr.Controls.Add(this.lblTrInterval, 0, 5);
            this.numTrInterval = MakeNum(120); this.numTrInterval.Name = "numTrInterval"; this.tflTr.Controls.Add(this.numTrInterval, 1, 5);

            this.lblTrHang = MakeLabel("lblTrHang", "最大挂起次数"); this.tflTr.Controls.Add(this.lblTrHang, 0, 6);
            this.numTrHang = MakeNum(120); this.numTrHang.Name = "numTrHang"; this.tflTr.Controls.Add(this.numTrHang, 1, 6);

            this.lblTrWork = MakeLabel("lblTrWork", "连续工作(分)"); this.tflTr.Controls.Add(this.lblTrWork, 0, 7);
            this.numTrWork = MakeNum(120); this.numTrWork.Name = "numTrWork"; this.tflTr.Controls.Add(this.numTrWork, 1, 7);

            this.lblTrRest = MakeLabel("lblTrRest", "休息时长(分)"); this.tflTr.Controls.Add(this.lblTrRest, 0, 8);
            this.numTrRest = MakeNum(120); this.numTrRest.Name = "numTrRest"; this.tflTr.Controls.Add(this.numTrRest, 1, 8);

            this.chkTrClearCache.AutoSize = true; this.chkTrClearCache.Checked = true; this.chkTrClearCache.CheckState = CheckState.Checked;
            this.chkTrClearCache.Name = "chkTrClearCache"; this.chkTrClearCache.Text = "启动前清空缓存目录"; this.chkTrClearCache.Anchor = AnchorStyles.Top | AnchorStyles.Left; this.chkTrClearCache.Margin = new Padding(3, 8, 3, 3);
            this.tflTr.Controls.Add(this.chkTrClearCache, 1, 9); this.tflTr.SetColumnSpan(this.chkTrClearCache, 2);

            // grpFrame：多行 TableLayout + 任务队列面板
            this.grpFrame.Dock = DockStyle.Fill; this.grpFrame.Name = "grpFrame"; this.grpFrame.Text = "单帧调度参数";
            this.grpFrame.Visible = false; this.grpFrame.Padding = new Padding(10, 6, 10, 8);
            this.grpFrame.Controls.Add(this.pnlFrameScroll);

            this.pnlFrameScroll.Dock = DockStyle.Fill; this.pnlFrameScroll.AutoScroll = true; this.pnlFrameScroll.Name = "pnlFrameScroll";
            this.pnlFrameScroll.Controls.Add(this.tflFrame);

            this.tflFrame.Dock = DockStyle.Top; this.tflFrame.Name = "tflFrame"; this.tflFrame.AutoSize = true;
            this.tflFrame.ColumnCount = 3;
            this.tflFrame.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 165F)); // 标签列固定宽，使文本框/数字框/下拉统一从 x=150 起（中英对齐）
            this.tflFrame.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            this.tflFrame.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            for (int i = 0; i < 14; i++) this.tflFrame.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            this.tflFrame.RowStyles[7] = new RowStyle(SizeType.Absolute, 140F); // 任务列表行

            this.lblFrExe = MakeLabel("lblFrExe", "主程序路径"); this.tflFrame.Controls.Add(this.lblFrExe, 0, 0);
            this.txtFrExe = MakeText(); this.tflFrame.Controls.Add(this.txtFrExe, 1, 0);
            this.btnFrExe = MakeBrowse(); this.btnFrExe.Name = "btnFrExe"; this.tflFrame.Controls.Add(this.btnFrExe, 2, 0);

            this.lblFrProc = MakeLabel("lblFrProc", "进程名称"); this.tflFrame.Controls.Add(this.lblFrProc, 0, 1);
            this.txtFrProc = MakeText(); this.tflFrame.Controls.Add(this.txtFrProc, 1, 1); this.tflFrame.SetColumnSpan(this.txtFrProc, 2);

            this.lblFrReport = MakeLabel("lblFrReport", "异常记录文件"); this.tflFrame.Controls.Add(this.lblFrReport, 0, 2);
            this.txtFrReport = MakeText(); this.tflFrame.Controls.Add(this.txtFrReport, 1, 2);
            this.btnFrReport = MakeBrowse(); this.btnFrReport.Name = "btnFrReport"; this.tflFrame.Controls.Add(this.btnFrReport, 2, 2);

            // 冷却 + 检查间隔：2×2 第一行（普通 Panel + 绝对坐标，确定性渲染）
            var pnlPair1 = new Panel(); pnlPair1.Name = "pnlPair1"; pnlPair1.AutoSize = false; pnlPair1.Size = new Size(540, 28); pnlPair1.Margin = new Padding(3, 4, 3, 3);
            this.lblFrCooldown = MakeLabel("lblFrCooldown", "帧间冷却(秒)"); this.lblFrCooldown.Location = new Point(0, 6); this.lblFrCooldown.Margin = new Padding(0);
            this.numFrCooldown = MakeNum(90); this.numFrCooldown.Name = "numFrCooldown"; this.numFrCooldown.Location = new Point(165, 3); this.numFrCooldown.Margin = new Padding(0);
            this.lblFrInterval = MakeLabel("lblFrInterval", "检查间隔(秒)"); this.lblFrInterval.Location = new Point(275, 6); this.lblFrInterval.Margin = new Padding(0);
            this.numFrInterval = MakeNum(90); this.numFrInterval.Name = "numFrInterval"; this.numFrInterval.Location = new Point(445, 3); this.numFrInterval.Margin = new Padding(0);
            pnlPair1.Controls.Add(this.lblFrCooldown); pnlPair1.Controls.Add(this.numFrCooldown);
            pnlPair1.Controls.Add(this.lblFrInterval); pnlPair1.Controls.Add(this.numFrInterval);
            this.tflFrame.Controls.Add(pnlPair1, 0, 3); this.tflFrame.SetColumnSpan(pnlPair1, 3);

            // 最大异常 + 帧最大失败：2×2 第二行
            var pnlPair2 = new Panel(); pnlPair2.Name = "pnlPair2"; pnlPair2.AutoSize = false; pnlPair2.Size = new Size(540, 28); pnlPair2.Margin = new Padding(3, 4, 3, 3);
            this.lblFrRetry = MakeLabel("lblFrRetry", "最大异常次数"); this.lblFrRetry.Location = new Point(0, 6); this.lblFrRetry.Margin = new Padding(0);
            this.numFrRetry = MakeNum(90); this.numFrRetry.Name = "numFrRetry"; this.numFrRetry.Location = new Point(165, 3); this.numFrRetry.Margin = new Padding(0);
            this.lblFrFail = MakeLabel("lblFrFail", "帧最大失败"); this.lblFrFail.Location = new Point(275, 6); this.lblFrFail.Margin = new Padding(0);
            this.numFrFail = MakeNum(90); this.numFrFail.Name = "numFrFail"; this.numFrFail.Location = new Point(445, 3); this.numFrFail.Margin = new Padding(0);
            pnlPair2.Controls.Add(this.lblFrRetry); pnlPair2.Controls.Add(this.numFrRetry);
            pnlPair2.Controls.Add(this.lblFrFail); pnlPair2.Controls.Add(this.numFrFail);
            this.tflFrame.Controls.Add(pnlPair2, 0, 4); this.tflFrame.SetColumnSpan(pnlPair2, 3);

            // 失败时
            this.lblFrOnFail = MakeLabel("lblFrOnFail", "失败时"); this.tflFrame.Controls.Add(this.lblFrOnFail, 0, 5);
            this.cmbFrOnFail.DropDownStyle = ComboBoxStyle.DropDownList; this.cmbFrOnFail.FormattingEnabled = true;
            this.cmbFrOnFail.Items.AddRange(new object[] { "停止并告警", "跳过继续" }); this.cmbFrOnFail.Name = "cmbFrOnFail";
            this.cmbFrOnFail.Size = new Size(180, 25); this.cmbFrOnFail.Anchor = AnchorStyles.Top | AnchorStyles.Left; this.cmbFrOnFail.Margin = new Padding(3, 6, 3, 3);
            this.tflFrame.Controls.Add(this.cmbFrOnFail, 1, 5);

            this.lblTaskList = MakeHeader("lblTaskList", "任务队列"); this.tflFrame.Controls.Add(this.lblTaskList, 0, 6); this.tflFrame.SetColumnSpan(this.lblTaskList, 3);

            // 任务列表面板（列表填充 + 右侧竖排按钮），row 7 固定高
            this.pnlTaskList.Dock = DockStyle.Fill; this.pnlTaskList.Name = "pnlTaskList"; this.pnlTaskList.Margin = new Padding(3, 3, 3, 6);
            this.tflFrame.Controls.Add(this.pnlTaskList, 0, 7); this.tflFrame.SetColumnSpan(this.pnlTaskList, 3);
            this.lstTasks.Dock = DockStyle.Fill; this.lstTasks.Name = "lstTasks"; this.lstTasks.IntegralHeight = false; this.lstTasks.SelectionMode = SelectionMode.One;
            this.tflTaskBtns.Dock = DockStyle.Right; this.tflTaskBtns.Name = "tflTaskBtns"; this.tflTaskBtns.RowCount = 4; this.tflTaskBtns.ColumnCount = 1;
            this.tflTaskBtns.AutoSize = true; this.tflTaskBtns.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            this.tflTaskBtns.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            for (int i = 0; i < 4; i++) this.tflTaskBtns.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            this.btnTaskAdd = MakeToolBtn("btnTaskAdd", "+"); this.btnTaskRemove = MakeToolBtn("btnTaskRemove", "-");
            this.btnTaskUp = MakeToolBtn("btnTaskUp", "↑"); this.btnTaskDown = MakeToolBtn("btnTaskDown", "↓");
            this.tflTaskBtns.Controls.Add(this.btnTaskAdd, 0, 0);
            this.tflTaskBtns.Controls.Add(this.btnTaskRemove, 0, 1);
            this.tflTaskBtns.Controls.Add(this.btnTaskUp, 0, 2);
            this.tflTaskBtns.Controls.Add(this.btnTaskDown, 0, 3);
            this.pnlTaskList.Controls.Add(this.lstTasks);
            this.pnlTaskList.Controls.Add(this.tflTaskBtns);
            this.lstTasks.BringToFront();

            this.lblTaskDetail = MakeHeader("lblTaskDetail", "选中任务参数"); this.tflFrame.Controls.Add(this.lblTaskDetail, 0, 8); this.tflFrame.SetColumnSpan(this.lblTaskDetail, 3);

            this.lblFrScene = MakeLabel("lblFrScene", "工程文件"); this.tflFrame.Controls.Add(this.lblFrScene, 0, 9);
            this.txtFrScene = MakeText(); this.tflFrame.Controls.Add(this.txtFrScene, 1, 9);
            this.btnFrScene = MakeBrowse(); this.btnFrScene.Name = "btnFrScene"; this.tflFrame.Controls.Add(this.btnFrScene, 2, 9);

            this.lblFrOutput = MakeLabel("lblFrOutput", "输出模板"); this.tflFrame.Controls.Add(this.lblFrOutput, 0, 10);
            this.txtFrOutput = MakeText(); this.tflFrame.Controls.Add(this.txtFrOutput, 1, 10);
            this.btnFrOutput = MakeBrowse(); this.btnFrOutput.Name = "btnFrOutput"; this.tflFrame.Controls.Add(this.btnFrOutput, 2, 10);

            // 帧范围行：普通 Panel + 绝对坐标，加大 ~ 与输入框间距，row 11
            this.lblFrStart = MakeLabel("lblFrStart", "帧范围(起~止)"); this.tflFrame.Controls.Add(this.lblFrStart, 0, 11);
            this.pnlRange.Name = "pnlRange"; this.pnlRange.AutoSize = false; this.pnlRange.Size = new Size(380, 28); this.pnlRange.Margin = new Padding(0, 4, 0, 0);
            this.numFrStart = MakeNum(80); this.numFrStart.Name = "numFrStart"; this.numFrStart.Location = new Point(0, 2);
            this.lblFrEnd.AutoSize = true; this.lblFrEnd.Name = "lblFrEnd"; this.lblFrEnd.Text = "~"; this.lblFrEnd.Location = new Point(92, 5);
            this.numFrEnd = MakeNum(80); this.numFrEnd.Name = "numFrEnd"; this.numFrEnd.Location = new Point(116, 2);
            this.lblRangeCount.AutoSize = true; this.lblRangeCount.ForeColor = Color.Gray; this.lblRangeCount.Name = "lblRangeCount"; this.lblRangeCount.Text = "共 0 帧"; this.lblRangeCount.Location = new Point(210, 5);
            this.pnlRange.Controls.Add(this.numFrStart);
            this.pnlRange.Controls.Add(this.lblFrEnd);
            this.pnlRange.Controls.Add(this.numFrEnd);
            this.pnlRange.Controls.Add(this.lblRangeCount);
            this.tflFrame.Controls.Add(this.pnlRange, 1, 11); this.tflFrame.SetColumnSpan(this.pnlRange, 2);

            // 最大分块 + 无进展超时：并排一行（普通 Panel + 绝对坐标），row 12
            var pnlPair3 = new Panel(); pnlPair3.Name = "pnlPair3"; pnlPair3.AutoSize = false; pnlPair3.Size = new Size(540, 28); pnlPair3.Margin = new Padding(3, 4, 3, 3);
            this.lblMaxChunk = MakeLabel("lblMaxChunk", "最大分块长度"); this.lblMaxChunk.Location = new Point(0, 6); this.lblMaxChunk.Margin = new Padding(0);
            this.numMaxChunk = MakeNum(90); this.numMaxChunk.Name = "numMaxChunk"; this.numMaxChunk.Location = new Point(165, 3); this.numMaxChunk.Margin = new Padding(0);
            this.lblFrTimeout = MakeLabel("lblFrTimeout", "无进展超时(秒)"); this.lblFrTimeout.Location = new Point(275, 6); this.lblFrTimeout.Margin = new Padding(0);
            this.numFrTimeout = MakeNum(90); this.numFrTimeout.Name = "numFrTimeout"; this.numFrTimeout.Location = new Point(445, 3); this.numFrTimeout.Margin = new Padding(0);
            pnlPair3.Controls.Add(this.lblMaxChunk); pnlPair3.Controls.Add(this.numMaxChunk);
            pnlPair3.Controls.Add(this.lblFrTimeout); pnlPair3.Controls.Add(this.numFrTimeout);
            this.tflFrame.Controls.Add(pnlPair3, 0, 12); this.tflFrame.SetColumnSpan(pnlPair3, 3);

            this.btnFrPreview.AutoSize = true; this.btnFrPreview.Name = "btnFrPreview"; this.btnFrPreview.Text = "预览输出文件名并校验目录";
            this.btnFrPreview.UseVisualStyleBackColor = true; this.btnFrPreview.Anchor = AnchorStyles.Top | AnchorStyles.Left; this.btnFrPreview.Margin = new Padding(3, 8, 3, 3);
            this.tflFrame.Controls.Add(this.btnFrPreview, 1, 13); this.tflFrame.SetColumnSpan(this.btnFrPreview, 2);

            // panelLog：顶部 Padding 预留标题行，标题/清空按钮作锚定子控件，rtbLog 用 Dock.Fill 落在 Padding 之下
            // （不依赖子面板 z-order，首帧即对齐，无需缩放窗口才归位）
            this.panelLog.Dock = DockStyle.Fill;
            this.panelLog.Padding = new Padding(8, 34, 8, 8);
            this.panelLog.Name = "panelLog";
            this.panelLog.Size = new Size(532, 616);
            this.panelLog.Controls.Add(this.rtbLog);
            this.panelLog.Controls.Add(this.lblLogTitle);
            this.panelLog.Controls.Add(this.btnClearLog);

            this.lblLogTitle.AutoSize = true; this.lblLogTitle.Location = new Point(8, 8); this.lblLogTitle.Name = "lblLogTitle"; this.lblLogTitle.Text = "运行日志"; this.lblLogTitle.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            this.btnClearLog.Name = "btnClearLog"; this.btnClearLog.Size = new Size(96, 24); this.btnClearLog.Text = "清空日志"; this.btnClearLog.UseVisualStyleBackColor = true; this.btnClearLog.Location = new Point(532 - 8 - 96, 5); this.btnClearLog.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            this.rtbLog.BackColor = Color.White; this.rtbLog.Dock = DockStyle.Fill; this.rtbLog.Font = new Font("Consolas", 9F);
            this.rtbLog.Name = "rtbLog"; this.rtbLog.ReadOnly = true; this.rtbLog.ScrollBars = RichTextBoxScrollBars.Vertical; this.rtbLog.WordWrap = true;

            // MainForm
            this.AutoScaleDimensions = new SizeF(7F, 17F);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.ClientSize = new Size(1100, 672);
            this.MinimumSize = new Size(820, 520);
            this.Controls.Add(this.panelLog);
            this.Controls.Add(this.panelParams);
            this.Controls.Add(this.panelTop);
            this.Font = new Font("Microsoft YaHei UI", 9F);
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MaximizeBox = true;
            this.Name = "MainForm";
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Text = "Render Server GUI";

            ((System.ComponentModel.ISupportInitialize)(this.numTrPort)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numTrInterval)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numTrHang)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numTrWork)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numTrRest)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numFrCooldown)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numFrInterval)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numFrRetry)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numFrFail)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numFrStart)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numFrEnd)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numMaxChunk)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numFrTimeout)).EndInit();
            this.panelLog.ResumeLayout(false);
            this.pnlRange.ResumeLayout(false);
            this.pnlRange.PerformLayout();
            this.tflTaskBtns.ResumeLayout(false);
            this.pnlTaskList.ResumeLayout(false);
            this.tflFrame.ResumeLayout(false);
            this.tflFrame.PerformLayout();
            this.pnlFrameScroll.ResumeLayout(false);
            this.grpFrame.ResumeLayout(false);
            this.tflTr.ResumeLayout(false);
            this.tflTr.PerformLayout();
            this.pnlTrScroll.ResumeLayout(false);
            this.grpTeamRender.ResumeLayout(false);
            this.panelParams.ResumeLayout(false);
            this.panelTop.ResumeLayout(false);
            this.panelTop.PerformLayout();
            this.ResumeLayout(false);
        }

        #endregion

        private FlowLayoutPanel panelTop;
        private Label lblMode;
        private ComboBox cmbMode;
        private Button btnStart;
        private Button btnStop;
        private Panel lblStatusDot;
        private Label lblStatusText;
        private ProgressBar progressBar;
        private CheckBox chkKeepAwake;
        private Label lblLang;
        private ComboBox cmbLang;

        private Panel panelParams;
        private TableLayoutPanel tflTr;
        private Panel pnlTrScroll;
        private GroupBox grpTeamRender;
        private Label lblTrExe;
        private TextBox txtTrExe;
        private Button btnTrExe;
        private Label lblTrProc;
        private TextBox txtTrProc;
        private Label lblTrReport;
        private TextBox txtTrReport;
        private Button btnTrReport;
        private Label lblTrCache;
        private TextBox txtTrCache;
        private Button btnTrCache;
        private Label lblTrPort;
        private NumericUpDown numTrPort;
        private Label lblTrInterval;
        private NumericUpDown numTrInterval;
        private Label lblTrHang;
        private NumericUpDown numTrHang;
        private Label lblTrWork;
        private NumericUpDown numTrWork;
        private Label lblTrRest;
        private NumericUpDown numTrRest;
        private CheckBox chkTrClearCache;

        private GroupBox grpFrame;
        private TableLayoutPanel tflFrame;
        private Panel pnlFrameScroll;
        private Label lblFrExe;
        private TextBox txtFrExe;
        private Button btnFrExe;
        private Label lblFrProc;
        private TextBox txtFrProc;
        private Label lblFrReport;
        private TextBox txtFrReport;
        private Button btnFrReport;
        private Label lblFrCooldown;
        private NumericUpDown numFrCooldown;
        private Label lblFrInterval;
        private NumericUpDown numFrInterval;
        private Label lblFrRetry;
        private NumericUpDown numFrRetry;
        private Label lblFrFail;
        private NumericUpDown numFrFail;
        private Label lblFrOnFail;
        private ComboBox cmbFrOnFail;
        private Label lblTaskList;
        private Panel pnlTaskList;
        private ListBox lstTasks;
        private TableLayoutPanel tflTaskBtns;
        private Button btnTaskAdd;
        private Button btnTaskRemove;
        private Button btnTaskUp;
        private Button btnTaskDown;
        private Label lblTaskDetail;
        private Label lblFrScene;
        private TextBox txtFrScene;
        private Button btnFrScene;
        private Label lblFrOutput;
        private TextBox txtFrOutput;
        private Button btnFrOutput;
        private Label lblFrStart;
        private Panel pnlRange;
        private NumericUpDown numFrStart;
        private Label lblFrEnd;
        private NumericUpDown numFrEnd;
        private Label lblRangeCount;
        private Label lblMaxChunk;
        private NumericUpDown numMaxChunk;
        private Label lblFrTimeout;
        private NumericUpDown numFrTimeout;
        private Button btnFrPreview;

        private Panel panelLog;
        private RichTextBox rtbLog;
        private Label lblLogTitle;
        private Button btnClearLog;
    }
}
