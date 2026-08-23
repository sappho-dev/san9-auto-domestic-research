using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using San9AutoDomestic.UI;

namespace San9AutoDomestic.UI.StateTests
{
    internal static class UiStateTests
    {
        public static IList<TestCase> All()
        {
            return new List<TestCase>
            {
                new TestCase("waiting state stays resident and retryable", WaitingStateStaysResident),
                new TestCase("WinForms message loop routes ThreadException", MessageLoopRoutesThreadException),
                new TestCase("read-only ready state is explicit and non-executing", ReadOnlyReadyIsExplicit),
                new TestCase("missing configuration disables preview", MissingConfigurationDisablesPreview),
                new TestCase("blocked snapshot remains diagnostic-only", BlockedSnapshotRemainsDiagnosticOnly),
                new TestCase("fault state keeps retry available", FaultStateKeepsRetryAvailable),
                new TestCase("recoverable UI exception keeps the form alive", RecoverableExceptionKeepsFormAlive),
                new TestCase("stop presentation follows safe boundary states", StopPresentationFollowsStates),
                new TestCase("presence generation identity is exact", PresenceGenerationIdentityIsExact),
                new TestCase("diagnostic session rejects stale completion", DiagnosticSessionRejectsStaleCompletion),
                new TestCase("stable blocking never auto-retries", StableBlockingNeverAutoRetries),
                new TestCase("transient retries are exponential and finite", TransientRetriesAreExponentialAndFinite),
                new TestCase("ready probe recovery retries exactly once", ReadyProbeRecoveryRetriesExactlyOnce),
                new TestCase("snapshot freshness is closed and bounded", SnapshotFreshnessIsBounded),
                new TestCase("log buffer retains bounded newest content", LogBufferRetainsBoundedNewestContent),
                new TestCase("profile reload disposes stale controls", ProfileReloadDisposesStaleControls),
                new TestCase("native controller arguments and startup are closed", NativeControllerArgumentsAndStartupAreClosed),
                new TestCase("native menu handshake sends exactly one signal", NativeMenuHandshakeSendsExactlyOneSignal),
                new TestCase("native batch success requires complete and final result", NativeBatchSuccessRequiresCompleteAndFinalResult),
                new TestCase("native batch diagnostic log is durable and complete", NativeBatchDiagnosticLogIsDurableAndComplete),
                new TestCase("native game focus handoff is exact and generation-bound", NativeGameFocusHandoffIsExactAndGenerationBound),
                new TestCase("native write confirmation states the exact scope", NativeWriteConfirmationStatesExactScope),
                new TestCase("automatic detection hides internal V2 details", AutomaticDetectionHidesInternalV2Details),
                new TestCase("profile buttons expose only Basic and Wealthy native batches", ProfileButtonsExposeOnlyNativeBatches),
                new TestCase("production UI assembly has no Win32 input dependency", ProductionUiHasNoWin32InputDependency),
                new TestCase("production dependency closure has a closed native surface", ProductionDependencyClosureHasReadOnlyNativeSurface),
                new TestCase("presence probe is replaceable by an offline fake", PresenceProbeIsReplaceable),
                new TestCase("activation signal survives primary startup", ActivationSignalSurvivesStartup),
                new TestCase("activation shutdown timeout defers handle disposal", ActivationShutdownTimeoutDefersDisposal)
            };
        }

        private static void MessageLoopRoutesThreadException()
        {
            const string Marker = "synthetic-message-loop-fault";
            bool observed = false;
            using (ApplicationContext context = new ApplicationContext())
            using (System.Windows.Forms.Timer throwingTimer =
                new System.Windows.Forms.Timer())
            using (System.Windows.Forms.Timer watchdogTimer =
                new System.Windows.Forms.Timer())
            {
                ThreadExceptionEventHandler handler =
                    delegate(object sender, ThreadExceptionEventArgs eventArgs)
                    {
                        observed = eventArgs.Exception != null
                            && string.Equals(
                                Marker,
                                eventArgs.Exception.Message,
                                StringComparison.Ordinal);
                        context.ExitThread();
                    };
                throwingTimer.Interval = 1;
                throwingTimer.Tick += delegate
                {
                    throwingTimer.Stop();
                    throw new InvalidOperationException(Marker);
                };
                watchdogTimer.Interval = 2000;
                watchdogTimer.Tick += delegate
                {
                    watchdogTimer.Stop();
                    context.ExitThread();
                };

                Application.ThreadException += handler;
                try
                {
                    throwingTimer.Start();
                    watchdogTimer.Start();
                    Application.Run(context);
                }
                finally
                {
                    Application.ThreadException -= handler;
                }
            }

            AssertEx.True(observed, "A real WinForms pump must route the synthetic UI exception.");
        }

        private static void WaitingStateStaysResident()
        {
            AssistantUiPresentation view = AssistantUiPresenter.Present(
                AssistantUiState.WaitingForGame("未发现游戏；助手仍在运行。"),
                true);
            AssertEx.Contains("助手保持运行", view.BannerText, "Waiting must be a resident state.");
            AssertEx.True(view.RefreshEnabled, "Waiting must allow an explicit retry.");
            AssertEx.False(view.PreviewEnabled, "Waiting must not expose a stale preview.");
            AssertEx.False(view.StopEnabled, "Waiting must not expose stop as an action.");
            AssertEx.Equal("无感执行未开放", view.StopButtonText, "Execution boundary must be explicit.");
        }

        private static void ReadOnlyReadyIsExplicit()
        {
            AssistantUiPresentation view = AssistantUiPresenter.Present(
                AssistantUiState.ReadOnlyReady(
                    1234,
                    new DateTimeOffset(2026, 8, 7, 4, 5, 6, TimeSpan.Zero),
                    "精确版本匹配",
                    "2 座城市"),
                true);
            AssertEx.Equal(AssistantUiTone.Warning, view.BannerTone, "Read-only mode must use the warning banner.");
            AssertEx.Contains("只读预览", view.BannerText, "Read-only scope must be visible.");
            AssertEx.Contains("无感执行开发中", view.BannerText, "The native execution development state must be visible.");
            AssertEx.Contains("未开放", view.BannerText, "Execution must not be implied.");
            AssertEx.Contains("句柄已释放", view.ConnectionText, "The snapshot connection is not persistent.");
            AssertEx.True(view.PreviewEnabled, "A ready, configured snapshot may be previewed.");
            AssertEx.False(view.StopEnabled, "Read-only mode has no running batch to stop.");
        }

        private static void MissingConfigurationDisablesPreview()
        {
            AssistantUiPresentation view = AssistantUiPresenter.Present(
                AssistantUiState.ReadOnlyReady(1, null, "匹配", "1 座"),
                false);
            AssertEx.False(view.PreviewEnabled, "A missing configuration must disable preview.");
            AssertEx.Contains("配置未就绪", view.BannerText, "The configuration failure must be visible.");
        }

        private static void BlockedSnapshotRemainsDiagnosticOnly()
        {
            AssistantUiPresentation view = AssistantUiPresenter.Present(
                AssistantUiState.ExecutionUnavailable(
                    "发现冲突，只能查看诊断。",
                    44,
                    DateTimeOffset.UtcNow,
                    "版本匹配",
                    "1 座",
                    true),
                true);
            AssertEx.True(view.PreviewEnabled, "A stable blocked snapshot may expose diagnostic detail.");
            AssertEx.Equal("查看只读阻断详情", view.PreviewButtonText, "The button must not imply execution.");
            AssertEx.False(view.StopEnabled, "A blocked snapshot is not a running command.");
        }

        private static void FaultStateKeepsRetryAvailable()
        {
            AssistantUiPresentation view = AssistantUiPresenter.Present(
                AssistantUiState.Faulted("检测异常；窗口仍保留。"),
                true);
            AssertEx.Contains("窗口保持运行", view.BannerText, "Fault must not look like application exit.");
            AssertEx.True(view.RefreshEnabled, "Fault must remain retryable.");
            AssertEx.False(view.PreviewEnabled, "Fault must discard preview authorization.");
        }

        private static void RecoverableExceptionKeepsFormAlive()
        {
            using (MainForm form = new MainForm())
            {
                form.HandleRecoverableUiException(
                    new InvalidOperationException("synthetic offline UI fault"));
                AssertEx.False(form.IsDisposed, "A recoverable UI exception must not dispose the main form.");
                AssertEx.Equal(
                    AssistantUiStateKind.Faulted,
                    form.CurrentUiStateForTests.Kind,
                    "The form must enter the explicit fault state.");
            }
        }

        private static void StopPresentationFollowsStates()
        {
            AssistantUiState basis = AssistantUiState.ReadOnlyReady(1, null, "匹配", "1 座");
            AssistantUiPresentation running = AssistantUiPresenter.Present(
                basis.WithBatchActivity(BatchActivityState.Running),
                true);
            AssertEx.True(running.StopEnabled, "Only a running batch may request stop.");
            AssertEx.Equal("请求停止", running.StopButtonText, "Running stop label mismatch.");
            AssertEx.False(running.PreviewEnabled, "A running batch must suppress preview actions.");
            AssertEx.False(running.RefreshEnabled, "A running batch must suppress diagnostics.");

            AssistantUiPresentation requested = AssistantUiPresenter.Present(
                basis.WithBatchActivity(BatchActivityState.StopRequested),
                true);
            AssertEx.False(requested.StopEnabled, "A stop request must be idempotent.");
            AssertEx.False(requested.PreviewEnabled, "StopRequested must suppress preview actions.");
            AssertEx.Contains("当前命令边界", requested.BannerText, "Safe-boundary behavior must be explicit.");

            AssistantUiPresentation stopped = AssistantUiPresenter.Present(
                basis.WithBatchActivity(BatchActivityState.Stopped),
                true);
            AssertEx.False(stopped.StopEnabled, "Stopped is a resident terminal UI state.");
            AssertEx.False(stopped.PreviewEnabled, "Stopped requires a fresh diagnostic before preview.");
            AssertEx.True(stopped.RefreshEnabled, "Stopped must allow a fresh diagnostic.");
            AssertEx.Contains("助手保持运行", stopped.BannerText, "Stopping a batch must not close the helper.");
        }

        private static void PresenceGenerationIdentityIsExact()
        {
            GamePresenceSnapshot first = GamePresenceSnapshot.Online(10, 100, @"D:\三国志9\10101749\San9PK.exe");
            GamePresenceSnapshot same = GamePresenceSnapshot.Online(10, 100, @"d:\三国志9\10101749\SAN9PK.EXE");
            GamePresenceSnapshot newPid = GamePresenceSnapshot.Online(11, 100, first.ImagePath);
            GamePresenceSnapshot newGeneration = GamePresenceSnapshot.Online(10, 101, first.ImagePath);
            AssertEx.True(first.IsSameGenerationAs(same), "Windows path casing must not create a false generation.");
            AssertEx.False(first.IsSameGenerationAs(newPid), "PID changes must invalidate a snapshot.");
            AssertEx.False(first.IsSameGenerationAs(newGeneration), "Creation-time changes must invalidate a snapshot.");
            AssertEx.False(first.IsSameGenerationAs(GamePresenceSnapshot.Offline()), "Offline cannot match an online generation.");
        }

        private static void DiagnosticSessionRejectsStaleCompletion()
        {
            GamePresenceSnapshot first = GamePresenceSnapshot.Online(10, 100, @"D:\San9PK.exe");
            GamePresenceSnapshot changed = GamePresenceSnapshot.Online(10, 101, @"D:\San9PK.exe");
            UiDiagnosticSessionGate gate = new UiDiagnosticSessionGate();
            UiDiagnosticSession session = gate.Begin(first);
            AssertEx.False(gate.TryComplete(session, changed), "A changed process generation must be rejected.");
            AssertEx.True(gate.TryComplete(session, first), "A matching session should complete once.");
            AssertEx.False(gate.TryComplete(session, first), "A completion must not be accepted twice.");

            UiDiagnosticSession invalidated = gate.Begin(first);
            gate.Invalidate();
            AssertEx.False(gate.TryComplete(invalidated, first), "An invalidated form/session must reject a late result.");
        }

        private static void StableBlockingNeverAutoRetries()
        {
            GamePresenceSnapshot online = GamePresenceSnapshot.Online(10, 100, @"D:\San9PK.exe");
            AssertEx.True(
                UiDiagnosticRetryPolicy.ShouldStart(
                    false,
                    online,
                    null,
                    AssistantUiStateKind.WaitingForGame,
                    false),
                "A newly online target must be inspected.");
            AssertEx.False(
                UiDiagnosticRetryPolicy.ShouldStart(
                    false,
                    online,
                    online,
                    AssistantUiStateKind.ReadOnlyReady,
                    false),
                "A stable ready target must not receive repeated full scans.");
            AssertEx.False(
                UiDiagnosticRetryPolicy.ShouldStart(
                    false,
                    online,
                    online,
                    AssistantUiStateKind.ExecutionUnavailable,
                    true),
                "A stable blocking result must ignore transient retry authorization.");
            AssertEx.True(
                UiDiagnosticRetryPolicy.ShouldStart(
                    true,
                    online,
                    online,
                    AssistantUiStateKind.ExecutionUnavailable,
                    false),
                "Manual refresh must recheck a stable blocking result.");
            GamePresenceSnapshot nextGeneration = GamePresenceSnapshot.Online(
                10,
                101,
                @"D:\San9PK.exe");
            AssertEx.True(
                UiDiagnosticRetryPolicy.ShouldStart(
                    false,
                    nextGeneration,
                    online,
                    AssistantUiStateKind.ExecutionUnavailable,
                    false),
                "A process-generation change must recheck a stable blocking result.");
            AssertEx.False(
                UiDiagnosticRetryPolicy.ShouldStart(
                    true,
                    GamePresenceSnapshot.Offline(),
                    online,
                    AssistantUiStateKind.Faulted,
                    true),
                "Even a forced refresh must not run a full scan while offline.");
        }

        private static void TransientRetriesAreExponentialAndFinite()
        {
            DateTimeOffset now = new DateTimeOffset(2026, 8, 7, 0, 0, 0, TimeSpan.Zero);
            TransientFaultRetrySchedule retries = new TransientFaultRetrySchedule(
                TimeSpan.FromSeconds(1),
                4);
            int[] expectedSeconds = new int[] { 1, 2, 4, 8 };
            for (int index = 0; index < expectedSeconds.Length; index++)
            {
                AssertEx.True(retries.ScheduleIfNeeded(now), "A bounded retry should be scheduled.");
                AssertEx.Equal(index + 1, retries.ScheduledRetryCount, "Retry count mismatch.");
                AssertEx.Equal(
                    now.AddSeconds(expectedSeconds[index]),
                    retries.RetryDueUtc.Value,
                    "Exponential retry delay mismatch.");
                AssertEx.True(retries.ScheduleIfNeeded(now), "Repeated fault polls must keep one pending retry.");
                AssertEx.Equal(index + 1, retries.ScheduledRetryCount, "Repeated polls must not consume retry budget.");
                AssertEx.False(
                    retries.TryConsume(retries.RetryDueUtc.Value.AddTicks(-1)),
                    "A retry must not be consumed before its boundary.");
                AssertEx.True(
                    retries.TryConsume(retries.RetryDueUtc.Value),
                    "A retry must be consumable exactly at its boundary.");
                AssertEx.False(
                    retries.TryConsume(retries.RetryDueUtc ?? now),
                    "One schedule must authorize only one retry.");
            }

            AssertEx.False(retries.ScheduleIfNeeded(now), "The automatic retry budget must be finite.");
            AssertEx.True(retries.IsExhausted, "The retry schedule must expose exhaustion.");
            retries.Reset();
            AssertEx.Equal(0, retries.ScheduledRetryCount, "Manual/generation reset must restore the budget.");
        }

        private static void ReadyProbeRecoveryRetriesExactlyOnce()
        {
            DateTimeOffset now = new DateTimeOffset(2026, 8, 7, 0, 0, 0, TimeSpan.Zero);
            GamePresenceSnapshot generation = GamePresenceSnapshot.Online(
                10,
                100,
                @"D:\San9PK.exe");
            TransientFaultRetrySchedule retries = new TransientFaultRetrySchedule(
                TimeSpan.FromSeconds(1),
                4);
            retries.ScheduleIfNeeded(now);

            bool beforeBoundary = retries.TryConsume(now.AddMilliseconds(999));
            AssertEx.False(
                UiDiagnosticRetryPolicy.ShouldStart(
                    false,
                    generation,
                    generation,
                    AssistantUiStateKind.Faulted,
                    beforeBoundary),
                "Ready -> ProbeFailed must respect its finite backoff.");

            bool firstAuthorization = retries.TryConsume(now.AddSeconds(1));
            AssertEx.True(
                UiDiagnosticRetryPolicy.ShouldStart(
                    false,
                    generation,
                    generation,
                    AssistantUiStateKind.Faulted,
                    firstAuthorization),
                "Same-generation recovery must trigger one diagnostic after backoff.");

            bool duplicateAuthorization = retries.TryConsume(now.AddSeconds(2));
            AssertEx.False(
                UiDiagnosticRetryPolicy.ShouldStart(
                    false,
                    generation,
                    generation,
                    AssistantUiStateKind.Faulted,
                    duplicateAuthorization),
                "The same recovery schedule must not trigger twice.");
        }

        private static void SnapshotFreshnessIsBounded()
        {
            DateTimeOffset completed = new DateTimeOffset(2026, 8, 7, 0, 0, 0, TimeSpan.Zero);
            TimeSpan age;
            AssertEx.True(
                SnapshotFreshness.IsFresh(completed.AddSeconds(30), completed, TimeSpan.FromSeconds(30), out age),
                "The exact maximum age is allowed.");
            AssertEx.False(
                SnapshotFreshness.IsFresh(completed.AddSeconds(30).AddTicks(1), completed, TimeSpan.FromSeconds(30), out age),
                "A snapshot beyond the maximum age must be rejected.");
            AssertEx.False(
                SnapshotFreshness.IsFresh(completed.AddTicks(-1), completed, TimeSpan.FromSeconds(30), out age),
                "A future-dated snapshot must be rejected.");
        }

        private static void LogBufferRetainsBoundedNewestContent()
        {
            string value = string.Empty;
            for (int index = 0; index < 20; index++)
            {
                value = BoundedLogBuffer.Append(
                    value,
                    "entry-" + index,
                    60,
                    5);
            }

            AssertEx.True(value.Length <= 60, "The log must honor its character limit.");
            AssertEx.True(
                BoundedLogBuffer.CountLines(value) <= 5,
                "The log must honor its line limit.");
            AssertEx.Contains("entry-19", value, "The newest log entry must be retained.");
            AssertEx.False(
                value.IndexOf("entry-0", StringComparison.Ordinal) >= 0,
                "Old log entries must be discarded from the start.");

            string huge = BoundedLogBuffer.Append(
                string.Empty,
                new string('x', 100) + "LATEST",
                20,
                5);
            AssertEx.True(huge.Length <= 20, "A single huge entry must be character-bounded.");
            AssertEx.Contains("LATEST", huge, "Character trimming must retain the newest suffix.");
        }

        private static void ProfileReloadDisposesStaleControls()
        {
            using (MainForm form = new MainForm())
            {
                form.ReloadProfilesForTests();
                Button[] first = form.ProfileButtonsForTests;
                AssertEx.True(first.Length > 0, "The test configuration must produce profile buttons.");
                AssertEx.True(
                    !string.IsNullOrEmpty(form.GetToolTipForTests(first[0])),
                    "A generated profile button must have explanatory ToolTip text.");

                form.ReloadProfilesForTests();
                Button[] second = form.ProfileButtonsForTests;
                AssertEx.Equal(first.Length, second.Length, "Refresh must keep the profile count stable.");
                foreach (Button oldButton in first)
                {
                    AssertEx.True(oldButton.IsDisposed, "Refresh must dispose every stale profile button.");
                    AssertEx.Equal(
                        string.Empty,
                        form.GetToolTipForTests(oldButton),
                        "Refresh must remove stale ToolTip registrations.");
                }

                form.ReloadProfilesForTests();
                AssertEx.Equal(
                    first.Length,
                    form.ProfileButtonsForTests.Length,
                    "Repeated refresh must not grow the profile control set.");
                foreach (Button oldButton in second)
                {
                    AssertEx.True(oldButton.IsDisposed, "Every prior generation must be disposed.");
                }
            }
        }

        private static void NativeControllerArgumentsAndStartupAreClosed()
        {
            AssertEx.Equal(
                "--inspect",
                NativeControllerClient.InspectArguments,
                "Inspect arguments must be fixed.");
            AssertEx.Equal(
                "--s8-basic-batch --confirm I_ACCEPT_BASIC_BATCH_COMMERCE_CULTIVATE_PINNED_UNTIL_GAME_RESTART",
                NativeControllerClient.ArgumentsFor(NativeBatchKind.Basic),
                "Basic arguments must be exact.");
            AssertEx.Equal(
                "--s8-wealthy-batch --confirm I_ACCEPT_WEALTHY_BATCH_PATROL_COMMERCE_CULTIVATE_TRAIN_REPAIR_PINNED_UNTIL_GAME_RESTART",
                NativeControllerClient.ArgumentsFor(NativeBatchKind.Wealthy),
                "Wealthy arguments must be exact.");

            string baseDirectory = Path.Combine(Path.GetTempPath(), "san9-ui-closed-launcher");
            ProcessStartInfo inspect = NativeControllerClient.InspectStartInfoForTests(baseDirectory);
            ProcessStartInfo basic = NativeControllerClient.BatchStartInfoForTests(
                baseDirectory,
                NativeBatchKind.Basic);
            string runtimeDirectory = Path.Combine(baseDirectory, "runtime");
            AssertEx.Equal(
                Path.Combine(runtimeDirectory, "controller.exe"),
                inspect.FileName,
                "The launcher path must be closed to runtime/controller.exe.");
            AssertEx.Equal(runtimeDirectory, inspect.WorkingDirectory, "Working directory mismatch.");
            AssertEx.False(inspect.UseShellExecute, "The helper must not use shell execution.");
            AssertEx.True(inspect.CreateNoWindow, "The helper console must be hidden.");
            AssertEx.Equal(ProcessWindowStyle.Hidden, inspect.WindowStyle, "Window style must be Hidden.");
            AssertEx.True(inspect.RedirectStandardOutput, "stdout must be redirected.");
            AssertEx.True(inspect.RedirectStandardError, "stderr must be redirected.");
            AssertEx.False(inspect.RedirectStandardInput, "Inspect must not expose stdin.");
            AssertEx.Equal(NativeControllerClient.InspectArguments, inspect.Arguments, "Inspect argv mismatch.");
            AssertEx.True(basic.RedirectStandardInput, "Batch stdin must be redirected.");
            AssertEx.Equal(NativeControllerClient.BasicArguments, basic.Arguments, "Basic argv mismatch.");
        }

        private static void NativeMenuHandshakeSendsExactlyOneSignal()
        {
            const string Waiting =
                "{\"mode\":\"s8-basic-batch\",\"phase\":\"WAITING_FOR_USER_MENU_SIGNAL\",\"game_pid\":123}";
            NativeBatchProtocol approved = new NativeBatchProtocol(NativeBatchKind.Basic);
            AssertEx.Equal(0, approved.SignalCount, "No signal may be sent before WAITING.");
            AssertEx.Equal(
                NativeProtocolAction.ConfirmCurrentCityMenu,
                approved.AcceptLine(Waiting),
                "WAITING must request the menu confirmation exactly once.");
            AssertEx.Equal(0, approved.SignalCount, "WAITING alone must not send the signal.");
            AssertEx.True(approved.RecordMenuDecision(true), "An explicit OK must authorize the signal.");
            AssertEx.Equal(1, approved.SignalCount, "An approved menu signal must be recorded once.");
            AssertEx.Equal(
                NativeProtocolAction.ProtocolFailure,
                approved.AcceptLine(Waiting),
                "A repeated WAITING request must fail closed.");
            AssertEx.Equal(1, approved.SignalCount, "Repeated WAITING must never send a second signal.");

            NativeBatchProtocol declined = new NativeBatchProtocol(NativeBatchKind.Basic);
            declined.AcceptLine(Waiting);
            AssertEx.False(declined.RecordMenuDecision(false), "Cancel must decline the menu signal.");
            AssertEx.Equal(0, declined.SignalCount, "Cancel must send no menu signal.");
            AssertEx.Equal(
                NativeControllerState.Declined,
                declined.Complete(1, string.Empty).State,
                "Cancel must remain a distinct terminal state.");
        }

        private static void NativeBatchSuccessRequiresCompleteAndFinalResult()
        {
            NativeBatchProtocol protocol = new NativeBatchProtocol(NativeBatchKind.Basic);
            protocol.AcceptLine(
                "{\"mode\":\"s8-basic-batch\",\"phase\":\"WAITING_FOR_USER_MENU_SIGNAL\"}");
            protocol.RecordMenuDecision(true);
            protocol.AcceptLine(
                "{\"mode\":\"s8-basic-batch\",\"phase\":\"STEP_REQUEST_PUBLISHED\",\"native_id\":1,\"top5\":[1,2,3,4,5]}");
            protocol.AcceptLine(
                "{\"mode\":\"s8-basic-batch\",\"phase\":\"BATCH_COMPLETE\",\"executed\":2,\"skipped\":0,\"rebind_required\":false}");
            protocol.AcceptLine("{\"mode\":\"s8-basic-batch\",\"result\":0}");
            NativeBatchResult success = protocol.Complete(0, string.Empty);
            AssertEx.Equal(NativeControllerState.Succeeded, success.State, "Exact success proof must pass.");
            AssertEx.Equal(2, success.Executed, "Executed count mismatch.");
            AssertEx.Equal(0, success.Skipped, "Skipped count mismatch.");
            AssertEx.Equal(4, success.RawJsonLines.Count, "Every NDJSON line must be retained.");

            NativeBatchProtocol missingComplete = new NativeBatchProtocol(NativeBatchKind.Basic);
            missingComplete.AcceptLine(
                "{\"mode\":\"s8-basic-batch\",\"phase\":\"WAITING_FOR_USER_MENU_SIGNAL\"}");
            missingComplete.RecordMenuDecision(true);
            missingComplete.AcceptLine("{\"mode\":\"s8-basic-batch\",\"result\":0}");
            AssertEx.Equal(
                NativeControllerState.Failed,
                missingComplete.Complete(0, string.Empty).State,
                "Final result without BATCH_COMPLETE must fail.");

            NativeBatchProtocol rebind = new NativeBatchProtocol(NativeBatchKind.Wealthy);
            rebind.AcceptLine(
                "{\"mode\":\"s8-wealthy-batch\",\"phase\":\"WAITING_FOR_USER_MENU_SIGNAL\"}");
            rebind.RecordMenuDecision(true);
            rebind.AcceptLine(
                "{\"mode\":\"s8-wealthy-batch\",\"phase\":\"BATCH_REBIND_REQUIRED\",\"executed\":1,\"skipped\":0}");
            NativeBatchResult rebindResult = rebind.Complete(23, string.Empty);
            AssertEx.Equal(
                NativeControllerState.RebindRequired,
                rebindResult.State,
                "Rebind must be a distinct terminal state.");
            AssertEx.Equal(1, rebindResult.Executed,
                "Rebind must retain the already executed count.");
            AssertEx.Equal(0, rebindResult.Skipped,
                "Rebind must retain the skipped count.");
            AssertEx.Contains(
                "停止前已执行 1，跳过 0",
                NativeControllerClient.DescribeJsonLine(
                    "{\"phase\":\"BATCH_REBIND_REQUIRED\",\"executed\":1,\"skipped\":0}"),
                "The visible rebind line must preserve completed work.");

            NativeBatchProtocol invalid = new NativeBatchProtocol(NativeBatchKind.Basic);
            AssertEx.Equal(
                NativeProtocolAction.ProtocolFailure,
                invalid.AcceptLine("not-json"),
                "Non-JSON output must fail closed.");
            AssertEx.Equal(
                NativeControllerState.Failed,
                invalid.Complete(0, string.Empty).State,
                "Protocol failure may not become success.");

            NativeBatchProtocol noAction = new NativeBatchProtocol(NativeBatchKind.Basic);
            noAction.AcceptLine(
                "{\"mode\":\"s8-basic-batch\",\"phase\":\"WAITING_FOR_USER_MENU_SIGNAL\"}");
            noAction.RecordMenuDecision(true);
            noAction.AcceptLine(
                "{\"mode\":\"s8-basic-batch\",\"phase\":\"STEP_SKIPPED\",\"native_id\":1,\"context_status\":14}");
            noAction.AcceptLine(
                "{\"mode\":\"s8-basic-batch\",\"phase\":\"BATCH_COMPLETE\",\"executed\":0,\"skipped\":2,\"rebind_required\":false}");
            noAction.AcceptLine("{\"mode\":\"s8-basic-batch\",\"result\":0}");
            NativeBatchResult noActionResult = noAction.Complete(0, string.Empty);
            AssertEx.Equal(
                NativeControllerState.Succeeded,
                noActionResult.State,
                "Zero executed remains a native success result.");
            AssertEx.Contains(
                "[NO ACTION]",
                MainForm.NativeBatchSuccessLogLineForTests(noActionResult),
                "Zero executed must not be presented as PASS.");
            AssertEx.False(
                MainForm.NativeBatchSuccessLogLineForTests(noActionResult)
                    .IndexOf("[PASS]", StringComparison.Ordinal) >= 0,
                "Zero executed must not use the PASS label.");
            AssertEx.Contains(
                "没有可执行项",
                MainForm.NativeBatchTerminalStageForTests(noActionResult),
                "The status bar must describe a no-action outcome plainly.");

            AssertEx.Contains(
                "当前可用武将少于 5 人",
                NativeControllerClient.DescribeJsonLine(
                    "{\"phase\":\"STEP_SKIPPED\",\"native_id\":1,\"context_status\":17}"),
                "Ready-officer skips must be translated.");
            AssertEx.Contains(
                "资金不足",
                NativeControllerClient.DescribeJsonLine(
                    "{\"phase\":\"STEP_SKIPPED\",\"native_id\":2,\"context_status\":12}"),
                "Money skips must be translated.");
            AssertEx.Contains(
                "该项数值已经完成",
                NativeControllerClient.DescribeJsonLine(
                    "{\"phase\":\"STEP_SKIPPED\",\"native_id\":3,\"context_status\":26}"),
                "Completed-value skips must be translated.");
            AssertEx.Contains(
                "本旬已经执行",
                NativeControllerClient.DescribeJsonLine(
                    "{\"phase\":\"STEP_SKIPPED\",\"native_id\":0,\"context_status\":14}"),
                "Order-conflict skips must be translated.");

            NativeInspectResult residentBridge =
                NativeControllerClient.ParseInspectResultForTests(
                    "{\"mode\":\"inspect\",\"ready\":false,\"status\":\"EASY_NOT_INSTALLED\","
                    + "\"game_pid\":40408,\"game_generation\":123456,\"detail_flags\":512,"
                    + "\"first_failure_point\":39}");
            AssertEx.True(
                residentBridge.RequiresGameRestart,
                "Exact idle-slot conflict point 39/flag 512 must require a full game restart.");
            AssertEx.Equal(40408, residentBridge.GameProcessId, "The restart latch must bind the game PID.");

            NativeBatchProtocol preWaitingFailure = new NativeBatchProtocol(NativeBatchKind.Wealthy);
            preWaitingFailure.AcceptLine(
                "{\"mode\":\"s8-wealthy-batch\",\"result\":10,\"game_pid\":40408,"
                + "\"restart_required\":1}");
            NativeBatchResult failedBeforeWaiting = preWaitingFailure.Complete(1, string.Empty);
            AssertEx.True(
                failedBeforeWaiting.RequiresGameRestart,
                "A native restart-required result before WAITING must survive protocol failure classification.");
            AssertEx.Equal(40408, failedBeforeWaiting.GameProcessId, "The failed batch PID must be retained.");
        }

        private static void NativeBatchDiagnosticLogIsDurableAndComplete()
        {
            string testRoot = Path.Combine(
                Path.GetTempPath(),
                "san9-native-batch-log-" + Guid.NewGuid().ToString("N"));
            string fullTestRoot = Path.GetFullPath(testRoot);
            try
            {
                NativeBatchDiagnosticLog diagnosticLog;
                string error;
                AssertEx.True(
                    NativeBatchDiagnosticLog.TryCreateInDirectory(
                        fullTestRoot,
                        NativeBatchKind.Wealthy,
                        out diagnosticLog,
                        out error),
                    "The durable batch log must be creatable: " + error);
                AssertEx.Equal(
                    Path.Combine(fullTestRoot, "native-batch-latest.log"),
                    diagnosticLog.LatestFilePath,
                    "The latest-log path must be exact.");
                AssertEx.False(
                    string.Equals(
                        diagnosticLog.FilePath,
                        diagnosticLog.LatestFilePath,
                        StringComparison.OrdinalIgnoreCase),
                    "Each session must have its own timestamped history file.");
                AssertEx.Contains(
                    "native-batch-",
                    Path.GetFileName(diagnosticLog.FilePath),
                    "The history file must be timestamp-named.");
                diagnosticLog.Write("CONTROLLER_STDOUT", "{\"phase\":\"WAITING_FOR_USER_MENU_SIGNAL\"}");
                diagnosticLog.Write("CONTROLLER_STDERR", "synthetic-stderr");
                diagnosticLog.Write("MENU_SIGNAL_SENT", NativeControllerClient.MenuSignal);
                diagnosticLog.Write("BATCH_PROCESS_EXIT", "exit=9");
                diagnosticLog.Write("BATCH_FINAL", "state=Failed error=synthetic");

                string path = diagnosticLog.FilePath;
                diagnosticLog = null;
                string persisted = File.ReadAllText(path);
                AssertEx.Contains("SESSION_START", persisted, "Session header must persist.");
                AssertEx.Contains("profile=Wealthy", persisted, "Profile must persist.");
                AssertEx.Contains("CONTROLLER_STDOUT", persisted, "Controller stdout must persist.");
                AssertEx.Contains("WAITING_FOR_USER_MENU_SIGNAL", persisted, "WAITING must persist.");
                AssertEx.Contains("CONTROLLER_STDERR", persisted, "Controller stderr must persist.");
                AssertEx.Contains("MENU_SIGNAL_SENT", persisted, "The signal transition must persist.");
                AssertEx.Contains("OPEN_CURRENT_CITY_MENU", persisted, "The exact signal must persist.");
                AssertEx.Contains("BATCH_PROCESS_EXIT", persisted, "The exit code must persist.");
                AssertEx.Contains("BATCH_FINAL", persisted, "The final decision must persist.");
                AssertEx.Equal(
                    persisted,
                    File.ReadAllText(Path.Combine(fullTestRoot, "native-batch-latest.log")),
                    "Latest must mirror the active session.");

                NativeBatchDiagnosticLog secondLog;
                AssertEx.True(
                    NativeBatchDiagnosticLog.TryCreateInDirectory(
                        fullTestRoot,
                        NativeBatchKind.Basic,
                        out secondLog,
                        out error),
                    "A second durable batch log must be creatable: " + error);
                secondLog.Write("SECOND_SESSION", "synthetic-second");
                AssertEx.False(
                    string.Equals(path, secondLog.FilePath, StringComparison.OrdinalIgnoreCase),
                    "A second session must not overwrite the first history path.");
                AssertEx.Contains(
                    "BATCH_FINAL",
                    File.ReadAllText(path),
                    "The first session history must survive the second session.");
                string latest = File.ReadAllText(secondLog.LatestFilePath);
                AssertEx.Contains("profile=Basic", latest, "Latest must point at the second session.");
                AssertEx.Contains("SECOND_SESSION", latest, "Latest must retain second-session events.");
                AssertEx.False(
                    latest.IndexOf("BATCH_FINAL", StringComparison.Ordinal) >= 0,
                    "Latest must not mix a prior session into the current session.");
            }
            finally
            {
                if (Directory.Exists(fullTestRoot))
                {
                    Directory.Delete(fullTestRoot, true);
                }
            }
        }

        private static void NativeGameFocusHandoffIsExactAndGenerationBound()
        {
            const string GamePath = @"D:\三国志9\10101749\San9PK.exe";
            GamePresenceSnapshot expected = GamePresenceSnapshot.Online(42, 123456, GamePath);
            FakeNativeGameWindowApi api = new FakeNativeGameWindowApi(
                new IntPtr(0x1234),
                42);
            NativeGameFocusHandoff handoff = new NativeGameFocusHandoff(
                new QueuePresenceProbe(expected, expected),
                api);
            NativeGameFocusHandoffResult result = handoff.TryHandoff(expected);
            AssertEx.True(result.Succeeded, "An exact user-initiated game focus handoff must pass.");
            AssertEx.Equal(1, api.RestoreCount, "The exact game window must be restored once.");
            AssertEx.Equal(1, api.ForegroundCount, "The exact game window must be foregrounded once.");
            AssertEx.Equal(new IntPtr(0x1234), result.WindowHandle, "The exact HWND must be retained.");

            GamePresenceSnapshot changed = GamePresenceSnapshot.Online(42, 123457, GamePath);
            FakeNativeGameWindowApi changedApi = new FakeNativeGameWindowApi(
                new IntPtr(0x1234),
                42);
            NativeGameFocusHandoff changedHandoff = new NativeGameFocusHandoff(
                new QueuePresenceProbe(expected, changed),
                changedApi);
            NativeGameFocusHandoffResult changedResult = changedHandoff.TryHandoff(expected);
            AssertEx.False(changedResult.Succeeded, "A generation change after focus must fail closed.");
            AssertEx.Equal(1, changedApi.ForegroundCount, "The handoff may issue at most one foreground call.");

            FakeNativeGameWindowApi rejectedApi = new FakeNativeGameWindowApi(
                new IntPtr(0x1234),
                42);
            rejectedApi.AllowForeground = false;
            NativeGameFocusHandoff rejectedHandoff = new NativeGameFocusHandoff(
                new QueuePresenceProbe(expected),
                rejectedApi);
            NativeGameFocusHandoffResult rejected = rejectedHandoff.TryHandoff(expected);
            AssertEx.False(rejected.Succeeded, "A rejected SetForegroundWindow must prevent controller startup.");
            AssertEx.Equal(1, rejectedApi.ForegroundCount, "A rejected handoff must not retry foregrounding.");
        }

        private static void NativeWriteConfirmationStatesExactScope()
        {
            string basic = MainForm.NativeWriteConfirmationText(NativeBatchKind.Basic);
            AssertEx.Contains("商业 → 开垦", basic, "Basic order must be explicit.");
            AssertEx.Contains("500", basic, "Basic maximum cost must be explicit.");
            AssertEx.Contains("10", basic, "Basic maximum officer count must be explicit.");
            AssertEx.Contains("失败不会自动重试", basic, "Failure retry policy must be explicit.");
            AssertEx.Contains("直到游戏完全重启", basic, "Bridge lifetime must be explicit.");
            AssertEx.Contains("战略地图", basic, "The initial game state must be explicit.");
            AssertEx.Contains("不要提前打开城市菜单", basic, "The menu must remain closed initially.");
            AssertEx.Contains("自动最小化", basic, "The game-idle handoff must be explicit.");
            AssertEx.Contains("fresh inspect", basic, "A fresh native inspect must be explicit.");
            AssertEx.Contains("不使用先前 V2 日志", basic, "Old save-state observations must not be authoritative.");
            AssertEx.Contains("同代核验的游戏窗口切到前台", basic, "The exact focus handoff must be explicit.");
            AssertEx.Contains("不模拟鼠标键盘", basic, "The focus handoff must not imply input simulation.");

            string wealthy = MainForm.NativeWriteConfirmationText(NativeBatchKind.Wealthy);
            AssertEx.Contains("巡察 → 商业 → 开垦 → 训练 → 修筑", wealthy, "Wealthy order must be explicit.");
            AssertEx.Contains("1000", wealthy, "Wealthy maximum cost must be explicit.");
            AssertEx.Contains("25", wealthy, "Wealthy maximum officer count must be explicit.");
            AssertEx.Contains("训练费用为 0", wealthy, "The zero-cost Train step must be explicit.");
            AssertEx.Contains("失败不会自动重试", wealthy, "Failure retry policy must be explicit.");
            AssertEx.Contains("直到游戏完全重启", wealthy, "Bridge lifetime must be explicit.");
            AssertEx.Contains("战略地图", wealthy, "The initial game state must be explicit.");
            AssertEx.Contains("不要提前打开城市菜单", wealthy, "The menu must remain closed initially.");
            AssertEx.Contains("自动最小化", wealthy, "The game-idle handoff must be explicit.");
            AssertEx.Contains("同代核验的游戏窗口切到前台", wealthy, "The exact focus handoff must be explicit.");
            AssertEx.Contains("不模拟鼠标键盘", wealthy, "The focus handoff must not imply input simulation.");

            AssertEx.Equal(
                FormWindowState.Minimized,
                MainForm.NativeBatchBackgroundWindowStateForTests,
                "The assistant must minimize while the controller waits for game idle.");
            AssertEx.Equal(
                FormWindowState.Normal,
                MainForm.NativeBatchMenuPromptWindowStateForTests,
                "The assistant must restore before the second confirmation.");
            string menuConfirmation = MainForm.NativeMenuConfirmationMessageForTests;
            AssertEx.Contains("切换到游戏", menuConfirmation, "The second prompt must send the user back to the game.");
            AssertEx.Contains("打开当前城市菜单", menuConfirmation, "The exact menu action must be explicit.");
            AssertEx.Contains("回到本窗口点击“确定”", menuConfirmation, "The return-to-UI step must be explicit.");
            AssertEx.Contains("唯一一次继续信号", menuConfirmation, "The one-signal boundary must be explicit.");

            string[] readOnlyMessages = MainForm.ReadOnlyBoundaryMessagesForTests;
            AssertEx.Equal(2, readOnlyMessages.Length, "Both explicit-preview boundaries must be covered.");
            AssertEx.Contains(
                "[READ-ONLY] 旧V2配置提交/停止仍禁用；绿色原生批次就绪时Basic/Wealthy可独立执行",
                readOnlyMessages[0],
                "Availability copy must preserve the V2 boundary without disabling native batches.");
            AssertEx.Contains(
                "该预览不授权旧V2配置提交/停止；绿色原生批次就绪时Basic/Wealthy可独立执行",
                readOnlyMessages[1],
                "Preview copy must preserve the V2 boundary without disabling native batches.");
            AssertEx.False(
                readOnlyMessages.Any(message => message.IndexOf("执行门禁始终关闭", StringComparison.Ordinal) >= 0
                    || message.IndexOf("所有配置方案按钮", StringComparison.Ordinal) >= 0),
                "Legacy copy must not claim that every native execution button is closed.");
        }

        private static void AutomaticDetectionHidesInternalV2Details()
        {
            AssertEx.False(
                MainForm.AvailabilityDetailsIncludedForTests(false),
                "Automatic detection must not render the full V2 report.");
            AssertEx.True(
                MainForm.AvailabilityDetailsIncludedForTests(true),
                "An explicit preview must retain the full read-only report.");

            string readyOutput = string.Join(
                "\n",
                MainForm.AutomaticDetectionMessagesForTests(true));
            AssertEx.Contains(
                "[DETECT] 正在检查游戏版本、当前城市和候选武将",
                readyOutput,
                "Default detection must use product-facing copy.");
            AssertEx.Contains(
                "[READY] 原生执行能力已就绪：Basic/Wealthy会实际修改当前城市，执行前会再次确认。",
                readyOutput,
                "Automatic success must state the real execution capability.");
            foreach (string hiddenToken in new string[]
                {
                    "[Warning]",
                    "[READ-ONLY]",
                    "PlanningReady",
                    "VerifiedNativeCapability"
                })
            {
                AssertEx.False(
                    readyOutput.IndexOf(hiddenToken, StringComparison.OrdinalIgnoreCase) >= 0,
                    "Automatic output must hide internal token: " + hiddenToken + ".");
            }

            string notReadyOutput = string.Join(
                "\n",
                MainForm.AutomaticDetectionMessagesForTests(false));
            AssertEx.Contains(
                "[NOT READY] 原生执行能力尚未就绪",
                notReadyOutput,
                "Automatic failure must state that native execution is not ready.");
            AssertEx.Contains(
                "[READ-ONLY]",
                MainForm.ReadOnlyBoundaryMessagesForTests[0],
                "An explicit preview must retain its read-only boundary.");
        }

        private static void ProfileButtonsExposeOnlyNativeBatches()
        {
            using (MainForm form = new MainForm())
            {
                form.ReloadProfilesForTests();
                Button[] buttons = form.ProfileButtonsForTests;
                AssertEx.Equal(2, buttons.Length, "Only Basic and Wealthy buttons may be generated.");
                AssertEx.True(
                    buttons.Any(button => Equals(button.Tag, NativeBatchKind.Basic)
                        && button.Text.IndexOf("基础内政", StringComparison.Ordinal) >= 0),
                    "The Basic native batch button is missing.");
                AssertEx.True(
                    buttons.Any(button => Equals(button.Tag, NativeBatchKind.Wealthy)
                        && button.Text.IndexOf("有钱内政", StringComparison.Ordinal) >= 0),
                    "The Wealthy native batch button is missing.");
                AssertEx.True(
                    buttons.All(button => button.Enabled),
                    "Loaded profiles must stay clickable while the game or read-only check is not ready.");
                AssertEx.False(
                    form.CanStartNativeBatchForTests,
                    "Waiting state must not authorize controller startup.");
                AssertEx.Equal(
                    NativeBatchClickDisposition.RefreshBeforeConfirm,
                    form.NativeBatchClickDispositionForTests,
                    "A not-ready click must force a fresh V2 detection before any controller can start.");

                form.SetUiStateForTests(AssistantUiState.ReadOnlyReady(
                    42,
                    DateTimeOffset.UtcNow,
                    "精确版本匹配",
                    "当前城市"));
                AssertEx.True(form.CanStartNativeBatchForTests, "Ready state must open the native batch gate.");
                AssertEx.Equal(
                    NativeBatchClickDisposition.RefreshBeforeConfirm,
                    form.NativeBatchClickDispositionForTests,
                    "Even a previously-ready same-PID state must be refreshed after a save switch.");
                AssertEx.True(
                    form.ProfileButtonsForTests.All(button => button.Enabled),
                    "Ready state must enable both native batch buttons.");

                form.SetPendingNativeBatchForTests(NativeBatchKind.Wealthy);
                AssertEx.Equal(
                    NativeBatchClickDisposition.RejectWithoutController,
                    form.NativeBatchClickDispositionForTests,
                    "A pending fresh detection must reject a second click.");
                AssertEx.True(
                    form.ProfileButtonsForTests.All(button => !button.Enabled),
                    "A pending same-click continuation must disable both buttons.");
                form.SetPendingNativeBatchForTests(null);

                GamePresenceSnapshot restartGeneration = GamePresenceSnapshot.Online(
                    42,
                    123456,
                    @"D:\三国志9\10101749\San9PK.exe");
                form.SetRestartRequiredForTests(restartGeneration);
                AssertEx.False(
                    form.CanStartNativeBatchForTests,
                    "A resident-Bridge generation must remain restart-locked.");
                AssertEx.True(
                    form.ProfileButtonsForTests.All(button => !button.Enabled),
                    "The same generation must disable both batch buttons.");
                form.RefreshRestartRequiredForTests(GamePresenceSnapshot.Online(
                    43,
                    123457,
                    @"D:\三国志9\10101749\San9PK.exe"));
                AssertEx.True(
                    form.CanStartNativeBatchForTests,
                    "A new exact game process generation must clear the old restart lock.");

                form.SetNativeBatchActiveForTests(true);
                AssertEx.False(form.CanStartNativeBatchForTests, "A running batch must close the session gate.");
                AssertEx.True(
                    form.ProfileButtonsForTests.All(button => !button.Enabled),
                    "A running batch must disable both buttons.");
            }
        }

        private static void ProductionUiHasNoWin32InputDependency()
        {
            string[] references = typeof(MainForm).Assembly
                .GetReferencedAssemblies()
                .Select(reference => reference.Name)
                .ToArray();
            AssertEx.False(
                references.Any(name => string.Equals(
                    name,
                    "San9AutoDomestic.Input.Win32",
                    StringComparison.OrdinalIgnoreCase)),
                "The production UI assembly must not reference the retired Win32 input executor.");
        }

        private static void ProductionDependencyClosureHasReadOnlyNativeSurface()
        {
            Assembly[] productionAssemblies = new Assembly[]
            {
                typeof(MainForm).Assembly,
                Assembly.Load("San9AutoDomestic.Adapter.San9Pk101"),
                Assembly.Load("San9AutoDomestic.Core")
            };
            HashSet<string> allowedSan9Dependencies = new HashSet<string>(
                new string[]
                {
                    "San9AutoDomestic.Adapter.San9Pk101",
                    "San9AutoDomestic.Core"
                },
                StringComparer.OrdinalIgnoreCase);
            HashSet<string> allowedNativeEntries = new HashSet<string>(
                new string[]
                {
                    "CloseHandle",
                    "CreateToolhelp32Snapshot",
                    "EnumWindows",
                    "FindWindow",
                    "GetClassName",
                    "GetProcessTimes",
                    "GetWindowThreadProcessId",
                    "Module32First",
                    "Module32Next",
                    "OpenProcess",
                    "QueryFullProcessImageName",
                    "ReadProcessMemory",
                    "VirtualQueryEx"
                },
                StringComparer.Ordinal);
            HashSet<string> exactFocusEntries = new HashSet<string>(
                new string[]
                {
                    "FindWindow",
                    "GetForegroundWindow",
                    "GetWindowThreadProcessId",
                    "IsWindow",
                    "SetForegroundWindow",
                    "ShowWindow"
                },
                StringComparer.Ordinal);
            int nativeEntryCount = 0;
            int exactFocusMutationCount = 0;

            foreach (Assembly assembly in productionAssemblies)
            {
                foreach (AssemblyName reference in assembly.GetReferencedAssemblies())
                {
                    if (reference.Name.StartsWith("San9AutoDomestic.", StringComparison.OrdinalIgnoreCase))
                    {
                        AssertEx.True(
                            allowedSan9Dependencies.Contains(reference.Name),
                            "Unexpected production dependency: " + reference.Name + ".");
                    }
                }

                foreach (Type type in assembly.GetTypes())
                {
                    foreach (MethodInfo method in type.GetMethods(
                        BindingFlags.Public
                        | BindingFlags.NonPublic
                        | BindingFlags.Static
                        | BindingFlags.Instance))
                    {
                        DllImportAttribute attribute = method
                            .GetCustomAttributes(typeof(DllImportAttribute), false)
                            .Cast<DllImportAttribute>()
                            .SingleOrDefault();
                        if (attribute == null)
                        {
                            continue;
                        }

                        nativeEntryCount++;
                        string entryPoint = string.IsNullOrWhiteSpace(attribute.EntryPoint)
                            ? method.Name
                            : attribute.EntryPoint;
                        bool exactFocusType = string.Equals(
                            type.FullName,
                            "San9AutoDomestic.UI.NativeGameFocusHandoff+NativeMethods",
                            StringComparison.Ordinal);
                        bool exactFocusEntry = exactFocusType
                            && exactFocusEntries.Contains(entryPoint);
                        if (exactFocusEntry
                            && (string.Equals(entryPoint, "SetForegroundWindow", StringComparison.Ordinal)
                                || string.Equals(entryPoint, "ShowWindow", StringComparison.Ordinal)))
                        {
                            exactFocusMutationCount++;
                        }
                        AssertEx.True(
                            allowedNativeEntries.Contains(entryPoint) || exactFocusEntry,
                            string.Format(
                                "Production dependency {0} exposes an unapproved native entry {1}.{2}.",
                                assembly.GetName().Name,
                                type.FullName,
                                entryPoint));
                    }
                }
            }

            AssertEx.True(nativeEntryCount > 0, "The native allowlist audit must inspect a non-empty surface.");
            AssertEx.Equal(
                2,
                exactFocusMutationCount,
                "Only exact ShowWindow and SetForegroundWindow handoff entries may change focus state.");
        }

        private static void PresenceProbeIsReplaceable()
        {
            QueuePresenceProbe fake = new QueuePresenceProbe(
                GamePresenceSnapshot.Offline(),
                GamePresenceSnapshot.Online(10, 100, @"D:\San9PK.exe"));
            AssertEx.Equal(GamePresenceStatus.Offline, fake.Probe().Status, "First fake state mismatch.");
            AssertEx.Equal(GamePresenceStatus.Online, fake.Probe().Status, "Second fake state mismatch.");
            AssertEx.Equal(2, fake.CallCount, "The fake must observe every poll without process access.");
        }

        private static void ActivationSignalSurvivesStartup()
        {
            using (EventWaitHandle activation = new EventWaitHandle(false, EventResetMode.AutoReset))
            using (ManualResetEvent observed = new ManualResetEvent(false))
            using (SingleInstanceActivationMonitor monitor =
                new SingleInstanceActivationMonitor(activation, delegate { observed.Set(); }))
            {
                activation.Set();
                monitor.Start();
                monitor.Start();
                AssertEx.True(observed.WaitOne(2000), "A signal sent before the listener starts must be retained.");
            }
        }

        private static void ActivationShutdownTimeoutDefersDisposal()
        {
            using (EventWaitHandle activation = new EventWaitHandle(false, EventResetMode.AutoReset))
            using (ManualResetEvent actionEntered = new ManualResetEvent(false))
            using (ManualResetEvent releaseAction = new ManualResetEvent(false))
            {
                SingleInstanceActivationMonitor monitor =
                    new SingleInstanceActivationMonitor(
                        activation,
                        delegate
                        {
                            actionEntered.Set();
                            releaseAction.WaitOne();
                        },
                        20);
                try
                {
                    monitor.Start();
                    activation.Set();
                    AssertEx.True(actionEntered.WaitOne(2000), "The listener action must start.");
                    monitor.Dispose();
                    AssertEx.True(
                        monitor.ShutdownTimedOutForTests,
                        "A blocked listener must report the bounded join timeout.");
                }
                finally
                {
                    releaseAction.Set();
                    AssertEx.True(
                        monitor.WaitForListenerExitForTests(2000),
                        "The background listener must exit safely after its action is released.");
                    monitor.Dispose();
                }
            }
        }

        private sealed class QueuePresenceProbe : IGamePresenceProbe
        {
            private readonly Queue<GamePresenceSnapshot> snapshots;

            public QueuePresenceProbe(params GamePresenceSnapshot[] snapshots)
            {
                this.snapshots = new Queue<GamePresenceSnapshot>(snapshots);
            }

            public int CallCount { get; private set; }

            public GamePresenceSnapshot Probe()
            {
                CallCount++;
                return snapshots.Dequeue();
            }
        }

        private sealed class FakeNativeGameWindowApi : INativeGameWindowApi
        {
            private readonly IntPtr window;
            private readonly uint processId;
            private IntPtr foregroundWindow;

            public FakeNativeGameWindowApi(IntPtr window, uint processId)
            {
                this.window = window;
                this.processId = processId;
                AllowForeground = true;
            }

            public bool AllowForeground { get; set; }

            public int RestoreCount { get; private set; }

            public int ForegroundCount { get; private set; }

            public IntPtr FindExactGameWindow()
            {
                return window;
            }

            public bool IsWindow(IntPtr candidate)
            {
                return candidate == window;
            }

            public uint GetWindowProcessId(IntPtr candidate, out uint outputProcessId)
            {
                outputProcessId = candidate == window ? processId : 0u;
                return candidate == window ? 1u : 0u;
            }

            public void RestoreWindow(IntPtr candidate)
            {
                if (candidate == window)
                {
                    RestoreCount++;
                }
            }

            public bool SetExactForegroundWindow(IntPtr candidate)
            {
                ForegroundCount++;
                if (AllowForeground && candidate == window)
                {
                    foregroundWindow = candidate;
                    return true;
                }
                return false;
            }

            public IntPtr GetForegroundWindow()
            {
                return foregroundWindow;
            }
        }
    }
}
