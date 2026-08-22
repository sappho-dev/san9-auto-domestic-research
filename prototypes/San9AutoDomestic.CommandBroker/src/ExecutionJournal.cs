using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace San9AutoDomestic.CommandBroker
{
    public enum ExecutionJournalState
    {
        ConsentRecorded = 1,
        Prepared = 2,
        IntentIssued = 3,
        SideEffectEntered = 4,
        Receipted = 5,
        TerminalVerified = 6,
        AbortUncertain = 7
    }

    public sealed class ExecutionJournalBinding : IEquatable<ExecutionJournalBinding>
    {
        private readonly byte[] requestFingerprint;

        public ExecutionJournalBinding(
            ProcessSessionIdentity processSession,
            string requestFingerprint,
            int stageOrdinal,
            Guid consentId)
        {
            if (processSession == null)
            {
                throw new ArgumentNullException("processSession");
            }

            if (stageOrdinal <= 0)
            {
                throw new ArgumentOutOfRangeException("stageOrdinal", "Stage ordinal must be positive.");
            }

            if (consentId == Guid.Empty)
            {
                throw new ArgumentException("Consent id must not be empty.", "consentId");
            }

            ProcessSession = processSession;
            this.requestFingerprint = BinaryValue.ParseSha256(requestFingerprint, "requestFingerprint");
            StageOrdinal = stageOrdinal;
            ConsentId = consentId;
        }

        internal ExecutionJournalBinding(
            ProcessSessionIdentity processSession,
            byte[] requestFingerprint,
            int stageOrdinal,
            Guid consentId)
        {
            if (processSession == null)
            {
                throw new ArgumentNullException("processSession");
            }

            BinaryValue.RequireLength(requestFingerprint, BinaryValue.Sha256Length, "requestFingerprint");
            if (stageOrdinal <= 0 || consentId == Guid.Empty)
            {
                throw new ArgumentException("Invalid persisted journal binding.");
            }

            ProcessSession = processSession;
            this.requestFingerprint = BinaryValue.Clone(requestFingerprint);
            StageOrdinal = stageOrdinal;
            ConsentId = consentId;
        }

        public ProcessSessionIdentity ProcessSession { get; private set; }

        public string RequestFingerprint
        {
            get { return BinaryValue.ToHex(requestFingerprint); }
        }

        public int StageOrdinal { get; private set; }

        public Guid ConsentId { get; private set; }

        internal byte[] GetRequestFingerprintBytes()
        {
            return BinaryValue.Clone(requestFingerprint);
        }

        public bool Equals(ExecutionJournalBinding other)
        {
            return other != null
                && ProcessSession.Equals(other.ProcessSession)
                && StageOrdinal == other.StageOrdinal
                && ConsentId == other.ConsentId
                && BinaryValue.AreEqual(requestFingerprint, other.requestFingerprint);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as ExecutionJournalBinding);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = ProcessSession.GetHashCode();
                hash = (hash * 397) ^ StageOrdinal;
                hash = (hash * 397) ^ ConsentId.GetHashCode();
                hash = (hash * 397) ^ requestFingerprint[0];
                return hash;
            }
        }
    }

    public enum JournalInspectionStatus
    {
        Missing = 1,
        ValidNonterminal = 2,
        ValidTerminal = 3,
        InUse = 4,
        CorruptRestartRequired = 5
    }

    public sealed class JournalInspection
    {
        internal JournalInspection(
            JournalInspectionStatus status,
            ExecutionJournalBinding binding,
            ExecutionJournalState? lastState,
            long recordCount,
            long fileLength,
            string fileSha256,
            string errorMessage)
        {
            Status = status;
            Binding = binding;
            LastState = lastState;
            RecordCount = recordCount;
            FileLength = fileLength;
            FileSha256 = fileSha256 ?? string.Empty;
            ErrorMessage = errorMessage ?? string.Empty;
        }

        public JournalInspectionStatus Status { get; private set; }

        public ExecutionJournalBinding Binding { get; private set; }

        public ExecutionJournalState? LastState { get; private set; }

        public long RecordCount { get; private set; }

        public long FileLength { get; private set; }

        public string FileSha256 { get; private set; }

        public string ErrorMessage { get; private set; }
    }

    public static class ExecutionJournalInspector
    {
        public static JournalInspection Inspect(string path)
        {
            return ExecutionJournal.Inspect(path);
        }
    }

    internal enum JournalCreateStatus
    {
        Created = 1,
        InUse = 2,
        RestartRequired = 3,
        ReplayRejected = 4,
        BindingMismatchRestartRequired = 5,
        CorruptRestartRequired = 6,
        IoFailureRestartRequired = 7
    }

    internal sealed class JournalCreateResult
    {
        internal JournalCreateResult(JournalCreateStatus status, ExecutionJournal journal, string errorMessage)
        {
            Status = status;
            Journal = journal;
            ErrorMessage = errorMessage ?? string.Empty;
        }

        internal JournalCreateStatus Status { get; private set; }

        internal ExecutionJournal Journal { get; private set; }

        internal string ErrorMessage { get; private set; }
    }

    internal enum JournalAppendStatus
    {
        Appended = 1,
        InvalidTransition = 2,
        Terminal = 3,
        WriteFailureRestartRequired = 4
    }

    internal sealed class ExecutionJournal : IDisposable
    {
        private const int SchemaVersion = 1;
        private const int RecordPayloadBytes = 120;
        private const int DigestBytes = 32;
        private const int MaximumRecords = 16;
        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("S9CBJR01");

        private readonly object sync;
        private readonly ExecutionJournalBinding binding;
        private FileStream stream;
        private long sequence;
        private ExecutionJournalState state;
        private bool faulted;

        private ExecutionJournal(
            string path,
            FileStream stream,
            ExecutionJournalBinding binding)
        {
            sync = new object();
            this.stream = stream;
            this.binding = binding;
        }

        internal ExecutionJournalState State
        {
            get
            {
                lock (sync)
                {
                    return state;
                }
            }
        }

        internal JournalDurableTip GetDurableTip()
        {
            lock (sync)
            {
                if (stream == null || faulted || sequence <= 0)
                {
                    throw new InvalidOperationException("A durable journal tip is unavailable.");
                }

                long length = stream.Length;
                stream.Position = 0;
                byte[] digest;
                using (SHA256 algorithm = SHA256.Create())
                {
                    digest = algorithm.ComputeHash(stream);
                }

                stream.Position = length;
                return new JournalDurableTip(sequence, state, length, digest);
            }
        }

        internal static JournalCreateResult TryCreate(string path, ExecutionJournalBinding binding)
        {
            ValidatePathAndBinding(path, binding);
            FileStream createdStream = null;
            try
            {
                createdStream = Open(path, FileMode.CreateNew);
                ExecutionJournal journal = new ExecutionJournal(path, createdStream, binding);
                JournalAppendStatus first = journal.AppendFirstRecord();
                if (first != JournalAppendStatus.Appended)
                {
                    journal.Dispose();
                    return new JournalCreateResult(
                        JournalCreateStatus.IoFailureRestartRequired,
                        null,
                        "The initial durable journal record could not be committed.");
                }

                return new JournalCreateResult(JournalCreateStatus.Created, journal, string.Empty);
            }
            catch (IOException)
            {
                if (createdStream != null)
                {
                    createdStream.Dispose();
                }

                return ClassifyExistingForStart(path, binding);
            }
            catch (Exception exception)
            {
                if (createdStream != null)
                {
                    createdStream.Dispose();
                }

                return new JournalCreateResult(
                    JournalCreateStatus.IoFailureRestartRequired,
                    null,
                    exception.GetType().Name + ": " + exception.Message);
            }
        }

        internal JournalAppendStatus Append(ExecutionJournalState next)
        {
            lock (sync)
            {
                if (stream == null || faulted)
                {
                    return JournalAppendStatus.WriteFailureRestartRequired;
                }

                if (IsTerminal(state))
                {
                    return JournalAppendStatus.Terminal;
                }

                if (!IsAllowedTransition(state, next))
                {
                    return JournalAppendStatus.InvalidTransition;
                }

                return AppendCore(next);
            }
        }

        public void Dispose()
        {
            lock (sync)
            {
                if (stream != null)
                {
                    stream.Dispose();
                    stream = null;
                }
            }
        }

        internal static JournalInspection Inspect(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("A journal path is required.", "path");
            }

            if (!File.Exists(path))
            {
                return new JournalInspection(JournalInspectionStatus.Missing, null, null, 0, 0, string.Empty, string.Empty);
            }

            try
            {
                using (FileStream inspectionStream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.None,
                    4096,
                    FileOptions.SequentialScan))
                {
                    List<JournalRecord> records;
                    string error;
                    if (!TryReadAll(inspectionStream, out records, out error))
                    {
                        return new JournalInspection(
                            JournalInspectionStatus.CorruptRestartRequired,
                            null,
                            null,
                            0,
                            0,
                            string.Empty,
                            error);
                    }

                    JournalRecord last = records[records.Count - 1];
                    long fileLength = inspectionStream.Length;
                    inspectionStream.Position = 0;
                    byte[] fileDigest;
                    using (SHA256 algorithm = SHA256.Create())
                    {
                        fileDigest = algorithm.ComputeHash(inspectionStream);
                    }
                    return new JournalInspection(
                        IsTerminal(last.State)
                            ? JournalInspectionStatus.ValidTerminal
                            : JournalInspectionStatus.ValidNonterminal,
                        last.Binding,
                        last.State,
                        records.Count,
                        fileLength,
                        BinaryValue.ToHex(fileDigest),
                        string.Empty);
                }
            }
            catch (IOException exception)
            {
                return new JournalInspection(
                    JournalInspectionStatus.InUse,
                    null,
                    null,
                    0,
                    0,
                    string.Empty,
                    exception.GetType().Name + ": " + exception.Message);
            }
            catch (Exception exception)
            {
                return new JournalInspection(
                    JournalInspectionStatus.CorruptRestartRequired,
                    null,
                    null,
                    0,
                    0,
                    string.Empty,
                    exception.GetType().Name + ": " + exception.Message);
            }
        }

        private static JournalCreateResult ClassifyExistingForStart(
            string path,
            ExecutionJournalBinding requestedBinding)
        {
            try
            {
                using (FileStream existing = Open(path, FileMode.Open))
                {
                    List<JournalRecord> records;
                    string error;
                    if (!TryReadAll(existing, out records, out error))
                    {
                        return new JournalCreateResult(
                            JournalCreateStatus.CorruptRestartRequired,
                            null,
                            error);
                    }

                    JournalRecord last = records[records.Count - 1];
                    if (!last.Binding.Equals(requestedBinding))
                    {
                        return new JournalCreateResult(
                            JournalCreateStatus.BindingMismatchRestartRequired,
                            null,
                            "The existing journal is bound to a different operation.");
                    }

                    if (IsTerminal(last.State))
                    {
                        return new JournalCreateResult(
                            JournalCreateStatus.ReplayRejected,
                            null,
                            "The operation already has a terminal journal.");
                    }

                    return new JournalCreateResult(
                        JournalCreateStatus.RestartRequired,
                        null,
                        "A nonterminal journal must never be resumed or replayed.");
                }
            }
            catch (IOException exception)
            {
                return new JournalCreateResult(
                    JournalCreateStatus.InUse,
                    null,
                    exception.GetType().Name + ": " + exception.Message);
            }
            catch (Exception exception)
            {
                return new JournalCreateResult(
                    JournalCreateStatus.CorruptRestartRequired,
                    null,
                    exception.GetType().Name + ": " + exception.Message);
            }
        }

        private JournalAppendStatus AppendFirstRecord()
        {
            lock (sync)
            {
                if (sequence != 0 || stream == null)
                {
                    return JournalAppendStatus.InvalidTransition;
                }

                return AppendCore(ExecutionJournalState.ConsentRecorded);
            }
        }

        private JournalAppendStatus AppendCore(ExecutionJournalState next)
        {
            long nextSequence = checked(sequence + 1);
            byte[] payload = SerializeRecord(new JournalRecord(nextSequence, next, binding));
            byte[] digest = BinaryValue.ComputeSha256(payload);
            byte[] prefix = BitConverter.GetBytes(payload.Length);

            try
            {
                stream.Write(prefix, 0, prefix.Length);
                stream.Write(payload, 0, payload.Length);
                stream.Write(digest, 0, digest.Length);
                stream.Flush(true);
                sequence = nextSequence;
                state = next;
                return JournalAppendStatus.Appended;
            }
            catch
            {
                faulted = true;
                return JournalAppendStatus.WriteFailureRestartRequired;
            }
        }

        private static byte[] SerializeRecord(JournalRecord record)
        {
            using (MemoryStream memory = new MemoryStream(RecordPayloadBytes))
            using (BinaryWriter writer = new BinaryWriter(memory, Encoding.UTF8))
            {
                writer.Write(Magic);
                writer.Write(SchemaVersion);
                writer.Write(record.Sequence);
                writer.Write((int)record.State);
                writer.Write(record.Binding.ProcessSession.ProcessId);
                writer.Write(record.Binding.ProcessSession.CreationFileTimeUtc);
                writer.Write(record.Binding.ProcessSession.GetExecutableSha256Bytes());
                writer.Write(record.Binding.GetRequestFingerprintBytes());
                writer.Write(record.Binding.StageOrdinal);
                writer.Write(record.Binding.ConsentId.ToByteArray());
                writer.Flush();
                byte[] payload = memory.ToArray();
                if (payload.Length != RecordPayloadBytes)
                {
                    throw new InvalidOperationException("Journal record serialization length changed.");
                }

                return payload;
            }
        }

        private static bool TryReadAll(
            FileStream stream,
            out List<JournalRecord> records,
            out string error)
        {
            records = new List<JournalRecord>();
            error = string.Empty;
            stream.Position = 0;

            while (true)
            {
                if (records.Count >= MaximumRecords)
                {
                    error = "The journal contains too many records.";
                    return false;
                }

                byte[] prefix = new byte[sizeof(int)];
                int first = stream.ReadByte();
                if (first < 0)
                {
                    break;
                }

                prefix[0] = (byte)first;
                if (!ReadExact(stream, prefix, 1, prefix.Length - 1))
                {
                    error = "The journal ends inside a record length prefix.";
                    return false;
                }

                int payloadLength = BitConverter.ToInt32(prefix, 0);
                if (payloadLength != RecordPayloadBytes)
                {
                    error = "The journal record length is invalid.";
                    return false;
                }

                byte[] payload = new byte[payloadLength];
                byte[] persistedDigest = new byte[DigestBytes];
                if (!ReadExact(stream, payload, 0, payload.Length)
                    || !ReadExact(stream, persistedDigest, 0, persistedDigest.Length))
                {
                    error = "The journal ends inside a record.";
                    return false;
                }

                byte[] computedDigest = BinaryValue.ComputeSha256(payload);
                if (!BinaryValue.AreEqual(persistedDigest, computedDigest))
                {
                    error = "The journal record checksum is invalid.";
                    return false;
                }

                JournalRecord record;
                if (!TryDeserializeRecord(payload, out record, out error))
                {
                    return false;
                }

                if (record.Sequence != records.Count + 1)
                {
                    error = "The journal record sequence is not contiguous.";
                    return false;
                }

                if (records.Count == 0)
                {
                    if (record.State != ExecutionJournalState.ConsentRecorded)
                    {
                        error = "The first journal record is not ConsentRecorded.";
                        return false;
                    }
                }
                else
                {
                    JournalRecord previous = records[records.Count - 1];
                    if (!previous.Binding.Equals(record.Binding))
                    {
                        error = "The journal binding changed between records.";
                        return false;
                    }

                    if (!IsAllowedTransition(previous.State, record.State))
                    {
                        error = "The journal state transition is invalid.";
                        return false;
                    }
                }

                records.Add(record);
            }

            if (records.Count == 0)
            {
                error = "The journal contains no complete records.";
                return false;
            }

            return true;
        }

        private static bool TryDeserializeRecord(
            byte[] payload,
            out JournalRecord record,
            out string error)
        {
            record = null;
            error = string.Empty;
            try
            {
                using (MemoryStream memory = new MemoryStream(payload, false))
                using (BinaryReader reader = new BinaryReader(memory, Encoding.UTF8))
                {
                    byte[] magic = reader.ReadBytes(Magic.Length);
                    if (!BinaryValue.AreEqual(magic, Magic))
                    {
                        error = "The journal magic is invalid.";
                        return false;
                    }

                    if (reader.ReadInt32() != SchemaVersion)
                    {
                        error = "The journal schema is unsupported.";
                        return false;
                    }

                    long sequence = reader.ReadInt64();
                    int rawState = reader.ReadInt32();
                    if (!Enum.IsDefined(typeof(ExecutionJournalState), rawState))
                    {
                        error = "The journal state is unknown.";
                        return false;
                    }

                    int processId = reader.ReadInt32();
                    long creationFileTimeUtc = reader.ReadInt64();
                    byte[] executableDigest = reader.ReadBytes(BinaryValue.Sha256Length);
                    byte[] requestDigest = reader.ReadBytes(BinaryValue.Sha256Length);
                    int stageOrdinal = reader.ReadInt32();
                    byte[] consentBytes = reader.ReadBytes(16);
                    if (executableDigest.Length != BinaryValue.Sha256Length
                        || requestDigest.Length != BinaryValue.Sha256Length
                        || consentBytes.Length != 16
                        || memory.Position != memory.Length)
                    {
                        error = "The journal binding payload is incomplete.";
                        return false;
                    }

                    ProcessSessionIdentity identity = new ProcessSessionIdentity(
                        processId,
                        creationFileTimeUtc,
                        executableDigest);
                    ExecutionJournalBinding binding = new ExecutionJournalBinding(
                        identity,
                        requestDigest,
                        stageOrdinal,
                        new Guid(consentBytes));
                    record = new JournalRecord(
                        sequence,
                        (ExecutionJournalState)rawState,
                        binding);
                    return true;
                }
            }
            catch (Exception exception)
            {
                error = exception.GetType().Name + ": " + exception.Message;
                return false;
            }
        }

        private static bool ReadExact(Stream stream, byte[] buffer, int offset, int count)
        {
            int remaining = count;
            while (remaining > 0)
            {
                int read = stream.Read(buffer, offset + (count - remaining), remaining);
                if (read <= 0)
                {
                    return false;
                }

                remaining -= read;
            }

            return true;
        }

        private static bool IsAllowedTransition(
            ExecutionJournalState current,
            ExecutionJournalState next)
        {
            if (!IsTerminal(current) && next == ExecutionJournalState.AbortUncertain)
            {
                return true;
            }

            switch (current)
            {
                case ExecutionJournalState.ConsentRecorded:
                    return next == ExecutionJournalState.Prepared;
                case ExecutionJournalState.Prepared:
                    return next == ExecutionJournalState.IntentIssued;
                case ExecutionJournalState.IntentIssued:
                    return next == ExecutionJournalState.SideEffectEntered;
                case ExecutionJournalState.SideEffectEntered:
                    return next == ExecutionJournalState.Receipted;
                case ExecutionJournalState.Receipted:
                    return next == ExecutionJournalState.TerminalVerified;
                default:
                    return false;
            }
        }

        private static bool IsTerminal(ExecutionJournalState value)
        {
            return value == ExecutionJournalState.TerminalVerified
                || value == ExecutionJournalState.AbortUncertain;
        }

        private static FileStream Open(string path, FileMode mode)
        {
            return new FileStream(
                path,
                mode,
                FileAccess.ReadWrite,
                FileShare.None,
                4096,
                FileOptions.WriteThrough);
        }

        private static void ValidatePathAndBinding(string path, ExecutionJournalBinding binding)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("A journal path is required.", "path");
            }

            if (!Path.IsPathRooted(path))
            {
                throw new ArgumentException("The journal path must be absolute.", "path");
            }

            if (binding == null)
            {
                throw new ArgumentNullException("binding");
            }

            string directory = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                throw new DirectoryNotFoundException("The journal directory must already exist.");
            }
        }

        private sealed class JournalRecord
        {
            internal JournalRecord(
                long sequence,
                ExecutionJournalState state,
                ExecutionJournalBinding binding)
            {
                Sequence = sequence;
                State = state;
                Binding = binding;
            }

            internal long Sequence { get; private set; }

            internal ExecutionJournalState State { get; private set; }

            internal ExecutionJournalBinding Binding { get; private set; }
        }
    }

    internal sealed class JournalDurableTip
    {
        private readonly byte[] fileSha256;

        internal JournalDurableTip(
            long recordCount,
            ExecutionJournalState state,
            long fileLength,
            byte[] fileSha256)
        {
            RecordCount = recordCount;
            State = state;
            FileLength = fileLength;
            this.fileSha256 = BinaryValue.Clone(fileSha256);
        }

        internal long RecordCount { get; private set; }

        internal ExecutionJournalState State { get; private set; }

        internal long FileLength { get; private set; }

        internal byte[] GetFileSha256Bytes()
        {
            return BinaryValue.Clone(fileSha256);
        }
    }
}
