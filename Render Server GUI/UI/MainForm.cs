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
        private readonly List<string> _langCodes = new List<string>();
        /// <summary>最近一次运行状态，切换语言时据此刷新状态文字。</summary>
        private RunnerStatus _lastStatus = RunnerStatus.Stopped;

        /// <summary>当前帧模式的任务列表引用（直接操作 profile.Tasks）。</summary>
        private List<RenderTask> _tasks;
        /// <summary>是否正在程序性刷新 ListBox（抑制 SelectedIndexChanged 回写）。</summary>
        private bool _suppressTaskSelect;

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

        /// <summary>集中订阅界面事件。</summary>
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

            // 编辑区任一参数变化：写回配置并即时刷新列表项显示
            txtFrScene.TextChanged += (s, e) => OnTaskEditChanged();
            txtFrOutput.TextChanged += (s, e) => OnTaskEditChanged();
            numFrStart.ValueChanged += (s, e) => OnTaskEditChanged();
            numFrEnd.ValueChanged += (s, e) => OnTaskEditChanged();
            numMaxChunk.ValueChanged += (s, e) => OnTaskEditChanged();
            numFrTimeout.ValueChanged += (s, e) => OnTaskEditChanged();

            // 任务队列按钮
            btnTaskAdd.Click += (s, e) => OnTaskAdd();
            btnTaskRemove.Click += (s, e) => OnTaskRemove();
            btnTaskUp.Click += (s, e) => OnTaskMove(-1);
            btnTaskDown.Click += (s, e) => OnTaskMove(1);
            lstTasks.SelectedIndexChanged += (s, e) => OnTaskSelected();

            FormClosing += OnFormClosingHandler;
        }

        /// <summary>刷新"帧范围"右侧的帧数量显示。</summary>
        private void UpdateRangeCount()
        {
            long n = (long)numFrEnd.Value - (long)numFrStart.Value + 1;
            if (n < 0) n = 0;
            lblRangeCount.Text = Localizer.Tf("ui.count", "共 {0} 帧", n);
        }

        // ---------- 多语言 ----------

        /// <summary>用扫描到的语言填充下拉。</summary>
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

        /// <summary>语言下拉切换。</summary>
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

        /// <summary>把界面所有可见文字按当前语言重设。</summary>
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

            // 单帧分组 - 全局
            grpFrame.Text = Localizer.T("ui.frGroup", "单帧调度参数");
            lblFrExe.Text = Localizer.T("ui.exe", "主程序路径");
            lblFrProc.Text = Localizer.T("ui.proc", "进程名称");
            lblFrReport.Text = Localizer.T("ui.report", "异常记录文件");
            lblFrCooldown.Text = Localizer.T("ui.cooldown", "帧间冷却(秒)");
            lblFrInterval.Text = Localizer.T("ui.checkInterval", "检查间隔(秒)");
            lblFrRetry.Text = Localizer.T("ui.maxAbnormal", "最大异常次数");
            lblFrFail.Text = Localizer.T("ui.maxFail", "帧最大失败");
            lblFrOnFail.Text = Localizer.T("ui.onFail", "失败时");
            RebuildCombo(cmbFrOnFail, new[]
            {
                Localizer.T("ui.onFailStop", "停止并告警"),
                Localizer.T("ui.onFailSkip", "跳过继续")
            });

            // 单帧分组 - 任务
            lblTaskList.Text = Localizer.T("ui.taskList", "任务队列");
            btnTaskAdd.Text = Localizer.T("ui.taskAdd", "添加");
            btnTaskRemove.Text = Localizer.T("ui.taskRemove", "删除");
            lblTaskDetail.Text = Localizer.T("ui.taskDetail", "选中任务参数");
            lblFrScene.Text = Localizer.T("ui.scene", "工程文件");
            lblFrOutput.Text = Localizer.T("ui.output", "输出模板");
            lblFrStart.Text = Localizer.T("ui.range", "帧范围(起~止)");
            lblMaxChunk.Text = Localizer.T("ui.maxChunk", "最大分块长度");
            lblFrTimeout.Text = Localizer.T("ui.noProgressTimeout", "无进展超时(秒)");
            btnFrPreview.Text = Localizer.T("ui.preview", "预览输出文件名并校验目录");
            lblLogTitle.Text = Localizer.T("ui.logTitle", "运行日志");
            btnClearLog.Text = Localizer.T("ui.clearLog", "清空日志");

            SetStatusText();
            UpdateRangeCount();
            CommitTaskFromUi(); // 语言切换会重建列表，先把编辑中的参数写回，避免旧值回显
            RefreshTaskList();
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
            CommitAll();
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
                // 全局参数
                txtFrExe.Text = p.ExePath;
                txtFrProc.Text = p.ProcessName;
                txtFrReport.Text = p.ReportPath;
                numFrCooldown.Value = Clamp(p.CooldownSeconds, numFrCooldown);
                numFrInterval.Value = Clamp(p.FrameCheckIntervalSeconds, numFrInterval);
                numFrRetry.Value = Clamp(p.MaxAbnormalCount, numFrRetry);
                numFrFail.Value = Clamp(p.MaxFrameFailCount, numFrFail);
                cmbFrOnFail.SelectedIndex = p.OnFail == OnFailBehaviour.Stop ? 0 : 1;

                // 任务列表
                _tasks = p.Tasks;
                if (_tasks == null) { _tasks = new List<RenderTask>(); p.Tasks = _tasks; }
                RefreshTaskList();
                if (lstTasks.Items.Count > 0)
                {
                    _suppressTaskSelect = true;
                    lstTasks.SelectedIndex = 0;
                    _suppressTaskSelect = false;
                    LoadTaskToUi(0);
                }
                else
                {
                    SetTaskDetailEnabled(false);
                }
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

        // ---------- 任务队列 UI ----------

        /// <summary>刷新 ListBox 显示内容并保留选中。</summary>
        private void RefreshTaskList()
        {
            if (_tasks == null) return;
            int sel = lstTasks.SelectedIndex;
            _suppressTaskSelect = true;
            lstTasks.BeginUpdate();
            lstTasks.Items.Clear();
            for (int i = 0; i < _tasks.Count; i++)
                lstTasks.Items.Add($"{i + 1}.{_tasks[i].DisplayLabel}");
            if (sel >= 0 && sel < lstTasks.Items.Count) lstTasks.SelectedIndex = sel;
            else if (lstTasks.Items.Count > 0) lstTasks.SelectedIndex = 0;
            lstTasks.EndUpdate();
            _suppressTaskSelect = false;
            UpdateTaskButtons();
        }

        /// <summary>按选中状态启用/禁用任务操作按钮。</summary>
        private void UpdateTaskButtons()
        {
            int sel = lstTasks.SelectedIndex;
            bool has = sel >= 0;
            btnTaskRemove.Enabled = has;
            btnTaskUp.Enabled = has && sel > 0;
            btnTaskDown.Enabled = has && sel < lstTasks.Items.Count - 1;
            SetTaskDetailEnabled(has);
        }

        /// <summary>启用/禁用选中任务编辑区控件。</summary>
        private void SetTaskDetailEnabled(bool enabled)
        {
            txtFrScene.Enabled = enabled;
            btnFrScene.Enabled = enabled;
            txtFrOutput.Enabled = enabled;
            btnFrOutput.Enabled = enabled;
            numFrStart.Enabled = enabled;
            numFrEnd.Enabled = enabled;
            numMaxChunk.Enabled = enabled;
            numFrTimeout.Enabled = enabled;
            btnFrPreview.Enabled = enabled;
        }

        /// <summary>ListBox 选中变化：先提交旧选中项编辑，再加载新选中项。</summary>
        private void OnTaskSelected()
        {
            if (_suppressTaskSelect || !_uiReady) return;
            // 提交前一个任务的编辑（通过 Tag 记住上次选中）
            CommitTaskFromUi();
            int idx = lstTasks.SelectedIndex;
            if (idx >= 0 && idx < _tasks.Count)
                LoadTaskToUi(idx);
            UpdateTaskButtons();
        }

        /// <summary>把指定索引的任务参数加载到 UI 控件。</summary>
        private void LoadTaskToUi(int idx)
        {
            if (_tasks == null || idx < 0 || idx >= _tasks.Count) return;
            var t = _tasks[idx];
            _suppressTaskSelect = true;
            txtFrScene.Text = t.SceneFile ?? string.Empty;
            txtFrOutput.Text = t.OutputTemplate ?? string.Empty;
            numFrStart.Value = Clamp(t.StartFrame, numFrStart);
            numFrEnd.Value = Clamp(t.EndFrame, numFrEnd);
            numMaxChunk.Value = Clamp(t.MaxChunkLength, numMaxChunk);
            numFrTimeout.Value = Clamp(t.FrameTimeoutSeconds, numFrTimeout);
            _suppressTaskSelect = false;
            UpdateRangeCount();
            lstTasks.Tag = idx; // 记住当前加载的索引
        }

        /// <summary>把 UI 控件值写回当前选中的任务对象。</summary>
        private void CommitTaskFromUi()
        {
            if (_tasks == null) return;
            int idx = lstTasks.Tag is int ti ? ti : lstTasks.SelectedIndex;
            if (idx < 0 || idx >= _tasks.Count) return;
            var t = _tasks[idx];
            t.SceneFile = txtFrScene.Text.Trim();
            t.OutputTemplate = txtFrOutput.Text.Trim();
            t.StartFrame = (int)numFrStart.Value;
            t.EndFrame = (int)numFrEnd.Value;
            t.MaxChunkLength = (int)numMaxChunk.Value;
            t.FrameTimeoutSeconds = (int)numFrTimeout.Value;
        }

        /// <summary>
        /// 编辑区参数变化：立即写回当前选中任务（内存配置），并同步刷新列表项显示。
        /// 程序性赋值（LoadTaskToUi 等）经 _suppressTaskSelect 抑制，不触发。
        /// </summary>
        private void OnTaskEditChanged()
        {
            if (_suppressTaskSelect || !_uiReady) return;
            CommitTaskFromUi();
            int idx = lstTasks.Tag is int ti ? ti : lstTasks.SelectedIndex;
            if (_tasks == null || idx < 0 || idx >= _tasks.Count) return;
            _suppressTaskSelect = true;
            lstTasks.Items[idx] = $"{idx + 1}.{_tasks[idx].DisplayLabel}";
            _suppressTaskSelect = false;
        }

        /// <summary>添加任务：复制当前选中项（或空白）追加到末尾并选中；主键=现有最大值+1（删除不回收）。</summary>
        private void OnTaskAdd()
        {
            if (_tasks == null) return;
            CommitTaskFromUi();
            RenderTask newTask;
            int sel = lstTasks.SelectedIndex;
            if (sel >= 0 && sel < _tasks.Count)
                newTask = _tasks[sel].Clone();
            else
                newTask = RenderTask.CreateDefault();
            int maxId = 0;
            foreach (var t in _tasks) { if (t.TaskId > maxId) maxId = t.TaskId; }
            newTask.TaskId = maxId + 1;
            _tasks.Add(newTask);
            int newIdx = _tasks.Count - 1;
            LoadTaskToUi(newIdx);   // 先同步编辑区与 Tag，再改选中，防错位提交
            RefreshTaskList();
            lstTasks.SelectedIndex = newIdx;
            UpdateTaskButtons();
        }

        /// <summary>删除选中任务。</summary>
        private void OnTaskRemove()
        {
            if (_tasks == null) return;
            int sel = lstTasks.SelectedIndex;
            if (sel < 0 || sel >= _tasks.Count) return;
            lstTasks.Tag = -1; // 编辑区数据即将失效，抑制结构性操作期间的错位提交
            _tasks.RemoveAt(sel);
            RefreshTaskList();
            if (_tasks.Count > 0)
            {
                int newSel = Math.Min(sel, _tasks.Count - 1);
                LoadTaskToUi(newSel);
                lstTasks.SelectedIndex = newSel;
            }
            else
            {
                SetTaskDetailEnabled(false);
            }
            UpdateTaskButtons();
        }

        /// <summary>上移/下移选中任务。delta=-1 上移，+1 下移。</summary>
        private void OnTaskMove(int delta)
        {
            if (_tasks == null) return;
            int sel = lstTasks.SelectedIndex;
            int target = sel + delta;
            if (sel < 0 || target < 0 || target >= _tasks.Count) return;
            CommitTaskFromUi();
            var tmp = _tasks[sel];
            _tasks[sel] = _tasks[target];
            _tasks[target] = tmp;
            LoadTaskToUi(target);   // 先同步编辑区与 Tag 到新位置，防错位提交
            RefreshTaskList();
            lstTasks.SelectedIndex = target;
            UpdateTaskButtons();
        }

        // ---------- 提交与校验 ----------

        /// <summary>提交当前模式的全部编辑（全局+任务）到配置对象。</summary>
        private void CommitAll()
        {
            CommitGlobalProfile();
            if (IsFrameMode(_cfg.Mode))
                CommitTaskFromUi();
        }

        /// <summary>把全局参数控件值写回当前模式的 ModeProfile。</summary>
        private void CommitGlobalProfile()
        {
            RenderMode mode = _cfg.Mode;
            ModeProfile p = _cfg.ProfileOf(mode) ?? ModeProfile.ForPreset(mode);

            if (IsFrameMode(mode))
            {
                p.ExePath = txtFrExe.Text.Trim();
                p.ProcessName = txtFrProc.Text.Trim();
                p.ReportPath = txtFrReport.Text.Trim();
                p.CooldownSeconds = (int)numFrCooldown.Value;
                p.FrameCheckIntervalSeconds = (int)numFrInterval.Value;
                p.MaxAbnormalCount = (int)numFrRetry.Value;
                p.MaxFrameFailCount = (int)numFrFail.Value;
                p.OnFail = cmbFrOnFail.SelectedIndex == 1 ? OnFailBehaviour.Skip : OnFailBehaviour.Stop;
                // 同步任务列表引用
                if (_tasks != null) p.Tasks = _tasks;
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
            CommitAll();

            ModeProfile p = _cfg.ProfileOf(_cfg.Mode);
            if (!ValidateProfile(p, out string err))
            {
                AppendLog(new LogEntry(LogLevel.Error, Localizer.T("msg.badPrefix", "参数校验未通过：") + err));
                MessageBox.Show(err, Localizer.T("msg.cantStartTitle", "无法启动"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _cfg.Save(out _);

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

        /// <summary>首次启动前发现同名进程：在 UI 线程弹窗询问。</summary>
        private PreexistingChoice AskPreexisting(int count)
        {
            if (InvokeRequired)
                return (PreexistingChoice)Invoke(new Func<int, PreexistingChoice>(AskPreexisting), count);

            string name = _cfg.ProfileOf(_cfg.Mode)?.ProcessName ?? "渲染";
            string body = Localizer.Tf("dlg.preexistBody",
                "检测到已有 {0} 个「{1}」进程在运行。\n\n选\"是\"＝结束这些残留进程后再开始队列；\n选\"否\"＝停止本次调度（不启动、也不动这些进程）。",
                count, name);
            var r = MessageBox.Show(this, body,
                Localizer.T("dlg.preexistTitle", "已存在渲染进程"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            return r == DialogResult.Yes ? PreexistingChoice.KillAndStart : PreexistingChoice.Abort;
        }

        private void StopCurrent()
        {
            if (_controller != null && _controller.IsRunning)
                _controller.Stop();
        }

        /// <summary>校验参数是否可启动。</summary>
        private bool ValidateProfile(ModeProfile p, out string error)
        {
            error = null;
            if (p == null) { error = Localizer.T("msg.badNull", "未选择模式。"); return false; }
            if (string.IsNullOrWhiteSpace(p.ExePath)) { error = Localizer.T("msg.badExe", "主程序路径为空。"); return false; }
            if (string.IsNullOrWhiteSpace(p.ProcessName)) { error = Localizer.T("msg.badProc", "进程名称为空。"); return false; }
            if (string.IsNullOrWhiteSpace(p.ReportPath)) { error = Localizer.T("msg.badReport", "异常记录文件路径为空。"); return false; }

            if (IsFrameMode(_cfg.Mode))
            {
                if (p.Tasks == null || p.Tasks.Count == 0)
                {
                    error = Localizer.T("msg.noTasks", "任务队列为空，请至少添加一个任务。");
                    return false;
                }
                for (int i = 0; i < p.Tasks.Count; i++)
                {
                    var t = p.Tasks[i];
                    string prefix = Localizer.Tf("msg.taskN", "任务 {0}: ", i + 1);
                    if (string.IsNullOrWhiteSpace(t.SceneFile)) { error = prefix + Localizer.T("msg.badScene", "工程文件为空。"); return false; }
                    if (t.StartFrame > t.EndFrame) { error = prefix + Localizer.T("msg.badRange", "起始帧不能大于结束帧。"); return false; }
                    if (string.IsNullOrWhiteSpace(t.OutputTemplate)) { error = prefix + Localizer.T("msg.templateEmpty", "输出模板为空。"); return false; }
                    if (!HasFrameToken(t.OutputTemplate)) { error = prefix + Localizer.T("msg.templateNoToken", "输出模板缺少帧号占位符，应形如 Image_****.png。"); return false; }
                }
            }
            return true;
        }

        private static bool HasFrameToken(string template)
            => System.Text.RegularExpressions.Regex.IsMatch(template, @"\*+");

        /// <summary>运行中禁用参数编辑与模式切换，停止后恢复。</summary>
        private void SetRunningUi(bool running)
        {
            btnStart.Enabled = !running;
            btnStop.Enabled = running;
            cmbMode.Enabled = !running;
            grpTeamRender.Enabled = !running;
            grpFrame.Enabled = !running;
        }

        // ---------- 控制器事件 ----------

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

        private const int MaxLogLines = 1500;
        private const int MaxLogChars = 150000;
        private const int LogTrimKeep = 1000;

        private void TrimLog()
        {
            if (rtbLog.Lines.Length <= MaxLogLines && rtbLog.TextLength <= MaxLogChars) return;
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

            CommitTaskFromUi();
            int idx = lstTasks.SelectedIndex;
            if (idx < 0 || _tasks == null || idx >= _tasks.Count)
            {
                MessageBox.Show(this, Localizer.T("msg.noTaskSelected", "请先选中一个任务。"),
                    Localizer.T("msg.previewTitleShort", "预览输出名"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var task = _tasks[idx];

            if (string.IsNullOrWhiteSpace(task.OutputTemplate))
            {
                MessageBox.Show(this, Localizer.T("msg.templateEmpty", "输出模板为空。"),
                    Localizer.T("msg.previewTitleShort", "预览输出名"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!HasFrameToken(task.OutputTemplate))
            {
                MessageBox.Show(this, Localizer.T("msg.templateNoToken", "输出模板缺少帧号占位符，应形如 Image_****.png（星号个数=补零位数）。"),
                    Localizer.T("msg.previewTitleShort", "预览输出名"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string startPath = FrameScanner.FramePath(task.OutputTemplate, task.StartFrame);
            string endPath = FrameScanner.FramePath(task.OutputTemplate, task.EndFrame);
            var sb = new StringBuilder();
            sb.AppendLine(Localizer.Tf("dlg.preStartFrame", "起始帧 {0} → {1}", task.StartFrame, startPath));
            sb.AppendLine(Localizer.Tf("dlg.preEndFrame", "结束帧 {0} → {1}", task.EndFrame, endPath));
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
                var frames = FrameScanner.EnumerateRenderedFrames(task.OutputTemplate);
                if (frames.Count == 0)
                {
                    sb.AppendLine(CountFiles(dir) > 0
                        ? Localizer.T("dlg.preNoMatchWarn", "⚠ 目录里有文件，但没有匹配命名规则的帧号：多半前缀/补零位数/扩展名与工程内实际输出名不一致，请对照修正模板。")
                        : Localizer.T("dlg.preEmpty", "（输出目录为空——全新任务属正常）"));
                }
                else
                {
                    string segs = BuildFrameRanges(Path.GetFileName(task.OutputTemplate), frames);
                    sb.AppendLine(Localizer.Tf("dlg.preCount", "{0} 等 {1} 个文件", segs, frames.Count));
                }
            }

            MessageBox.Show(this, sb.ToString(), Localizer.T("dlg.previewTitle", "输出文件预览 / 校验"), MessageBoxButtons.OK, MessageBoxIcon.Information);
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
            // 列表显示由 txtFrScene.TextChanged → OnTaskEditChanged 即时接手
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

        private static int CountFiles(string dir)
        {
            try { return Directory.GetFiles(dir).Length; }
            catch { return 0; }
        }

        private static string BuildFrameRanges(string template, List<int> sortedFrames)
        {
            var parts = new List<string>();
            int runStart = sortedFrames[0], runPrev = sortedFrames[0];
            for (int i = 1; i <= sortedFrames.Count; i++)
            {
                bool continues = i < sortedFrames.Count && sortedFrames[i] == runPrev + 1;
                if (continues) { runPrev = sortedFrames[i]; continue; }
                string rep = runStart == runPrev ? $"[{runStart}]" : $"[{runStart},{runPrev}]";
                parts.Add(new System.Text.RegularExpressions.Regex(@"\*+").Replace(template, rep, 1));
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
                System.Threading.Thread.Sleep(200);
            }
            CommitAll();
            _cfg.Save(out _);
        }
    }
}
