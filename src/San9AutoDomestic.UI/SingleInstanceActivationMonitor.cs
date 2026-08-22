using System;
using System.Threading;

namespace San9AutoDomestic.UI
{
    internal sealed class SingleInstanceActivationMonitor : IDisposable
    {
        private readonly EventWaitHandle activationEvent;
        private readonly Action activationAction;
        private readonly ManualResetEvent stopping;
        private readonly int shutdownJoinMilliseconds;
        private Thread listenerThread;
        private int started;
        private int disposed;
        private int deferredStoppingDispose;
        private int shutdownTimedOut;

        public SingleInstanceActivationMonitor(
            EventWaitHandle activationEvent,
            Action activationAction)
            : this(activationEvent, activationAction, 2000)
        {
        }

        internal SingleInstanceActivationMonitor(
            EventWaitHandle activationEvent,
            Action activationAction,
            int shutdownJoinMilliseconds)
        {
            if (activationEvent == null)
            {
                throw new ArgumentNullException("activationEvent");
            }

            if (activationAction == null)
            {
                throw new ArgumentNullException("activationAction");
            }

            if (shutdownJoinMilliseconds <= 0)
            {
                throw new ArgumentOutOfRangeException("shutdownJoinMilliseconds");
            }

            this.activationEvent = activationEvent;
            this.activationAction = activationAction;
            this.shutdownJoinMilliseconds = shutdownJoinMilliseconds;
            stopping = new ManualResetEvent(false);
        }

        internal bool ShutdownTimedOutForTests
        {
            get { return Interlocked.CompareExchange(ref shutdownTimedOut, 0, 0) != 0; }
        }

        public void Start()
        {
            if (Interlocked.CompareExchange(ref disposed, 0, 0) != 0)
            {
                throw new ObjectDisposedException("SingleInstanceActivationMonitor");
            }

            if (Interlocked.CompareExchange(ref started, 1, 0) != 0)
            {
                return;
            }

            listenerThread = new Thread(Listen);
            listenerThread.Name = "San9AutoDomestic activation listener";
            listenerThread.IsBackground = true;
            listenerThread.Start();
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }

            stopping.Set();
            Thread thread = listenerThread;
            if (thread == null)
            {
                stopping.Dispose();
                return;
            }

            if (thread == Thread.CurrentThread)
            {
                Interlocked.Exchange(ref deferredStoppingDispose, 1);
                return;
            }

            if (thread.Join(shutdownJoinMilliseconds))
            {
                stopping.Dispose();
                return;
            }

            Interlocked.Exchange(ref shutdownTimedOut, 1);
            Interlocked.Exchange(ref deferredStoppingDispose, 1);
            if (!thread.IsAlive
                && Interlocked.CompareExchange(
                    ref deferredStoppingDispose,
                    0,
                    1) == 1)
            {
                stopping.Dispose();
            }
        }

        internal bool WaitForListenerExitForTests(int millisecondsTimeout)
        {
            Thread thread = listenerThread;
            return thread == null || thread.Join(millisecondsTimeout);
        }

        private void Listen()
        {
            WaitHandle[] handles = new WaitHandle[] { activationEvent, stopping };
            try
            {
                while (true)
                {
                    int signaled;
                    try
                    {
                        signaled = WaitHandle.WaitAny(handles);
                    }
                    catch (ObjectDisposedException)
                    {
                        return;
                    }

                    if (signaled != 0)
                    {
                        return;
                    }

                    try
                    {
                        activationAction();
                    }
                    catch (ObjectDisposedException)
                    {
                        return;
                    }
                    catch (InvalidOperationException)
                    {
                        if (Interlocked.CompareExchange(ref disposed, 0, 0) != 0)
                        {
                            return;
                        }
                    }

                    if (stopping.WaitOne(0))
                    {
                        return;
                    }
                }
            }
            finally
            {
                if (Interlocked.CompareExchange(
                    ref deferredStoppingDispose,
                    0,
                    1) == 1)
                {
                    stopping.Dispose();
                }
            }
        }
    }
}
