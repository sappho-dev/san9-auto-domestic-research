using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using San9AutoDomestic.Core.Configuration;
using San9AutoDomestic.Core.Domain;

namespace San9AutoDomestic.Core.Planning
{
    public enum ScopeSkipReason
    {
        None = 0,
        NonCityFacility = 1,
        NotOwnedByPlayer = 2,
        DelegatedCity = 3,
        UnknownFacilityType = 4
    }

    public sealed class ScopeSkip
    {
        public ScopeSkip(int facilityId, string facilityName, ScopeSkipReason reason, string detail)
        {
            FacilityId = facilityId;
            FacilityName = facilityName ?? string.Empty;
            Reason = reason;
            Detail = detail ?? string.Empty;
        }

        public int FacilityId { get; private set; }

        public string FacilityName { get; private set; }

        public ScopeSkipReason Reason { get; private set; }

        public string Detail { get; private set; }
    }

    public sealed class CityQueueSnapshot
    {
        private readonly ReadOnlyCollection<int> _cityIds;
        private readonly ReadOnlyCollection<ScopeSkip> _scopeSkips;

        private CityQueueSnapshot(
            BatchContext context,
            IEnumerable<int> cityIds,
            IEnumerable<ScopeSkip> scopeSkips)
        {
            Context = context;
            _cityIds = new ReadOnlyCollection<int>(new List<int>(cityIds));
            _scopeSkips = new ReadOnlyCollection<ScopeSkip>(new List<ScopeSkip>(scopeSkips));
        }

        public BatchContext Context { get; private set; }

        public ReadOnlyCollection<int> CityIds
        {
            get { return _cityIds; }
        }

        public ReadOnlyCollection<ScopeSkip> ScopeSkips
        {
            get { return _scopeSkips; }
        }

        public CommandObservationRequest CreateCommandObservationRequest(
            ExecutionPlan plan,
            int cityId,
            DomesticCommand command)
        {
            if (plan == null)
            {
                throw new ArgumentNullException("plan");
            }

            if (!string.Equals(Context.ProfileId, plan.ProfileId, StringComparison.Ordinal)
                || !string.Equals(
                    Context.ConfigurationFingerprint,
                    plan.ConfigurationFingerprint,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The plan does not match this captured city queue.");
            }

            if (!_cityIds.Contains(cityId))
            {
                throw new ArgumentOutOfRangeException(
                    "cityId",
                    "A command observation can only target a city in this queue.");
            }

            if (!plan.Tasks.Any(item => item.Command == command))
            {
                throw new ArgumentOutOfRangeException(
                    "command",
                    "A command observation can only target a command in the frozen plan.");
            }

            if (!Context.InitialObservationTrusted
                || !Context.InitialPlanningComplete
                || Context.InitialReadiness != SnapshotReadiness.PlanningReady
                || !Context.ProcessId.HasValue
                || !Context.ProcessStartUtcTicks.HasValue
                || !Context.InitialSnapshotGeneration.HasValue
                || Context.ScenarioToken == null
                || !Context.ScenarioToken.IsVerified
                || Context.TurnToken == null
                || !Context.TurnToken.IsVerified
                || Context.PhaseToken == null
                || !Context.PhaseToken.IsVerified)
            {
                throw new InvalidOperationException(
                    "A command re-read request requires a complete trusted snapshot with verified game tokens.");
            }

            return new CommandObservationRequest(Context, cityId, command);
        }

        public static CityQueueSnapshot Capture(ExecutionPlan plan, GameSnapshot snapshot)
        {
            if (plan == null)
            {
                throw new ArgumentNullException("plan");
            }

            if (snapshot == null)
            {
                throw new ArgumentNullException("snapshot");
            }

            if (plan.CityScope != CityScope.DirectCities || plan.CityOrder != CityOrder.GameIdAscending)
            {
                throw new NotSupportedException("The planner only supports direct cities ordered by game id.");
            }

            List<int> cityIds = new List<int>();
            List<ScopeSkip> skips = new List<ScopeSkip>();
            foreach (FacilitySnapshot facility in snapshot.Facilities.OrderBy(item => item.Id))
            {
                if (facility.FacilityType == FacilityType.Unknown)
                {
                    skips.Add(new ScopeSkip(
                        facility.Id,
                        facility.Name,
                        ScopeSkipReason.UnknownFacilityType,
                        "Facility type could not be verified."));
                }
                else if (facility.FacilityType != FacilityType.City)
                {
                    skips.Add(new ScopeSkip(
                        facility.Id,
                        facility.Name,
                        ScopeSkipReason.NonCityFacility,
                        "Facility type is not city."));
                }
                else if (facility.OwnerForceId != snapshot.PlayerForceId)
                {
                    skips.Add(new ScopeSkip(
                        facility.Id,
                        facility.Name,
                        ScopeSkipReason.NotOwnedByPlayer,
                        "City is not owned by the player force."));
                }
                else if (!facility.IsDirectlyControlled)
                {
                    skips.Add(new ScopeSkip(
                        facility.Id,
                        facility.Name,
                        ScopeSkipReason.DelegatedCity,
                        "City belongs to a delegated corps."));
                }
                else
                {
                    cityIds.Add(facility.Id);
                }
            }

            return new CityQueueSnapshot(new BatchContext(plan, snapshot), cityIds, skips);
        }
    }
}
