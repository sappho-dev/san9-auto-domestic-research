using System;
using System.IO;
using System.Text;
using System.Threading;

namespace San9AutoDomestic.CommandBroker
{
    public enum CommandBrokerStartStatus
    {
        Started = 1,
        StopRequested = 2,
        LocalSingleFlightBusy = 3,
        ProcessSingleFlightBusy = 4,
        RestartRequired = 5,
        ReplayRejected = 6,
        JournalInUse = 7,
        JournalCorruptRestartRequired = 8,
        BindingMismatch = 9,
        GateFailureRestartRequired = 10,
        JournalFailureRestartRequired = 11,
        Disposed = 12
    }

    public sealed class CommandBrokerStartResult
    {
        internal CommandBrokerStartResult(
            CommandBrokerStartStatus status,
            CommandBrokerOperation operation,
            string errorMessage)
        {
            Status = status;
            Operation = operation;
            ErrorMessage = errorMessage ?? string.Empty;
        }

        public CommandBrokerStartStatus Status { get; private set; }

        public CommandBrokerOperation Operation { get; private set; }

        public string ErrorMessage { get; private set; }
    }

    public enum BrokerStopDisposition
    {
        NoActiveOperation = 1,
        WillStopBeforeSideEffect = 2,
        DeferredUntilTerminal = 3,
        HostDisposed = 4
    }

    public enum BrokerTransitionStatus
    {
        Advanced = 1,
        InvalidState = 2,
        AlreadyClosed = 3,
        StoppedBeforeSideEffect = 4,
        JournalFailureRestartRequired = 5,
        OwnershipReleaseTimeoutRestartRequired = 6,
        OwnershipReleaseFailedRestartRequired = 7
    }

    public sealed class CommandBrokerHost : IDisposable
    {
        private const string JournalPrefix = "S9CB-";
        private const string JournalExtension = ".journal";
        private const string LedgerPrefix = "S9CBL-";
        private const string LedgerExtension = ".ledger";
        private readonly object sync;
        private readonly ProcessSessionIdentity processSession;
        private readonly string journalDirectory;
        private readonly string sessionToken;
        private readonly string ledgerPath;
        private readonly CurrentUserNamedMutex processGate;
        private CommandBrokerOperation activeOperation;
        private int stopRequested;
        private int restartRequired;
        private bool disposed;

        public CommandBrokerHost(ProcessSessionIdentity processSession, string journalDirectory)
            : this(processSession, journalDirectory, 5000, 5000)
        {
        }

        public CommandBrokerHost(
            ProcessSessionIdentity processSession,
            string journalDirectory,
            int mutexReadyTimeoutMilliseconds,
            int mutexReleaseTimeoutMilliseconds)
        {
            if (processSession == null)
            {
                throw new ArgumentNullException("processSession");
            }

            if (string.IsNullOrWhiteSpace(journalDirectory)
                || !Path.IsPathRooted(journalDirectory)
                || !Directory.Exists(journalDirectory))
            {
                throw new ArgumentException(
                    "An existing absolute journal directory is required.",
                    "journalDirectory");
            }

            sync = new object();
            this.processSession = processSession;
            this.journalDirectory = ControlledJournalDirectory.Validate(journalDirectory);
            sessionToken = CurrentUserNamedMutex.CreateSessionToken(processSession);
            ledgerPath = Path.Combine(this.journalDirectory, LedgerPrefix + sessionToken + LedgerExtension);
            processGate = new CurrentUserNamedMutex(
                CurrentUserNamedMutex.CreateName(processSession),
                mutexReadyTimeoutMilliseconds,
                mutexReleaseTimeoutMilliseconds);
        }

        public bool LiveAuthorized
        {
            get { return BrokerSafetyPolicy.LiveAuthorized; }
        }

        public bool IsStopRequested
        {
            get { return Volatile.Read(ref stopRequested) != 0; }
        }

        public bool RestartRequired
        {
            get { return Volatile.Read(ref restartRequired) != 0; }
        }

        public CommandBrokerStartResult TryStart(ExecutionJournalBinding binding)
        {
            if (binding == null)
            {
                throw new ArgumentNullException("binding");
            }

            lock (sync)
            {
                if (disposed)
                {
                    return StartResult(CommandBrokerStartStatus.Disposed, null, string.Empty);
                }

                if (!processSession.Equals(binding.ProcessSession))
                {
                    return StartResult(
                        CommandBrokerStartStatus.BindingMismatch,
                        null,
                        "The journal binding does not match this process session.");
                }

                if (IsStopRequested)
                {
                    return StartResult(CommandBrokerStartStatus.StopRequested, null, string.Empty);
                }

                if (RestartRequired)
                {
                    return StartResult(CommandBrokerStartStatus.RestartRequired, null, "The broker host has latched RestartRequired.");
                }

                if (activeOperation != null)
                {
                    return StartResult(
                        CommandBrokerStartStatus.LocalSingleFlightBusy,
                        null,
                        string.Empty);
                }

                try
                {
                    ControlledJournalDirectory.Validate(journalDirectory);
                }
                catch (Exception exception)
                {
                    MarkRestartRequired();
                    return StartResult(
                        CommandBrokerStartStatus.JournalFailureRestartRequired,
                        null,
                        "The controlled journal directory no longer satisfies its security contract. "
                            + exception.GetType().Name + ": " + exception.Message);
                }

                NamedMutexAcquireResult gateResult = processGate.TryAcquire();
                if (gateResult.Status == NamedMutexAcquireStatus.Busy)
                {
                    return StartResult(
                        CommandBrokerStartStatus.ProcessSingleFlightBusy,
                        null,
                        gateResult.ErrorMessage);
                }

                if (gateResult.Status == NamedMutexAcquireStatus.AbandonedRestartRequired)
                {
                    MarkRestartRequired();
                    return StartResult(
                        CommandBrokerStartStatus.RestartRequired,
                        null,
                        "The process-session mutex was abandoned.");
                }

                if (gateResult.Status != NamedMutexAcquireStatus.Acquired || gateResult.Lease == null)
                {
                    MarkRestartRequired();
                    return StartResult(
                        CommandBrokerStartStatus.GateFailureRestartRequired,
                        null,
                        gateResult.ErrorMessage);
                }

                NamedMutexLease lease = gateResult.Lease;
                string journalPath = GetJournalPath(binding);
                SessionJournalScanResult scan = ScanSessionJournals(binding, journalPath, false);
                if (scan.Status != SessionJournalScanStatus.Clear)
                {
                    if (StartStatusRequiresRestart(scan.StartStatus))
                    {
                        MarkRestartRequired();
                    }

                    NamedMutexReleaseStatus release = lease.ReleaseDefault();
                    if (ReleaseStatusRequiresRestart(release))
                    {
                        MarkRestartRequired();
                        return StartResult(
                            CommandBrokerStartStatus.GateFailureRestartRequired,
                            null,
                            "Journal scan failed and mutex release did not complete safely (" + release + ").");
                    }

                    return StartResult(scan.StartStatus, null, scan.ErrorMessage);
                }

                JournalCreateResult journalResult = ExecutionJournal.TryCreate(journalPath, binding);
                if (journalResult.Status != JournalCreateStatus.Created || journalResult.Journal == null)
                {
                    CommandBrokerStartResult mapped = MapJournalCreateFailure(journalResult);
                    if (StartStatusRequiresRestart(mapped.Status))
                    {
                        MarkRestartRequired();
                    }

                    NamedMutexReleaseStatus release = lease.ReleaseDefault();
                    if (ReleaseStatusRequiresRestart(release))
                    {
                        MarkRestartRequired();
                        return StartResult(
                            CommandBrokerStartStatus.GateFailureRestartRequired,
                            null,
                            "Journal creation failed and mutex release did not complete safely (" + release + ").");
                    }

                    return mapped;
                }

                try
                {
                    ControlledJournalDirectory.ValidateControlledFile(journalPath);
                }
                catch (Exception exception)
                {
                    journalResult.Journal.Dispose();
                    MarkRestartRequired();
                    NamedMutexReleaseStatus release = lease.ReleaseDefault();
                    return StartResult(
                        CommandBrokerStartStatus.JournalFailureRestartRequired,
                        null,
                        "The new journal failed its owner/ACL contract. " + exception.GetType().Name
                            + ": " + exception.Message + " Release=" + release + ".");
                }

                string ledgerError;
                SessionLedgerAppendStatus ledgerAppend = SessionLedger.TryAppend(
                    ledgerPath,
                    binding,
                    Path.GetFileName(journalPath),
                    out ledgerError);
                if (ledgerAppend != SessionLedgerAppendStatus.Appended)
                {
                    journalResult.Journal.Dispose();
                    MarkRestartRequired();
                    NamedMutexReleaseStatus release = lease.ReleaseDefault();
                    string suffix = ReleaseStatusRequiresRestart(release)
                        ? " Mutex release also requires restart (" + release + ")."
                        : string.Empty;
                    return StartResult(
                        CommandBrokerStartStatus.JournalFailureRestartRequired,
                        null,
                        "The canonical session ledger could not register the journal. " + ledgerError + suffix);
                }

                SessionJournalScanResult postRegistration = ScanSessionJournals(binding, journalPath, true);
                if (postRegistration.Status != SessionJournalScanStatus.Clear)
                {
                    journalResult.Journal.Dispose();
                    MarkRestartRequired();
                    NamedMutexReleaseStatus release = lease.ReleaseDefault();
                    string suffix = ReleaseStatusRequiresRestart(release)
                        ? " Mutex release also requires restart (" + release + ")."
                        : string.Empty;
                    return StartResult(
                        CommandBrokerStartStatus.JournalFailureRestartRequired,
                        null,
                        "Post-registration ledger verification failed. " + postRegistration.ErrorMessage + suffix);
                }

                CommandBrokerOperation operation = new CommandBrokerOperation(
                    this,
                    journalResult.Journal,
                    lease,
                    journalPath);
                activeOperation = operation;
                return StartResult(CommandBrokerStartStatus.Started, operation, string.Empty);
            }
        }

        public BrokerStopDisposition RequestStop()
        {
            Interlocked.Exchange(ref stopRequested, 1);
            CommandBrokerOperation captured;
            lock (sync)
            {
                if (disposed)
                {
                    return BrokerStopDisposition.HostDisposed;
                }

                captured = activeOperation;
            }

            return captured == null
                ? BrokerStopDisposition.NoActiveOperation
                : captured.GetStopDisposition();
        }

        public void Dispose()
        {
            CommandBrokerOperation captured;
            lock (sync)
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
                Interlocked.Exchange(ref stopRequested, 1);
                captured = activeOperation;
            }

            if (captured != null)
            {
                captured.Dispose();
            }
        }

        internal void OperationClosed(CommandBrokerOperation operation)
        {
            lock (sync)
            {
                if (object.ReferenceEquals(activeOperation, operation))
                {
                    activeOperation = null;
                }
            }
        }

        internal void MarkRestartRequired()
        {
            Interlocked.Exchange(ref restartRequired, 1);
        }

        private string GetJournalPath(ExecutionJournalBinding binding)
        {
            byte[] operationDigest;
            using (MemoryStream memory = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(memory, Encoding.UTF8))
            {
                writer.Write(binding.GetRequestFingerprintBytes());
                writer.Write(binding.StageOrdinal);
                writer.Write(binding.ConsentId.ToByteArray());
                writer.Flush();
                operationDigest = BinaryValue.ComputeSha256(memory.ToArray());
            }

            string fileName = JournalPrefix
                + sessionToken
                + "-"
                + BinaryValue.ToHex(operationDigest)
                + JournalExtension;
            return Path.Combine(journalDirectory, fileName);
        }

        private SessionJournalScanResult ScanSessionJournals(
            ExecutionJournalBinding requestedBinding,
            string requestedPath,
            bool allowRequestedJournalInUse)
        {
            string artifactPattern = JournalPrefix + sessionToken + "-*";
            string[] artifacts;
            try
            {
                artifacts = Directory.GetFileSystemEntries(journalDirectory, artifactPattern, SearchOption.TopDirectoryOnly);
                Array.Sort(artifacts, StringComparer.OrdinalIgnoreCase);
                foreach (string artifact in artifacts)
                {
                    if ((File.GetAttributes(artifact) & FileAttributes.ReparsePoint) != 0)
                    {
                        return SessionJournalScanResult.Failure(
                            CommandBrokerStartStatus.JournalCorruptRestartRequired,
                            "Reparse-point session artifacts are forbidden.");
                    }
                }
            }
            catch (Exception exception)
            {
                return SessionJournalScanResult.Failure(
                    CommandBrokerStartStatus.JournalFailureRestartRequired,
                    exception.GetType().Name + ": " + exception.Message);
            }

            SessionLedgerInspection ledger = SessionLedger.Inspect(ledgerPath);
            if (ledger.Status == SessionLedgerStatus.Missing)
            {
                return artifacts.Length == 0
                    ? SessionJournalScanResult.Clear()
                    : SessionJournalScanResult.Failure(
                        CommandBrokerStartStatus.JournalCorruptRestartRequired,
                        "Session artifacts exist without the canonical session ledger.");
            }

            if (ledger.Status == SessionLedgerStatus.InUse)
            {
                return SessionJournalScanResult.Failure(CommandBrokerStartStatus.JournalInUse, "The canonical session ledger is in use.");
            }

            if (ledger.Status != SessionLedgerStatus.Valid)
            {
                return SessionJournalScanResult.Failure(
                    CommandBrokerStartStatus.JournalCorruptRestartRequired,
                    "The canonical session ledger is corrupt. " + ledger.ErrorMessage);
            }

            try
            {
                ControlledJournalDirectory.ValidateControlledFile(ledgerPath);
            }
            catch (Exception exception)
            {
                return SessionJournalScanResult.Failure(
                    CommandBrokerStartStatus.JournalCorruptRestartRequired,
                    "The canonical ledger failed its owner/ACL contract. "
                        + exception.GetType().Name + ": " + exception.Message);
            }

            System.Collections.Generic.HashSet<string> registeredPaths =
                new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (SessionLedgerEntry ledgerEntry in ledger.Entries)
            {
                if (!processSession.Equals(ledgerEntry.Binding.ProcessSession))
                {
                    return SessionJournalScanResult.Failure(
                        CommandBrokerStartStatus.JournalCorruptRestartRequired,
                        "A canonical ledger entry is bound to another process session.");
                }

                string path = GetJournalPath(ledgerEntry.Binding);
                if (!string.Equals(Path.GetFileName(path), ledgerEntry.JournalFileName, StringComparison.Ordinal)
                    || !registeredPaths.Add(path))
                {
                    return SessionJournalScanResult.Failure(
                        CommandBrokerStartStatus.JournalCorruptRestartRequired,
                        "A canonical ledger entry disagrees with its binding-derived journal path.");
                }

                if (File.Exists(path))
                {
                    try
                    {
                        ControlledJournalDirectory.ValidateControlledFile(path);
                    }
                    catch (Exception exception)
                    {
                        return SessionJournalScanResult.Failure(
                            CommandBrokerStartStatus.JournalCorruptRestartRequired,
                            "A registered journal failed its owner/ACL contract. "
                                + exception.GetType().Name + ": " + exception.Message);
                    }
                }

                JournalInspection inspection = ExecutionJournalInspector.Inspect(path);
                switch (inspection.Status)
                {
                    case JournalInspectionStatus.InUse:
                        if (allowRequestedJournalInUse
                            && string.Equals(path, requestedPath, StringComparison.OrdinalIgnoreCase))
                        {
                            break;
                        }

                        return SessionJournalScanResult.Failure(
                            CommandBrokerStartStatus.JournalInUse,
                            "A process-session journal is already in use.");
                    case JournalInspectionStatus.CorruptRestartRequired:
                    case JournalInspectionStatus.Missing:
                        return SessionJournalScanResult.Failure(
                            CommandBrokerStartStatus.JournalCorruptRestartRequired,
                            "A process-session journal is missing or corrupt.");
                    case JournalInspectionStatus.ValidNonterminal:
                        return SessionJournalScanResult.Failure(
                            CommandBrokerStartStatus.RestartRequired,
                            "A nonterminal process-session journal requires restart.");
                    case JournalInspectionStatus.ValidTerminal:
                        if (inspection.Binding == null
                            || !processSession.Equals(inspection.Binding.ProcessSession)
                            || !ledgerEntry.Binding.Equals(inspection.Binding))
                        {
                            return SessionJournalScanResult.Failure(
                                CommandBrokerStartStatus.JournalCorruptRestartRequired,
                                "A journal filename and persisted session binding disagree.");
                        }

                        string canonicalPersistedPath = GetJournalPath(inspection.Binding);
                        if (!string.Equals(path, canonicalPersistedPath, StringComparison.OrdinalIgnoreCase))
                        {
                            return SessionJournalScanResult.Failure(
                                CommandBrokerStartStatus.JournalCorruptRestartRequired,
                                "A journal was renamed or copied away from its canonical binding path.");
                        }

                        if (inspection.LastState == ExecutionJournalState.AbortUncertain)
                        {
                            return SessionJournalScanResult.Failure(
                                CommandBrokerStartStatus.RestartRequired,
                                "AbortUncertain is latched for this process session.");
                        }

                        if (SessionLedger.HasSameReplayIdentity(ledgerEntry.Binding, requestedBinding))
                        {
                            return SessionJournalScanResult.Failure(
                                CommandBrokerStartStatus.ReplayRejected,
                                "The exact operation already completed.");
                        }

                        break;
                    default:
                        return SessionJournalScanResult.Failure(
                            CommandBrokerStartStatus.JournalCorruptRestartRequired,
                            "Unknown journal inspection status.");
                }
            }

            foreach (string artifact in artifacts)
            {
                if (!registeredPaths.Contains(artifact))
                {
                    return SessionJournalScanResult.Failure(
                        CommandBrokerStartStatus.JournalCorruptRestartRequired,
                        "An unregistered process-session artifact exists beside the canonical ledger.");
                }
            }

            return SessionJournalScanResult.Clear();
        }

        private static CommandBrokerStartResult MapJournalCreateFailure(JournalCreateResult result)
        {
            switch (result.Status)
            {
                case JournalCreateStatus.InUse:
                    return StartResult(CommandBrokerStartStatus.JournalInUse, null, result.ErrorMessage);
                case JournalCreateStatus.RestartRequired:
                    return StartResult(CommandBrokerStartStatus.RestartRequired, null, result.ErrorMessage);
                case JournalCreateStatus.ReplayRejected:
                    return StartResult(CommandBrokerStartStatus.ReplayRejected, null, result.ErrorMessage);
                case JournalCreateStatus.BindingMismatchRestartRequired:
                case JournalCreateStatus.CorruptRestartRequired:
                    return StartResult(
                        CommandBrokerStartStatus.JournalCorruptRestartRequired,
                        null,
                        result.ErrorMessage);
                default:
                    return StartResult(
                        CommandBrokerStartStatus.JournalFailureRestartRequired,
                        null,
                        result.ErrorMessage);
            }
        }

        private static CommandBrokerStartResult StartResult(
            CommandBrokerStartStatus status,
            CommandBrokerOperation operation,
            string errorMessage)
        {
            return new CommandBrokerStartResult(status, operation, errorMessage);
        }

        private static bool StartStatusRequiresRestart(CommandBrokerStartStatus status)
        {
            return status == CommandBrokerStartStatus.RestartRequired
                || status == CommandBrokerStartStatus.JournalCorruptRestartRequired
                || status == CommandBrokerStartStatus.GateFailureRestartRequired
                || status == CommandBrokerStartStatus.JournalFailureRestartRequired;
        }

        private static bool ReleaseStatusRequiresRestart(NamedMutexReleaseStatus status)
        {
            return status == NamedMutexReleaseStatus.TimedOutOwnershipUnknown
                || status == NamedMutexReleaseStatus.FailedRestartRequired;
        }

        private enum SessionJournalScanStatus
        {
            Clear = 1,
            Failure = 2
        }

        private sealed class SessionJournalScanResult
        {
            private SessionJournalScanResult(
                SessionJournalScanStatus status,
                CommandBrokerStartStatus startStatus,
                string errorMessage)
            {
                Status = status;
                StartStatus = startStatus;
                ErrorMessage = errorMessage;
            }

            internal SessionJournalScanStatus Status { get; private set; }

            internal CommandBrokerStartStatus StartStatus { get; private set; }

            internal string ErrorMessage { get; private set; }

            internal static SessionJournalScanResult Clear()
            {
                return new SessionJournalScanResult(
                    SessionJournalScanStatus.Clear,
                    CommandBrokerStartStatus.Started,
                    string.Empty);
            }

            internal static SessionJournalScanResult Failure(
                CommandBrokerStartStatus status,
                string errorMessage)
            {
                return new SessionJournalScanResult(
                    SessionJournalScanStatus.Failure,
                    status,
                    errorMessage);
            }
        }
    }

    public sealed class CommandBrokerOperation : IDisposable
    {
        private readonly object sync;
        private readonly CommandBrokerHost host;
        private readonly ExecutionJournal journal;
        private readonly NamedMutexLease lease;
        private readonly string journalPath;
        private ExecutionJournalState currentState;
        private bool closed;
        private bool restartRequired;
        private bool ownershipReleaseUnknown;
        private int journalDisposed;
        private int hostNotified;

        internal CommandBrokerOperation(
            CommandBrokerHost host,
            ExecutionJournal journal,
            NamedMutexLease lease,
            string journalPath)
        {
            sync = new object();
            this.host = host;
            this.journal = journal;
            this.lease = lease;
            this.journalPath = journalPath;
            currentState = ExecutionJournalState.ConsentRecorded;
        }

        public string JournalPath
        {
            get { return journalPath; }
        }

        public ExecutionJournalState CurrentState
        {
            get
            {
                lock (sync)
                {
                    return currentState;
                }
            }
        }

        public bool IsClosed
        {
            get
            {
                lock (sync)
                {
                    return closed;
                }
            }
        }

        public bool RestartRequired
        {
            get
            {
                lock (sync)
                {
                    return restartRequired;
                }
            }
        }

        public bool OwnershipReleaseUnknown
        {
            get
            {
                lock (sync)
                {
                    return ownershipReleaseUnknown;
                }
            }
        }

        public NamedMutexReleaseStatus RetryOwnershipRelease(int timeoutMilliseconds)
        {
            lock (sync)
            {
                if (!ownershipReleaseUnknown)
                {
                    throw new InvalidOperationException("Ownership release may be retried only after a release timeout.");
                }
            }

            NamedMutexReleaseStatus status = lease.Release(timeoutMilliseconds);
            if (status == NamedMutexReleaseStatus.Released
                || status == NamedMutexReleaseStatus.AlreadyReleased)
            {
                lock (sync)
                {
                    ownershipReleaseUnknown = false;
                }

                NotifyHostOnce();
            }
            else if (status == NamedMutexReleaseStatus.TimedOutOwnershipUnknown)
            {
                MarkOwnershipUnknown();
            }
            else
            {
                MarkReleaseFailed();
                NotifyHostOnce();
            }

            return status;
        }

        public BrokerTransitionStatus Prepare()
        {
            return Transition(
                ExecutionJournalState.ConsentRecorded,
                ExecutionJournalState.Prepared,
                false);
        }

        public BrokerTransitionStatus IssueIntent()
        {
            return Transition(
                ExecutionJournalState.Prepared,
                ExecutionJournalState.IntentIssued,
                false);
        }

        public BrokerTransitionStatus TryEnterSideEffectBoundary()
        {
            bool release = false;
            BrokerTransitionStatus result;
            lock (sync)
            {
                if (closed)
                {
                    return BrokerTransitionStatus.AlreadyClosed;
                }

                if (currentState != ExecutionJournalState.IntentIssued)
                {
                    return BrokerTransitionStatus.InvalidState;
                }

                if (host.IsStopRequested)
                {
                    JournalAppendStatus stopped = journal.Append(ExecutionJournalState.AbortUncertain);
                    if (stopped == JournalAppendStatus.Appended)
                    {
                        currentState = ExecutionJournalState.AbortUncertain;
                        closed = true;
                        LatchRestartRequired();
                        release = true;
                        result = BrokerTransitionStatus.StoppedBeforeSideEffect;
                    }
                    else
                    {
                        closed = true;
                        LatchRestartRequired();
                        release = true;
                        result = BrokerTransitionStatus.JournalFailureRestartRequired;
                    }
                }
                else
                {
                    JournalAppendStatus append = journal.Append(ExecutionJournalState.SideEffectEntered);
                    if (append == JournalAppendStatus.Appended)
                    {
                        currentState = ExecutionJournalState.SideEffectEntered;
                        result = BrokerTransitionStatus.Advanced;
                    }
                    else if (append == JournalAppendStatus.InvalidTransition)
                    {
                        result = BrokerTransitionStatus.InvalidState;
                    }
                    else
                    {
                        closed = true;
                        LatchRestartRequired();
                        release = true;
                        result = BrokerTransitionStatus.JournalFailureRestartRequired;
                    }
                }
            }

            if (release)
            {
                NamedMutexReleaseStatus releaseStatus = ReleaseResources();
                if (releaseStatus == NamedMutexReleaseStatus.TimedOutOwnershipUnknown)
                {
                    result = BrokerTransitionStatus.OwnershipReleaseTimeoutRestartRequired;
                }
                else if (releaseStatus == NamedMutexReleaseStatus.FailedRestartRequired)
                {
                    result = BrokerTransitionStatus.OwnershipReleaseFailedRestartRequired;
                }
            }

            return result;
        }

        public BrokerTransitionStatus RecordReceipt()
        {
            return Transition(
                ExecutionJournalState.SideEffectEntered,
                ExecutionJournalState.Receipted,
                false);
        }

        public BrokerTransitionStatus VerifyTerminal()
        {
            return Transition(
                ExecutionJournalState.Receipted,
                ExecutionJournalState.TerminalVerified,
                true);
        }

        public BrokerTransitionStatus AbortUncertain()
        {
            bool release = false;
            BrokerTransitionStatus result;
            lock (sync)
            {
                if (closed)
                {
                    return BrokerTransitionStatus.AlreadyClosed;
                }

                JournalAppendStatus append = journal.Append(ExecutionJournalState.AbortUncertain);
                if (append == JournalAppendStatus.Appended)
                {
                    currentState = ExecutionJournalState.AbortUncertain;
                    LatchRestartRequired();
                    closed = true;
                    release = true;
                    result = BrokerTransitionStatus.Advanced;
                }
                else if (append == JournalAppendStatus.InvalidTransition)
                {
                    result = BrokerTransitionStatus.InvalidState;
                }
                else
                {
                    LatchRestartRequired();
                    closed = true;
                    release = true;
                    result = BrokerTransitionStatus.JournalFailureRestartRequired;
                }
            }

            if (release)
            {
                NamedMutexReleaseStatus releaseStatus = ReleaseResources();
                if (releaseStatus == NamedMutexReleaseStatus.TimedOutOwnershipUnknown)
                {
                    result = BrokerTransitionStatus.OwnershipReleaseTimeoutRestartRequired;
                }
                else if (releaseStatus == NamedMutexReleaseStatus.FailedRestartRequired)
                {
                    result = BrokerTransitionStatus.OwnershipReleaseFailedRestartRequired;
                }
            }

            return result;
        }

        public void Dispose()
        {
            lock (sync)
            {
                if (!closed)
                {
                    LatchRestartRequired();
                    closed = true;
                }
            }

            ReleaseResources();
        }

        internal BrokerStopDisposition GetStopDisposition()
        {
            lock (sync)
            {
                if (closed)
                {
                    return BrokerStopDisposition.NoActiveOperation;
                }

                return (int)currentState >= (int)ExecutionJournalState.SideEffectEntered
                    ? BrokerStopDisposition.DeferredUntilTerminal
                    : BrokerStopDisposition.WillStopBeforeSideEffect;
            }
        }

        private BrokerTransitionStatus Transition(
            ExecutionJournalState expected,
            ExecutionJournalState next,
            bool terminal)
        {
            bool release = false;
            BrokerTransitionStatus result;
            lock (sync)
            {
                if (closed)
                {
                    return BrokerTransitionStatus.AlreadyClosed;
                }

                if (currentState != expected)
                {
                    return BrokerTransitionStatus.InvalidState;
                }

                JournalAppendStatus append = journal.Append(next);
                if (append == JournalAppendStatus.Appended)
                {
                    currentState = next;
                    if (terminal)
                    {
                        closed = true;
                        release = true;
                    }

                    result = BrokerTransitionStatus.Advanced;
                }
                else if (append == JournalAppendStatus.InvalidTransition)
                {
                    result = BrokerTransitionStatus.InvalidState;
                }
                else
                {
                    LatchRestartRequired();
                    closed = true;
                    release = true;
                    result = BrokerTransitionStatus.JournalFailureRestartRequired;
                }
            }

            if (release)
            {
                NamedMutexReleaseStatus releaseStatus = ReleaseResources();
                if (releaseStatus == NamedMutexReleaseStatus.TimedOutOwnershipUnknown)
                {
                    result = BrokerTransitionStatus.OwnershipReleaseTimeoutRestartRequired;
                }
                else if (releaseStatus == NamedMutexReleaseStatus.FailedRestartRequired)
                {
                    result = BrokerTransitionStatus.OwnershipReleaseFailedRestartRequired;
                }
            }

            return result;
        }

        private NamedMutexReleaseStatus ReleaseResources()
        {
            if (Interlocked.Exchange(ref journalDisposed, 1) == 0)
            {
                journal.Dispose();
            }

            NamedMutexReleaseStatus release = lease.ReleaseDefault();
            if (release == NamedMutexReleaseStatus.TimedOutOwnershipUnknown)
            {
                MarkOwnershipUnknown();
                return release;
            }

            if (release == NamedMutexReleaseStatus.FailedRestartRequired)
            {
                MarkReleaseFailed();
                NotifyHostOnce();
                return release;
            }

            lock (sync)
            {
                ownershipReleaseUnknown = false;
            }

            NotifyHostOnce();
            return release;
        }

        private void MarkOwnershipUnknown()
        {
            lock (sync)
            {
                LatchRestartRequired();
                ownershipReleaseUnknown = true;
            }
        }

        private void MarkReleaseFailed()
        {
            lock (sync)
            {
                LatchRestartRequired();
                ownershipReleaseUnknown = false;
            }
        }

        private void LatchRestartRequired()
        {
            restartRequired = true;
            host.MarkRestartRequired();
        }

        private void NotifyHostOnce()
        {
            if (Interlocked.Exchange(ref hostNotified, 1) == 0)
            {
                host.OperationClosed(this);
            }
        }
    }
}
