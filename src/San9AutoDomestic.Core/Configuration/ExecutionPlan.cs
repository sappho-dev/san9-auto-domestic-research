using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace San9AutoDomestic.Core.Configuration
{
    public sealed class FrozenTaskPlan
    {
        internal FrozenTaskPlan(
            DomesticCommand command,
            int minOfficers,
            int maxOfficers,
            bool requireExactCount,
            SelectionPolicy selectionPolicy,
            int reserveMoney)
        {
            Command = command;
            MinOfficers = minOfficers;
            MaxOfficers = maxOfficers;
            RequireExactCount = requireExactCount;
            SelectionPolicy = selectionPolicy;
            ReserveMoney = reserveMoney;
        }

        public DomesticCommand Command { get; private set; }

        public int MinOfficers { get; private set; }

        public int MaxOfficers { get; private set; }

        public bool RequireExactCount { get; private set; }

        public SelectionPolicy SelectionPolicy { get; private set; }

        public int ReserveMoney { get; private set; }
    }

    public sealed class ExecutionPlan
    {
        private readonly ReadOnlyCollection<FrozenTaskPlan> _tasks;

        internal ExecutionPlan(
            int schemaVersion,
            string profileId,
            string displayName,
            string configurationFingerprint,
            CityScope cityScope,
            CityOrder cityOrder,
            bool dryRunByDefault,
            bool previewOnly,
            IEnumerable<FrozenTaskPlan> tasks)
        {
            SchemaVersion = schemaVersion;
            ProfileId = profileId;
            DisplayName = displayName;
            ConfigurationFingerprint = configurationFingerprint;
            CityScope = cityScope;
            CityOrder = cityOrder;
            DryRunByDefault = dryRunByDefault;
            PreviewOnly = previewOnly;
            _tasks = new ReadOnlyCollection<FrozenTaskPlan>(new List<FrozenTaskPlan>(tasks));
        }

        public int SchemaVersion { get; private set; }

        public string ProfileId { get; private set; }

        public string DisplayName { get; private set; }

        public string ConfigurationFingerprint { get; private set; }

        public CityScope CityScope { get; private set; }

        public CityOrder CityOrder { get; private set; }

        public bool DryRunByDefault { get; private set; }

        public bool PreviewOnly { get; private set; }

        /// <summary>
        /// Indicates only that every task is eligible to enter a future
        /// one-command-at-a-time submission validator. It never authorizes
        /// submitting this full execution plan.
        /// </summary>
        public bool EligibleForStepwiseValidation
        {
            get { return !PreviewOnly && _tasks.Count != 0; }
        }

        public ReadOnlyCollection<FrozenTaskPlan> Tasks
        {
            get { return _tasks; }
        }
    }

    public sealed class ExecutionPlanCatalog
    {
        private readonly ReadOnlyCollection<ExecutionPlan> _plans;
        private readonly IDictionary<string, ExecutionPlan> _plansById;

        internal ExecutionPlanCatalog(IEnumerable<ExecutionPlan> plans)
        {
            List<ExecutionPlan> copiedPlans = new List<ExecutionPlan>(plans);
            Dictionary<string, ExecutionPlan> plansById = new Dictionary<string, ExecutionPlan>(StringComparer.Ordinal);
            foreach (ExecutionPlan plan in copiedPlans)
            {
                plansById.Add(plan.ProfileId, plan);
            }

            _plans = new ReadOnlyCollection<ExecutionPlan>(copiedPlans);
            _plansById = plansById;
        }

        public ReadOnlyCollection<ExecutionPlan> Plans
        {
            get { return _plans; }
        }

        public ExecutionPlan GetRequired(string profileId)
        {
            ExecutionPlan plan;
            if (profileId == null || !_plansById.TryGetValue(profileId, out plan))
            {
                throw new KeyNotFoundException("No enabled execution profile has id '" + profileId + "'.");
            }

            return plan;
        }
    }

    public sealed class ExecutionPlanCompiler
    {
        public ExecutionPlanCatalog Compile(DomesticConfiguration configuration)
        {
            ConfigurationValidator.ValidateAndThrow(configuration);
            string configurationFingerprint = ConfigurationFingerprint.Compute(configuration);
            List<ExecutionPlan> plans = new List<ExecutionPlan>();
            foreach (ProfileConfiguration profile in configuration.Profiles)
            {
                if (!profile.Enabled)
                {
                    continue;
                }

                List<FrozenTaskPlan> tasks = new List<FrozenTaskPlan>();
                bool previewOnly = false;
                foreach (TaskConfiguration task in profile.Tasks)
                {
                    if (!task.Enabled)
                    {
                        continue;
                    }

                    int reserveMoney = task.ReserveMoneyOverride.HasValue
                        ? task.ReserveMoneyOverride.Value
                        : configuration.ReserveMoney;
                    tasks.Add(new FrozenTaskPlan(
                        task.Command,
                        task.MinOfficers,
                        task.MaxOfficers,
                        task.RequireExactCount,
                        task.SelectionPolicy,
                        reserveMoney));
                    if (task.SelectionPolicy == SelectionPolicy.VerifiedStatFallback)
                    {
                        previewOnly = true;
                    }
                }

                if (tasks.Count == 0)
                {
                    previewOnly = true;
                }

                plans.Add(new ExecutionPlan(
                    configuration.SchemaVersion,
                    profile.Id,
                    profile.DisplayName,
                    configurationFingerprint,
                    configuration.CityScope,
                    configuration.CityOrder,
                    configuration.DryRunByDefault,
                    previewOnly,
                    tasks));
            }

            return new ExecutionPlanCatalog(plans);
        }
    }
}
