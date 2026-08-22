using System;
using System.Threading;

namespace San9AutoDomestic.Input.Win32
{
    internal static class DomesticExecutionBatchLease
    {
        private static int inFlight;

        internal static bool TryAcquire(out Lease lease)
        {
            if (Interlocked.CompareExchange(ref inFlight, 1, 0) != 0)
            {
                lease = null;
                return false;
            }
            lease = new Lease();
            return true;
        }

        internal sealed class Lease : IDisposable
        {
            private int released;

            internal bool IsHeld { get { return Volatile.Read(ref released) == 0; } }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref released, 1) == 0)
                    Interlocked.Exchange(ref inFlight, 0);
            }
        }
    }
}
