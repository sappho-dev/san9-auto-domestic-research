using System;
using System.Collections.Generic;

namespace San9AutoDomestic.CommandBroker
{
    public sealed class InMemoryFrameTransport : IDisposable
    {
        private readonly object sync;
        private readonly Queue<byte[]> frames;
        private readonly int capacity;
        private readonly int maximumFrameBytes;
        private bool closed;

        public InMemoryFrameTransport(int capacity, int maximumFrameBytes)
        {
            if (capacity <= 0 || capacity > 1024)
            {
                throw new ArgumentOutOfRangeException("capacity");
            }

            if (maximumFrameBytes <= 0
                || maximumFrameBytes > AuthenticatedIpcProtocol.MaximumFrameBytes)
            {
                throw new ArgumentOutOfRangeException("maximumFrameBytes");
            }

            sync = new object();
            frames = new Queue<byte[]>(capacity);
            this.capacity = capacity;
            this.maximumFrameBytes = maximumFrameBytes;
        }

        public int Count
        {
            get
            {
                lock (sync)
                {
                    return frames.Count;
                }
            }
        }

        public bool TrySend(byte[] frame)
        {
            if (frame == null)
            {
                throw new ArgumentNullException("frame");
            }

            if (frame.Length > maximumFrameBytes)
            {
                throw new ArgumentException("Frame exceeds the in-memory transport bound.", "frame");
            }

            lock (sync)
            {
                if (closed || frames.Count >= capacity)
                {
                    return false;
                }

                frames.Enqueue(BinaryValue.Clone(frame));
                return true;
            }
        }

        public bool TryReceive(out byte[] frame)
        {
            lock (sync)
            {
                if (frames.Count == 0)
                {
                    frame = null;
                    return false;
                }

                frame = BinaryValue.Clone(frames.Dequeue());
                return true;
            }
        }

        public void Close()
        {
            lock (sync)
            {
                closed = true;
            }
        }

        public void Dispose()
        {
            lock (sync)
            {
                closed = true;
                while (frames.Count != 0)
                {
                    byte[] frame = frames.Dequeue();
                    Array.Clear(frame, 0, frame.Length);
                }
            }
        }
    }
}
