using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using San9AutoDomestic.Adapter.San9Pk101;

namespace San9AutoDomestic.Input.Win32
{
    internal interface IDomesticExecutionObservationSource
    {
        DomesticUiSnapshot CaptureUi();
        DomesticAvailabilitySnapshot CaptureAvailability(DomesticCommandKind command, int cityId);
    }

    internal interface IDomesticDirectCityQueueSource
    {
        DomesticDirectCityQueueSnapshot CaptureDirectCityQueue();
    }

    internal sealed class AdapterDomesticExecutionObservationSource : IDomesticExecutionObservationSource, IDomesticDirectCityQueueSource
    {
        private readonly San9Pk101Adapter adapter = new San9Pk101Adapter();

        public DomesticUiSnapshot CaptureUi()
        {
            return DomesticUiSnapshot.FromReport(adapter.ReadUiObservation());
        }

        public DomesticAvailabilitySnapshot CaptureAvailability(DomesticCommandKind command, int cityId)
        {
            return DomesticAvailabilitySnapshot.FromReport(adapter.ReadAvailability(), command, cityId);
        }

        public DomesticDirectCityQueueSnapshot CaptureDirectCityQueue()
        {
            return DomesticDirectCityQueueSnapshot.FromReport(adapter.ReadAvailability());
        }
    }

    internal sealed class DomesticDirectCityQueueSnapshot
    {
        internal DomesticDirectCityQueueSnapshot()
        {
            Error = string.Empty;
            CityIds = new int[0];
        }

        internal bool IsValid;
        internal string Error;
        internal int ProcessId;
        internal long ProcessCreationFileTimeUtc;
        internal int[] CityIds;

        internal static DomesticDirectCityQueueSnapshot FromReport(San9Pk101AvailabilityReport report)
        {
            DomesticDirectCityQueueSnapshot value = new DomesticDirectCityQueueSnapshot();
            if (report == null) return Invalid(value, "No availability report was returned.");
            if (!report.DataReadSucceeded || report.ObservationBlocked || !report.RawFieldsWereStable
                || !report.CodeAnchorsWereStableAndExact || !report.StructureWasRevalidated
                || !report.ProcessIdentityRevalidatedAfterObservation)
                return Invalid(value, "The direct-city queue did not pass all exact/stable read gates.");
            if (report.Baseline == null || !report.Baseline.ExecutionAllowed)
                return Invalid(value, "The exact target/Easy compatibility gate did not authorize the direct-city queue.");
            if (report.StructureBefore == null || !report.StructureBefore.ProcessId.HasValue
                || !report.StructureBefore.ProcessCreationFileTimeUtc.HasValue)
                return Invalid(value, "The direct-city queue process generation is missing.");

            value.ProcessId = report.StructureBefore.ProcessId.Value;
            value.ProcessCreationFileTimeUtc = report.StructureBefore.ProcessCreationFileTimeUtc.Value;
            CityAvailabilityObservation[] directCities = report.Cities == null
                ? new CityAvailabilityObservation[0]
                : report.Cities.Where(city => city != null && city.IsDirectlyControlled).ToArray();
            if (directCities.Any(city => city.CityId < 0 || city.CityId >= 50)
                || directCities.Select(city => city.CityId).Distinct().Count() != directCities.Length)
                return Invalid(value, "The direct-city queue contains an invalid or duplicate stable city ID.");
            value.CityIds = directCities.Select(city => city.CityId).OrderBy(cityId => cityId).ToArray();
            if (value.ProcessId <= 0 || value.ProcessCreationFileTimeUtc <= 0)
                return Invalid(value, "The direct-city queue process binding is invalid.");
            if (value.CityIds.Length == 0)
                return Invalid(value, "No directly controlled CITY records were observed.");
            value.IsValid = true;
            return value;
        }

        private static DomesticDirectCityQueueSnapshot Invalid(DomesticDirectCityQueueSnapshot value, string error)
        {
            value.IsValid = false;
            value.Error = error ?? string.Empty;
            return value;
        }
    }

    internal sealed class DomesticUiSnapshot
    {
        internal DomesticUiSnapshot()
        {
            Error = string.Empty;
            WindowObservationToken = string.Empty;
            HoveredCommandState = UiObservationState.Unknown;
            HoveredCommand = string.Empty;
            OuterCommandName = string.Empty;
            CandidateOfficerIds = new int[0];
            SelectedOfficerIds = new int[0];
            WorkingOfficerIds = new int[0];
        }

        internal bool IsValid;
        internal string Error;
        internal DateTimeOffset CapturedUtc;
        internal int ProcessId;
        internal long ProcessCreationFileTimeUtc;
        internal long MainWindowHandle;
        internal UiLayerKind Layer;
        internal int? VerifiedCityId;
        internal int? DomesticTargetCityId;
        internal string WindowObservationToken;
        internal UiObservationState HoveredCommandState;
        internal string HoveredCommand;
        internal bool HoveredCommandVerified { get { return HoveredCommandState == UiObservationState.Verified; } }
        internal string OuterCommandName;
        internal int[] CandidateOfficerIds;
        internal int[] SelectedOfficerIds;
        internal int[] WorkingOfficerIds;

        internal static DomesticUiSnapshot FromReport(San9Pk101UiObservationReport report)
        {
            DomesticUiSnapshot value = new DomesticUiSnapshot();
            if (report == null) return Invalid(value, "No UI report was returned.");
            value.CapturedUtc = report.CapturedUtc;
            value.Layer = report.Layer;
            value.ProcessId = report.ProcessId ?? 0;
            value.ProcessCreationFileTimeUtc = report.ProcessCreationFileTimeUtc ?? 0;
            value.MainWindowHandle = report.MainWindowHandle ?? 0;
            value.WindowObservationToken = report.WindowObservationToken ?? string.Empty;
            value.CandidateOfficerIds = Copy(report.CandidateOfficerIds);
            value.SelectedOfficerIds = Copy(report.SelectedOfficerIds);
            value.WorkingOfficerIds = Copy(report.WorkingOfficerIds);
            value.VerifiedCityId = ParseVerifiedInt(report, "VerifiedCurrentCityId");
            value.DomesticTargetCityId = ParseVerifiedInt(report, "DomesticTargetCityId");
            value.HoveredCommandState = report.HoveredCommandState;
            value.HoveredCommand = report.HoveredCommand ?? string.Empty;
            UiObservationField outer = FindField(report, "DomesticOuterDialog");
            value.OuterCommandName = outer == null ? string.Empty : BeforeAt(outer.Value);

            if (!report.ReadSucceeded || !report.StableAbc) return Invalid(value, "The UI observation was not a stable A/B/C read.");
            if (report.Baseline == null || !report.Baseline.ExecutionAllowed)
                return Invalid(value, "The exact target/Easy compatibility gate did not authorize UI observation.");
            if (report.Issues != null && report.Issues.Any(issue => issue != null && issue.Severity == DiagnosticSeverity.Blocking))
                return Invalid(value, "The UI observation contains a blocking issue.");
            if (value.ProcessId <= 0 || value.ProcessCreationFileTimeUtc <= 0 || value.MainWindowHandle <= 0)
                return Invalid(value, "The UI process binding is incomplete.");
            if (string.IsNullOrEmpty(value.WindowObservationToken)) return Invalid(value, "The UI observation token is missing.");
            value.IsValid = true;
            return value;
        }

        private static DomesticUiSnapshot Invalid(DomesticUiSnapshot value, string error)
        {
            value.IsValid = false;
            value.Error = error ?? string.Empty;
            return value;
        }

        private static int[] Copy(int[] source)
        {
            return source == null ? new int[0] : source.ToArray();
        }

        private static UiObservationField FindField(San9Pk101UiObservationReport report, string name)
        {
            return report.Fields == null
                ? null
                : report.Fields.FirstOrDefault(field => field != null && string.Equals(field.Name, name, StringComparison.Ordinal));
        }

        private static int? ParseVerifiedInt(San9Pk101UiObservationReport report, string name)
        {
            UiObservationField field = FindField(report, name);
            int parsed;
            return field != null && field.State == UiObservationState.Verified
                && int.TryParse(field.Value, NumberStyles.None, CultureInfo.InvariantCulture, out parsed)
                    ? (int?)parsed : null;
        }

        private static string BeforeAt(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            int index = value.IndexOf('@');
            return index < 0 ? value : value.Substring(0, index);
        }

    }

    internal sealed class DomesticAvailabilitySnapshot
    {
        internal DomesticAvailabilitySnapshot()
        {
            Error = string.Empty;
            ReadyCandidatesInSourceOrder = new int[0];
            RankedCandidates = new int[0];
        }

        internal bool IsValid;
        internal string Error;
        internal int ProcessId;
        internal long ProcessCreationFileTimeUtc;
        internal int CityId;
        internal int CorpsMoney;
        internal bool IsDirectlyControlled;
        internal int CostPerOfficer;
        internal bool KnownStaticSubsetWouldPass;
        internal bool? OrderBitClearPassed;
        internal int[] ReadyCandidatesInSourceOrder;
        internal int[] RankedCandidates;

        internal static DomesticAvailabilitySnapshot FromReport(
            San9Pk101AvailabilityReport report,
            DomesticCommandKind command,
            int cityId)
        {
            DomesticAvailabilitySnapshot value = new DomesticAvailabilitySnapshot { CityId = cityId };
            if (report == null) return Invalid(value, "No availability report was returned.");
            if (!report.DataReadSucceeded || report.ObservationBlocked || !report.RawFieldsWereStable
                || !report.CodeAnchorsWereStableAndExact || !report.StructureWasRevalidated
                || !report.ProcessIdentityRevalidatedAfterObservation)
                return Invalid(value, "The availability report did not pass all exact/stable read gates.");
            if (report.Baseline == null || !report.Baseline.ExecutionAllowed)
                return Invalid(value, "The exact target/Easy compatibility gate did not authorize availability observation.");
            if (report.StructureBefore == null || !report.StructureBefore.ProcessId.HasValue
                || !report.StructureBefore.ProcessCreationFileTimeUtc.HasValue)
                return Invalid(value, "The availability process generation is missing.");

            value.ProcessId = report.StructureBefore.ProcessId.Value;
            value.ProcessCreationFileTimeUtc = report.StructureBefore.ProcessCreationFileTimeUtc.Value;

            CityAvailabilityObservation city = report.Cities == null
                ? null : report.Cities.FirstOrDefault(item => item != null && item.CityId == cityId);
            if (city == null)
            {
                value.IsDirectlyControlled = false;
                value.IsValid = value.ProcessId > 0 && value.ProcessCreationFileTimeUtc > 0;
                value.Error = value.IsValid ? string.Empty : "The availability process binding is invalid.";
                return value;
            }
            CommandAvailabilityObservation task = city.Commands == null
                ? null : city.Commands.FirstOrDefault(item => item != null && item.Command == command);
            if (task == null) return Invalid(value, "The requested command was not present in the availability report.");

            value.CorpsMoney = city.CorpsMoney;
            value.IsDirectlyControlled = city.IsDirectlyControlled;
            value.CostPerOfficer = task.ProvisionalCostPerOfficer;
            value.KnownStaticSubsetWouldPass = task.KnownStaticSubsetWouldPass;
            AvailabilityCondition order = task.Conditions == null
                ? null : task.Conditions.FirstOrDefault(item => item != null && string.Equals(item.Code, "COMMAND_ORDER_BIT_CLEAR", StringComparison.Ordinal));
            value.OrderBitClearPassed = order == null ? null : order.Passed;
            value.ReadyCandidatesInSourceOrder = task.ReadyCandidatesInSourceOrder == null
                ? new int[0]
                : task.ReadyCandidatesInSourceOrder.Where(item => item != null).Select(item => item.PersonId).ToArray();
            value.RankedCandidates = task.RankedCandidates == null
                ? new int[0]
                : task.RankedCandidates.Where(item => item != null).Select(item => item.PersonId).ToArray();
            if (value.ProcessId <= 0 || value.ProcessCreationFileTimeUtc <= 0)
                return Invalid(value, "The availability process binding is invalid.");
            value.IsValid = true;
            return value;
        }

        private static DomesticAvailabilitySnapshot Invalid(DomesticAvailabilitySnapshot value, string error)
        {
            value.IsValid = false;
            value.Error = error ?? string.Empty;
            return value;
        }
    }
}
