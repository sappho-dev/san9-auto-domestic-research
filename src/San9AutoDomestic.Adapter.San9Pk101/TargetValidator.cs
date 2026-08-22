using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;

namespace San9AutoDomestic.Adapter.San9Pk101
{
    public sealed class San9Pk101TargetValidator
    {
        public FileValidationDiagnostic Validate()
        {
            return Validate(San9Pk101Target.ExpectedExecutablePath);
        }

        public FileValidationDiagnostic Validate(string executablePath)
        {
            FileValidationDiagnostic result = new FileValidationDiagnostic();
            List<DiagnosticIssue> issues = new List<DiagnosticIssue>();
            result.RequestedPath = executablePath;

            if (string.IsNullOrWhiteSpace(executablePath))
            {
                issues.Add(new DiagnosticIssue(
                    "TARGET_PATH_EMPTY",
                    DiagnosticSeverity.Blocking,
                    "The target executable path is empty."));
                result.Issues = issues.ToArray();
                return result;
            }

            try
            {
                result.CanonicalPath = Path.GetFullPath(executablePath);
            }
            catch (Exception exception)
            {
                issues.Add(new DiagnosticIssue(
                    "TARGET_PATH_INVALID",
                    DiagnosticSeverity.Blocking,
                    exception.Message));
                result.Issues = issues.ToArray();
                return result;
            }

            string expectedPath = Path.GetFullPath(San9Pk101Target.ExpectedExecutablePath);
            result.PathMatches = PathsEqual(result.CanonicalPath, expectedPath);
            if (!result.PathMatches)
            {
                issues.Add(new DiagnosticIssue(
                    "TARGET_PATH_MISMATCH",
                    DiagnosticSeverity.Blocking,
                    string.Format("Expected exactly '{0}', got '{1}'.", expectedPath, result.CanonicalPath)));
            }

            result.Exists = File.Exists(result.CanonicalPath);
            if (!result.Exists)
            {
                issues.Add(new DiagnosticIssue(
                    "TARGET_FILE_NOT_FOUND",
                    DiagnosticSeverity.Blocking,
                    "The target executable does not exist."));
                result.Issues = issues.ToArray();
                return result;
            }

            try
            {
                FileInfo file = new FileInfo(result.CanonicalPath);
                result.ActualSize = file.Length;
                result.SizeMatches = file.Length == San9Pk101Target.ExpectedExecutableSize;
                if (!result.SizeMatches)
                {
                    issues.Add(new DiagnosticIssue(
                        "TARGET_SIZE_MISMATCH",
                        DiagnosticSeverity.Blocking,
                        string.Format("Expected {0} bytes, got {1} bytes.",
                            San9Pk101Target.ExpectedExecutableSize,
                            file.Length)));
                }
            }
            catch (Exception exception)
            {
                issues.Add(new DiagnosticIssue(
                    "TARGET_SIZE_READ_FAILED",
                    DiagnosticSeverity.Blocking,
                    exception.Message));
            }

            try
            {
                FileVersionInfo versionInfo = FileVersionInfo.GetVersionInfo(result.CanonicalPath);
                Version actualVersion = new Version(
                    versionInfo.FileMajorPart,
                    versionInfo.FileMinorPart,
                    versionInfo.FileBuildPart,
                    versionInfo.FilePrivatePart);
                result.ActualFileVersion = actualVersion.ToString();
                result.RawFileVersion = versionInfo.FileVersion;
                result.VersionMatches = actualVersion.Equals(San9Pk101Target.ExpectedFileVersion);
                if (!result.VersionMatches)
                {
                    issues.Add(new DiagnosticIssue(
                        "TARGET_VERSION_MISMATCH",
                        DiagnosticSeverity.Blocking,
                        string.Format("Expected {0}, got {1}.",
                            San9Pk101Target.ExpectedFileVersion,
                            actualVersion)));
                }
            }
            catch (Exception exception)
            {
                issues.Add(new DiagnosticIssue(
                    "TARGET_VERSION_READ_FAILED",
                    DiagnosticSeverity.Blocking,
                    exception.Message));
            }

            try
            {
                result.ActualSha256 = ComputeSha256(result.CanonicalPath);
                result.Sha256Matches = string.Equals(
                    result.ActualSha256,
                    San9Pk101Target.ExpectedSha256,
                    StringComparison.OrdinalIgnoreCase);
                if (!result.Sha256Matches)
                {
                    issues.Add(new DiagnosticIssue(
                        "TARGET_SHA256_MISMATCH",
                        DiagnosticSeverity.Blocking,
                        string.Format("Expected {0}, got {1}.",
                            San9Pk101Target.ExpectedSha256,
                            result.ActualSha256)));
                }
            }
            catch (Exception exception)
            {
                issues.Add(new DiagnosticIssue(
                    "TARGET_SHA256_READ_FAILED",
                    DiagnosticSeverity.Blocking,
                    exception.Message));
            }

            try
            {
                result.ActualPeMachine = ReadPeMachine(result.CanonicalPath);
                result.ArchitectureMatches = result.ActualPeMachine.Value == San9Pk101Target.ExpectedPeMachine;
                if (!result.ArchitectureMatches)
                {
                    issues.Add(new DiagnosticIssue(
                        "TARGET_ARCHITECTURE_MISMATCH",
                        DiagnosticSeverity.Blocking,
                        string.Format("Expected PE machine 0x{0:X4}, got 0x{1:X4}.",
                            San9Pk101Target.ExpectedPeMachine,
                            result.ActualPeMachine.Value)));
                }
            }
            catch (Exception exception)
            {
                issues.Add(new DiagnosticIssue(
                    "TARGET_PE_READ_FAILED",
                    DiagnosticSeverity.Blocking,
                    exception.Message));
            }

            result.IsValid = result.PathMatches
                && result.Exists
                && result.SizeMatches
                && result.VersionMatches
                && result.Sha256Matches
                && result.ArchitectureMatches;
            result.Issues = issues.ToArray();
            return result;
        }

        internal static bool PathsEqual(string left, string right)
        {
            if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right))
            {
                return false;
            }

            try
            {
                return string.Equals(
                    Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static string ComputeSha256(string path)
        {
            using (FileStream stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete))
            using (SHA256 algorithm = SHA256.Create())
            {
                byte[] hash = algorithm.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", string.Empty);
            }
        }

        private static ushort ReadPeMachine(string path)
        {
            using (FileStream stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                if (stream.Length < 64 || reader.ReadUInt16() != 0x5a4d)
                {
                    throw new InvalidDataException("The file does not contain a valid DOS header.");
                }

                stream.Position = 0x3c;
                int peOffset = reader.ReadInt32();
                if (peOffset < 0 || peOffset > stream.Length - 6)
                {
                    throw new InvalidDataException("The PE header offset is outside the file.");
                }

                stream.Position = peOffset;
                if (reader.ReadUInt32() != 0x00004550)
                {
                    throw new InvalidDataException("The file does not contain a valid PE signature.");
                }

                return reader.ReadUInt16();
            }
        }
    }
}
