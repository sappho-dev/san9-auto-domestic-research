using System;

namespace San9AutoDomestic.UI
{
    public sealed class UiDiagnosticSession
    {
        internal UiDiagnosticSession(long generation, GamePresenceSnapshot presence)
        {
            Generation = generation;
            Presence = presence;
        }

        public long Generation { get; private set; }

        public GamePresenceSnapshot Presence { get; private set; }
    }

    public sealed class UiDiagnosticSessionGate
    {
        private long nextGeneration;
        private long activeGeneration;

        public UiDiagnosticSession Begin(GamePresenceSnapshot presence)
        {
            if (presence == null)
            {
                throw new ArgumentNullException("presence");
            }

            if (presence.Status != GamePresenceStatus.Online)
            {
                throw new ArgumentException("A diagnostic session requires an online target.", "presence");
            }

            nextGeneration++;
            if (nextGeneration <= 0)
            {
                nextGeneration = 1;
            }

            activeGeneration = nextGeneration;
            return new UiDiagnosticSession(activeGeneration, presence);
        }

        public void Invalidate()
        {
            activeGeneration = 0;
        }

        public bool TryComplete(
            UiDiagnosticSession session,
            GamePresenceSnapshot currentPresence)
        {
            if (session == null
                || currentPresence == null
                || activeGeneration == 0
                || session.Generation != activeGeneration
                || !session.Presence.IsSameGenerationAs(currentPresence))
            {
                return false;
            }

            activeGeneration = 0;
            return true;
        }
    }

    public static class UiDiagnosticRetryPolicy
    {
        public static bool ShouldStart(
            bool forced,
            GamePresenceSnapshot currentPresence,
            GamePresenceSnapshot lastAttemptedPresence,
            AssistantUiStateKind currentState,
            bool transientRetryAuthorized)
        {
            if (currentPresence == null
                || currentPresence.Status != GamePresenceStatus.Online)
            {
                return false;
            }

            if (forced)
            {
                return true;
            }

            if (lastAttemptedPresence == null
                || !lastAttemptedPresence.IsSameGenerationAs(currentPresence))
            {
                return true;
            }

            return currentState == AssistantUiStateKind.Faulted
                && transientRetryAuthorized;
        }
    }

    public sealed class TransientFaultRetrySchedule
    {
        private readonly TimeSpan baseDelay;
        private readonly int maximumAutomaticRetries;
        private int scheduledRetryCount;
        private bool hasPendingRetry;
        private DateTimeOffset retryDueUtc;

        public TransientFaultRetrySchedule(
            TimeSpan baseDelay,
            int maximumAutomaticRetries)
        {
            if (baseDelay <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException("baseDelay");
            }

            if (maximumAutomaticRetries <= 0 || maximumAutomaticRetries > 30)
            {
                throw new ArgumentOutOfRangeException("maximumAutomaticRetries");
            }

            this.baseDelay = baseDelay;
            this.maximumAutomaticRetries = maximumAutomaticRetries;
        }

        public int ScheduledRetryCount
        {
            get { return scheduledRetryCount; }
        }

        public int MaximumAutomaticRetries
        {
            get { return maximumAutomaticRetries; }
        }

        public bool HasPendingRetry
        {
            get { return hasPendingRetry; }
        }

        public bool IsExhausted
        {
            get
            {
                return !hasPendingRetry
                    && scheduledRetryCount >= maximumAutomaticRetries;
            }
        }

        public DateTimeOffset? RetryDueUtc
        {
            get
            {
                return hasPendingRetry
                    ? (DateTimeOffset?)retryDueUtc
                    : null;
            }
        }

        public bool ScheduleIfNeeded(DateTimeOffset nowUtc)
        {
            if (hasPendingRetry)
            {
                return true;
            }

            if (scheduledRetryCount >= maximumAutomaticRetries)
            {
                return false;
            }

            TimeSpan delay = CalculateDelay(scheduledRetryCount);
            retryDueUtc = nowUtc > DateTimeOffset.MaxValue - delay
                ? DateTimeOffset.MaxValue
                : nowUtc + delay;
            scheduledRetryCount++;
            hasPendingRetry = true;
            return true;
        }

        public bool TryConsume(DateTimeOffset nowUtc)
        {
            if (!hasPendingRetry || nowUtc < retryDueUtc)
            {
                return false;
            }

            hasPendingRetry = false;
            return true;
        }

        public void Reset()
        {
            scheduledRetryCount = 0;
            hasPendingRetry = false;
            retryDueUtc = default(DateTimeOffset);
        }

        private TimeSpan CalculateDelay(int retryIndex)
        {
            long ticks = baseDelay.Ticks;
            for (int index = 0; index < retryIndex; index++)
            {
                if (ticks > TimeSpan.MaxValue.Ticks / 2)
                {
                    ticks = TimeSpan.MaxValue.Ticks;
                    break;
                }

                ticks *= 2;
            }

            return TimeSpan.FromTicks(ticks);
        }
    }

    public static class SnapshotFreshness
    {
        public static bool IsFresh(
            DateTimeOffset nowUtc,
            DateTimeOffset completedUtc,
            TimeSpan maximumAge,
            out TimeSpan age)
        {
            if (maximumAge < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException("maximumAge");
            }

            age = nowUtc - completedUtc;
            return age >= TimeSpan.Zero && age <= maximumAge;
        }
    }

    public static class BoundedLogBuffer
    {
        public static string Append(
            string existing,
            string message,
            int maximumCharacters,
            int maximumLines)
        {
            if (maximumCharacters <= 0)
            {
                throw new ArgumentOutOfRangeException("maximumCharacters");
            }

            if (maximumLines <= 0)
            {
                throw new ArgumentOutOfRangeException("maximumLines");
            }

            string safeExisting = existing ?? string.Empty;
            string safeMessage = message ?? string.Empty;
            string combined = safeExisting.Length == 0
                ? safeMessage
                : safeExisting + Environment.NewLine + Environment.NewLine + safeMessage;
            combined = TrimCharactersFromStart(combined, maximumCharacters);
            return TrimLinesFromStart(combined, maximumLines);
        }

        public static int CountLines(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return 0;
            }

            int lines = 1;
            for (int index = 0; index < value.Length; index++)
            {
                if (value[index] == '\n')
                {
                    lines++;
                }
            }

            return lines;
        }

        private static string TrimCharactersFromStart(
            string value,
            int maximumCharacters)
        {
            if (value.Length <= maximumCharacters)
            {
                return value;
            }

            int start = value.Length - maximumCharacters;
            int nextLine = value.IndexOf('\n', start);
            if (nextLine >= start && nextLine + 1 < value.Length)
            {
                start = nextLine + 1;
            }

            return value.Substring(start);
        }

        private static string TrimLinesFromStart(string value, int maximumLines)
        {
            int linesToRemove = CountLines(value) - maximumLines;
            if (linesToRemove <= 0)
            {
                return value;
            }

            int start = 0;
            while (linesToRemove > 0)
            {
                int newline = value.IndexOf('\n', start);
                if (newline < 0)
                {
                    return string.Empty;
                }

                start = newline + 1;
                linesToRemove--;
            }

            return value.Substring(start);
        }
    }

    internal interface IUiClock
    {
        DateTimeOffset UtcNow { get; }
    }

    internal sealed class SystemUiClock : IUiClock
    {
        public DateTimeOffset UtcNow
        {
            get { return DateTimeOffset.UtcNow; }
        }
    }
}
