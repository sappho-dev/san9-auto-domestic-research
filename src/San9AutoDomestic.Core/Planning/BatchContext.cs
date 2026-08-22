using System;
using San9AutoDomestic.Core.Configuration;
using San9AutoDomestic.Core.Domain;

namespace San9AutoDomestic.Core.Planning
{
    /// <summary>
    /// Immutable binding between a frozen profile/configuration and the game
    /// observation used to capture a city queue. The constructor is internal so
    /// callers cannot manufacture a queue context independently of Capture().
    /// </summary>
    public sealed class BatchContext
    {
        internal BatchContext(ExecutionPlan plan, GameSnapshot snapshot)
        {
            if (plan == null)
            {
                throw new ArgumentNullException("plan");
            }

            if (snapshot == null)
            {
                throw new ArgumentNullException("snapshot");
            }

            BatchId = Guid.NewGuid().ToString("N");
            ProfileId = plan.ProfileId;
            ConfigurationFingerprint = plan.ConfigurationFingerprint;
            PlayerForceId = snapshot.PlayerForceId;

            GameSnapshotContext context = snapshot.Context;
            ProcessId = context.ProcessId;
            ProcessStartUtcTicks = context.ProcessStartUtcTicks;
            ScenarioToken = context.ScenarioToken;
            TurnToken = context.TurnToken;
            PhaseToken = context.PhaseToken;
            InitialSnapshotGeneration = context.SnapshotGeneration;
            InitialReadiness = context.Readiness;
            InitialObservationId = context.ObservationId;
            InitialObservationTrusted = context.HasTrustedObservation;
            InitialPlanningComplete = snapshot.IsPlanningComplete;
            InitialPlanningIncompleteDetail = snapshot.PlanningIncompleteDetail;
        }

        public string BatchId { get; private set; }

        public string ProfileId { get; private set; }

        public string ConfigurationFingerprint { get; private set; }

        public int? ProcessId { get; private set; }

        public long? ProcessStartUtcTicks { get; private set; }

        public int PlayerForceId { get; private set; }

        public GameContextToken ScenarioToken { get; private set; }

        public GameContextToken TurnToken { get; private set; }

        public GameContextToken PhaseToken { get; private set; }

        public long? InitialSnapshotGeneration { get; private set; }

        public SnapshotReadiness InitialReadiness { get; private set; }

        public string InitialObservationId { get; private set; }

        public bool InitialObservationTrusted { get; private set; }

        public bool InitialPlanningComplete { get; private set; }

        public string InitialPlanningIncompleteDetail { get; private set; }
    }
}
