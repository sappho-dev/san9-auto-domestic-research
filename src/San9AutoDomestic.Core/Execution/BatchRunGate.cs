using System;
using System.Threading;

namespace San9AutoDomestic.Core.Execution
{
    /// <summary>
    /// The only public batch entry coordinator in this process. Callers cannot
    /// construct independent gates and accidentally bypass mutual exclusion.
    /// </summary>
    public sealed class BatchRunCoordinator
    {
        private static readonly BatchRunCoordinator _processWide = new BatchRunCoordinator();
        private readonly BatchRunGate _gate;

        private BatchRunCoordinator()
        {
            _gate = new BatchRunGate();
        }

        public static BatchRunCoordinator ProcessWide
        {
            get { return _processWide; }
        }

        public bool IsRunning
        {
            get { return _gate.IsRunning; }
        }

        public long CurrentGeneration
        {
            get { return _gate.CurrentGeneration; }
        }

        public bool TryBegin(out BatchRunLease lease)
        {
            return _gate.TryAcquire(out lease);
        }
    }

    internal sealed class BatchRunGate
    {
        private readonly object _sync = new object();
        private long _generation;
        private BatchRunLease _activeLease;

        public bool IsRunning
        {
            get
            {
                lock (_sync)
                {
                    return _activeLease != null;
                }
            }
        }

        public long CurrentGeneration
        {
            get
            {
                lock (_sync)
                {
                    return _generation;
                }
            }
        }

        public bool TryAcquire(out BatchRunLease lease)
        {
            lock (_sync)
            {
                if (_activeLease != null)
                {
                    lease = null;
                    return false;
                }

                checked
                {
                    _generation++;
                }

                lease = new BatchRunLease(this, _generation);
                _activeLease = lease;
                return true;
            }
        }

        internal void Release(BatchRunLease lease, long generation)
        {
            lock (_sync)
            {
                if (ReferenceEquals(_activeLease, lease)
                    && _generation == generation)
                {
                    _activeLease = null;
                }
            }
        }
    }

    public sealed class BatchRunLease : IDisposable
    {
        private readonly BatchRunGate _owner;
        private int _isDisposed;

        internal BatchRunLease(BatchRunGate owner, long generation)
        {
            _owner = owner;
            Generation = generation;
        }

        public long Generation { get; private set; }

        public bool IsDisposed
        {
            get { return Interlocked.CompareExchange(ref _isDisposed, 0, 0) != 0; }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _isDisposed, 1) == 0)
            {
                _owner.Release(this, Generation);
            }
        }
    }
}
