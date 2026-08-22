using System;
using System.Collections.Generic;
using System.Threading;
using San9AutoDomestic.Core.Configuration;
using San9AutoDomestic.Core.Planning;

namespace San9AutoDomestic.Core.Domain
{
    /// <summary>
    /// An in-process proof that snapshot data came through a trusted reader.
    /// The type and every creation path are internal; public callers can only
    /// construct unverified/structure-only data.
    /// </summary>
    internal sealed class TrustedObservationCapability
    {
        private int _contextIssued;

        internal TrustedObservationCapability(CommandObservationRequest commandRequest)
        {
            ObservationId = Guid.NewGuid().ToString("N");
            CommandRequest = commandRequest;
        }

        internal string ObservationId { get; private set; }

        internal CommandObservationRequest CommandRequest { get; private set; }

        internal void ClaimContext()
        {
            if (Interlocked.Exchange(ref _contextIssued, 1) != 0)
            {
                throw new InvalidOperationException(
                    "A trusted observation capability can be bound to only one snapshot context.");
            }
        }
    }

    /// <summary>
    /// Friend-visible only to the exact adapter assembly and Core tests. A
    /// future verified reader must use one capability for the context and all
    /// native rankings read in the same observation.
    /// </summary>
    internal static class TrustedSnapshotFactory
    {
        internal static TrustedObservationCapability BeginPlanningObservation()
        {
            return new TrustedObservationCapability(null);
        }

        internal static TrustedObservationCapability BeginCommandObservation(
            CommandObservationRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException("request");
            }

            return new TrustedObservationCapability(request);
        }

        internal static GameSnapshotContext CreatePlanningContext(
            TrustedObservationCapability capability,
            int processId,
            long processStartUtcTicks,
            int playerForceId,
            string scenarioToken,
            string turnToken,
            string phaseToken,
            bool tokensVerified,
            long snapshotGeneration,
            string detail)
        {
            if (capability == null)
            {
                throw new ArgumentNullException("capability");
            }

            capability.ClaimContext();

            return GameSnapshotContext.CreateTrustedPlanningReady(
                capability,
                processId,
                processStartUtcTicks,
                playerForceId,
                scenarioToken,
                turnToken,
                phaseToken,
                tokensVerified,
                snapshotGeneration,
                detail);
        }

        internal static NativeCommandSnapshot CreateNativeCommand(
            TrustedObservationCapability capability,
            DomesticCommand command,
            bool canExecute,
            NativeCommandBlockReason blockReason,
            IEnumerable<int> rankedCandidateOfficerIds,
            int estimatedCostPerOfficer)
        {
            if (capability == null)
            {
                throw new ArgumentNullException("capability");
            }

            return new NativeCommandSnapshot(
                command,
                canExecute,
                blockReason,
                rankedCandidateOfficerIds,
                estimatedCostPerOfficer,
                capability);
        }

        internal static NativeCommandSnapshot ReissueNativeCommand(
            TrustedObservationCapability capability,
            NativeCommandSnapshot source)
        {
            if (source == null)
            {
                throw new ArgumentNullException("source");
            }

            return CreateNativeCommand(
                capability,
                source.Command,
                source.CanExecute,
                source.BlockReason,
                source.RankedCandidateOfficerIds,
                source.EstimatedCostPerOfficer);
        }
    }
}
