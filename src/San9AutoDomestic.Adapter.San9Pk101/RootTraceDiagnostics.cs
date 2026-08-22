using System;
using System.Collections.Generic;

namespace San9AutoDomestic.Adapter.San9Pk101
{
    public sealed class San9Pk101RootTraceSample
    {
        internal San9Pk101RootTraceSample()
        {
            CapturedUtc = DateTimeOffset.UtcNow;
        }

        public bool ReadSucceeded { get; internal set; }
        public string FailureCode { get; internal set; }
        public string FailureMessage { get; internal set; }
        public DateTimeOffset CapturedUtc { get; internal set; }
        public int? ProcessId { get; internal set; }
        public long? ProcessCreationFileTimeUtc { get; internal set; }
        public uint WindowPointer { get; internal set; }
        public uint OwnerPointer { get; internal set; }
        public uint ScenePointer { get; internal set; }
        public uint SchedulerRootPointer { get; internal set; }
        public uint SchedulerRootVtable { get; internal set; }
        public uint SchedulerRootPendingPointer { get; internal set; }
        public uint? DomesticControllerPointer { get; internal set; }
        public int? DomesticControllerDepth { get; internal set; }
        public uint? DomesticControllerCorpsPointer { get; internal set; }
        public uint? DomesticControllerState { get; internal set; }
        public uint? DomesticControllerTargetPointer { get; internal set; }
        public uint LeafPointer { get; internal set; }
        public uint LeafVtable { get; internal set; }
        public uint LeafTickFunctionPointer { get; internal set; }
        public uint LeafPendingPointer { get; internal set; }
        public int DescendantDepth { get; internal set; }
        public bool StructureDoubleReadStable { get; internal set; }
        public int StabilityAttemptCount { get; internal set; }

        public bool IsActionable
        {
            get { return false; }
        }

        public bool ExecutionAuthorized
        {
            get { return false; }
        }
    }

    internal sealed class San9Pk101RootTraceReader
    {
        internal const uint AppObjectAddress = 0x01228340;
        internal const uint ExpectedSchedulerRootVtable = 0x00607560;
        internal const uint ExpectedDomesticControllerVtable = 0x00610bc8;
        internal const int MaximumDescendantDepth = 64;
        internal const int MaximumStabilityRetries = 3;

        private const uint AppWindowOffset = 0x04;
        private const uint WindowOwnerOffset = 0x1c;
        private const uint OwnerSceneOffset = 0x18;
        private const uint SceneSchedulerRootOffset = 0x8c;
        private const uint TaskChildOffset = 0x0c;
        private const uint TaskPendingOffset = 0x10;
        private const uint RootCorpsOffset = 0x30;
        private const uint RootStateOffset = 0x34;
        private const uint RootTargetOffset = 0x38;

        internal San9Pk101RootTraceSample Read(IReadOnlyProcessMemory memory)
        {
            if (memory == null)
            {
                return Failure("READ_ONLY_MEMORY_REQUIRED", "A read-only process-memory source is required.", null);
            }

            ProcessIdentitySnapshot initial;
            try
            {
                initial = memory.InitialIdentity;
            }
            catch (Exception exception)
            {
                return Failure("PROCESS_BINDING_UNAVAILABLE", exception.Message, null);
            }

            string initialError;
            if (!ValidateExactInitialIdentity(memory, initial, out initialError))
            {
                return Failure("PROCESS_BINDING_INVALID", initialError, initial);
            }

            ProcessIdentitySnapshot before;
            string identityError;
            if (!TryCaptureIdentity(memory, out before, out identityError))
            {
                return Failure("PROCESS_IDENTITY_CAPTURE_FAILED", identityError, initial);
            }

            if (!initial.SameGenerationAndImage(before))
            {
                return Failure(
                    "PROCESS_GENERATION_CHANGED_BEFORE_SAMPLE",
                    "The process generation or exact main-module identity changed before this sample.",
                    initial);
            }

            RootTraceValues values = null;
            int stabilityAttemptCount = 0;
            for (int retry = 0; retry <= MaximumStabilityRetries; retry++)
            {
                RootTraceValues first;
                RootTraceValues second;
                try
                {
                    first = ReadValues(memory, initial);
                    second = ReadValues(memory, initial);
                }
                catch (RootTraceReadException exception)
                {
                    return Failure(exception.Code, exception.Message, initial);
                }
                catch (Exception exception)
                {
                    return Failure("MEMORY_READ_FAILED", exception.Message, initial);
                }

                stabilityAttemptCount = retry + 1;
                ProcessIdentitySnapshot afterPass;
                if (!TryCaptureIdentity(memory, out afterPass, out identityError))
                {
                    return Failure("PROCESS_IDENTITY_POSTCHECK_FAILED", identityError, initial);
                }

                if (!initial.SameGenerationAndImage(afterPass)
                    || !before.SameGenerationAndImage(afterPass))
                {
                    return Failure(
                        "PROCESS_GENERATION_CHANGED_DURING_SAMPLE",
                        "The process generation or exact main-module identity changed while this sample was read.",
                        initial);
                }

                if (first.StructurallyEquals(second))
                {
                    values = second;
                    break;
                }
            }

            if (values == null)
            {
                San9Pk101RootTraceSample changed = Failure(
                    "ROOT_TRACE_CHANGED_DURING_SAMPLE",
                    string.Format(
                        "The complete root/task structure did not match across A/B reads after {0} attempts ({1} retries).",
                        stabilityAttemptCount,
                        MaximumStabilityRetries),
                    initial);
                changed.StabilityAttemptCount = stabilityAttemptCount;
                return changed;
            }

            return new San9Pk101RootTraceSample
            {
                ReadSucceeded = true,
                ProcessId = initial.ProcessId,
                ProcessCreationFileTimeUtc = initial.CreationFileTimeUtc,
                WindowPointer = values.WindowPointer,
                OwnerPointer = values.OwnerPointer,
                ScenePointer = values.ScenePointer,
                SchedulerRootPointer = values.SchedulerRootPointer,
                SchedulerRootVtable = values.SchedulerRootVtable,
                SchedulerRootPendingPointer = values.SchedulerRootPendingPointer,
                DomesticControllerPointer = values.DomesticControllerPointer,
                DomesticControllerDepth = values.DomesticControllerDepth,
                DomesticControllerCorpsPointer = values.DomesticControllerCorpsPointer,
                DomesticControllerState = values.DomesticControllerState,
                DomesticControllerTargetPointer = values.DomesticControllerTargetPointer,
                LeafPointer = values.LeafPointer,
                LeafVtable = values.LeafVtable,
                LeafTickFunctionPointer = values.LeafTickFunctionPointer,
                LeafPendingPointer = values.LeafPendingPointer,
                DescendantDepth = values.DescendantDepth,
                StructureDoubleReadStable = true,
                StabilityAttemptCount = stabilityAttemptCount
            };
        }

        private static RootTraceValues ReadValues(
            IReadOnlyProcessMemory memory,
            ProcessIdentitySnapshot identity)
        {
            uint window = ReadRequiredObjectPointer(
                memory,
                AddAddress(AppObjectAddress, AppWindowOffset, "app+0x04"),
                "window");
            uint owner = ReadRequiredObjectPointer(
                memory,
                AddAddress(window, WindowOwnerOffset, "window+0x1C"),
                "owner");
            uint scene = ReadRequiredObjectPointer(
                memory,
                AddAddress(owner, OwnerSceneOffset, "owner+0x18"),
                "scene");
            uint root = ReadRequiredObjectPointer(
                memory,
                AddAddress(scene, SceneSchedulerRootOffset, "scene+0x8C"),
                "schedulerRoot");

            HashSet<uint> visited = new HashSet<uint>();
            visited.Add(root);
            uint current = root;
            int depth = 0;
            TaskNodeCommon rootNode = null;
            TaskNodeCommon leafNode = null;
            List<TaskNodeCommon> nodes = new List<TaskNodeCommon>();

            while (true)
            {
                TaskNodeCommon node = ReadTaskNodeCommon(memory, identity, current, depth == 0);
                nodes.Add(node);
                if (depth == 0)
                {
                    rootNode = node;
                }

                if (node.ChildPointer == 0)
                {
                    leafNode = node;
                    break;
                }

                ValidateObjectPointer(node.ChildPointer, "task child");
                if (visited.Contains(node.ChildPointer))
                {
                    throw new RootTraceReadException(
                        "ROOT_CHAIN_CYCLE",
                        string.Format(
                            "The task child chain revisited 0x{0:X8} at descendant depth {1}.",
                            node.ChildPointer,
                            depth + 1));
                }

                if (depth >= MaximumDescendantDepth)
                {
                    throw new RootTraceReadException(
                        "ROOT_CHAIN_DEPTH_EXCEEDED",
                        string.Format(
                            "The task child chain exceeds the maximum descendant depth of {0}.",
                            MaximumDescendantDepth));
                }

                visited.Add(node.ChildPointer);
                current = node.ChildPointer;
                depth++;
            }

            if (rootNode == null || leafNode == null)
            {
                throw new RootTraceReadException(
                    "ROOT_CHAIN_INTERNAL_FAILURE",
                    "The scheduler root or leaf was not captured.");
            }

            TaskNodeCommon domesticController = null;
            int? domesticControllerDepth = null;
            for (int index = 0; index < nodes.Count; index++)
            {
                if (nodes[index].Vtable != ExpectedDomesticControllerVtable)
                {
                    continue;
                }

                if (domesticController != null)
                {
                    throw new RootTraceReadException(
                        "MULTIPLE_DOMESTIC_CONTROLLERS",
                        "The active task chain contains more than one 0x00610BC8 domestic controller.");
                }

                domesticController = nodes[index];
                domesticControllerDepth = index;
            }

            return new RootTraceValues
            {
                WindowPointer = window,
                OwnerPointer = owner,
                ScenePointer = scene,
                SchedulerRootPointer = root,
                SchedulerRootVtable = rootNode.Vtable,
                SchedulerRootPendingPointer = rootNode.PendingPointer,
                DomesticControllerPointer = domesticController == null
                    ? (uint?)null
                    : domesticController.Address,
                DomesticControllerDepth = domesticControllerDepth,
                DomesticControllerCorpsPointer = domesticController == null
                    ? (uint?)null
                    : ReadUInt32(
                        memory,
                        AddAddress(domesticController.Address, RootCorpsOffset, "domesticController+0x30"),
                        "domestic controller corps"),
                DomesticControllerState = domesticController == null
                    ? (uint?)null
                    : ReadUInt32(
                        memory,
                        AddAddress(domesticController.Address, RootStateOffset, "domesticController+0x34"),
                        "domestic controller state"),
                DomesticControllerTargetPointer = domesticController == null
                    ? (uint?)null
                    : ReadUInt32(
                        memory,
                        AddAddress(domesticController.Address, RootTargetOffset, "domesticController+0x38"),
                        "domestic controller target"),
                LeafPointer = leafNode.Address,
                LeafVtable = leafNode.Vtable,
                LeafTickFunctionPointer = leafNode.TickFunctionPointer,
                LeafPendingPointer = leafNode.PendingPointer,
                DescendantDepth = depth,
                ChainNodes = nodes.ToArray()
            };
        }

        private static TaskNodeCommon ReadTaskNodeCommon(
            IReadOnlyProcessMemory memory,
            ProcessIdentitySnapshot identity,
            uint address,
            bool isSchedulerRoot)
        {
            ValidateObjectPointer(address, "task node");
            uint vtable = ReadUInt32(memory, address, "task vptr");
            if (isSchedulerRoot && vtable != ExpectedSchedulerRootVtable)
            {
                throw new RootTraceReadException(
                    "ROOT_VPTR_MISMATCH",
                    string.Format(
                        "schedulerRoot 0x{0:X8} has vptr 0x{1:X8}; expected 0x{2:X8}.",
                        address,
                        vtable,
                        ExpectedSchedulerRootVtable));
            }

            if (!IsRangeInsideExactMainModule(vtable, 0x10, identity))
            {
                throw new RootTraceReadException(
                    "TASK_VPTR_OUTSIDE_MAIN_MODULE",
                    string.Format(
                        "Task 0x{0:X8} has vptr 0x{1:X8}, outside the exact main-module range.",
                        address,
                        vtable));
            }

            uint tickSlot = AddAddress(vtable, TaskChildOffset, "vptr+0x0C");
            uint tickFunction = ReadUInt32(memory, tickSlot, "task vtable+0x0C function");
            if (!IsAddressInsideExactMainModule(tickFunction, identity))
            {
                throw new RootTraceReadException(
                    "TASK_TICK_OUTSIDE_MAIN_MODULE",
                    string.Format(
                        "Task 0x{0:X8} vtable+0x0C points to 0x{1:X8}, outside the exact main module.",
                        address,
                        tickFunction));
            }

            return new TaskNodeCommon
            {
                Address = address,
                Vtable = vtable,
                TickFunctionPointer = tickFunction,
                ChildPointer = ReadUInt32(
                    memory,
                    AddAddress(address, TaskChildOffset, "task+0x0C"),
                    "task child"),
                PendingPointer = ReadUInt32(
                    memory,
                    AddAddress(address, TaskPendingOffset, "task+0x10"),
                    "task pending")
            };
        }

        private static bool ValidateExactInitialIdentity(
            IReadOnlyProcessMemory memory,
            ProcessIdentitySnapshot identity,
            out string error)
        {
            error = null;
            if (identity == null)
            {
                error = "The read-only connection has no initial process identity.";
                return false;
            }

            if (identity.ProcessId <= 0 || memory.ProcessId != identity.ProcessId)
            {
                error = "The read-only source PID does not match its initial process identity.";
                return false;
            }

            if (identity.CreationFileTimeUtc <= 0)
            {
                error = "The process creation generation is absent.";
                return false;
            }

            if (!San9Pk101TargetValidator.PathsEqual(memory.ImagePath, San9Pk101Target.ExpectedExecutablePath)
                || !San9Pk101TargetValidator.PathsEqual(identity.ImagePath, San9Pk101Target.ExpectedExecutablePath))
            {
                error = "The read-only source is not bound to the exact supported executable path.";
                return false;
            }

            if (identity.MainModuleBaseAddress != San9Pk101Target.ExpectedImageBase
                || identity.MainModuleSize != San9Pk101Target.ExpectedSizeOfImage)
            {
                error = string.Format(
                    "The exact main-module identity is not 0x{0:X8}+0x{1:X8}.",
                    San9Pk101Target.ExpectedImageBase,
                    San9Pk101Target.ExpectedSizeOfImage);
                return false;
            }

            return true;
        }

        private static bool TryCaptureIdentity(
            IReadOnlyProcessMemory memory,
            out ProcessIdentitySnapshot identity,
            out string error)
        {
            try
            {
                return memory.TryCaptureIdentity(out identity, out error);
            }
            catch (Exception exception)
            {
                identity = null;
                error = exception.Message;
                return false;
            }
        }

        private static uint ReadRequiredObjectPointer(
            IReadOnlyProcessMemory memory,
            uint address,
            string name)
        {
            uint value = ReadUInt32(memory, address, name + " pointer");
            if (value == 0)
            {
                throw new RootTraceReadException(
                    "ROOT_CHAIN_NULL_POINTER",
                    string.Format("The {0} pointer read at 0x{1:X8} is null.", name, address));
            }

            ValidateObjectPointer(value, name);
            return value;
        }

        private static void ValidateObjectPointer(uint value, string name)
        {
            if (value == 0)
            {
                throw new RootTraceReadException(
                    "INVALID_32BIT_OBJECT_POINTER",
                    name + " is null.");
            }

            if ((value & 3U) != 0)
            {
                throw new RootTraceReadException(
                    "UNALIGNED_32BIT_OBJECT_POINTER",
                    string.Format("{0} 0x{1:X8} is not 4-byte aligned.", name, value));
            }
        }

        private static uint AddAddress(uint address, uint offset, string description)
        {
            ulong result = unchecked((ulong)address) + offset;
            if (address == 0 || result > uint.MaxValue)
            {
                throw new RootTraceReadException(
                    "ADDRESS_RANGE_OVERFLOW",
                    string.Format("The 32-bit address for {0} is invalid.", description));
            }

            return unchecked((uint)result);
        }

        private static uint ReadUInt32(
            IReadOnlyProcessMemory memory,
            uint address,
            string description)
        {
            try
            {
                byte[] bytes = memory.ReadBytes(address, 4);
                if (bytes == null || bytes.Length != 4)
                {
                    throw new InvalidOperationException("The read did not return exactly four bytes.");
                }

                return BitConverter.ToUInt32(bytes, 0);
            }
            catch (RootTraceReadException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new RootTraceReadException(
                    "MEMORY_READ_FAILED",
                    string.Format(
                        "Read-only read of {0} at 0x{1:X8} failed: {2}",
                        description,
                        address,
                        exception.Message));
            }
        }

        private static bool IsRangeInsideExactMainModule(
            uint address,
            uint size,
            ProcessIdentitySnapshot identity)
        {
            ulong start = address;
            ulong endExclusive = start + size;
            ulong moduleStart = identity.MainModuleBaseAddress;
            ulong moduleEnd = moduleStart + identity.MainModuleSize;
            return size != 0 && start >= moduleStart && endExclusive <= moduleEnd;
        }

        private static bool IsAddressInsideExactMainModule(
            uint address,
            ProcessIdentitySnapshot identity)
        {
            ulong moduleStart = identity.MainModuleBaseAddress;
            ulong moduleEnd = moduleStart + identity.MainModuleSize;
            return address >= moduleStart && address < moduleEnd;
        }

        private static San9Pk101RootTraceSample Failure(
            string code,
            string message,
            ProcessIdentitySnapshot identity)
        {
            return new San9Pk101RootTraceSample
            {
                ReadSucceeded = false,
                FailureCode = code ?? "ROOT_TRACE_FAILED",
                FailureMessage = message ?? "The root trace sample failed.",
                ProcessId = identity == null ? (int?)null : identity.ProcessId,
                ProcessCreationFileTimeUtc = identity == null
                    ? (long?)null
                    : identity.CreationFileTimeUtc
            };
        }

        private sealed class TaskNodeCommon
        {
            internal uint Address;
            internal uint Vtable;
            internal uint TickFunctionPointer;
            internal uint ChildPointer;
            internal uint PendingPointer;

            internal bool StructurallyEquals(TaskNodeCommon other)
            {
                return other != null
                    && Address == other.Address
                    && Vtable == other.Vtable
                    && TickFunctionPointer == other.TickFunctionPointer
                    && ChildPointer == other.ChildPointer
                    && PendingPointer == other.PendingPointer;
            }
        }

        private sealed class RootTraceValues
        {
            internal uint WindowPointer;
            internal uint OwnerPointer;
            internal uint ScenePointer;
            internal uint SchedulerRootPointer;
            internal uint SchedulerRootVtable;
            internal uint SchedulerRootPendingPointer;
            internal uint? DomesticControllerPointer;
            internal int? DomesticControllerDepth;
            internal uint? DomesticControllerCorpsPointer;
            internal uint? DomesticControllerState;
            internal uint? DomesticControllerTargetPointer;
            internal uint LeafPointer;
            internal uint LeafVtable;
            internal uint LeafTickFunctionPointer;
            internal uint LeafPendingPointer;
            internal int DescendantDepth;
            internal TaskNodeCommon[] ChainNodes;

            internal bool StructurallyEquals(RootTraceValues other)
            {
                if (other == null
                    || WindowPointer != other.WindowPointer
                    || OwnerPointer != other.OwnerPointer
                    || ScenePointer != other.ScenePointer
                    || SchedulerRootPointer != other.SchedulerRootPointer
                    || SchedulerRootVtable != other.SchedulerRootVtable
                    || SchedulerRootPendingPointer != other.SchedulerRootPendingPointer
                    || DomesticControllerPointer != other.DomesticControllerPointer
                    || DomesticControllerDepth != other.DomesticControllerDepth
                    || DomesticControllerCorpsPointer != other.DomesticControllerCorpsPointer
                    || DomesticControllerState != other.DomesticControllerState
                    || DomesticControllerTargetPointer != other.DomesticControllerTargetPointer
                    || LeafPointer != other.LeafPointer
                    || LeafVtable != other.LeafVtable
                    || LeafTickFunctionPointer != other.LeafTickFunctionPointer
                    || LeafPendingPointer != other.LeafPendingPointer
                    || DescendantDepth != other.DescendantDepth
                    || ChainNodes == null
                    || other.ChainNodes == null
                    || ChainNodes.Length != other.ChainNodes.Length)
                {
                    return false;
                }

                for (int index = 0; index < ChainNodes.Length; index++)
                {
                    if (!ChainNodes[index].StructurallyEquals(other.ChainNodes[index]))
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        private sealed class RootTraceReadException : Exception
        {
            internal RootTraceReadException(string code, string message)
                : base(message)
            {
                Code = code;
            }

            internal string Code { get; private set; }
        }
    }
}
