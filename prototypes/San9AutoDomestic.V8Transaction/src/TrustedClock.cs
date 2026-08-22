using System.Diagnostics;

namespace San9AutoDomestic.V8Transaction
{
    internal interface ITrustedMonotonicClock
    {
        long Frequency { get; }

        long GetTimestamp();
    }

    internal sealed class SystemTrustedMonotonicClock : ITrustedMonotonicClock
    {
        private static readonly SystemTrustedMonotonicClock Singleton =
            new SystemTrustedMonotonicClock();

        private SystemTrustedMonotonicClock()
        {
        }

        internal static SystemTrustedMonotonicClock Instance
        {
            get { return Singleton; }
        }

        public long Frequency
        {
            get { return Stopwatch.Frequency; }
        }

        public long GetTimestamp()
        {
            return Stopwatch.GetTimestamp();
        }
    }
}
