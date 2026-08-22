using System;

namespace San9AutoDomestic.Core.Domain
{
    public enum SnapshotReadiness
    {
        StructureOnly = 1,
        PlanningReady = 2
    }

    public sealed class GameContextToken
    {
        public GameContextToken(string value)
            : this(value, false, null)
        {
        }

        internal GameContextToken(
            string value,
            bool isVerified,
            TrustedObservationCapability observationCapability)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("A context token value is required.", "value");
            }

            if (isVerified && observationCapability == null)
            {
                throw new ArgumentException(
                    "A verified token requires an internal trusted-observation capability.",
                    "observationCapability");
            }

            Value = value;
            IsVerified = isVerified;
            ObservationCapability = observationCapability;
        }

        public string Value { get; private set; }

        public bool IsVerified { get; private set; }

        internal TrustedObservationCapability ObservationCapability { get; private set; }
    }

    /// <summary>
    /// Identifies the process/game observation from which a snapshot was read.
    /// Structure-only readers may leave fields unavailable, but such snapshots
    /// are deliberately rejected by the planner rather than by construction.
    /// </summary>
    public sealed class GameSnapshotContext
    {
        public GameSnapshotContext(
            int? processId,
            long? processStartUtcTicks,
            int? playerForceId,
            GameContextToken scenarioToken,
            GameContextToken turnToken,
            GameContextToken phaseToken,
            long? snapshotGeneration,
            SnapshotReadiness readiness,
            string detail)
            : this(
                processId,
                processStartUtcTicks,
                playerForceId,
                scenarioToken,
                turnToken,
                phaseToken,
                snapshotGeneration,
                readiness,
                detail,
                null)
        {
        }

        private GameSnapshotContext(
            int? processId,
            long? processStartUtcTicks,
            int? playerForceId,
            GameContextToken scenarioToken,
            GameContextToken turnToken,
            GameContextToken phaseToken,
            long? snapshotGeneration,
            SnapshotReadiness readiness,
            string detail,
            TrustedObservationCapability observationCapability)
        {
            if (!Enum.IsDefined(typeof(SnapshotReadiness), readiness))
            {
                throw new ArgumentOutOfRangeException("readiness");
            }

            if (processId.HasValue && processId.Value <= 0)
            {
                throw new ArgumentOutOfRangeException("processId");
            }

            if (processStartUtcTicks.HasValue && processStartUtcTicks.Value <= 0)
            {
                throw new ArgumentOutOfRangeException("processStartUtcTicks");
            }

            if (processStartUtcTicks.HasValue
                && processStartUtcTicks.Value > DateTime.MaxValue.Ticks)
            {
                throw new ArgumentOutOfRangeException("processStartUtcTicks");
            }

            if (playerForceId.HasValue && playerForceId.Value < 0)
            {
                throw new ArgumentOutOfRangeException("playerForceId");
            }

            if (snapshotGeneration.HasValue && snapshotGeneration.Value <= 0)
            {
                throw new ArgumentOutOfRangeException("snapshotGeneration");
            }

            if (readiness == SnapshotReadiness.PlanningReady
                && (!processId.HasValue
                    || !processStartUtcTicks.HasValue
                    || !playerForceId.HasValue
                    || scenarioToken == null
                    || turnToken == null
                    || phaseToken == null
                    || !snapshotGeneration.HasValue))
            {
                throw new ArgumentException(
                    "Planning-ready context requires process, player, scenario, turn, phase, and generation fields.");
            }

            if (readiness == SnapshotReadiness.StructureOnly && string.IsNullOrWhiteSpace(detail))
            {
                throw new ArgumentException("Structure-only snapshots require an incompleteness detail.", "detail");
            }

            if (observationCapability == null
                && ((scenarioToken != null && scenarioToken.IsVerified)
                    || (turnToken != null && turnToken.IsVerified)
                    || (phaseToken != null && phaseToken.IsVerified)))
            {
                throw new ArgumentException(
                    "Public snapshot contexts cannot assert verified game tokens.");
            }

            if (observationCapability != null)
            {
                if (readiness != SnapshotReadiness.PlanningReady)
                {
                    throw new ArgumentException(
                        "Trusted observations must use planning-ready context.",
                        "readiness");
                }

                ValidateTokenCapability(scenarioToken, observationCapability, "scenarioToken");
                ValidateTokenCapability(turnToken, observationCapability, "turnToken");
                ValidateTokenCapability(phaseToken, observationCapability, "phaseToken");
            }

            ProcessId = processId;
            ProcessStartUtcTicks = processStartUtcTicks;
            PlayerForceId = playerForceId;
            ScenarioToken = scenarioToken;
            TurnToken = turnToken;
            PhaseToken = phaseToken;
            SnapshotGeneration = snapshotGeneration;
            Readiness = readiness;
            Detail = detail ?? string.Empty;
            ObservationCapability = observationCapability;
        }

        public int? ProcessId { get; private set; }

        public long? ProcessStartUtcTicks { get; private set; }

        public int? PlayerForceId { get; private set; }

        public GameContextToken ScenarioToken { get; private set; }

        public GameContextToken TurnToken { get; private set; }

        public GameContextToken PhaseToken { get; private set; }

        public long? SnapshotGeneration { get; private set; }

        public SnapshotReadiness Readiness { get; private set; }

        public bool IsPlanningReady
        {
            get { return Readiness == SnapshotReadiness.PlanningReady; }
        }

        public string Detail { get; private set; }

        /// <summary>
        /// Diagnostic-only observation identifier. Possessing this string does
        /// not allow a caller to manufacture the internal capability.
        /// </summary>
        public string ObservationId
        {
            get
            {
                return ObservationCapability == null
                    ? string.Empty
                    : ObservationCapability.ObservationId;
            }
        }

        public string CommandObservationRequestId
        {
            get
            {
                return ObservationCapability == null
                    || ObservationCapability.CommandRequest == null
                    ? string.Empty
                    : ObservationCapability.CommandRequest.RequestId;
            }
        }

        internal TrustedObservationCapability ObservationCapability { get; private set; }

        internal bool HasTrustedObservation
        {
            get { return ObservationCapability != null; }
        }

        public static GameSnapshotContext CreateStructureOnly(int? playerForceId, string detail)
        {
            return new GameSnapshotContext(
                null,
                null,
                playerForceId,
                null,
                null,
                null,
                null,
                SnapshotReadiness.StructureOnly,
                detail);
        }

        internal static GameSnapshotContext CreateTrustedPlanningReady(
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

            return new GameSnapshotContext(
                processId,
                processStartUtcTicks,
                playerForceId,
                new GameContextToken(scenarioToken, tokensVerified, capability),
                new GameContextToken(turnToken, tokensVerified, capability),
                new GameContextToken(phaseToken, tokensVerified, capability),
                snapshotGeneration,
                SnapshotReadiness.PlanningReady,
                detail,
                capability);
        }

        private static void ValidateTokenCapability(
            GameContextToken token,
            TrustedObservationCapability capability,
            string parameterName)
        {
            if (token == null || !ReferenceEquals(token.ObservationCapability, capability))
            {
                throw new ArgumentException(
                    "Trusted context tokens must originate from the same observation capability.",
                    parameterName);
            }
        }
    }
}
