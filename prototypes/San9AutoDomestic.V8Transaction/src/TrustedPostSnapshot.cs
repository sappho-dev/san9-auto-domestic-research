using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using System.Threading;

namespace San9AutoDomestic.V8Transaction
{
    internal sealed class CoordinatorPostReadClaimAuthority
    {
    }

    internal sealed class SessionObservationCapability
    {
        private int _active;

        internal SessionObservationCapability(Guid sessionId)
        {
            if (sessionId == Guid.Empty)
            {
                throw new ArgumentException("Session id is required.", "sessionId");
            }

            SessionId = sessionId;
            CapabilityId = Guid.NewGuid();
            _active = 1;
        }

        internal Guid SessionId { get; private set; }

        internal Guid CapabilityId { get; private set; }

        internal bool IsActive
        {
            get { return Volatile.Read(ref _active) == 1; }
        }

        internal void Revoke()
        {
            Interlocked.Exchange(ref _active, 0);
        }
    }

    internal sealed class PostStateRead
    {
        private readonly ReadOnlyCollection<int> _orderedOfficerIds;
        private readonly ReadOnlyCollection<int> _busyOfficerIds;

        internal PostStateRead(
            int processId,
            long processCreationUtcTicks,
            ulong generationNumber,
            FixedDigest generationDigest,
            FixedDigest contextDigest,
            Guid requestId,
            ulong sequence,
            FixedDigest requestFingerprint,
            int cityId,
            int corpsId,
            RegisteredDomesticCommand command,
            int observedNativeCommandId,
            NativeOuterTaskType observedOuterTaskType,
            uint observedOuterTaskVptr,
            IEnumerable<int> orderedOfficerIds,
            IEnumerable<int> busyOfficerIds,
            bool commandStateVerified,
            bool nativeOrderVerified,
            int moneyBefore,
            int moneyAfter)
        {
            if (processId <= 0)
            {
                throw new ArgumentOutOfRangeException("processId");
            }

            if (processCreationUtcTicks <= 0 || processCreationUtcTicks > DateTime.MaxValue.Ticks)
            {
                throw new ArgumentOutOfRangeException("processCreationUtcTicks");
            }

            if (generationNumber == 0)
            {
                throw new ArgumentOutOfRangeException("generationNumber");
            }

            if (generationDigest == null || contextDigest == null)
            {
                throw new ArgumentNullException(
                    generationDigest == null ? "generationDigest" : "contextDigest");
            }

            if (requestId == Guid.Empty || sequence == 0 || requestFingerprint == null)
            {
                throw new ArgumentException("Post-state request binding is absent.");
            }

            if (cityId < 0 || cityId >= SingleCommandRequest.CityCount
                || corpsId < 0 || corpsId >= SingleCommandRequest.CorpsCount)
            {
                throw new ArgumentOutOfRangeException("cityId", "City/corps is outside the exact target bounds.");
            }

            NativeCommandDescriptorRegistry.GetRequired(command);
            if (!Enum.IsDefined(typeof(NativeOuterTaskType), observedOuterTaskType)
                || observedOuterTaskType == NativeOuterTaskType.Unknown)
            {
                throw new ArgumentOutOfRangeException("observedOuterTaskType");
            }

            if (orderedOfficerIds == null || busyOfficerIds == null)
            {
                throw new ArgumentNullException(
                    orderedOfficerIds == null ? "orderedOfficerIds" : "busyOfficerIds");
            }

            ProcessId = processId;
            ProcessCreationUtcTicks = processCreationUtcTicks;
            GenerationNumber = generationNumber;
            GenerationDigest = generationDigest;
            ContextDigest = contextDigest;
            RequestId = requestId;
            Sequence = sequence;
            RequestFingerprint = requestFingerprint;
            CityId = cityId;
            CorpsId = corpsId;
            Command = command;
            ObservedNativeCommandId = observedNativeCommandId;
            ObservedOuterTaskType = observedOuterTaskType;
            ObservedOuterTaskVptr = observedOuterTaskVptr;
            _orderedOfficerIds = CopyIds(orderedOfficerIds, "orderedOfficerIds");
            _busyOfficerIds = CopyIds(busyOfficerIds, "busyOfficerIds");
            CommandStateVerified = commandStateVerified;
            NativeOrderVerified = nativeOrderVerified;
            MoneyBefore = moneyBefore;
            MoneyAfter = moneyAfter;
            SnapshotDigest = ComputeDigest();
        }

        internal int ProcessId { get; private set; }
        internal long ProcessCreationUtcTicks { get; private set; }
        internal ulong GenerationNumber { get; private set; }
        internal FixedDigest GenerationDigest { get; private set; }
        internal FixedDigest ContextDigest { get; private set; }
        internal Guid RequestId { get; private set; }
        internal ulong Sequence { get; private set; }
        internal FixedDigest RequestFingerprint { get; private set; }
        internal int CityId { get; private set; }
        internal int CorpsId { get; private set; }
        internal RegisteredDomesticCommand Command { get; private set; }
        internal int ObservedNativeCommandId { get; private set; }
        internal NativeOuterTaskType ObservedOuterTaskType { get; private set; }
        internal uint ObservedOuterTaskVptr { get; private set; }

        internal ReadOnlyCollection<int> OrderedOfficerIds
        {
            get { return _orderedOfficerIds; }
        }

        internal ReadOnlyCollection<int> BusyOfficerIds
        {
            get { return _busyOfficerIds; }
        }

        internal bool CommandStateVerified { get; private set; }
        internal bool NativeOrderVerified { get; private set; }
        internal int MoneyBefore { get; private set; }
        internal int MoneyAfter { get; private set; }
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
                writer.Write(RequestId.ToByteArray());
                writer.Write(Sequence);
                writer.Write(RequestFingerprint.ToArray());
                writer.Write(CityId);
                writer.Write(CorpsId);
                writer.Write((int)Command);
                writer.Write(ObservedNativeCommandId);
                writer.Write((int)ObservedOuterTaskType);
                writer.Write(ObservedOuterTaskVptr);
                WriteIds(writer, _orderedOfficerIds);
                WriteIds(writer, _busyOfficerIds);
                writer.Write(CommandStateVerified);
                writer.Write(NativeOrderVerified);
                writer.Write(MoneyBefore);
                writer.Write(MoneyAfter);
                writer.Flush();
                using (SHA256 algorithm = SHA256.Create())
                {
                    return FixedDigest.Create(algorithm.ComputeHash(stream.ToArray()));
                }
            }
        }

        private static ReadOnlyCollection<int> CopyIds(IEnumerable<int> ids, string parameterName)
        {
            List<int> copy = new List<int>(ids);
            HashSet<int> unique = new HashSet<int>();
            foreach (int id in copy)
            {
                if (id < 0 || id >= SingleCommandRequest.OfficerCount || !unique.Add(id))
                {
                    throw new ArgumentException("Post-state officer ids must be valid and unique.", parameterName);
                }
            }

            return new ReadOnlyCollection<int>(copy);
        }

        private static void WriteIds(BinaryWriter writer, IList<int> ids)
        {
            writer.Write(ids.Count);
            for (int index = 0; index < ids.Count; index++)
            {
                writer.Write(ids[index]);
            }
        }
    }

    internal sealed class PostReadTicket
    {
        private int _state;
        private readonly CoordinatorPostReadClaimAuthority _claimAuthority;
        private PostReadClaim _activeClaim;

        internal PostReadTicket(
            SessionObservationCapability capability,
            CoordinatorPostReadClaimAuthority claimAuthority,
            ProcessGenerationKey key,
            SingleCommandRequest request,
            int ordinal)
        {
            if (capability == null || claimAuthority == null || key == null || request == null)
            {
                throw new ArgumentNullException(capability == null ? "capability" : (key == null ? "key" : "request"));
            }

            if (ordinal != 1 && ordinal != 2)
            {
                throw new ArgumentOutOfRangeException("ordinal");
            }

            Capability = capability;
            _claimAuthority = claimAuthority;
            CoordinatorSessionId = capability.SessionId;
            TicketId = Guid.NewGuid();
            ProcessId = key.ProcessId;
            ProcessCreationUtcTicks = key.ProcessCreationUtcTicks;
            InitialGenerationNumber = key.GenerationNumber;
            InitialGenerationDigest = key.GenerationDigest;
            RequestId = request.RequestId;
            Sequence = request.Sequence;
            RequestFingerprint = request.RequestFingerprint;
            Ordinal = ordinal;
            _state = (int)OneShotExecutionState.Issued;
        }

        internal SessionObservationCapability Capability { get; private set; }
        internal Guid CoordinatorSessionId { get; private set; }
        internal Guid TicketId { get; private set; }
        internal int ProcessId { get; private set; }
        internal long ProcessCreationUtcTicks { get; private set; }
        internal ulong InitialGenerationNumber { get; private set; }
        internal FixedDigest InitialGenerationDigest { get; private set; }
        internal Guid RequestId { get; private set; }
        internal ulong Sequence { get; private set; }
        internal FixedDigest RequestFingerprint { get; private set; }
        internal int Ordinal { get; private set; }
        internal OneShotExecutionState State { get { return (OneShotExecutionState)Volatile.Read(ref _state); } }

        internal bool TryClaim(
            CoordinatorPostReadClaimAuthority claimAuthority,
            out PostReadClaim claim)
        {
            if (ReferenceEquals(claimAuthority, _claimAuthority)
                && Interlocked.CompareExchange(
                ref _state,
                (int)OneShotExecutionState.Executing,
                (int)OneShotExecutionState.Issued) == (int)OneShotExecutionState.Issued)
            {
                claim = new PostReadClaim(this);
                Interlocked.CompareExchange(ref _activeClaim, claim, null);
                return true;
            }

            claim = null;
            return false;
        }

        internal bool TryProduceReceipt(PostReadClaim claim)
        {
            return claim != null
                && ReferenceEquals(claim.Ticket, this)
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

    internal sealed class PostReadClaim
    {
        internal PostReadClaim(PostReadTicket ticket)
        {
            Ticket = ticket;
            ClaimNonce = Guid.NewGuid();
        }

        internal PostReadTicket Ticket { get; private set; }
        internal Guid ClaimNonce { get; private set; }
    }

    internal sealed class PostReadPairIssueResult
    {
        internal PostReadPairIssueResult(
            bool issued,
            PostReadTicket first,
            PostReadTicket second,
            TransactionTransition transition,
            string detail)
        {
            Issued = issued;
            First = first;
            Second = second;
            Transition = transition;
            Detail = detail ?? string.Empty;
        }

        internal bool Issued { get; private set; }
        internal PostReadTicket First { get; private set; }
        internal PostReadTicket Second { get; private set; }
        internal TransactionTransition Transition { get; private set; }
        internal string Detail { get; private set; }
    }

    internal sealed class PostReadClaimResult
    {
        internal PostReadClaimResult(
            bool claimed,
            PostReadClaim claim,
            TransactionTransition transition,
            string detail)
        {
            Claimed = claimed;
            Claim = claim;
            Transition = transition;
            Detail = detail ?? string.Empty;
        }

        internal bool Claimed { get; private set; }
        internal PostReadClaim Claim { get; private set; }
        internal TransactionTransition Transition { get; private set; }
        internal string Detail { get; private set; }
    }

    internal sealed class AuthenticatedPostReadReceipt
    {
        internal AuthenticatedPostReadReceipt(
            SessionObservationCapability capability,
            PostReadClaim claim,
            PostStateRead read)
        {
            Capability = capability;
            Claim = claim;
            Ticket = claim.Ticket;
            Read = read;
        }

        internal SessionObservationCapability Capability { get; private set; }
        internal PostReadClaim Claim { get; private set; }
        internal PostReadTicket Ticket { get; private set; }
        internal PostStateRead Read { get; private set; }
    }

    internal sealed class AuthenticatedStablePostSnapshot
    {
        internal AuthenticatedStablePostSnapshot(
            SessionObservationCapability capability,
            AuthenticatedPostReadReceipt first,
            AuthenticatedPostReadReceipt second)
        {
            ObservationCapability = capability;
            FirstReceipt = first;
            SecondReceipt = second;
            Read = second.Read;
            FirstDigest = first.Read.SnapshotDigest;
            SecondDigest = second.Read.SnapshotDigest;
            IsStable = FirstDigest.Equals(SecondDigest);
        }

        internal SessionObservationCapability ObservationCapability { get; private set; }
        internal AuthenticatedPostReadReceipt FirstReceipt { get; private set; }
        internal AuthenticatedPostReadReceipt SecondReceipt { get; private set; }
        internal PostStateRead Read { get; private set; }
        internal FixedDigest FirstDigest { get; private set; }
        internal FixedDigest SecondDigest { get; private set; }
        internal bool IsStable { get; private set; }
    }

    internal sealed partial class TrustedObservationFactory
    {
        private readonly SessionObservationCapability _capability;

        internal TrustedObservationFactory(SessionObservationCapability capability)
        {
            if (capability == null)
            {
                throw new ArgumentNullException("capability");
            }

            _capability = capability;
        }

        internal Guid SessionId
        {
            get { return _capability.SessionId; }
        }

        internal bool IsActive
        {
            get { return _capability.IsActive; }
        }

        internal T Seal<T>(T evidence)
            where T : SyntheticStageEvidence
        {
            EnsureActive();
            if (evidence == null)
            {
                throw new ArgumentNullException("evidence");
            }

            evidence.Seal(_capability);
            return evidence;
        }

        internal ActionReceipt Complete(
            ActionSideEffectPermit permit,
            SyntheticStageEvidence evidence,
            string diagnostic)
        {
            EnsureActionPermit(permit);
            if (evidence == null)
            {
                throw new ArgumentNullException("evidence");
            }

            Seal(evidence);
            if (!permit.Claim.Intent.TryProduceReceipt(permit.Claim))
            {
                throw new InvalidOperationException("Action claim is stale, revoked, or already receipted.");
            }

            return new ActionReceipt(_capability, permit, evidence, true, diagnostic);
        }

        internal ActionReceipt Fail(ActionSideEffectPermit permit, string diagnostic)
        {
            EnsureActionPermit(permit);
            if (!permit.Claim.Intent.TryProduceReceipt(permit.Claim))
            {
                throw new InvalidOperationException("Action claim is stale, revoked, or already receipted.");
            }

            return new ActionReceipt(_capability, permit, null, false, diagnostic);
        }

        internal AuthenticatedPostReadReceipt CompletePostRead(
            PostReadClaim claim,
            PostStateRead read)
        {
            EnsureActive();
            if (claim == null || read == null)
            {
                throw new ArgumentNullException(claim == null ? "claim" : "read");
            }

            if (!ReferenceEquals(claim.Ticket.Capability, _capability)
                || !claim.Ticket.TryProduceReceipt(claim))
            {
                throw new InvalidOperationException("Post read claim is foreign, stale, or already receipted.");
            }

            return new AuthenticatedPostReadReceipt(_capability, claim, read);
        }

        internal AuthenticatedStablePostSnapshot CreatePostSnapshot(
            AuthenticatedPostReadReceipt first,
            AuthenticatedPostReadReceipt second)
        {
            EnsureActive();
            if (first == null || second == null)
            {
                throw new ArgumentNullException(first == null ? "first" : "second");
            }

            if (ReferenceEquals(first, second)
                || ReferenceEquals(first.Read, second.Read)
                || !ReferenceEquals(first.Capability, _capability)
                || !ReferenceEquals(second.Capability, _capability)
                || first.Ticket.Ordinal != 1
                || second.Ticket.Ordinal != 2
                || first.Ticket.TicketId == second.Ticket.TicketId
                || first.Ticket.CoordinatorSessionId != second.Ticket.CoordinatorSessionId
                || first.Ticket.RequestId != second.Ticket.RequestId
                || first.Ticket.Sequence != second.Ticket.Sequence
                || !first.Ticket.RequestFingerprint.Equals(second.Ticket.RequestFingerprint))
            {
                first.Ticket.Revoke();
                second.Ticket.Revoke();
                throw new InvalidOperationException("Post A/B receipts are not independent ordered reads of one request.");
            }

            if (!first.Ticket.TryConsumeReceipt() || !second.Ticket.TryConsumeReceipt())
            {
                first.Ticket.Revoke();
                second.Ticket.Revoke();
                throw new InvalidOperationException("Post A/B receipt was replayed or concurrently consumed.");
            }

            return new AuthenticatedStablePostSnapshot(_capability, first, second);
        }

        private void EnsureActionPermit(ActionSideEffectPermit permit)
        {
            EnsureActive();
            if (permit == null
                || permit.Claim == null
                || !permit.Claim.SideEffectEntered
                || !ReferenceEquals(permit.Claim.Intent.Capability, _capability)
                || permit.Claim.ClaimNonce != permit.ClaimNonce
                || permit.Claim.Intent.Stage != permit.Stage
                || ActionStages.GetRequiredMethod(permit.Stage) != permit.Method)
            {
                throw new InvalidOperationException("Action permit does not belong to this frozen session factory.");
            }
        }

        private void EnsureActive()
        {
            if (!_capability.IsActive)
            {
                throw new InvalidOperationException("The frozen coordinator session capability is retired.");
            }
        }
    }
}
