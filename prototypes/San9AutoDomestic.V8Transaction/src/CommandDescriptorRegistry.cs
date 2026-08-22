using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace San9AutoDomestic.V8Transaction
{
    /// <summary>
    /// Stable protocol identifiers.  These are not profile identifiers and
    /// deliberately contain no Basic/Wealthy branching concept.
    /// </summary>
    public enum RegisteredDomesticCommand
    {
        Unknown = 0,
        Patrol = 1,
        Commerce = 2,
        Cultivate = 3,
        Train = 4,
        Repair = 5
    }

    public enum NativeMoneyModel
    {
        PerOfficerFifty = 1,
        Zero = 2
    }

    public enum NativeOuterTaskType
    {
        Unknown = 0,
        Patrol = 1,
        Commerce = 2,
        Cultivate = 3,
        Train = 4,
        Repair = 5
    }

    public sealed class NativeCommandDescriptor
    {
        internal NativeCommandDescriptor(
            RegisteredDomesticCommand command,
            string key,
            int nativeCommandId,
            NativeOuterTaskType outerTaskType,
            string outerTaskTypeKey,
            uint outerTaskVptr,
            NativeMoneyModel moneyModel)
        {
            if (!Enum.IsDefined(typeof(RegisteredDomesticCommand), command)
                || command == RegisteredDomesticCommand.Unknown)
            {
                throw new ArgumentOutOfRangeException("command");
            }

            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Descriptor key is required.", "key");
            }

            if (nativeCommandId < 0)
            {
                throw new ArgumentOutOfRangeException("nativeCommandId");
            }

            if (!Enum.IsDefined(typeof(NativeOuterTaskType), outerTaskType)
                || outerTaskType == NativeOuterTaskType.Unknown
                || (int)outerTaskType != (int)command)
            {
                throw new ArgumentOutOfRangeException("outerTaskType");
            }

            if (string.IsNullOrWhiteSpace(outerTaskTypeKey))
            {
                throw new ArgumentException("Outer task type key is required.", "outerTaskTypeKey");
            }

            if (outerTaskVptr == 0)
            {
                throw new ArgumentOutOfRangeException("outerTaskVptr");
            }

            if (!Enum.IsDefined(typeof(NativeMoneyModel), moneyModel))
            {
                throw new ArgumentOutOfRangeException("moneyModel");
            }

            ValidateExactBinding(
                command,
                key,
                nativeCommandId,
                outerTaskTypeKey,
                outerTaskVptr,
                moneyModel);

            Command = command;
            Key = key;
            NativeCommandId = nativeCommandId;
            OuterTaskType = outerTaskType;
            OuterTaskTypeKey = outerTaskTypeKey;
            OuterTaskVptr = outerTaskVptr;
            MoneyModel = moneyModel;
        }

        private static void ValidateExactBinding(
            RegisteredDomesticCommand command,
            string key,
            int nativeCommandId,
            string outerTaskTypeKey,
            uint outerTaskVptr,
            NativeMoneyModel moneyModel)
        {
            string expectedKey;
            int expectedNativeId;
            string expectedTypeKey;
            uint expectedVptr;
            NativeMoneyModel expectedMoneyModel;
            switch (command)
            {
                case RegisteredDomesticCommand.Patrol:
                    expectedKey = "patrol";
                    expectedNativeId = 0;
                    expectedTypeKey = "outer-task-patrol";
                    expectedVptr = 0x0060B920U;
                    expectedMoneyModel = NativeMoneyModel.PerOfficerFifty;
                    break;
                case RegisteredDomesticCommand.Commerce:
                    expectedKey = "commerce";
                    expectedNativeId = 1;
                    expectedTypeKey = "outer-task-commerce";
                    expectedVptr = 0x0060CCB0U;
                    expectedMoneyModel = NativeMoneyModel.PerOfficerFifty;
                    break;
                case RegisteredDomesticCommand.Cultivate:
                    expectedKey = "cultivate";
                    expectedNativeId = 2;
                    expectedTypeKey = "outer-task-cultivate";
                    expectedVptr = 0x0060BBA0U;
                    expectedMoneyModel = NativeMoneyModel.PerOfficerFifty;
                    break;
                case RegisteredDomesticCommand.Train:
                    expectedKey = "train";
                    expectedNativeId = 5;
                    expectedTypeKey = "outer-task-train";
                    expectedVptr = 0x0060C370U;
                    expectedMoneyModel = NativeMoneyModel.Zero;
                    break;
                case RegisteredDomesticCommand.Repair:
                    expectedKey = "repair";
                    expectedNativeId = 3;
                    expectedTypeKey = "outer-task-repair";
                    expectedVptr = 0x0060BCE8U;
                    expectedMoneyModel = NativeMoneyModel.PerOfficerFifty;
                    break;
                default:
                    throw new ArgumentOutOfRangeException("command");
            }

            if (!string.Equals(key, expectedKey, StringComparison.Ordinal)
                || nativeCommandId != expectedNativeId
                || !string.Equals(outerTaskTypeKey, expectedTypeKey, StringComparison.Ordinal)
                || outerTaskVptr != expectedVptr
                || moneyModel != expectedMoneyModel)
            {
                throw new ArgumentException("Descriptor fields do not match the frozen exact-version registry.");
            }
        }

        public RegisteredDomesticCommand Command { get; private set; }

        public string Key { get; private set; }

        public int NativeCommandId { get; private set; }

        public NativeOuterTaskType OuterTaskType { get; private set; }

        /// <summary>
        /// Symbolic exact-version type binding.  No process address is carried
        /// by this offline protocol.
        /// </summary>
        public string OuterTaskTypeKey { get; private set; }

        public uint OuterTaskVptr { get; private set; }

        public NativeMoneyModel MoneyModel { get; private set; }

        public int RequiredOfficerCount
        {
            get { return 5; }
        }

        public int ExpectedCost
        {
            get
            {
                switch (MoneyModel)
                {
                    case NativeMoneyModel.Zero:
                        return 0;
                    case NativeMoneyModel.PerOfficerFifty:
                        return RequiredOfficerCount * 50;
                    default:
                        throw new InvalidOperationException("Unknown native money model.");
                }
            }
        }
    }

    /// <summary>
    /// Closed registry for the five native domestic commands proven by V7.
    /// Configuration adds/removes/reorders tasks in Core; it cannot add an
    /// unregistered business opcode to this protocol.
    /// </summary>
    public static class NativeCommandDescriptorRegistry
    {
        private static readonly ReadOnlyCollection<NativeCommandDescriptor> AllDescriptors;
        private static readonly IDictionary<RegisteredDomesticCommand, NativeCommandDescriptor> ByCommand;
        private static readonly IDictionary<string, NativeCommandDescriptor> ByKey;

        static NativeCommandDescriptorRegistry()
        {
            List<NativeCommandDescriptor> descriptors = new List<NativeCommandDescriptor>
            {
                new NativeCommandDescriptor(
                    RegisteredDomesticCommand.Patrol,
                    "patrol",
                    0,
                    NativeOuterTaskType.Patrol,
                    "outer-task-patrol",
                    0x0060B920U,
                    NativeMoneyModel.PerOfficerFifty),
                new NativeCommandDescriptor(
                    RegisteredDomesticCommand.Commerce,
                    "commerce",
                    1,
                    NativeOuterTaskType.Commerce,
                    "outer-task-commerce",
                    0x0060CCB0U,
                    NativeMoneyModel.PerOfficerFifty),
                new NativeCommandDescriptor(
                    RegisteredDomesticCommand.Cultivate,
                    "cultivate",
                    2,
                    NativeOuterTaskType.Cultivate,
                    "outer-task-cultivate",
                    0x0060BBA0U,
                    NativeMoneyModel.PerOfficerFifty),
                new NativeCommandDescriptor(
                    RegisteredDomesticCommand.Train,
                    "train",
                    5,
                    NativeOuterTaskType.Train,
                    "outer-task-train",
                    0x0060C370U,
                    NativeMoneyModel.Zero),
                new NativeCommandDescriptor(
                    RegisteredDomesticCommand.Repair,
                    "repair",
                    3,
                    NativeOuterTaskType.Repair,
                    "outer-task-repair",
                    0x0060BCE8U,
                    NativeMoneyModel.PerOfficerFifty)
            };

            Dictionary<RegisteredDomesticCommand, NativeCommandDescriptor> byCommand =
                new Dictionary<RegisteredDomesticCommand, NativeCommandDescriptor>();
            Dictionary<string, NativeCommandDescriptor> byKey =
                new Dictionary<string, NativeCommandDescriptor>(StringComparer.Ordinal);
            foreach (NativeCommandDescriptor descriptor in descriptors)
            {
                byCommand.Add(descriptor.Command, descriptor);
                byKey.Add(descriptor.Key, descriptor);
            }

            AllDescriptors = new ReadOnlyCollection<NativeCommandDescriptor>(descriptors);
            ByCommand = byCommand;
            ByKey = byKey;
        }

        public static ReadOnlyCollection<NativeCommandDescriptor> All
        {
            get { return AllDescriptors; }
        }

        public static NativeCommandDescriptor GetRequired(RegisteredDomesticCommand command)
        {
            NativeCommandDescriptor descriptor;
            if (!ByCommand.TryGetValue(command, out descriptor))
            {
                throw new ArgumentOutOfRangeException(
                    "command",
                    "Only the five registered native domestic commands are valid V8 transactions.");
            }

            return descriptor;
        }

        public static bool TryGetByKey(string key, out NativeCommandDescriptor descriptor)
        {
            if (key == null)
            {
                descriptor = null;
                return false;
            }

            return ByKey.TryGetValue(key, out descriptor);
        }
    }
}
