using System;
using San9AutoDomestic.Adapter.San9Pk101;

namespace San9AutoDomestic.V4RootTrace
{
    internal sealed class RootTraceEnvironmentStamp
    {
        internal int ProcessId;
        internal long ProcessCreationFileTimeUtc;
        internal uint MainModuleBaseAddress;
        internal uint MainModuleSize;

        internal static bool TryCreate(
            San9Pk101DiagnosticReport report,
            out RootTraceEnvironmentStamp stamp,
            out string error)
        {
            stamp = null;
            if (!San9Pk101DiagnosticGate.TryValidateExactConflictFreeReport(report, out error))
            {
                return false;
            }

            stamp = new RootTraceEnvironmentStamp
            {
                ProcessId = report.ProcessDiscovery.SelectedProcessId.Value,
                ProcessCreationFileTimeUtc = report.ReadOnlyConnection.ProcessCreationFileTimeUtc.Value,
                MainModuleBaseAddress = report.ReadOnlyConnection.MainModuleBaseAddress.Value,
                MainModuleSize = report.ReadOnlyConnection.MainModuleSize.Value
            };
            return true;
        }

        internal bool SameProcessGenerationAndModule(RootTraceEnvironmentStamp other)
        {
            return other != null
                && ProcessId == other.ProcessId
                && ProcessCreationFileTimeUtc == other.ProcessCreationFileTimeUtc
                && MainModuleBaseAddress == other.MainModuleBaseAddress
                && MainModuleSize == other.MainModuleSize;
        }
    }

    internal sealed class RootTraceSessionBinding
    {
        private readonly RootTraceEnvironmentStamp initial;

        internal RootTraceSessionBinding(RootTraceEnvironmentStamp initialStamp)
        {
            if (initialStamp == null)
            {
                throw new ArgumentNullException("initialStamp");
            }

            initial = initialStamp;
        }

        internal int ProcessId
        {
            get { return initial.ProcessId; }
        }

        internal long ProcessCreationFileTimeUtc
        {
            get { return initial.ProcessCreationFileTimeUtc; }
        }

        internal bool TryMatch(RootTraceEnvironmentStamp candidate, out string error)
        {
            if (!initial.SameProcessGenerationAndModule(candidate))
            {
                error = "The unique target PID, creation generation, or exact main-module identity changed during the V4 session.";
                return false;
            }

            error = null;
            return true;
        }
    }
}
