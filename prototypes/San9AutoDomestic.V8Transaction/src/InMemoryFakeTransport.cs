using System;
using System.Collections.Generic;

namespace San9AutoDomestic.V8Transaction
{
    internal enum FakeEnvelopeKind
    {
        Request = 1,
        Evidence = 2,
        Transition = 3
    }

    internal sealed class FakeProtocolEnvelope
    {
        private FakeProtocolEnvelope(
            FakeEnvelopeKind kind,
            SingleCommandRequest request,
            SyntheticStageEvidence evidence,
            TransactionTransition transition)
        {
            Kind = kind;
            Request = request;
            Evidence = evidence;
            Transition = transition;
        }

        public FakeEnvelopeKind Kind { get; private set; }

        public SingleCommandRequest Request { get; private set; }

        public SyntheticStageEvidence Evidence { get; private set; }

        public TransactionTransition Transition { get; private set; }

        public static FakeProtocolEnvelope ForRequest(SingleCommandRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException("request");
            }

            return new FakeProtocolEnvelope(FakeEnvelopeKind.Request, request, null, null);
        }

        public static FakeProtocolEnvelope ForEvidence(SyntheticStageEvidence evidence)
        {
            if (evidence == null)
            {
                throw new ArgumentNullException("evidence");
            }

            return new FakeProtocolEnvelope(FakeEnvelopeKind.Evidence, null, evidence, null);
        }

        public static FakeProtocolEnvelope ForTransition(TransactionTransition transition)
        {
            if (transition == null)
            {
                throw new ArgumentNullException("transition");
            }

            return new FakeProtocolEnvelope(FakeEnvelopeKind.Transition, null, null, transition);
        }
    }

    internal interface ITransactionTransport
    {
        bool TrySend(FakeProtocolEnvelope envelope);

        bool TryReceive(out FakeProtocolEnvelope envelope);

        bool IsLiveConnection { get; }

        bool CanAuthorizeNativeMutation { get; }
    }

    /// <summary>
    /// Bounded in-memory duplex endpoint.  There is intentionally no named
    /// pipe, mapping, socket, process handle, or Windows-message transport.
    /// </summary>
    internal sealed class InMemoryFakeTransportEndpoint : ITransactionTransport
    {
        private readonly Queue<FakeProtocolEnvelope> _incoming;
        private readonly object _incomingLock;
        private readonly int _capacity;
        private InMemoryFakeTransportEndpoint _peer;

        private InMemoryFakeTransportEndpoint(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException("capacity");
            }

            _capacity = capacity;
            _incoming = new Queue<FakeProtocolEnvelope>();
            _incomingLock = new object();
        }

        public bool IsLiveConnection
        {
            get { return false; }
        }

        public bool CanAuthorizeNativeMutation
        {
            get { return false; }
        }

        public int PendingReceiveCount
        {
            get
            {
                lock (_incomingLock)
                {
                    return _incoming.Count;
                }
            }
        }

        public static void CreateDuplex(
            int capacity,
            out InMemoryFakeTransportEndpoint first,
            out InMemoryFakeTransportEndpoint second)
        {
            first = new InMemoryFakeTransportEndpoint(capacity);
            second = new InMemoryFakeTransportEndpoint(capacity);
            first._peer = second;
            second._peer = first;
        }

        public bool TrySend(FakeProtocolEnvelope envelope)
        {
            if (envelope == null)
            {
                return false;
            }

            InMemoryFakeTransportEndpoint peer = _peer;
            if (peer == null)
            {
                return false;
            }

            lock (peer._incomingLock)
            {
                if (peer._incoming.Count >= peer._capacity)
                {
                    return false;
                }

                peer._incoming.Enqueue(envelope);
                return true;
            }
        }

        public bool TryReceive(out FakeProtocolEnvelope envelope)
        {
            lock (_incomingLock)
            {
                if (_incoming.Count == 0)
                {
                    envelope = null;
                    return false;
                }

                envelope = _incoming.Dequeue();
                return true;
            }
        }
    }
}
