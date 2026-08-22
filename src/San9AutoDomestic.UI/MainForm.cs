using System;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using San9AutoDomestic.Adapter.San9Pk101;
using San9AutoDomestic.Core.Configuration;

namespace San9AutoDomestic.UI
{
    internal enum NativeBatchClickDisposition
    {
        RejectWithoutController = 0,
        RefreshBeforeConfirm = 1,
        ConfirmWrite = 2
    }

    public sealed class MainForm : Form
    {
        private static readonly TimeSpan MaximumPreviewSnapshotAge = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan TransientRetryBaseDelay = TimeSpan.FromMilliseconds(2500);
        private const int PresencePollIntervalMilliseconds = 2500;
        private const int MaximumAutomaticTransientRetries = 4;
        private const int MaximumLogCharacters = 200000;
        private const int MaximumLogLines = 2000;
        private const string AvailabilityReadOnlyBoundaryMessage =
            "[READ-ONLY] 旧V2配置提交/停止仍禁用；绿色原生批次就绪时Basic/Wealthy可独立执行。";
        private const string PreviewReadOnlyBoundaryMessage =
            "[READ-ONLY] IsActionable=false，CommitAuthorized=false；该预览不授权旧V2配置提交/停止；绿色原生批次就绪时Basic/Wealthy可独立执行。";
        private const string DefaultDetectionMessage =
            "[DETECT] 正在检查游戏版本、当前城市和候选武将；检测完成后会单独显示原生批次是否就绪。";
        private const string NativeExecutionReadyMessage =
            "[READY] 原生执行能力已就绪：Basic/Wealthy会实际修改当前城市，执行前会再次确认。";
        private const string NativeExecutionNotReadyMessage =
            "[NOT READY] 原生执行能力尚未就绪：请等待顶部绿色状态或点击“重新检测”。";
        private const string NativeRestartRequiredMessage =
            "当前游戏进程已有驻留 Bridge 或经历了批次失败；必须完全退出并重启游戏后才能再次执行。";
        private const FormWindowState NativeBatchBackgroundWindowState = FormWindowState.Minimized;
        private const FormWindowState NativeBatchMenuPromptWindowState = FormWindowState.Normal;
        private const string NativeMenuConfirmationMessage =
            "controller 已到达菜单等待点。\r\n\r\n"
            + "现在请切换到游戏，打开当前城市菜单；然后回到本窗口点击“确定”。\r\n"
            + "只有点击“确定”后，助手才会发送唯一一次继续信号。\r\n"
            + "点击“取消”不会发送信号，也不会自动重试。";

        private readonly Label statusBanner;
        private readonly Label connectionValue;
        private readonly Label targetValue;
        private readonly Label ruleValue;
        private readonly Label cityValue;
        private readonly FlowLayoutPanel profilePanel;
        private readonly Button previewButton;
        private readonly Button stopButton;
        private readonly Button refreshButton;
        private readonly CheckBox topMostCheckBox;
        private readonly RichTextBox logBox;
        private readonly BackgroundWorker diagnosticWorker;
        private readonly BackgroundWorker batchWorker;
        private readonly Timer presenceTimer;
        private readonly ToolTip toolTip;
        private readonly IAvailabilityReader availabilityReader;
        private readonly IGamePresenceProbe presenceProbe;
        private readonly IUiClock clock;
        private readonly INativeControllerClient nativeControllerClient;
        private readonly INativeGameFocusHandoff nativeGameFocusHandoff;
        private readonly UiDiagnosticSessionGate diagnosticSessionGate;
        private readonly TransientFaultRetrySchedule transientFaultRetries;
        private DomesticConfiguration loadedConfiguration;
        private ExecutionPlanCatalog loadedPlans;
        private San9Pk101AvailabilityReport latestAvailability;
        private AssistantUiState uiState;
        private GamePresenceSnapshot lastAttemptedPresence;
        private UiDiagnosticSession activeDiagnosticSession;
        private bool previewAfterDiagnostics;
        private bool nativeBatchActive;
        private NativeBatchKind? pendingNativeBatchKind;
        private NativeBatchDiagnosticLog activeNativeBatchLog;
        private GamePresenceSnapshot activeNativeBatchPresence;
        private GamePresenceSnapshot restartRequiredGeneration;
        private string restartRequiredReason;
        private Button basicBatchButton;
        private Button wealthyBatchButton;
        private bool isClosing;
        private bool managedResourcesDisposed;

        public MainForm()
            : this(
                new AdapterAvailabilityReader(),
                new ExactTargetGamePresenceProbe(),
                new SystemUiClock(),
                new NativeControllerClient())
        {
        }

        internal MainForm(
            IAvailabilityReader availabilityReader,
            IGamePresenceProbe presenceProbe,
            IUiClock clock)
            : this(
                availabilityReader,
                presenceProbe,
                clock,
                new NativeControllerClient())
        {
        }

        internal MainForm(
            IAvailabilityReader availabilityReader,
            IGamePresenceProbe presenceProbe,
            IUiClock clock,
            INativeControllerClient nativeControllerClient)
            : this(
                availabilityReader,
                presenceProbe,
                clock,
                nativeControllerClient,
                new NativeGameFocusHandoff(presenceProbe))
        {
        }

        internal MainForm(
            IAvailabilityReader availabilityReader,
            IGamePresenceProbe presenceProbe,
            IUiClock clock,
            INativeControllerClient nativeControllerClient,
            INativeGameFocusHandoff nativeGameFocusHandoff)
        {
            if (availabilityReader == null)
            {
                throw new ArgumentNullException("availabilityReader");
            }

            if (presenceProbe == null)
            {
                throw new ArgumentNullException("presenceProbe");
            }

            if (clock == null)
            {
                throw new ArgumentNullException("clock");
            }

            if (nativeControllerClient == null)
            {
                throw new ArgumentNullException("nativeControllerClient");
            }
            if (nativeGameFocusHandoff == null)
            {
                throw new ArgumentNullException("nativeGameFocusHandoff");
            }

            this.availabilityReader = availabilityReader;
            this.presenceProbe = presenceProbe;
            this.clock = clock;
            this.nativeControllerClient = nativeControllerClient;
            this.nativeGameFocusHandoff = nativeGameFocusHandoff;
            diagnosticSessionGate = new UiDiagnosticSessionGate();
            transientFaultRetries = new TransientFaultRetrySchedule(
                TransientRetryBaseDelay,
                MaximumAutomaticTransientRetries);
            uiState = AssistantUiState.WaitingForGame(
                "尚未检测游戏；助手会保持运行并自动等待。");

            Text = "三国志9 一键内政助手";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(520, 560);
            Size = new Size(720, 760);
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            BackColor = Color.FromArgb(245, 246, 248);

            toolTip = new ToolTip();
            statusBanner = new Label();
            statusBanner.AutoSize = false;
            statusBanner.Dock = DockStyle.Top;
            statusBanner.Height = 42;
            statusBanner.Padding = new Padding(16, 11, 16, 8);
            statusBanner.Font = new Font(Font, FontStyle.Bold);
            connectionValue = CreateValueLabel("尚未检测");
            targetValue = CreateValueLabel("尚未检测");
            ruleValue = CreateValueLabel("尚未加载");
            cityValue = CreateValueLabel("尚未扫描");

            TableLayoutPanel summary = new TableLayoutPanel();
            summary.Dock = DockStyle.Top;
            summary.AutoSize = true;
            summary.Padding = new Padding(16, 14, 16, 8);
            summary.ColumnCount = 2;
            summary.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112F));
            summary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            AddSummaryRow(summary, "游戏连接", connectionValue);
            AddSummaryRow(summary, "目标版本", targetValue);
            AddSummaryRow(summary, "规则配置", ruleValue);
            AddSummaryRow(summary, "直属城市", cityValue);

            profilePanel = new FlowLayoutPanel();
            profilePanel.Dock = DockStyle.Top;
            profilePanel.AutoSize = true;
            profilePanel.Padding = new Padding(12, 8, 12, 4);
            profilePanel.WrapContents = true;

            previewButton = CreateActionButton("预览全部方案（只读）");
            previewButton.Click += delegate { RequestFreshPreview(); };
            toolTip.SetToolTip(previewButton, "先重新读取游戏，再按新鲜快照静态拟选全部方案；不会写入游戏，也不能授权提交。");

            stopButton = CreateActionButton("无感执行未开放");
            stopButton.Visible = false;

            refreshButton = CreateActionButton("重新检测");
            refreshButton.Click += delegate
            {
                previewAfterDiagnostics = false;
                LoadProfiles();
                PollPresence(true, false);
            };
            toolTip.SetToolTip(refreshButton, "重新加载配置；仅在精确目标在线时执行严格只读检测。");

            topMostCheckBox = new CheckBox();
            topMostCheckBox.Text = "窗口置顶";
            topMostCheckBox.AutoSize = true;
            topMostCheckBox.Margin = new Padding(12, 12, 6, 6);
            topMostCheckBox.CheckedChanged += delegate { TopMost = topMostCheckBox.Checked; };

            FlowLayoutPanel utilityPanel = new FlowLayoutPanel();
            utilityPanel.Dock = DockStyle.Top;
            utilityPanel.AutoSize = true;
            utilityPanel.Padding = new Padding(12, 4, 12, 8);
            utilityPanel.Controls.Add(previewButton);
            utilityPanel.Controls.Add(stopButton);
            utilityPanel.Controls.Add(refreshButton);
            utilityPanel.Controls.Add(topMostCheckBox);

            Label logTitle = new Label();
            logTitle.Text = "诊断、只读预览与原生批次日志";
            logTitle.Dock = DockStyle.Top;
            logTitle.Height = 32;
            logTitle.Padding = new Padding(16, 8, 0, 0);
            logTitle.Font = new Font(Font, FontStyle.Bold);

            logBox = new RichTextBox();
            logBox.Dock = DockStyle.Fill;
            logBox.ReadOnly = true;
            logBox.BackColor = Color.White;
            logBox.BorderStyle = BorderStyle.FixedSingle;
            logBox.Font = new Font("Consolas", 9F, FontStyle.Regular, GraphicsUnit.Point);
            logBox.DetectUrls = false;
            logBox.MaxLength = MaximumLogCharacters;
            logBox.Margin = new Padding(16);

            Panel logHost = new Panel();
            logHost.Dock = DockStyle.Fill;
            logHost.Padding = new Padding(16, 0, 16, 16);
            logHost.Controls.Add(logBox);

            Controls.Add(logHost);
            Controls.Add(logTitle);
            Controls.Add(utilityPanel);
            Controls.Add(profilePanel);
            Controls.Add(summary);
            Controls.Add(statusBanner);

            diagnosticWorker = new BackgroundWorker();
            diagnosticWorker.DoWork += DiagnosticWorkerDoWork;
            diagnosticWorker.RunWorkerCompleted += DiagnosticWorkerCompleted;

            batchWorker = new BackgroundWorker();
            batchWorker.DoWork += BatchWorkerDoWork;
            batchWorker.RunWorkerCompleted += BatchWorkerCompleted;

            presenceTimer = new Timer();
            presenceTimer.Interval = PresencePollIntervalMilliseconds;
            presenceTimer.Tick += delegate { PollPresence(false, false); };

            FormClosing += delegate(object sender, FormClosingEventArgs eventArgs)
            {
                if (pendingNativeBatchKind.HasValue
                    || nativeBatchActive
                    || batchWorker.IsBusy)
                {
                    if (activeNativeBatchLog != null)
                    {
                        activeNativeBatchLog.Write(
                            "UI_CLOSE_BLOCKED",
                            "batch_active=true; controller_not_terminated=true");
                    }
                    eventArgs.Cancel = true;
                    MessageBox.Show(
                        this,
                        "原生批次仍在运行。请保持助手窗口开启，等待当前批次自然结束；助手不会强制终止控制器。",
                        "批次仍在运行",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                isClosing = true;
                presenceTimer.Stop();
                diagnosticSessionGate.Invalidate();
            };

            Shown += delegate
            {
                LoadProfiles();
                presenceTimer.Start();
                PollPresence(true, false);
            };

            RenderUiState();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !managedResourcesDisposed)
            {
                managedResourcesDisposed = true;
                isClosing = true;
                diagnosticSessionGate.Invalidate();
                presenceTimer.Stop();
                presenceTimer.Dispose();
                diagnosticWorker.Dispose();
                batchWorker.Dispose();
                toolTip.Dispose();
            }

            base.Dispose(disposing);
        }

        private static Label CreateValueLabel(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = true;
            label.Margin = new Padding(4, 5, 4, 5);
            label.MaximumSize = new Size(430, 0);
            return label;
        }

        private static Button CreateActionButton(string text)
        {
            Button button = new Button();
            button.Text = text;
            button.AutoSize = true;
            button.MinimumSize = new Size(132, 38);
            button.Margin = new Padding(4);
            button.UseVisualStyleBackColor = true;
            return button;
        }

        private static void AddSummaryRow(TableLayoutPanel panel, string name, Control value)
        {
            int row = panel.RowCount;
            panel.RowCount++;
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            Label key = new Label();
            key.Text = name;
            key.AutoSize = true;
            key.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold, GraphicsUnit.Point);
            key.Margin = new Padding(4, 5, 4, 5);
            panel.Controls.Add(key, 0, row);
            panel.Controls.Add(value, 1, row);
        }

        private void LoadProfiles()
        {
            loadedConfiguration = null;
            loadedPlans = null;
            ClearProfileControls();
            string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", "default.json");
            try
            {
                DomesticConfiguration configuration = new ConfigurationLoader().LoadFile(configPath);
                ExecutionPlanCatalog catalog = new ExecutionPlanCompiler().Compile(configuration);
                foreach (ExecutionPlan plan in catalog.Plans)
                {
                    NativeBatchKind kind;
                    string buttonText;
                    string tooltipText;
                    if (string.Equals(plan.ProfileId, "basic", StringComparison.Ordinal))
                    {
                        kind = NativeBatchKind.Basic;
                        buttonText = "基础内政（商业 → 开垦）";
                        tooltipText = "当前城市执行商业、开垦；原生选择每项五人，最多消耗 500 资金。";
                    }
                    else if (string.Equals(plan.ProfileId, "wealthy", StringComparison.Ordinal))
                    {
                        kind = NativeBatchKind.Wealthy;
                        buttonText = "有钱内政（五项）";
                        tooltipText = "当前城市依次执行巡察、商业、开垦、训练、修筑；最多消耗 1000 资金。";
                    }
                    else
                    {
                        continue;
                    }

                    Button button = CreateActionButton(buttonText);
                    NativeBatchKind capturedKind = kind;
                    button.Tag = kind;
                    button.Click += delegate { BeginNativeBatch(capturedKind); };
                    toolTip.SetToolTip(button, tooltipText);
                    profilePanel.Controls.Add(button);
                    if (kind == NativeBatchKind.Basic)
                    {
                        basicBatchButton = button;
                    }
                    else
                    {
                        wealthyBatchButton = button;
                    }
                }

                loadedConfiguration = configuration;
                loadedPlans = catalog;

                ruleValue.Text = string.Format("已加载 schema v{0}，{1} 个方案", configuration.SchemaVersion, catalog.Plans.Count);
                ruleValue.ForeColor = Color.DarkGreen;
            }
            catch (Exception exception)
            {
                ruleValue.Text = "配置加载失败";
                ruleValue.ForeColor = Color.DarkRed;
                AppendLog("[BLOCK] CONFIG: " + exception.Message);
            }

            RenderUiState();
        }

        private void ClearProfileControls()
        {
            basicBatchButton = null;
            wealthyBatchButton = null;
            Control[] oldControls = profilePanel.Controls
                .Cast<Control>()
                .ToArray();
            foreach (Control control in oldControls)
            {
                toolTip.SetToolTip(control, null);
                profilePanel.Controls.Remove(control);
                control.Dispose();
            }
        }

        private void PollPresence(bool forceDiagnostic, bool previewRequested)
        {
            if (isClosing || diagnosticWorker.IsBusy
                || nativeBatchActive || batchWorker.IsBusy)
            {
                return;
            }

            GamePresenceSnapshot presence = ProbePresenceSafely();
            RefreshRestartRequiredGeneration(presence);
            if (IsRestartRequiredForPresence(presence))
            {
                RenderUiState();
                return;
            }
            if (presence.Status == GamePresenceStatus.Offline)
            {
                latestAvailability = null;
                lastAttemptedPresence = null;
                activeDiagnosticSession = null;
                diagnosticSessionGate.Invalidate();
                transientFaultRetries.Reset();
                previewAfterDiagnostics = false;
                SetUiState(AssistantUiState.WaitingForGame(presence.Detail));
                if (previewRequested)
                {
                    AppendLog("[BLOCK] PREVIEW_GAME_OFFLINE: 精确目标游戏尚未上线；助手会继续等待。");
                }

                return;
            }

            if (presence.Status == GamePresenceStatus.ProbeFailed)
            {
                latestAvailability = null;
                activeDiagnosticSession = null;
                diagnosticSessionGate.Invalidate();
                previewAfterDiagnostics = false;
                EnterTransientFault(presence.Detail);
                if (previewRequested)
                {
                    AppendLog("[BLOCK] PREVIEW_PRESENCE_FAILED: " + presence.Detail);
                }

                return;
            }

            DateTimeOffset nowUtc = clock.UtcNow;
            bool sameGeneration = lastAttemptedPresence != null
                && lastAttemptedPresence.IsSameGenerationAs(presence);
            bool transientRetryAuthorized = !forceDiagnostic
                && sameGeneration
                && uiState.Kind == AssistantUiStateKind.Faulted
                && transientFaultRetries.TryConsume(nowUtc);
            bool shouldStart = UiDiagnosticRetryPolicy.ShouldStart(
                forceDiagnostic,
                presence,
                lastAttemptedPresence,
                uiState.Kind,
                transientRetryAuthorized);
            if (!shouldStart)
            {
                if (previewRequested)
                {
                    AppendLog("[BLOCK] PREVIEW_REFRESH_NOT_STARTED: 当前无法启动新的只读检测。");
                }

                return;
            }

            if (forceDiagnostic || !sameGeneration)
            {
                transientFaultRetries.Reset();
            }

            previewAfterDiagnostics = previewRequested;
            StartDiagnostics(presence);
        }

        private GamePresenceSnapshot ProbePresenceSafely()
        {
            try
            {
                return presenceProbe.Probe()
                    ?? GamePresenceSnapshot.Failed("轻量在线检测没有返回结果；助手仍在运行。");
            }
            catch (Exception exception)
            {
                return GamePresenceSnapshot.Failed(
                    "轻量在线检测异常；助手仍在运行：" + exception.Message);
            }
        }

        private void StartDiagnostics(GamePresenceSnapshot presence)
        {
            if (diagnosticWorker.IsBusy)
            {
                return;
            }

            latestAvailability = null;
            lastAttemptedPresence = presence;
            activeDiagnosticSession = diagnosticSessionGate.Begin(presence);
            SetUiState(AssistantUiState.Inspecting(presence.ProcessId.Value));
            AppendLog(DefaultDetectionMessage);
            diagnosticWorker.RunWorkerAsync(activeDiagnosticSession);
        }

        private void RequestFreshPreview()
        {
            if (diagnosticWorker.IsBusy)
            {
                AppendLog("[BLOCK] PREVIEW_REFRESH_BUSY: 只读检测仍在进行，请稍候。");
                return;
            }

            LoadProfiles();
            if (loadedPlans == null)
            {
                AppendLog("[BLOCK] PREVIEW_CONFIG_MISSING: 配置未能加载，不能推演。");
                return;
            }

            AppendLog("预览请求已转换为一次全新的 V2 只读检测；检测完成后才会静态拟选。");
            PollPresence(true, true);
        }

        private void DiagnosticWorkerDoWork(object sender, DoWorkEventArgs eventArgs)
        {
            UiDiagnosticSession session = eventArgs.Argument as UiDiagnosticSession;
            if (session == null)
            {
                throw new InvalidOperationException("The diagnostic session is missing.");
            }

            eventArgs.Result = new DiagnosticWorkResult(
                session,
                availabilityReader.ReadAvailability());
        }

        private void DiagnosticWorkerCompleted(object sender, RunWorkerCompletedEventArgs eventArgs)
        {
            if (isClosing || IsDisposed)
            {
                return;
            }

            bool renderPreviewWhenReady = previewAfterDiagnostics;
            previewAfterDiagnostics = false;
            UiDiagnosticSession session = activeDiagnosticSession;
            activeDiagnosticSession = null;
            GamePresenceSnapshot postPresence = ProbePresenceSafely();
            if (session == null
                || !diagnosticSessionGate.TryComplete(session, postPresence))
            {
                latestAvailability = null;
                if (uiState.Kind == AssistantUiStateKind.Faulted
                    && postPresence.Status == GamePresenceStatus.Online
                    && lastAttemptedPresence != null
                    && lastAttemptedPresence.IsSameGenerationAs(postPresence))
                {
                    AppendLog("[BLOCK] DIAGNOSTIC_STALE_AFTER_UI_FAULT: 已失效的后台结果未采用；保留当前有限重试计划。");
                    FailPendingNativeBatch("本次强制检测结果已失效；controller 未启动。");
                    return;
                }

                HandleChangedPresenceAfterDiagnostic(postPresence);
                FailPendingNativeBatch("检测期间游戏进程代发生变化；本次 controller 未启动。");
                return;
            }

            lastAttemptedPresence = postPresence;
            if (eventArgs.Cancelled)
            {
                RenderDiagnosticFailure("诊断已取消", "[BLOCK] DIAGNOSTIC_CANCELLED");
                FailPendingNativeBatch("本次强制检测被取消；controller 未启动。");
                return;
            }

            if (eventArgs.Error != null)
            {
                RenderDiagnosticFailure(
                    "诊断异常",
                    "[BLOCK] DIAGNOSTIC_EXCEPTION: " + eventArgs.Error.Message);
                FailPendingNativeBatch("本次强制检测异常；controller 未启动：" + eventArgs.Error.Message);
                return;
            }

            DiagnosticWorkResult workResult = eventArgs.Result as DiagnosticWorkResult;
            if (workResult == null
                || workResult.Session == null
                || workResult.Session.Generation != session.Generation
                || workResult.Availability == null)
            {
                RenderDiagnosticFailure(
                    "诊断结果无效",
                    "[BLOCK] DIAGNOSTIC_RESULT_MISSING");
                FailPendingNativeBatch("本次强制检测没有有效结果；controller 未启动。");
                return;
            }

            San9Pk101AvailabilityReport availability = workResult.Availability;
            try
            {
                bool includeDetails = ShouldIncludeAvailabilityDetails(renderPreviewWhenReady);
                AssistantUiState completedState = RenderAvailabilityReport(
                    availability,
                    includeDetails);
                latestAvailability = availability.DataReadSucceeded
                    ? availability
                    : null;
                SetUiState(completedState);
                transientFaultRetries.Reset();
                if (!includeDetails)
                {
                    AppendLog(CanStartNativeBatch()
                        ? NativeExecutionReadyMessage
                        : NativeExecutionNotReadyMessage);
                }
                if (renderPreviewWhenReady && completedState.SnapshotAvailable)
                {
                    RenderUnverifiedPreviews();
                }
                else if (renderPreviewWhenReady)
                {
                    AppendLog("[BLOCK] PREVIEW_DIAGNOSTIC_NOT_READY: 新鲜只读检测未产生可预览快照。");
                }
                if (pendingNativeBatchKind.HasValue)
                {
                    if (completedState.SnapshotAvailable && CanStartNativeBatch())
                    {
                        ContinuePendingNativeBatch();
                    }
                    else
                    {
                        FailPendingNativeBatch("本次强制检测未达到原生执行就绪状态；controller 未启动。");
                    }
                }
            }
            catch (Exception exception)
            {
                latestAvailability = null;
                RenderDiagnosticFailure(
                    "诊断渲染异常",
                    "[BLOCK] DIAGNOSTIC_RENDER_EXCEPTION: " + exception.Message);
                FailPendingNativeBatch("本次强制检测渲染异常；controller 未启动：" + exception.Message);
            }
        }

        private void HandleChangedPresenceAfterDiagnostic(
            GamePresenceSnapshot postPresence)
        {
            if (postPresence.Status == GamePresenceStatus.Offline)
            {
                lastAttemptedPresence = null;
                transientFaultRetries.Reset();
                SetUiState(AssistantUiState.WaitingForGame(
                    "游戏已离线；本次检测结果已丢弃，助手会继续等待。"));
                AppendLog("[BLOCK] DIAGNOSTIC_TARGET_OFFLINE: 检测结束前目标游戏已离线；结果未采用。");
                return;
            }

            if (postPresence.Status == GamePresenceStatus.ProbeFailed)
            {
                EnterTransientFault(postPresence.Detail);
                AppendLog("[BLOCK] DIAGNOSTIC_POST_PRESENCE_FAILED: " + postPresence.Detail);
                return;
            }

            lastAttemptedPresence = null;
            transientFaultRetries.Reset();
            SetUiState(AssistantUiState.WaitingForGame(
                "游戏进程代在检测期间发生变化；旧结果已丢弃，稍后会自动重检。"));
            AppendLog("[BLOCK] DIAGNOSTIC_PROCESS_GENERATION_CHANGED: 检测前后 PID/创建代/路径不一致；结果未采用。");
        }

        private AssistantUiState RenderAvailabilityReport(
            San9Pk101AvailabilityReport availability,
            bool includeDetails)
        {
            if (availability == null)
            {
                throw new ArgumentNullException("availability");
            }

            San9Pk101ReadReport structure = availability.StructureAfter
                ?? availability.StructureBefore;
            if (includeDetails && structure != null)
            {
                RenderReadReport(structure);
            }

            San9Pk101DiagnosticReport baseline = availability.Baseline;
            string targetSummary;
            if (baseline != null
                && baseline.FileValidation != null
                && baseline.FileValidation.IsValid)
            {
                targetSummary = "San9PK 1.0.1.0 / x86 / SHA-256 匹配";
            }
            else
            {
                targetSummary = "目标文件不匹配或不可验证";
            }

            int? processId = baseline != null
                && baseline.ReadOnlyConnection != null
                ? baseline.ReadOnlyConnection.ProcessId
                : null;
            CityAvailabilityObservation[] cities = availability.Cities
                ?? new CityAvailabilityObservation[0];
            string[] citySummaries = cities
                .Where(city => city != null)
                .OrderBy(city => city.CityId)
                .Select(city => string.Format(
                    "{0}（空闲{1}）",
                    string.IsNullOrWhiteSpace(city.CityName)
                        ? "城市#" + city.CityId
                        : city.CityName,
                    city.Commands != null && city.Commands.Length != 0
                        ? city.Commands[0].ReadyCandidateCount
                        : 0))
                .ToArray();
            string citySummary = citySummaries.Length == 0
                ? "0 座（当前 V2 快照）"
                : string.Format("{0} 座：{1}", citySummaries.Length, string.Join("、", citySummaries));

            if (includeDetails)
            {
                CodeAnchorObservation[] anchors = availability.CodeAnchors
                    ?? new CodeAnchorObservation[0];
                int exactAnchors = anchors.Count(anchor => anchor != null
                    && anchor.DiskMatchesExpected
                    && anchor.LiveWasStable
                    && anchor.LiveMatchesDisk
                    && string.IsNullOrEmpty(anchor.Error));
                StringBuilder builder = new StringBuilder();
                builder.AppendLine("V2 只读可用性观察（不会写入游戏）：");
                builder.AppendLine(string.Format(
                    "  dataRead={0}, blocked={1}, rawStable={2}, structureRevalidated={3}, processRevalidated={4}",
                    availability.DataReadSucceeded,
                    availability.ObservationBlocked,
                    availability.RawFieldsWereStable,
                    availability.StructureWasRevalidated,
                    availability.ProcessIdentityRevalidatedAfterObservation));
                builder.AppendLine(string.Format(
                    "  codeAnchors={0}/{1}, exact={2}; PlanningReady={3}; VerifiedNativeCapability={4}",
                    exactAnchors,
                    anchors.Length,
                    availability.CodeAnchorsWereStableAndExact,
                    availability.PlanningReady,
                    availability.VerifiedNativeCapability));
                if (availability.ContextToken != null)
                {
                    builder.AppendLine(string.Format(
                        "  phaseCandidate={0}, strategicInputCandidate={1}, token={2}",
                        availability.ContextToken.PhaseCandidate,
                        availability.ContextToken.IsStrategicInputPhaseCandidate,
                        ShortHash(availability.ContextToken.TokenSha256)));
                }

                foreach (CityAvailabilityObservation city in cities
                    .Where(city => city != null)
                    .OrderBy(city => city.CityId))
                {
                    int ready = city.Commands != null && city.Commands.Length != 0
                        ? city.Commands[0].ReadyCandidateCount
                        : 0;
                    builder.AppendLine(string.Format(
                        "  [READ] 城市#{0} {1}: 军团={2}, 金={3}, 空闲={4}",
                        city.CityId,
                        city.CityName,
                        city.CorpsId.HasValue ? city.CorpsId.Value.ToString() : "未知",
                        city.CorpsMoney,
                        ready));
                    foreach (CommandAvailabilityObservation command in city.Commands
                        ?? new CommandAvailabilityObservation[0])
                    {
                        string[] closed = (command.Conditions ?? new AvailabilityCondition[0])
                            .Where(condition => condition != null && condition.Passed == false)
                            .Select(condition => condition.Code)
                            .ToArray();
                        builder.AppendLine(string.Format(
                            "    {0}: 静态子集={1}, 每人静态预估费用={2}, 关闭条件={3}",
                            CommandName(command.Command),
                            command.KnownStaticSubsetWouldPass,
                            command.ProvisionalCostPerOfficer,
                            closed.Length == 0 ? "无" : string.Join(",", closed)));
                    }
                }

                foreach (AvailabilityIssue issue in availability.Issues
                    ?? new AvailabilityIssue[0])
                {
                    builder.AppendLine(string.Format(
                        "[{0}] {1}: {2}",
                        issue.Severity,
                        issue.Code,
                        issue.Message));
                }

                builder.AppendLine(AvailabilityReadOnlyBoundaryMessage);
                AppendLog(builder.ToString().TrimEnd());
            }

            if (!availability.DataReadSucceeded)
            {
                return AssistantUiState.ExecutionUnavailable(
                    "V2 只读检测未通过；助手仍在运行，可重新检测。",
                    processId,
                    availability.ReadCompletedUtc,
                    targetSummary,
                    "暂无可用只读快照",
                    false);
            }

            if (availability.ObservationBlocked)
            {
                return AssistantUiState.ExecutionUnavailable(
                    string.Format(
                        "PID {0} 只读快照完成（句柄已释放），但存在阻断；只能查看诊断。",
                        processId.HasValue ? processId.Value.ToString() : "?"),
                    processId,
                    availability.ReadCompletedUtc,
                    targetSummary,
                    citySummary,
                    true);
            }

            return AssistantUiState.ReadOnlyReady(
                processId,
                availability.ReadCompletedUtc,
                targetSummary,
                citySummary);
        }

        private void RenderUnverifiedPreviews()
        {
            San9Pk101AvailabilityReport availability = latestAvailability;
            ExecutionPlanCatalog plans = loadedPlans;
            if (availability == null || plans == null)
            {
                AppendLog("[BLOCK] PREVIEW_SNAPSHOT_MISSING: 请先重新检测。");
                return;
            }

            if (!availability.ReadCompletedUtc.HasValue)
            {
                AppendLog("[BLOCK] PREVIEW_TIME_MISSING: 快照没有完成时间，请重新检测。");
                return;
            }

            TimeSpan snapshotAge;
            if (!SnapshotFreshness.IsFresh(
                clock.UtcNow,
                availability.ReadCompletedUtc.Value,
                MaximumPreviewSnapshotAge,
                out snapshotAge))
            {
                AppendLog(string.Format(
                    "[BLOCK] PREVIEW_SNAPSHOT_STALE: 快照年龄为 {0:F1} 秒，超过 {1:F0} 秒上限，请重新点击预览。",
                    snapshotAge.TotalSeconds,
                    MaximumPreviewSnapshotAge.TotalSeconds));
                return;
            }

            StringBuilder builder = new StringBuilder();
            builder.AppendLine("只读方案静态拟选（未验证）：本次点击已先完成全新的稳定快照；不会调用游戏函数、不会写内存、不会授权提交。");
            builder.AppendLine(string.Format(
                "快照完成={0:u}，年龄={1:F1} 秒，token={2}",
                availability.ReadCompletedUtc.Value,
                snapshotAge.TotalSeconds,
                availability.ContextToken == null ? "无" : ShortHash(availability.ContextToken.TokenSha256)));
            builder.AppendLine("正式执行将来必须在每条命令前后重新读取；本页不是执行承诺。");
            foreach (AvailabilityIssue issue in availability.Issues ?? new AvailabilityIssue[0])
            {
                if (issue != null && issue.Severity != DiagnosticSeverity.Blocking)
                {
                    builder.AppendLine(string.Format(
                        "[再次提示:{0}] {1}: {2}",
                        issue.Severity,
                        issue.Code,
                        issue.Message));
                }
            }
            San9Pk101UnverifiedPreviewProjector projector =
                new San9Pk101UnverifiedPreviewProjector();
            foreach (ExecutionPlan plan in plans.Plans)
            {
                San9Pk101UnverifiedPreviewResult result = projector.Project(plan, availability);
                AssertPreviewCannotAuthorize(result);
                builder.AppendLine();
                builder.AppendLine(string.Format(
                    "[{0}] {1}：diagnosticOnly={2}，静态拟选项目={3}，静态拟选人次={4}",
                    plan.ProfileId,
                    plan.DisplayName,
                    result.DiagnosticOnly,
                    result.ProvisionallySelectedTaskCount,
                    result.SelectedOfficerCount));
                if (result.DiagnosticOnly)
                {
                    foreach (San9Pk101UnverifiedPreviewIssue issue in result.Issues)
                    {
                        builder.AppendLine(string.Format(
                            "  [BLOCK] {0}: {1}",
                            issue.Code,
                            issue.Message));
                    }
                    continue;
                }

                foreach (San9Pk101UnverifiedPreviewIssue issue in result.Issues)
                {
                    builder.AppendLine(string.Format(
                        "  [PROVENANCE] {0}: {1}",
                        issue.Code,
                        issue.Message));
                }

                foreach (San9Pk101UnverifiedCityPreview city in result.Cities)
                {
                    builder.AppendLine(string.Format(
                        "  城市#{0} {1}（军团{2}）",
                        city.CityId,
                        city.CityName,
                        city.CorpsId));
                    foreach (San9Pk101UnverifiedTaskPreview task in city.Tasks)
                    {
                        if (task.Decision == UnverifiedPreviewDecision.ProvisionallySelected)
                        {
                            string officers = string.Join(", ", task.SelectedOfficers
                                .Select(officer => string.Format(
                                    "{0}#{1}({2})",
                                    string.IsNullOrWhiteSpace(officer.Name) ? "武将" : officer.Name,
                                    officer.PersonId,
                                    officer.Score))
                                .ToArray());
                            builder.AppendLine(string.Format(
                                "    [静态拟选·未验证] {0} [requested={1}, applied={2}]: {3}; 费用={4}; 金={5}->{6}",
                                CommandName(task.ObservedCommand),
                                task.RequestedSelectionPolicy,
                                task.AppliedRankingSource,
                                officers,
                                task.EstimatedCost,
                                task.MoneyBefore,
                                task.MoneyAfter));
                        }
                        else
                        {
                            builder.AppendLine(string.Format(
                                "    [跳过] {0} [requested={1}, applied={2}]: {3}; 候选={4}; 原因={5}{6}",
                                CommandName(task.ObservedCommand),
                                task.RequestedSelectionPolicy,
                                task.AppliedRankingSource,
                                PreviewSkipName(task.SkipReason),
                                task.EligibleCandidateCount,
                                task.Detail,
                                task.FailedConditionCodes.Count == 0
                                    ? string.Empty
                                    : " [" + string.Join(",", task.FailedConditionCodes.ToArray()) + "]"));
                        }
                    }
                }

                foreach (San9Pk101UnverifiedCorpsMoneyPreview money in result.RemainingMoneyByCorps)
                {
                    builder.AppendLine(string.Format(
                        "  军团#{0} 推演后余额={1}",
                        money.CorpsId,
                        money.RemainingMoney));
                }
            }

            builder.AppendLine();
            builder.AppendLine(PreviewReadOnlyBoundaryMessage);
            AppendLog(builder.ToString().TrimEnd());
        }

        private static void AssertPreviewCannotAuthorize(
            San9Pk101UnverifiedPreviewResult result)
        {
            if (result == null || result.IsActionable || result.CommitAuthorized
                || result.Issues.Any(issue => issue.IsActionable || issue.CommitAuthorized)
                || result.Cities.Any(city => city.IsActionable || city.CommitAuthorized
                    || city.Tasks.Any(task => task.IsActionable || task.CommitAuthorized
                        || task.SelectedOfficers.Any(officer =>
                            officer.IsActionable || officer.CommitAuthorized)))
                || result.RemainingMoneyByCorps.Any(money =>
                    money.IsActionable || money.CommitAuthorized))
            {
                throw new InvalidOperationException(
                    "The read-only preview projector exposed an authorization capability.");
            }
        }

        private static string CommandName(DomesticCommandKind command)
        {
            switch (command)
            {
                case DomesticCommandKind.Patrol:
                    return "巡察";
                case DomesticCommandKind.Commerce:
                    return "商业";
                case DomesticCommandKind.Cultivate:
                    return "开垦";
                case DomesticCommandKind.Train:
                    return "训练";
                case DomesticCommandKind.Repair:
                    return "修筑";
                default:
                    return "未知命令";
            }
        }

        private static string PreviewSkipName(UnverifiedPreviewSkipReason reason)
        {
            switch (reason)
            {
                case UnverifiedPreviewSkipReason.NativeRuleBlocked:
                    return "项目当前灰色/不可用";
                case UnverifiedPreviewSkipReason.InsufficientOfficers:
                    return "剩余武将不足";
                case UnverifiedPreviewSkipReason.InsufficientMoney:
                    return "军团资金不足";
                case UnverifiedPreviewSkipReason.ReserveMoneyProtected:
                    return "触及保留资金";
                case UnverifiedPreviewSkipReason.DuplicateCommand:
                    return "同城同项已拟执行";
                default:
                    return "未分类";
            }
        }

        private static string ShortHash(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "无";
            }

            return value.Length <= 12 ? value : value.Substring(0, 12) + "…";
        }

        private void RenderReadReport(San9Pk101ReadReport readReport)
        {
            if (readReport == null)
            {
                throw new ArgumentNullException("readReport");
            }

            San9Pk101DiagnosticReport report = readReport.Baseline;
            if (report == null)
            {
                AppendLog("[BLOCK] BASELINE_UNAVAILABLE");
                return;
            }

            CityReadRecord[] cities = readReport.Cities ?? new CityReadRecord[0];
            ForceReadRecord[] forces = readReport.Forces ?? new ForceReadRecord[0];
            PersonReadRecord[] persons = readReport.Persons ?? new PersonReadRecord[0];
            ReadInvariantDiagnostic[] readIssues = readReport.Issues ?? new ReadInvariantDiagnostic[0];
            CityReadRecord[] directCities = cities
                .Where(city => city.IsDirectlyControlled)
                .OrderBy(city => city.Id)
                .ToArray();
            StringBuilder builder = new StringBuilder();
            builder.AppendLine(string.Format("诊断时间：{0:u}", report.CreatedUtc));
            if (report.ProcessDiscovery != null)
            {
                builder.AppendLine("进程发现：" + report.ProcessDiscovery.Status);
            }

            if (report.ConflictScan != null)
            {
                ConflictDiagnostic[] conflicts = report.ConflictScan.Conflicts
                    ?? new ConflictDiagnostic[0];
                builder.AppendLine(string.Format(
                    "冲突检测：{0} 项，阻断={1}",
                    conflicts.Length,
                    report.ConflictScan.HasBlockingConflicts));
                foreach (ConflictDiagnostic conflict in conflicts)
                {
                    builder.AppendLine(string.Format(
                        "  [{0}] {1}: {2}",
                        conflict.IsBlocking ? "BLOCK" : "WARN",
                        conflict.Name,
                        conflict.Reason));
                }
            }

            builder.AppendLine(string.Format(
                "V1 只读扫描：成功={0}，双读稳定={1}，尝试={2}，玩家主势力={3}",
                readReport.ReadSucceeded,
                readReport.RelevantFieldsWereStable,
                readReport.StableReadAttempts,
                readReport.PlayerForceId.HasValue ? readReport.PlayerForceId.Value.ToString() : "未知"));
            builder.AppendLine(string.Format(
                "表规模：军团/势力={0}，城市={1}，武将={2}；直属城市={3}",
                forces.Length,
                cities.Length,
                persons.Length,
                directCities.Length));
            foreach (CityReadRecord city in directCities)
            {
                builder.AppendLine(string.Format(
                    "  [READ] 城市#{0} {1}：军团={2}，在城有效武将={3}/{4}",
                    city.Id,
                    city.Name,
                    city.CorpsId.HasValue ? city.CorpsId.Value.ToString() : "未知",
                    city.ObservedValidResidentOfficerCount,
                    city.DeclaredValidResidentOfficerCount));
            }

            foreach (DiagnosticIssue issue in report.Issues ?? new DiagnosticIssue[0])
            {
                builder.AppendLine(issue.ToString());
            }

            foreach (ReadInvariantDiagnostic issue in readIssues)
            {
                builder.AppendLine(string.Format(
                    "[{0}] {1} {2}:{3}: {4}",
                    issue.Severity,
                    issue.Code,
                    issue.EntityKind,
                    issue.EntityId.HasValue ? issue.EntityId.Value.ToString() : "-",
                    issue.Message));
            }

            AppendLog(builder.ToString().TrimEnd());
        }

        private void SetUiState(AssistantUiState state)
        {
            if (state == null)
            {
                throw new ArgumentNullException("state");
            }

            uiState = state;
            RenderUiState();
        }

        private void BeginNativeBatch(NativeBatchKind kind)
        {
            if (restartRequiredGeneration != null)
            {
                GamePresenceSnapshot currentPresence = ProbePresenceSafely();
                RefreshRestartRequiredGeneration(currentPresence);
                if (IsRestartRequiredForPresence(currentPresence))
                {
                    AppendLog("[RESTART REQUIRED] " + NativeRestartRequiredMessage);
                    SetNativeBatchStage(NativeRestartRequiredMessage, true);
                    MessageBox.Show(
                        this,
                        NativeRestartRequiredMessage,
                        "必须重启游戏",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return;
                }
            }

            if (NativeBatchClickDispositionForCurrentState()
                == NativeBatchClickDisposition.RejectWithoutController)
            {
                AppendLog("[BLOCK] NATIVE_BATCH_BUSY: 当前正在检测、等待确认或执行批次；本次未启动新的 controller。");
                MessageBox.Show(
                    this,
                    "当前正在检测、等待确认或执行批次。请等待当前阶段结束；本次不会启动新的 controller。",
                    "原生批次正忙",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            NativeBatchDiagnosticLog diagnosticLog;
            string diagnosticError;
            if (!NativeBatchDiagnosticLog.TryCreate(
                    kind,
                    out diagnosticLog,
                    out diagnosticError))
            {
                string message = "无法创建持久批次日志，因此本次不启动 controller：" + diagnosticError;
                AppendLog("[BLOCK] NATIVE_BATCH_LOG_CREATE_FAILED: " + message);
                SetNativeBatchStage(message, true);
                MessageBox.Show(
                    this,
                    message,
                    "无法创建批次日志",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            activeNativeBatchLog = diagnosticLog;
            pendingNativeBatchKind = kind;
            diagnosticLog.Write(
                "UI_FRESH_V2_REQUESTED",
                "profile=" + kind + "; same_pid_save_state_not_reused=true");
            AppendLog("[DETECT] 本次点击先强制重新检测当前进程代与当前存档状态；成功后会自动继续首次写入确认，无需再次点击。");
            RenderUiState();
            SetNativeBatchStage("正在为本次点击重新检测当前存档；controller 尚未启动。", false);
            PollPresence(true, false);
            if (pendingNativeBatchKind.HasValue && !diagnosticWorker.IsBusy)
            {
                FailPendingNativeBatch("未能启动本次强制检测；请确认游戏在线后重试。");
            }
        }

        private void ContinuePendingNativeBatch()
        {
            if (!pendingNativeBatchKind.HasValue)
            {
                return;
            }

            NativeBatchKind kind = pendingNativeBatchKind.Value;
            NativeBatchDiagnosticLog diagnosticLog = activeNativeBatchLog;
            if (diagnosticLog == null || !CanStartNativeBatch())
            {
                FailPendingNativeBatch("本次强制检测未得到原生批次就绪结论；controller 未启动。");
                return;
            }

            diagnosticLog.Write("UI_FRESH_V2_ACCEPTED", "profile=" + kind);
            diagnosticLog.Write("UI_WRITE_CONFIRM_SHOWN", "profile=" + kind);
            SetNativeBatchStage("本次强制检测已通过；等待首次写入确认。", false);
            DialogResult confirmation = MessageBox.Show(
                this,
                NativeWriteConfirmationText(kind),
                kind == NativeBatchKind.Basic
                    ? "确认执行基础内政"
                    : "确认执行有钱内政",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (confirmation != DialogResult.OK)
            {
                diagnosticLog.Write("UI_WRITE_CONFIRM_DECLINED", "controller_started=false");
                AppendLog("用户取消：未启动原生控制器，未加载 Bridge。");
                SetNativeBatchStage("用户取消首次写入确认；controller 未启动。", false);
                pendingNativeBatchKind = null;
                activeNativeBatchLog = null;
                RenderUiState();
                return;
            }

            diagnosticLog.Write("UI_WRITE_CONFIRM_APPROVED", "controller_may_start=true");
            diagnosticLog.Write(
                "UI_FRESH_INSPECT_REQUIRED",
                "previous_v2_not_authoritative=true; fresh_inspect_before_batch=true");
            SetNativeBatchStage("正在核验并把精确游戏窗口切到前台；controller 尚未启动。", false);
            diagnosticLog.Write(
                "GAME_FOCUS_HANDOFF_BEGIN",
                "user_initiated=true; controller_started=false");
            NativeGameFocusHandoffResult focusResult =
                nativeGameFocusHandoff.TryHandoff(lastAttemptedPresence);
            if (focusResult == null || !focusResult.Succeeded)
            {
                string focusError = focusResult == null
                    ? "游戏窗口焦点交接没有返回结果。"
                    : focusResult.Error;
                diagnosticLog.Write(
                    "GAME_FOCUS_HANDOFF_REJECTED",
                    "controller_started=false; error=" + focusError);
                pendingNativeBatchKind = null;
                activeNativeBatchLog = null;
                AppendLog("[BLOCK] GAME_FOCUS_HANDOFF_FAILED: " + focusError);
                RenderUiState();
                SetNativeBatchStage(focusError + " 本次未启动 controller。", true);
                MessageBox.Show(
                    this,
                    focusError + "\r\n\r\n本次未启动 controller，也未加载 Bridge。请保持游戏窗口可见后重试。",
                    "无法切换到游戏",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }
            diagnosticLog.Write(
                "GAME_FOCUS_HANDOFF_COMPLETE",
                string.Format(
                    "pid={0}; hwnd=0x{1:X}; exact_generation=true",
                    focusResult.ProcessId,
                    focusResult.WindowHandle.ToInt64()));
            pendingNativeBatchKind = null;
            nativeBatchActive = true;
            activeNativeBatchPresence = lastAttemptedPresence;
            uiState = uiState.WithBatchActivity(BatchActivityState.Running);
            AppendLog(kind == NativeBatchKind.Basic
                ? "开始基础内政：先由原生控制器重新执行只读 inspect。"
                : "开始有钱内政：先由原生控制器重新执行只读 inspect。");
            RenderUiState();
            SetNativeBatchStage("正在启动只读 inspect；尚未加载批次 Bridge。", false);
            diagnosticLog.Write(
                "UI_WINDOW_MINIMIZE_REQUESTED",
                "state=Minimized; reason=exact_game_focus_handoff_complete");
            WindowState = NativeBatchBackgroundWindowState;
            try
            {
                batchWorker.RunWorkerAsync(new NativeBatchWorkRequest(
                    kind,
                    diagnosticLog,
                    activeNativeBatchPresence));
            }
            catch (Exception exception)
            {
                diagnosticLog.Write("UI_WORKER_START_EXCEPTION", exception.Message);
                nativeBatchActive = false;
                uiState = uiState.WithBatchActivity(BatchActivityState.Unavailable);
                RenderUiState();
                WindowState = NativeBatchMenuPromptWindowState;
                SetNativeBatchStage("批次工作线程未启动：" + exception.Message, true);
                activeNativeBatchLog = null;
                activeNativeBatchPresence = null;
                throw;
            }
        }

        private void FailPendingNativeBatch(string reason)
        {
            if (!pendingNativeBatchKind.HasValue)
            {
                return;
            }
            NativeBatchDiagnosticLog diagnosticLog = activeNativeBatchLog;
            if (diagnosticLog != null)
            {
                diagnosticLog.Write("UI_FRESH_V2_REJECTED", reason);
            }
            pendingNativeBatchKind = null;
            activeNativeBatchLog = null;
            AppendLog("[BLOCK] NATIVE_BATCH_FRESH_DETECTION_FAILED: " + reason);
            RenderUiState();
            SetNativeBatchStage(reason, true);
            MessageBox.Show(
                this,
                reason,
                "本次检测未就绪",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        private void BatchWorkerDoWork(object sender, DoWorkEventArgs eventArgs)
        {
            NativeBatchWorkRequest request = eventArgs.Argument as NativeBatchWorkRequest;
            if (request == null)
            {
                throw new InvalidOperationException("The native batch work request is missing.");
            }
            NativeBatchKind kind = request.Kind;
            NativeInspectResult inspect = nativeControllerClient.Inspect(
                request.DiagnosticLog,
                QueueNativeStageFromWorker);
            if (!inspect.Ready)
            {
                eventArgs.Result = new NativeBatchWorkResult(
                    kind,
                    inspect,
                    null,
                    request.ExpectedPresence);
                return;
            }

            NativeBatchResult batch = nativeControllerClient.RunBatch(
                kind,
                ConfirmCurrentCityMenuFromWorker,
                QueueNativeJsonFromWorker,
                request.DiagnosticLog,
                QueueNativeStageFromWorker);
            eventArgs.Result = new NativeBatchWorkResult(
                kind,
                inspect,
                batch,
                request.ExpectedPresence);
        }

        private bool ConfirmCurrentCityMenuFromWorker()
        {
            bool approved = false;
            NativeBatchDiagnosticLog diagnosticLog = activeNativeBatchLog;
            if (diagnosticLog != null)
            {
                diagnosticLog.Write("UI_MENU_CONFIRM_REQUESTED", "invoke_pending=true");
            }
            if (isClosing || IsDisposed || !IsHandleCreated)
            {
                if (diagnosticLog != null)
                {
                    diagnosticLog.Write(
                        "UI_MENU_CONFIRM_UNAVAILABLE",
                        "isClosing=" + isClosing + " disposed=" + IsDisposed
                            + " handle=" + IsHandleCreated);
                }
                return false;
            }

            Invoke(new MethodInvoker(delegate
            {
                if (isClosing || IsDisposed)
                {
                    if (diagnosticLog != null)
                    {
                        diagnosticLog.Write("UI_MENU_CONFIRM_ABORTED", "form_closing=true");
                    }
                    return;
                }

                if (diagnosticLog != null)
                {
                    diagnosticLog.Write("UI_MENU_CONFIRM_SHOWN", "waiting_for_user=true");
                }
                RestoreNativeBatchWindow(
                    diagnosticLog,
                    "UI_WINDOW_RESTORED_FOR_MENU");
                SetNativeBatchStage("controller 正在等待；请打开当前城市菜单并完成第二次确认。", false);
                approved = MessageBox.Show(
                    this,
                    NativeMenuConfirmationMessage,
                    "等待当前城市菜单",
                    MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Information,
                    MessageBoxDefaultButton.Button2) == DialogResult.OK;
                if (diagnosticLog != null)
                {
                    diagnosticLog.Write(
                        approved ? "UI_MENU_CONFIRM_APPROVED" : "UI_MENU_CONFIRM_DECLINED",
                        "approved=" + approved);
                }
            }));
            return approved;
        }

        private void QueueNativeStageFromWorker(string stage)
        {
            if (isClosing || IsDisposed || !IsHandleCreated)
            {
                return;
            }

            try
            {
                BeginInvoke(new MethodInvoker(delegate
                {
                    if (!isClosing && !IsDisposed && nativeBatchActive)
                    {
                        SetNativeBatchStage(stage, IsFailureStage(stage));
                        AppendLog("[NATIVE STAGE] " + stage);
                    }
                }));
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void QueueNativeJsonFromWorker(string json)
        {
            if (isClosing || IsDisposed || !IsHandleCreated)
            {
                return;
            }

            try
            {
                BeginInvoke(new MethodInvoker(delegate
                {
                    if (!isClosing && !IsDisposed)
                    {
                        AppendLog(NativeControllerClient.DescribeJsonLine(json));
                        AppendLog("[NATIVE JSON] " + json);
                    }
                }));
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void BatchWorkerCompleted(object sender, RunWorkerCompletedEventArgs eventArgs)
        {
            NativeBatchDiagnosticLog diagnosticLog = activeNativeBatchLog;
            NativeBatchWorkResult work = eventArgs.Error == null && !eventArgs.Cancelled
                ? eventArgs.Result as NativeBatchWorkResult
                : null;
            string terminalStage;
            bool terminalFailure;
            bool newlyRequiresRestart = false;
            RestoreNativeBatchWindow(
                diagnosticLog,
                "UI_WINDOW_RESTORED_ON_COMPLETION");
            nativeBatchActive = false;
            uiState = uiState.WithBatchActivity(BatchActivityState.Unavailable);
            if (eventArgs.Error != null)
            {
                AppendLog("[FAIL] 原生批次界面工作线程异常：" + eventArgs.Error.Message);
                terminalStage = "批次界面工作线程异常：" + eventArgs.Error.Message;
                terminalFailure = true;
            }
            else
            {
                if (work == null || work.Inspect == null)
                {
                    AppendLog("[FAIL] 原生批次没有返回结果；不会重试。");
                    terminalStage = "批次没有返回结果；不会重试。";
                    terminalFailure = true;
                }
                else if (!work.Inspect.Ready)
                {
                    AppendLog("[BLOCK] 原生 inspect 未通过：" + work.Inspect.Error);
                    terminalStage = "原生 inspect 未通过：" + work.Inspect.Error;
                    terminalFailure = true;
                }
                else if (work.Batch == null)
                {
                    AppendLog("[FAIL] 原生批次未启动；不会重试。");
                    terminalStage = "原生批次未启动；不会重试。";
                    terminalFailure = true;
                }
                else
                {
                    RenderNativeBatchResult(work.Batch);
                    terminalStage = NativeBatchTerminalStage(work.Batch);
                    terminalFailure = work.Batch.State != NativeControllerState.Succeeded;
                }
            }

            GamePresenceSnapshot resultGeneration = work != null
                ? work.ExpectedPresence
                : activeNativeBatchPresence;
            if (work != null
                && work.Inspect != null
                && work.Inspect.RequiresGameRestart)
            {
                LatchRestartRequired(
                    resultGeneration,
                    work.Inspect.Error,
                    diagnosticLog);
                newlyRequiresRestart = true;
            }
            else if (work != null
                && work.Batch != null
                && work.Batch.RequiresGameRestart)
            {
                string restartReason = work.Batch.State == NativeControllerState.Succeeded
                    ? "本次批次已结束且 Bridge 仍驻留；请先在游戏中保存，再完全重启游戏后执行下一批次。"
                    : "批次 controller 已启动，Bridge 可能仍驻留；本次失败后必须完全重启游戏。";
                LatchRestartRequired(
                    resultGeneration,
                    restartReason,
                    diagnosticLog);
                newlyRequiresRestart = true;
            }

            if (newlyRequiresRestart && terminalFailure)
            {
                terminalStage = restartRequiredReason;
            }

            RenderUiState();
            if (!isClosing && !IsRestartRequiredForLastPresence())
            {
                PollPresence(true, false);
            }
            SetNativeBatchStage(terminalStage, terminalFailure);
            if (diagnosticLog != null)
            {
                diagnosticLog.Write(
                    terminalFailure ? "UI_FINAL_FAILURE" : "UI_FINAL_SUCCESS",
                    terminalStage);
            }
            activeNativeBatchLog = null;
            activeNativeBatchPresence = null;
            if (newlyRequiresRestart && !isClosing)
            {
                MessageBox.Show(
                    this,
                    restartRequiredReason,
                    "必须重启游戏",
                    MessageBoxButtons.OK,
                    terminalFailure ? MessageBoxIcon.Error : MessageBoxIcon.Information);
            }
        }

        private void RenderNativeBatchResult(NativeBatchResult result)
        {
            if (!string.IsNullOrWhiteSpace(result.Stderr))
            {
                AppendLog("[NATIVE STDERR] " + result.Stderr);
            }

            switch (result.State)
            {
                case NativeControllerState.Succeeded:
                    AppendLog(NativeBatchSuccessLogLine(result));
                    break;
                case NativeControllerState.RebindRequired:
                    AppendLog("[STOP] 当前城市绑定已变化；请重新检测。本次不会重试。");
                    break;
                case NativeControllerState.Declined:
                    AppendLog("用户取消菜单确认：未发送继续信号，本次不会重试。");
                    break;
                default:
                    AppendLog("[FAIL] 原生批次失败且不会重试："
                        + (string.IsNullOrWhiteSpace(result.Error)
                            ? "exit=" + result.ExitCode
                            : result.Error));
                    break;
            }
        }

        private void SetNativeBatchStage(string stage, bool failure)
        {
            string detail = string.IsNullOrWhiteSpace(stage) ? "状态未知" : stage.Trim();
            statusBanner.Text = (failure ? "原生批次失败：" : "原生批次阶段：") + detail;
            AssistantUiTone tone = failure
                ? AssistantUiTone.Error
                : AssistantUiTone.Warning;
            statusBanner.ForeColor = ToneTextColor(tone);
            statusBanner.BackColor = ToneBackgroundColor(tone);
        }

        private void RestoreNativeBatchWindow(
            NativeBatchDiagnosticLog diagnosticLog,
            string logStage)
        {
            bool wasMinimized = WindowState == FormWindowState.Minimized;
            if (!Visible)
            {
                Show();
            }
            ShowInTaskbar = true;
            WindowState = NativeBatchMenuPromptWindowState;
            BringToFront();
            if (diagnosticLog != null)
            {
                diagnosticLog.Write(
                    logStage,
                    "window_state=Normal; was_minimized=" + wasMinimized);
            }
        }

        private static bool IsFailureStage(string stage)
        {
            if (string.IsNullOrWhiteSpace(stage))
            {
                return false;
            }
            return stage.IndexOf("失败", StringComparison.Ordinal) >= 0
                || stage.IndexOf("异常", StringComparison.Ordinal) >= 0
                || stage.IndexOf("未通过", StringComparison.Ordinal) >= 0
                || stage.IndexOf("stderr", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string NativeBatchTerminalStage(NativeBatchResult result)
        {
            switch (result.State)
            {
                case NativeControllerState.Succeeded:
                    if (result.Executed == 0)
                    {
                        return string.Format(
                            "本次没有可执行项；跳过 {0} 项。请查看逐项原因。",
                            result.Skipped);
                    }
                    return string.Format(
                        "批次完成：执行 {0} 项，跳过 {1} 项。",
                        result.Executed,
                        result.Skipped);
                case NativeControllerState.RebindRequired:
                    return "当前城市绑定已变化；批次停止且不会重试。";
                case NativeControllerState.Declined:
                    return "用户取消第二次菜单确认；未发送继续信号。";
                default:
                    return "批次失败且不会重试："
                        + (string.IsNullOrWhiteSpace(result.Error)
                            ? "controller exit=" + result.ExitCode
                            : result.Error);
            }
        }

        private static string NativeBatchSuccessLogLine(NativeBatchResult result)
        {
            if (result.Executed == 0)
            {
                return string.Format(
                    "[NO ACTION] 本次没有可执行项，跳过 {0} 项；请查看上方逐项原因。本次授权不可复用。",
                    result.Skipped);
            }

            return string.Format(
                "[PASS] 原生批次完成：执行 {0}，跳过 {1}。请在游戏中保存；本次授权不可复用。",
                result.Executed,
                result.Skipped);
        }

        private bool CanStartNativeBatch()
        {
            return !isClosing
                && !nativeBatchActive
                && !IsRestartRequiredForLastPresence()
                && !diagnosticWorker.IsBusy
                && !batchWorker.IsBusy
                && loadedPlans != null
                && uiState.Kind == AssistantUiStateKind.ReadOnlyReady
                && uiState.BatchActivity == BatchActivityState.Unavailable;
        }

        private NativeBatchClickDisposition NativeBatchClickDispositionForCurrentState()
        {
            return CanQueueNativeBatch()
                ? NativeBatchClickDisposition.RefreshBeforeConfirm
                : NativeBatchClickDisposition.RejectWithoutController;
        }

        private bool CanQueueNativeBatch()
        {
            return !isClosing
                && !nativeBatchActive
                && !IsRestartRequiredForLastPresence()
                && !pendingNativeBatchKind.HasValue
                && !diagnosticWorker.IsBusy
                && !batchWorker.IsBusy
                && loadedPlans != null;
        }

        private static bool ShouldIncludeAvailabilityDetails(bool renderPreviewWhenReady)
        {
            return renderPreviewWhenReady;
        }

        internal static string NativeWriteConfirmationText(NativeBatchKind kind)
        {
            if (kind == NativeBatchKind.Basic)
            {
                return "即将在当前城市执行：商业 → 开垦。\r\n\r\n"
                    + "最多扣除 500 资金，最多使 10 名武将进入行动状态。\r\n"
                    + "开始前必须停留在战略地图，不要提前打开城市菜单。\r\n"
                    + "点击“确定”即授权助手把本次同代核验的游戏窗口切到前台（不模拟鼠标键盘），随后自动最小化并运行 fresh inspect；不使用先前 V2 日志作为执行依据。\r\n"
                    + "等助手窗口再次出现后，再按提示去游戏打开当前城市菜单。\r\n"
                    + "失败不会自动重试；Bridge 会驻留在游戏进程中，直到游戏完全重启。\r\n\r\n"
                    + "确定要启动本次原生写入批次吗？";
            }

            if (kind == NativeBatchKind.Wealthy)
            {
                return "即将在当前城市执行：巡察 → 商业 → 开垦 → 训练 → 修筑。\r\n\r\n"
                    + "最多扣除 1000 资金，最多使 25 名武将进入行动状态；训练费用为 0。\r\n"
                    + "开始前必须停留在战略地图，不要提前打开城市菜单。\r\n"
                    + "点击“确定”即授权助手把本次同代核验的游戏窗口切到前台（不模拟鼠标键盘），随后自动最小化并运行 fresh inspect；不使用先前 V2 日志作为执行依据。\r\n"
                    + "等助手窗口再次出现后，再按提示去游戏打开当前城市菜单。\r\n"
                    + "失败不会自动重试；Bridge 会驻留在游戏进程中，直到游戏完全重启。\r\n\r\n"
                    + "确定要启动本次原生写入批次吗？";
            }

            throw new ArgumentOutOfRangeException("kind");
        }

        internal static string[] ReadOnlyBoundaryMessagesForTests
        {
            get
            {
                return new string[]
                {
                    AvailabilityReadOnlyBoundaryMessage,
                    PreviewReadOnlyBoundaryMessage
                };
            }
        }

        internal static bool AvailabilityDetailsIncludedForTests(bool renderPreviewWhenReady)
        {
            return ShouldIncludeAvailabilityDetails(renderPreviewWhenReady);
        }

        internal static string[] AutomaticDetectionMessagesForTests(bool nativeReady)
        {
            return new string[]
            {
                DefaultDetectionMessage,
                nativeReady
                    ? NativeExecutionReadyMessage
                    : NativeExecutionNotReadyMessage
            };
        }

        private void RenderUiState()
        {
            AssistantUiPresentation presentation = AssistantUiPresenter.Present(
                uiState,
                loadedPlans != null);
            statusBanner.Text = presentation.BannerText;
            statusBanner.ForeColor = ToneTextColor(presentation.BannerTone);
            statusBanner.BackColor = ToneBackgroundColor(presentation.BannerTone);
            connectionValue.Text = presentation.ConnectionText;
            connectionValue.ForeColor = ToneTextColor(presentation.ConnectionTone);
            targetValue.Text = presentation.TargetText;
            targetValue.ForeColor = ToneTextColor(presentation.TargetTone);
            cityValue.Text = presentation.CityText;
            cityValue.ForeColor = ToneTextColor(presentation.CityTone);
            refreshButton.Enabled = presentation.RefreshEnabled
                && !diagnosticWorker.IsBusy;
            previewButton.Text = presentation.PreviewButtonText;
            previewButton.Enabled = presentation.PreviewEnabled
                && !diagnosticWorker.IsBusy;
            stopButton.Text = presentation.StopButtonText;
            stopButton.Enabled = presentation.StopEnabled;

            bool nativeButtonsEnabled = CanQueueNativeBatch();
            foreach (Control control in profilePanel.Controls)
            {
                Button button = control as Button;
                if (button != null)
                {
                    button.Enabled = nativeButtonsEnabled;
                }
            }
            if (CanStartNativeBatch())
            {
                statusBanner.Text = "游戏在线 · 原生批次已就绪";
                statusBanner.ForeColor = ToneTextColor(AssistantUiTone.Success);
                statusBanner.BackColor = ToneBackgroundColor(AssistantUiTone.Success);
            }
            if (IsRestartRequiredForLastPresence())
            {
                statusBanner.Text = "必须完全重启游戏 · 当前进程禁止再次执行";
                statusBanner.ForeColor = ToneTextColor(AssistantUiTone.Error);
                statusBanner.BackColor = ToneBackgroundColor(AssistantUiTone.Error);
            }
        }

        private bool IsRestartRequiredForLastPresence()
        {
            return IsRestartRequiredForPresence(lastAttemptedPresence);
        }

        private bool IsRestartRequiredForPresence(GamePresenceSnapshot presence)
        {
            return restartRequiredGeneration != null
                && presence != null
                && restartRequiredGeneration.IsSameGenerationAs(presence);
        }

        private void RefreshRestartRequiredGeneration(GamePresenceSnapshot presence)
        {
            if (restartRequiredGeneration == null
                || presence == null
                || presence.Status != GamePresenceStatus.Online
                || restartRequiredGeneration.IsSameGenerationAs(presence))
            {
                return;
            }

            AppendLog("[READY] 检测到新的游戏进程代；旧进程的重启锁已解除。");
            restartRequiredGeneration = null;
            restartRequiredReason = string.Empty;
        }

        private void LatchRestartRequired(
            GamePresenceSnapshot generation,
            string reason,
            NativeBatchDiagnosticLog diagnosticLog)
        {
            if (generation == null || generation.Status != GamePresenceStatus.Online)
            {
                return;
            }

            restartRequiredGeneration = generation;
            restartRequiredReason = string.IsNullOrWhiteSpace(reason)
                ? NativeRestartRequiredMessage
                : reason.Trim();
            if (diagnosticLog != null)
            {
                diagnosticLog.Write(
                    "GAME_RESTART_REQUIRED_LATCHED",
                    "pid=" + generation.ProcessId + "; reason=" + restartRequiredReason);
            }
            AppendLog("[RESTART REQUIRED] " + restartRequiredReason);
        }

        private void RenderDiagnosticFailure(string connectionText, string logMessage)
        {
            latestAvailability = null;
            EnterTransientFault(connectionText + "；助手仍在运行。");
            AppendLog(logMessage);
        }

        private void EnterTransientFault(string detail)
        {
            transientFaultRetries.ScheduleIfNeeded(clock.UtcNow);
            string retryDetail;
            if (transientFaultRetries.HasPendingRetry
                && transientFaultRetries.RetryDueUtc.HasValue)
            {
                retryDetail = string.Format(
                    " 自动重试 {0}/{1} 最早于 {2}；也可手动重新检测。",
                    transientFaultRetries.ScheduledRetryCount,
                    transientFaultRetries.MaximumAutomaticRetries,
                    transientFaultRetries.RetryDueUtc.Value.ToLocalTime().ToString("HH:mm:ss"));
            }
            else
            {
                retryDetail = string.Format(
                    " 已达到 {0} 次自动重试上限；仅在手动重新检测或游戏进程换代后再试。",
                    transientFaultRetries.MaximumAutomaticRetries);
            }

            SetUiState(AssistantUiState.Faulted(
                (detail ?? "瞬时检测异常；助手仍在运行。") + retryDetail));
        }

        internal void HandleRecoverableUiException(Exception exception)
        {
            if (exception == null)
            {
                throw new ArgumentNullException("exception");
            }

            latestAvailability = null;
            previewAfterDiagnostics = false;
            diagnosticSessionGate.Invalidate();
            EnterTransientFault(
                "界面发生异常，但助手窗口会保留：" + exception.Message);
            AppendLog("[BLOCK] UI_THREAD_EXCEPTION: " + exception);
        }

        internal void RestoreFromSecondInstance()
        {
            if (isClosing || IsDisposed)
            {
                return;
            }

            if (!Visible)
            {
                Show();
            }

            if (WindowState == FormWindowState.Minimized)
            {
                WindowState = FormWindowState.Normal;
            }

            ShowInTaskbar = true;
            BringToFront();
            Activate();
        }

        internal AssistantUiState CurrentUiStateForTests
        {
            get { return uiState; }
        }

        internal void ReloadProfilesForTests()
        {
            LoadProfiles();
        }

        internal Button[] ProfileButtonsForTests
        {
            get { return profilePanel.Controls.OfType<Button>().ToArray(); }
        }

        internal string GetToolTipForTests(Control control)
        {
            return toolTip.GetToolTip(control);
        }

        internal void SetUiStateForTests(AssistantUiState state)
        {
            SetUiState(state);
        }

        internal bool CanStartNativeBatchForTests
        {
            get { return CanStartNativeBatch(); }
        }

        internal NativeBatchClickDisposition NativeBatchClickDispositionForTests
        {
            get { return NativeBatchClickDispositionForCurrentState(); }
        }

        internal void SetNativeBatchActiveForTests(bool active)
        {
            nativeBatchActive = active;
            RenderUiState();
        }

        internal static string NativeMenuConfirmationMessageForTests
        {
            get { return NativeMenuConfirmationMessage; }
        }

        internal static FormWindowState NativeBatchBackgroundWindowStateForTests
        {
            get { return NativeBatchBackgroundWindowState; }
        }

        internal static FormWindowState NativeBatchMenuPromptWindowStateForTests
        {
            get { return NativeBatchMenuPromptWindowState; }
        }

        internal static string NativeBatchTerminalStageForTests(NativeBatchResult result)
        {
            return NativeBatchTerminalStage(result);
        }

        internal static string NativeBatchSuccessLogLineForTests(NativeBatchResult result)
        {
            return NativeBatchSuccessLogLine(result);
        }

        internal void SetPendingNativeBatchForTests(NativeBatchKind? kind)
        {
            pendingNativeBatchKind = kind;
            RenderUiState();
        }

        internal void SetRestartRequiredForTests(GamePresenceSnapshot generation)
        {
            lastAttemptedPresence = generation;
            restartRequiredGeneration = generation;
            restartRequiredReason = NativeRestartRequiredMessage;
            RenderUiState();
        }

        internal void RefreshRestartRequiredForTests(GamePresenceSnapshot generation)
        {
            lastAttemptedPresence = generation;
            RefreshRestartRequiredGeneration(generation);
            RenderUiState();
        }

        private static Color ToneTextColor(AssistantUiTone tone)
        {
            switch (tone)
            {
                case AssistantUiTone.Information:
                    return Color.FromArgb(32, 86, 140);
                case AssistantUiTone.Warning:
                    return Color.FromArgb(128, 86, 0);
                case AssistantUiTone.Success:
                    return Color.DarkGreen;
                case AssistantUiTone.Error:
                    return Color.DarkRed;
                default:
                    return Color.FromArgb(70, 70, 70);
            }
        }

        private static Color ToneBackgroundColor(AssistantUiTone tone)
        {
            switch (tone)
            {
                case AssistantUiTone.Information:
                    return Color.FromArgb(225, 239, 252);
                case AssistantUiTone.Warning:
                    return Color.FromArgb(255, 242, 184);
                case AssistantUiTone.Success:
                    return Color.FromArgb(225, 244, 229);
                case AssistantUiTone.Error:
                    return Color.FromArgb(253, 226, 226);
                default:
                    return Color.FromArgb(232, 234, 237);
            }
        }

        private void AppendLog(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            logBox.Text = BoundedLogBuffer.Append(
                logBox.Text,
                message,
                MaximumLogCharacters,
                MaximumLogLines);
            logBox.SelectionStart = logBox.TextLength;
            logBox.ScrollToCaret();
        }

        private sealed class DiagnosticWorkResult
        {
            public DiagnosticWorkResult(
                UiDiagnosticSession session,
                San9Pk101AvailabilityReport availability)
            {
                Session = session;
                Availability = availability;
            }

            public UiDiagnosticSession Session { get; private set; }

            public San9Pk101AvailabilityReport Availability { get; private set; }
        }

        private sealed class NativeBatchWorkRequest
        {
            public NativeBatchWorkRequest(
                NativeBatchKind kind,
                NativeBatchDiagnosticLog diagnosticLog,
                GamePresenceSnapshot expectedPresence)
            {
                Kind = kind;
                DiagnosticLog = diagnosticLog;
                ExpectedPresence = expectedPresence;
            }

            public NativeBatchKind Kind { get; private set; }

            public NativeBatchDiagnosticLog DiagnosticLog { get; private set; }

            public GamePresenceSnapshot ExpectedPresence { get; private set; }
        }

        private sealed class NativeBatchWorkResult
        {
            public NativeBatchWorkResult(
                NativeBatchKind kind,
                NativeInspectResult inspect,
                NativeBatchResult batch,
                GamePresenceSnapshot expectedPresence)
            {
                Kind = kind;
                Inspect = inspect;
                Batch = batch;
                ExpectedPresence = expectedPresence;
            }

            public NativeBatchKind Kind { get; private set; }

            public NativeInspectResult Inspect { get; private set; }

            public NativeBatchResult Batch { get; private set; }

            public GamePresenceSnapshot ExpectedPresence { get; private set; }
        }
    }
}
