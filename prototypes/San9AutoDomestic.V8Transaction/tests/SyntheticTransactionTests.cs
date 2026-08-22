using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using San9AutoDomestic.V8Transaction;

namespace San9AutoDomestic.V8Transaction.SelfTest
{
    internal static class SyntheticTransactionTests
    {
        private static int _passed;

        internal static int RunAll()
        {
            Run("synthetic_surface_is_not_public", SyntheticSurfaceIsNotPublic);
            Run("no_unsigned_friend_and_protocol_v2_ttl", NoUnsignedFriendAndProtocolV2Ttl);
            Run("registry_has_five_exact_native_mappings", RegistryHasFiveExactNativeMappings);
            Run("native_callback_method_oracle_is_exact", NativeCallbackMethodOracleIsExact);
            Run("native_callback_interface_switches_match_independent_oracle",
                NativeCallbackInterfaceSwitchesMatchIndependentOracle);
            Run("descriptor_constructor_fails_closed", DescriptorConstructorFailsClosed);
            Run("unknown_and_default_enums_throw", UnknownAndDefaultEnumsThrow);
            Run("request_fingerprint_binds_descriptor_and_fields", RequestFingerprintBindsDescriptorAndFields);
            Run("all_five_commands_complete_through_coordinator", AllFiveCommandsCompleteThroughCoordinator);
            Run("commit_requires_authenticated_next_generation", CommitRequiresAuthenticatedNextGeneration);
            Run("known_business_gates_skip_before_intent", KnownBusinessGatesSkipBeforeIntent);
            Run("global_single_flight_uses_real_barrier", GlobalSingleFlightUsesRealBarrier);
            Run("multiple_coordinators_share_one_slot", MultipleCoordinatorsShareOneSlot);
            Run("attempt_is_recorded_before_callback", AttemptIsRecordedBeforeCallback);
            Run("second_intent_is_abort_not_reissue", SecondIntentIsAbortNotReissue);
            Run("double_callback_execution_claims_once", DoubleCallbackExecutionClaimsOnce);
            Run("same_action_claim_serial_dispatch_invokes_side_effect_once", SameActionClaimSerialDispatchInvokesSideEffectOnce);
            Run("same_action_claim_concurrent_dispatch_invokes_side_effect_once", SameActionClaimConcurrentDispatchInvokesSideEffectOnce);
            Run("wrong_action_method_is_rejected_before_side_effect", WrongActionMethodIsRejectedBeforeSideEffect);
            Run("claimed_action_expiry_is_rejected_before_side_effect", ClaimedActionExpiryIsRejectedBeforeSideEffect);
            Run("callback_failure_aborts_without_reissue", CallbackFailureAbortsWithoutReissue);
            Run("entered_action_throw_halts_all_seven_stages", EnteredActionThrowHaltsAllSevenStages);
            Run("entered_action_null_halts_all_seven_stages", EnteredActionNullHaltsAllSevenStages);
            Run("trusted_clock_blocks_intent_before_expiry_boundary", TrustedClockBlocksIntentBeforeExpiryBoundary);
            Run("trusted_clock_blocks_receipt_after_expiry", TrustedClockBlocksReceiptAfterExpiry);
            Run("delayed_execution_claim_is_revoked_at_deadline", DelayedExecutionClaimIsRevokedAtDeadline);
            Run("abort_revokes_stale_intent_across_generation", AbortRevokesStaleIntentAcrossGeneration);
            Run("executing_without_receipt_requires_restart", ExecutingWithoutReceiptRequiresRestart);
            Run("evidence_timestamp_is_diagnostic_only", EvidenceTimestampIsDiagnosticOnly);
            Run("wrong_city_latches_fault", WrongCityLatchesFault);
            Run("destroyed_and_reused_objects_abort", DestroyedAndReusedObjectsAbort);
            Run("typed_outer_mapping_is_enforced_field_by_field", TypedOuterMappingIsEnforcedFieldByField);
            Run("source_count_matches_begin_and_bounds", SourceCountMatchesBeginAndBounds);
            Run("wrong_lists_and_context_abort", WrongListsAndContextAbort);
            Run("post_snapshot_fields_fail_independently", PostSnapshotFieldsFailIndependently);
            Run("post_snapshot_requires_stable_authenticated_new_generation", PostSnapshotRequiresStableAuthenticatedNewGeneration);
            Run("abort_blocks_new_task_same_generation", AbortBlocksNewTaskSameGeneration);
            Run("manual_ack_requires_new_generation", ManualAckRequiresNewGeneration);
            Run("process_restart_resets_fault_without_ack", ProcessRestartResetsFaultWithoutAck);
            Run("same_pid_new_creation_retires_active_session", SamePidNewCreationRetiresActiveSession);
            Run("session_retire_reclaims_bounded_slot", SessionRetireReclaimsBoundedSlot);
            Run("trusted_clock_reentry_and_exception_fail_closed", TrustedClockReentryAndExceptionFailClosed);
            Run("cleanup_directives_follow_abort_stage", CleanupDirectivesFollowAbortStage);
            Run("cleanup_failure_escalates_to_halt_restart", CleanupFailureEscalatesToHaltRestart);
            Run("post_read_tickets_are_ordered_independent_one_shot", PostReadTicketsAreOrderedIndependentOneShot);
            Run("cleanup_requires_authenticated_one_shot_ab_postcondition", CleanupRequiresAuthenticatedOneShotAbPostcondition);
            Run("cleanup_replay_and_concurrent_claim_halt_restart", CleanupReplayAndConcurrentClaimHaltRestart);
            Run("same_cleanup_claim_serial_dispatch_invokes_side_effect_once", SameCleanupClaimSerialDispatchInvokesSideEffectOnce);
            Run("same_cleanup_claim_concurrent_dispatch_invokes_side_effect_once", SameCleanupClaimConcurrentDispatchInvokesSideEffectOnce);
            Run("wrong_cleanup_method_is_rejected_before_side_effect", WrongCleanupMethodIsRejectedBeforeSideEffect);
            Run("claimed_cleanup_expiry_is_rejected_before_side_effect", ClaimedCleanupExpiryIsRejectedBeforeSideEffect);
            Run("cleanup_pre_entry_ab_claims_are_unavailable", CleanupPreEntryAbClaimsAreUnavailable);
            Run("cleanup_ab_reads_start_after_callback_return", CleanupAbReadsStartAfterCallbackReturn);
            Run("fake_transport_remains_internal_non_live", FakeTransportRemainsInternalNonLive);
            Run("authorization_is_permanently_false", AuthorizationIsPermanentlyFalse);
            return _passed;
        }

        private static void SyntheticSurfaceIsNotPublic()
        {
            Assembly assembly = LoadProductionAssembly();
            string[] exported = assembly.GetExportedTypes().Select(type => type.Name).ToArray();
            string[] forbidden =
            {
                "SyntheticStageEvidence",
                "SyntheticSingleCommandStateMachine",
                "InMemoryFakeTransportEndpoint",
                "INativeSingleCommandCallbacks",
                "INativeAbortCleanupCallback",
                "OneShotActionIntent",
                "OneShotCleanupIntent",
                "ActionExecutionClaim",
                "CleanupExecutionClaim",
                "ActionSideEffectPermit",
                "CleanupSideEffectPermit",
                "CleanupPostReadPermit",
                "CoordinatorActionClaimAuthority",
                "CoordinatorCleanupClaimAuthority",
                "CoordinatorSideEffectAuthority",
                "TrustedObservationFactory",
                "ActionReceipt",
                "AuthenticatedCleanupReceipt",
                "ProcessGenerationCoordinator"
            };
            foreach (string name in forbidden)
            {
                Assert(!exported.Contains(name), name + " leaked into the public API.");
            }

            ConstructorInfo[] machineConstructors = typeof(SyntheticSingleCommandStateMachine)
                .GetConstructors(BindingFlags.Instance | BindingFlags.Public);
            Assert(machineConstructors.Length == 0, "Machine has a public constructor.");
            Type contract = assembly.GetType(
                "San9AutoDomestic.V8Transaction.INativeSingleCommandCallbacks", true);
            Assert(!contract.IsPublic, "Native callback contract is public.");
            Assert(!assembly.GetTypes().Any(type => type.IsClass && !type.IsAbstract && contract.IsAssignableFrom(type)),
                "A native callback implementation exists.");
            Type cleanupContract = assembly.GetType(
                "San9AutoDomestic.V8Transaction.INativeAbortCleanupCallback", true);
            Assert(!cleanupContract.IsPublic, "Native cleanup callback contract is public.");
            Assert(!assembly.GetTypes().Any(type => type.IsClass
                && !type.IsAbstract
                && cleanupContract.IsAssignableFrom(type)),
                "A native cleanup callback implementation exists.");

            Type coordinator = assembly.GetType(
                "San9AutoDomestic.V8Transaction.ProcessGenerationCoordinator", true);
            string[] forbiddenProductionMethods =
            {
                "AttachForTesting",
                "InvokeClaimedActionForTesting",
                "InvokeClaimedCleanupForTesting",
                "AuthorizeCleanupSideEffectForReceiptTesting",
                "ClaimCleanupPostReadForTesting",
                "CompleteCleanupPostReadForTesting",
                "CompleteCleanupForTesting",
                "SubmitActionReceipt",
                "SubmitCleanupReceipt"
            };
            MethodInfo[] coordinatorMethods = coordinator.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
            foreach (string methodName in forbiddenProductionMethods)
            {
                Assert(!coordinatorMethods.Any(method => method.Name == methodName),
                    "Production DLL contains test/raw settlement seam " + methodName + ".");
            }
        }

        private static void NoUnsignedFriendAndProtocolV2Ttl()
        {
            Assembly assembly = LoadProductionAssembly();
            object[] friends = assembly.GetCustomAttributes(typeof(InternalsVisibleToAttribute), false);
            Assert(friends.Length == 0, "Production sources retain an unsigned friend-assembly bypass.");
            Type productionRequest = assembly.GetType(
                "San9AutoDomestic.V8Transaction.SingleCommandRequest", true);
            FieldInfo protocolVersion = productionRequest.GetField(
                "CurrentProtocolVersion", BindingFlags.Public | BindingFlags.Static);
            Assert(protocolVersion != null
                && (int)protocolVersion.GetRawConstantValue() == 2,
                "Production protocol schema was not advanced.");
            Assert(SingleCommandRequest.CurrentProtocolVersion == 2, "Linked test protocol schema was not advanced.");
            SingleCommandRequest request = CreateRequest(
                RegisteredDomesticCommand.Commerce, 1, Digest(0x31), 100, 1000);
            Assert(request.ProtocolVersion == 2 && request.TimeToLiveMilliseconds == 900,
                "Request does not carry the frozen relative TTL.");
            Assert(typeof(SingleCommandRequest).GetProperty("CreatedAtMonotonicTicks") == null
                && typeof(SingleCommandRequest).GetProperty("DeadlineMonotonicTicks") == null,
                "Caller-controlled absolute monotonic timestamps remain in the schema.");
            Assert(productionRequest.GetProperty("CreatedAtMonotonicTicks") == null
                && productionRequest.GetProperty("DeadlineMonotonicTicks") == null,
                "Production DLL retains caller-controlled absolute monotonic timestamps.");
        }

        private static Assembly LoadProductionAssembly()
        {
            string path = Path.GetFullPath(Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "..", "..", "..", "src", "bin", "Release",
                "San9AutoDomestic.V8Transaction.dll"));
            Assert(File.Exists(path), "Built production V8 DLL was not found for surface audit: " + path);
            return Assembly.LoadFile(path);
        }

        private static void RegistryHasFiveExactNativeMappings()
        {
            NativeCommandDescriptor[] all = NativeCommandDescriptorRegistry.All.ToArray();
            Assert(all.Length == 5, "Registry count is not five.");
            AssertDescriptor(RegisteredDomesticCommand.Patrol, "patrol", 0, NativeOuterTaskType.Patrol,
                "outer-task-patrol", 0x0060B920U, NativeMoneyModel.PerOfficerFifty, 250);
            AssertDescriptor(RegisteredDomesticCommand.Commerce, "commerce", 1, NativeOuterTaskType.Commerce,
                "outer-task-commerce", 0x0060CCB0U, NativeMoneyModel.PerOfficerFifty, 250);
            AssertDescriptor(RegisteredDomesticCommand.Cultivate, "cultivate", 2, NativeOuterTaskType.Cultivate,
                "outer-task-cultivate", 0x0060BBA0U, NativeMoneyModel.PerOfficerFifty, 250);
            AssertDescriptor(RegisteredDomesticCommand.Train, "train", 5, NativeOuterTaskType.Train,
                "outer-task-train", 0x0060C370U, NativeMoneyModel.Zero, 0);
            AssertDescriptor(RegisteredDomesticCommand.Repair, "repair", 3, NativeOuterTaskType.Repair,
                "outer-task-repair", 0x0060BCE8U, NativeMoneyModel.PerOfficerFifty, 250);
            Assert(all.All(item => item.RequiredOfficerCount == 5), "A descriptor is not exact-five.");
        }

        private static void NativeCallbackMethodOracleIsExact()
        {
            Assert(ActionStages.GetRequiredMethod(TransactionStage.BindTargetCandidate)
                == NativeActionMethod.BindTargetCandidate, "Bind method route drifted.");
            Assert(ActionStages.GetRequiredMethod(TransactionStage.OpenOuter)
                == NativeActionMethod.OpenOuter, "OpenOuter method route drifted.");
            Assert(ActionStages.GetRequiredMethod(TransactionStage.OpenSelector)
                == NativeActionMethod.OpenSelector, "OpenSelector method route drifted.");
            Assert(ActionStages.GetRequiredMethod(TransactionStage.Clear)
                == NativeActionMethod.Clear, "Clear method route drifted.");
            Assert(ActionStages.GetRequiredMethod(TransactionStage.NativeFillMax)
                == NativeActionMethod.NativeFillMax, "NativeFillMax method route drifted.");
            Assert(ActionStages.GetRequiredMethod(TransactionStage.AcceptInner)
                == NativeActionMethod.AcceptInner, "AcceptInner method route drifted.");
            Assert(ActionStages.GetRequiredMethod(TransactionStage.AcceptOuter)
                == NativeActionMethod.AcceptOuter, "AcceptOuter method route drifted.");
            Assert(CleanupMethods.GetRequiredMethod(AbortCleanupKind.ConditionalRestoreTarget)
                == NativeCleanupMethod.ConditionalRestoreTarget,
                "ConditionalRestoreTarget cleanup route drifted.");
            Assert(CleanupMethods.GetRequiredMethod(AbortCleanupKind.CancelSelector)
                == NativeCleanupMethod.CancelSelector, "CancelSelector cleanup route drifted.");
            Assert(CleanupMethods.GetRequiredMethod(AbortCleanupKind.CancelOuter)
                == NativeCleanupMethod.CancelOuter, "CancelOuter cleanup route drifted.");
            Assert(CleanupMethods.GetRequiredMethod(AbortCleanupKind.DoNotRollbackAfterAccept)
                == NativeCleanupMethod.ReviewAfterAccept, "ReviewAfterAccept cleanup route drifted.");
        }

        private static void NativeCallbackInterfaceSwitchesMatchIndependentOracle()
        {
            TransactionStage[] actionStages =
            {
                TransactionStage.BindTargetCandidate,
                TransactionStage.OpenOuter,
                TransactionStage.OpenSelector,
                TransactionStage.Clear,
                TransactionStage.NativeFillMax,
                TransactionStage.AcceptInner,
                TransactionStage.AcceptOuter
            };
            NativeActionMethod[] expectedActionMethods =
            {
                NativeActionMethod.BindTargetCandidate,
                NativeActionMethod.OpenOuter,
                NativeActionMethod.OpenSelector,
                NativeActionMethod.Clear,
                NativeActionMethod.NativeFillMax,
                NativeActionMethod.AcceptInner,
                NativeActionMethod.AcceptOuter
            };
            for (int index = 0; index < actionStages.Length; index++)
            {
                Fixture fixture = new Fixture(RegisteredDomesticCommand.Commerce);
                ActionIssueResult issue = DriveToActionStage(fixture, actionStages[index]);
                ActionClaimResult claim = fixture.Coordinator.ClaimActionExecution(
                    fixture.Handle, issue.Intent);
                Assert(claim.Claimed, "Production action route could not claim " + actionStages[index] + ".");
                NativeActionCallbackRecorder recorder = new NativeActionCallbackRecorder(
                    fixture, issue.Intent);

                ActionInvocationResult invocation = fixture.Coordinator.InvokeClaimedAction(
                    fixture.Handle, claim.Claim, recorder);

                Assert(invocation.CallbackInvoked
                    && invocation.Transition != null
                    && invocation.Transition.Accepted,
                    "Production action invoker did not settle " + actionStages[index] + ": "
                        + invocation.Detail);
                Assert(recorder.SideEffectCalls == 1
                    && recorder.RecordedMethod == expectedActionMethods[index],
                    "Production action interface switch routed " + actionStages[index]
                        + " to " + recorder.RecordedMethod + " instead of "
                        + expectedActionMethods[index] + ".");
            }

            AbortCleanupKind[] cleanupKinds =
            {
                AbortCleanupKind.ConditionalRestoreTarget,
                AbortCleanupKind.CancelSelector,
                AbortCleanupKind.CancelOuter,
                AbortCleanupKind.DoNotRollbackAfterAccept
            };
            NativeCleanupMethod[] expectedCleanupMethods =
            {
                NativeCleanupMethod.ConditionalRestoreTarget,
                NativeCleanupMethod.CancelSelector,
                NativeCleanupMethod.CancelOuter,
                NativeCleanupMethod.ReviewAfterAccept
            };
            for (int index = 0; index < cleanupKinds.Length; index++)
            {
                FaultLatchSnapshot fault;
                Fixture fixture = CreateCleanupRouteFixture(cleanupKinds[index], out fault);
                CleanupIssueResult issue = fixture.Coordinator.IssueCleanup(
                    fault.FaultId, fault.CleanupDirective.DirectiveId);
                Assert(issue.Issued, "Production cleanup route did not issue " + cleanupKinds[index] + ".");
                CleanupClaimResult claim = fixture.Coordinator.ClaimCleanupExecution(issue.Intent);
                Assert(claim.Claimed, "Production cleanup route could not claim " + cleanupKinds[index] + ".");
                NativeCleanupCallbackRecorder recorder = new NativeCleanupCallbackRecorder(
                    fault, cleanupKinds[index] == AbortCleanupKind.DoNotRollbackAfterAccept);

                CleanupInvocationResult invocation = fixture.Coordinator.InvokeClaimedCleanup(
                    claim.Claim, recorder);

                Assert(invocation.CallbackInvoked && invocation.ReceiptAccepted,
                    "Production cleanup invoker did not settle " + cleanupKinds[index] + ": "
                        + invocation.Detail);
                Assert(recorder.SideEffectCalls == 1
                    && recorder.RecordedMethod == expectedCleanupMethods[index],
                    "Production cleanup interface switch routed " + cleanupKinds[index]
                        + " to " + recorder.RecordedMethod + " instead of "
                        + expectedCleanupMethods[index] + ".");
                Assert(recorder.PostReadCalls == 2,
                    "Production cleanup invoker did not perform exact ordered A/B reads for "
                        + cleanupKinds[index] + ".");
            }
        }

        private static void DescriptorConstructorFailsClosed()
        {
            AssertThrows<ArgumentOutOfRangeException>(delegate
            {
                new NativeCommandDescriptor(
                    RegisteredDomesticCommand.Unknown, "x", 0, NativeOuterTaskType.Patrol,
                    "x", 1, NativeMoneyModel.Zero);
            });
            AssertThrows<ArgumentException>(delegate
            {
                new NativeCommandDescriptor(
                    RegisteredDomesticCommand.Patrol, " ", 0, NativeOuterTaskType.Patrol,
                    "x", 1, NativeMoneyModel.Zero);
            });
            AssertThrows<ArgumentOutOfRangeException>(delegate
            {
                new NativeCommandDescriptor(
                    RegisteredDomesticCommand.Patrol, "x", -1, NativeOuterTaskType.Patrol,
                    "x", 1, NativeMoneyModel.Zero);
            });
            AssertThrows<ArgumentOutOfRangeException>(delegate
            {
                new NativeCommandDescriptor(
                    RegisteredDomesticCommand.Patrol, "x", 0, NativeOuterTaskType.Commerce,
                    "x", 1, NativeMoneyModel.Zero);
            });
            AssertThrows<ArgumentException>(delegate
            {
                new NativeCommandDescriptor(
                    RegisteredDomesticCommand.Patrol, "x", 0, NativeOuterTaskType.Patrol,
                    string.Empty, 1, NativeMoneyModel.Zero);
            });
            AssertThrows<ArgumentOutOfRangeException>(delegate
            {
                new NativeCommandDescriptor(
                    RegisteredDomesticCommand.Patrol, "x", 0, NativeOuterTaskType.Patrol,
                    "x", 0, NativeMoneyModel.Zero);
            });
            AssertThrows<ArgumentOutOfRangeException>(delegate
            {
                new NativeCommandDescriptor(
                    RegisteredDomesticCommand.Patrol, "x", 0, NativeOuterTaskType.Patrol,
                    "x", 1, (NativeMoneyModel)99);
            });
            AssertThrows<ArgumentException>(delegate
            {
                new NativeCommandDescriptor(
                    RegisteredDomesticCommand.Patrol, "patrol-typo", 0, NativeOuterTaskType.Patrol,
                    "outer-task-patrol", 0x0060B920U, NativeMoneyModel.PerOfficerFifty);
            });
            AssertThrows<ArgumentException>(delegate
            {
                new NativeCommandDescriptor(
                    RegisteredDomesticCommand.Patrol, "patrol", 9, NativeOuterTaskType.Patrol,
                    "outer-task-patrol", 0x0060B920U, NativeMoneyModel.PerOfficerFifty);
            });
            AssertThrows<ArgumentException>(delegate
            {
                new NativeCommandDescriptor(
                    RegisteredDomesticCommand.Patrol, "patrol", 0, NativeOuterTaskType.Patrol,
                    "outer-task-patrol", 0x0060B921U, NativeMoneyModel.PerOfficerFifty);
            });
            AssertThrows<ArgumentException>(delegate
            {
                new NativeCommandDescriptor(
                    RegisteredDomesticCommand.Patrol, "patrol", 0, NativeOuterTaskType.Patrol,
                    "outer-task-patrol", 0x0060B920U, NativeMoneyModel.Zero);
            });
        }

        private static void UnknownAndDefaultEnumsThrow()
        {
            AssertThrows<ArgumentOutOfRangeException>(delegate
            {
                NativeCommandDescriptorRegistry.GetRequired(RegisteredDomesticCommand.Unknown);
            });
            AssertThrows<ArgumentOutOfRangeException>(delegate
            {
                CreateRequest(RegisteredDomesticCommand.Unknown, 1, Digest(0x11), 100, 1000);
            });
            AssertThrows<ArgumentOutOfRangeException>(delegate
            {
                new OpenOuterEvidence(
                    Guid.NewGuid(), Digest(1), 1, 3, 1, Digest(2), Digest(3), 1, 1,
                    Token(ObjectKind.RootController, 1), Token(ObjectKind.CityTarget, 2),
                    Token(ObjectKind.OuterTask, 3), 1, NativeOuterTaskType.Unknown, 1, true, 1000);
            });
        }

        private static void RequestFingerprintBindsDescriptorAndFields()
        {
            FixedDigest generation = Digest(0x30);
            SingleCommandRequest commerce = CreateRequest(
                RegisteredDomesticCommand.Commerce, 1, generation, 100, 1000);
            SingleCommandRequest train = new SingleCommandRequest(
                commerce.RequestId,
                commerce.Sequence,
                RegisteredDomesticCommand.Train,
                commerce.CityId,
                commerce.CorpsId,
                commerce.OfficerIds,
                commerce.ReserveMoney,
                commerce.ContextDigest,
                commerce.GenerationDigest,
                commerce.TimeToLiveMilliseconds);
            SingleCommandRequest changedCity = new SingleCommandRequest(
                commerce.RequestId,
                commerce.Sequence,
                commerce.Descriptor.Command,
                commerce.CityId + 1,
                commerce.CorpsId,
                commerce.OfficerIds,
                commerce.ReserveMoney,
                commerce.ContextDigest,
                commerce.GenerationDigest,
                commerce.TimeToLiveMilliseconds);
            Assert(!commerce.RequestFingerprint.Equals(train.RequestFingerprint), "Descriptor did not affect fingerprint.");
            Assert(!commerce.RequestFingerprint.Equals(changedCity.RequestFingerprint), "City did not affect fingerprint.");
            Assert(!commerce.AuthorizesLiveMutation, "Request became authorizing.");
        }

        private static void AllFiveCommandsCompleteThroughCoordinator()
        {
            foreach (NativeCommandDescriptor descriptor in NativeCommandDescriptorRegistry.All)
            {
                Fixture fixture = new Fixture(descriptor.Command);
                fixture.Start();
                TransactionTransition result = fixture.DriveSuccess();
                Assert(result.Outcome == TransactionOutcome.SyntheticCommittedVerified,
                    descriptor.Key + " did not reach synthetic verification.");
                Assert(result.InnerAcceptAttempts == 1 && result.OuterAcceptAttempts == 1,
                    "Confirmation attempts are not exactly one.");
                Assert(!result.CommitAuthorized, "Synthetic success authorized a commit.");
            }
        }

        private static void CommitRequiresAuthenticatedNextGeneration()
        {
            Fixture fixture = new Fixture(RegisteredDomesticCommand.Commerce);
            fixture.Start();
            TransactionTransition committed = fixture.DriveSuccess();
            Assert(committed.Outcome == TransactionOutcome.SyntheticCommittedVerified, "Commit did not verify.");
            CoordinatorStartResult stale = fixture.Coordinator.TryStart(
                fixture.NewRequest(2, fixture.Key.GenerationDigest));
            Assert(!stale.Accepted && stale.Rejection == CoordinatorStartRejection.GenerationAdvanceRequired,
                "A stale generation started after commit.");
            AssertThrows<InvalidOperationException>(delegate
            {
                ProcessGenerationCoordinator.AttachForTesting(
                    new ProcessGenerationKey(
                        fixture.Key.ProcessId,
                        fixture.Key.ProcessCreationUtcTicks,
                        fixture.Key.GenerationNumber + 2,
                        Digest(0x99)),
                    fixture.Clock);
            });
            ProcessGenerationKey verified = new ProcessGenerationKey(
                fixture.Key.ProcessId,
                fixture.Key.ProcessCreationUtcTicks,
                fixture.Key.GenerationNumber + 1,
                Digest(0x80));
            ProcessGenerationCoordinator next = ProcessGenerationCoordinator.AttachForTesting(verified, fixture.Clock);
            CoordinatorStartResult nextStart = next.TryStart(fixture.NewRequest(1, verified.GenerationDigest));
            Assert(nextStart.Accepted, "Authenticated next generation could not start.");
        }

        private static void KnownBusinessGatesSkipBeforeIntent()
        {
            VerifySkip(BusinessSkipReason.DelegatedCity, false, true, 10, 10000, 0);
            VerifySkip(BusinessSkipReason.NativeGreyedOut, true, false, 10, 10000, 0);
            VerifySkip(BusinessSkipReason.InsufficientOfficers, true, true, 4, 10000, 0);
            VerifySkip(BusinessSkipReason.InsufficientFundsOrReserve, true, true, 10, 2249, 2000);
        }

        private static void GlobalSingleFlightUsesRealBarrier()
        {
            Fixture fixture = new Fixture(RegisteredDomesticCommand.Commerce);
            ProcessGenerationCoordinator first = fixture.Coordinator;
            ProcessGenerationCoordinator second = fixture.AttachSibling();
            CoordinatorStartResult firstResult = null;
            CoordinatorStartResult secondResult = null;
            Exception firstError = null;
            Exception secondError = null;
            using (Barrier barrier = new Barrier(3))
            {
                Thread one = new Thread(new ThreadStart(delegate
                {
                    try
                    {
                        barrier.SignalAndWait();
                        firstResult = first.TryStart(fixture.Request);
                    }
                    catch (Exception exception)
                    {
                        firstError = exception;
                    }
                }));
                Thread two = new Thread(new ThreadStart(delegate
                {
                    try
                    {
                        barrier.SignalAndWait();
                        secondResult = second.TryStart(fixture.Request);
                    }
                    catch (Exception exception)
                    {
                        secondError = exception;
                    }
                }));
                one.Start();
                two.Start();
                barrier.SignalAndWait();
                one.Join();
                two.Join();
            }

            Assert(firstError == null && secondError == null, "Concurrent start threw.");
            int accepted = (firstResult.Accepted ? 1 : 0) + (secondResult.Accepted ? 1 : 0);
            Assert(accepted == 1, "Global process-generation slot accepted " + accepted + " starts.");
            CoordinatorStartResult rejected = firstResult.Accepted ? secondResult : firstResult;
            Assert(rejected.Rejection == CoordinatorStartRejection.ActiveTransactionExists,
                "Concurrent loser has wrong rejection: " + rejected.Rejection + ".");
        }

        private static void MultipleCoordinatorsShareOneSlot()
        {
            Fixture fixture = new Fixture(RegisteredDomesticCommand.Commerce);
            ProcessGenerationCoordinator sibling = fixture.AttachSibling();
            fixture.Start();
            SingleCommandRequest secondRequest = fixture.NewRequest(2, fixture.Key.GenerationDigest);
            CoordinatorStartResult rejected = sibling.TryStart(secondRequest);
            Assert(!rejected.Accepted && rejected.Rejection == CoordinatorStartRejection.ActiveTransactionExists,
                "Sibling coordinator bypassed global single-flight.");
        }

        private static void AttemptIsRecordedBeforeCallback()
        {
            Fixture fixture = new Fixture(RegisteredDomesticCommand.Commerce);
            fixture.Start();
            fixture.SubmitBegin();
            ActionIssueResult issue = fixture.Coordinator.IssueNextAction(fixture.Handle);
            Assert(issue.Issued && issue.Intent != null, "Bind intent was not issued.");
            Assert(issue.Intent.AttemptNumber == 1 && !issue.Intent.IsConsumed, "Attempt was not recorded before callback.");
            Assert(issue.Transition.MutationStarted, "Issued callback was not treated as potentially mutating.");
        }

        private static void SecondIntentIsAbortNotReissue()
        {
            Fixture fixture = new Fixture(RegisteredDomesticCommand.Commerce);
            fixture.Start();
            fixture.SubmitBegin();
            ActionIssueResult first = fixture.Coordinator.IssueNextAction(fixture.Handle);
            ActionIssueResult second = fixture.Coordinator.IssueNextAction(fixture.Handle);
            Assert(first.Issued && !second.Issued, "Second intent was issued.");
            AssertAbort(second.Transition, AbortUncertainReason.ActionIntentReplay);
            Assert(fixture.Coordinator.GetFaultLatch().IsLatched, "Intent replay did not latch the session.");
        }

        private static void DoubleCallbackExecutionClaimsOnce()
        {
            Fixture fixture = new Fixture(RegisteredDomesticCommand.Commerce);
            ProcessGenerationCoordinator sibling = fixture.AttachSibling();
            fixture.Start();
            fixture.SubmitBegin();
            ActionIssueResult issue = fixture.Coordinator.IssueNextAction(fixture.Handle);
            ActionClaimResult first = null;
            ActionClaimResult second = null;
            using (Barrier barrier = new Barrier(3))
            {
                Thread one = new Thread(new ThreadStart(delegate
                {
                    barrier.SignalAndWait();
                    first = fixture.Coordinator.ClaimActionExecution(fixture.Handle, issue.Intent);
                }));
                Thread two = new Thread(new ThreadStart(delegate
                {
                    barrier.SignalAndWait();
                    second = sibling.ClaimActionExecution(fixture.Handle, issue.Intent);
                }));
                one.Start();
                two.Start();
                barrier.SignalAndWait();
                one.Join();
                two.Join();
            }

            int claimed = (first != null && first.Claimed ? 1 : 0) + (second != null && second.Claimed ? 1 : 0);
            Assert(claimed == 1, "Duplicate callbacks obtained " + claimed + " execution claims.");
            ActionClaimResult loser = first != null && first.Claimed ? second : first;
            AssertAbort(loser.Transition, AbortUncertainReason.ActionExecutionClaimReplay);
            Assert(fixture.Coordinator.GetFaultLatch().CleanupDirective.Kind == AbortCleanupKind.HaltRestart,
                "Concurrent callback while execution was in flight did not require restart.");
        }

        private static void SameActionClaimSerialDispatchInvokesSideEffectOnce()
        {
            Fixture fixture = new Fixture(RegisteredDomesticCommand.Commerce);
            fixture.Start();
            fixture.SubmitBegin();
            ActionIssueResult issue = fixture.Coordinator.IssueNextAction(fixture.Handle);
            ActionClaimResult claim = fixture.Coordinator.ClaimActionExecution(fixture.Handle, issue.Intent);
            int sideEffects = 0;
            bool callbackReenteredCoordinator = false;
            ActionInvocationResult first = fixture.Coordinator.InvokeClaimedActionForTesting(
                fixture.Handle,
                claim.Claim,
                NativeActionMethod.BindTargetCandidate,
                delegate(ActionSideEffectPermit permit)
                {
                    callbackReenteredCoordinator = fixture.Coordinator.PollExpiry(fixture.Handle) == null;
                    Interlocked.Increment(ref sideEffects);
                    return fixture.CreateBindEvidence(issue.Intent, null, null, true);
                });
            ActionInvocationResult replay = fixture.Coordinator.InvokeClaimedActionForTesting(
                fixture.Handle,
                claim.Claim,
                NativeActionMethod.BindTargetCandidate,
                delegate(ActionSideEffectPermit permit)
                {
                    Interlocked.Increment(ref sideEffects);
                    return fixture.CreateBindEvidence(issue.Intent, null, null, true);
                });

            Assert(first.CallbackInvoked && first.Transition != null && first.Transition.Accepted,
                "The first admitted action callback did not settle successfully.");
            Assert(callbackReenteredCoordinator,
                "The external action callback ran while a coordinator/global operation gate was held.");
            Assert(!replay.CallbackInvoked && sideEffects == 1,
                "Serial dispatch of one action claim entered " + sideEffects + " side effects.");
            Assert(fixture.Coordinator.GetFaultLatch().CleanupDirective.Kind == AbortCleanupKind.HaltRestart,
                "Serial action claim replay did not require restart.");
        }

        private static void SameActionClaimConcurrentDispatchInvokesSideEffectOnce()
        {
            Fixture fixture = new Fixture(RegisteredDomesticCommand.Commerce);
            fixture.Start();
            fixture.SubmitBegin();
            ActionIssueResult issue = fixture.Coordinator.IssueNextAction(fixture.Handle);
            ActionClaimResult claim = fixture.Coordinator.ClaimActionExecution(fixture.Handle, issue.Intent);
            int sideEffects = 0;
            ActionInvocationResult first = null;
            ActionInvocationResult second = null;
            using (ManualResetEvent callbackEntered = new ManualResetEvent(false))
            using (ManualResetEvent releaseCallback = new ManualResetEvent(false))
            {
                Thread one = new Thread(new ThreadStart(delegate
                {
                    first = fixture.Coordinator.InvokeClaimedActionForTesting(
                        fixture.Handle,
                        claim.Claim,
                        NativeActionMethod.BindTargetCandidate,
                        delegate(ActionSideEffectPermit permit)
                        {
                            Interlocked.Increment(ref sideEffects);
                            callbackEntered.Set();
                            releaseCallback.WaitOne();
                            return fixture.CreateBindEvidence(issue.Intent, null, null, true);
                        });
                }));
                one.Start();
                bool entered = callbackEntered.WaitOne(5000);
                Thread two = new Thread(new ThreadStart(delegate
                {
                    second = fixture.AttachSibling().InvokeClaimedActionForTesting(
                        fixture.Handle,
                        claim.Claim,
                        NativeActionMethod.BindTargetCandidate,
                        delegate(ActionSideEffectPermit permit)
                        {
                            Interlocked.Increment(ref sideEffects);
                            return fixture.CreateBindEvidence(issue.Intent, null, null, true);
                        });
                }));
                two.Start();
                bool secondFinished = two.Join(5000);
                releaseCallback.Set();
                bool firstFinished = one.Join(5000);
                Assert(entered && secondFinished && firstFinished,
                    "Concurrent action dispatch deadlocked at the callback boundary.");
            }

            Assert(first != null && first.CallbackInvoked,
                "The admitted action callback was not invoked.");
            Assert(second != null && !second.CallbackInvoked && sideEffects == 1,
                "Concurrent dispatch of one action claim entered " + sideEffects + " side effects.");
            Assert(fixture.Coordinator.GetFaultLatch().CleanupDirective.Kind == AbortCleanupKind.HaltRestart,
                "Concurrent action claim replay did not require restart.");
        }

        private static void WrongActionMethodIsRejectedBeforeSideEffect()
        {
            Fixture fixture = new Fixture(RegisteredDomesticCommand.Commerce);
            fixture.Start();
            fixture.SubmitBegin();
            ActionIssueResult issue = fixture.Coordinator.IssueNextAction(fixture.Handle);
            ActionClaimResult claim = fixture.Coordinator.ClaimActionExecution(fixture.Handle, issue.Intent);
            int sideEffects = 0;
            ActionInvocationResult result = fixture.Coordinator.InvokeClaimedActionForTesting(
                fixture.Handle,
                claim.Claim,
                NativeActionMethod.OpenOuter,
                delegate(ActionSideEffectPermit permit)
                {
                    Interlocked.Increment(ref sideEffects);
                    return fixture.CreateBindEvidence(issue.Intent, null, null, true);
                });

            Assert(!result.CallbackInvoked && sideEffects == 0,
                "Wrong action method reached the native side effect.");
            AssertAbort(result.Transition, AbortUncertainReason.ActionReceiptBindingMismatch);
            Assert(result.Transition.CleanupDirective.Kind == AbortCleanupKind.HaltRestart,
                "Wrong action method did not require restart.");
        }

        private static void ClaimedActionExpiryIsRejectedBeforeSideEffect()
        {
            Fixture fixture = new Fixture(RegisteredDomesticCommand.Commerce);
            fixture.Start();
            fixture.SubmitBegin();
            ActionIssueResult issue = fixture.Coordinator.IssueNextAction(fixture.Handle);
            ActionClaimResult claim = fixture.Coordinator.ClaimActionExecution(fixture.Handle, issue.Intent);
            fixture.Clock.Now = fixture.Deadline;
            int sideEffects = 0;
            ActionInvocationResult result = fixture.Coordinator.InvokeClaimedActionForTesting(
                fixture.Handle,
                claim.Claim,
                NativeActionMethod.BindTargetCandidate,
                delegate(ActionSideEffectPermit permit)
                {
                    Interlocked.Increment(ref sideEffects);
                    return fixture.CreateBindEvidence(issue.Intent, null, null, true);
                });

            Assert(!result.CallbackInvoked && sideEffects == 0,
                "A claimed action entered a side effect at the exclusive deadline.");
            AssertAbort(result.Transition, AbortUncertainReason.ExternalTimeout);
            Assert(result.Transition.CleanupDirective.Kind == AbortCleanupKind.HaltRestart,
                "Expired executing action did not require restart.");
        }

        private static void CallbackFailureAbortsWithoutReissue()
        {
            Fixture fixture = new Fixture(RegisteredDomesticCommand.Commerce);
            fixture.Start();
            fixture.SubmitBegin();
            ActionIssueResult issue = fixture.Coordinator.IssueNextAction(fixture.Handle);
            ActionClaimResult claim = fixture.Coordinator.ClaimActionExecution(fixture.Handle, issue.Intent);
            Assert(claim.Claimed, "Callback failure test could not claim execution.");
            ActionInvocationResult invocation = fixture.Coordinator.InvokeClaimedActionForTesting(
                fixture.Handle,
                claim.Claim,
                NativeActionMethod.BindTargetCandidate,
                delegate(ActionSideEffectPermit permit)
                {
                    throw new InvalidOperationException("synthetic exception before observation");
                });
            TransactionTransition result = invocation.Transition;
            AssertAbort(result, AbortUncertainReason.ActionCallbackFailed);
            Assert(issue.Intent.IsConsumed, "Failed callback capability remained reusable.");
            ActionIssueResult retry = fixture.Coordinator.IssueNextAction(fixture.Handle);
            Assert(!retry.Issued, "Failed callback was reissued.");
        }

        private static void EnteredActionThrowHaltsAllSevenStages()
        {
            AssertEnteredActionFailureMatrix(true);
        }

        private static void EnteredActionNullHaltsAllSevenStages()
        {
            AssertEnteredActionFailureMatrix(false);
        }

        private static void AssertEnteredActionFailureMatrix(bool throwFromCallback)
        {
            TransactionStage[] stages =
            {
                TransactionStage.BindTargetCandidate,
                TransactionStage.OpenOuter,
                TransactionStage.OpenSelector,
                TransactionStage.Clear,
                TransactionStage.NativeFillMax,
                TransactionStage.AcceptInner,
                TransactionStage.AcceptOuter
            };
            for (int index = 0; index < stages.Length; index++)
            {
                TransactionStage stage = stages[index];
                Fixture fixture = new Fixture(RegisteredDomesticCommand.Commerce);
                ActionIssueResult issue = DriveToActionStage(fixture, stage);
                ActionClaimResult claim = fixture.Coordinator.ClaimActionExecution(
                    fixture.Handle, issue.Intent);
                int sideEffects = 0;
                ActionInvocationResult invocation = fixture.Coordinator.InvokeClaimedActionForTesting(
                    fixture.Handle,
                    claim.Claim,
                    ActionStages.GetRequiredMethod(stage),
                    delegate(ActionSideEffectPermit permit)
                    {
                        Interlocked.Increment(ref sideEffects);
                        if (throwFromCallback)
                        {
                            throw new InvalidOperationException("synthetic entered callback failure");
                        }

                        return null;
                    });

                Assert(invocation.CallbackInvoked && sideEffects == 1,
                    stage + " did not enter exactly one failing callback.");
                AssertAbort(invocation.Transition, AbortUncertainReason.ActionCallbackFailed);
                Assert(invocation.Transition.CleanupDirective.Kind == AbortCleanupKind.HaltRestart,
                    stage + " callback failure underestimated unknown native side effects.");
                FaultLatchSnapshot fault = fixture.Coordinator.GetFaultLatch();
                CleanupIssueResult cleanup = fixture.Coordinator.IssueCleanup(
                    fault.FaultId, fault.CleanupDirective.DirectiveId);
                Assert(!cleanup.Issued,
                    stage + " callback failure incorrectly permitted cleanup unlock.");
                Assert(!fixture.Coordinator.AcknowledgeFault(
                    fault.FaultId, ProcessGenerationCoordinator.RequiredManualAcknowledgement),
                    stage + " callback failure was manually acknowledged as recoverable.");
                Assert(!fixture.Coordinator.TryStart(
                    fixture.NewRequest(2, fixture.Key.GenerationDigest)).Accepted,
                    stage + " callback failure allowed another request in the same generation.");
                byte marker = unchecked((byte)(0xD0 + index + (throwFromCallback ? 0 : 8)));
                AssertThrows<InvalidOperationException>(delegate
                {
                    ProcessGenerationCoordinator.AttachForTesting(
                        new ProcessGenerationKey(
                            fixture.Key.ProcessId,
                            fixture.Key.ProcessCreationUtcTicks,
                            fixture.Key.GenerationNumber + 1,
                            Digest(marker)),
                        fixture.Clock);
                });
            }
        }

        private static ActionIssueResult DriveToActionStage(
            Fixture fixture,
            TransactionStage stage)
        {
            fixture.Start();
            switch (stage)
            {
                case TransactionStage.BindTargetCandidate:
                    Assert(fixture.SubmitBegin().Accepted, "Begin did not reach Bind.");
                    break;
                case TransactionStage.OpenOuter:
                    Assert(fixture.SubmitBegin().Accepted, "Begin did not reach Bind.");
                    fixture.SubmitNormalBind();
                    break;
                case TransactionStage.OpenSelector:
                    Assert(fixture.SubmitBegin().Accepted, "Begin did not reach Bind.");
                    fixture.SubmitNormalBind();
                    fixture.SubmitNormalOpenOuter();
                    break;
                case TransactionStage.Clear:
                    fixture.DriveToSelector();
                    break;
                case TransactionStage.NativeFillMax:
                    fixture.DriveToSelector();
                    ActionIssueResult clear = fixture.Coordinator.IssueNextAction(fixture.Handle);
                    Assert(fixture.SubmitReceipt(
                        clear, fixture.CreateClearEvidence(clear.Intent, null)).Accepted,
                        "Clear did not reach NativeFillMax.");
                    break;
                case TransactionStage.AcceptInner:
                    fixture.DriveThroughFill();
                    Assert(fixture.SubmitVerifyFive(fixture.Request.OfficerIds).Accepted,
                        "VerifyExactlyExpected5 did not reach AcceptInner.");
                    break;
                case TransactionStage.AcceptOuter:
                    fixture.DriveThroughAcceptInner();
                    Assert(fixture.Coordinator.SubmitObservation(
                        fixture.Handle,
                        fixture.Factory.Seal(fixture.CreateVerifyWorkingEvidence(
                            fixture.Request.OfficerIds))).Accepted,
                        "VerifyWorking did not reach AcceptOuter.");
                    break;
                default:
                    throw new ArgumentOutOfRangeException("stage");
            }

            ActionIssueResult issue = fixture.Coordinator.IssueNextAction(fixture.Handle);
            Assert(issue.Issued && issue.Intent.Stage == stage,
                "Fixture did not issue expected action stage " + stage + ".");
            return issue;
        }

        private static void TrustedClockBlocksIntentBeforeExpiryBoundary()
        {
            Fixture fixture = new Fixture(RegisteredDomesticCommand.Commerce);
            fixture.Start();
            fixture.SubmitBegin();
            fixture.Clock.Now = fixture.Deadline;
            ActionIssueResult result = fixture.Coordinator.IssueNextAction(fixture.Handle);
            Assert(!result.Issued, "Intent was issued at the exclusive deadline.");
            AssertAbort(result.Transition, AbortUncertainReason.ExternalTimeout);
            Assert(result.Transition.CleanupDirective.Kind == AbortCleanupKind.NoMutation,
                "Pre-intent expiry has wrong cleanup directive.");
        }

        private static void TrustedClockBlocksReceiptAfterExpiry()
        {
            Fixture fixture = new Fixture(RegisteredDomesticCommand.Commerce);
            fixture.Start();
            fixture.SubmitBegin();
            ActionIssueResult issue = fixture.Coordinator.IssueNextAction(fixture.Handle);
            ActionClaimResult claim = fixture.Coordinator.ClaimActionExecution(fixture.Handle, issue.Intent);
            Assert(claim.Claimed, "Receipt expiry test could not claim execution.");
            ActionInvocationResult invocation = fixture.Coordinator.InvokeClaimedActionForTesting(
                fixture.Handle,
                claim.Claim,
                NativeActionMethod.BindTargetCandidate,
                delegate(ActionSideEffectPermit permit)
                {
                    fixture.Clock.Now = fixture.Deadline;
                    return fixture.CreateBindEvidence(issue.Intent, null, 1, true);
                });
            TransactionTransition result = invocation.Transition;
            AssertAbort(result, AbortUncertainReason.ExternalTimeout);
            Assert(claim.Claim.SideEffectEntered
                && issue.Intent.State == OneShotExecutionState.Executing,
                "Entered callback without a timely receipt was incorrectly reusable or settled.");
            Assert(result.CleanupDirective.Kind == AbortCleanupKind.HaltRestart,
                "Settled bind callback without a timely receipt must require restart.");
        }

        private static void DelayedExecutionClaimIsRevokedAtDeadline()
        {
            Fixture fixture = new Fixture(RegisteredDomesticCommand.Commerce);
            fixture.Start();
            fixture.SubmitBegin();
            ActionIssueResult issue = fixture.Coordinator.IssueNextAction(fixture.Handle);
            fixture.Clock.Now = fixture.Deadline;
            ActionClaimResult claim = fixture.Coordinator.ClaimActionExecution(fixture.Handle, issue.Intent);
            Assert(!claim.Claimed, "A callback claimed execution at the exclusive deadline.");
            AssertAbort(claim.Transition, AbortUncertainReason.ExternalTimeout);
            Assert(issue.Intent.State == OneShotExecutionState.Revoked,
                "Deadline Abort left the issued capability executable.");
        }

        private static void AbortRevokesStaleIntentAcrossGeneration()
        {
            Fixture fixture = new Fixture(RegisteredDomesticCommand.Commerce);
            fixture.Start();
            fixture.SubmitBegin();
            ActionIssueResult issue = fixture.Coordinator.IssueNextAction(fixture.Handle);
            TrustedObservationFactory oldFactory = fixture.Factory;
            TransactionTransition abort = fixture.Coordinator.SubmitObservation(
                fixture.Handle,
                fixture.Factory.Seal(fixture.CreateBeginEvidence(null, null)));
            AssertAbort(abort, AbortUncertainReason.ActionIntentRequired);
            Assert(issue.Intent.State == OneShotExecutionState.Revoked,
                "Abort did not atomically revoke the pending intent.");

            FaultLatchSnapshot fault = fixture.Coordinator.GetFaultLatch();
            Assert(fault.CleanupCompleted && fixture.Coordinator.AcknowledgeFault(
                fault.FaultId, ProcessGenerationCoordinator.RequiredManualAcknowledgement),
                "No-mutation Abort could not be explicitly acknowledged.");
            ProcessGenerationKey nextKey = new ProcessGenerationKey(
                fixture.Key.ProcessId,
                fixture.Key.ProcessCreationUtcTicks,
                fixture.Key.GenerationNumber + 1,
                Digest(0xD1));
            ProcessGenerationCoordinator next = ProcessGenerationCoordinator.AttachForTesting(nextKey, fixture.Clock);
            Assert(!oldFactory.IsActive, "Generation advance did not revoke the frozen old factory.");
            AssertThrows<InvalidOperationException>(delegate
            {
                oldFactory.Seal(fixture.CreateBeginEvidence(null, null));
            });
            ActionClaimResult oldCoordinatorClaim = fixture.Coordinator.ClaimActionExecution(
                fixture.Handle, issue.Intent);
            Assert(!oldCoordinatorClaim.Claimed, "Retired coordinator accepted a stale action intent.");
            Assert(next.TryStart(fixture.NewRequest(1, nextKey.GenerationDigest)).Accepted,
                "Strictly new generation did not start after safe revocation and acknowledgement.");
        }

        private static void ExecutingWithoutReceiptRequiresRestart()
        {
            Fixture fixture = new Fixture(RegisteredDomesticCommand.Commerce);
            fixture.Start();
            fixture.SubmitBegin();
            ActionIssueResult issue = fixture.Coordinator.IssueNextAction(fixture.Handle);
            ActionClaimResult claim = fixture.Coordinator.ClaimActionExecution(fixture.Handle, issue.Intent);
            Assert(claim.Claimed, "Callback did not enter Executing state.");
            fixture.Clock.Now = fixture.Deadline;
            TransactionTransition abort = fixture.Coordinator.PollExpiry(fixture.Handle);
            AssertAbort(abort, AbortUncertainReason.ExternalTimeout);
            FaultLatchSnapshot fault = fixture.Coordinator.GetFaultLatch();
            Assert(fault.CleanupDirective.Kind == AbortCleanupKind.HaltRestart && fault.CleanupFailed,
                "In-flight callback without receipt did not latch restart-only recovery.");
            Assert(!fixture.Coordinator.IssueCleanup(
                fault.FaultId, fault.CleanupDirective.DirectiveId).Issued,
                "Restart-only fault issued a cleanup action.");
            Assert(!fixture.Coordinator.AcknowledgeFault(
                fault.FaultId, ProcessGenerationCoordinator.RequiredManualAcknowledgement),
                "In-flight callback fault was unlocked by acknowledgement.");
        }

        private static void EvidenceTimestampIsDiagnosticOnly()
        {
            Fixture fixture = new Fixture(RegisteredDomesticCommand.Commerce);
            fixture.Start();
            TransactionTransition begin = fixture.SubmitBegin(1);
            Assert(begin.Accepted, "Forged diagnostic timestamp affected Begin.");
            ActionIssueResult issue = fixture.Coordinator.IssueNextAction(fixture.Handle);
            TransactionTransition bind = fixture.SubmitReceipt(
                issue,
                fixture.CreateBindEvidence(issue.Intent, null, DateTime.MaxValue.Ticks, true));
            Assert(bind.Accepted, "Forged diagnostic timestamp affected callback receipt.");
        }

        private static void WrongCityLatchesFault()
        {
            Fixture fixture = new Fixture(RegisteredDomesticCommand.Commerce);
            fixture.Start();
            fixture.SubmitBegin();
            ActionIssueResult issue = fixture.Coordinator.IssueNextAction(fixture.Handle);
            TransactionTransition result = fixture.SubmitReceipt(
                issue,
                fixture.CreateBindEvidence(issue.Intent, fixture.Request.CityId + 1, null, true));
            AssertAbort(result, AbortUncertainReason.CityOrCorpsMismatch);
            Assert(fixture.Coordinator.GetFaultLatch().IsLatched, "Wrong-city abort did not latch session.");
        }

        private static void DestroyedAndReusedObjectsAbort()
        {
            Fixture destroyedFixture = new Fixture(RegisteredDomesticCommand.Commerce);
            destroyedFixture.Start();
            destroyedFixture.DriveToSelector();
            ActionIssueResult clear = destroyedFixture.Coordinator.IssueNextAction(destroyedFixture.Handle);
            ObjectGenerationToken destroyed = new ObjectGenerationToken(
                ObjectKind.InnerSelector,
                destroyedFixture.Selector.Identity,
                destroyedFixture.Selector.Generation,
                false);
            TransactionTransition destroyedResult = destroyedFixture.SubmitReceipt(
                clear,
                destroyedFixture.CreateClearEvidence(clear.Intent, destroyed));
            AssertAbort(destroyedResult, AbortUncertainReason.ObjectMissingDestroyedOrReused);

            Fixture reusedFixture = new Fixture(RegisteredDomesticCommand.Commerce);
            reusedFixture.Start();
            reusedFixture.DriveToSelector();
            ActionIssueResult reusedClear = reusedFixture.Coordinator.IssueNextAction(reusedFixture.Handle);
            ObjectGenerationToken reused = new ObjectGenerationToken(
                ObjectKind.InnerSelector,
                reusedFixture.Selector.Identity,
                reusedFixture.Selector.Generation + 1,
                true);
            TransactionTransition reusedResult = reusedFixture.SubmitReceipt(
                reusedClear,
                reusedFixture.CreateClearEvidence(reusedClear.Intent, reused));
            AssertAbort(reusedResult, AbortUncertainReason.ObjectMissingDestroyedOrReused);
        }

        private static void TypedOuterMappingIsEnforcedFieldByField()
        {
            VerifyOuterMismatch(99, null, null);
            VerifyOuterMismatch(null, NativeOuterTaskType.Train, null);
            VerifyOuterMismatch(null, null, 0x0060CCB1U);
        }

        private static void SourceCountMatchesBeginAndBounds()
        {
            Fixture changed = new Fixture(RegisteredDomesticCommand.Commerce);
            changed.Start();
            changed.SubmitBegin();
            changed.SubmitNormalBind();
            changed.SubmitNormalOpenOuter();
            ActionIssueResult issue = changed.Coordinator.IssueNextAction(changed.Handle);
            TransactionTransition changedResult = changed.SubmitReceipt(
                issue,
                changed.CreateOpenSelectorEvidence(issue.Intent, changed.CandidateCount - 1));
            AssertAbort(changedResult, AbortUncertainReason.SourceCountChanged);

            Fixture over = new Fixture(RegisteredDomesticCommand.Commerce);
            over.CandidateCount = SingleCommandRequest.OfficerCount + 1;
            over.Start();
            TransactionTransition begin = over.SubmitBegin();
            AssertAbort(begin, AbortUncertainReason.InvalidBusinessObservation);
        }

        private static void WrongListsAndContextAbort()
        {
            Fixture listFixture = new Fixture(RegisteredDomesticCommand.Commerce);
            listFixture.Start();
            listFixture.DriveThroughFill();
            int[] wrong = { 10, 11, 12, 13, 99 };
            TransactionTransition list = listFixture.SubmitVerifyFive(wrong);
            AssertAbort(list, AbortUncertainReason.SelectionListMismatch);

            Fixture contextFixture = new Fixture(RegisteredDomesticCommand.Commerce);
            contextFixture.Start();
            BeginEvidence raw = contextFixture.CreateBeginEvidence(null, Digest(0xEE));
            TransactionTransition context = contextFixture.Coordinator.SubmitObservation(
                contextFixture.Handle,
                contextFixture.Factory.Seal(raw));
            AssertAbort(context, AbortUncertainReason.ContextDigestMismatch);
        }

        private static void PostSnapshotFieldsFailIndependently()
        {
            VerifyPostFailure(delegate(PostVariant value) { value.CityId++; }, AbortUncertainReason.PostSnapshotBindingMismatch);
            VerifyPostFailure(delegate(PostVariant value) { value.CorpsId++; }, AbortUncertainReason.PostSnapshotBindingMismatch);
            VerifyPostFailure(delegate(PostVariant value)
            {
                value.Command = RegisteredDomesticCommand.Train;
                NativeCommandDescriptor descriptor = NativeCommandDescriptorRegistry.GetRequired(value.Command);
                value.NativeCommandId = descriptor.NativeCommandId;
                value.OuterTaskType = descriptor.OuterTaskType;
                value.OuterTaskVptr = descriptor.OuterTaskVptr;
            }, AbortUncertainReason.PostSnapshotBindingMismatch);
            VerifyPostFailure(delegate(PostVariant value) { value.OrderedOfficerIds = new[] { 10, 11, 12, 13, 99 }; }, AbortUncertainReason.PartialCommitObserved);
            VerifyPostFailure(delegate(PostVariant value) { value.BusyOfficerIds = new[] { 10, 11, 12, 13 }; }, AbortUncertainReason.PartialCommitObserved);
            VerifyPostFailure(delegate(PostVariant value) { value.NativeOrderVerified = false; }, AbortUncertainReason.PartialCommitObserved);
            VerifyPostFailure(delegate(PostVariant value) { value.CommandStateVerified = false; }, AbortUncertainReason.PartialCommitObserved);
            VerifyPostFailure(delegate(PostVariant value) { value.MoneyAfter++; }, AbortUncertainReason.PartialCommitObserved);
        }

        private static void PostSnapshotRequiresStableAuthenticatedNewGeneration()
        {
            Fixture stale = new Fixture(RegisteredDomesticCommand.Train);
            stale.Start();
            stale.DriveThroughAcceptOuter();
            PostVariant staleValue = stale.DefaultPostVariant();
            staleValue.GenerationNumber = stale.Key.GenerationNumber;
            staleValue.GenerationDigest = stale.Key.GenerationDigest;
            TransactionTransition staleResult = stale.SubmitPost(staleValue, staleValue.Clone());
            AssertAbort(staleResult, AbortUncertainReason.ResultGenerationNotFresh);

            Fixture unstable = new Fixture(RegisteredDomesticCommand.Train);
            unstable.Start();
            unstable.DriveThroughAcceptOuter();
            PostVariant first = unstable.DefaultPostVariant();
            PostVariant second = first.Clone();
            second.MoneyAfter++;
            TransactionTransition unstableResult = unstable.SubmitPost(first, second);
            AssertAbort(unstableResult, AbortUncertainReason.PostSnapshotUnstable);

            Fixture foreign = new Fixture(RegisteredDomesticCommand.Train);
            foreign.Start();
            foreign.DriveThroughAcceptOuter();
            Fixture otherSession = new Fixture(RegisteredDomesticCommand.Train);
            otherSession.Start();
            otherSession.DriveThroughAcceptOuter();
            AuthenticatedStablePostSnapshot wrongCapability = otherSession.CreatePostSnapshotOnly(
                otherSession.DefaultPostVariant(),
                otherSession.DefaultPostVariant().Clone());
            VerifyCommittedEvidence raw = foreign.CreateVerifyCommittedEvidence(wrongCapability);
            TransactionTransition foreignResult = foreign.Coordinator.SubmitObservation(
                foreign.Handle, foreign.Factory.Seal(raw));
            AssertAbort(foreignResult, AbortUncertainReason.PostSnapshotUnauthenticated);
        }

        private static void AbortBlocksNewTaskSameGeneration()
        {
            Fixture fixture = new Fixture(RegisteredDomesticCommand.Commerce);
            fixture.Start();
            TransactionTransition abort = fixture.SubmitUntrustedBegin();
            AssertAbort(abort, AbortUncertainReason.UntrustedObservation);
            CoordinatorStartResult blocked = fixture.AttachSibling().TryStart(fixture.NewRequest(2, fixture.Key.GenerationDigest));
            Assert(!blocked.Accepted && blocked.Rejection == CoordinatorStartRejection.FaultLatched,
                "Fault latch allowed a new task in the same generation.");
        }

        private static void ManualAckRequiresNewGeneration()
        {
            Fixture fixture = new Fixture(RegisteredDomesticCommand.Commerce);
            fixture.Start();
            fixture.SubmitUntrustedBegin();
            FaultLatchSnapshot fault = fixture.Coordinator.GetFaultLatch();
            Assert(!fixture.Coordinator.AcknowledgeFault(fault.FaultId, "yes"), "Weak acknowledgement was accepted.");
            Assert(fixture.Coordinator.AcknowledgeFault(
                fault.FaultId, ProcessGenerationCoordinator.RequiredManualAcknowledgement),
                "Exact acknowledgement was rejected.");
            CoordinatorStartResult sameGeneration = fixture.AttachSibling().TryStart(
                fixture.NewRequest(2, fixture.Key.GenerationDigest));
            Assert(!sameGeneration.Accepted && sameGeneration.Rejection == CoordinatorStartRejection.FaultLatched,
                "Acknowledgement cleared the same generation.");

            AssertThrows<InvalidOperationException>(delegate
            {
                ProcessGenerationCoordinator.AttachForTesting(
                    new ProcessGenerationKey(
                        fixture.Key.ProcessId,
                        fixture.Key.ProcessCreationUtcTicks,
                        fixture.Key.GenerationNumber + 1,
                        fixture.Key.GenerationDigest),
                    fixture.Clock);
            });

            ProcessGenerationKey nextKey = new ProcessGenerationKey(
                fixture.Key.ProcessId,
                fixture.Key.ProcessCreationUtcTicks,
                fixture.Key.GenerationNumber + 1,
                Digest(0xA1));
            ProcessGenerationCoordinator next = ProcessGenerationCoordinator.AttachForTesting(nextKey, fixture.Clock);
            SingleCommandRequest nextRequest = fixture.NewRequest(1, nextKey.GenerationDigest);
            CoordinatorStartResult started = next.TryStart(nextRequest);
            Assert(started.Accepted, "Acknowledged fault did not allow a strictly new generation.");
        }

        private static void ProcessRestartResetsFaultWithoutAck()
        {
            Fixture fixture = new Fixture(RegisteredDomesticCommand.Commerce);
            fixture.Start();
            fixture.SubmitUntrustedBegin();
            ProcessGenerationKey restart = new ProcessGenerationKey(
                fixture.Key.ProcessId,
                fixture.Key.ProcessCreationUtcTicks + 1,
                1,
                Digest(0xB1));
            ProcessGenerationCoordinator restarted = ProcessGenerationCoordinator.AttachForTesting(restart, fixture.Clock);
            CoordinatorStartResult result = restarted.TryStart(fixture.NewRequest(1, restart.GenerationDigest));
            Assert(result.Accepted, "New process creation did not reset prior-process fault.");
        }

        private static void SamePidNewCreationRetiresActiveSession()
        {
            Fixture fixture = new Fixture(RegisteredDomesticCommand.Commerce);
            fixture.Start();
            fixture.SubmitBegin();
            ActionIssueResult issue = fixture.Coordinator.IssueNextAction(fixture.Handle);
            TrustedObservationFactory oldFactory = fixture.Factory;

            ProcessGenerationKey restartKey = new ProcessGenerationKey(
                fixture.Key.ProcessId,
                fixture.Key.ProcessCreationUtcTicks + 10,
                1,
                Digest(0xD2));
            ProcessGenerationCoordinator restarted = ProcessGenerationCoordinator.AttachForTesting(
                restartKey, fixture.Clock);
            Assert(issue.Intent.State == OneShotExecutionState.Revoked,
                "PID reuse left the prior active intent claimable.");
            Assert(!oldFactory.IsActive, "PID reuse left the old session factory active.");
            AssertThrows<InvalidOperationException>(delegate
            {
                oldFactory.Seal(fixture.CreateBeginEvidence(null, null));
            });
            Assert(!fixture.Coordinator.IssueNextAction(fixture.Handle).Issued,
                "Old coordinator continued after the same PID acquired a newer creation time.");
            Assert(restarted.TryStart(fixture.NewRequest(1, restartKey.GenerationDigest)).Accepted,
                "Replacement process creation could not acquire its fresh transaction slot.");
        }

        private static void SessionRetireReclaimsBoundedSlot()
        {
            Fixture active = new Fixture(RegisteredDomesticCommand.Commerce);
            active.Start();
            Assert(!active.Coordinator.RetireProcess(),
                "An active/used process session was retired and could lose replay or fault state.");
            Fixture fixture = new Fixture(RegisteredDomesticCommand.Commerce);
            int attachedCount = ProcessGenerationCoordinator.ActiveSessionCountForTesting;
            Assert(attachedCount <= ProcessGenerationCoordinator.MaximumTrackedProcesses,
                "Session table exceeded its hard bound.");
            Assert(fixture.Coordinator.RetireProcess(), "Exact process session was not retired.");
            Assert(ProcessGenerationCoordinator.ActiveSessionCountForTesting == attachedCount - 1,
                "Retired process session was not removed from the bounded table.");
            CoordinatorStartResult stale = fixture.Coordinator.TryStart(fixture.Request);
            Assert(!stale.Accepted && stale.Rejection == CoordinatorStartRejection.StaleCoordinator,
                "Retired coordinator remained usable.");
        }

        private static void TrustedClockReentryAndExceptionFailClosed()
        {
            Fixture reentrant = new Fixture(RegisteredDomesticCommand.Commerce);
            reentrant.Clock.OnRead = delegate
            {
                ProcessGenerationCoordinator.AttachForTesting(
                    new ProcessGenerationKey(
                        reentrant.Key.ProcessId,
                        reentrant.Key.ProcessCreationUtcTicks,
                        reentrant.Key.GenerationNumber + 1,
                        Digest(0xD3)),
                    reentrant.Clock);
            };
            reentrant.Start();
            Assert(reentrant.Clock.CallbackException is InvalidOperationException,
                "Reentrant clock callback was not rejected before generation mutation.");
            Assert(reentrant.Coordinator.Key.GenerationNumber == 1,
                "Reentrant clock callback changed the coordinator generation.");

            Fixture throwing = new Fixture(RegisteredDomesticCommand.Commerce);
            throwing.Start();
            throwing.SubmitBegin();
            ActionIssueResult issue = throwing.Coordinator.IssueNextAction(throwing.Handle);
            ActionClaimResult claim = throwing.Coordinator.ClaimActionExecution(throwing.Handle, issue.Intent);
            ActionInvocationResult invocation = throwing.Coordinator.InvokeClaimedActionForTesting(
                throwing.Handle,
                claim.Claim,
                NativeActionMethod.BindTargetCandidate,
                delegate(ActionSideEffectPermit permit)
                {
                    throwing.Clock.ThrowOnRead = true;
                    return throwing.CreateBindEvidence(issue.Intent, null, null, true);
                });
            TransactionTransition abort = invocation.Transition;
            AssertAbort(abort, AbortUncertainReason.TrustedClockFailure);
            Assert(throwing.Coordinator.GetFaultLatch().IsLatched,
                "Clock exception after callback did not fail closed.");
        }

        private static void CleanupDirectivesFollowAbortStage()
        {
            Fixture noMutation = new Fixture(RegisteredDomesticCommand.Commerce);
            noMutation.Start();
            TransactionTransition noMutationAbort = noMutation.SubmitUntrustedBegin();
            Assert(noMutationAbort.CleanupDirective.Kind == AbortCleanupKind.NoMutation, "Begin cleanup is wrong.");

            Fixture target = new Fixture(RegisteredDomesticCommand.Commerce);
            target.Start();
            target.SubmitBegin();
            target.SubmitNormalBind();
            target.Coordinator.IssueNextAction(target.Handle);
            target.Clock.Now = target.Deadline;
            TransactionTransition targetAbort = target.Coordinator.PollExpiry(target.Handle);
            Assert(targetAbort.CleanupDirective.Kind == AbortCleanupKind.ConditionalRestoreTarget, "Bind cleanup is wrong.");

            Fixture selector = new Fixture(RegisteredDomesticCommand.Commerce);
            selector.Start();
            selector.DriveToSelector();
            ActionIssueResult clear = selector.Coordinator.IssueNextAction(selector.Handle);
            ObjectGenerationToken dead = new ObjectGenerationToken(
                ObjectKind.InnerSelector, selector.Selector.Identity, selector.Selector.Generation, false);
            TransactionTransition selectorAbort = selector.SubmitReceipt(clear, selector.CreateClearEvidence(clear.Intent, dead));
            Assert(selectorAbort.CleanupDirective.Kind == AbortCleanupKind.CancelSelector, "Selector cleanup is wrong.");

            Fixture outer = new Fixture(RegisteredDomesticCommand.Commerce);
            outer.Start();
            outer.DriveThroughAcceptInner();
            VerifyWorkingEvidence wrongWorking = outer.CreateVerifyWorkingEvidence(new[] { 10, 11, 12, 13, 99 });
            TransactionTransition outerAbort = outer.Coordinator.SubmitObservation(
                outer.Handle, outer.Factory.Seal(wrongWorking));
            Assert(outerAbort.CleanupDirective.Kind == AbortCleanupKind.CancelOuter, "Outer cleanup is wrong.");

            Fixture accepted = new Fixture(RegisteredDomesticCommand.Commerce);
            accepted.Start();
            accepted.DriveThroughAcceptOuter();
            PostVariant partial = accepted.DefaultPostVariant();
            partial.BusyOfficerIds = new[] { 10, 11, 12, 13 };
            TransactionTransition acceptedAbort = accepted.SubmitPost(partial, partial.Clone());
            Assert(acceptedAbort.CleanupDirective.Kind == AbortCleanupKind.DoNotRollbackAfterAccept,
                "Post-accept cleanup is wrong.");
        }

        private static void CleanupFailureEscalatesToHaltRestart()
        {
            Fixture fixture = new Fixture(RegisteredDomesticCommand.Commerce);
            fixture.Start();
            fixture.DriveToSelector();
            ActionIssueResult clear = fixture.Coordinator.IssueNextAction(fixture.Handle);
            ObjectGenerationToken dead = new ObjectGenerationToken(
                ObjectKind.InnerSelector, fixture.Selector.Identity, fixture.Selector.Generation, false);
            fixture.SubmitReceipt(clear, fixture.CreateClearEvidence(clear.Intent, dead));
            FaultLatchSnapshot initial = fixture.Coordinator.GetFaultLatch();
            Assert(initial.CleanupDirective.Kind == AbortCleanupKind.CancelSelector, "Expected CancelSelector.");
            CleanupIssueResult cleanup = fixture.Coordinator.IssueCleanup(
                initial.FaultId, initial.CleanupDirective.DirectiveId);
            Assert(cleanup.Issued, "Cleanup intent was not issued.");
            CleanupClaimResult claim = fixture.Coordinator.ClaimCleanupExecution(cleanup.Intent);
            Assert(claim.Claimed, "Cleanup execution was not claimed.");
            CleanupInvocationResult failedInvocation = fixture.Coordinator.InvokeClaimedCleanupForTesting(
                claim.Claim,
                NativeCleanupMethod.CancelSelector,
                delegate(CleanupSideEffectPermit permit)
                {
                    throw new InvalidOperationException("synthetic cleanup exception");
                },
                delegate(CleanupPostReadPermit readPermit)
                {
                    return CreateCleanupRead(initial, false, null);
                });
            Assert(failedInvocation.CallbackInvoked && !failedInvocation.ReceiptAccepted,
                "Failed cleanup receipt was accepted as success.");
            FaultLatchSnapshot failed = fixture.Coordinator.GetFaultLatch();
            Assert(failed.IsLatched && failed.CleanupFailed
                && failed.CleanupDirective.Kind == AbortCleanupKind.HaltRestart,
                "Cleanup failure did not escalate to HaltRestart.");
            Assert(!fixture.Coordinator.AcknowledgeFault(
                failed.FaultId, ProcessGenerationCoordinator.RequiredManualAcknowledgement),
                "HaltRestart fault was acknowledged as recoverable.");
            AssertThrows<InvalidOperationException>(delegate
            {
                ProcessGenerationCoordinator.AttachForTesting(
                    new ProcessGenerationKey(
                        fixture.Key.ProcessId,
                        fixture.Key.ProcessCreationUtcTicks,
                        fixture.Key.GenerationNumber + 1,
                        Digest(0xCC)),
                    fixture.Clock);
            });
        }

        private static void PostReadTicketsAreOrderedIndependentOneShot()
        {
            Fixture outOfOrder = new Fixture(RegisteredDomesticCommand.Train);
            outOfOrder.Start();
            outOfOrder.DriveThroughAcceptOuter();
            PostReadPairIssueResult pair = outOfOrder.Coordinator.IssuePostReadPair(outOfOrder.Handle);
            PostReadClaimResult earlyB = outOfOrder.Coordinator.ClaimPostRead(
                outOfOrder.Handle, pair.Second);
            Assert(!earlyB.Claimed, "Post read B was claimed before A produced a receipt.");
            AssertAbort(earlyB.Transition, AbortUncertainReason.PostReadTicketMismatch);

            Fixture sameObject = new Fixture(RegisteredDomesticCommand.Train);
            sameObject.Start();
            sameObject.DriveThroughAcceptOuter();
            PostReadPairIssueResult samePair = sameObject.Coordinator.IssuePostReadPair(sameObject.Handle);
            PostReadClaimResult firstClaim = sameObject.Coordinator.ClaimPostRead(
                sameObject.Handle, samePair.First);
            PostStateRead sharedRead = sameObject.CreatePostRead(sameObject.DefaultPostVariant());
            AuthenticatedPostReadReceipt firstReceipt = sameObject.Factory.CompletePostRead(
                firstClaim.Claim, sharedRead);
            PostReadClaimResult secondClaim = sameObject.Coordinator.ClaimPostRead(
                sameObject.Handle, samePair.Second);
            AuthenticatedPostReadReceipt secondReceipt = sameObject.Factory.CompletePostRead(
                secondClaim.Claim, sharedRead);
            AssertThrows<InvalidOperationException>(delegate
            {
                sameObject.Factory.CreatePostSnapshot(firstReceipt, secondReceipt);
            });
            Assert(samePair.First.State == OneShotExecutionState.Revoked
                && samePair.Second.State == OneShotExecutionState.Revoked,
                "Invalid same-object A/B pair remained retryable.");

            Fixture sameOrdinal = new Fixture(RegisteredDomesticCommand.Train);
            sameOrdinal.Start();
            sameOrdinal.DriveThroughAcceptOuter();
            PostReadPairIssueResult ordinalPair = sameOrdinal.Coordinator.IssuePostReadPair(sameOrdinal.Handle);
            PostReadClaimResult ordinalClaim = sameOrdinal.Coordinator.ClaimPostRead(
                sameOrdinal.Handle, ordinalPair.First);
            AuthenticatedPostReadReceipt ordinalFirst = sameOrdinal.Factory.CompletePostRead(
                ordinalClaim.Claim, sameOrdinal.CreatePostRead(sameOrdinal.DefaultPostVariant()));
            AuthenticatedPostReadReceipt forgedSecondOrdinal = new AuthenticatedPostReadReceipt(
                ordinalFirst.Capability,
                ordinalFirst.Claim,
                sameOrdinal.CreatePostRead(sameOrdinal.DefaultPostVariant().Clone()));
            AssertThrows<InvalidOperationException>(delegate
            {
                sameOrdinal.Factory.CreatePostSnapshot(ordinalFirst, forgedSecondOrdinal);
            });

            Fixture replay = new Fixture(RegisteredDomesticCommand.Train);
            replay.Start();
            replay.DriveThroughAcceptOuter();
            AuthenticatedStablePostSnapshot snapshot = replay.CreatePostSnapshotOnly(
                replay.DefaultPostVariant(), replay.DefaultPostVariant().Clone());
            AssertThrows<InvalidOperationException>(delegate
            {
                replay.Factory.CreatePostSnapshot(snapshot.FirstReceipt, snapshot.SecondReceipt);
            });
        }

        private static void CleanupRequiresAuthenticatedOneShotAbPostcondition()
        {
            Fixture outOfOrder = CreateSelectorFaultFixture();
            FaultLatchSnapshot outOfOrderFault = outOfOrder.Coordinator.GetFaultLatch();
            CleanupIssueResult outOfOrderIssue = outOfOrder.Coordinator.IssueCleanup(
                outOfOrderFault.FaultId, outOfOrderFault.CleanupDirective.DirectiveId);
            CleanupClaimResult outOfOrderClaim = outOfOrder.Coordinator.ClaimCleanupExecution(
                outOfOrderIssue.Intent);
            CleanupSideEffectPermit outOfOrderPermit = outOfOrder.Coordinator
                .AuthorizeCleanupSideEffectForReceiptTesting(outOfOrderClaim.Claim);
            AssertThrows<InvalidOperationException>(delegate
            {
                outOfOrder.Coordinator.ClaimCleanupPostReadForTesting(outOfOrderPermit, 2);
            });
            Assert(outOfOrderPermit.FirstReadTicket.State == OneShotExecutionState.Revoked
                && outOfOrderPermit.SecondReadTicket.State == OneShotExecutionState.Revoked,
                "Out-of-order cleanup A/B read remained retryable.");

            Fixture sameObject = CreateSelectorFaultFixture();
            FaultLatchSnapshot sameObjectFault = sameObject.Coordinator.GetFaultLatch();
            CleanupIssueResult sameObjectIssue = sameObject.Coordinator.IssueCleanup(
                sameObjectFault.FaultId, sameObjectFault.CleanupDirective.DirectiveId);
            CleanupClaimResult sameObjectClaim = sameObject.Coordinator.ClaimCleanupExecution(
                sameObjectIssue.Intent);
            CleanupSideEffectPermit sameObjectPermit = sameObject.Coordinator
                .AuthorizeCleanupSideEffectForReceiptTesting(sameObjectClaim.Claim);
            CleanupPostStateRead sharedCleanupRead = CreateCleanupRead(sameObjectFault, false, null);
            CleanupPostReadClaim sameFirstClaim = sameObject.Coordinator.ClaimCleanupPostReadForTesting(
                sameObjectPermit, 1);
            AuthenticatedCleanupPostReadReceipt sameFirst = sameObject.Coordinator.CompleteCleanupPostReadForTesting(
                sameFirstClaim, sharedCleanupRead);
            CleanupPostReadClaim sameSecondClaim = sameObject.Coordinator.ClaimCleanupPostReadForTesting(
                sameObjectPermit, 2);
            AuthenticatedCleanupPostReadReceipt sameSecond = sameObject.Coordinator.CompleteCleanupPostReadForTesting(
                sameSecondClaim, sharedCleanupRead);
            AssertThrows<InvalidOperationException>(delegate
            {
                sameObject.Coordinator.CompleteCleanupForTesting(
                    sameObjectPermit, sameFirst, sameSecond, "same object");
            });

            Fixture valid = CreateSelectorFaultFixture();
            FaultLatchSnapshot fault = valid.Coordinator.GetFaultLatch();
            CleanupIssueResult issue = valid.Coordinator.IssueCleanup(
                fault.FaultId, fault.CleanupDirective.DirectiveId);
            CleanupClaimResult claim = valid.Coordinator.ClaimCleanupExecution(issue.Intent);
            CleanupSideEffectPermit validPermit = valid.Coordinator
                .AuthorizeCleanupSideEffectForReceiptTesting(claim.Claim);
            Fixture foreign = new Fixture(RegisteredDomesticCommand.Commerce);
            AssertThrows<InvalidOperationException>(delegate
            {
                foreign.Coordinator.ClaimCleanupPostReadForTesting(validPermit, 1);
            });
            AuthenticatedCleanupReceipt receipt = CreateCleanupReceipt(
                valid, validPermit, fault, false, null, "exact object generations restored");
            AssertThrows<InvalidOperationException>(delegate
            {
                valid.Coordinator.CompleteCleanupForTesting(
                    validPermit,
                    receipt.FirstReceipt,
                    receipt.SecondReceipt,
                    "replayed A/B receipts");
            });
            Assert(valid.Coordinator.SubmitCleanupReceipt(receipt),
                "Authenticated cleanup A/B postcondition was rejected.");
            Assert(valid.Coordinator.AcknowledgeFault(
                fault.FaultId, ProcessGenerationCoordinator.RequiredManualAcknowledgement),
                "Authenticated cleanup could not be explicitly acknowledged.");

            Fixture wrongObject = CreateSelectorFaultFixture();
            FaultLatchSnapshot wrongFault = wrongObject.Coordinator.GetFaultLatch();
            CleanupIssueResult wrongIssue = wrongObject.Coordinator.IssueCleanup(
                wrongFault.FaultId, wrongFault.CleanupDirective.DirectiveId);
            CleanupClaimResult wrongClaim = wrongObject.Coordinator.ClaimCleanupExecution(wrongIssue.Intent);
            ObjectGenerationToken wrongTargetAfter = new ObjectGenerationToken(
                ObjectKind.CityTarget,
                wrongFault.CleanupDirective.TargetAtAbort.Identity,
                wrongFault.CleanupDirective.TargetAtAbort.Generation + 1,
                true);
            AuthenticatedCleanupReceipt wrongReceipt = CreateCleanupReceipt(
                wrongObject,
                wrongClaim.Claim,
                wrongFault,
                false,
                wrongTargetAfter,
                "wrong object generation");
            Assert(!wrongObject.Coordinator.SubmitCleanupReceipt(wrongReceipt),
                "Wrong cleanup object generation was accepted.");
            Assert(wrongObject.Coordinator.GetFaultLatch().CleanupDirective.Kind == AbortCleanupKind.HaltRestart,
                "Wrong cleanup postcondition did not escalate to restart.");

            Fixture review = new Fixture(RegisteredDomesticCommand.Commerce);
            review.Start();
            review.DriveThroughAcceptOuter();
            PostVariant partial = review.DefaultPostVariant();
            partial.BusyOfficerIds = new[] { 10, 11, 12, 13 };
            AssertAbort(review.SubmitPost(partial, partial.Clone()), AbortUncertainReason.PartialCommitObserved);
            FaultLatchSnapshot reviewFault = review.Coordinator.GetFaultLatch();
            Assert(reviewFault.CleanupDirective.Kind == AbortCleanupKind.DoNotRollbackAfterAccept
                && reviewFault.CleanupDirective.RequiresExternalCleanup,
                "Post-accept Abort did not require a stable review receipt.");
            CleanupIssueResult reviewIssue = review.Coordinator.IssueCleanup(
                reviewFault.FaultId, reviewFault.CleanupDirective.DirectiveId);
            CleanupClaimResult reviewClaim = review.Coordinator.ClaimCleanupExecution(reviewIssue.Intent);
            AuthenticatedCleanupReceipt reviewReceipt = CreateCleanupReceipt(
                review,
                reviewClaim.Claim,
                reviewFault,
                true,
                null,
                "stable manual review");
            Assert(review.Coordinator.SubmitCleanupReceipt(reviewReceipt),
                "Stable post-accept review receipt was rejected.");
        }

        private static void CleanupReplayAndConcurrentClaimHaltRestart()
        {
            Fixture claimRace = CreateSelectorFaultFixture();
            ProcessGenerationCoordinator sibling = claimRace.AttachSibling();
            FaultLatchSnapshot raceFault = claimRace.Coordinator.GetFaultLatch();
            CleanupIssueResult issue = claimRace.Coordinator.IssueCleanup(
                raceFault.FaultId, raceFault.CleanupDirective.DirectiveId);
            CleanupClaimResult firstClaim = null;
            CleanupClaimResult secondClaim = null;
            using (Barrier barrier = new Barrier(3))
            {
                Thread one = new Thread(new ThreadStart(delegate
                {
                    barrier.SignalAndWait();
                    firstClaim = claimRace.Coordinator.ClaimCleanupExecution(issue.Intent);
                }));
                Thread two = new Thread(new ThreadStart(delegate
                {
                    barrier.SignalAndWait();
                    secondClaim = sibling.ClaimCleanupExecution(issue.Intent);
                }));
                one.Start();
                two.Start();
                barrier.SignalAndWait();
                one.Join();
                two.Join();
            }

            int claimCount = (firstClaim.Claimed ? 1 : 0) + (secondClaim.Claimed ? 1 : 0);
            Assert(claimCount == 1, "Concurrent cleanup callbacks obtained " + claimCount + " claims.");
            Assert(claimRace.Coordinator.GetFaultLatch().CleanupDirective.Kind == AbortCleanupKind.HaltRestart,
                "Concurrent cleanup claim did not escalate to restart.");

            Fixture receiptRace = CreateSelectorFaultFixture();
            FaultLatchSnapshot receiptFault = receiptRace.Coordinator.GetFaultLatch();
            CleanupIssueResult receiptIssue = receiptRace.Coordinator.IssueCleanup(
                receiptFault.FaultId, receiptFault.CleanupDirective.DirectiveId);
            CleanupClaimResult receiptClaim = receiptRace.Coordinator.ClaimCleanupExecution(receiptIssue.Intent);
            AuthenticatedCleanupReceipt receipt = CreateCleanupReceipt(
                receiptRace,
                receiptClaim.Claim,
                receiptFault,
                false,
                null,
                "concurrent receipt");
            bool firstResult = false;
            bool secondResult = false;
            using (Barrier barrier = new Barrier(3))
            {
                Thread one = new Thread(new ThreadStart(delegate
                {
                    barrier.SignalAndWait();
                    firstResult = receiptRace.Coordinator.SubmitCleanupReceipt(receipt);
                }));
                Thread two = new Thread(new ThreadStart(delegate
                {
                    barrier.SignalAndWait();
                    secondResult = receiptRace.AttachSibling().SubmitCleanupReceipt(receipt);
                }));
                one.Start();
                two.Start();
                barrier.SignalAndWait();
                one.Join();
                two.Join();
            }

            Assert((firstResult ? 1 : 0) + (secondResult ? 1 : 0) == 1,
                "Concurrent cleanup receipt did not consume exactly once.");
            Assert(receiptRace.Coordinator.GetFaultLatch().CleanupDirective.Kind == AbortCleanupKind.HaltRestart,
                "Cleanup receipt replay did not escalate to restart.");
        }

        private static void SameCleanupClaimSerialDispatchInvokesSideEffectOnce()
        {
            Fixture fixture = CreateSelectorFaultFixture();
            FaultLatchSnapshot fault = fixture.Coordinator.GetFaultLatch();
            CleanupIssueResult issue = fixture.Coordinator.IssueCleanup(
                fault.FaultId, fault.CleanupDirective.DirectiveId);
            CleanupClaimResult claim = fixture.Coordinator.ClaimCleanupExecution(issue.Intent);
            int sideEffects = 0;
            bool callbackReenteredCoordinator = false;
            CleanupInvocationResult first = fixture.Coordinator.InvokeClaimedCleanupForTesting(
                claim.Claim,
                NativeCleanupMethod.CancelSelector,
                delegate(CleanupSideEffectPermit permit)
                {
                    callbackReenteredCoordinator = fixture.Coordinator.GetFaultLatch().IsLatched;
                    Interlocked.Increment(ref sideEffects);
                },
                delegate(CleanupPostReadPermit readPermit)
                {
                    return CreateCleanupRead(fault, false, null);
                });
            CleanupInvocationResult replay = fixture.Coordinator.InvokeClaimedCleanupForTesting(
                claim.Claim,
                NativeCleanupMethod.CancelSelector,
                delegate(CleanupSideEffectPermit permit)
                {
                    Interlocked.Increment(ref sideEffects);
                },
                delegate(CleanupPostReadPermit readPermit)
                {
                    return CreateCleanupRead(fault, false, null);
                });

            Assert(first.CallbackInvoked && first.ReceiptAccepted,
                "The first admitted cleanup callback did not settle successfully.");
            Assert(callbackReenteredCoordinator,
                "The external cleanup callback ran while a coordinator/global operation gate was held.");
            Assert(!replay.CallbackInvoked && sideEffects == 1,
                "Serial dispatch of one cleanup claim entered " + sideEffects + " side effects.");
            Assert(fixture.Coordinator.GetFaultLatch().CleanupDirective.Kind == AbortCleanupKind.HaltRestart,
                "Serial cleanup claim replay did not require restart.");
        }

        private static void SameCleanupClaimConcurrentDispatchInvokesSideEffectOnce()
        {
            Fixture fixture = CreateSelectorFaultFixture();
            FaultLatchSnapshot fault = fixture.Coordinator.GetFaultLatch();
            CleanupIssueResult issue = fixture.Coordinator.IssueCleanup(
                fault.FaultId, fault.CleanupDirective.DirectiveId);
            CleanupClaimResult claim = fixture.Coordinator.ClaimCleanupExecution(issue.Intent);
            int sideEffects = 0;
            CleanupInvocationResult first = null;
            CleanupInvocationResult second = null;
            using (ManualResetEvent callbackEntered = new ManualResetEvent(false))
            using (ManualResetEvent releaseCallback = new ManualResetEvent(false))
            {
                Thread one = new Thread(new ThreadStart(delegate
                {
                    first = fixture.Coordinator.InvokeClaimedCleanupForTesting(
                        claim.Claim,
                        NativeCleanupMethod.CancelSelector,
                        delegate(CleanupSideEffectPermit permit)
                        {
                            Interlocked.Increment(ref sideEffects);
                            callbackEntered.Set();
                            releaseCallback.WaitOne();
                        },
                        delegate(CleanupPostReadPermit readPermit)
                        {
                            return CreateCleanupRead(fault, false, null);
                        });
                }));
                one.Start();
                bool entered = callbackEntered.WaitOne(5000);
                Thread two = new Thread(new ThreadStart(delegate
                {
                    second = fixture.AttachSibling().InvokeClaimedCleanupForTesting(
                        claim.Claim,
                        NativeCleanupMethod.CancelSelector,
                        delegate(CleanupSideEffectPermit permit)
                        {
                            Interlocked.Increment(ref sideEffects);
                        },
                        delegate(CleanupPostReadPermit readPermit)
                        {
                            return CreateCleanupRead(fault, false, null);
                        });
                }));
                two.Start();
                bool secondFinished = two.Join(5000);
                releaseCallback.Set();
                bool firstFinished = one.Join(5000);
                Assert(entered && secondFinished && firstFinished,
                    "Concurrent cleanup dispatch deadlocked at the callback boundary.");
            }

            Assert(first != null && first.CallbackInvoked,
                "The admitted cleanup callback was not invoked.");
            Assert(second != null && !second.CallbackInvoked && sideEffects == 1,
                "Concurrent dispatch of one cleanup claim entered " + sideEffects + " side effects.");
            Assert(fixture.Coordinator.GetFaultLatch().CleanupDirective.Kind == AbortCleanupKind.HaltRestart,
                "Concurrent cleanup claim replay did not require restart.");
        }

        private static void WrongCleanupMethodIsRejectedBeforeSideEffect()
        {
            Fixture fixture = CreateSelectorFaultFixture();
            FaultLatchSnapshot fault = fixture.Coordinator.GetFaultLatch();
            CleanupIssueResult issue = fixture.Coordinator.IssueCleanup(
                fault.FaultId, fault.CleanupDirective.DirectiveId);
            CleanupClaimResult claim = fixture.Coordinator.ClaimCleanupExecution(issue.Intent);
            int sideEffects = 0;
            CleanupInvocationResult result = fixture.Coordinator.InvokeClaimedCleanupForTesting(
                claim.Claim,
                NativeCleanupMethod.CancelOuter,
                delegate(CleanupSideEffectPermit permit)
                {
                    Interlocked.Increment(ref sideEffects);
                },
                delegate(CleanupPostReadPermit readPermit)
                {
                    return CreateCleanupRead(fault, false, null);
                });

            Assert(!result.CallbackInvoked && sideEffects == 0,
                "Wrong cleanup method reached the native side effect.");
            Assert(fixture.Coordinator.GetFaultLatch().CleanupDirective.Kind == AbortCleanupKind.HaltRestart,
                "Wrong cleanup method did not require restart.");
        }

        private static void ClaimedCleanupExpiryIsRejectedBeforeSideEffect()
        {
            Fixture fixture = CreateSelectorFaultFixture();
            FaultLatchSnapshot fault = fixture.Coordinator.GetFaultLatch();
            CleanupIssueResult issue = fixture.Coordinator.IssueCleanup(
                fault.FaultId, fault.CleanupDirective.DirectiveId);
            CleanupClaimResult claim = fixture.Coordinator.ClaimCleanupExecution(issue.Intent);
            fixture.Clock.Now = issue.Intent.DeadlineMonotonicTicks;
            int sideEffects = 0;
            CleanupInvocationResult result = fixture.Coordinator.InvokeClaimedCleanupForTesting(
                claim.Claim,
                NativeCleanupMethod.CancelSelector,
                delegate(CleanupSideEffectPermit permit)
                {
                    Interlocked.Increment(ref sideEffects);
                },
                delegate(CleanupPostReadPermit readPermit)
                {
                    return CreateCleanupRead(fault, false, null);
                });

            Assert(!result.CallbackInvoked && sideEffects == 0,
                "A claimed cleanup entered a side effect at the exclusive deadline.");
            Assert(fixture.Coordinator.GetFaultLatch().CleanupDirective.Kind == AbortCleanupKind.HaltRestart,
                "Expired executing cleanup did not require restart.");
        }

        private static void CleanupPreEntryAbClaimsAreUnavailable()
        {
            Fixture fixture = CreateSelectorFaultFixture();
            FaultLatchSnapshot fault = fixture.Coordinator.GetFaultLatch();
            CleanupIssueResult issue = fixture.Coordinator.IssueCleanup(
                fault.FaultId, fault.CleanupDirective.DirectiveId);
            CleanupClaimResult claim = fixture.Coordinator.ClaimCleanupExecution(issue.Intent);
            Assert(claim.Claim.ActivePermit == null && !claim.Claim.SideEffectEntered,
                "Cleanup claim exposed read tickets before actual side-effect entry.");
            AssertThrows<ArgumentException>(delegate
            {
                new CleanupSideEffectPermit(
                    new CoordinatorSideEffectAuthority(),
                    claim.Claim,
                    NativeCleanupMethod.CancelSelector);
            });

            int readCount = 0;
            CleanupInvocationResult result = fixture.Coordinator.InvokeClaimedCleanupForTesting(
                claim.Claim,
                NativeCleanupMethod.CancelSelector,
                delegate(CleanupSideEffectPermit permit)
                {
                    Assert(permit.FirstReadTicket.State == OneShotExecutionState.Issued
                        && permit.SecondReadTicket.State == OneShotExecutionState.Issued,
                        "Entry did not create fresh unread A/B tickets.");
                },
                delegate(CleanupPostReadPermit readPermit)
                {
                    Interlocked.Increment(ref readCount);
                    return CreateCleanupRead(fault, false, null);
                });
            Assert(result.ReceiptAccepted && readCount == 2,
                "Post-entry coordinator reads did not complete as an exact A/B pair.");
        }

        private static void CleanupAbReadsStartAfterCallbackReturn()
        {
            Fixture fixture = CreateSelectorFaultFixture();
            FaultLatchSnapshot fault = fixture.Coordinator.GetFaultLatch();
            CleanupIssueResult issue = fixture.Coordinator.IssueCleanup(
                fault.FaultId, fault.CleanupDirective.DirectiveId);
            CleanupClaimResult claim = fixture.Coordinator.ClaimCleanupExecution(issue.Intent);
            int sideEffectState = 0;
            int observedOrdinal = 0;
            bool unauthorizedPreActionReadClaimed = false;
            CleanupInvocationResult result = fixture.Coordinator.InvokeClaimedCleanupForTesting(
                claim.Claim,
                NativeCleanupMethod.CancelSelector,
                delegate(CleanupSideEffectPermit permit)
                {
                    CleanupPostReadClaim unauthorizedClaim;
                    unauthorizedPreActionReadClaimed = permit.FirstReadTicket.TryClaim(
                        new CoordinatorSideEffectAuthority(), out unauthorizedClaim);
                    Interlocked.Exchange(ref sideEffectState, 1);
                },
                delegate(CleanupPostReadPermit readPermit)
                {
                    Assert(Volatile.Read(ref sideEffectState) == 1,
                        "Coordinator sampled cleanup post-state before the action callback returned.");
                    int expectedOrdinal = Interlocked.Increment(ref observedOrdinal);
                    Assert(readPermit.Ordinal == expectedOrdinal,
                        "Cleanup post reads were not independently ordered A then B.");
                    return CreateCleanupRead(fault, false, null);
                });

            Assert(!unauthorizedPreActionReadClaimed,
                "Action callback claimed post-read A before returning to the coordinator.");
            Assert(result.ReceiptAccepted && observedOrdinal == 2,
                "Coordinator did not own both post-action A/B reads.");
        }

        private static void FakeTransportRemainsInternalNonLive()
        {
            InMemoryFakeTransportEndpoint first;
            InMemoryFakeTransportEndpoint second;
            InMemoryFakeTransportEndpoint.CreateDuplex(1, out first, out second);
            Fixture fixture = new Fixture(RegisteredDomesticCommand.Commerce);
            Assert(first.TrySend(FakeProtocolEnvelope.ForRequest(fixture.Request)), "Fake send failed.");
            Assert(!first.TrySend(FakeProtocolEnvelope.ForRequest(fixture.Request)), "Fake queue overflowed.");
            FakeProtocolEnvelope envelope;
            Assert(second.TryReceive(out envelope) && envelope.Kind == FakeEnvelopeKind.Request, "Fake receive failed.");
            Assert(!first.IsLiveConnection && !first.CanAuthorizeNativeMutation, "Fake claims live authority.");
        }

        private static void AuthorizationIsPermanentlyFalse()
        {
            Fixture fixture = new Fixture(RegisteredDomesticCommand.Commerce);
            fixture.Start();
            TransactionTransition snapshot = fixture.SubmitBegin();
            Assert(!V8SafetyBoundary.LiveBusinessAuthorizationCompiled, "Live authorization constant changed.");
            Assert(!fixture.Request.AuthorizesLiveMutation, "Request authorizes live mutation.");
            Assert(!snapshot.CommitAuthorized && !snapshot.RetryConfirmationAllowed,
                "Transition authorizes commit or retry.");
        }

        private static void VerifySkip(
            BusinessSkipReason expected,
            bool direct,
            bool canExecute,
            int candidates,
            int money,
            int reserve)
        {
            Fixture fixture = new Fixture(RegisteredDomesticCommand.Commerce, reserve);
            fixture.CandidateCount = candidates;
            fixture.Money = money;
            fixture.Start();
            TransactionTransition result = fixture.SubmitBegin(null, direct, canExecute);
            Assert(result.Outcome == TransactionOutcome.SkipBeforeMutation, "Outcome is not SkipBeforeMutation.");
            Assert(result.SkipReason == expected && !result.MutationStarted, "Wrong or mutated skip.");
            ActionIssueResult action = fixture.Coordinator.IssueNextAction(fixture.Handle);
            Assert(!action.Issued, "A skipped transaction issued an action.");
        }

        private static void VerifyOuterMismatch(
            int? nativeId,
            NativeOuterTaskType? type,
            uint? vptr)
        {
            Fixture fixture = new Fixture(RegisteredDomesticCommand.Commerce);
            fixture.Start();
            fixture.SubmitBegin();
            fixture.SubmitNormalBind();
            ActionIssueResult issue = fixture.Coordinator.IssueNextAction(fixture.Handle);
            OpenOuterEvidence evidence = fixture.CreateOpenOuterEvidence(issue.Intent, nativeId, type, vptr);
            TransactionTransition result = fixture.SubmitReceipt(issue, evidence);
            AssertAbort(result, AbortUncertainReason.CommandDescriptorMismatch);
        }

        private static void VerifyPostFailure(Action<PostVariant> mutate, AbortUncertainReason expected)
        {
            Fixture fixture = new Fixture(RegisteredDomesticCommand.Commerce);
            fixture.Start();
            fixture.DriveThroughAcceptOuter();
            PostVariant value = fixture.DefaultPostVariant();
            mutate(value);
            TransactionTransition result = fixture.SubmitPost(value, value.Clone());
            AssertAbort(result, expected);
        }

        private static Fixture CreateSelectorFaultFixture()
        {
            Fixture fixture = new Fixture(RegisteredDomesticCommand.Commerce);
            fixture.Start();
            fixture.DriveToSelector();
            ActionIssueResult clear = fixture.Coordinator.IssueNextAction(fixture.Handle);
            ObjectGenerationToken dead = new ObjectGenerationToken(
                ObjectKind.InnerSelector,
                fixture.Selector.Identity,
                fixture.Selector.Generation,
                false);
            TransactionTransition abort = fixture.SubmitReceipt(
                clear, fixture.CreateClearEvidence(clear.Intent, dead));
            AssertAbort(abort, AbortUncertainReason.ObjectMissingDestroyedOrReused);
            return fixture;
        }

        private static Fixture CreateCleanupRouteFixture(
            AbortCleanupKind kind,
            out FaultLatchSnapshot fault)
        {
            Fixture fixture;
            switch (kind)
            {
                case AbortCleanupKind.ConditionalRestoreTarget:
                    fixture = new Fixture(RegisteredDomesticCommand.Commerce);
                    fixture.Start();
                    Assert(fixture.SubmitBegin().Accepted, "Cleanup route setup could not begin.");
                    fixture.SubmitNormalBind();
                    ActionIssueResult pending = fixture.Coordinator.IssueNextAction(fixture.Handle);
                    Assert(pending.Issued && pending.Intent.Stage == TransactionStage.OpenOuter,
                        "Cleanup route setup did not leave a bound target.");
                    fixture.Clock.Now = fixture.Deadline;
                    Assert(fixture.Coordinator.PollExpiry(fixture.Handle) != null,
                        "Cleanup route setup did not expire after binding the target.");
                    break;
                case AbortCleanupKind.CancelSelector:
                    fixture = CreateSelectorFaultFixture();
                    break;
                case AbortCleanupKind.CancelOuter:
                    fixture = new Fixture(RegisteredDomesticCommand.Commerce);
                    fixture.Start();
                    fixture.DriveThroughAcceptInner();
                    AssertAbort(
                        fixture.Coordinator.SubmitObservation(
                            fixture.Handle,
                            fixture.Factory.Seal(fixture.CreateVerifyWorkingEvidence(
                                new[] { 10, 11, 12, 13, 99 }))),
                        AbortUncertainReason.WorkingListMismatch);
                    break;
                case AbortCleanupKind.DoNotRollbackAfterAccept:
                    fixture = new Fixture(RegisteredDomesticCommand.Commerce);
                    fixture.Start();
                    fixture.DriveThroughAcceptOuter();
                    PostVariant partial = fixture.DefaultPostVariant();
                    partial.BusyOfficerIds = new[] { 10, 11, 12, 13 };
                    AssertAbort(
                        fixture.SubmitPost(partial, partial.Clone()),
                        AbortUncertainReason.PartialCommitObserved);
                    break;
                default:
                    throw new ArgumentOutOfRangeException("kind");
            }

            fault = fixture.Coordinator.GetFaultLatch();
            Assert(fault.IsLatched && fault.CleanupDirective != null
                && fault.CleanupDirective.Kind == kind,
                "Cleanup route setup produced "
                    + (fault.CleanupDirective == null
                        ? "no directive"
                        : fault.CleanupDirective.Kind.ToString())
                    + " instead of " + kind + ".");
            return fixture;
        }

        private static CleanupPostStateRead CreateCleanupRead(
            FaultLatchSnapshot fault,
            bool postAcceptReview,
            ObjectGenerationToken targetAfterOverride)
        {
            AbortCleanupDirective directive = fault.CleanupDirective;
            ulong generation = postAcceptReview
                ? directive.GenerationNumber + 1
                : directive.GenerationNumber;
            FixedDigest generationDigest = postAcceptReview
                ? Digest(0xE2)
                : directive.GenerationDigest;
            return new CleanupPostStateRead(
                directive.ProcessId,
                directive.ProcessCreationUtcTicks,
                generation,
                generationDigest,
                directive.ContextDigest,
                fault.FaultId,
                directive.DirectiveId,
                directive.RequestId,
                directive.RequestFingerprint,
                directive.RootAtAbort,
                directive.TargetAtAbort,
                directive.OuterAtAbort,
                directive.SelectorAtAbort,
                postAcceptReview ? ObjectGenerationToken.None : directive.RootAtAbort,
                targetAfterOverride ?? ObjectGenerationToken.None,
                ObjectGenerationToken.None,
                ObjectGenerationToken.None,
                postAcceptReview,
                true);
        }

        private static AuthenticatedCleanupReceipt CreateCleanupReceipt(
            Fixture fixture,
            CleanupExecutionClaim cleanupClaim,
            FaultLatchSnapshot fault,
            bool postAcceptReview,
            ObjectGenerationToken targetAfterOverride,
            string diagnostic)
        {
            CleanupSideEffectPermit permit = fixture.Coordinator
                .AuthorizeCleanupSideEffectForReceiptTesting(cleanupClaim);
            Assert(permit != null && ReferenceEquals(permit.Claim, cleanupClaim),
                "Cleanup receipt test did not receive the exact entered permit.");
            return CreateCleanupReceipt(
                fixture,
                permit,
                fault,
                postAcceptReview,
                targetAfterOverride,
                diagnostic);
        }

        private static AuthenticatedCleanupReceipt CreateCleanupReceipt(
            Fixture fixture,
            CleanupSideEffectPermit permit,
            FaultLatchSnapshot fault,
            bool postAcceptReview,
            ObjectGenerationToken targetAfterOverride,
            string diagnostic)
        {
            CleanupExecutionClaim cleanupClaim = permit == null ? null : permit.Claim;
            Assert(cleanupClaim != null && cleanupClaim.SideEffectEntered,
                "Cleanup callback did not receive an entered side-effect permit.");
            CleanupPostReadClaim firstClaim = fixture.Coordinator.ClaimCleanupPostReadForTesting(permit, 1);
            AuthenticatedCleanupPostReadReceipt firstReceipt = fixture.Coordinator.CompleteCleanupPostReadForTesting(
                firstClaim,
                CreateCleanupRead(fault, postAcceptReview, targetAfterOverride));
            CleanupPostReadClaim secondClaim = fixture.Coordinator.ClaimCleanupPostReadForTesting(permit, 2);
            AuthenticatedCleanupPostReadReceipt secondReceipt = fixture.Coordinator.CompleteCleanupPostReadForTesting(
                secondClaim,
                CreateCleanupRead(fault, postAcceptReview, targetAfterOverride));
            return fixture.Coordinator.CompleteCleanupForTesting(
                permit,
                firstReceipt,
                secondReceipt,
                diagnostic);
        }

        private static void AssertDescriptor(
            RegisteredDomesticCommand command,
            string key,
            int nativeId,
            NativeOuterTaskType type,
            string typeKey,
            uint vptr,
            NativeMoneyModel moneyModel,
            int cost)
        {
            NativeCommandDescriptor descriptor = NativeCommandDescriptorRegistry.GetRequired(command);
            Assert(descriptor.Command == command
                && descriptor.Key == key
                && descriptor.NativeCommandId == nativeId
                && descriptor.OuterTaskType == type
                && descriptor.OuterTaskTypeKey == typeKey
                && descriptor.OuterTaskVptr == vptr
                && descriptor.MoneyModel == moneyModel
                && descriptor.ExpectedCost == cost
                && descriptor.RequiredOfficerCount == 5,
                "Descriptor mismatch for " + command + ".");
        }

        private static void AssertAbort(TransactionTransition transition, AbortUncertainReason expected)
        {
            Assert(transition != null, "Abort transition is null.");
            Assert(transition.Outcome == TransactionOutcome.AbortUncertain, "Outcome is not AbortUncertain.");
            Assert(transition.AbortReason == expected,
                "Expected " + expected + ", got " + transition.AbortReason + ": " + transition.Detail);
            Assert(transition.CleanupDirective != null, "Abort lacks cleanup directive.");
            Assert(!transition.RetryConfirmationAllowed && !transition.CommitAuthorized,
                "Abort allows confirmation retry or commit.");
        }

        private static void AssertThrows<T>(Action action)
            where T : Exception
        {
            bool threw = false;
            try
            {
                action();
            }
            catch (T)
            {
                threw = true;
            }

            Assert(threw, "Expected " + typeof(T).Name + ".");
        }

        private static void Run(string name, Action action)
        {
            action();
            _passed++;
            Console.WriteLine("PASS: " + name);
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        private static SingleCommandRequest CreateRequest(
            RegisteredDomesticCommand command,
            ulong sequence,
            FixedDigest generation,
            long created,
            long deadline)
        {
            return new SingleCommandRequest(
                Guid.NewGuid(), sequence, command, 7, 3, new[] { 10, 11, 12, 13, 14 }, 0,
                Digest(0x20), generation, checked((int)(deadline - created)));
        }

        private static ObjectGenerationToken Token(ObjectKind kind, ulong identity)
        {
            return new ObjectGenerationToken(kind, identity, 1, true);
        }

        private static FixedDigest Digest(byte marker)
        {
            byte[] bytes = new byte[FixedDigest.ByteCount];
            for (int index = 0; index < bytes.Length; index++)
            {
                bytes[index] = unchecked((byte)(marker + index));
            }

            return FixedDigest.Create(bytes);
        }

        private sealed class ManualClock : ITrustedMonotonicClock
        {
            internal long Now;
            internal bool ThrowOnRead;
            internal Action OnRead;
            internal Exception CallbackException;

            public long Frequency
            {
                get { return 1000; }
            }

            public long GetTimestamp()
            {
                Action callback = OnRead;
                OnRead = null;
                if (callback != null)
                {
                    try
                    {
                        callback();
                    }
                    catch (Exception exception)
                    {
                        CallbackException = exception;
                    }
                }

                if (ThrowOnRead)
                {
                    throw new InvalidOperationException("synthetic trusted clock failure");
                }

                return Now;
            }
        }

        private sealed class PostVariant
        {
            internal int ProcessId;
            internal long CreationTicks;
            internal ulong GenerationNumber;
            internal FixedDigest GenerationDigest;
            internal FixedDigest ContextDigest;
            internal int CityId;
            internal int CorpsId;
            internal RegisteredDomesticCommand Command;
            internal int NativeCommandId;
            internal NativeOuterTaskType OuterTaskType;
            internal uint OuterTaskVptr;
            internal int[] OrderedOfficerIds;
            internal int[] BusyOfficerIds;
            internal bool CommandStateVerified;
            internal bool NativeOrderVerified;
            internal int MoneyBefore;
            internal int MoneyAfter;

            internal PostVariant Clone()
            {
                return new PostVariant
                {
                    ProcessId = ProcessId,
                    CreationTicks = CreationTicks,
                    GenerationNumber = GenerationNumber,
                    GenerationDigest = GenerationDigest,
                    ContextDigest = ContextDigest,
                    CityId = CityId,
                    CorpsId = CorpsId,
                    Command = Command,
                    NativeCommandId = NativeCommandId,
                    OuterTaskType = OuterTaskType,
                    OuterTaskVptr = OuterTaskVptr,
                    OrderedOfficerIds = (int[])OrderedOfficerIds.Clone(),
                    BusyOfficerIds = (int[])BusyOfficerIds.Clone(),
                    CommandStateVerified = CommandStateVerified,
                    NativeOrderVerified = NativeOrderVerified,
                    MoneyBefore = MoneyBefore,
                    MoneyAfter = MoneyAfter
                };
            }
        }

        private sealed class NativeActionCallbackRecorder : INativeSingleCommandCallbacks
        {
            private readonly Fixture _fixture;
            private readonly OneShotActionIntent _intent;

            internal NativeActionCallbackRecorder(Fixture fixture, OneShotActionIntent intent)
            {
                _fixture = fixture;
                _intent = intent;
            }

            internal int SideEffectCalls { get; private set; }
            internal NativeActionMethod RecordedMethod { get; private set; }

            public void Begin(SingleCommandRequest request)
            {
                throw new InvalidOperationException("Action-stage invoker unexpectedly called Begin.");
            }

            public SyntheticStageEvidence BindTargetCandidate(
                SingleCommandRequest request,
                ActionSideEffectPermit permit)
            {
                Record(NativeActionMethod.BindTargetCandidate, request, permit);
                return _fixture.CreateBindEvidence(_intent, null, null, true);
            }

            public SyntheticStageEvidence OpenOuter(
                SingleCommandRequest request,
                ActionSideEffectPermit permit)
            {
                Record(NativeActionMethod.OpenOuter, request, permit);
                return _fixture.CreateOpenOuterEvidence(_intent, null, null, null);
            }

            public SyntheticStageEvidence OpenSelector(
                SingleCommandRequest request,
                ActionSideEffectPermit permit)
            {
                Record(NativeActionMethod.OpenSelector, request, permit);
                return _fixture.CreateOpenSelectorEvidence(_intent, _fixture.CandidateCount);
            }

            public SyntheticStageEvidence Clear(
                SingleCommandRequest request,
                ActionSideEffectPermit permit)
            {
                Record(NativeActionMethod.Clear, request, permit);
                return _fixture.CreateClearEvidence(_intent, null);
            }

            public SyntheticStageEvidence NativeFillMax(
                SingleCommandRequest request,
                ActionSideEffectPermit permit)
            {
                Record(NativeActionMethod.NativeFillMax, request, permit);
                return _fixture.CreateFillEvidence(_intent);
            }

            public SyntheticStageEvidence AcceptInner(
                SingleCommandRequest request,
                ActionSideEffectPermit permit)
            {
                Record(NativeActionMethod.AcceptInner, request, permit);
                return _fixture.CreateAcceptInnerEvidence(_intent);
            }

            public SyntheticStageEvidence AcceptOuter(
                SingleCommandRequest request,
                ActionSideEffectPermit permit)
            {
                Record(NativeActionMethod.AcceptOuter, request, permit);
                return _fixture.CreateAcceptOuterEvidence(_intent);
            }

            public void VerifyCommitted(SingleCommandRequest request)
            {
                throw new InvalidOperationException(
                    "Action-stage invoker unexpectedly called VerifyCommitted.");
            }

            private void Record(
                NativeActionMethod method,
                SingleCommandRequest request,
                ActionSideEffectPermit permit)
            {
                SideEffectCalls++;
                Assert(SideEffectCalls == 1, "Action recorder received more than one side effect.");
                Assert(request != null
                    && request.RequestId == _fixture.Request.RequestId
                    && permit != null
                    && permit.Claim != null
                    && ReferenceEquals(permit.Claim.Intent, _intent),
                    "Action recorder received a foreign request or permit.");
                RecordedMethod = method;
            }
        }

        private sealed class NativeCleanupCallbackRecorder : INativeAbortCleanupCallback
        {
            private readonly FaultLatchSnapshot _fault;
            private readonly bool _postAcceptReview;

            internal NativeCleanupCallbackRecorder(
                FaultLatchSnapshot fault,
                bool postAcceptReview)
            {
                _fault = fault;
                _postAcceptReview = postAcceptReview;
            }

            internal int SideEffectCalls { get; private set; }
            internal int PostReadCalls { get; private set; }
            internal NativeCleanupMethod RecordedMethod { get; private set; }

            public void ConditionalRestoreTarget(CleanupSideEffectPermit permit)
            {
                Record(NativeCleanupMethod.ConditionalRestoreTarget, permit);
            }

            public void CancelSelector(CleanupSideEffectPermit permit)
            {
                Record(NativeCleanupMethod.CancelSelector, permit);
            }

            public void CancelOuter(CleanupSideEffectPermit permit)
            {
                Record(NativeCleanupMethod.CancelOuter, permit);
            }

            public void ReviewAfterAccept(CleanupSideEffectPermit permit)
            {
                Record(NativeCleanupMethod.ReviewAfterAccept, permit);
            }

            public CleanupPostStateRead ReadPostState(CleanupPostReadPermit permit)
            {
                PostReadCalls++;
                Assert(SideEffectCalls == 1,
                    "Cleanup recorder sampled state before the cleanup side effect returned.");
                Assert(permit != null && permit.CleanupPermit != null
                    && permit.CleanupPermit.Claim.Intent.FaultId == _fault.FaultId,
                    "Cleanup recorder received a foreign post-read permit.");
                return CreateCleanupRead(_fault, _postAcceptReview, null);
            }

            private void Record(NativeCleanupMethod method, CleanupSideEffectPermit permit)
            {
                SideEffectCalls++;
                Assert(SideEffectCalls == 1, "Cleanup recorder received more than one side effect.");
                Assert(permit != null
                    && permit.Claim != null
                    && permit.Claim.Intent.FaultId == _fault.FaultId,
                    "Cleanup recorder received a foreign side-effect permit.");
                RecordedMethod = method;
            }
        }

        private sealed class Fixture
        {
            private static int _identityCounter;
            private const long DiagnosticBaseTicks = 638000000000000000L;

            internal Fixture(RegisteredDomesticCommand command)
                : this(command, 0)
            {
            }

            internal Fixture(RegisteredDomesticCommand command, int reserveMoney)
            {
                int identity = Interlocked.Increment(ref _identityCounter);
                Clock = new ManualClock { Now = 100 };
                Key = new ProcessGenerationKey(20000 + identity, DiagnosticBaseTicks + identity, 1, Digest(0x40));
                Coordinator = ProcessGenerationCoordinator.AttachForTesting(Key, Clock);
                Factory = Coordinator.ObservationFactory;
                Request = new SingleCommandRequest(
                    Guid.NewGuid(), 1, command, 7, 3, new[] { 10, 11, 12, 13, 14 }, reserveMoney,
                    Digest(0x20), Key.GenerationDigest, 900);
                CandidateCount = 10;
                Money = 10000;
                Root = Token(ObjectKind.RootController, 0x1000);
                Target = Token(ObjectKind.CityTarget, 0x2000);
                Outer = Token(ObjectKind.OuterTask, 0x3000);
                Selector = Token(ObjectKind.InnerSelector, 0x4000);
                SourceDigest = Digest(0x60);
            }

            internal ManualClock Clock;
            internal ProcessGenerationKey Key;
            internal ProcessGenerationCoordinator Coordinator;
            internal TrustedObservationFactory Factory;
            internal SingleCommandRequest Request;
            internal TransactionHandle Handle;
            internal int CandidateCount;
            internal int Money;
            internal ObjectGenerationToken Root;
            internal ObjectGenerationToken Target;
            internal ObjectGenerationToken Outer;
            internal ObjectGenerationToken Selector;
            internal FixedDigest SourceDigest;
            internal long Deadline;

            internal ProcessGenerationCoordinator AttachSibling()
            {
                return ProcessGenerationCoordinator.AttachForTesting(Key, Clock);
            }

            internal void Start()
            {
                CoordinatorStartResult result = Coordinator.TryStart(Request);
                Assert(result.Accepted, "Start failed: " + result.Rejection + " " + result.Detail);
                Handle = result.Handle;
                Deadline = Clock.Now + Request.TimeToLiveMilliseconds;
            }

            internal SingleCommandRequest NewRequest(ulong sequence, FixedDigest generation)
            {
                return new SingleCommandRequest(
                    Guid.NewGuid(), sequence, Request.Descriptor.Command, Request.CityId, Request.CorpsId,
                    Request.OfficerIds, Request.ReserveMoney, Request.ContextDigest, generation,
                    1000);
            }

            internal BeginEvidence CreateBeginEvidence(long? diagnosticTicks, FixedDigest contextOverride)
            {
                return new BeginEvidence(
                    Request.RequestId, Request.RequestFingerprint, Request.Sequence, 1,
                    diagnosticTicks ?? Diagnostic(1), contextOverride ?? Request.ContextDigest,
                    Request.GenerationDigest, Request.CityId, Request.CorpsId,
                    true, true, CandidateCount, Money);
            }

            internal TransactionTransition SubmitBegin()
            {
                return SubmitBegin(null, true, true);
            }

            internal TransactionTransition SubmitBegin(long diagnosticTicks)
            {
                return SubmitBegin(diagnosticTicks, true, true);
            }

            internal TransactionTransition SubmitBegin(long? diagnosticTicks, bool direct, bool canExecute)
            {
                BeginEvidence evidence = new BeginEvidence(
                    Request.RequestId, Request.RequestFingerprint, Request.Sequence, 1,
                    diagnosticTicks ?? Diagnostic(1), Request.ContextDigest, Request.GenerationDigest,
                    Request.CityId, Request.CorpsId, direct, canExecute, CandidateCount, Money);
                return Coordinator.SubmitObservation(Handle, Factory.Seal(evidence));
            }

            internal TransactionTransition SubmitUntrustedBegin()
            {
                return Coordinator.SubmitObservation(Handle, CreateBeginEvidence(null, null));
            }

            internal BindTargetCandidateEvidence CreateBindEvidence(
                OneShotActionIntent intent,
                int? cityOverride,
                long? diagnosticTicks,
                bool actionApplied)
            {
                return new BindTargetCandidateEvidence(
                    Request.RequestId, Request.RequestFingerprint, Request.Sequence, intent.StageOrdinal,
                    diagnosticTicks ?? Diagnostic(2), Request.ContextDigest, Request.GenerationDigest,
                    cityOverride ?? Request.CityId, Request.CorpsId, Root, Target,
                    true, true, true, actionApplied, Money);
            }

            internal OpenOuterEvidence CreateOpenOuterEvidence(
                OneShotActionIntent intent,
                int? nativeId,
                NativeOuterTaskType? type,
                uint? vptr)
            {
                NativeCommandDescriptor descriptor = Request.Descriptor;
                return new OpenOuterEvidence(
                    Request.RequestId, Request.RequestFingerprint, Request.Sequence, intent.StageOrdinal,
                    Diagnostic(3), Request.ContextDigest, Request.GenerationDigest,
                    Request.CityId, Request.CorpsId, Root, Target, Outer,
                    nativeId ?? descriptor.NativeCommandId,
                    type ?? descriptor.OuterTaskType,
                    vptr ?? descriptor.OuterTaskVptr,
                    true,
                    Money);
            }

            internal OpenSelectorEvidence CreateOpenSelectorEvidence(
                OneShotActionIntent intent,
                int sourceCount)
            {
                return new OpenSelectorEvidence(
                    Request.RequestId, Request.RequestFingerprint, Request.Sequence, intent.StageOrdinal,
                    Diagnostic(4), Request.ContextDigest, Request.GenerationDigest,
                    Request.CityId, Request.CorpsId, Root, Target, Outer, Selector,
                    SourceDigest, sourceCount, Request.OfficerIds, true, Money);
            }

            internal ClearEvidence CreateClearEvidence(
                OneShotActionIntent intent,
                ObjectGenerationToken selector)
            {
                return new ClearEvidence(
                    Request.RequestId, Request.RequestFingerprint, Request.Sequence, intent.StageOrdinal,
                    Diagnostic(5), Request.ContextDigest, Request.GenerationDigest,
                    Request.CityId, Request.CorpsId, Root, Target, Outer, selector ?? Selector,
                    SourceDigest, new int[0], true, Money);
            }

            internal NativeFillMaxEvidence CreateFillEvidence(OneShotActionIntent intent)
            {
                return new NativeFillMaxEvidence(
                    Request.RequestId, Request.RequestFingerprint, Request.Sequence, intent.StageOrdinal,
                    Diagnostic(6), Request.ContextDigest, Request.GenerationDigest,
                    Request.CityId, Request.CorpsId, Root, Target, Outer, Selector,
                    SourceDigest, true, Money);
            }

            internal VerifyExactlyExpectedFiveEvidence CreateVerifyFiveEvidence(IEnumerable<int> ids)
            {
                return new VerifyExactlyExpectedFiveEvidence(
                    Request.RequestId, Request.RequestFingerprint, Request.Sequence, 7,
                    Diagnostic(7), Request.ContextDigest, Request.GenerationDigest,
                    Request.CityId, Request.CorpsId, Root, Target, Outer, Selector,
                    SourceDigest, ids, Money);
            }

            internal AcceptInnerEvidence CreateAcceptInnerEvidence(OneShotActionIntent intent)
            {
                return new AcceptInnerEvidence(
                    Request.RequestId, Request.RequestFingerprint, Request.Sequence, intent.StageOrdinal,
                    Diagnostic(8), Request.ContextDigest, Request.GenerationDigest,
                    Request.CityId, Request.CorpsId, Root, Target, Outer, Selector,
                    SourceDigest, Request.OfficerIds, true, Money);
            }

            internal VerifyWorkingEvidence CreateVerifyWorkingEvidence(IEnumerable<int> ids)
            {
                return new VerifyWorkingEvidence(
                    Request.RequestId, Request.RequestFingerprint, Request.Sequence, 9,
                    Diagnostic(9), Request.ContextDigest, Request.GenerationDigest,
                    Request.CityId, Request.CorpsId, Root, Target, Outer, SourceDigest, ids, Money);
            }

            internal AcceptOuterEvidence CreateAcceptOuterEvidence(OneShotActionIntent intent)
            {
                return new AcceptOuterEvidence(
                    Request.RequestId, Request.RequestFingerprint, Request.Sequence, intent.StageOrdinal,
                    Diagnostic(10), Request.ContextDigest, Request.GenerationDigest,
                    Request.CityId, Request.CorpsId, Root, Target, Outer,
                    SourceDigest, Request.OfficerIds, true, Money);
            }

            internal VerifyCommittedEvidence CreateVerifyCommittedEvidence(
                AuthenticatedStablePostSnapshot snapshot)
            {
                return new VerifyCommittedEvidence(
                    Request.RequestId, Request.RequestFingerprint, Request.Sequence, 11,
                    Diagnostic(11), Request.ContextDigest, Request.GenerationDigest,
                    Request.CityId, Request.CorpsId, snapshot);
            }

            internal TransactionTransition SubmitReceipt(
                ActionIssueResult issue,
                SyntheticStageEvidence evidence)
            {
                Assert(issue.Issued, "No action intent to receive.");
                ActionClaimResult claimed = Coordinator.ClaimActionExecution(Handle, issue.Intent);
                Assert(claimed.Claimed, "Action execution claim failed: " + claimed.Detail);
                ActionInvocationResult invocation = Coordinator.InvokeClaimedActionForTesting(
                    Handle,
                    claimed.Claim,
                    ActionStages.GetRequiredMethod(issue.Intent.Stage),
                    delegate(ActionSideEffectPermit permit)
                    {
                        return evidence;
                    });
                return invocation.Transition;
            }

            internal void SubmitNormalBind()
            {
                ActionIssueResult issue = Coordinator.IssueNextAction(Handle);
                MustAccept(SubmitReceipt(issue, CreateBindEvidence(issue.Intent, null, null, true)));
            }

            internal void SubmitNormalOpenOuter()
            {
                ActionIssueResult issue = Coordinator.IssueNextAction(Handle);
                MustAccept(SubmitReceipt(issue, CreateOpenOuterEvidence(issue.Intent, null, null, null)));
            }

            internal void DriveToSelector()
            {
                MustAccept(SubmitBegin());
                SubmitNormalBind();
                SubmitNormalOpenOuter();
                ActionIssueResult issue = Coordinator.IssueNextAction(Handle);
                MustAccept(SubmitReceipt(issue, CreateOpenSelectorEvidence(issue.Intent, CandidateCount)));
            }

            internal void DriveThroughFill()
            {
                DriveToSelector();
                ActionIssueResult clear = Coordinator.IssueNextAction(Handle);
                MustAccept(SubmitReceipt(clear, CreateClearEvidence(clear.Intent, null)));
                ActionIssueResult fill = Coordinator.IssueNextAction(Handle);
                MustAccept(SubmitReceipt(fill, CreateFillEvidence(fill.Intent)));
            }

            internal TransactionTransition SubmitVerifyFive(IEnumerable<int> ids)
            {
                return Coordinator.SubmitObservation(Handle, Factory.Seal(CreateVerifyFiveEvidence(ids)));
            }

            internal void DriveThroughAcceptInner()
            {
                DriveThroughFill();
                MustAccept(SubmitVerifyFive(Request.OfficerIds));
                ActionIssueResult accept = Coordinator.IssueNextAction(Handle);
                MustAccept(SubmitReceipt(accept, CreateAcceptInnerEvidence(accept.Intent)));
            }

            internal void DriveThroughAcceptOuter()
            {
                DriveThroughAcceptInner();
                MustAccept(Coordinator.SubmitObservation(
                    Handle, Factory.Seal(CreateVerifyWorkingEvidence(Request.OfficerIds))));
                ActionIssueResult accept = Coordinator.IssueNextAction(Handle);
                MustAccept(SubmitReceipt(accept, CreateAcceptOuterEvidence(accept.Intent)));
            }

            internal TransactionTransition DriveSuccess()
            {
                DriveThroughAcceptOuter();
                PostVariant value = DefaultPostVariant();
                return SubmitPost(value, value.Clone());
            }

            internal PostVariant DefaultPostVariant()
            {
                NativeCommandDescriptor descriptor = Request.Descriptor;
                return new PostVariant
                {
                    ProcessId = Key.ProcessId,
                    CreationTicks = Key.ProcessCreationUtcTicks,
                    GenerationNumber = Key.GenerationNumber + 1,
                    GenerationDigest = Digest(0x80),
                    ContextDigest = Request.ContextDigest,
                    CityId = Request.CityId,
                    CorpsId = Request.CorpsId,
                    Command = descriptor.Command,
                    NativeCommandId = descriptor.NativeCommandId,
                    OuterTaskType = descriptor.OuterTaskType,
                    OuterTaskVptr = descriptor.OuterTaskVptr,
                    OrderedOfficerIds = Request.OfficerIds.ToArray(),
                    BusyOfficerIds = Request.OfficerIds.ToArray(),
                    CommandStateVerified = true,
                    NativeOrderVerified = true,
                    MoneyBefore = Money,
                    MoneyAfter = Money - descriptor.ExpectedCost
                };
            }

            internal PostStateRead CreatePostRead(PostVariant value)
            {
                return new PostStateRead(
                    value.ProcessId, value.CreationTicks, value.GenerationNumber,
                    value.GenerationDigest, value.ContextDigest,
                    Request.RequestId, Request.Sequence, Request.RequestFingerprint,
                    value.CityId, value.CorpsId,
                    value.Command, value.NativeCommandId, value.OuterTaskType, value.OuterTaskVptr,
                    value.OrderedOfficerIds, value.BusyOfficerIds, value.CommandStateVerified,
                    value.NativeOrderVerified, value.MoneyBefore, value.MoneyAfter);
            }

            internal TransactionTransition SubmitPost(PostVariant first, PostVariant second)
            {
                AuthenticatedStablePostSnapshot snapshot = CreatePostSnapshotOnly(first, second);
                VerifyCommittedEvidence evidence = Factory.Seal(CreateVerifyCommittedEvidence(snapshot));
                return Coordinator.SubmitObservation(Handle, evidence);
            }

            internal AuthenticatedStablePostSnapshot CreatePostSnapshotOnly(
                PostVariant first,
                PostVariant second)
            {
                PostReadPairIssueResult pair = Coordinator.IssuePostReadPair(Handle);
                Assert(pair.Issued, "Post A/B ticket issue failed: " + pair.Detail);
                PostReadClaimResult firstClaim = Coordinator.ClaimPostRead(Handle, pair.First);
                Assert(firstClaim.Claimed, "Post A ticket claim failed.");
                AuthenticatedPostReadReceipt firstReceipt = Factory.CompletePostRead(
                    firstClaim.Claim, CreatePostRead(first));
                PostReadClaimResult secondClaim = Coordinator.ClaimPostRead(Handle, pair.Second);
                Assert(secondClaim.Claimed, "Post B ticket claim failed.");
                AuthenticatedPostReadReceipt secondReceipt = Factory.CompletePostRead(
                    secondClaim.Claim, CreatePostRead(second));
                return Factory.CreatePostSnapshot(
                    firstReceipt, secondReceipt);
            }

            private static long Diagnostic(int ordinal)
            {
                return DiagnosticBaseTicks + ordinal;
            }

            private static void MustAccept(TransactionTransition transition)
            {
                Assert(transition != null && transition.Accepted,
                    "Transition failed: " + (transition == null ? "null" : transition.AbortReason + " " + transition.Detail));
            }
        }
    }
}
