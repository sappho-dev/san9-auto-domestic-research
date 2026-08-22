using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;

namespace San9AutoDomestic.V8Transaction
{
    /// <summary>
    /// One city, one native domestic command, exactly five expected officers.
    /// It is a bound description to validate, never an authorization token.
    /// </summary>
    public sealed class SingleCommandRequest
    {
        public const int CurrentProtocolVersion = 2;
        public const int CityCount = 50;
        public const int CorpsCount = 50;
        public const int OfficerCount = 850;
        public const int GameMoneyMaximum = 1000000;
        public const int MaximumTimeToLiveMilliseconds = 10000;

        private readonly ReadOnlyCollection<int> _officerIds;

        public SingleCommandRequest(
            Guid requestId,
            ulong sequence,
            RegisteredDomesticCommand command,
            int cityId,
            int corpsId,
            IEnumerable<int> officerIds,
            int reserveMoney,
            FixedDigest contextDigest,
            FixedDigest generationDigest,
            int timeToLiveMilliseconds)
        {
            if (requestId == Guid.Empty)
            {
                throw new ArgumentException("A non-empty request id is required.", "requestId");
            }

            if (sequence == 0)
            {
                throw new ArgumentOutOfRangeException("sequence", "Sequence zero is reserved as absent.");
            }

            if (cityId < 0 || cityId >= CityCount)
            {
                throw new ArgumentOutOfRangeException("cityId", "The exact target has city ids 0..49.");
            }

            if (corpsId < 0 || corpsId >= CorpsCount)
            {
                throw new ArgumentOutOfRangeException("corpsId", "The exact target has corps ids 0..49.");
            }

            if (reserveMoney < 0 || reserveMoney > GameMoneyMaximum)
            {
                throw new ArgumentOutOfRangeException("reserveMoney");
            }

            if (contextDigest == null)
            {
                throw new ArgumentNullException("contextDigest");
            }

            if (generationDigest == null)
            {
                throw new ArgumentNullException("generationDigest");
            }

            if (timeToLiveMilliseconds <= 0
                || timeToLiveMilliseconds > MaximumTimeToLiveMilliseconds)
            {
                throw new ArgumentOutOfRangeException("timeToLiveMilliseconds");
            }

            NativeCommandDescriptor descriptor = NativeCommandDescriptorRegistry.GetRequired(command);
            List<int> copiedOfficerIds = CopyAndValidateOfficers(officerIds, descriptor.RequiredOfficerCount);

            RequestId = requestId;
            Sequence = sequence;
            Descriptor = descriptor;
            CityId = cityId;
            CorpsId = corpsId;
            ReserveMoney = reserveMoney;
            ContextDigest = contextDigest;
            GenerationDigest = generationDigest;
            TimeToLiveMilliseconds = timeToLiveMilliseconds;
            _officerIds = new ReadOnlyCollection<int>(copiedOfficerIds);
            RequestFingerprint = ComputeFingerprint();
        }

        public int ProtocolVersion
        {
            get { return CurrentProtocolVersion; }
        }

        public Guid RequestId { get; private set; }

        public ulong Sequence { get; private set; }

        public NativeCommandDescriptor Descriptor { get; private set; }

        public int CityId { get; private set; }

        public int CorpsId { get; private set; }

        public ReadOnlyCollection<int> OfficerIds
        {
            get { return _officerIds; }
        }

        /// <summary>
        /// Frozen Core policy value for this one task.  It has no profile
        /// semantics; expected command cost comes only from the registry.
        /// </summary>
        public int ReserveMoney { get; private set; }

        public FixedDigest ContextDigest { get; private set; }

        public FixedDigest GenerationDigest { get; private set; }

        /// <summary>
        /// Relative lifetime only.  The coordinator samples its own trusted
        /// monotonic clock and stamps the absolute deadline when TryStart
        /// wins the process slot; callers never self-report absolute time.
        /// </summary>
        public int TimeToLiveMilliseconds { get; private set; }

        public FixedDigest RequestFingerprint { get; private set; }

        public bool AuthorizesLiveMutation
        {
            get { return false; }
        }

        private static List<int> CopyAndValidateOfficers(IEnumerable<int> officerIds, int expectedCount)
        {
            if (officerIds == null)
            {
                throw new ArgumentNullException("officerIds");
            }

            List<int> copied = new List<int>(officerIds);
            if (copied.Count != expectedCount)
            {
                throw new ArgumentException("A V8 request must bind exactly five officers.", "officerIds");
            }

            HashSet<int> unique = new HashSet<int>();
            foreach (int officerId in copied)
            {
                if (officerId < 0 || officerId >= OfficerCount)
                {
                    throw new ArgumentOutOfRangeException("officerIds", "Officer ids must be in 0..849.");
                }

                if (!unique.Add(officerId))
                {
                    throw new ArgumentException("The five officer ids must be unique.", "officerIds");
                }
            }

            return copied;
        }

        private FixedDigest ComputeFingerprint()
        {
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write(CurrentProtocolVersion);
                writer.Write(RequestId.ToByteArray());
                writer.Write(Sequence);
                writer.Write((int)Descriptor.Command);
                writer.Write(Descriptor.Key);
                writer.Write(Descriptor.NativeCommandId);
                writer.Write((int)Descriptor.OuterTaskType);
                writer.Write(Descriptor.OuterTaskTypeKey);
                writer.Write(Descriptor.OuterTaskVptr);
                writer.Write((int)Descriptor.MoneyModel);
                writer.Write(Descriptor.RequiredOfficerCount);
                writer.Write(Descriptor.ExpectedCost);
                writer.Write(CityId);
                writer.Write(CorpsId);
                writer.Write(_officerIds.Count);
                foreach (int officerId in _officerIds)
                {
                    writer.Write(officerId);
                }

                writer.Write(ReserveMoney);
                writer.Write(ContextDigest.ToArray());
                writer.Write(GenerationDigest.ToArray());
                writer.Write(TimeToLiveMilliseconds);
                writer.Flush();

                using (SHA256 algorithm = SHA256.Create())
                {
                    return FixedDigest.Create(algorithm.ComputeHash(stream.ToArray()));
                }
            }
        }
    }
}
