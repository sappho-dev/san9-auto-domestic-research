using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace San9AutoDomestic.Core.Configuration
{
    public sealed class DomesticConfiguration
    {
        private readonly ReadOnlyCollection<ProfileConfiguration> _profiles;

        public DomesticConfiguration(
            int schemaVersion,
            IEnumerable<ProfileConfiguration> profiles,
            CityScope cityScope,
            CityOrder cityOrder,
            int reserveMoney,
            bool dryRunByDefault)
        {
            if (profiles == null)
            {
                throw new ArgumentNullException("profiles");
            }

            SchemaVersion = schemaVersion;
            _profiles = new ReadOnlyCollection<ProfileConfiguration>(new List<ProfileConfiguration>(profiles));
            CityScope = cityScope;
            CityOrder = cityOrder;
            ReserveMoney = reserveMoney;
            DryRunByDefault = dryRunByDefault;
        }

        public int SchemaVersion { get; private set; }

        public ReadOnlyCollection<ProfileConfiguration> Profiles
        {
            get { return _profiles; }
        }

        public CityScope CityScope { get; private set; }

        public CityOrder CityOrder { get; private set; }

        public int ReserveMoney { get; private set; }

        public bool DryRunByDefault { get; private set; }
    }

    public sealed class ProfileConfiguration
    {
        private readonly ReadOnlyCollection<TaskConfiguration> _tasks;

        public ProfileConfiguration(
            string id,
            string displayName,
            bool enabled,
            IEnumerable<TaskConfiguration> tasks)
        {
            if (tasks == null)
            {
                throw new ArgumentNullException("tasks");
            }

            Id = id;
            DisplayName = displayName;
            Enabled = enabled;
            _tasks = new ReadOnlyCollection<TaskConfiguration>(new List<TaskConfiguration>(tasks));
        }

        public string Id { get; private set; }

        public string DisplayName { get; private set; }

        public bool Enabled { get; private set; }

        public ReadOnlyCollection<TaskConfiguration> Tasks
        {
            get { return _tasks; }
        }
    }

    public sealed class TaskConfiguration
    {
        public TaskConfiguration(
            DomesticCommand command,
            bool enabled,
            int minOfficers,
            int maxOfficers,
            bool requireExactCount,
            SelectionPolicy selectionPolicy,
            int? reserveMoneyOverride)
        {
            Command = command;
            Enabled = enabled;
            MinOfficers = minOfficers;
            MaxOfficers = maxOfficers;
            RequireExactCount = requireExactCount;
            SelectionPolicy = selectionPolicy;
            ReserveMoneyOverride = reserveMoneyOverride;
        }

        public DomesticCommand Command { get; private set; }

        public bool Enabled { get; private set; }

        public int MinOfficers { get; private set; }

        public int MaxOfficers { get; private set; }

        public bool RequireExactCount { get; private set; }

        public SelectionPolicy SelectionPolicy { get; private set; }

        public int? ReserveMoneyOverride { get; private set; }
    }
}
