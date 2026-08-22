using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace San9AutoDomestic.CommandBroker
{
    internal static class ControlledJournalDirectory
    {
        internal static string Validate(string path)
        {
            if (string.IsNullOrWhiteSpace(path)
                || !Path.IsPathRooted(path)
                || !Directory.Exists(path))
            {
                throw new ArgumentException("An existing absolute journal directory is required.", "path");
            }

            string fullPath = Path.GetFullPath(path);
            RejectReparsePointsInPath(fullPath);

            SecurityIdentifier currentSid;
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                currentSid = identity.User;
            }

            if (currentSid == null)
            {
                throw new UnauthorizedAccessException("The current Windows identity has no SID.");
            }

            DirectorySecurity security = Directory.GetAccessControl(fullPath);
            SecurityIdentifier owner = security.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
            if (owner == null || !owner.Equals(currentSid) || !security.AreAccessRulesProtected)
            {
                throw new UnauthorizedAccessException("Journal directory ownership or ACL protection is invalid.");
            }

            AuthorizationRuleCollection rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier));
            bool hasFullControl = false;
            foreach (AuthorizationRule authorizationRule in rules)
            {
                FileSystemAccessRule rule = authorizationRule as FileSystemAccessRule;
                SecurityIdentifier sid = rule == null ? null : rule.IdentityReference as SecurityIdentifier;
                if (rule == null || sid == null || !sid.Equals(currentSid) || rule.IsInherited)
                {
                    throw new UnauthorizedAccessException("Journal directory ACL is not current-user-only.");
                }

                if (rule.AccessControlType == AccessControlType.Allow
                    && (rule.FileSystemRights & FileSystemRights.FullControl) == FileSystemRights.FullControl
                    && (rule.InheritanceFlags & InheritanceFlags.ContainerInherit) != 0
                    && (rule.InheritanceFlags & InheritanceFlags.ObjectInherit) != 0
                    && rule.PropagationFlags == PropagationFlags.None)
                {
                    hasFullControl = true;
                }
            }

            if (!hasFullControl)
            {
                throw new UnauthorizedAccessException("Journal directory does not grant current-user full control.");
            }

            return fullPath;
        }

        internal static void ValidateControlledFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                throw new FileNotFoundException("A controlled file is required.", path);
            }

            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                throw new UnauthorizedAccessException("Reparse-point controlled files are forbidden.");
            }

            SecurityIdentifier currentSid;
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                currentSid = identity.User;
            }

            FileSecurity security = File.GetAccessControl(path);
            SecurityIdentifier owner = security.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
            if (currentSid == null || owner == null || !owner.Equals(currentSid))
            {
                throw new UnauthorizedAccessException("Controlled file ownership is invalid.");
            }

            AuthorizationRuleCollection rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier));
            bool hasCurrentUserAllow = false;
            foreach (AuthorizationRule authorizationRule in rules)
            {
                FileSystemAccessRule rule = authorizationRule as FileSystemAccessRule;
                SecurityIdentifier sid = rule == null ? null : rule.IdentityReference as SecurityIdentifier;
                if (rule == null || sid == null || !sid.Equals(currentSid))
                {
                    throw new UnauthorizedAccessException("Controlled file ACL is not current-user-only.");
                }

                if (rule.AccessControlType == AccessControlType.Allow
                    && (rule.FileSystemRights & FileSystemRights.Read) != 0
                    && (rule.FileSystemRights & FileSystemRights.Write) != 0)
                {
                    hasCurrentUserAllow = true;
                }
            }

            if (!hasCurrentUserAllow)
            {
                throw new UnauthorizedAccessException("Controlled file does not grant current-user read/write access.");
            }
        }

        private static void RejectReparsePointsInPath(string fullPath)
        {
            DirectoryInfo current = new DirectoryInfo(fullPath);
            while (current != null)
            {
                current.Refresh();
                if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new UnauthorizedAccessException("Reparse points in the journal directory ancestry are forbidden.");
                }

                current = current.Parent;
            }
        }
    }
}
