using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace San9AutoDomestic.CommandBroker
{
    internal enum SessionLedgerStatus
    {
        Missing = 1,
        Valid = 2,
        InUse = 3,
        CorruptRestartRequired = 4
    }

    internal sealed class SessionLedgerEntry
    {
        internal SessionLedgerEntry(long sequence, ExecutionJournalBinding binding, string journalFileName)
        {
            Sequence = sequence;
            Binding = binding;
            JournalFileName = journalFileName;
        }

        internal long Sequence { get; private set; }

        internal ExecutionJournalBinding Binding { get; private set; }

        internal string JournalFileName { get; private set; }
    }

    internal sealed class SessionLedgerInspection
    {
        internal SessionLedgerInspection(SessionLedgerStatus status, IList<SessionLedgerEntry> entries, string error)
        {
            Status = status;
            Entries = entries ?? new List<SessionLedgerEntry>().AsReadOnly();
            ErrorMessage = error ?? string.Empty;
        }

        internal SessionLedgerStatus Status { get; private set; }

        internal IList<SessionLedgerEntry> Entries { get; private set; }

        internal string ErrorMessage { get; private set; }
    }

    internal enum SessionLedgerAppendStatus
    {
        Appended = 1,
        InUse = 2,
        CorruptRestartRequired = 3,
        WriteFailureRestartRequired = 4
    }

    internal static class SessionLedger
    {
        private const int SchemaVersion = 1;
        private const int JournalFileNameBytes = 142;
        private const int PayloadBytes = 258;
        private const int DigestBytes = 32;
        private const int MaximumEntries = 4096;
        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("S9CBLD01");

        internal static SessionLedgerInspection Inspect(string path)
        {
            if (!File.Exists(path))
            {
                return new SessionLedgerInspection(SessionLedgerStatus.Missing, null, string.Empty);
            }

            try
            {
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                {
                    return new SessionLedgerInspection(
                        SessionLedgerStatus.CorruptRestartRequired,
                        null,
                        "Reparse-point session ledgers are forbidden.");
                }

                using (FileStream stream = Open(path, FileMode.Open))
                {
                    List<SessionLedgerEntry> entries;
                    string error;
                    if (!TryReadAll(stream, out entries, out error))
                    {
                        return new SessionLedgerInspection(SessionLedgerStatus.CorruptRestartRequired, null, error);
                    }

                    return new SessionLedgerInspection(SessionLedgerStatus.Valid, entries.AsReadOnly(), string.Empty);
                }
            }
            catch (IOException exception)
            {
                return new SessionLedgerInspection(SessionLedgerStatus.InUse, null, exception.GetType().Name + ": " + exception.Message);
            }
            catch (Exception exception)
            {
                return new SessionLedgerInspection(SessionLedgerStatus.CorruptRestartRequired, null, exception.GetType().Name + ": " + exception.Message);
            }
        }

        internal static SessionLedgerAppendStatus TryAppend(
            string path,
            ExecutionJournalBinding binding,
            string journalFileName,
            out string error)
        {
            error = string.Empty;
            ValidateJournalFileName(journalFileName);
            try
            {
                if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                {
                    error = "Reparse-point session ledgers are forbidden.";
                    return SessionLedgerAppendStatus.CorruptRestartRequired;
                }

                using (FileStream stream = Open(path, FileMode.OpenOrCreate))
                {
                    List<SessionLedgerEntry> entries;
                    if (stream.Length == 0)
                    {
                        entries = new List<SessionLedgerEntry>();
                    }
                    else if (!TryReadAll(stream, out entries, out error))
                    {
                        return SessionLedgerAppendStatus.CorruptRestartRequired;
                    }

                    if (entries.Count >= MaximumEntries)
                    {
                        error = "The session ledger has reached its maximum entry count.";
                        return SessionLedgerAppendStatus.CorruptRestartRequired;
                    }

                    foreach (SessionLedgerEntry existing in entries)
                    {
                        if (string.Equals(existing.JournalFileName, journalFileName, StringComparison.OrdinalIgnoreCase)
                            || existing.Binding.Equals(binding)
                            || HasSameReplayIdentity(existing.Binding, binding))
                        {
                            error = "The session ledger already contains this operation.";
                            return SessionLedgerAppendStatus.CorruptRestartRequired;
                        }
                    }

                    long sequence = checked(entries.Count + 1L);
                    byte[] payload = Serialize(new SessionLedgerEntry(sequence, binding, journalFileName));
                    byte[] digest = BinaryValue.ComputeSha256(payload);
                    stream.Position = stream.Length;
                    stream.Write(BitConverter.GetBytes(payload.Length), 0, sizeof(int));
                    stream.Write(payload, 0, payload.Length);
                    stream.Write(digest, 0, digest.Length);
                    stream.Flush(true);
                    return SessionLedgerAppendStatus.Appended;
                }
            }
            catch (IOException exception)
            {
                error = exception.GetType().Name + ": " + exception.Message;
                return SessionLedgerAppendStatus.InUse;
            }
            catch (Exception exception)
            {
                error = exception.GetType().Name + ": " + exception.Message;
                return SessionLedgerAppendStatus.WriteFailureRestartRequired;
            }
        }

        private static FileStream Open(string path, FileMode mode)
        {
            return new FileStream(path, mode, FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.WriteThrough);
        }

        private static byte[] Serialize(SessionLedgerEntry entry)
        {
            using (MemoryStream memory = new MemoryStream(PayloadBytes))
            using (BinaryWriter writer = new BinaryWriter(memory, Encoding.UTF8))
            {
                writer.Write(Magic);
                writer.Write(SchemaVersion);
                writer.Write(entry.Sequence);
                writer.Write(entry.Binding.ProcessSession.ProcessId);
                writer.Write(entry.Binding.ProcessSession.CreationFileTimeUtc);
                writer.Write(entry.Binding.ProcessSession.GetExecutableSha256Bytes());
                writer.Write(entry.Binding.GetRequestFingerprintBytes());
                writer.Write(entry.Binding.StageOrdinal);
                writer.Write(entry.Binding.ConsentId.ToByteArray());
                writer.Write(Encoding.ASCII.GetBytes(entry.JournalFileName));
                writer.Flush();
                byte[] payload = memory.ToArray();
                if (payload.Length != PayloadBytes)
                {
                    throw new InvalidOperationException("Session ledger serialization length changed.");
                }

                return payload;
            }
        }

        private static bool TryReadAll(FileStream stream, out List<SessionLedgerEntry> entries, out string error)
        {
            entries = new List<SessionLedgerEntry>();
            error = string.Empty;
            stream.Position = 0;
            while (stream.Position < stream.Length)
            {
                if (entries.Count >= MaximumEntries)
                {
                    error = "The session ledger contains too many entries.";
                    return false;
                }

                byte[] prefix = new byte[sizeof(int)];
                if (!ReadExact(stream, prefix, 0, prefix.Length))
                {
                    error = "The session ledger ends inside a length prefix.";
                    return false;
                }

                int length = BitConverter.ToInt32(prefix, 0);
                if (length != PayloadBytes)
                {
                    error = "The session ledger entry length is invalid.";
                    return false;
                }

                byte[] payload = new byte[length];
                byte[] digest = new byte[DigestBytes];
                if (!ReadExact(stream, payload, 0, payload.Length)
                    || !ReadExact(stream, digest, 0, digest.Length)
                    || !BinaryValue.AreEqual(digest, BinaryValue.ComputeSha256(payload)))
                {
                    error = "The session ledger entry is partial or has an invalid checksum.";
                    return false;
                }

                SessionLedgerEntry entry;
                if (!TryDeserialize(payload, out entry, out error)
                    || entry.Sequence != entries.Count + 1L)
                {
                    if (string.IsNullOrEmpty(error))
                    {
                        error = "The session ledger sequence is not contiguous.";
                    }

                    return false;
                }

                foreach (SessionLedgerEntry previous in entries)
                {
                    if (previous.Binding.Equals(entry.Binding)
                        || HasSameReplayIdentity(previous.Binding, entry.Binding)
                        || string.Equals(previous.JournalFileName, entry.JournalFileName, StringComparison.OrdinalIgnoreCase))
                    {
                        error = "The session ledger contains a duplicate binding or journal path.";
                        return false;
                    }
                }

                entries.Add(entry);
            }

            if (entries.Count == 0)
            {
                error = "The session ledger contains no complete entries.";
                return false;
            }

            return true;
        }

        private static bool TryDeserialize(byte[] payload, out SessionLedgerEntry entry, out string error)
        {
            entry = null;
            error = string.Empty;
            try
            {
                using (MemoryStream memory = new MemoryStream(payload, false))
                using (BinaryReader reader = new BinaryReader(memory, Encoding.UTF8))
                {
                    if (!BinaryValue.AreEqual(reader.ReadBytes(Magic.Length), Magic)
                        || reader.ReadInt32() != SchemaVersion)
                    {
                        error = "The session ledger magic or schema is invalid.";
                        return false;
                    }

                    long sequence = reader.ReadInt64();
                    ProcessSessionIdentity session = new ProcessSessionIdentity(reader.ReadInt32(), reader.ReadInt64(), reader.ReadBytes(32));
                    ExecutionJournalBinding binding = new ExecutionJournalBinding(
                        session,
                        reader.ReadBytes(32),
                        reader.ReadInt32(),
                        new Guid(reader.ReadBytes(16)));
                    byte[] nameBytes = reader.ReadBytes(JournalFileNameBytes);
                    if (nameBytes.Length != JournalFileNameBytes || memory.Position != memory.Length)
                    {
                        error = "The session ledger entry shape is invalid.";
                        return false;
                    }

                    string journalFileName = Encoding.ASCII.GetString(nameBytes);
                    ValidateJournalFileName(journalFileName);
                    entry = new SessionLedgerEntry(sequence, binding, journalFileName);
                    return true;
                }
            }
            catch (Exception exception)
            {
                error = exception.GetType().Name + ": " + exception.Message;
                return false;
            }
        }

        private static void ValidateJournalFileName(string value)
        {
            if (string.IsNullOrEmpty(value)
                || value.Length != JournalFileNameBytes
                || !value.StartsWith("S9CB-", StringComparison.Ordinal)
                || !value.EndsWith(".journal", StringComparison.Ordinal)
                || !string.Equals(Path.GetFileName(value), value, StringComparison.Ordinal))
            {
                throw new ArgumentException("A canonical journal file name is required.", "value");
            }

            foreach (char character in value)
            {
                if (character > 0x7F)
                {
                    throw new ArgumentException("Journal file names must be ASCII.", "value");
                }
            }
        }

        private static bool ReadExact(Stream stream, byte[] buffer, int offset, int count)
        {
            while (count > 0)
            {
                int read = stream.Read(buffer, offset, count);
                if (read <= 0)
                {
                    return false;
                }

                offset += read;
                count -= read;
            }

            return true;
        }

        internal static bool HasSameReplayIdentity(
            ExecutionJournalBinding left,
            ExecutionJournalBinding right)
        {
            return left != null
                && right != null
                && left.ProcessSession.Equals(right.ProcessSession)
                && left.StageOrdinal == right.StageOrdinal
                && string.Equals(left.RequestFingerprint, right.RequestFingerprint, StringComparison.Ordinal);
        }
    }
}
