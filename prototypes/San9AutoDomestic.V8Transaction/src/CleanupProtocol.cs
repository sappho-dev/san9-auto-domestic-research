using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;

namespace San9AutoDomestic.V8Transaction
{
    internal sealed class CoordinatorCleanupClaimAuthority
    {
    }

    internal enum NativeCleanupMethod
    {
        ConditionalRestoreTarget = 1,
        CancelSelector = 2,
        CancelOuter = 3,
        ReviewAfterAccept = 4
    }

    internal static class CleanupMethods
    {
        internal static NativeCleanupMethod GetRequiredMethod(AbortCleanupKind kind)
        {
            switch (kind)
            {
                case AbortCleanupKind.ConditionalRestoreTarget:
                    return NativeCleanupMethod.ConditionalRestoreTarget;
                case AbortCleanupKind.CancelSelector:
                    return NativeCleanupMethod.CancelSelector;
                case AbortCleanupKind.CancelOuter:
                    return NativeCleanupMethod.CancelOuter;
                case AbortCleanupKind.DoNotRollbackAfterAccept:
                    return NativeCleanupMethod.ReviewAfterAccept;
                default:
                    throw new ArgumentOutOfRangeException("kind");
            }
        }
    }

    internal sealed class CleanupPostStateRead
    {
        internal CleanupPostStateRead(
            int processId,
            long processCreationUtcTicks,
            ulong generationNumber,
            FixedDigest generationDigest,
            FixedDigest contextDigest,
            Guid faultId,
            Guid directiveId,
            Guid requestId,
            FixedDigest requestFingerprint,
            ObjectGenerationToken rootBefore,
            ObjectGenerationToken targetBefore,
            ObjectGenerationToken outerBefore,
            ObjectGenerationToken selectorBefore,
            ObjectGenerationToken rootAfter,
            ObjectGenerationToken targetAfter,
            ObjectGenerationToken outerAfter,
            ObjectGenerationToken selectorAfter,
            bool manualReviewCompleted,
            bool noUnexpectedMutation)
        {
            if (processId <= 0
                || processCreationUtcTicks <= 0
                || processCreationUtcTicks > DateTime.MaxValue.Ticks
                || generationNumber == 0)
            {
                throw new ArgumentOutOfRangeException("processId");
            }

            if (generationDigest == null || contextDigest == null || requestFingerprint == null)
            {
                throw new ArgumentNullException(
                    generationDigest == null
                        ? "generationDigest"
                        : (contextDigest == null ? "contextDigest" : "requestFingerprint"));
            }

            if (faultId == Guid.Empty || directiveId == Guid.Empty || requestId == Guid.Empty)
            {
                throw new ArgumentException("Cleanup post-state binding is absent.");
            }

            if (rootBefore == null || targetBefore == null || outerBefore == null || selectorBefore == null
                || rootAfter == null || targetAfter == null || outerAfter == null || selectorAfter == null)
            {
                throw new ArgumentNullException("objectToken");
            }

            ProcessId = processId;
            ProcessCreationUtcTicks = processCreationUtcTicks;
            GenerationNumber = generationNumber;
            GenerationDigest = generationDigest;
            ContextDigest = contextDigest;
            FaultId = faultId;
            DirectiveId = directiveId;
            RequestId = requestId;
            RequestFingerprint = requestFingerprint;
            RootBefore = rootBefore;
            TargetBefore = targetBefore;
            OuterBefore = outerBefore;
            SelectorBefore = selectorBefore;
            RootAfter = rootAfter;
            TargetAfter = targetAfter;
            OuterAfter = outerAfter;
            SelectorAfter = selectorAfter;
            ManualReviewCompleted = manualReviewCompleted;
            NoUnexpectedMutation = noUnexpectedMutation;
            SnapshotDigest = ComputeDigest();
        }

        internal int ProcessId { get; private set; }
        internal long ProcessCreationUtcTicks { get; private set; }
        internal ulong GenerationNumber { get; private set; }
        internal FixedDigest GenerationDigest { get; private set; }
        internal FixedDigest ContextDigest { get; private set; }
        internal Guid FaultId { get; private set; }
        internal Guid DirectiveId { get; private set; }
        internal Guid RequestId { get; private set; }
        internal FixedDigest RequestFingerprint { get; private set; }
        internal ObjectGenerationToken RootBefore { get; private set; }
        internal ObjectGenerationToken TargetBefore { get; private set; }
        internal ObjectGenerationToken OuterBefore { get; private set; }
        internal ObjectGenerationToken SelectorBefore { get; private set; }
        internal ObjectGenerationToken RootAfter { get; private set; }
        internal ObjectGenerationToken TargetAfter { get; private set; }
        internal ObjectGenerationToken OuterAfter { get; private set; }
        internal ObjectGenerationToken SelectorAfter { get; private set; }
        internal bool ManualReviewCompleted { get; private set; }
        internal bool NoUnexpectedMutation { get; private set; }
        internal FixedDigest SnapshotDigest { get; private set; }

        private FixedDigest ComputeDigest()
        {
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write(ProcessId);
                writer.Write(ProcessCreationUtcTicks);
                writer.Write(GenerationNumber);
                writer.Write(GenerationDigest.ToArray());
                writer.Write(ContextDigest.ToArray());
                writer.Write(FaultId.ToByteArray());
                writer.Write(DirectiveId.ToByteArray());
                writer.Write(RequestId.ToByteArray());
                writer.Write(RequestFingerprint.ToArray());
                WriteToken(writer, RootBefore);
                WriteToken(writer, TargetBefore);
                WriteToken(writer, OuterBefore);
                WriteToken(writer, SelectorBefore);
                WriteToken(writer, RootAfter);
                WriteToken(writer, TargetAfter);
                WriteToken(writer, OuterAfter);
                WriteToken(writer, SelectorAfter);
                writer.Write(ManualReviewCompleted);
                writer.Write(NoUnexpectedMutation);
                writer.Flush();
                using (SHA256 algorithm = SHA256.Create())
                {
                    return FixedDigest.Create(algorithm.ComputeHash(stream.ToArray()));
                }
            }
        }

        private static void WriteToken(BinaryWriter writer, ObjectGenerationToken token)
        {
            writer.Write((int)token.Kind);
            writer.Write(token.Identity);
            writer.Write(token.Generation);
            writer.Write(token.IsAlive);
        }
    }

    internal sealed class OneShotCleanupIntent
    {
        private int _state;
        private readonly CoordinatorCleanupClaimAuthority _claimAuthority;
        private CleanupExecutionClaim _activeClaim;

        internal OneShotCleanupIntent(
            SessionObservationCapability capability,
            CoordinatorCleanupClaimAuthority claimAuthority,
            Guid faultId,
            AbortCleanupDirective directive,
            long deadlineMonotonicTicks)
        {
            if (capability == null
                || claimAuthority == null
                || faultId == Guid.Empty
                || directive == null
                || deadlineMonotonicTicks <= 0)
            {
                throw new ArgumentException("Cleanup intent bindings are invalid.");
            }

            Capability = capability;
            _claimAuthority = claimAuthority;
            CoordinatorSessionId = capability.SessionId;
            FaultId = faultId;
            Directive = directive;
            IntentId = Guid.NewGuid();
            Nonce = Guid.NewGuid();
            DeadlineMonotonicTicks = deadlineMonotonicTicks;
            _state = (int)OneShotExecutionState.Issued;
        }

        internal SessionObservationCapability Capability { get; private set; }
        internal Guid CoordinatorSessionId { get; private set; }
        internal Guid FaultId { get; private set; }
        internal AbortCleanupDirective Directive { get; private set; }
        internal Guid IntentId { get; private set; }
        internal Guid Nonce { get; private set; }
        internal long DeadlineMonotonicTicks { get; private set; }
        internal OneShotExecutionState State { get { return (OneShotExecutionState)Volatile.Read(ref _state); } }

        internal bool TryClaim(
            CoordinatorCleanupClaimAuthority claimAuthority,
            CoordinatorSideEffectAuthority sideEffectAuthority,
            out CleanupExecutionClaim claim)
        {
            if (ReferenceEquals(claimAuthority, _claimAuthority)
                && sideEffectAuthority != null
                && Interlocked.CompareExchange(
                ref _state,
                (int)OneShotExecutionState.Executing,
                (int)OneShotExecutionState.Issued) == (int)OneShotExecutionState.Issued)
            {
                claim = new CleanupExecutionClaim(this, sideEffectAuthority);
                Interlocked.CompareExchange(ref _activeClaim, claim, null);
                return true;
            }

            claim = null;
            return false;
        }

        internal bool MatchesActiveClaim(CleanupExecutionClaim claim)
        {
            return claim != null
                && ReferenceEquals(claim.Intent, this)
                && ReferenceEquals(Volatile.Read(ref _activeClaim), claim)
                && claim.ClaimNonce != Guid.Empty;
        }

        internal bool TryProduceReceipt(CleanupExecutionClaim claim)
        {
            return claim != null
                && ReferenceEquals(claim.Intent, this)
                && ReferenceEquals(Volatile.Read(ref _activeClaim), claim)
                && Interlocked.CompareExchange(
                    ref _state,
                    (int)OneShotExecutionState.Receipted,
                    (int)OneShotExecutionState.Executing) == (int)OneShotExecutionState.Executing;
        }

        internal bool TryConsumeReceipt()
        {
            return Interlocked.CompareExchange(
                ref _state,
                (int)OneShotExecutionState.Consumed,
                (int)OneShotExecutionState.Receipted) == (int)OneShotExecutionState.Receipted;
        }

        internal IntentAbortDisposition Revoke()
        {
            while (true)
            {
                OneShotExecutionState state = State;
                CleanupExecutionClaim activeClaim = Volatile.Read(ref _activeClaim);
                if (state == OneShotExecutionState.Executing)
                {
                    if (activeClaim != null)
                    {
                        activeClaim.RevokeReadTickets();
                    }

                    return IntentAbortDisposition.ExecutionInFlight;
                }

                if (state == OneShotExecutionState.Consumed || state == OneShotExecutionState.Revoked)
                {
                    return IntentAbortDisposition.AlreadyTerminal;
                }

                if (Interlocked.CompareExchange(
                    ref _state,
                    (int)OneShotExecutionState.Revoked,
                    (int)state) == (int)state)
                {
                    if (activeClaim != null)
                    {
                        activeClaim.RevokeReadTickets();
                    }

                    return state == OneShotExecutionState.Issued
                        ? IntentAbortDisposition.RevokedBeforeExecution
                        : IntentAbortDisposition.SettledReceiptRevoked;
                }
            }
        }
    }

    internal sealed class CleanupExecutionClaim
    {
        private int _sideEffectEntered;
        private readonly CoordinatorSideEffectAuthority _sideEffectAuthority;
        private CleanupSideEffectPermit _activePermit;

        internal CleanupExecutionClaim(
            OneShotCleanupIntent intent,
            CoordinatorSideEffectAuthority sideEffectAuthority)
        {
            if (intent == null || sideEffectAuthority == null)
            {
                throw new ArgumentNullException(intent == null ? "intent" : "sideEffectAuthority");
            }

            Intent = intent;
            _sideEffectAuthority = sideEffectAuthority;
            ClaimNonce = Guid.NewGuid();
        }

        internal OneShotCleanupIntent Intent { get; private set; }
        internal Guid ClaimNonce { get; private set; }

        internal CleanupSideEffectPermit ActivePermit
        {
            get { return Volatile.Read(ref _activePermit); }
        }

        internal bool SideEffectEntered
        {
            get { return Volatile.Read(ref _sideEffectEntered) != 0; }
        }

        internal bool TryEnterSideEffectOnce(
            CoordinatorSideEffectAuthority authority,
            NativeCleanupMethod method,
            out CleanupSideEffectPermit permit)
        {
            if (authority != null
                && ReferenceEquals(authority, _sideEffectAuthority)
                && CleanupMethods.GetRequiredMethod(Intent.Directive.Kind) == method
                && Interlocked.CompareExchange(ref _sideEffectEntered, 1, 0) == 0)
            {
                permit = new CleanupSideEffectPermit(authority, this, method);
                if (Interlocked.CompareExchange(ref _activePermit, permit, null) == null)
                {
                    return true;
                }
            }

            permit = null;
            return false;
        }

        internal bool MatchesActivePermit(CleanupSideEffectPermit permit)
        {
            return permit != null
                && ReferenceEquals(permit.Claim, this)
                && ReferenceEquals(Volatile.Read(ref _activePermit), permit)
                && permit.PermitId != Guid.Empty;
        }

        internal void RevokeReadTickets()
        {
            CleanupSideEffectPermit permit = Volatile.Read(ref _activePermit);
            if (permit != null)
            {
                permit.RevokeReadTickets();
            }
        }
    }

    internal sealed class CleanupSideEffectPermit
    {
        private readonly CoordinatorSideEffectAuthority _authority;

        internal CleanupSideEffectPermit(
            CoordinatorSideEffectAuthority authority,
            CleanupExecutionClaim claim,
            NativeCleanupMethod method)
        {
            if (authority == null
                || claim == null
                || claim.Intent == null
                || !claim.SideEffectEntered
                || CleanupMethods.GetRequiredMethod(claim.Intent.Directive.Kind) != method)
            {
                throw new ArgumentException("Cleanup side-effect permit binding is invalid.");
            }

            _authority = authority;
            Claim = claim;
            Method = method;
            PermitId = Guid.NewGuid();
            CoordinatorSessionId = claim.Intent.CoordinatorSessionId;
            FaultId = claim.Intent.FaultId;
            DirectiveId = claim.Intent.Directive.DirectiveId;
            ProcessId = claim.Intent.Directive.ProcessId;
            ProcessCreationUtcTicks = claim.Intent.Directive.ProcessCreationUtcTicks;
            GenerationNumber = claim.Intent.Directive.GenerationNumber;
            GenerationDigest = claim.Intent.Directive.GenerationDigest;
            RequestId = claim.Intent.Directive.RequestId;
            RequestFingerprint = claim.Intent.Directive.RequestFingerprint;
            ClaimNonce = claim.ClaimNonce;
            FirstReadTicket = new CleanupPostReadTicket(this, 1);
            SecondReadTicket = new CleanupPostReadTicket(this, 2);
        }

        internal CleanupExecutionClaim Claim { get; private set; }
        internal NativeCleanupMethod Method { get; private set; }
        internal Guid PermitId { get; private set; }
        internal Guid CoordinatorSessionId { get; private set; }
        internal Guid FaultId { get; private set; }
        internal Guid DirectiveId { get; private set; }
        internal int ProcessId { get; private set; }
        internal long ProcessCreationUtcTicks { get; private set; }
        internal ulong GenerationNumber { get; private set; }
        internal FixedDigest GenerationDigest { get; private set; }
        internal Guid RequestId { get; private set; }
        internal FixedDigest RequestFingerprint { get; private set; }
        internal Guid ClaimNonce { get; private set; }
        internal CleanupPostReadTicket FirstReadTicket { get; private set; }
        internal CleanupPostReadTicket SecondReadTicket { get; private set; }

        internal void RevokeReadTickets()
        {
            FirstReadTicket.Revoke();
            SecondReadTicket.Revoke();
        }

        internal bool IsAuthorized(
            CoordinatorSideEffectAuthority authority,
            CleanupExecutionClaim claim,
            NativeCleanupMethod method)
        {
            OneShotCleanupIntent intent = claim == null ? null : claim.Intent;
            AbortCleanupDirective directive = intent == null ? null : intent.Directive;
            return authority != null
                && ReferenceEquals(authority, _authority)
                && claim != null
                && intent != null
                && directive != null
                && ReferenceEquals(Claim, claim)
                && Method == method
                && PermitId != Guid.Empty
                && claim.SideEffectEntered
                && claim.MatchesActivePermit(this)
                && CoordinatorSessionId == intent.CoordinatorSessionId
                && FaultId == intent.FaultId
                && DirectiveId == directive.DirectiveId
                && ProcessId == directive.ProcessId
                && ProcessCreationUtcTicks == directive.ProcessCreationUtcTicks
                && GenerationNumber == directive.GenerationNumber
                && GenerationDigest.Equals(directive.GenerationDigest)
                && RequestId == directive.RequestId
                && RequestFingerprint.Equals(directive.RequestFingerprint)
                && ClaimNonce == claim.ClaimNonce;
        }
    }

    internal sealed class CleanupInvocationResult
    {
        internal CleanupInvocationResult(bool callbackInvoked, bool receiptAccepted, string detail)
        {
            CallbackInvoked = callbackInvoked;
            ReceiptAccepted = receiptAccepted;
            Detail = detail ?? string.Empty;
        }

        internal bool CallbackInvoked { get; private set; }
        internal bool ReceiptAccepted { get; private set; }
        internal string Detail { get; private set; }
    }

    internal sealed class CleanupPostReadTicket
    {
        private int _state;
        private CleanupPostReadClaim _activeClaim;

        internal CleanupPostReadTicket(CleanupSideEffectPermit permit, int ordinal)
        {
            if (permit == null
                || permit.Claim == null
                || !permit.Claim.SideEffectEntered
                || (ordinal != 1 && ordinal != 2))
            {
                throw new ArgumentException("Cleanup post-read ticket binding is invalid.");
            }

            Permit = permit;
            PermitId = permit.PermitId;
            Intent = permit.Claim.Intent;
            TicketId = Guid.NewGuid();
            Ordinal = ordinal;
            _state = (int)OneShotExecutionState.Issued;
        }

        internal OneShotCleanupIntent Intent { get; private set; }
        internal CleanupSideEffectPermit Permit { get; private set; }
        internal Guid PermitId { get; private set; }
        internal Guid TicketId { get; private set; }
        internal int Ordinal { get; private set; }
        internal OneShotExecutionState State { get { return (OneShotExecutionState)Volatile.Read(ref _state); } }

        internal bool TryClaim(
            CoordinatorSideEffectAuthority authority,
            out CleanupPostReadClaim claim)
        {
            if (Intent.State == OneShotExecutionState.Executing
                && PermitId == Permit.PermitId
                && Permit.Claim.MatchesActivePermit(Permit)
                && Permit.IsAuthorized(authority, Permit.Claim, Permit.Method)
                && Interlocked.CompareExchange(
                    ref _state,
                    (int)OneShotExecutionState.Executing,
                    (int)OneShotExecutionState.Issued) == (int)OneShotExecutionState.Issued)
            {
                claim = new CleanupPostReadClaim(this);
                Interlocked.CompareExchange(ref _activeClaim, claim, null);
                return true;
            }

            claim = null;
            return false;
        }

        internal bool TryProduceReceipt(
            CoordinatorSideEffectAuthority authority,
            CleanupPostReadClaim claim)
        {
            return claim != null
                && ReferenceEquals(claim.Ticket, this)
                && ReferenceEquals(Volatile.Read(ref _activeClaim), claim)
                && Intent.State == OneShotExecutionState.Executing
                && PermitId == Permit.PermitId
                && Permit.Claim.MatchesActivePermit(Permit)
                && Permit.IsAuthorized(authority, Permit.Claim, Permit.Method)
                && Interlocked.CompareExchange(
                    ref _state,
                    (int)OneShotExecutionState.Receipted,
                    (int)OneShotExecutionState.Executing) == (int)OneShotExecutionState.Executing;
        }

        internal bool TryConsumeReceipt()
        {
            return Interlocked.CompareExchange(
                ref _state,
                (int)OneShotExecutionState.Consumed,
                (int)OneShotExecutionState.Receipted) == (int)OneShotExecutionState.Receipted;
        }

        internal void Revoke()
        {
            while (true)
            {
                OneShotExecutionState state = State;
                if (state == OneShotExecutionState.Consumed || state == OneShotExecutionState.Revoked)
                {
                    return;
                }

                if (Interlocked.CompareExchange(
                    ref _state,
                    (int)OneShotExecutionState.Revoked,
                    (int)state) == (int)state)
                {
                    return;
                }
            }
        }
    }

    internal sealed class CleanupPostReadClaim
    {
        internal CleanupPostReadClaim(CleanupPostReadTicket ticket)
        {
            Ticket = ticket;
            ClaimNonce = Guid.NewGuid();
        }

        internal CleanupPostReadTicket Ticket { get; private set; }
        internal Guid ClaimNonce { get; private set; }
    }

    internal sealed class CleanupPostReadPermit
    {
        internal CleanupPostReadPermit(
            CleanupSideEffectPermit cleanupPermit,
            CleanupPostReadClaim claim)
        {
            if (cleanupPermit == null
                || claim == null
                || claim.Ticket == null
                || !ReferenceEquals(claim.Ticket.Permit, cleanupPermit))
            {
                throw new ArgumentException("Cleanup post-read permit binding is invalid.");
            }

            CleanupPermit = cleanupPermit;
            Claim = claim;
            TicketId = claim.Ticket.TicketId;
            Ordinal = claim.Ticket.Ordinal;
            PermitId = cleanupPermit.PermitId;
            ReadPermitId = Guid.NewGuid();
        }

        internal CleanupSideEffectPermit CleanupPermit { get; private set; }
        internal CleanupPostReadClaim Claim { get; private set; }
        internal Guid TicketId { get; private set; }
        internal int Ordinal { get; private set; }
        internal Guid PermitId { get; private set; }
        internal Guid ReadPermitId { get; private set; }
    }

    internal sealed class AuthenticatedCleanupPostReadReceipt
    {
        internal AuthenticatedCleanupPostReadReceipt(
            SessionObservationCapability capability,
            CleanupPostReadClaim claim,
            CleanupPostStateRead read)
        {
            Capability = capability;
            Claim = claim;
            Ticket = claim.Ticket;
            Read = read;
        }

        internal SessionObservationCapability Capability { get; private set; }
        internal CleanupPostReadClaim Claim { get; private set; }
        internal CleanupPostReadTicket Ticket { get; private set; }
        internal CleanupPostStateRead Read { get; private set; }
    }

    internal sealed class CleanupIssueResult
    {
        internal CleanupIssueResult(bool issued, OneShotCleanupIntent intent, string detail)
        {
            Issued = issued;
            Intent = intent;
            Detail = detail ?? string.Empty;
        }

        internal bool Issued { get; private set; }
        internal OneShotCleanupIntent Intent { get; private set; }
        internal string Detail { get; private set; }
    }

    internal sealed class CleanupClaimResult
    {
        internal CleanupClaimResult(bool claimed, CleanupExecutionClaim claim, string detail)
        {
            Claimed = claimed;
            Claim = claim;
            Detail = detail ?? string.Empty;
        }

        internal bool Claimed { get; private set; }
        internal CleanupExecutionClaim Claim { get; private set; }
        internal string Detail { get; private set; }
    }

    internal sealed class AuthenticatedCleanupReceipt
    {
        internal AuthenticatedCleanupReceipt(
            SessionObservationCapability capability,
            CleanupSideEffectPermit permit,
            AuthenticatedCleanupPostReadReceipt firstReceipt,
            AuthenticatedCleanupPostReadReceipt secondReceipt,
            bool callbackCompleted,
            string diagnostic)
        {
            if (capability == null || permit == null || permit.Claim == null)
            {
                throw new ArgumentNullException(capability == null ? "capability" : "permit");
            }

            Capability = capability;
            Permit = permit;
            PermitId = permit.PermitId;
            Claim = permit.Claim;
            Intent = permit.Claim.Intent;
            FirstReceipt = firstReceipt;
            SecondReceipt = secondReceipt;
            First = firstReceipt == null ? null : firstReceipt.Read;
            Second = secondReceipt == null ? null : secondReceipt.Read;
            CallbackCompleted = callbackCompleted;
            Diagnostic = diagnostic ?? string.Empty;
            IsStable = callbackCompleted
                && First != null
                && Second != null
                && !ReferenceEquals(First, Second)
                && First.SnapshotDigest.Equals(Second.SnapshotDigest);
        }

        internal SessionObservationCapability Capability { get; private set; }
        internal CleanupSideEffectPermit Permit { get; private set; }
        internal Guid PermitId { get; private set; }
        internal CleanupExecutionClaim Claim { get; private set; }
        internal OneShotCleanupIntent Intent { get; private set; }
        internal AuthenticatedCleanupPostReadReceipt FirstReceipt { get; private set; }
        internal AuthenticatedCleanupPostReadReceipt SecondReceipt { get; private set; }
        internal CleanupPostStateRead First { get; private set; }
        internal CleanupPostStateRead Second { get; private set; }
        internal bool CallbackCompleted { get; private set; }
        internal string Diagnostic { get; private set; }
        internal bool IsStable { get; private set; }
    }

    internal sealed partial class TrustedObservationFactory
    {
        internal CleanupPostReadClaim ClaimCleanupPostRead(
            CoordinatorSideEffectAuthority authority,
            CleanupSideEffectPermit permit,
            int ordinal)
        {
            EnsureCleanupPermit(permit);
            CleanupPostReadTicket ticket;
            if (ordinal == 1)
            {
                ticket = permit.FirstReadTicket;
            }
            else if (ordinal == 2)
            {
                if (permit.FirstReadTicket.State != OneShotExecutionState.Receipted)
                {
                    permit.RevokeReadTickets();
                    throw new InvalidOperationException("Cleanup post read B cannot precede receipt A.");
                }

                ticket = permit.SecondReadTicket;
            }
            else
            {
                throw new ArgumentOutOfRangeException("ordinal");
            }

            CleanupPostReadClaim readClaim;
            if (!ticket.TryClaim(authority, out readClaim))
            {
                permit.RevokeReadTickets();
                throw new InvalidOperationException("Cleanup post-read ticket was replayed or revoked.");
            }

            return readClaim;
        }

        internal AuthenticatedCleanupPostReadReceipt CompleteCleanupPostRead(
            CoordinatorSideEffectAuthority authority,
            CleanupPostReadClaim claim,
            CleanupPostStateRead read)
        {
            EnsureActive();
            if (claim == null || read == null)
            {
                throw new ArgumentNullException(claim == null ? "claim" : "read");
            }

            EnsureCleanupPermit(claim.Ticket.Permit);
            if (!ReferenceEquals(claim.Ticket.Intent.Capability, _capability)
                || claim.Ticket.PermitId != claim.Ticket.Permit.PermitId
                || !claim.Ticket.TryProduceReceipt(authority, claim))
            {
                throw new InvalidOperationException("Cleanup post-read claim is foreign, stale, or already receipted.");
            }

            return new AuthenticatedCleanupPostReadReceipt(_capability, claim, read);
        }

        internal AuthenticatedCleanupReceipt CompleteCleanup(
            CoordinatorSideEffectAuthority authority,
            CleanupSideEffectPermit permit,
            AuthenticatedCleanupPostReadReceipt first,
            AuthenticatedCleanupPostReadReceipt second,
            string diagnostic)
        {
            EnsureCleanupPermit(permit);
            if (!permit.IsAuthorized(authority, permit.Claim, permit.Method))
            {
                throw new InvalidOperationException("Cleanup completion lacks coordinator settlement authority.");
            }
            CleanupExecutionClaim claim = permit.Claim;
            if (first == null
                || second == null
                || ReferenceEquals(first, second)
                || ReferenceEquals(first.Read, second.Read)
                || !ReferenceEquals(first.Capability, _capability)
                || !ReferenceEquals(second.Capability, _capability)
                || !ReferenceEquals(first.Ticket.Permit, permit)
                || !ReferenceEquals(second.Ticket.Permit, permit)
                || first.Ticket.PermitId != permit.PermitId
                || second.Ticket.PermitId != permit.PermitId
                || !ReferenceEquals(first.Ticket, permit.FirstReadTicket)
                || !ReferenceEquals(second.Ticket, permit.SecondReadTicket)
                || first.Ticket.Ordinal != 1
                || second.Ticket.Ordinal != 2
                || first.Ticket.TicketId == second.Ticket.TicketId)
            {
                claim.RevokeReadTickets();
                throw new InvalidOperationException("Cleanup success requires two independent post-state reads.");
            }

            if (!first.Ticket.TryConsumeReceipt() || !second.Ticket.TryConsumeReceipt())
            {
                claim.RevokeReadTickets();
                throw new InvalidOperationException("Cleanup post-read receipt was replayed or concurrently consumed.");
            }

            if (!claim.Intent.TryProduceReceipt(claim))
            {
                throw new InvalidOperationException("Cleanup claim is stale, revoked, or already receipted.");
            }

            return new AuthenticatedCleanupReceipt(_capability, permit, first, second, true, diagnostic);
        }

        internal AuthenticatedCleanupReceipt FailCleanup(
            CoordinatorSideEffectAuthority authority,
            CleanupSideEffectPermit permit,
            string diagnostic)
        {
            EnsureCleanupPermit(permit);
            if (!permit.IsAuthorized(authority, permit.Claim, permit.Method))
            {
                throw new InvalidOperationException("Cleanup failure lacks coordinator settlement authority.");
            }
            CleanupExecutionClaim claim = permit.Claim;
            permit.RevokeReadTickets();
            if (!claim.Intent.TryProduceReceipt(claim))
            {
                throw new InvalidOperationException("Cleanup claim is stale, revoked, or already receipted.");
            }

            return new AuthenticatedCleanupReceipt(_capability, permit, null, null, false, diagnostic);
        }

        private void EnsureCleanupPermit(CleanupSideEffectPermit permit)
        {
            EnsureActive();
            if (permit == null
                || permit.Claim == null
                || !permit.Claim.SideEffectEntered
                || !permit.Claim.MatchesActivePermit(permit)
                || permit.PermitId == Guid.Empty
                || !ReferenceEquals(permit.Claim.Intent.Capability, _capability)
                || CleanupMethods.GetRequiredMethod(permit.Claim.Intent.Directive.Kind) != permit.Method)
            {
                throw new InvalidOperationException(
                    "Cleanup permit does not belong to this frozen session factory or callback entry.");
            }
        }
    }
}
