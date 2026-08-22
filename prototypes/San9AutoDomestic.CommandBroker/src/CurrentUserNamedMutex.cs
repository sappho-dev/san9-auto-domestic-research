using System;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;

namespace San9AutoDomestic.CommandBroker
{
    public enum NamedMutexAcquireStatus
    {
        Acquired = 1,
        Busy = 2,
        AbandonedRestartRequired = 3,
        ReadyTimeoutRestartRequired = 4,
        OwnershipReleaseTimeoutRestartRequired = 5,
        Failed = 6,
        OwnershipReleaseFailedRestartRequired = 7
    }

    public enum NamedMutexReleaseStatus
    {
        Released = 1,
        TimedOutOwnershipUnknown = 2,
        AlreadyReleased = 3,
        FailedRestartRequired = 4
    }

    public sealed class NamedMutexAcquireResult
    {
        internal NamedMutexAcquireResult(NamedMutexAcquireStatus status, NamedMutexLease lease, string errorMessage)
        {
            Status = status;
            Lease = lease;
            ErrorMessage = errorMessage ?? string.Empty;
        }

        public NamedMutexAcquireStatus Status { get; private set; }

        public NamedMutexLease Lease { get; private set; }

        public string ErrorMessage { get; private set; }
    }

    public sealed class CurrentUserNamedMutex
    {
        private const string RequiredPrefix = "Local\\San9AutoDomestic.CommandBroker.";
        private const int DefaultReadyTimeoutMilliseconds = 5000;
        private const int DefaultReleaseTimeoutMilliseconds = 5000;
        private readonly string name;
        private readonly int readyTimeoutMilliseconds;
        private readonly int releaseTimeoutMilliseconds;

        public CurrentUserNamedMutex(string name)
            : this(name, DefaultReadyTimeoutMilliseconds, DefaultReleaseTimeoutMilliseconds)
        {
        }

        public CurrentUserNamedMutex(
            string name,
            int readyTimeoutMilliseconds,
            int releaseTimeoutMilliseconds)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("A mutex name is required.", "name");
            }

            if (!name.StartsWith(RequiredPrefix, StringComparison.Ordinal) || name.Length > 240)
            {
                throw new ArgumentException("Mutex names must use the local CommandBroker namespace.", "name");
            }

            if (readyTimeoutMilliseconds < 0 || readyTimeoutMilliseconds > 30000)
            {
                throw new ArgumentOutOfRangeException("readyTimeoutMilliseconds");
            }

            if (releaseTimeoutMilliseconds < 0 || releaseTimeoutMilliseconds > 30000)
            {
                throw new ArgumentOutOfRangeException("releaseTimeoutMilliseconds");
            }

            this.name = name;
            this.readyTimeoutMilliseconds = readyTimeoutMilliseconds;
            this.releaseTimeoutMilliseconds = releaseTimeoutMilliseconds;
        }

        public string Name
        {
            get { return name; }
        }

        public static string CreateName(ProcessSessionIdentity identity)
        {
            if (identity == null)
            {
                throw new ArgumentNullException("identity");
            }

            return RequiredPrefix + CreateSessionToken(identity);
        }

        internal static string CreateSessionToken(ProcessSessionIdentity identity)
        {
            if (identity == null)
            {
                throw new ArgumentNullException("identity");
            }

            byte[] digest = BinaryValue.ComputeSha256(identity.GetCanonicalBytes());
            return BinaryValue.ToHex(digest);
        }

        public NamedMutexAcquireResult TryAcquire()
        {
            MutexOwnerState owner = new MutexOwnerState(name);
            owner.Start();
            bool becameReady = readyTimeoutMilliseconds != 0
                && owner.WaitUntilReady(readyTimeoutMilliseconds);
            if (!becameReady)
            {
                NamedMutexReleaseStatus cleanup = owner.TryRelease(releaseTimeoutMilliseconds);
                NamedMutexAcquireStatus timeoutStatus = cleanup == NamedMutexReleaseStatus.TimedOutOwnershipUnknown
                    ? NamedMutexAcquireStatus.OwnershipReleaseTimeoutRestartRequired
                    : cleanup == NamedMutexReleaseStatus.FailedRestartRequired
                        ? NamedMutexAcquireStatus.OwnershipReleaseFailedRestartRequired
                        : NamedMutexAcquireStatus.ReadyTimeoutRestartRequired;
                return new NamedMutexAcquireResult(
                    timeoutStatus,
                    null,
                    "Named-mutex owner readiness timed out; ownership was not granted.");
            }

            if (owner.Status == NamedMutexAcquireStatus.Acquired)
            {
                return new NamedMutexAcquireResult(
                    NamedMutexAcquireStatus.Acquired,
                    new NamedMutexLease(owner, releaseTimeoutMilliseconds),
                    string.Empty);
            }

            NamedMutexAcquireStatus status = owner.Status;
            string errorMessage = owner.ErrorMessage;
            NamedMutexReleaseStatus releaseStatus = owner.TryRelease(releaseTimeoutMilliseconds);
            if (releaseStatus == NamedMutexReleaseStatus.TimedOutOwnershipUnknown)
            {
                status = NamedMutexAcquireStatus.OwnershipReleaseTimeoutRestartRequired;
                errorMessage = "Named-mutex ownership cleanup timed out and remains unknown.";
            }
            else if (releaseStatus == NamedMutexReleaseStatus.FailedRestartRequired)
            {
                status = NamedMutexAcquireStatus.OwnershipReleaseFailedRestartRequired;
                errorMessage = "Named-mutex ownership cleanup failed and requires restart.";
            }

            return new NamedMutexAcquireResult(status, null, errorMessage);
        }

        internal sealed class MutexOwnerState
        {
            private readonly string name;
            private readonly ManualResetEvent ready;
            private readonly ManualResetEvent release;
            private readonly ManualResetEvent completed;
            private int eventsDisposed;

            internal MutexOwnerState(string name)
            {
                this.name = name;
                ready = new ManualResetEvent(false);
                release = new ManualResetEvent(false);
                completed = new ManualResetEvent(false);
                Status = NamedMutexAcquireStatus.Failed;
                ErrorMessage = string.Empty;
            }

            internal NamedMutexAcquireStatus Status { get; private set; }

            internal string ErrorMessage { get; private set; }

            internal void Start()
            {
                Thread thread = new Thread(Run);
                thread.IsBackground = true;
                thread.Name = "CommandBroker named-mutex owner";
                thread.Start();
            }

            internal bool WaitUntilReady(int timeoutMilliseconds)
            {
                return ready.WaitOne(timeoutMilliseconds);
            }

            internal NamedMutexReleaseStatus TryRelease(int timeoutMilliseconds)
            {
                release.Set();
                bool finished = timeoutMilliseconds != 0
                    && completed.WaitOne(timeoutMilliseconds);
                if (!finished)
                {
                    return NamedMutexReleaseStatus.TimedOutOwnershipUnknown;
                }

                DisposeEventsOnce();
                return Status == NamedMutexAcquireStatus.Failed
                    ? NamedMutexReleaseStatus.FailedRestartRequired
                    : NamedMutexReleaseStatus.Released;
            }

            private void DisposeEventsOnce()
            {
                if (Interlocked.Exchange(ref eventsDisposed, 1) == 0)
                {
                    ready.Dispose();
                    release.Dispose();
                    completed.Dispose();
                }
            }

            private void Run()
            {
                Mutex mutex = null;
                bool acquired = false;
                try
                {
                    SecurityIdentifier currentSid;
                    using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                    {
                        currentSid = identity.User;
                    }

                    if (currentSid == null)
                    {
                        throw new InvalidOperationException("The current Windows identity has no SID.");
                    }

                    MutexSecurity security = CreateCurrentUserOnlySecurity(currentSid);
                    bool createdNew;
                    mutex = new Mutex(false, name, out createdNew, security);
                    if (!HasCurrentUserOnlySecurity(mutex, currentSid))
                    {
                        throw new UnauthorizedAccessException("The named mutex ACL is not current-user-only.");
                    }

                    try
                    {
                        acquired = mutex.WaitOne(0, false);
                        Status = acquired ? NamedMutexAcquireStatus.Acquired : NamedMutexAcquireStatus.Busy;
                    }
                    catch (AbandonedMutexException)
                    {
                        acquired = true;
                        Status = NamedMutexAcquireStatus.AbandonedRestartRequired;
                    }
                }
                catch (Exception exception)
                {
                    Status = NamedMutexAcquireStatus.Failed;
                    ErrorMessage = exception.GetType().Name + ": " + exception.Message;
                }
                finally
                {
                    ready.Set();
                }

                try
                {
                    if (acquired)
                    {
                        release.WaitOne();
                        mutex.ReleaseMutex();
                    }
                }
                catch (ApplicationException exception)
                {
                    Status = NamedMutexAcquireStatus.Failed;
                    ErrorMessage = exception.GetType().Name + ": " + exception.Message;
                }
                finally
                {
                    if (mutex != null)
                    {
                        mutex.Dispose();
                    }

                    completed.Set();
                }
            }

            private static MutexSecurity CreateCurrentUserOnlySecurity(SecurityIdentifier currentSid)
            {
                MutexSecurity security = new MutexSecurity();
                security.SetAccessRuleProtection(true, false);
                security.AddAccessRule(
                    new MutexAccessRule(
                        currentSid,
                        MutexRights.FullControl,
                        AccessControlType.Allow));
                return security;
            }

            private static bool HasCurrentUserOnlySecurity(Mutex mutex, SecurityIdentifier currentSid)
            {
                MutexSecurity security = mutex.GetAccessControl();
                if (!security.AreAccessRulesProtected)
                {
                    return false;
                }

                AuthorizationRuleCollection rules = security.GetAccessRules(
                    true,
                    true,
                    typeof(SecurityIdentifier));
                bool hasFullControl = false;
                foreach (AuthorizationRule authorizationRule in rules)
                {
                    MutexAccessRule rule = authorizationRule as MutexAccessRule;
                    SecurityIdentifier sid = rule == null ? null : rule.IdentityReference as SecurityIdentifier;
                    if (rule == null || sid == null || !sid.Equals(currentSid) || rule.IsInherited)
                    {
                        return false;
                    }

                    if (rule.AccessControlType == AccessControlType.Allow
                        && (rule.MutexRights & MutexRights.FullControl) == MutexRights.FullControl)
                    {
                        hasFullControl = true;
                    }
                }

                return hasFullControl;
            }
        }
    }

    public sealed class NamedMutexLease : IDisposable
    {
        private readonly object sync;
        private readonly int defaultReleaseTimeoutMilliseconds;
        private CurrentUserNamedMutex.MutexOwnerState owner;

        internal NamedMutexLease(
            CurrentUserNamedMutex.MutexOwnerState owner,
            int defaultReleaseTimeoutMilliseconds)
        {
            sync = new object();
            this.owner = owner;
            this.defaultReleaseTimeoutMilliseconds = defaultReleaseTimeoutMilliseconds;
        }

        public bool IsHeld
        {
            get { return Volatile.Read(ref owner) != null; }
        }

        public void Dispose()
        {
            ReleaseDefault();
        }

        internal NamedMutexReleaseStatus ReleaseDefault()
        {
            return Release(defaultReleaseTimeoutMilliseconds);
        }

        public NamedMutexReleaseStatus Release(int timeoutMilliseconds)
        {
            if (timeoutMilliseconds < 0 || timeoutMilliseconds > 30000)
            {
                throw new ArgumentOutOfRangeException("timeoutMilliseconds");
            }

            lock (sync)
            {
                if (owner == null)
                {
                    return NamedMutexReleaseStatus.AlreadyReleased;
                }

                NamedMutexReleaseStatus result = owner.TryRelease(timeoutMilliseconds);
                if (result == NamedMutexReleaseStatus.Released)
                {
                    owner = null;
                }
                else if (result == NamedMutexReleaseStatus.FailedRestartRequired)
                {
                    owner = null;
                }

                return result;
            }
        }
    }
}
