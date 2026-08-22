using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using San9AutoDomestic.Adapter.San9Pk101;

namespace San9AutoDomestic.Input.Win32
{
    public sealed class AllDirectCitiesDomesticPlanRequest
    {
        public AllDirectCitiesDomesticPlanRequest(
            Guid authorizationId,
            IEnumerable<DomesticCommandKind> commands,
            string evidenceDirectory,
            int expectedProcessId,
            long expectedProcessCreationFileTimeUtc)
        {
            if (authorizationId == Guid.Empty) throw new ArgumentException("Authorization ID must be non-empty.", "authorizationId");
            if (commands == null) throw new ArgumentNullException("commands");
            DomesticCommandKind[] values = commands.ToArray();
            if (values.Length < 1 || values.Length > 5)
                throw new ArgumentException("A plan must contain between one and five commands.", "commands");
            if (values.Any(command => !DomesticExecutionCoordinates.IsSupported(command)))
                throw new ArgumentOutOfRangeException("commands");
            if (string.IsNullOrWhiteSpace(evidenceDirectory))
                throw new ArgumentException("Evidence directory is required.", "evidenceDirectory");
            if (expectedProcessId <= 0) throw new ArgumentOutOfRangeException("expectedProcessId");
            if (expectedProcessCreationFileTimeUtc <= 0)
                throw new ArgumentOutOfRangeException("expectedProcessCreationFileTimeUtc");

            AuthorizationId = authorizationId;
            Commands = new ReadOnlyCollection<DomesticCommandKind>(values);
            EvidenceDirectory = Path.GetFullPath(evidenceDirectory);
            ExpectedProcessId = expectedProcessId;
            ExpectedProcessCreationFileTimeUtc = expectedProcessCreationFileTimeUtc;
        }

        public Guid AuthorizationId { get; private set; }
        public ReadOnlyCollection<DomesticCommandKind> Commands { get; private set; }
        public string EvidenceDirectory { get; private set; }
        public int ExpectedProcessId { get; private set; }
        public long ExpectedProcessCreationFileTimeUtc { get; private set; }
    }

    public sealed class DirectCityDomesticPlanItemResult
    {
        internal DirectCityDomesticPlanItemResult(
            int cityId,
            DomesticExecutionStatus status,
            string code,
            string message,
            SingleCityDomesticPlanResult cityPlanResult)
        {
            CityId = cityId;
            Status = status;
            Code = code ?? string.Empty;
            Message = message ?? string.Empty;
            CityPlanResult = cityPlanResult;
        }

        public int CityId { get; private set; }
        public DomesticExecutionStatus Status { get; private set; }
        public string Code { get; private set; }
        public string Message { get; private set; }
        public SingleCityDomesticPlanResult CityPlanResult { get; private set; }
    }

    public sealed class AllDirectCitiesDomesticPlanResult
    {
        internal AllDirectCitiesDomesticPlanResult(
            AllDirectCitiesDomesticPlanRequest request,
            DomesticExecutionStatus status,
            string code,
            string message,
            IList<int> directCityIds,
            IList<DirectCityDomesticPlanItemResult> cityResults,
            IList<DomesticExecutionEvidence> navigationEvidence,
            int inputActionCount)
        {
            AuthorizationId = request.AuthorizationId;
            Commands = request.Commands;
            Status = status;
            Code = code ?? string.Empty;
            Message = message ?? string.Empty;
            DirectCityIds = new ReadOnlyCollection<int>(directCityIds.ToArray());
            CityResults = new ReadOnlyCollection<DirectCityDomesticPlanItemResult>(cityResults.ToArray());
            NavigationEvidence = new ReadOnlyCollection<DomesticExecutionEvidence>(navigationEvidence.ToArray());
            InputActionCount = inputActionCount;
        }

        public Guid AuthorizationId { get; private set; }
        public ReadOnlyCollection<DomesticCommandKind> Commands { get; private set; }
        public DomesticExecutionStatus Status { get; private set; }
        public string Code { get; private set; }
        public string Message { get; private set; }
        public ReadOnlyCollection<int> DirectCityIds { get; private set; }
        public ReadOnlyCollection<DirectCityDomesticPlanItemResult> CityResults { get; private set; }
        public ReadOnlyCollection<DomesticExecutionEvidence> NavigationEvidence { get; private set; }
        public int InputActionCount { get; private set; }
    }
}
