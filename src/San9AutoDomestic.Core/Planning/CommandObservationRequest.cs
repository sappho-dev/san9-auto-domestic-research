using System;
using San9AutoDomestic.Core.Configuration;
using San9AutoDomestic.Core.Domain;

namespace San9AutoDomestic.Core.Planning
{
    /// <summary>
    /// Opaque, Core-issued challenge for one future command re-read. It is not
    /// an execution authorization. Only the trusted adapter friend assembly can
    /// turn it into an observation capability.
    /// </summary>
    public sealed class CommandObservationRequest
    {
        internal CommandObservationRequest(BatchContext context, int cityId, DomesticCommand command)
        {
            if (context == null)
            {
                throw new ArgumentNullException("context");
            }

            RequestId = Guid.NewGuid().ToString("N");
            BatchId = context.BatchId;
            CityId = cityId;
            Command = command;
            MinimumSnapshotGeneration = context.InitialSnapshotGeneration.Value;
            ExpectedProcessId = context.ProcessId.Value;
            ExpectedProcessStartUtcTicks = context.ProcessStartUtcTicks.Value;
            ExpectedPlayerForceId = context.PlayerForceId;
            ExpectedScenarioToken = context.ScenarioToken.Value;
            ExpectedTurnToken = context.TurnToken.Value;
            ExpectedPhaseToken = context.PhaseToken.Value;
        }

        public string RequestId { get; private set; }

        public string BatchId { get; private set; }

        public int CityId { get; private set; }

        public DomesticCommand Command { get; private set; }

        public long MinimumSnapshotGeneration { get; private set; }

        internal int ExpectedProcessId { get; private set; }

        internal long ExpectedProcessStartUtcTicks { get; private set; }

        internal int ExpectedPlayerForceId { get; private set; }

        internal string ExpectedScenarioToken { get; private set; }

        internal string ExpectedTurnToken { get; private set; }

        internal string ExpectedPhaseToken { get; private set; }

        internal bool Matches(GameSnapshotContext context)
        {
            return context != null
                && context.ProcessId.HasValue
                && context.ProcessId.Value == ExpectedProcessId
                && context.ProcessStartUtcTicks.HasValue
                && context.ProcessStartUtcTicks.Value == ExpectedProcessStartUtcTicks
                && context.PlayerForceId.HasValue
                && context.PlayerForceId.Value == ExpectedPlayerForceId
                && context.SnapshotGeneration.HasValue
                && context.SnapshotGeneration.Value > MinimumSnapshotGeneration
                && IsVerifiedMatch(context.ScenarioToken, ExpectedScenarioToken)
                && IsVerifiedMatch(context.TurnToken, ExpectedTurnToken)
                && IsVerifiedMatch(context.PhaseToken, ExpectedPhaseToken);
        }

        private static bool IsVerifiedMatch(GameContextToken token, string expected)
        {
            return token != null
                && token.IsVerified
                && string.Equals(token.Value, expected, StringComparison.Ordinal);
        }
    }
}
