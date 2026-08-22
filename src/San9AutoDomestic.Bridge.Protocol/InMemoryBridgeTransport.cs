using System;
using System.Collections.Generic;

namespace San9AutoDomestic.Bridge.Protocol
{
    public interface IBridgeTransport
    {
        bool TrySend(byte[] frameBytes);

        bool TryReceive(out byte[] frameBytes);

        int PendingReceiveCount { get; }

        bool IsGameConnected { get; }
    }

    public sealed class InMemoryBridgeTransport : IBridgeTransport
    {
        private sealed class Channel
        {
            internal readonly object Sync = new object();
            internal readonly Queue<byte[]> Frames = new Queue<byte[]>();
        }

        private readonly Channel inbound;
        private readonly Channel outbound;
        private readonly int capacity;

        private InMemoryBridgeTransport(Channel inboundChannel, Channel outboundChannel, int queueCapacity)
        {
            inbound = inboundChannel;
            outbound = outboundChannel;
            capacity = queueCapacity;
        }

        public int PendingReceiveCount
        {
            get
            {
                lock (inbound.Sync)
                {
                    return inbound.Frames.Count;
                }
            }
        }

        public bool IsGameConnected
        {
            get { return false; }
        }

        public static void CreateDuplex(
            int queueCapacity,
            out InMemoryBridgeTransport endpointA,
            out InMemoryBridgeTransport endpointB)
        {
            if (queueCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException("queueCapacity");
            }

            Channel aToB = new Channel();
            Channel bToA = new Channel();
            endpointA = new InMemoryBridgeTransport(bToA, aToB, queueCapacity);
            endpointB = new InMemoryBridgeTransport(aToB, bToA, queueCapacity);
        }

        public bool TrySend(byte[] frameBytes)
        {
            if (frameBytes == null || frameBytes.Length != ProtocolWireLayout.FrameSize)
            {
                return false;
            }

            byte[] copy = (byte[])frameBytes.Clone();
            lock (outbound.Sync)
            {
                if (outbound.Frames.Count >= capacity)
                {
                    return false;
                }

                outbound.Frames.Enqueue(copy);
                return true;
            }
        }

        public bool TryReceive(out byte[] frameBytes)
        {
            lock (inbound.Sync)
            {
                if (inbound.Frames.Count == 0)
                {
                    frameBytes = null;
                    return false;
                }

                frameBytes = (byte[])inbound.Frames.Dequeue().Clone();
                return true;
            }
        }
    }
}
