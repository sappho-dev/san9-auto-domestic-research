using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using San9AutoDomestic.Adapter.San9Pk101;

namespace San9AutoDomestic.Input.Win32
{
    public enum DomesticExecutionStatus
    {
        Completed,
        SkippedUnavailable,
        SkippedFewerThanFive,
        StoppedBeforeCommit,
        RejectedBeforeInput,
        AbortUncertain
    }

    public sealed class DomesticExecutionStopSignal
    {
        private int requested;

        public bool IsStopRequested { get { return Volatile.Read(ref requested) != 0; } }

        public void RequestStop()
        {
            Interlocked.Exchange(ref requested, 1);
        }
    }

    public sealed class SingleCityDomesticExecutionRequest
    {
        public SingleCityDomesticExecutionRequest(
            Guid authorizationId,
            DomesticCommandKind command,
            string evidenceDirectory)
        {
            if (authorizationId == Guid.Empty) throw new ArgumentException("Authorization ID must be non-empty.", "authorizationId");
            if (!DomesticExecutionCoordinates.IsSupported(command)) throw new ArgumentOutOfRangeException("command");
            if (string.IsNullOrWhiteSpace(evidenceDirectory)) throw new ArgumentException("Evidence directory is required.", "evidenceDirectory");

            AuthorizationId = authorizationId;
            Command = command;
            EvidenceDirectory = Path.GetFullPath(evidenceDirectory);
        }

        public Guid AuthorizationId { get; private set; }
        public DomesticCommandKind Command { get; private set; }
        public string EvidenceDirectory { get; private set; }
    }

    public sealed class DomesticExecutionEvidence
    {
        internal DomesticExecutionEvidence(string action, string beforePath, string afterPath)
        {
            Action = action ?? string.Empty;
            BeforeScreenshotPath = beforePath ?? string.Empty;
            AfterScreenshotPath = afterPath ?? string.Empty;
        }

        public string Action { get; private set; }
        public string BeforeScreenshotPath { get; private set; }
        public string AfterScreenshotPath { get; private set; }
    }

    public sealed class SingleCityDomesticExecutionResult
    {
        internal SingleCityDomesticExecutionResult()
        {
            Code = string.Empty;
            Message = string.Empty;
            SelectedOfficerIds = new ReadOnlyCollection<int>(new int[0]);
            Evidence = new ReadOnlyCollection<DomesticExecutionEvidence>(new DomesticExecutionEvidence[0]);
        }

        public DomesticExecutionStatus Status { get; internal set; }
        public string Code { get; internal set; }
        public string Message { get; internal set; }
        public Guid AuthorizationId { get; internal set; }
        public DomesticCommandKind Command { get; internal set; }
        public int? CityId { get; internal set; }
        public int? MoneyBefore { get; internal set; }
        public int? MoneyAfter { get; internal set; }
        public int? ReadyBefore { get; internal set; }
        public int? ReadyAfter { get; internal set; }
        public bool CommitClickAttempted { get; internal set; }
        public int InputActionCount { get; internal set; }
        public ReadOnlyCollection<int> SelectedOfficerIds { get; internal set; }
        public ReadOnlyCollection<DomesticExecutionEvidence> Evidence { get; internal set; }

        internal static SingleCityDomesticExecutionResult Create(
            SingleCityDomesticExecutionRequest request,
            DomesticExecutionStatus status,
            string code,
            string message,
            DomesticExecutionRunState state)
        {
            return new SingleCityDomesticExecutionResult
            {
                Status = status,
                Code = code ?? string.Empty,
                Message = message ?? string.Empty,
                AuthorizationId = request.AuthorizationId,
                Command = request.Command,
                CityId = state.CityId,
                MoneyBefore = state.MoneyBefore,
                MoneyAfter = state.MoneyAfter,
                ReadyBefore = state.ReadyBefore,
                ReadyAfter = state.ReadyAfter,
                CommitClickAttempted = state.CommitClickAttempted,
                InputActionCount = state.InputActionCount,
                SelectedOfficerIds = new ReadOnlyCollection<int>(state.SelectedOfficerIds.ToArray()),
                Evidence = new ReadOnlyCollection<DomesticExecutionEvidence>(state.Evidence.ToArray())
            };
        }
    }
}
