using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;

namespace San9AutoDomestic.CommandBroker.SelfTest
{
    internal static class CommandBrokerTests
    {
        private static int identityCounter;

        internal static int RunAll()
        {
            TestCase[] tests = new[]
            {
                new TestCase("offline_policy_and_x86_are_frozen", OfflinePolicyAndX86AreFrozen),
                new TestCase("production_assembly_has_no_live_escape_surface", ProductionAssemblyHasNoLiveEscapeSurface),
                new TestCase("process_session_identity_is_exact_and_immutable", ProcessSessionIdentityIsExactAndImmutable),
                new TestCase("named_mutex_is_current_user_only", NamedMutexIsCurrentUserOnly),
                new TestCase("named_mutex_blocks_second_lease", NamedMutexBlocksSecondLease),
                new TestCase("abandoned_mutex_requires_restart", AbandonedMutexRequiresRestart),
                new TestCase("named_mutex_ready_timeout_is_restart_required", NamedMutexReadyTimeoutIsRestartRequired),
                new TestCase("named_mutex_release_timeout_retains_unknown_ownership", NamedMutexReleaseTimeoutRetainsUnknownOwnership),
                new TestCase("named_mutex_release_failure_is_observable", NamedMutexReleaseFailureIsObservable),
                new TestCase("controlled_directory_rejects_unprotected_acl", ControlledDirectoryRejectsUnprotectedAcl),
                new TestCase("controlled_directory_rejects_foreign_sid_acl", ControlledDirectoryRejectsForeignSidAcl),
                new TestCase("controlled_directory_requires_inheritable_full_control", ControlledDirectoryRequiresInheritableFullControl),
                new TestCase("controlled_directory_revalidation_latches_replacement", ControlledDirectoryRevalidationLatchesReplacement),
                new TestCase("journal_happy_path_is_strict_and_terminal", JournalHappyPathIsStrictAndTerminal),
                new TestCase("journal_is_exclusive_while_operation_is_active", JournalIsExclusiveWhileOperationIsActive),
                new TestCase("journal_in_use_does_not_latch_restart", JournalInUseDoesNotLatchRestart),
                new TestCase("nonterminal_reopen_requires_restart", NonterminalReopenRequiresRestart),
                new TestCase("corrupt_journal_requires_restart", CorruptJournalRequiresRestart),
                new TestCase("partial_journal_requires_restart", PartialJournalRequiresRestart),
                new TestCase("terminal_journal_rejects_exact_replay", TerminalJournalRejectsExactReplay),
                new TestCase("new_consent_cannot_replay_same_fingerprint_and_stage", NewConsentCannotReplaySameFingerprintAndStage),
                new TestCase("renamed_terminal_journal_cannot_reenable_replay", RenamedTerminalJournalCannotReenableReplay),
                new TestCase("journal_moved_outside_glob_is_still_detected", JournalMovedOutsideGlobIsStillDetected),
                new TestCase("registered_journal_deletion_requires_restart", RegisteredJournalDeletionRequiresRestart),
                new TestCase("missing_ledger_with_artifact_requires_restart", MissingLedgerWithArtifactRequiresRestart),
                new TestCase("corrupt_ledger_requires_restart", CorruptLedgerRequiresRestart),
                new TestCase("unregistered_session_artifact_requires_restart", UnregisteredSessionArtifactRequiresRestart),
                new TestCase("journal_foreign_acl_latches_restart", JournalForeignAclLatchesRestart),
                new TestCase("ledger_foreign_acl_latches_restart", LedgerForeignAclLatchesRestart),
                new TestCase("session_ledger_refuses_entry_4097_without_writing", SessionLedgerRefusesEntry4097WithoutWriting),
                new TestCase("corrupt_then_restored_journal_remains_latched_on_same_host", CorruptThenRestoredJournalRemainsLatchedOnSameHost),
                new TestCase("bak_moved_then_restored_journal_remains_latched_on_same_host", BakMovedThenRestoredJournalRemainsLatchedOnSameHost),
                new TestCase("deleted_then_restored_ledger_remains_latched_on_same_host", DeletedThenRestoredLedgerRemainsLatchedOnSameHost),
                new TestCase("corrupt_then_restored_ledger_remains_latched_on_same_host", CorruptThenRestoredLedgerRemainsLatchedOnSameHost),
                new TestCase("nonterminal_scan_remains_latched_on_same_host", NonterminalScanRemainsLatchedOnSameHost),
                new TestCase("terminal_verified_allows_next_distinct_request", TerminalVerifiedAllowsNextDistinctRequest),
                new TestCase("abort_uncertain_latches_process_session", AbortUncertainLatchesProcessSession),
                new TestCase("binding_mismatch_is_rejected", BindingMismatchIsRejected),
                new TestCase("same_host_concurrent_start_is_single_flight", SameHostConcurrentStartIsSingleFlight),
                new TestCase("second_broker_is_blocked_by_process_mutex", SecondBrokerIsBlockedByProcessMutex),
                new TestCase("stop_before_start_blocks_new_operation", StopBeforeStartBlocksNewOperation),
                new TestCase("stop_before_side_effect_never_enters_boundary", StopBeforeSideEffectNeverEntersBoundary),
                new TestCase("stop_after_side_effect_is_deferred_to_terminal", StopAfterSideEffectIsDeferredToTerminal),
                new TestCase("ipc_round_trip_binds_nonce_hmac_and_sequence", IpcRoundTripBindsNonceHmacAndSequence),
                new TestCase("ipc_replay_is_rejected", IpcReplayIsRejected),
                new TestCase("ipc_gap_does_not_advance_receiver", IpcGapDoesNotAdvanceReceiver),
                new TestCase("ipc_wrong_hmac_does_not_advance_receiver", IpcWrongHmacDoesNotAdvanceReceiver),
                new TestCase("ipc_wrong_nonce_is_rejected_after_authentication", IpcWrongNonceIsRejectedAfterAuthentication),
                new TestCase("ipc_oversize_prefix_is_rejected_before_buffering", IpcOversizePrefixIsRejectedBeforeBuffering),
                new TestCase("ipc_partial_and_trailing_frames_are_rejected", IpcPartialAndTrailingFramesAreRejected),
                new TestCase("ipc_payload_bound_is_enforced", IpcPayloadBoundIsEnforced),
                new TestCase("in_memory_transport_is_bounded_and_clones", InMemoryTransportIsBoundedAndClones),
                new TestCase("operation_dispose_never_completes_or_replays", OperationDisposeNeverCompletesOrReplays)
                ,new TestCase("operation_release_timeout_is_observable_and_retriable", OperationReleaseTimeoutIsObservableAndRetriable)
                ,new TestCase("failed_retry_after_timeout_detaches_operation_but_keeps_latch", FailedRetryAfterTimeoutDetachesOperationButKeepsLatch)
                ,new TestCase("operation_release_failure_immediately_latches_host", OperationReleaseFailureImmediatelyLatchesHost)
                ,new TestCase("journal_write_failure_immediately_latches_host", JournalWriteFailureImmediatelyLatchesHost)
            };

            int passed = 0;
            foreach (TestCase test in tests)
            {
                test.Body();
                passed++;
                Console.WriteLine("PASS " + test.Name);
            }

            return passed;
        }

        private static void NamedMutexReadyTimeoutIsRestartRequired()
        {
            CurrentUserNamedMutex gate = new CurrentUserNamedMutex(RandomMutexName(), 0, 5000);
            NamedMutexAcquireResult result = gate.TryAcquire();
            AssertEx.True(
                result.Status == NamedMutexAcquireStatus.ReadyTimeoutRestartRequired
                    || result.Status == NamedMutexAcquireStatus.OwnershipReleaseTimeoutRestartRequired,
                "A bounded readiness timeout must fail closed as RestartRequired.");
            AssertEx.Null(result.Lease, "A readiness timeout must not publish a lease.");
        }

        private static void NamedMutexReleaseTimeoutRetainsUnknownOwnership()
        {
            CurrentUserNamedMutex gate = new CurrentUserNamedMutex(RandomMutexName());
            NamedMutexAcquireResult acquired = gate.TryAcquire();
            AssertEx.Equal(NamedMutexAcquireStatus.Acquired, acquired.Status, acquired.ErrorMessage);
            AssertEx.Equal(
                NamedMutexReleaseStatus.TimedOutOwnershipUnknown,
                acquired.Lease.Release(0),
                "A zero release budget must report unknown ownership.");
            AssertEx.True(acquired.Lease.IsHeld, "A release timeout must retain the lease owner for retry.");
            AssertEx.Equal(
                NamedMutexReleaseStatus.Released,
                acquired.Lease.Release(5000),
                "The same lease must permit bounded release recovery.");
            AssertEx.False(acquired.Lease.IsHeld, "A confirmed release may clear ownership.");
        }

        private static void NamedMutexReleaseFailureIsObservable()
        {
            CurrentUserNamedMutex gate = new CurrentUserNamedMutex(RandomMutexName());
            NamedMutexAcquireResult acquired = gate.TryAcquire();
            AssertEx.Equal(NamedMutexAcquireStatus.Acquired, acquired.Status, acquired.ErrorMessage);
            ForceLeaseOwnerStatus(acquired.Lease, NamedMutexAcquireStatus.Failed);
            AssertEx.Equal(
                NamedMutexReleaseStatus.FailedRestartRequired,
                acquired.Lease.Release(5000),
                "A completed owner thread with failed release status must not report Released.");
            AssertEx.False(acquired.Lease.IsHeld, "A completed failed owner is no longer retryable as a held lease.");
        }

        private static void ControlledDirectoryRejectsUnprotectedAcl()
        {
            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "San9AutoDomestic.CommandBroker.Uncontrolled." + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            try
            {
                AssertEx.Throws<UnauthorizedAccessException>(
                    delegate { new CommandBrokerHost(NewIdentity(), path).Dispose(); },
                    "An inherited/unprotected journal ACL must be rejected.");
            }
            finally
            {
                Directory.Delete(path, true);
            }
        }

        private static void ControlledDirectoryRejectsForeignSidAcl()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            {
                DirectorySecurity security = Directory.GetAccessControl(directory.Path);
                security.AddAccessRule(
                    new FileSystemAccessRule(
                        new SecurityIdentifier(WellKnownSidType.WorldSid, null),
                        FileSystemRights.Read,
                        AccessControlType.Allow));
                Directory.SetAccessControl(directory.Path, security);
                AssertEx.Throws<UnauthorizedAccessException>(
                    delegate { new CommandBrokerHost(NewIdentity(), directory.Path).Dispose(); },
                    "A protected directory with a foreign SID ACE must be rejected.");
            }
        }

        private static void ControlledDirectoryRevalidationLatchesReplacement()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            {
                ProcessSessionIdentity identity = NewIdentity();
                using (CommandBrokerHost host = new CommandBrokerHost(identity, directory.Path))
                {
                    DirectorySecurity security = Directory.GetAccessControl(directory.Path);
                    security.AddAccessRule(
                        new FileSystemAccessRule(
                            new SecurityIdentifier(WellKnownSidType.WorldSid, null),
                            FileSystemRights.Read,
                            AccessControlType.Allow));
                    Directory.SetAccessControl(directory.Path, security);
                    CommandBrokerStartResult result = host.TryStart(NewBinding(identity, "unsafe-directory", 1));
                    AssertEx.Equal(CommandBrokerStartStatus.JournalFailureRestartRequired, result.Status, result.ErrorMessage);
                    AssertEx.True(host.RestartRequired, "Directory contract loss after construction must latch restart.");
                }
            }
        }

        private static void ControlledDirectoryRequiresInheritableFullControl()
        {
            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "San9AutoDomestic.CommandBroker.NonInheritable." + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            try
            {
                SecurityIdentifier currentSid;
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                {
                    currentSid = identity.User;
                }

                DirectorySecurity security = new DirectorySecurity();
                security.SetOwner(currentSid);
                security.SetAccessRuleProtection(true, false);
                security.AddAccessRule(new FileSystemAccessRule(currentSid, FileSystemRights.FullControl, AccessControlType.Allow));
                Directory.SetAccessControl(path, security);
                AssertEx.Throws<UnauthorizedAccessException>(
                    delegate { new CommandBrokerHost(NewIdentity(), path).Dispose(); },
                    "FullControl without child-file and child-directory inheritance must be rejected.");
            }
            finally
            {
                Directory.Delete(path, true);
            }
        }

        private static void OfflinePolicyAndX86AreFrozen()
        {
            AssertEx.False(BrokerSafetyPolicy.LiveAuthorized, "Live authorization must remain false.");
            AssertEx.False(BrokerSafetyPolicy.ProcessAccessEnabled, "Process access must remain false.");
            AssertEx.False(BrokerSafetyPolicy.NativeCallbacksEnabled, "Native callbacks must remain false.");
            AssertEx.False(BrokerSafetyPolicy.ExternalTransportEnabled, "External transport must remain false.");
            AssertEx.Equal(4, IntPtr.Size, "Self-test must execute as x86.");
        }

        private static void ProductionAssemblyHasNoLiveEscapeSurface()
        {
            Assembly assembly = typeof(CommandBrokerHost).Assembly;
            object[] friends = assembly.GetCustomAttributes(typeof(System.Runtime.CompilerServices.InternalsVisibleToAttribute), false);
            AssertEx.Equal(0, friends.Length, "Production assembly must not expose internal authority to a friend assembly.");

            int transportTypes = 0;
            foreach (Type type in assembly.GetTypes())
            {
                if (type.Name.IndexOf("Transport", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    transportTypes++;
                    AssertEx.Equal(typeof(InMemoryFrameTransport), type, "Only the in-memory transport may exist.");
                }

                AssertEx.False(type.Name.IndexOf("NativeCallback", StringComparison.OrdinalIgnoreCase) >= 0, "Native callback types are forbidden.");
                AssertEx.False(type.Name.IndexOf("Bridge", StringComparison.OrdinalIgnoreCase) >= 0, "Bridge types are forbidden.");
                AssertEx.False(type.Name.IndexOf("Pipe", StringComparison.OrdinalIgnoreCase) >= 0, "Pipe types are forbidden.");
                AssertEx.False(type.Name.IndexOf("Socket", StringComparison.OrdinalIgnoreCase) >= 0, "Socket types are forbidden.");

                foreach (MethodInfo method in type.GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    object[] imports = method.GetCustomAttributes(typeof(DllImportAttribute), false);
                    AssertEx.Equal(0, imports.Length, "P/Invoke methods are forbidden.");
                }
            }

            AssertEx.Equal(1, transportTypes, "Exactly one in-memory transport type is expected.");
            foreach (AssemblyName reference in assembly.GetReferencedAssemblies())
            {
                AssertEx.False(reference.Name.IndexOf("San9Bridge", StringComparison.OrdinalIgnoreCase) >= 0, "Bridge references are forbidden.");
                AssertEx.False(reference.Name.IndexOf("V8Transaction", StringComparison.OrdinalIgnoreCase) >= 0, "V8 is intentionally not connected here.");
            }
        }

        private static void ProcessSessionIdentityIsExactAndImmutable()
        {
            string digest = Hash("identity");
            ProcessSessionIdentity left = new ProcessSessionIdentity(101, 123456789, digest.ToLowerInvariant());
            ProcessSessionIdentity same = new ProcessSessionIdentity(101, 123456789, digest);
            ProcessSessionIdentity differentCreation = new ProcessSessionIdentity(101, 123456790, digest);
            AssertEx.True(left.Equals(same), "Equivalent identities must compare equal.");
            AssertEx.False(left.Equals(differentCreation), "Creation FILETIME must participate in identity.");
            AssertEx.Equal(digest, left.ExecutableSha256, "Digest is normalized and returned by value.");
            AssertEx.Throws<ArgumentOutOfRangeException>(
                delegate { new ProcessSessionIdentity(0, 1, digest); },
                "PID zero must fail.");
            AssertEx.Throws<ArgumentException>(
                delegate { new ProcessSessionIdentity(1, 1, "00"); },
                "Short executable digests must fail.");
        }

        private static void NamedMutexIsCurrentUserOnly()
        {
            string name = RandomMutexName();
            CurrentUserNamedMutex gate = new CurrentUserNamedMutex(name);
            NamedMutexAcquireResult result = gate.TryAcquire();
            AssertEx.Equal(NamedMutexAcquireStatus.Acquired, result.Status, result.ErrorMessage);
            try
            {
                SecurityIdentifier currentSid;
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                {
                    currentSid = identity.User;
                }

                using (Mutex opened = Mutex.OpenExisting(name, MutexRights.ReadPermissions))
                {
                    MutexSecurity security = opened.GetAccessControl();
                    AssertEx.True(security.AreAccessRulesProtected, "Mutex ACL inheritance must be disabled.");
                    AuthorizationRuleCollection rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier));
                    bool currentUserAllow = false;
                    foreach (AuthorizationRule authorizationRule in rules)
                    {
                        MutexAccessRule rule = (MutexAccessRule)authorizationRule;
                        SecurityIdentifier sid = (SecurityIdentifier)rule.IdentityReference;
                        AssertEx.Equal(currentSid, sid, "No other SID may appear in the mutex ACL.");
                        AssertEx.False(rule.IsInherited, "No inherited ACE may appear in the mutex ACL.");
                        if (rule.AccessControlType == AccessControlType.Allow)
                        {
                            currentUserAllow = true;
                        }
                    }

                    AssertEx.True(currentUserAllow, "The current user needs an explicit allow ACE.");
                }
            }
            finally
            {
                result.Lease.Dispose();
            }
        }

        private static void NamedMutexBlocksSecondLease()
        {
            string name = RandomMutexName();
            NamedMutexAcquireResult first = new CurrentUserNamedMutex(name).TryAcquire();
            AssertEx.Equal(NamedMutexAcquireStatus.Acquired, first.Status, first.ErrorMessage);
            try
            {
                NamedMutexAcquireResult second = new CurrentUserNamedMutex(name).TryAcquire();
                AssertEx.Equal(NamedMutexAcquireStatus.Busy, second.Status, "Second lease must be rejected, including inside one process.");
                AssertEx.Null(second.Lease, "Busy acquisition must not expose a lease.");
            }
            finally
            {
                first.Lease.Dispose();
            }
        }

        private static void AbandonedMutexRequiresRestart()
        {
            ProcessSessionIdentity identity = NewIdentity();
            string name = CurrentUserNamedMutex.CreateName(identity);
            Mutex abandoned = null;
            ManualResetEvent ready = new ManualResetEvent(false);
            Thread owner = new Thread(delegate()
            {
                SecurityIdentifier sid;
                using (WindowsIdentity windowsIdentity = WindowsIdentity.GetCurrent())
                {
                    sid = windowsIdentity.User;
                }

                MutexSecurity security = new MutexSecurity();
                security.SetAccessRuleProtection(true, false);
                security.AddAccessRule(new MutexAccessRule(sid, MutexRights.FullControl, AccessControlType.Allow));
                bool created;
                abandoned = new Mutex(false, name, out created, security);
                abandoned.WaitOne();
                ready.Set();
            });
            owner.IsBackground = true;
            owner.Start();
            ready.WaitOne();
            owner.Join();

            try
            {
                NamedMutexAcquireResult result = new CurrentUserNamedMutex(name).TryAcquire();
                AssertEx.Equal(NamedMutexAcquireStatus.AbandonedRestartRequired, result.Status, result.ErrorMessage);
                AssertEx.Null(result.Lease, "An abandoned mutex must not produce an operational lease.");
            }
            finally
            {
                abandoned.Dispose();
                ready.Dispose();
            }
        }

        private static void JournalHappyPathIsStrictAndTerminal()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            using (CommandBrokerHost host = NewHost(directory))
            {
                ExecutionJournalBinding binding = NewBinding(GetIdentity(host), "happy", 1);
                CommandBrokerStartResult started = host.TryStart(binding);
                AssertStarted(started);
                CommandBrokerOperation operation = started.Operation;
                AssertEx.Equal(BrokerTransitionStatus.InvalidState, operation.RecordReceipt(), "Receipt cannot skip prior states.");
                AssertAdvanced(operation.Prepare());
                AssertAdvanced(operation.IssueIntent());
                AssertAdvanced(operation.TryEnterSideEffectBoundary());
                AssertAdvanced(operation.RecordReceipt());
                AssertAdvanced(operation.VerifyTerminal());
                AssertEx.True(operation.IsClosed, "Terminal operation must release its resources.");
                AssertEx.False(operation.RestartRequired, "Verified terminal completion must not require restart.");

                JournalInspection inspection = ExecutionJournalInspector.Inspect(operation.JournalPath);
                AssertEx.Equal(JournalInspectionStatus.ValidTerminal, inspection.Status, inspection.ErrorMessage);
                AssertEx.Equal(ExecutionJournalState.TerminalVerified, inspection.LastState.Value, "Wrong terminal journal state.");
                AssertEx.Equal(6L, inspection.RecordCount, "Happy path must contain all six durable states.");
                AssertEx.True(binding.Equals(inspection.Binding), "Every journal record must preserve the exact binding.");
            }
        }

        private static void JournalIsExclusiveWhileOperationIsActive()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            using (CommandBrokerHost host = NewHost(directory))
            {
                ExecutionJournalBinding binding = NewBinding(GetIdentity(host), "exclusive", 1);
                CommandBrokerStartResult started = host.TryStart(binding);
                AssertStarted(started);
                JournalInspection inspection = ExecutionJournalInspector.Inspect(started.Operation.JournalPath);
                AssertEx.Equal(JournalInspectionStatus.InUse, inspection.Status, "FileShare.None must reject a second journal opener.");
                AssertAdvanced(started.Operation.AbortUncertain());
            }
        }

        private static void JournalInUseDoesNotLatchRestart()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            {
                ProcessSessionIdentity identity = NewIdentity();
                using (CommandBrokerHost host = new CommandBrokerHost(identity, directory.Path))
                {
                    CommandBrokerStartResult first = host.TryStart(NewBinding(identity, "in-use-first", 1));
                    AssertStarted(first);
                    string journal = first.Operation.JournalPath;
                    CompleteVerified(first.Operation);
                    using (FileStream held = new FileStream(journal, FileMode.Open, FileAccess.Read, FileShare.None))
                    {
                        CommandBrokerStartResult blocked = host.TryStart(NewBinding(identity, "in-use-blocked", 1));
                        AssertEx.Equal(CommandBrokerStartStatus.JournalInUse, blocked.Status, blocked.ErrorMessage);
                        AssertEx.False(host.RestartRequired, "Journal InUse is retryable and must not latch restart.");
                    }

                    CommandBrokerStartResult afterRelease = host.TryStart(NewBinding(identity, "in-use-after-release", 1));
                    AssertStarted(afterRelease);
                    CompleteVerified(afterRelease.Operation);
                }
            }
        }

        private static void NonterminalReopenRequiresRestart()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            {
                ProcessSessionIdentity identity = NewIdentity();
                ExecutionJournalBinding binding = NewBinding(identity, "crash", 2);
                string path;
                using (CommandBrokerHost first = new CommandBrokerHost(identity, directory.Path))
                {
                    CommandBrokerStartResult started = first.TryStart(binding);
                    AssertStarted(started);
                    path = started.Operation.JournalPath;
                    AssertAdvanced(started.Operation.Prepare());
                    AssertAdvanced(started.Operation.IssueIntent());
                    started.Operation.Dispose();
                }

                JournalInspection inspection = ExecutionJournalInspector.Inspect(path);
                AssertEx.Equal(JournalInspectionStatus.ValidNonterminal, inspection.Status, inspection.ErrorMessage);
                AssertEx.Equal(ExecutionJournalState.IntentIssued, inspection.LastState.Value, "Crash state must remain nonterminal.");
                using (CommandBrokerHost second = new CommandBrokerHost(identity, directory.Path))
                {
                    CommandBrokerStartResult reopened = second.TryStart(binding);
                    AssertEx.Equal(CommandBrokerStartStatus.RestartRequired, reopened.Status, reopened.ErrorMessage);
                    AssertEx.Null(reopened.Operation, "Nonterminal journals are never resumed.");
                    AssertEx.True(second.RestartRequired, "A nonterminal scan must latch the same host.");
                }
            }
        }

        private static void CorruptJournalRequiresRestart()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            {
                ProcessSessionIdentity identity = NewIdentity();
                ExecutionJournalBinding binding = NewBinding(identity, "corrupt", 1);
                string path = CreateNonterminalJournal(directory.Path, identity, binding);
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    stream.Position = stream.Length - 1;
                    int value = stream.ReadByte();
                    stream.Position = stream.Length - 1;
                    stream.WriteByte((byte)(value ^ 0x5A));
                    stream.Flush(true);
                }

                using (CommandBrokerHost host = new CommandBrokerHost(identity, directory.Path))
                {
                    CommandBrokerStartResult result = host.TryStart(binding);
                    AssertEx.Equal(CommandBrokerStartStatus.JournalCorruptRestartRequired, result.Status, result.ErrorMessage);
                    AssertEx.True(host.RestartRequired, "A corrupt journal must latch the same host.");
                    AssertStillLatched(host, identity, "after-corrupt-journal");
                }
            }
        }

        private static void PartialJournalRequiresRestart()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            {
                ProcessSessionIdentity identity = NewIdentity();
                ExecutionJournalBinding binding = NewBinding(identity, "partial", 1);
                string path = CreateNonterminalJournal(directory.Path, identity, binding);
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    stream.SetLength(stream.Length - 1);
                    stream.Flush(true);
                }

                using (CommandBrokerHost host = new CommandBrokerHost(identity, directory.Path))
                {
                    CommandBrokerStartResult result = host.TryStart(binding);
                    AssertEx.Equal(CommandBrokerStartStatus.JournalCorruptRestartRequired, result.Status, result.ErrorMessage);
                }
            }
        }

        private static void TerminalJournalRejectsExactReplay()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            {
                ProcessSessionIdentity identity = NewIdentity();
                ExecutionJournalBinding binding = NewBinding(identity, "replay", 1);
                using (CommandBrokerHost first = new CommandBrokerHost(identity, directory.Path))
                {
                    CommandBrokerStartResult result = first.TryStart(binding);
                    AssertStarted(result);
                    CompleteVerified(result.Operation);
                }

                using (CommandBrokerHost second = new CommandBrokerHost(identity, directory.Path))
                {
                    CommandBrokerStartResult replay = second.TryStart(binding);
                    AssertEx.Equal(CommandBrokerStartStatus.ReplayRejected, replay.Status, replay.ErrorMessage);
                    AssertEx.Null(replay.Operation, "Replay must not expose an operation.");
                    AssertEx.False(second.RestartRequired, "Replay rejection must not latch restart.");
                    CommandBrokerStartResult distinct = second.TryStart(NewBinding(identity, "after-replay", 1));
                    AssertStarted(distinct);
                    CompleteVerified(distinct.Operation);
                }
            }
        }

        private static void NewConsentCannotReplaySameFingerprintAndStage()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            {
                ProcessSessionIdentity identity = NewIdentity();
                ExecutionJournalBinding firstBinding = NewBinding(identity, "same-durable-request", 7);
                using (CommandBrokerHost host = new CommandBrokerHost(identity, directory.Path))
                {
                    CommandBrokerStartResult first = host.TryStart(firstBinding);
                    AssertStarted(first);
                    CompleteVerified(first.Operation);
                    ExecutionJournalBinding newConsent = new ExecutionJournalBinding(
                        identity,
                        firstBinding.RequestFingerprint,
                        firstBinding.StageOrdinal,
                        Guid.NewGuid());
                    CommandBrokerStartResult replay = host.TryStart(newConsent);
                    AssertEx.Equal(
                        CommandBrokerStartStatus.ReplayRejected,
                        replay.Status,
                        "Changing only ConsentId must not reset durable replay identity.");
                    AssertEx.False(host.RestartRequired, "Replay rejection remains a safe non-latching result.");
                    CommandBrokerStartResult distinct = host.TryStart(NewBinding(identity, "fresh-durable-request", 7));
                    AssertStarted(distinct);
                    CompleteVerified(distinct.Operation);
                }
            }
        }

        private static void TerminalVerifiedAllowsNextDistinctRequest()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            {
                ProcessSessionIdentity identity = NewIdentity();
                using (CommandBrokerHost first = new CommandBrokerHost(identity, directory.Path))
                {
                    CommandBrokerStartResult result = first.TryStart(NewBinding(identity, "first", 1));
                    AssertStarted(result);
                    CompleteVerified(result.Operation);
                }

                using (CommandBrokerHost second = new CommandBrokerHost(identity, directory.Path))
                {
                    CommandBrokerStartResult next = second.TryStart(NewBinding(identity, "second", 1));
                    AssertStarted(next);
                    CompleteVerified(next.Operation);
                }
            }
        }

        private static void RenamedTerminalJournalCannotReenableReplay()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            {
                ProcessSessionIdentity identity = NewIdentity();
                ExecutionJournalBinding binding = NewBinding(identity, "renamed-replay", 1);
                string originalPath;
                using (CommandBrokerHost first = new CommandBrokerHost(identity, directory.Path))
                {
                    CommandBrokerStartResult result = first.TryStart(binding);
                    AssertStarted(result);
                    originalPath = result.Operation.JournalPath;
                    CompleteVerified(result.Operation);
                }

                string fileName = System.IO.Path.GetFileName(originalPath);
                int operationTokenSeparator = fileName.LastIndexOf('-');
                string renamedFile = fileName.Substring(0, operationTokenSeparator + 1)
                    + new string('F', 64)
                    + ".journal";
                string renamedPath = System.IO.Path.Combine(directory.Path, renamedFile);
                File.Move(originalPath, renamedPath);

                using (CommandBrokerHost second = new CommandBrokerHost(identity, directory.Path))
                {
                    CommandBrokerStartResult replay = second.TryStart(binding);
                    AssertEx.Equal(
                        CommandBrokerStartStatus.JournalCorruptRestartRequired,
                        replay.Status,
                        "Renaming a terminal journal must fail closed instead of re-enabling the request.");
                }
            }
        }

        private static void JournalMovedOutsideGlobIsStillDetected()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            {
                ProcessSessionIdentity identity = NewIdentity();
                ExecutionJournalBinding binding = NewBinding(identity, "moved-outside-glob", 1);
                string path;
                using (CommandBrokerHost first = new CommandBrokerHost(identity, directory.Path))
                {
                    CommandBrokerStartResult result = first.TryStart(binding);
                    AssertStarted(result);
                    path = result.Operation.JournalPath;
                    CompleteVerified(result.Operation);
                }

                File.Move(path, System.IO.Path.Combine(directory.Path, "unrelated-name.bak"));
                using (CommandBrokerHost reopened = new CommandBrokerHost(identity, directory.Path))
                {
                    AssertEx.Equal(
                        CommandBrokerStartStatus.JournalCorruptRestartRequired,
                        reopened.TryStart(binding).Status,
                        "The ledger must detect a journal moved outside the session glob.");
                }
            }
        }

        private static void RegisteredJournalDeletionRequiresRestart()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            {
                ProcessSessionIdentity identity = NewIdentity();
                ExecutionJournalBinding binding = NewBinding(identity, "deleted-journal", 1);
                string path;
                using (CommandBrokerHost first = new CommandBrokerHost(identity, directory.Path))
                {
                    CommandBrokerStartResult result = first.TryStart(binding);
                    AssertStarted(result);
                    path = result.Operation.JournalPath;
                    CompleteVerified(result.Operation);
                }

                File.Delete(path);
                using (CommandBrokerHost reopened = new CommandBrokerHost(identity, directory.Path))
                {
                    AssertEx.Equal(
                        CommandBrokerStartStatus.JournalCorruptRestartRequired,
                        reopened.TryStart(binding).Status,
                        "A ledger-registered journal may not disappear.");
                    AssertEx.True(reopened.RestartRequired, "A deleted registered journal must latch the same host.");
                    AssertStillLatched(reopened, identity, "after-deleted-journal");
                }
            }
        }

        private static void MissingLedgerWithArtifactRequiresRestart()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            {
                ProcessSessionIdentity identity = NewIdentity();
                ExecutionJournalBinding binding = NewBinding(identity, "deleted-ledger", 1);
                using (CommandBrokerHost first = new CommandBrokerHost(identity, directory.Path))
                {
                    CommandBrokerStartResult result = first.TryStart(binding);
                    AssertStarted(result);
                    CompleteVerified(result.Operation);
                }

                File.Delete(FindOnlyLedger(directory.Path));
                using (CommandBrokerHost reopened = new CommandBrokerHost(identity, directory.Path))
                {
                    AssertEx.Equal(
                        CommandBrokerStartStatus.JournalCorruptRestartRequired,
                        reopened.TryStart(binding).Status,
                        "Session artifacts without their canonical ledger must fail closed.");
                    AssertEx.True(reopened.RestartRequired, "A deleted ledger must latch the same host.");
                    AssertStillLatched(reopened, identity, "after-deleted-ledger");
                }
            }
        }

        private static void CorruptLedgerRequiresRestart()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            {
                ProcessSessionIdentity identity = NewIdentity();
                ExecutionJournalBinding binding = NewBinding(identity, "corrupt-ledger", 1);
                using (CommandBrokerHost first = new CommandBrokerHost(identity, directory.Path))
                {
                    CommandBrokerStartResult result = first.TryStart(binding);
                    AssertStarted(result);
                    CompleteVerified(result.Operation);
                }

                string ledger = FindOnlyLedger(directory.Path);
                using (FileStream stream = new FileStream(ledger, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    stream.Position = stream.Length - 1;
                    int value = stream.ReadByte();
                    stream.Position = stream.Length - 1;
                    stream.WriteByte((byte)(value ^ 0x33));
                    stream.Flush(true);
                }

                using (CommandBrokerHost reopened = new CommandBrokerHost(identity, directory.Path))
                {
                    AssertEx.Equal(
                        CommandBrokerStartStatus.JournalCorruptRestartRequired,
                        reopened.TryStart(binding).Status,
                        "A corrupt canonical ledger must require restart.");
                    AssertEx.True(reopened.RestartRequired, "A corrupt ledger must latch the same host.");
                    AssertStillLatched(reopened, identity, "after-corrupt-ledger");
                }
            }
        }

        private static void UnregisteredSessionArtifactRequiresRestart()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            {
                ProcessSessionIdentity identity = NewIdentity();
                ExecutionJournalBinding binding = NewBinding(identity, "extra-artifact", 1);
                string journalPath;
                using (CommandBrokerHost first = new CommandBrokerHost(identity, directory.Path))
                {
                    CommandBrokerStartResult result = first.TryStart(binding);
                    AssertStarted(result);
                    journalPath = result.Operation.JournalPath;
                    CompleteVerified(result.Operation);
                }

                File.WriteAllBytes(journalPath + ".bak", new byte[] { 1, 2, 3 });
                using (CommandBrokerHost reopened = new CommandBrokerHost(identity, directory.Path))
                {
                    AssertEx.Equal(
                        CommandBrokerStartStatus.JournalCorruptRestartRequired,
                        reopened.TryStart(NewBinding(identity, "next-after-extra", 1)).Status,
                        "A .bak or other unregistered session artifact must fail closed.");
                }
            }
        }

        private static void SessionLedgerRefusesEntry4097WithoutWriting()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            {
                ProcessSessionIdentity identity = NewIdentity();
                string ledger = System.IO.Path.Combine(directory.Path, "capacity.ledger");
                using (FileStream stream = new FileStream(ledger, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    for (int index = 0; index < 4096; index++)
                    {
                        ExecutionJournalBinding binding = NewBinding(identity, "capacity-" + index, index + 1);
                        string fileName = "S9CB-" + new string('A', 64) + "-" + index.ToString("X64") + ".journal";
                        byte[] payload = SerializeLedgerTestEntry(index + 1L, binding, fileName);
                        byte[] digest;
                        using (SHA256 algorithm = SHA256.Create())
                        {
                            digest = algorithm.ComputeHash(payload);
                        }

                        stream.Write(BitConverter.GetBytes(payload.Length), 0, sizeof(int));
                        stream.Write(payload, 0, payload.Length);
                        stream.Write(digest, 0, digest.Length);
                    }

                    stream.Flush(true);
                }

                long before = new FileInfo(ledger).Length;
                Type ledgerType = typeof(CommandBrokerHost).Assembly.GetType("San9AutoDomestic.CommandBroker.SessionLedger", true);
                MethodInfo append = ledgerType.GetMethod("TryAppend", BindingFlags.Static | BindingFlags.NonPublic);
                object[] arguments = new object[]
                {
                    ledger,
                    NewBinding(identity, "capacity-overflow", 4097),
                    "S9CB-" + new string('B', 64) + "-" + new string('C', 64) + ".journal",
                    null
                };
                object status = append.Invoke(null, arguments);
                AssertEx.Equal("CorruptRestartRequired", status.ToString(), "Entry 4097 must be refused before any write.");
                AssertEx.Equal(before, new FileInfo(ledger).Length, "Capacity rejection must leave the ledger byte-for-byte length unchanged.");
            }
        }

        private static void JournalForeignAclLatchesRestart()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            {
                ProcessSessionIdentity identity = NewIdentity();
                using (CommandBrokerHost host = new CommandBrokerHost(identity, directory.Path))
                {
                    CommandBrokerStartResult first = host.TryStart(NewBinding(identity, "journal-acl-first", 1));
                    AssertStarted(first);
                    string journal = first.Operation.JournalPath;
                    CompleteVerified(first.Operation);
                    AddWorldReadAce(journal);
                    AssertRestartLatched(host, identity, "journal-acl-detect");
                }
            }
        }

        private static void LedgerForeignAclLatchesRestart()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            {
                ProcessSessionIdentity identity = NewIdentity();
                using (CommandBrokerHost host = new CommandBrokerHost(identity, directory.Path))
                {
                    CommandBrokerStartResult first = host.TryStart(NewBinding(identity, "ledger-acl-first", 1));
                    AssertStarted(first);
                    CompleteVerified(first.Operation);
                    AddWorldReadAce(FindOnlyLedger(directory.Path));
                    AssertRestartLatched(host, identity, "ledger-acl-detect");
                }
            }
        }

        private static void CorruptThenRestoredJournalRemainsLatchedOnSameHost()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            {
                ProcessSessionIdentity identity = NewIdentity();
                using (CommandBrokerHost host = new CommandBrokerHost(identity, directory.Path))
                {
                    ExecutionJournalBinding firstBinding = NewBinding(identity, "latch-corrupt-journal-first", 1);
                    CommandBrokerStartResult first = host.TryStart(firstBinding);
                    AssertStarted(first);
                    string journal = first.Operation.JournalPath;
                    CompleteVerified(first.Operation);
                    byte[] original = File.ReadAllBytes(journal);
                    byte[] corrupt = (byte[])original.Clone();
                    corrupt[corrupt.Length - 1] ^= 0x51;
                    File.WriteAllBytes(journal, corrupt);
                    AssertRestartLatched(host, identity, "latch-corrupt-journal-detect");
                    File.WriteAllBytes(journal, original);
                    AssertStillLatched(host, identity, "latch-corrupt-journal-after-restore");
                }
            }
        }

        private static void BakMovedThenRestoredJournalRemainsLatchedOnSameHost()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            {
                ProcessSessionIdentity identity = NewIdentity();
                using (CommandBrokerHost host = new CommandBrokerHost(identity, directory.Path))
                {
                    CommandBrokerStartResult first = host.TryStart(NewBinding(identity, "latch-delete-journal-first", 1));
                    AssertStarted(first);
                    string journal = first.Operation.JournalPath;
                    CompleteVerified(first.Operation);
                    string backup = journal + ".bak";
                    File.Move(journal, backup);
                    AssertRestartLatched(host, identity, "latch-bak-journal-detect");
                    File.Move(backup, journal);
                    AssertStillLatched(host, identity, "latch-bak-journal-after-restore");
                }
            }
        }

        private static void DeletedThenRestoredLedgerRemainsLatchedOnSameHost()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            {
                ProcessSessionIdentity identity = NewIdentity();
                using (CommandBrokerHost host = new CommandBrokerHost(identity, directory.Path))
                {
                    CommandBrokerStartResult first = host.TryStart(NewBinding(identity, "latch-delete-ledger-first", 1));
                    AssertStarted(first);
                    CompleteVerified(first.Operation);
                    string ledger = FindOnlyLedger(directory.Path);
                    byte[] original = File.ReadAllBytes(ledger);
                    File.Delete(ledger);
                    AssertRestartLatched(host, identity, "latch-delete-ledger-detect");
                    File.WriteAllBytes(ledger, original);
                    AssertStillLatched(host, identity, "latch-delete-ledger-after-restore");
                }
            }
        }

        private static void CorruptThenRestoredLedgerRemainsLatchedOnSameHost()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            {
                ProcessSessionIdentity identity = NewIdentity();
                using (CommandBrokerHost host = new CommandBrokerHost(identity, directory.Path))
                {
                    CommandBrokerStartResult first = host.TryStart(NewBinding(identity, "latch-corrupt-ledger-first", 1));
                    AssertStarted(first);
                    CompleteVerified(first.Operation);
                    string ledger = FindOnlyLedger(directory.Path);
                    byte[] original = File.ReadAllBytes(ledger);
                    byte[] corrupt = (byte[])original.Clone();
                    corrupt[corrupt.Length - 1] ^= 0x73;
                    File.WriteAllBytes(ledger, corrupt);
                    AssertRestartLatched(host, identity, "latch-corrupt-ledger-detect");
                    File.WriteAllBytes(ledger, original);
                    AssertStillLatched(host, identity, "latch-corrupt-ledger-after-restore");
                }
            }
        }

        private static void NonterminalScanRemainsLatchedOnSameHost()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            {
                ProcessSessionIdentity identity = NewIdentity();
                using (CommandBrokerHost host = new CommandBrokerHost(identity, directory.Path))
                {
                    CommandBrokerStartResult first = host.TryStart(NewBinding(identity, "latch-nonterminal-first", 1));
                    AssertStarted(first);
                    AssertAdvanced(first.Operation.Prepare());
                    first.Operation.Dispose();
                    AssertEx.True(host.RestartRequired, "Nonterminal Dispose must immediately latch the host.");
                    AssertStillLatched(host, identity, "latch-nonterminal-next");
                }
            }
        }

        private static void AbortUncertainLatchesProcessSession()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            {
                ProcessSessionIdentity identity = NewIdentity();
                using (CommandBrokerHost first = new CommandBrokerHost(identity, directory.Path))
                {
                    CommandBrokerStartResult result = first.TryStart(NewBinding(identity, "fault", 1));
                    AssertStarted(result);
                    AssertAdvanced(result.Operation.AbortUncertain());
                }

                using (CommandBrokerHost second = new CommandBrokerHost(identity, directory.Path))
                {
                    CommandBrokerStartResult blocked = second.TryStart(NewBinding(identity, "after-fault", 1));
                    AssertEx.Equal(CommandBrokerStartStatus.RestartRequired, blocked.Status, blocked.ErrorMessage);
                }
            }
        }

        private static void BindingMismatchIsRejected()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            {
                ProcessSessionIdentity identity = NewIdentity();
                ProcessSessionIdentity wrong = NewIdentity();
                using (CommandBrokerHost host = new CommandBrokerHost(identity, directory.Path))
                {
                    CommandBrokerStartResult result = host.TryStart(NewBinding(wrong, "wrong", 1));
                    AssertEx.Equal(CommandBrokerStartStatus.BindingMismatch, result.Status, result.ErrorMessage);
                    AssertEx.False(host.RestartRequired, "Caller binding mismatch must not latch restart.");
                    CommandBrokerStartResult valid = host.TryStart(NewBinding(identity, "valid-after-mismatch", 1));
                    AssertStarted(valid);
                    CompleteVerified(valid.Operation);
                }
            }
        }

        private static void SameHostConcurrentStartIsSingleFlight()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            {
                ProcessSessionIdentity identity = NewIdentity();
                using (CommandBrokerHost host = new CommandBrokerHost(identity, directory.Path))
                {
                    const int WorkerCount = 12;
                    CommandBrokerStartResult[] results = new CommandBrokerStartResult[WorkerCount];
                    Thread[] workers = new Thread[WorkerCount];
                    ManualResetEvent begin = new ManualResetEvent(false);
                    for (int index = 0; index < WorkerCount; index++)
                    {
                        int captured = index;
                        workers[index] = new Thread(delegate()
                        {
                            begin.WaitOne();
                            results[captured] = host.TryStart(NewBinding(identity, "concurrent-" + captured, 1));
                        });
                        workers[index].Start();
                    }

                    begin.Set();
                    foreach (Thread worker in workers)
                    {
                        worker.Join();
                    }

                    begin.Dispose();
                    int startedCount = 0;
                    CommandBrokerOperation winner = null;
                    foreach (CommandBrokerStartResult result in results)
                    {
                        if (result.Status == CommandBrokerStartStatus.Started)
                        {
                            startedCount++;
                            winner = result.Operation;
                        }
                        else
                        {
                            AssertEx.Equal(CommandBrokerStartStatus.LocalSingleFlightBusy, result.Status, result.ErrorMessage);
                        }
                    }

                    AssertEx.Equal(1, startedCount, "Exactly one concurrent start may succeed.");
                    CompleteVerified(winner);
                }
            }
        }

        private static void SecondBrokerIsBlockedByProcessMutex()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            {
                ProcessSessionIdentity identity = NewIdentity();
                using (CommandBrokerHost first = new CommandBrokerHost(identity, directory.Path))
                using (CommandBrokerHost second = new CommandBrokerHost(identity, directory.Path))
                {
                    CommandBrokerStartResult held = first.TryStart(NewBinding(identity, "held", 1));
                    AssertStarted(held);
                    CommandBrokerStartResult blocked = second.TryStart(NewBinding(identity, "blocked", 1));
                    AssertEx.Equal(CommandBrokerStartStatus.ProcessSingleFlightBusy, blocked.Status, blocked.ErrorMessage);
                    AssertEx.False(second.RestartRequired, "Process mutex Busy must not latch restart.");
                    CompleteVerified(held.Operation);

                    CommandBrokerStartResult afterRelease = second.TryStart(NewBinding(identity, "after-release", 1));
                    AssertStarted(afterRelease);
                    CompleteVerified(afterRelease.Operation);
                }
            }
        }

        private static void StopBeforeStartBlocksNewOperation()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            using (CommandBrokerHost host = NewHost(directory))
            {
                AssertEx.Equal(BrokerStopDisposition.NoActiveOperation, host.RequestStop(), "Stop with no active operation is a clean boundary.");
                CommandBrokerStartResult result = host.TryStart(NewBinding(GetIdentity(host), "stopped", 1));
                AssertEx.Equal(CommandBrokerStartStatus.StopRequested, result.Status, result.ErrorMessage);
            }
        }

        private static void StopBeforeSideEffectNeverEntersBoundary()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            {
                ProcessSessionIdentity identity = NewIdentity();
                string journalPath;
                using (CommandBrokerHost host = new CommandBrokerHost(identity, directory.Path))
                {
                    CommandBrokerStartResult result = host.TryStart(NewBinding(identity, "stop-pre", 1));
                    AssertStarted(result);
                    journalPath = result.Operation.JournalPath;
                    AssertAdvanced(result.Operation.Prepare());
                    AssertAdvanced(result.Operation.IssueIntent());
                    AssertEx.Equal(BrokerStopDisposition.WillStopBeforeSideEffect, host.RequestStop(), "Pre-entry stop must win the boundary.");
                    AssertEx.Equal(
                        BrokerTransitionStatus.StoppedBeforeSideEffect,
                        result.Operation.TryEnterSideEffectBoundary(),
                        "No side-effect entry may be recorded after pre-entry stop.");
                    AssertEx.Equal(ExecutionJournalState.AbortUncertain, result.Operation.CurrentState, "Stop must fail closed.");
                }

                JournalInspection inspection = ExecutionJournalInspector.Inspect(journalPath);
                AssertEx.Equal(ExecutionJournalState.AbortUncertain, inspection.LastState.Value, "SideEffectEntered must not be present.");
                AssertEx.Equal(4L, inspection.RecordCount, "Only Consent, Prepared, Intent, Abort are expected.");
            }
        }

        private static void StopAfterSideEffectIsDeferredToTerminal()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            using (CommandBrokerHost host = NewHost(directory))
            {
                CommandBrokerStartResult result = host.TryStart(NewBinding(GetIdentity(host), "stop-post", 1));
                AssertStarted(result);
                AssertAdvanced(result.Operation.Prepare());
                AssertAdvanced(result.Operation.IssueIntent());
                AssertAdvanced(result.Operation.TryEnterSideEffectBoundary());
                AssertEx.Equal(BrokerStopDisposition.DeferredUntilTerminal, host.RequestStop(), "Entered side effect cannot be cancelled.");
                AssertAdvanced(result.Operation.RecordReceipt());
                AssertAdvanced(result.Operation.VerifyTerminal());
                JournalInspection inspection = ExecutionJournalInspector.Inspect(result.Operation.JournalPath);
                AssertEx.Equal(ExecutionJournalState.TerminalVerified, inspection.LastState.Value, "Deferred stop must allow safe settlement.");
                AssertEx.Equal(CommandBrokerStartStatus.StopRequested, host.TryStart(NewBinding(GetIdentity(host), "no-next", 1)).Status, "Stop blocks the next command.");
            }
        }

        private static void IpcRoundTripBindsNonceHmacAndSequence()
        {
            byte[] key = Bytes(0x11);
            byte[] nonce = Bytes(0x22);
            using (AuthenticatedIpcSender sender = new AuthenticatedIpcSender(key, nonce))
            using (AuthenticatedIpcReceiver receiver = new AuthenticatedIpcReceiver(key, nonce))
            {
                byte[] payload = Encoding.UTF8.GetBytes("offline-command");
                AuthenticatedIpcDecodeResult decoded = receiver.Decode(
                    sender.EncodeNext(AuthenticatedIpcMessageType.StartSingleCommand, payload));
                AssertEx.Equal(AuthenticatedIpcDecodeStatus.Accepted, decoded.Status, "Valid authenticated frame must pass.");
                AssertEx.Equal(1L, decoded.Message.Sequence, "First sequence must be one.");
                AssertEx.Equal(AuthenticatedIpcMessageType.StartSingleCommand, decoded.Message.MessageType, "Message type changed.");
                AssertEx.SequenceEqual(payload, decoded.Message.GetPayloadCopy(), "Payload changed.");
                AssertEx.Equal(2L, receiver.ExpectedSequence, "Receiver must advance exactly once.");
            }
        }

        private static void IpcReplayIsRejected()
        {
            byte[] key = Bytes(0x31);
            byte[] nonce = Bytes(0x32);
            using (AuthenticatedIpcSender sender = new AuthenticatedIpcSender(key, nonce))
            using (AuthenticatedIpcReceiver receiver = new AuthenticatedIpcReceiver(key, nonce))
            {
                byte[] frame = sender.EncodeNext(AuthenticatedIpcMessageType.StopRequested, new byte[0]);
                AssertEx.Equal(AuthenticatedIpcDecodeStatus.Accepted, receiver.Decode(frame).Status, "First delivery must pass.");
                AssertEx.Equal(AuthenticatedIpcDecodeStatus.Replay, receiver.Decode(frame).Status, "Same authenticated sequence must be rejected as replay.");
                AssertEx.Equal(2L, receiver.ExpectedSequence, "Replay must not advance the receiver.");
            }
        }

        private static void IpcGapDoesNotAdvanceReceiver()
        {
            byte[] key = Bytes(0x41);
            byte[] nonce = Bytes(0x42);
            using (AuthenticatedIpcSender sender = new AuthenticatedIpcSender(key, nonce))
            using (AuthenticatedIpcReceiver receiver = new AuthenticatedIpcReceiver(key, nonce))
            {
                byte[] first = sender.EncodeNext(AuthenticatedIpcMessageType.JournalStatus, new byte[] { 1 });
                byte[] second = sender.EncodeNext(AuthenticatedIpcMessageType.JournalStatus, new byte[] { 2 });
                AssertEx.Equal(AuthenticatedIpcDecodeStatus.SequenceGap, receiver.Decode(second).Status, "Sequence two cannot arrive first.");
                AssertEx.Equal(1L, receiver.ExpectedSequence, "Gap must not advance receiver.");
                AssertEx.Equal(AuthenticatedIpcDecodeStatus.Accepted, receiver.Decode(first).Status, "Missing frame must still be accepted later.");
                AssertEx.Equal(AuthenticatedIpcDecodeStatus.Accepted, receiver.Decode(second).Status, "Sequence two is valid after sequence one.");
            }
        }

        private static void IpcWrongHmacDoesNotAdvanceReceiver()
        {
            byte[] key = Bytes(0x51);
            byte[] nonce = Bytes(0x52);
            using (AuthenticatedIpcSender sender = new AuthenticatedIpcSender(key, nonce))
            using (AuthenticatedIpcReceiver receiver = new AuthenticatedIpcReceiver(key, nonce))
            {
                byte[] valid = sender.EncodeNext(AuthenticatedIpcMessageType.TerminalResult, new byte[] { 7 });
                byte[] tampered = (byte[])valid.Clone();
                tampered[tampered.Length - 1] ^= 1;
                AssertEx.Equal(AuthenticatedIpcDecodeStatus.WrongHmac, receiver.Decode(tampered).Status, "Tampered HMAC must fail.");
                AssertEx.Equal(1L, receiver.ExpectedSequence, "Wrong HMAC must not consume sequence.");
                AssertEx.Equal(AuthenticatedIpcDecodeStatus.Accepted, receiver.Decode(valid).Status, "Original frame remains valid.");
            }
        }

        private static void IpcWrongNonceIsRejectedAfterAuthentication()
        {
            byte[] key = Bytes(0x61);
            using (AuthenticatedIpcSender sender = new AuthenticatedIpcSender(key, Bytes(0x62)))
            using (AuthenticatedIpcReceiver receiver = new AuthenticatedIpcReceiver(key, Bytes(0x63)))
            {
                byte[] frame = sender.EncodeNext(AuthenticatedIpcMessageType.StopRequested, new byte[0]);
                AssertEx.Equal(AuthenticatedIpcDecodeStatus.WrongNonce, receiver.Decode(frame).Status, "Authenticated frame from another nonce must fail.");
                AssertEx.Equal(1L, receiver.ExpectedSequence, "Wrong nonce must not advance receiver.");
            }
        }

        private static void IpcOversizePrefixIsRejectedBeforeBuffering()
        {
            byte[] frame = BitConverter.GetBytes(AuthenticatedIpcProtocol.MaximumFrameBytes);
            using (AuthenticatedIpcReceiver receiver = new AuthenticatedIpcReceiver(Bytes(0x71), Bytes(0x72)))
            {
                AssertEx.Equal(AuthenticatedIpcDecodeStatus.Oversize, receiver.Decode(frame).Status, "Oversize declaration must fail before waiting for a body.");
            }
        }

        private static void IpcPartialAndTrailingFramesAreRejected()
        {
            byte[] key = Bytes(0x81);
            byte[] nonce = Bytes(0x82);
            using (AuthenticatedIpcSender sender = new AuthenticatedIpcSender(key, nonce))
            using (AuthenticatedIpcReceiver receiver = new AuthenticatedIpcReceiver(key, nonce))
            {
                byte[] frame = sender.EncodeNext(AuthenticatedIpcMessageType.JournalStatus, new byte[] { 9 });
                byte[] partial = new byte[frame.Length - 1];
                Buffer.BlockCopy(frame, 0, partial, 0, partial.Length);
                byte[] trailing = new byte[frame.Length + 1];
                Buffer.BlockCopy(frame, 0, trailing, 0, frame.Length);
                AssertEx.Equal(AuthenticatedIpcDecodeStatus.PartialFrame, receiver.Decode(partial).Status, "Partial frame must fail closed.");
                AssertEx.Equal(AuthenticatedIpcDecodeStatus.TrailingData, receiver.Decode(trailing).Status, "Trailing bytes must fail closed.");
                AssertEx.Equal(1L, receiver.ExpectedSequence, "Framing failures must not consume sequence.");
            }
        }

        private static void IpcPayloadBoundIsEnforced()
        {
            using (AuthenticatedIpcSender sender = new AuthenticatedIpcSender(Bytes(0x91), Bytes(0x92)))
            {
                AssertEx.Throws<ArgumentException>(
                    delegate
                    {
                        sender.EncodeNext(
                            AuthenticatedIpcMessageType.StartSingleCommand,
                            new byte[AuthenticatedIpcProtocol.MaximumPayloadBytes + 1]);
                    },
                    "Oversize payload must be rejected by sender.");
                byte[] maximum = sender.EncodeNext(
                    AuthenticatedIpcMessageType.StartSingleCommand,
                    new byte[AuthenticatedIpcProtocol.MaximumPayloadBytes]);
                AssertEx.Equal(AuthenticatedIpcProtocol.MaximumFrameBytes, maximum.Length, "Maximum frame size calculation changed.");
            }
        }

        private static void InMemoryTransportIsBoundedAndClones()
        {
            using (InMemoryFrameTransport transport = new InMemoryFrameTransport(1, 32))
            {
                byte[] source = new byte[] { 1, 2, 3 };
                AssertEx.True(transport.TrySend(source), "First in-memory frame must enqueue.");
                source[0] = 99;
                AssertEx.False(transport.TrySend(new byte[] { 4 }), "Capacity must be enforced.");
                byte[] received;
                AssertEx.True(transport.TryReceive(out received), "Queued frame must dequeue.");
                AssertEx.Equal((byte)1, received[0], "Transport must clone on send.");
                received[1] = 99;
                AssertEx.False(transport.TryReceive(out received), "Queue must now be empty.");
                transport.Close();
                AssertEx.False(transport.TrySend(new byte[] { 5 }), "Closed transport cannot accept frames.");
                AssertEx.Throws<ArgumentException>(
                    delegate { new InMemoryFrameTransport(1, 32).TrySend(new byte[33]); },
                    "Transport frame bound must be enforced.");
            }
        }

        private static void OperationDisposeNeverCompletesOrReplays()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            {
                ProcessSessionIdentity identity = NewIdentity();
                ExecutionJournalBinding binding = NewBinding(identity, "dispose", 1);
                string path;
                using (CommandBrokerHost host = new CommandBrokerHost(identity, directory.Path))
                {
                    CommandBrokerStartResult result = host.TryStart(binding);
                    AssertStarted(result);
                    path = result.Operation.JournalPath;
                    AssertAdvanced(result.Operation.Prepare());
                    result.Operation.Dispose();
                    AssertEx.True(result.Operation.RestartRequired, "Disposing a nonterminal operation must require restart.");
                    AssertEx.True(host.RestartRequired, "Nonterminal Dispose must immediately latch the host.");
                }

                JournalInspection inspection = ExecutionJournalInspector.Inspect(path);
                AssertEx.Equal(JournalInspectionStatus.ValidNonterminal, inspection.Status, inspection.ErrorMessage);
                using (CommandBrokerHost reopened = new CommandBrokerHost(identity, directory.Path))
                {
                    AssertEx.Equal(CommandBrokerStartStatus.RestartRequired, reopened.TryStart(binding).Status, "Disposed operation must not auto-complete or replay.");
                }
            }
        }

        private static void OperationReleaseTimeoutIsObservableAndRetriable()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            {
                ProcessSessionIdentity identity = NewIdentity();
                using (CommandBrokerHost host = new CommandBrokerHost(identity, directory.Path, 5000, 0))
                {
                    CommandBrokerStartResult result = host.TryStart(NewBinding(identity, "release-timeout", 1));
                    AssertStarted(result);
                    AssertAdvanced(result.Operation.Prepare());
                    AssertAdvanced(result.Operation.IssueIntent());
                    AssertAdvanced(result.Operation.TryEnterSideEffectBoundary());
                    AssertAdvanced(result.Operation.RecordReceipt());
                    AssertEx.Equal(
                        BrokerTransitionStatus.OwnershipReleaseTimeoutRestartRequired,
                        result.Operation.VerifyTerminal(),
                        "Terminal state must not hide a mutex-release timeout.");
                    AssertEx.True(result.Operation.RestartRequired, "The operation must latch RestartRequired.");
                    AssertEx.True(result.Operation.OwnershipReleaseUnknown, "Ownership must remain explicitly unknown.");
                    AssertEx.True(host.RestartRequired, "The host must latch RestartRequired.");
                    AssertEx.Equal(
                        NamedMutexReleaseStatus.Released,
                        result.Operation.RetryOwnershipRelease(5000),
                        "A bounded retry must confirm release.");
                    AssertEx.False(result.Operation.OwnershipReleaseUnknown, "Confirmed release clears only the unknown-ownership flag.");
                    AssertEx.True(host.RestartRequired, "Confirmed late release must not clear the restart latch.");
                    AssertEx.Equal(
                        CommandBrokerStartStatus.RestartRequired,
                        host.TryStart(NewBinding(identity, "after-release-timeout", 1)).Status,
                        "A host that observed release timeout cannot resume execution.");
                }
            }
        }

        private static void OperationReleaseFailureImmediatelyLatchesHost()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            {
                ProcessSessionIdentity identity = NewIdentity();
                using (CommandBrokerHost host = new CommandBrokerHost(identity, directory.Path))
                {
                    CommandBrokerStartResult result = host.TryStart(NewBinding(identity, "release-failure", 1));
                    AssertStarted(result);
                    FieldInfo leaseField = typeof(CommandBrokerOperation).GetField("lease", BindingFlags.Instance | BindingFlags.NonPublic);
                    NamedMutexLease lease = (NamedMutexLease)leaseField.GetValue(result.Operation);
                    ForceLeaseOwnerStatus(lease, NamedMutexAcquireStatus.Failed);
                    AssertAdvanced(result.Operation.Prepare());
                    AssertAdvanced(result.Operation.IssueIntent());
                    AssertAdvanced(result.Operation.TryEnterSideEffectBoundary());
                    AssertAdvanced(result.Operation.RecordReceipt());
                    AssertEx.Equal(
                        BrokerTransitionStatus.OwnershipReleaseFailedRestartRequired,
                        result.Operation.VerifyTerminal(),
                        "ReleaseMutex failure must remain visible at the operation boundary.");
                    AssertEx.True(result.Operation.RestartRequired, "Release failure must latch the operation.");
                    AssertEx.True(host.RestartRequired, "Release failure must immediately latch the host.");
                    AssertEx.False(result.Operation.OwnershipReleaseUnknown, "A completed failed owner differs from a timeout with unknown ownership.");
                    AssertStillLatched(host, identity, "after-release-failure");
                }
            }
        }

        private static void FailedRetryAfterTimeoutDetachesOperationButKeepsLatch()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            {
                ProcessSessionIdentity identity = NewIdentity();
                using (CommandBrokerHost host = new CommandBrokerHost(identity, directory.Path, 5000, 0))
                {
                    CommandBrokerStartResult result = host.TryStart(NewBinding(identity, "timeout-then-failed-retry", 1));
                    AssertStarted(result);
                    AssertAdvanced(result.Operation.Prepare());
                    AssertAdvanced(result.Operation.IssueIntent());
                    AssertAdvanced(result.Operation.TryEnterSideEffectBoundary());
                    AssertAdvanced(result.Operation.RecordReceipt());
                    AssertEx.Equal(
                        BrokerTransitionStatus.OwnershipReleaseTimeoutRestartRequired,
                        result.Operation.VerifyTerminal(),
                        "The initial zero-budget release must time out.");

                    FieldInfo leaseField = typeof(CommandBrokerOperation).GetField("lease", BindingFlags.Instance | BindingFlags.NonPublic);
                    NamedMutexLease lease = (NamedMutexLease)leaseField.GetValue(result.Operation);
                    ForceLeaseOwnerStatus(lease, NamedMutexAcquireStatus.Failed);
                    AssertEx.Equal(
                        NamedMutexReleaseStatus.FailedRestartRequired,
                        result.Operation.RetryOwnershipRelease(5000),
                        "The bounded retry must expose the forced owner failure.");
                    AssertEx.False(result.Operation.OwnershipReleaseUnknown, "A completed failed retry is no longer ownership-unknown.");
                    AssertEx.True(result.Operation.RestartRequired, "The operation restart latch must remain set.");
                    AssertEx.True(host.RestartRequired, "The host restart latch must remain set.");
                    FieldInfo activeField = typeof(CommandBrokerHost).GetField("activeOperation", BindingFlags.Instance | BindingFlags.NonPublic);
                    AssertEx.Null(activeField.GetValue(host), "A completed failed retry must detach the closed operation from its host.");
                    AssertStillLatched(host, identity, "after-timeout-failed-retry");
                }
            }
        }

        private static void JournalWriteFailureImmediatelyLatchesHost()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            {
                ProcessSessionIdentity identity = NewIdentity();
                using (CommandBrokerHost host = new CommandBrokerHost(identity, directory.Path))
                {
                    CommandBrokerStartResult started = host.TryStart(NewBinding(identity, "forced-write-failure", 1));
                    AssertStarted(started);
                    FieldInfo journalField = typeof(CommandBrokerOperation).GetField("journal", BindingFlags.Instance | BindingFlags.NonPublic);
                    object journal = journalField.GetValue(started.Operation);
                    FieldInfo streamField = journal.GetType().GetField("stream", BindingFlags.Instance | BindingFlags.NonPublic);
                    FileStream stream = (FileStream)streamField.GetValue(journal);
                    stream.Dispose();
                    AssertEx.Equal(
                        BrokerTransitionStatus.JournalFailureRestartRequired,
                        started.Operation.Prepare(),
                        "A forced durable-write failure must be visible.");
                    AssertEx.True(started.Operation.RestartRequired, "Write failure must latch the operation.");
                    AssertEx.True(host.RestartRequired, "Write failure must immediately latch the host.");
                    AssertStillLatched(host, identity, "after-forced-write-failure");
                }
            }
        }

        private static CommandBrokerHost NewHost(TemporaryDirectory directory)
        {
            ProcessSessionIdentity identity = NewIdentity();
            CommandBrokerHost host = new CommandBrokerHost(identity, directory.Path);
            HostIdentities.Add(host, identity);
            return host;
        }

        private static readonly Dictionary<CommandBrokerHost, ProcessSessionIdentity> HostIdentities
            = new Dictionary<CommandBrokerHost, ProcessSessionIdentity>();

        private static ProcessSessionIdentity GetIdentity(CommandBrokerHost host)
        {
            return HostIdentities[host];
        }

        private static ProcessSessionIdentity NewIdentity()
        {
            int value = Interlocked.Increment(ref identityCounter);
            return new ProcessSessionIdentity(4000 + value, 1000000000L + value, Hash("executable-" + value));
        }

        private static ExecutionJournalBinding NewBinding(
            ProcessSessionIdentity identity,
            string request,
            int stage)
        {
            return new ExecutionJournalBinding(identity, Hash(request), stage, Guid.NewGuid());
        }

        private static string CreateNonterminalJournal(
            string directory,
            ProcessSessionIdentity identity,
            ExecutionJournalBinding binding)
        {
            string path;
            using (CommandBrokerHost host = new CommandBrokerHost(identity, directory))
            {
                CommandBrokerStartResult result = host.TryStart(binding);
                AssertStarted(result);
                path = result.Operation.JournalPath;
                AssertAdvanced(result.Operation.Prepare());
                result.Operation.Dispose();
            }

            return path;
        }

        private static string FindOnlyLedger(string directory)
        {
            string[] ledgers = Directory.GetFiles(directory, "S9CBL-*.ledger", SearchOption.TopDirectoryOnly);
            AssertEx.Equal(1, ledgers.Length, "Exactly one session ledger was expected.");
            return ledgers[0];
        }

        private static void AssertRestartLatched(CommandBrokerHost host, ProcessSessionIdentity identity, string request)
        {
            CommandBrokerStartResult detected = host.TryStart(NewBinding(identity, request, 1));
            AssertEx.Equal(CommandBrokerStartStatus.JournalCorruptRestartRequired, detected.Status, detected.ErrorMessage);
            AssertEx.True(host.RestartRequired, "A restart-required scan result must permanently latch this host.");
        }

        private static void AssertStillLatched(CommandBrokerHost host, ProcessSessionIdentity identity, string request)
        {
            CommandBrokerStartResult blocked = host.TryStart(NewBinding(identity, request, 1));
            AssertEx.Equal(CommandBrokerStartStatus.RestartRequired, blocked.Status, blocked.ErrorMessage);
            AssertEx.True(host.RestartRequired, "The host restart latch must never clear in-place.");
        }

        private static void ForceLeaseOwnerStatus(NamedMutexLease lease, NamedMutexAcquireStatus status)
        {
            FieldInfo ownerField = typeof(NamedMutexLease).GetField("owner", BindingFlags.Instance | BindingFlags.NonPublic);
            object owner = ownerField.GetValue(lease);
            FieldInfo statusField = owner.GetType().GetField("<Status>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
            statusField.SetValue(owner, status);
        }

        private static byte[] SerializeLedgerTestEntry(
            long sequence,
            ExecutionJournalBinding binding,
            string journalFileName)
        {
            using (MemoryStream memory = new MemoryStream(258))
            using (BinaryWriter writer = new BinaryWriter(memory, Encoding.UTF8))
            {
                writer.Write(Encoding.ASCII.GetBytes("S9CBLD01"));
                writer.Write(1);
                writer.Write(sequence);
                writer.Write(binding.ProcessSession.ProcessId);
                writer.Write(binding.ProcessSession.CreationFileTimeUtc);
                writer.Write(HexBytes(binding.ProcessSession.ExecutableSha256));
                writer.Write(HexBytes(binding.RequestFingerprint));
                writer.Write(binding.StageOrdinal);
                writer.Write(binding.ConsentId.ToByteArray());
                writer.Write(Encoding.ASCII.GetBytes(journalFileName));
                writer.Flush();
                byte[] payload = memory.ToArray();
                AssertEx.Equal(258, payload.Length, "Synthetic ledger entry shape changed.");
                return payload;
            }
        }

        private static byte[] HexBytes(string value)
        {
            byte[] result = new byte[value.Length / 2];
            for (int index = 0; index < result.Length; index++)
            {
                result[index] = Convert.ToByte(value.Substring(index * 2, 2), 16);
            }

            return result;
        }

        private static void AddWorldReadAce(string path)
        {
            FileSecurity security = File.GetAccessControl(path);
            security.AddAccessRule(
                new FileSystemAccessRule(
                    new SecurityIdentifier(WellKnownSidType.WorldSid, null),
                    FileSystemRights.Read,
                    AccessControlType.Allow));
            File.SetAccessControl(path, security);
        }

        private static void CompleteVerified(CommandBrokerOperation operation)
        {
            AssertEx.NotNull(operation, "Operation is required.");
            AssertAdvanced(operation.Prepare());
            AssertAdvanced(operation.IssueIntent());
            AssertAdvanced(operation.TryEnterSideEffectBoundary());
            AssertAdvanced(operation.RecordReceipt());
            AssertAdvanced(operation.VerifyTerminal());
        }

        private static void AssertStarted(CommandBrokerStartResult result)
        {
            AssertEx.Equal(CommandBrokerStartStatus.Started, result.Status, result.ErrorMessage);
            AssertEx.NotNull(result.Operation, "Started result must carry an operation.");
            AssertEx.False(result.Operation.IsClosed, "New operation must be active.");
        }

        private static void AssertAdvanced(BrokerTransitionStatus status)
        {
            AssertEx.Equal(BrokerTransitionStatus.Advanced, status, "Expected journal transition to advance.");
        }

        private static byte[] Bytes(byte value)
        {
            byte[] bytes = new byte[32];
            for (int index = 0; index < bytes.Length; index++)
            {
                bytes[index] = (byte)(value + index);
            }

            return bytes;
        }

        private static string Hash(string value)
        {
            using (SHA256 algorithm = SHA256.Create())
            {
                byte[] digest = algorithm.ComputeHash(Encoding.UTF8.GetBytes(value));
                StringBuilder builder = new StringBuilder(digest.Length * 2);
                foreach (byte item in digest)
                {
                    builder.Append(item.ToString("X2"));
                }

                return builder.ToString();
            }
        }

        private static string RandomMutexName()
        {
            return "Local\\San9AutoDomestic.CommandBroker.Test." + Guid.NewGuid().ToString("N");
        }

        private sealed class TestCase
        {
            internal TestCase(string name, Action body)
            {
                Name = name;
                Body = body;
            }

            internal string Name { get; private set; }

            internal Action Body { get; private set; }
        }

        private sealed class TemporaryDirectory : IDisposable
        {
            internal TemporaryDirectory()
            {
                string root = System.IO.Path.GetTempPath();
                Path = System.IO.Path.Combine(root, "San9AutoDomestic.CommandBroker.Test." + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path);
                SecurityIdentifier currentSid;
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                {
                    currentSid = identity.User;
                }

                DirectorySecurity security = new DirectorySecurity();
                security.SetOwner(currentSid);
                security.SetAccessRuleProtection(true, false);
                security.AddAccessRule(
                    new FileSystemAccessRule(
                        currentSid,
                        FileSystemRights.FullControl,
                        InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                        PropagationFlags.None,
                        AccessControlType.Allow));
                Directory.SetAccessControl(Path, security);
            }

            internal string Path { get; private set; }

            public void Dispose()
            {
                if (Path == null)
                {
                    return;
                }

                string full = System.IO.Path.GetFullPath(Path);
                string temp = System.IO.Path.GetFullPath(System.IO.Path.GetTempPath());
                if (!full.StartsWith(temp, StringComparison.OrdinalIgnoreCase)
                    || System.IO.Path.GetFileName(full).IndexOf("San9AutoDomestic.CommandBroker.Test.", StringComparison.Ordinal) != 0)
                {
                    throw new InvalidOperationException("Refusing to remove an unexpected test directory.");
                }

                if (Directory.Exists(full))
                {
                    Directory.Delete(full, true);
                }

                Path = null;
            }
        }

        private static class AssertEx
        {
            internal static void True(bool condition, string message)
            {
                if (!condition)
                {
                    throw new InvalidOperationException(message);
                }
            }

            internal static void False(bool condition, string message)
            {
                True(!condition, message);
            }

            internal static void Null(object value, string message)
            {
                True(value == null, message);
            }

            internal static void NotNull(object value, string message)
            {
                True(value != null, message);
            }

            internal static void Equal<T>(T expected, T actual, string message)
            {
                if (!EqualityComparer<T>.Default.Equals(expected, actual))
                {
                    throw new InvalidOperationException(
                        message + " Expected=" + expected + "; Actual=" + actual + ".");
                }
            }

            internal static void SequenceEqual(byte[] expected, byte[] actual, string message)
            {
                if (expected == null || actual == null || expected.Length != actual.Length)
                {
                    throw new InvalidOperationException(message);
                }

                for (int index = 0; index < expected.Length; index++)
                {
                    if (expected[index] != actual[index])
                    {
                        throw new InvalidOperationException(message);
                    }
                }
            }

            internal static void Throws<TException>(Action action, string message)
                where TException : Exception
            {
                try
                {
                    action();
                }
                catch (TException)
                {
                    return;
                }

                throw new InvalidOperationException(message);
            }
        }
    }
}
