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

        private void InitializeComponent()
        {
            this.panelTop = new System.Windows.Forms.Panel();
            this.lblMode = new System.Windows.Forms.Label();
            this.cmbMode = new System.Windows.Forms.ComboBox();
            this.btnStart = new System.Windows.Forms.Button();
            this.btnStop = new System.Windows.Forms.Button();
            this.lblStatusDot = new System.Windows.Forms.Panel();
            this.lblStatusText = new System.Windows.Forms.Label();
            this.progressBar = new System.Windows.Forms.ProgressBar();

            this.panelParams = new System.Windows.Forms.Panel();
            this.grpTeamRender = new System.Windows.Forms.GroupBox();
            this.lblTrExe = new System.Windows.Forms.Label();
            this.txtTrExe = new System.Windows.Forms.TextBox();
            this.btnTrExe = new System.Windows.Forms.Button();
            this.lblTrProc = new System.Windows.Forms.Label();
            this.txtTrProc = new System.Windows.Forms.TextBox();
            this.lblTrPort = new System.Windows.Forms.Label();
            this.numTrPort = new System.Windows.Forms.NumericUpDown();
            this.lblTrInterval = new System.Windows.Forms.Label();
            this.numTrInterval = new System.Windows.Forms.NumericUpDown();
            this.lblTrHang = new System.Windows.Forms.Label();
            this.numTrHang = new System.Windows.Forms.NumericUpDown();
            this.lblTrWork = new System.Windows.Forms.Label();
            this.numTrWork = new System.Windows.Forms.NumericUpDown();
            this.lblTrRest = new System.Windows.Forms.Label();
            this.numTrRest = new System.Windows.Forms.NumericUpDown();
            this.lblTrReport = new System.Windows.Forms.Label();
            this.txtTrReport = new System.Windows.Forms.TextBox();
            this.btnTrReport = new System.Windows.Forms.Button();
            this.lblTrCache = new System.Windows.Forms.Label();
            this.txtTrCache = new System.Windows.Forms.TextBox();
            this.btnTrCache = new System.Windows.Forms.Button();
            this.chkTrClearCache = new System.Windows.Forms.CheckBox();

            this.grpFrame = new System.Windows.Forms.GroupBox();
            this.lblFrExe = new System.Windows.Forms.Label();
            this.txtFrExe = new System.Windows.Forms.TextBox();
            this.btnFrExe = new System.Windows.Forms.Button();
            this.lblFrScene = new System.Windows.Forms.Label();
            this.txtFrScene = new System.Windows.Forms.TextBox();
            this.btnFrScene = new System.Windows.Forms.Button();
            this.btnFrPreview = new System.Windows.Forms.Button();
            this.lblFrProc = new System.Windows.Forms.Label();
            this.txtFrProc = new System.Windows.Forms.TextBox();
            this.lblFrReport = new System.Windows.Forms.Label();
            this.txtFrReport = new System.Windows.Forms.TextBox();
            this.btnFrReport = new System.Windows.Forms.Button();
            this.lblFrOutput = new System.Windows.Forms.Label();
            this.txtFrOutput = new System.Windows.Forms.TextBox();
            this.btnFrOutput = new System.Windows.Forms.Button();
            this.lblFrStart = new System.Windows.Forms.Label();
            this.numFrStart = new System.Windows.Forms.NumericUpDown();
            this.lblFrEnd = new System.Windows.Forms.Label();
            this.numFrEnd = new System.Windows.Forms.NumericUpDown();
            this.lblFrCooldown = new System.Windows.Forms.Label();
            this.numFrCooldown = new System.Windows.Forms.NumericUpDown();
            this.lblFrTimeout = new System.Windows.Forms.Label();
            this.numFrTimeout = new System.Windows.Forms.NumericUpDown();
            this.lblFrInterval = new System.Windows.Forms.Label();
            this.numFrInterval = new System.Windows.Forms.NumericUpDown();
            this.lblFrRetry = new System.Windows.Forms.Label();
            this.numFrRetry = new System.Windows.Forms.NumericUpDown();
            this.lblFrOnFail = new System.Windows.Forms.Label();
            this.cmbFrOnFail = new System.Windows.Forms.ComboBox();

            this.panelLog = new System.Windows.Forms.Panel();
            this.lblLogTitle = new System.Windows.Forms.Label();
            this.btnClearLog = new System.Windows.Forms.Button();
            this.rtbLog = new System.Windows.Forms.RichTextBox();

            this.panelTop.SuspendLayout();
            this.panelParams.SuspendLayout();
            this.grpTeamRender.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numTrPort)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numTrInterval)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numTrHang)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numTrWork)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numTrRest)).BeginInit();
            this.grpFrame.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numFrStart)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numFrEnd)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numFrCooldown)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numFrTimeout)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numFrInterval)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numFrRetry)).BeginInit();
            this.panelLog.SuspendLayout();
            this.SuspendLayout();

            // panelTop
            this.panelTop.Controls.Add(this.lblMode);
            this.panelTop.Controls.Add(this.cmbMode);
            this.panelTop.Controls.Add(this.btnStart);
            this.panelTop.Controls.Add(this.btnStop);
            this.panelTop.Controls.Add(this.lblStatusDot);
            this.panelTop.Controls.Add(this.lblStatusText);
            this.panelTop.Controls.Add(this.progressBar);
            this.panelTop.Location = new System.Drawing.Point(0, 0);
            this.panelTop.Name = "panelTop";
            this.panelTop.Size = new System.Drawing.Size(940, 56);

            // lblMode
            this.lblMode.AutoSize = true;
            this.lblMode.Location = new System.Drawing.Point(12, 20);
            this.lblMode.Name = "lblMode";
            this.lblMode.Text = "模式";

            // cmbMode
            this.cmbMode.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbMode.FormattingEnabled = true;
            this.cmbMode.Items.AddRange(new object[] {
                "Team Render（看门狗）",
                "Cinema 4D（单帧）",
                "Commandline（单帧）"});
            this.cmbMode.Location = new System.Drawing.Point(52, 16);
            this.cmbMode.Name = "cmbMode";
            this.cmbMode.Size = new System.Drawing.Size(190, 25);

            // btnStart
            this.btnStart.Location = new System.Drawing.Point(258, 14);
            this.btnStart.Name = "btnStart";
            this.btnStart.Size = new System.Drawing.Size(80, 28);
            this.btnStart.Text = "启动";
            this.btnStart.UseVisualStyleBackColor = true;

            // btnStop
            this.btnStop.Enabled = false;
            this.btnStop.Location = new System.Drawing.Point(344, 14);
            this.btnStop.Name = "btnStop";
            this.btnStop.Size = new System.Drawing.Size(80, 28);
            this.btnStop.Text = "停止";
            this.btnStop.UseVisualStyleBackColor = true;

            // lblStatusDot
            this.lblStatusDot.BackColor = System.Drawing.Color.Gray;
            this.lblStatusDot.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.lblStatusDot.Location = new System.Drawing.Point(444, 19);
            this.lblStatusDot.Name = "lblStatusDot";
            this.lblStatusDot.Size = new System.Drawing.Size(18, 18);

            // lblStatusText
            this.lblStatusText.AutoSize = true;
            this.lblStatusText.Location = new System.Drawing.Point(470, 20);
            this.lblStatusText.Name = "lblStatusText";
            this.lblStatusText.Text = "已停止";

            // progressBar
            this.progressBar.Location = new System.Drawing.Point(620, 18);
            this.progressBar.Name = "progressBar";
            this.progressBar.Size = new System.Drawing.Size(300, 20);
            this.progressBar.Visible = false;

            // panelParams
            this.panelParams.AutoScroll = true;
            this.panelParams.Controls.Add(this.grpTeamRender);
            this.panelParams.Controls.Add(this.grpFrame);
            this.panelParams.Location = new System.Drawing.Point(0, 56);
            this.panelParams.Name = "panelParams";
            this.panelParams.Size = new System.Drawing.Size(484, 428);

            // grpTeamRender
            this.grpTeamRender.Controls.Add(this.lblTrExe);
            this.grpTeamRender.Controls.Add(this.txtTrExe);
            this.grpTeamRender.Controls.Add(this.btnTrExe);
            this.grpTeamRender.Controls.Add(this.lblTrProc);
            this.grpTeamRender.Controls.Add(this.txtTrProc);
            this.grpTeamRender.Controls.Add(this.lblTrPort);
            this.grpTeamRender.Controls.Add(this.numTrPort);
            this.grpTeamRender.Controls.Add(this.lblTrInterval);
            this.grpTeamRender.Controls.Add(this.numTrInterval);
            this.grpTeamRender.Controls.Add(this.lblTrHang);
            this.grpTeamRender.Controls.Add(this.numTrHang);
            this.grpTeamRender.Controls.Add(this.lblTrWork);
            this.grpTeamRender.Controls.Add(this.numTrWork);
            this.grpTeamRender.Controls.Add(this.lblTrRest);
            this.grpTeamRender.Controls.Add(this.numTrRest);
            this.grpTeamRender.Controls.Add(this.lblTrReport);
            this.grpTeamRender.Controls.Add(this.txtTrReport);
            this.grpTeamRender.Controls.Add(this.btnTrReport);
            this.grpTeamRender.Controls.Add(this.lblTrCache);
            this.grpTeamRender.Controls.Add(this.txtTrCache);
            this.grpTeamRender.Controls.Add(this.btnTrCache);
            this.grpTeamRender.Controls.Add(this.chkTrClearCache);
            this.grpTeamRender.Location = new System.Drawing.Point(8, 6);
            this.grpTeamRender.Name = "grpTeamRender";
            this.grpTeamRender.Size = new System.Drawing.Size(470, 420);
            this.grpTeamRender.Text = "Team Render 参数";

            // Team Render rows
            this.lblTrExe.AutoSize = true; this.lblTrExe.Location = new System.Drawing.Point(14, 32); this.lblTrExe.Name = "lblTrExe"; this.lblTrExe.Text = "主程序路径";
            this.txtTrExe.Location = new System.Drawing.Point(150, 28); this.txtTrExe.Name = "txtTrExe"; this.txtTrExe.Size = new System.Drawing.Size(270, 23);
            this.btnTrExe.Location = new System.Drawing.Point(426, 27); this.btnTrExe.Name = "btnTrExe"; this.btnTrExe.Size = new System.Drawing.Size(30, 25); this.btnTrExe.Text = "…"; this.btnTrExe.UseVisualStyleBackColor = true;

            this.lblTrProc.AutoSize = true; this.lblTrProc.Location = new System.Drawing.Point(14, 64); this.lblTrProc.Name = "lblTrProc"; this.lblTrProc.Text = "进程名称";
            this.txtTrProc.Location = new System.Drawing.Point(150, 60); this.txtTrProc.Name = "txtTrProc"; this.txtTrProc.Size = new System.Drawing.Size(306, 23);

            this.lblTrReport.AutoSize = true; this.lblTrReport.Location = new System.Drawing.Point(14, 96); this.lblTrReport.Name = "lblTrReport"; this.lblTrReport.Text = "异常记录文件";
            this.txtTrReport.Location = new System.Drawing.Point(150, 92); this.txtTrReport.Name = "txtTrReport"; this.txtTrReport.Size = new System.Drawing.Size(270, 23);
            this.btnTrReport.Location = new System.Drawing.Point(426, 91); this.btnTrReport.Name = "btnTrReport"; this.btnTrReport.Size = new System.Drawing.Size(30, 25); this.btnTrReport.Text = "…"; this.btnTrReport.UseVisualStyleBackColor = true;

            this.lblTrCache.AutoSize = true; this.lblTrCache.Location = new System.Drawing.Point(14, 128); this.lblTrCache.Name = "lblTrCache"; this.lblTrCache.Text = "缓存目录";
            this.txtTrCache.Location = new System.Drawing.Point(150, 124); this.txtTrCache.Name = "txtTrCache"; this.txtTrCache.Size = new System.Drawing.Size(270, 23);
            this.btnTrCache.Location = new System.Drawing.Point(426, 123); this.btnTrCache.Name = "btnTrCache"; this.btnTrCache.Size = new System.Drawing.Size(30, 25); this.btnTrCache.Text = "…"; this.btnTrCache.UseVisualStyleBackColor = true;

            this.lblTrPort.AutoSize = true; this.lblTrPort.Location = new System.Drawing.Point(14, 160); this.lblTrPort.Name = "lblTrPort"; this.lblTrPort.Text = "端口";
            this.numTrPort.Location = new System.Drawing.Point(150, 156); this.numTrPort.Name = "numTrPort"; this.numTrPort.Size = new System.Drawing.Size(100, 23);

            this.lblTrInterval.AutoSize = true; this.lblTrInterval.Location = new System.Drawing.Point(14, 192); this.lblTrInterval.Name = "lblTrInterval"; this.lblTrInterval.Text = "检查间隔(秒)";
            this.numTrInterval.Location = new System.Drawing.Point(150, 188); this.numTrInterval.Name = "numTrInterval"; this.numTrInterval.Size = new System.Drawing.Size(100, 23);

            this.lblTrHang.AutoSize = true; this.lblTrHang.Location = new System.Drawing.Point(14, 224); this.lblTrHang.Name = "lblTrHang"; this.lblTrHang.Text = "最大挂起次数";
            this.numTrHang.Location = new System.Drawing.Point(150, 220); this.numTrHang.Name = "numTrHang"; this.numTrHang.Size = new System.Drawing.Size(100, 23);

            this.lblTrWork.AutoSize = true; this.lblTrWork.Location = new System.Drawing.Point(14, 256); this.lblTrWork.Name = "lblTrWork"; this.lblTrWork.Text = "连续工作(分)";
            this.numTrWork.Location = new System.Drawing.Point(150, 252); this.numTrWork.Name = "numTrWork"; this.numTrWork.Size = new System.Drawing.Size(100, 23);

            this.lblTrRest.AutoSize = true; this.lblTrRest.Location = new System.Drawing.Point(14, 288); this.lblTrRest.Name = "lblTrRest"; this.lblTrRest.Text = "休息时长(分)";
            this.numTrRest.Location = new System.Drawing.Point(150, 284); this.numTrRest.Name = "numTrRest"; this.numTrRest.Size = new System.Drawing.Size(100, 23);

            this.chkTrClearCache.AutoSize = true; this.chkTrClearCache.Checked = true; this.chkTrClearCache.CheckState = System.Windows.Forms.CheckState.Checked;
            this.chkTrClearCache.Location = new System.Drawing.Point(150, 320); this.chkTrClearCache.Name = "chkTrClearCache"; this.chkTrClearCache.Text = "启动前清空缓存目录";

            // grpFrame
            this.grpFrame.Controls.Add(this.lblFrExe);
            this.grpFrame.Controls.Add(this.txtFrExe);
            this.grpFrame.Controls.Add(this.btnFrExe);
            this.grpFrame.Controls.Add(this.lblFrScene);
            this.grpFrame.Controls.Add(this.txtFrScene);
            this.grpFrame.Controls.Add(this.btnFrScene);
            this.grpFrame.Controls.Add(this.btnFrPreview);
            this.grpFrame.Controls.Add(this.lblFrProc);
            this.grpFrame.Controls.Add(this.txtFrProc);
            this.grpFrame.Controls.Add(this.lblFrReport);
            this.grpFrame.Controls.Add(this.txtFrReport);
            this.grpFrame.Controls.Add(this.btnFrReport);
            this.grpFrame.Controls.Add(this.lblFrOutput);
            this.grpFrame.Controls.Add(this.txtFrOutput);
            this.grpFrame.Controls.Add(this.btnFrOutput);
            this.grpFrame.Controls.Add(this.lblFrStart);
            this.grpFrame.Controls.Add(this.numFrStart);
            this.grpFrame.Controls.Add(this.lblFrEnd);
            this.grpFrame.Controls.Add(this.numFrEnd);
            this.grpFrame.Controls.Add(this.lblFrCooldown);
            this.grpFrame.Controls.Add(this.numFrCooldown);
            this.grpFrame.Controls.Add(this.lblFrTimeout);
            this.grpFrame.Controls.Add(this.numFrTimeout);
            this.grpFrame.Controls.Add(this.lblFrInterval);
            this.grpFrame.Controls.Add(this.numFrInterval);
            this.grpFrame.Controls.Add(this.lblFrRetry);
            this.grpFrame.Controls.Add(this.numFrRetry);
            this.grpFrame.Controls.Add(this.lblFrOnFail);
            this.grpFrame.Controls.Add(this.cmbFrOnFail);
            this.grpFrame.Location = new System.Drawing.Point(8, 6);
            this.grpFrame.Name = "grpFrame";
            this.grpFrame.Size = new System.Drawing.Size(470, 420);
            this.grpFrame.Text = "单帧调度参数";
            this.grpFrame.Visible = false;

            // Frame rows
            this.lblFrExe.AutoSize = true; this.lblFrExe.Location = new System.Drawing.Point(14, 32); this.lblFrExe.Name = "lblFrExe"; this.lblFrExe.Text = "主程序路径";
            this.txtFrExe.Location = new System.Drawing.Point(150, 28); this.txtFrExe.Name = "txtFrExe"; this.txtFrExe.Size = new System.Drawing.Size(270, 23);
            this.btnFrExe.Location = new System.Drawing.Point(426, 27); this.btnFrExe.Name = "btnFrExe"; this.btnFrExe.Size = new System.Drawing.Size(30, 25); this.btnFrExe.Text = "…"; this.btnFrExe.UseVisualStyleBackColor = true;

            this.lblFrProc.AutoSize = true; this.lblFrProc.Location = new System.Drawing.Point(14, 64); this.lblFrProc.Name = "lblFrProc"; this.lblFrProc.Text = "进程名称";
            this.txtFrProc.Location = new System.Drawing.Point(150, 60); this.txtFrProc.Name = "txtFrProc"; this.txtFrProc.Size = new System.Drawing.Size(306, 23);

            this.lblFrReport.AutoSize = true; this.lblFrReport.Location = new System.Drawing.Point(14, 96); this.lblFrReport.Name = "lblFrReport"; this.lblFrReport.Text = "异常记录文件";
            this.txtFrReport.Location = new System.Drawing.Point(150, 92); this.txtFrReport.Name = "txtFrReport"; this.txtFrReport.Size = new System.Drawing.Size(270, 23);
            this.btnFrReport.Location = new System.Drawing.Point(426, 91); this.btnFrReport.Name = "btnFrReport"; this.btnFrReport.Size = new System.Drawing.Size(30, 25); this.btnFrReport.Text = "…"; this.btnFrReport.UseVisualStyleBackColor = true;

            this.lblFrScene.AutoSize = true; this.lblFrScene.Location = new System.Drawing.Point(14, 128); this.lblFrScene.Name = "lblFrScene"; this.lblFrScene.Text = "工程文件";
            this.txtFrScene.Location = new System.Drawing.Point(150, 124); this.txtFrScene.Name = "txtFrScene"; this.txtFrScene.Size = new System.Drawing.Size(270, 23);
            this.btnFrScene.Location = new System.Drawing.Point(426, 123); this.btnFrScene.Name = "btnFrScene"; this.btnFrScene.Size = new System.Drawing.Size(30, 25); this.btnFrScene.Text = "…"; this.btnFrScene.UseVisualStyleBackColor = true;

            this.lblFrOutput.AutoSize = true; this.lblFrOutput.Location = new System.Drawing.Point(14, 160); this.lblFrOutput.Name = "lblFrOutput"; this.lblFrOutput.Text = "输出模板";
            this.txtFrOutput.Location = new System.Drawing.Point(150, 156); this.txtFrOutput.Name = "txtFrOutput"; this.txtFrOutput.Size = new System.Drawing.Size(270, 23);
            this.btnFrOutput.Location = new System.Drawing.Point(426, 155); this.btnFrOutput.Name = "btnFrOutput"; this.btnFrOutput.Size = new System.Drawing.Size(30, 25); this.btnFrOutput.Text = "…"; this.btnFrOutput.UseVisualStyleBackColor = true;

            this.lblFrStart.AutoSize = true; this.lblFrStart.Location = new System.Drawing.Point(14, 192); this.lblFrStart.Name = "lblFrStart"; this.lblFrStart.Text = "帧范围(起-止)";
            this.numFrStart.Location = new System.Drawing.Point(150, 188); this.numFrStart.Name = "numFrStart"; this.numFrStart.Size = new System.Drawing.Size(80, 23);
            this.lblFrEnd.AutoSize = true; this.lblFrEnd.Location = new System.Drawing.Point(240, 192); this.lblFrEnd.Name = "lblFrEnd"; this.lblFrEnd.Text = "–";
            this.numFrEnd.Location = new System.Drawing.Point(256, 188); this.numFrEnd.Name = "numFrEnd"; this.numFrEnd.Size = new System.Drawing.Size(80, 23);

            this.lblFrCooldown.AutoSize = true; this.lblFrCooldown.Location = new System.Drawing.Point(14, 224); this.lblFrCooldown.Name = "lblFrCooldown"; this.lblFrCooldown.Text = "帧间冷却(秒)";
            this.numFrCooldown.Location = new System.Drawing.Point(150, 220); this.numFrCooldown.Name = "numFrCooldown"; this.numFrCooldown.Size = new System.Drawing.Size(100, 23);

            this.lblFrTimeout.AutoSize = true; this.lblFrTimeout.Location = new System.Drawing.Point(14, 256); this.lblFrTimeout.Name = "lblFrTimeout"; this.lblFrTimeout.Text = "单帧超时(秒)";
            this.numFrTimeout.Location = new System.Drawing.Point(150, 252); this.numFrTimeout.Name = "numFrTimeout"; this.numFrTimeout.Size = new System.Drawing.Size(100, 23);

            this.lblFrInterval.AutoSize = true; this.lblFrInterval.Location = new System.Drawing.Point(14, 288); this.lblFrInterval.Name = "lblFrInterval"; this.lblFrInterval.Text = "检查间隔(秒)";
            this.numFrInterval.Location = new System.Drawing.Point(150, 284); this.numFrInterval.Name = "numFrInterval"; this.numFrInterval.Size = new System.Drawing.Size(100, 23);

            this.lblFrRetry.AutoSize = true; this.lblFrRetry.Location = new System.Drawing.Point(14, 320); this.lblFrRetry.Name = "lblFrRetry"; this.lblFrRetry.Text = "最大重试次数";
            this.numFrRetry.Location = new System.Drawing.Point(150, 316); this.numFrRetry.Name = "numFrRetry"; this.numFrRetry.Size = new System.Drawing.Size(100, 23);

            this.lblFrOnFail.AutoSize = true; this.lblFrOnFail.Location = new System.Drawing.Point(14, 352); this.lblFrOnFail.Name = "lblFrOnFail"; this.lblFrOnFail.Text = "失败时";
            this.cmbFrOnFail.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbFrOnFail.FormattingEnabled = true;
            this.cmbFrOnFail.Items.AddRange(new object[] { "停止并告警", "跳过继续" });
            this.cmbFrOnFail.Location = new System.Drawing.Point(150, 348); this.cmbFrOnFail.Name = "cmbFrOnFail"; this.cmbFrOnFail.Size = new System.Drawing.Size(150, 25);

            this.btnFrPreview.Location = new System.Drawing.Point(150, 380); this.btnFrPreview.Name = "btnFrPreview"; this.btnFrPreview.Size = new System.Drawing.Size(300, 26);
            this.btnFrPreview.Text = "预览输出文件名并校验目录"; this.btnFrPreview.UseVisualStyleBackColor = true;

            // panelLog
            this.panelLog.Controls.Add(this.lblLogTitle);
            this.panelLog.Controls.Add(this.btnClearLog);
            this.panelLog.Controls.Add(this.rtbLog);
            this.panelLog.Location = new System.Drawing.Point(488, 56);
            this.panelLog.Name = "panelLog";
            this.panelLog.Size = new System.Drawing.Size(452, 428);

            this.lblLogTitle.AutoSize = true;
            this.lblLogTitle.Location = new System.Drawing.Point(12, 8);
            this.lblLogTitle.Name = "lblLogTitle";
            this.lblLogTitle.Text = "运行日志";

            this.btnClearLog.Location = new System.Drawing.Point(348, 6);
            this.btnClearLog.Name = "btnClearLog";
            this.btnClearLog.Size = new System.Drawing.Size(96, 24);
            this.btnClearLog.Text = "清空日志";
            this.btnClearLog.UseVisualStyleBackColor = true;

            this.rtbLog.BackColor = System.Drawing.Color.White;
            this.rtbLog.Font = new System.Drawing.Font("Consolas", 9F);
            this.rtbLog.Location = new System.Drawing.Point(12, 30);
            this.rtbLog.Name = "rtbLog";
            this.rtbLog.ReadOnly = true;
            this.rtbLog.ScrollBars = System.Windows.Forms.RichTextBoxScrollBars.Vertical;
            this.rtbLog.Size = new System.Drawing.Size(432, 390);
            this.rtbLog.WordWrap = true;

            // MainForm
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 17F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(940, 490);
            this.Controls.Add(this.panelTop);
            this.Controls.Add(this.panelParams);
            this.Controls.Add(this.panelLog);
            this.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.Name = "MainForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "Render Server GUI";

            this.panelTop.ResumeLayout(false);
            this.panelTop.PerformLayout();
            this.panelParams.ResumeLayout(false);
            this.grpTeamRender.ResumeLayout(false);
            this.grpTeamRender.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numTrPort)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numTrInterval)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numTrHang)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numTrWork)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numTrRest)).EndInit();
            this.grpFrame.ResumeLayout(false);
            this.grpFrame.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numFrStart)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numFrEnd)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numFrCooldown)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numFrTimeout)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numFrInterval)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numFrRetry)).EndInit();
            this.panelLog.ResumeLayout(false);
            this.panelLog.PerformLayout();
            this.ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.Panel panelTop;
        private System.Windows.Forms.Label lblMode;
        private System.Windows.Forms.ComboBox cmbMode;
        private System.Windows.Forms.Button btnStart;
        private System.Windows.Forms.Button btnStop;
        private System.Windows.Forms.Panel lblStatusDot;
        private System.Windows.Forms.Label lblStatusText;
        private System.Windows.Forms.ProgressBar progressBar;

        private System.Windows.Forms.Panel panelParams;
        private System.Windows.Forms.GroupBox grpTeamRender;
        private System.Windows.Forms.Label lblTrExe;
        private System.Windows.Forms.TextBox txtTrExe;
        private System.Windows.Forms.Button btnTrExe;
        private System.Windows.Forms.Label lblTrProc;
        private System.Windows.Forms.TextBox txtTrProc;
        private System.Windows.Forms.Label lblTrPort;
        private System.Windows.Forms.NumericUpDown numTrPort;
        private System.Windows.Forms.Label lblTrInterval;
        private System.Windows.Forms.NumericUpDown numTrInterval;
        private System.Windows.Forms.Label lblTrHang;
        private System.Windows.Forms.NumericUpDown numTrHang;
        private System.Windows.Forms.Label lblTrWork;
        private System.Windows.Forms.NumericUpDown numTrWork;
        private System.Windows.Forms.Label lblTrRest;
        private System.Windows.Forms.NumericUpDown numTrRest;
        private System.Windows.Forms.Label lblTrReport;
        private System.Windows.Forms.TextBox txtTrReport;
        private System.Windows.Forms.Button btnTrReport;
        private System.Windows.Forms.Label lblTrCache;
        private System.Windows.Forms.TextBox txtTrCache;
        private System.Windows.Forms.Button btnTrCache;
        private System.Windows.Forms.CheckBox chkTrClearCache;

        private System.Windows.Forms.GroupBox grpFrame;
        private System.Windows.Forms.Label lblFrExe;
        private System.Windows.Forms.TextBox txtFrExe;
        private System.Windows.Forms.Button btnFrExe;
        private System.Windows.Forms.Label lblFrScene;
        private System.Windows.Forms.TextBox txtFrScene;
        private System.Windows.Forms.Button btnFrScene;
        private System.Windows.Forms.Button btnFrPreview;
        private System.Windows.Forms.Label lblFrProc;
        private System.Windows.Forms.TextBox txtFrProc;
        private System.Windows.Forms.Label lblFrReport;
        private System.Windows.Forms.TextBox txtFrReport;
        private System.Windows.Forms.Button btnFrReport;
        private System.Windows.Forms.Label lblFrOutput;
        private System.Windows.Forms.TextBox txtFrOutput;
        private System.Windows.Forms.Button btnFrOutput;
        private System.Windows.Forms.Label lblFrStart;
        private System.Windows.Forms.NumericUpDown numFrStart;
        private System.Windows.Forms.Label lblFrEnd;
        private System.Windows.Forms.NumericUpDown numFrEnd;
        private System.Windows.Forms.Label lblFrCooldown;
        private System.Windows.Forms.NumericUpDown numFrCooldown;
        private System.Windows.Forms.Label lblFrTimeout;
        private System.Windows.Forms.NumericUpDown numFrTimeout;
        private System.Windows.Forms.Label lblFrInterval;
        private System.Windows.Forms.NumericUpDown numFrInterval;
        private System.Windows.Forms.Label lblFrRetry;
        private System.Windows.Forms.NumericUpDown numFrRetry;
        private System.Windows.Forms.Label lblFrOnFail;
        private System.Windows.Forms.ComboBox cmbFrOnFail;

        private System.Windows.Forms.Panel panelLog;
        private System.Windows.Forms.Label lblLogTitle;
        private System.Windows.Forms.Button btnClearLog;
        private System.Windows.Forms.RichTextBox rtbLog;
    }
}
