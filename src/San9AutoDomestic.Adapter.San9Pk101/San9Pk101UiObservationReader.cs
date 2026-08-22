using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace San9AutoDomestic.Adapter.San9Pk101
{
    internal interface IUiCandidateScanner : IDisposable
    {
        UiCandidateScan Scan(IReadOnlyProcessMemory memory, ProcessIdentitySnapshot expectedIdentity);
    }

    internal sealed class UiCandidateScan
    {
        internal UiCandidateScan()
        {
            AddressesByVtable = new Dictionary<uint, uint[]>();
        }

        internal Dictionary<uint, uint[]> AddressesByVtable;
        internal int QueriedRegionCount;
        internal int ScannedRegionCount;
        internal long ScannedByteCount;
    }

    internal sealed class San9Pk101UiObservationReader
    {
        internal const uint CommandMenuVtable = 0x0060a238;
        internal const uint SelectorVtable = 0x0061f0b8;
        internal const uint PersonListVtable = 0x00606c8c;
        internal const uint GlobalSelectedListAddress = 0x015455ac;
        internal const uint TrackedModalCountAddress = 0x01b41890;
        internal const uint TrackedModalArrayMinusOneAddress = 0x01b423dc;
        internal const int MinimumStableMilliseconds = 100;
        internal const int MaximumStabilityAttempts = 3;

        private const uint AppObjectAddress = 0x01228340;
        private const uint ExpectedSchedulerRootVtable = 0x00607560;
        private const uint ExpectedDomesticControllerVtable = 0x00610bc8;
        private const int MaximumTaskDepth = 64;
        private const uint AppWindowOffset = 0x04;
        private const uint WindowOwnerOffset = 0x1c;
        private const uint OwnerSceneOffset = 0x18;
        private const uint SceneSchedulerRootOffset = 0x8c;
        private const uint TaskChildOffset = 0x0c;
        private const uint TaskPendingOffset = 0x10;
        private const uint ControllerCorpsOffset = 0x30;
        private const uint ControllerStateOffset = 0x34;
        private const uint ControllerTargetOffset = 0x38;
        private const uint OuterCommittedListOffset = 0x6ac;
        private const uint OuterSourceListOffset = 0x6cc;
        private const uint OuterWorkingListOffset = 0x6ec;
        private const uint OuterResultOffset = 0x680;
        private const int OuterReadSize = 0x700;
        private const uint SelectorRowsOffset = 0x154;
        private const uint SelectorMaximumOffset = 0x180;
        private const int SelectorHeaderReadSize = 0x188;
        private const uint CommandMenuCorpsOffset = 0x2668;
        private const uint CommandMenuTargetOffset = 0x266c;
        private const int CommandMenuReadSize = 0x2670;
        private const int CommerceHoverOffset = 0x1898;
        private const int CultivateHoverOffset = 0x19ac;
        private const int RepairHoverOffset = 0x1ac0;
        private const int PersonListHeaderSize = 0x10;
        private const int PersonListNodeSize = 0x0c;
        private const int MaximumSelection = 5;
        private const int MaximumTrackedModalDepth = 64;

        private static readonly Dictionary<uint, CommandShape> CommandShapes =
            new Dictionary<uint, CommandShape>
            {
                { 0x0060b920, new CommandShape("Patrol", San9Pk101MemoryLayout.PersonEffectiveIntelligenceOffset) },
                { 0x0060ccb0, new CommandShape("Commerce", San9Pk101MemoryLayout.PersonEffectivePoliticsOffset) },
                { 0x0060bba0, new CommandShape("Cultivate", San9Pk101MemoryLayout.PersonEffectivePoliticsOffset) },
                { 0x0060bce8, new CommandShape("Repair", San9Pk101MemoryLayout.PersonEffectiveLeadershipOffset) },
                { 0x0060c370, new CommandShape("Train", San9Pk101MemoryLayout.PersonEffectiveMightOffset) }
            };

        private readonly Func<int, IUiCandidateScanner> scannerFactory;
        private readonly Action<int> delay;
        private readonly Func<int, long> uniqueWindowReader;
        private readonly int minimumStableMilliseconds;
        private readonly ObservationLifetimeTracker lifetimeTracker;

        internal San9Pk101UiObservationReader()
            : this(
                delegate(int processId) { return new Win32UiCandidateScanner(processId); },
                Thread.Sleep,
                MinimumStableMilliseconds,
                ReadUniqueGameWindow)
        {
        }

        internal San9Pk101UiObservationReader(
            Func<int, IUiCandidateScanner> scannerFactory,
            Action<int> delay,
            int minimumStableMilliseconds)
            : this(scannerFactory, delay, minimumStableMilliseconds, ReadUniqueGameWindow)
        {
        }

        internal San9Pk101UiObservationReader(
            Func<int, IUiCandidateScanner> scannerFactory,
            Action<int> delay,
            int minimumStableMilliseconds,
            Func<int, long> uniqueWindowReader)
        {
            if (scannerFactory == null) throw new ArgumentNullException("scannerFactory");
            if (delay == null) throw new ArgumentNullException("delay");
            if (minimumStableMilliseconds < 0 || minimumStableMilliseconds > 5000)
                throw new ArgumentOutOfRangeException("minimumStableMilliseconds");
            if (uniqueWindowReader == null) throw new ArgumentNullException("uniqueWindowReader");
            this.scannerFactory = scannerFactory;
            this.delay = delay;
            this.minimumStableMilliseconds = minimumStableMilliseconds;
            this.uniqueWindowReader = uniqueWindowReader;
            lifetimeTracker = new ObservationLifetimeTracker();
        }

        internal San9Pk101UiObservationReport Read(
            IReadOnlyProcessMemory memory,
            San9Pk101DiagnosticReport baseline)
        {
            if (memory == null)
                return Failure(baseline, "UI_READ_ONLY_MEMORY_REQUIRED", "A read-only process-memory source is required.", null);

            ProcessIdentitySnapshot initial;
            try { initial = memory.InitialIdentity; }
            catch (Exception exception)
            {
                return Failure(baseline, "UI_PROCESS_BINDING_UNAVAILABLE", exception.Message, null);
            }

            string bindingError;
            if (!ValidateInitialBinding(memory, initial, out bindingError))
                return Failure(baseline, "UI_PROCESS_BINDING_INVALID", bindingError, initial);

            CaptureValues accepted = null;
            int attempts = 0;
            int stableDuration = 0;
            try
            {
                using (IUiCandidateScanner scanner = scannerFactory(memory.ProcessId))
                {
                    for (int attempt = 1; attempt <= MaximumStabilityAttempts; attempt++)
                    {
                        attempts = attempt;
                        Stopwatch stopwatch = Stopwatch.StartNew();
                        CaptureValues a = Capture(memory, scanner, initial);
                        DelayForStableGap();
                        CaptureValues b = Capture(memory, scanner, initial);
                        DelayForStableGap();
                        CaptureValues c = Capture(memory, scanner, initial);
                        stopwatch.Stop();
                        stableDuration = checked((int)Math.Min(int.MaxValue, stopwatch.ElapsedMilliseconds));
                        if (a.CanonicalEquals(b)
                            && b.CanonicalEquals(c)
                            && stableDuration >= minimumStableMilliseconds)
                        {
                            accepted = c;
                            break;
                        }
                    }
                }
            }
            catch (UiObservationReadException exception)
            {
                return Failure(baseline, exception.Code, exception.Message, initial);
            }
            catch (Exception exception)
            {
                return Failure(baseline, "UI_OBSERVATION_READ_FAILED", exception.Message, initial);
            }

            if (accepted == null)
            {
                San9Pk101UiObservationReport unstable = Failure(
                    baseline,
                    "UI_NORMALIZED_STATE_UNSTABLE",
                    string.Format(
                        "The normalized UI state did not match across A/B/C after {0} bounded attempts.",
                        attempts),
                    initial);
                unstable.StabilityAttemptCount = attempts;
                unstable.StableDurationMilliseconds = stableDuration;
                return unstable;
            }

            return BuildReport(baseline, initial, accepted, attempts, stableDuration);
        }

        private void DelayForStableGap()
        {
            int gap = (minimumStableMilliseconds + 1) / 2;
            if (gap > 0) delay(gap);
        }

        private CaptureValues Capture(
            IReadOnlyProcessMemory memory,
            IUiCandidateScanner scanner,
            ProcessIdentitySnapshot initial)
        {
            ProcessIdentitySnapshot before = CaptureIdentity(memory, "UI_PROCESS_IDENTITY_BEFORE_FAILED");
            RequireSameIdentity(initial, before, "UI_PROCESS_GENERATION_CHANGED_BEFORE_CAPTURE");
            long windowHandle = uniqueWindowReader(memory.ProcessId);

            CaptureValues values = new CaptureValues();
            values.ProcessId = initial.ProcessId;
            values.ProcessCreationFileTimeUtc = initial.CreationFileTimeUtc;
            values.MainWindowHandle = windowHandle;
            values.RawCurrentOperationBuildingPointer = ReadUInt32(
                memory,
                San9Pk101MemoryLayout.RawCurrentCityPointerAddress,
                "raw current-operation building pointer");
            values.RawSceneSelector = ReadUInt32(
                memory,
                San9Pk101MemoryLayout.RawContextValue2480Address,
                "raw scene selector");
            values.RawSceneMode = ReadUInt32(
                memory,
                San9Pk101MemoryLayout.RawPhaseCandidateAddress,
                "raw scene mode");
            values.RawStrategicTime = ReadUInt32(
                memory,
                San9Pk101MemoryLayout.RawStrategicTimeCandidateAddress,
                "raw strategic time");
            values.TopModalWindowHandle = ReadTopModalWindowHandle(memory);
            values.Root = ReadRootChain(memory, initial);

            PersonTable persons = new PersonTable(memory.ReadBytes(
                San9Pk101MemoryLayout.PersonBase,
                checked(San9Pk101MemoryLayout.PersonStride * San9Pk101MemoryLayout.PersonCount)));
            UiCandidateScan scan = scanner.Scan(memory, initial);
            values.Scan = scan;
            values.Outer = ResolveOuter(memory, persons, scan);
            values.Menu = ResolveMenu(memory, scan, values.TopModalWindowHandle);
            values.Selector = ResolveSelector(
                memory,
                persons,
                scan,
                values.TopModalWindowHandle,
                values.Outer);
            values.GlobalSelected = ParsePersonList(
                memory,
                persons,
                GlobalSelectedListAddress,
                "global selected",
                MaximumSelection,
                null);

            ProcessIdentitySnapshot after = CaptureIdentity(memory, "UI_PROCESS_IDENTITY_AFTER_FAILED");
            RequireSameIdentity(initial, after, "UI_PROCESS_GENERATION_CHANGED_DURING_CAPTURE");
            RequireSameIdentity(before, after, "UI_PROCESS_GENERATION_CHANGED_DURING_CAPTURE");
            long afterWindow = uniqueWindowReader(memory.ProcessId);
            if (afterWindow != windowHandle)
                throw new UiObservationReadException(
                    "UI_WINDOW_GENERATION_CHANGED_DURING_CAPTURE",
                    "The unique KOEI_SAN9WINDOW HWND changed during one capture.");
            return values;
        }

        private static RootValues ReadRootChain(
            IReadOnlyProcessMemory memory,
            ProcessIdentitySnapshot identity)
        {
            uint window = ReadRequiredPointer(memory, AppObjectAddress + AppWindowOffset, "app window");
            uint owner = ReadRequiredPointer(memory, window + WindowOwnerOffset, "window owner");
            uint scene = ReadRequiredPointer(memory, owner + OwnerSceneOffset, "scene");
            uint sceneVtable = ReadUInt32(memory, scene, "scene vptr");
            RequireMainModuleAddress(sceneVtable, identity, "scene vptr");
            uint root = ReadRequiredPointer(memory, scene + SceneSchedulerRootOffset, "scheduler root");

            HashSet<uint> visited = new HashSet<uint>();
            List<TaskNodeValues> nodes = new List<TaskNodeValues>();
            uint current = root;
            for (int depth = 0; depth <= MaximumTaskDepth; depth++)
            {
                if (!visited.Add(current))
                    throw new UiObservationReadException("UI_TASK_CHAIN_CYCLE", "The active task child chain is cyclic.");
                uint vtable = ReadUInt32(memory, current, "task vptr");
                if (depth == 0 && vtable != ExpectedSchedulerRootVtable)
                    throw new UiObservationReadException(
                        "UI_SCHEDULER_ROOT_VPTR_MISMATCH",
                        string.Format("scheduler root vptr 0x{0:X8} is not 0x{1:X8}.", vtable, ExpectedSchedulerRootVtable));
                RequireMainModuleAddress(vtable, identity, "task vptr");
                uint tick = ReadUInt32(memory, vtable + TaskChildOffset, "task tick slot");
                RequireMainModuleAddress(tick, identity, "task tick function");
                TaskNodeValues node = new TaskNodeValues
                {
                    Depth = depth,
                    Address = current,
                    Vtable = vtable,
                    ChildPointer = ReadUInt32(memory, current + TaskChildOffset, "task child"),
                    PendingPointer = ReadUInt32(memory, current + TaskPendingOffset, "task pending")
                };
                nodes.Add(node);
                if (node.ChildPointer == 0) break;
                RequireAlignedPointer(node.ChildPointer, "task child");
                current = node.ChildPointer;
                if (depth == MaximumTaskDepth)
                    throw new UiObservationReadException("UI_TASK_CHAIN_DEPTH_LIMIT", "The active task chain exceeds 64 descendants.");
            }

            TaskNodeValues controller = null;
            foreach (TaskNodeValues node in nodes.Where(item => item.Vtable == ExpectedDomesticControllerVtable))
            {
                if (controller != null)
                    throw new UiObservationReadException(
                        "UI_MULTIPLE_DOMESTIC_CONTROLLERS",
                        "The active task chain contains multiple domestic controllers.");
                controller = node;
            }

            return new RootValues
            {
                WindowPointer = window,
                OwnerPointer = owner,
                ScenePointer = scene,
                SceneVtable = sceneVtable,
                SchedulerRootPointer = root,
                SchedulerRootVtable = nodes[0].Vtable,
                Nodes = nodes.ToArray(),
                ControllerPointer = controller == null ? (uint?)null : controller.Address,
                ControllerCorpsPointer = controller == null ? (uint?)null : ReadUInt32(memory, controller.Address + ControllerCorpsOffset, "controller corps"),
                ControllerState = controller == null ? (uint?)null : ReadUInt32(memory, controller.Address + ControllerStateOffset, "controller state"),
                ControllerTargetPointer = controller == null ? (uint?)null : ReadUInt32(memory, controller.Address + ControllerTargetOffset, "controller target")
            };
        }

        private static OuterValues ResolveOuter(
            IReadOnlyProcessMemory memory,
            PersonTable persons,
            UiCandidateScan scan)
        {
            List<OuterValues> valid = new List<OuterValues>();
            int rawCount = 0;
            foreach (KeyValuePair<uint, CommandShape> pair in CommandShapes)
            {
                uint[] addresses = GetCandidates(scan, pair.Key);
                rawCount += addresses.Length;
                foreach (uint address in addresses)
                {
                    try
                    {
                        byte[] header = memory.ReadBytes(address, OuterReadSize);
                        if (ReadUInt32(header, 0) != pair.Key) continue;
                        PersonListValues source = ParsePersonList(
                            memory,
                            persons,
                            address + OuterSourceListOffset,
                            "outer source",
                            San9Pk101MemoryLayout.PersonCount,
                            null);
                        PersonListValues working = ParsePersonList(
                            memory,
                            persons,
                            address + OuterWorkingListOffset,
                            "outer working",
                            MaximumSelection,
                            null);
                        PersonListValues committed = ParsePersonList(
                            memory,
                            persons,
                            address + OuterCommittedListOffset,
                            "outer committed",
                            MaximumSelection,
                            null);
                        if (source.OfficerIds.Length == 0) continue;

                        int? ownerCityId = ResolveCommonResidenceCity(persons, source.PersonPointers);
                        bool ready = ownerCityId.HasValue
                            && SourceOrderIsReadyAndStable(persons, source.PersonPointers, pair.Value.AbilityOffset);
                        valid.Add(new OuterValues
                        {
                            Present = true,
                            Address = address,
                            Vtable = pair.Key,
                            CommandName = pair.Value.Name,
                            AbilityOffset = pair.Value.AbilityOffset,
                            Result = ReadUInt32(header, checked((int)OuterResultOffset)),
                            Source = source,
                            Working = working,
                            Committed = committed,
                            OwnerCityId = ownerCityId,
                            SourceReadyAndStable = ready
                        });
                    }
                    catch (Exception)
                    {
                        // A vptr-shaped value is not accepted unless the complete object invariants hold.
                    }
                }
            }

            if (valid.Count == 0)
                return new OuterValues { RawCandidateCount = rawCount };
            if (valid.Count != 1)
                return new OuterValues
                {
                    RawCandidateCount = rawCount,
                    Error = string.Format("{0} structurally valid domestic outer tasks were observed.", valid.Count)
                };
            valid[0].RawCandidateCount = rawCount;
            return valid[0];
        }

        private static MenuValues ResolveMenu(
            IReadOnlyProcessMemory memory,
            UiCandidateScan scan,
            uint topModalWindowHandle)
        {
            uint[] addresses = GetCandidates(scan, CommandMenuVtable);
            List<MenuValues> valid = new List<MenuValues>();
            foreach (uint address in addresses)
            {
                try
                {
                    byte[] bytes = memory.ReadBytes(address, CommandMenuReadSize);
                    if (ReadUInt32(bytes, 0) != CommandMenuVtable) continue;
                    uint hwnd = ReadUInt32(bytes, 4);
                    if (topModalWindowHandle != 0 && hwnd != topModalWindowHandle) continue;
                    uint commerceHover = ReadUInt32(bytes, CommerceHoverOffset);
                    uint cultivateHover = ReadUInt32(bytes, CultivateHoverOffset);
                    uint repairHover = ReadUInt32(bytes, RepairHoverOffset);
                    string hoverError = null;
                    string hoveredCommand = null;
                    if (new[] { commerceHover, cultivateHover, repairHover }.Count(value => (value & 1U) != 0) > 1)
                    {
                        hoverError = "The low bits of the three verified domestic hover flags are not one-hot.";
                    }
                    else if ((commerceHover & 1U) != 0) hoveredCommand = "Commerce";
                    else if ((cultivateHover & 1U) != 0) hoveredCommand = "Cultivate";
                    else if ((repairHover & 1U) != 0) hoveredCommand = "Repair";
                    valid.Add(new MenuValues
                    {
                        Present = true,
                        Address = address,
                        Vtable = CommandMenuVtable,
                        WindowHandle = hwnd,
                        CorpsPointer = ReadUInt32(bytes, checked((int)CommandMenuCorpsOffset)),
                        TargetPointer = ReadUInt32(bytes, checked((int)CommandMenuTargetOffset)),
                        CommerceHover = commerceHover,
                        CultivateHover = cultivateHover,
                        RepairHover = repairHover,
                        HoveredCommand = hoveredCommand,
                        HoverError = hoverError
                    });
                }
                catch (Exception)
                {
                }
            }

            if (valid.Count == 0)
                return new MenuValues { RawCandidateCount = addresses.Length };
            if (valid.Count != 1)
                return new MenuValues
                {
                    RawCandidateCount = addresses.Length,
                    Error = string.Format("{0} command-menu objects matched the active modal HWND.", valid.Count)
                };
            valid[0].RawCandidateCount = addresses.Length;
            return valid[0];
        }

        private static SelectorValues ResolveSelector(
            IReadOnlyProcessMemory memory,
            PersonTable persons,
            UiCandidateScan scan,
            uint topModalWindowHandle,
            OuterValues outer)
        {
            uint[] addresses = GetCandidates(scan, SelectorVtable);
            List<SelectorValues> valid = new List<SelectorValues>();
            foreach (uint address in addresses)
            {
                try
                {
                    byte[] header = memory.ReadBytes(address, SelectorHeaderReadSize);
                    if (ReadUInt32(header, 0) != SelectorVtable) continue;
                    uint hwnd = ReadUInt32(header, 4);
                    if (topModalWindowHandle != 0 && hwnd != topModalWindowHandle) continue;
                    uint rowPointer = ReadUInt32(header, checked((int)SelectorRowsOffset + 4));
                    uint capacity = ReadUInt32(header, checked((int)SelectorRowsOffset + 8));
                    uint maximum = ReadUInt32(header, checked((int)SelectorMaximumOffset));
                    if (rowPointer == 0 || capacity == 0 || capacity > San9Pk101MemoryLayout.PersonCount)
                        continue;
                    if (maximum == 0 || maximum > MaximumSelection) continue;

                    int expectedCount = outer != null && outer.Present
                        ? outer.Source.OfficerIds.Length
                        : checked((int)capacity);
                    if (expectedCount <= 0 || expectedCount > checked((int)capacity)) continue;
                    byte[] rows = memory.ReadBytes(rowPointer, checked((expectedCount + 1) * 8));
                    List<uint> personPointers = new List<uint>();
                    List<int> ids = new List<int>();
                    List<int> selected = new List<int>();
                    for (int index = 0; index < expectedCount; index++)
                    {
                        uint personPointer = ReadUInt32(rows, index * 8);
                        uint flags = ReadUInt32(rows, index * 8 + 4);
                        int personId = persons.GetPersonId(personPointer, "selector row");
                        personPointers.Add(personPointer);
                        ids.Add(personId);
                        if ((flags & 0x2) != 0) selected.Add(personId);
                    }
                    if (ReadUInt32(rows, expectedCount * 8) != 0) continue;
                    if (selected.Count > maximum) continue;
                    bool sourceMatches = outer == null
                        || !outer.Present
                        || ids.SequenceEqual(outer.Source.OfficerIds);
                    valid.Add(new SelectorValues
                    {
                        Present = true,
                        Address = address,
                        Vtable = SelectorVtable,
                        WindowHandle = hwnd,
                        Maximum = maximum,
                        RowPointer = rowPointer,
                        Capacity = capacity,
                        OfficerIds = ids.ToArray(),
                        PersonPointers = personPointers.ToArray(),
                        SelectedOfficerIds = selected.ToArray(),
                        SourceMatchesOuter = sourceMatches,
                        OwnerCityId = ResolveCommonResidenceCity(persons, personPointers.ToArray())
                    });
                }
                catch (Exception)
                {
                }
            }

            if (valid.Count == 0)
                return new SelectorValues { RawCandidateCount = addresses.Length };
            if (valid.Count != 1)
                return new SelectorValues
                {
                    RawCandidateCount = addresses.Length,
                    Error = string.Format("{0} selector objects matched the active modal HWND.", valid.Count)
                };
            valid[0].RawCandidateCount = addresses.Length;
            return valid[0];
        }

        private San9Pk101UiObservationReport BuildReport(
            San9Pk101DiagnosticReport baseline,
            ProcessIdentitySnapshot identity,
            CaptureValues values,
            int attempts,
            int stableDuration)
        {
            List<UiObservationField> fields = new List<UiObservationField>();
            List<AvailabilityIssue> issues = new List<AvailabilityIssue>();
            string sceneToken;
            string windowToken;
            Dictionary<string, string> objectTokens = lifetimeTracker.Observe(identity, values, out sceneToken, out windowToken);

            AddField(fields, "Scene", UiObservationState.Verified,
                string.Format("0x{0:X8}", values.Root.ScenePointer),
                "scene pointer/vptr and process generation were stable across A/B/C.");
            bool strategicInput = values.RawSceneMode == 1
                && values.Root.ControllerPointer.HasValue
                && values.Root.ControllerState == 0x3e9;
            bool definitelyNotStrategicInput = !values.Root.ControllerPointer.HasValue;
            AddField(fields, "StrategicPhase",
                strategicInput || definitelyNotStrategicInput
                    ? UiObservationState.Verified : UiObservationState.Unknown,
                strategicInput ? "StrategicInput" : definitelyNotStrategicInput ? "NotStrategicInput" : "TransitionOrUnknown",
                string.Format(
                    "rawMode={0}; rawSelector={1}; strategicTime={2}; controller={3}; controllerState={4}. rawMode alone is not sufficient.",
                    values.RawSceneMode,
                    values.RawSceneSelector,
                    values.RawStrategicTime,
                    values.Root.ControllerPointer.HasValue ? string.Format("0x{0:X8}", values.Root.ControllerPointer.Value) : "absent",
                    values.Root.ControllerState.HasValue ? string.Format("0x{0:X}", values.Root.ControllerState.Value) : "absent"));
            AddField(fields, "RootTaskChain", UiObservationState.Verified,
                string.Format("root=0x{0:X8}; nodes={1}", values.Root.SchedulerRootPointer, values.Root.Nodes.Length),
                "scheduler root vptr, task vptr/tick ranges, alignment, acyclicity and A/B/C identity passed.");

            AddPresenceField(fields, issues, "DomesticOuterDialog", values.Outer.Present, values.Outer.Error,
                values.Outer.Present ? string.Format("{0}@0x{1:X8}", values.Outer.CommandName, values.Outer.Address) : "absent",
                "writable private-memory scan plus three embedded person-list invariants.");
            AddPresenceField(fields, issues, "OfficerSelector", values.Selector.Present, values.Selector.Error,
                values.Selector.Present ? string.Format("0x{0:X8}; hwnd=0x{1:X8}", values.Selector.Address, values.Selector.WindowHandle) : "absent",
                "active modal HWND, selector vptr, row vector, max count and exact PERSON records.");
            AddPresenceField(fields, issues, "DomesticCommandMenu", values.Menu.Present, values.Menu.Error,
                values.Menu.Present ? string.Format("0x{0:X8}", values.Menu.Address) : "absent",
                "active modal HWND and command-menu vptr/fields.");
            UiObservationState hoverState = values.Menu.Present
                && string.IsNullOrEmpty(values.Menu.HoverError)
                && !string.IsNullOrEmpty(values.Menu.HoveredCommand)
                    ? UiObservationState.Verified
                    : string.IsNullOrEmpty(values.Menu.HoverError)
                        ? UiObservationState.Unknown : UiObservationState.Contradictory;
            AddField(fields, "HoveredCommand", hoverState,
                values.Menu.HoveredCommand ?? "none/unverified",
                string.IsNullOrEmpty(values.Menu.HoverError)
                    ? string.Format("one-hot low bits (raw shown): Commerce@+0x1898={0}, Cultivate@+0x19AC={1}, Repair@+0x1AC0={2}.",
                        values.Menu.CommerceHover, values.Menu.CultivateHover, values.Menu.RepairHover)
                    : values.Menu.HoverError);
            if (hoverState == UiObservationState.Contradictory)
                issues.Add(new AvailabilityIssue("UI_HOVERED_COMMAND_CONTRADICTORY", DiagnosticSeverity.Blocking, values.Menu.HoverError));

            bool confirmation = values.Outer.Present
                && !values.Selector.Present
                && values.Outer.Working.OfficerIds.Length > 0
                && values.Outer.Result == 0;
            AddField(fields, "DomesticConfirmation", UiObservationState.Verified,
                confirmation ? "present" : "absent",
                "outer task present + selector absent + non-empty working list + result flag not completed.");

            int? mapCity = CityIdFromPointer(values.RawCurrentOperationBuildingPointer);
            AddField(fields, "MapFocusedFacilityId", UiObservationState.Unknown,
                mapCity.HasValue ? mapCity.Value.ToString() : "none/non-city",
                "0x01232474 is only a current-operation building observation; no authoritative map-focus setter/accessor is proven.");

            int? controllerCity = values.Root.ControllerTargetPointer.HasValue
                ? CityIdFromPointer(values.Root.ControllerTargetPointer.Value)
                : null;
            UiObservationState targetState = values.Root.ControllerPointer.HasValue
                && values.Root.ControllerTargetPointer.HasValue
                && (values.Root.ControllerTargetPointer.Value == 0 || controllerCity.HasValue)
                    ? UiObservationState.Verified
                    : UiObservationState.Unknown;
            AddField(fields, "DomesticTargetCityId", targetState,
                controllerCity.HasValue ? controllerCity.Value.ToString() : "none",
                "domestic controller +0x38; a null target pointer means not currently bound (city ID 0 remains valid)." );

            int? selectorCity = values.Selector.Present ? values.Selector.OwnerCityId : values.Outer.OwnerCityId;
            UiObservationState selectorOwnerState = selectorCity.HasValue
                && values.Outer.Present
                && values.Outer.SourceReadyAndStable
                && (!values.Selector.Present || values.Selector.SourceMatchesOuter)
                    ? UiObservationState.Verified
                    : UiObservationState.Unknown;
            AddField(fields, "SelectorOwnerCityId", selectorOwnerState,
                selectorCity.HasValue ? selectorCity.Value.ToString() : "none",
                "all source PERSON residence pointers agree; source is ready and stable ability-descending.");

            int? menuCity = values.Menu.Present ? CityIdFromPointer(values.Menu.TargetPointer) : null;
            int? verifiedCity = null;
            UiObservationState currentState = UiObservationState.Unknown;
            string currentEvidence = "no two independent authoritative UI paths agree.";
            if ((values.Outer.Present || values.Selector.Present)
                && controllerCity.HasValue
                && selectorCity.HasValue)
            {
                if (controllerCity.Value == selectorCity.Value)
                {
                    verifiedCity = controllerCity;
                    currentState = UiObservationState.Verified;
                    currentEvidence = "domestic controller target equals selector/outer source owner.";
                }
                else
                {
                    currentState = UiObservationState.Contradictory;
                    currentEvidence = "domestic controller target disagrees with selector/outer source owner.";
                }
            }
            else if (values.Menu.Present && controllerCity.HasValue && menuCity.HasValue)
            {
                if (controllerCity.Value == menuCity.Value)
                {
                    verifiedCity = controllerCity;
                    currentState = UiObservationState.Verified;
                    currentEvidence = "domestic controller target equals command-menu target.";
                }
                else
                {
                    currentState = UiObservationState.Contradictory;
                    currentEvidence = "domestic controller target disagrees with command-menu target.";
                }
            }
            AddField(fields, "VerifiedCurrentCityId", currentState,
                verifiedCity.HasValue ? verifiedCity.Value.ToString() : "none",
                currentEvidence);
            if (currentState == UiObservationState.Contradictory)
                issues.Add(new AvailabilityIssue("UI_CURRENT_CITY_CONTRADICTORY", DiagnosticSeverity.Blocking, currentEvidence));

            int[] candidates = values.Selector.Present
                ? values.Selector.OfficerIds
                : values.Outer.Present ? values.Outer.Source.OfficerIds : new int[0];
            int[] selected = values.Selector.Present
                ? values.Selector.SelectedOfficerIds
                : values.Outer.Present ? values.Outer.Working.OfficerIds : new int[0];
            UiObservationState candidateState = values.Outer.Present
                && values.Outer.SourceReadyAndStable
                && (!values.Selector.Present || values.Selector.SourceMatchesOuter)
                    ? UiObservationState.Verified
                    : UiObservationState.Unknown;
            AddField(fields, "CandidateSourceOrder", candidateState,
                JoinIds(candidates),
                "outer source list order; selector rows must exactly preserve it.");
            AddField(fields, "SelectedOfficerIds", values.Selector.Present || values.Outer.Present
                    ? UiObservationState.Verified : UiObservationState.Unknown,
                JoinIds(selected),
                values.Selector.Present ? "selector row flag bit 0x2." : "outer working list after selector closure.");

            UiLayerKind layer = strategicInput ? UiLayerKind.StrategicMapCandidate : UiLayerKind.Unknown;
            if (values.Menu.Present) layer = UiLayerKind.DomesticCommandMenu;
            if (values.Outer.Present) layer = confirmation ? UiLayerKind.DomesticConfirmation : UiLayerKind.DomesticOuterDialog;
            if (values.Selector.Present) layer = UiLayerKind.OfficerSelector;

            San9Pk101UiObservationReport report = new San9Pk101UiObservationReport
            {
                Baseline = baseline,
                ReadSucceeded = true,
                StableAbc = true,
                StabilityAttemptCount = attempts,
                StableDurationMilliseconds = stableDuration,
                CapturedUtc = DateTimeOffset.UtcNow,
                ProcessId = identity.ProcessId,
                ProcessCreationFileTimeUtc = identity.CreationFileTimeUtc,
                MainWindowHandle = values.MainWindowHandle,
                WindowObservationToken = windowToken,
                RawSceneMode = values.RawSceneMode,
                RawSceneSelector = values.RawSceneSelector,
                RawStrategicTime = values.RawStrategicTime,
                Layer = layer,
                HoveredCommandState = hoverState,
                HoveredCommand = values.Menu.HoveredCommand ?? string.Empty,
                Scene = CreateObject(values.Root.ScenePointer, values.Root.SceneVtable, 0, GetToken(objectTokens, "scene")),
                SchedulerRoot = CreateObject(values.Root.SchedulerRootPointer, values.Root.SchedulerRootVtable, 0, GetToken(objectTokens, "root")),
                DomesticController = values.Root.ControllerPointer.HasValue
                    ? CreateObject(values.Root.ControllerPointer.Value, ExpectedDomesticControllerVtable, 0, GetToken(objectTokens, "controller")) : null,
                CommandMenu = values.Menu.Present
                    ? CreateObject(values.Menu.Address, values.Menu.Vtable, values.Menu.WindowHandle, GetToken(objectTokens, "menu")) : null,
                OuterDialog = values.Outer.Present
                    ? CreateObject(values.Outer.Address, values.Outer.Vtable, 0, GetToken(objectTokens, "outer")) : null,
                Selector = values.Selector.Present
                    ? CreateObject(values.Selector.Address, values.Selector.Vtable, values.Selector.WindowHandle, GetToken(objectTokens, "selector")) : null,
                TaskChain = values.Root.Nodes.Select(delegate(TaskNodeValues item)
                {
                    return new UiTaskNodeObservation
                    {
                        Depth = item.Depth,
                        Address = item.Address,
                        Vtable = item.Vtable,
                        ChildPointer = item.ChildPointer,
                        PendingPointer = item.PendingPointer,
                        ObservationToken = GetToken(objectTokens, "task:" + item.Depth)
                    };
                }).ToArray(),
                CandidateOfficerIds = candidates,
                SelectedOfficerIds = selected,
                WorkingOfficerIds = values.Outer.Present ? values.Outer.Working.OfficerIds : new int[0],
                CommittedOfficerIds = values.Outer.Present ? values.Outer.Committed.OfficerIds : new int[0],
                Fields = fields.ToArray(),
                Issues = issues.ToArray()
            };
            return report;
        }

        private static San9Pk101UiObservationReport Failure(
            San9Pk101DiagnosticReport baseline,
            string code,
            string message,
            ProcessIdentitySnapshot identity)
        {
            return new San9Pk101UiObservationReport
            {
                Baseline = baseline,
                ReadSucceeded = false,
                StableAbc = false,
                CapturedUtc = DateTimeOffset.UtcNow,
                ProcessId = identity == null ? (int?)null : identity.ProcessId,
                ProcessCreationFileTimeUtc = identity == null ? (long?)null : identity.CreationFileTimeUtc,
                WindowObservationToken = string.Empty,
                Layer = UiLayerKind.Unknown,
                Issues = new[] { new AvailabilityIssue(code, DiagnosticSeverity.Blocking, message) }
            };
        }

        private static void AddPresenceField(
            IList<UiObservationField> fields,
            IList<AvailabilityIssue> issues,
            string name,
            bool present,
            string error,
            string value,
            string evidence)
        {
            UiObservationState state = string.IsNullOrEmpty(error)
                ? UiObservationState.Verified
                : UiObservationState.Contradictory;
            AddField(fields, name, state, value, string.IsNullOrEmpty(error) ? evidence : error);
            if (state == UiObservationState.Contradictory)
                issues.Add(new AvailabilityIssue("UI_" + name.ToUpperInvariant() + "_CONTRADICTORY", DiagnosticSeverity.Blocking, error));
        }

        private static void AddField(
            IList<UiObservationField> fields,
            string name,
            UiObservationState state,
            string value,
            string evidence)
        {
            fields.Add(new UiObservationField(name, state, value, evidence));
        }

        private static UiObjectObservation CreateObject(uint address, uint vtable, uint hwnd, string token)
        {
            return new UiObjectObservation
            {
                Address = address,
                Vtable = vtable,
                WindowHandle = hwnd,
                ObservationToken = token
            };
        }

        private static string GetToken(IDictionary<string, string> tokens, string key)
        {
            string value;
            return tokens.TryGetValue(key, out value) ? value : string.Empty;
        }

        private static string JoinIds(IEnumerable<int> ids)
        {
            return string.Join(",", ids.Select(item => item.ToString()).ToArray());
        }

        private static uint[] GetCandidates(UiCandidateScan scan, uint vtable)
        {
            uint[] values;
            return scan != null && scan.AddressesByVtable.TryGetValue(vtable, out values)
                ? values
                : new uint[0];
        }

        private static PersonListValues ParsePersonList(
            IReadOnlyProcessMemory memory,
            PersonTable persons,
            uint listAddress,
            string label,
            int maximumCount,
            int[] expectedIds)
        {
            byte[] header = memory.ReadBytes(listAddress, PersonListHeaderSize);
            uint vtable = ReadUInt32(header, 0);
            uint first = ReadUInt32(header, 4);
            uint last = ReadUInt32(header, 8);
            uint count = ReadUInt32(header, 12);
            if (vtable != PersonListVtable)
                throw new UiObservationReadException("UI_PERSON_LIST_VPTR_INVALID", label + " list vptr is invalid.");
            if (count > maximumCount)
                throw new UiObservationReadException("UI_PERSON_LIST_COUNT_INVALID", label + " list count exceeds its bound.");
            if (count == 0)
            {
                if (first != 0 || last != 0)
                    throw new UiObservationReadException("UI_PERSON_LIST_EMPTY_LINK_INVALID", label + " empty list has non-null links.");
                return new PersonListValues();
            }
            if (first == 0 || last == 0)
                throw new UiObservationReadException("UI_PERSON_LIST_LINK_INVALID", label + " non-empty list has a null endpoint.");

            List<int> ids = new List<int>();
            List<uint> pointers = new List<uint>();
            HashSet<uint> visited = new HashSet<uint>();
            uint current = first;
            uint previous = 0;
            for (int index = 0; index < count; index++)
            {
                RequireAlignedPointer(current, label + " node");
                if (!visited.Add(current))
                    throw new UiObservationReadException("UI_PERSON_LIST_CYCLE", label + " list contains a cycle.");
                byte[] node = memory.ReadBytes(current, PersonListNodeSize);
                uint next = ReadUInt32(node, 0);
                uint nodePrevious = ReadUInt32(node, 4);
                uint personPointer = ReadUInt32(node, 8);
                if (nodePrevious != previous)
                    throw new UiObservationReadException("UI_PERSON_LIST_PREVIOUS_INVALID", label + " previous link is inconsistent.");
                ids.Add(persons.GetPersonId(personPointer, label));
                pointers.Add(personPointer);
                previous = current;
                current = next;
            }
            if (previous != last || current != 0)
                throw new UiObservationReadException("UI_PERSON_LIST_TAIL_INVALID", label + " list tail/count is inconsistent.");
            if (ids.Distinct().Count() != ids.Count)
                throw new UiObservationReadException("UI_PERSON_LIST_DUPLICATE", label + " list contains duplicate officers.");
            if (expectedIds != null && !ids.SequenceEqual(expectedIds))
                throw new UiObservationReadException("UI_PERSON_LIST_ORDER_MISMATCH", label + " list order differs from the expected order.");
            return new PersonListValues
            {
                OfficerIds = ids.ToArray(),
                PersonPointers = pointers.ToArray()
            };
        }

        private static int? ResolveCommonResidenceCity(PersonTable persons, uint[] personPointers)
        {
            if (personPointers == null || personPointers.Length == 0) return null;
            int? city = null;
            foreach (uint pointer in personPointers)
            {
                uint residence = persons.GetResidencePointer(pointer);
                int? current = CityIdFromResidencePointer(residence);
                if (!current.HasValue) return null;
                if (!city.HasValue) city = current;
                else if (city.Value != current.Value) return null;
            }
            return city;
        }

        private static bool SourceOrderIsReadyAndStable(PersonTable persons, uint[] pointers, int abilityOffset)
        {
            int previousAbility = int.MaxValue;
            HashSet<uint> seen = new HashSet<uint>();
            foreach (uint pointer in pointers)
            {
                if (!seen.Add(pointer)) return false;
                uint flags = persons.GetUInt32(pointer, San9Pk101MemoryLayout.PersonReadyFlagsOffset);
                uint identity = persons.GetUInt32(pointer, San9Pk101MemoryLayout.PersonIdentityOffset);
                if ((flags & San9Pk101MemoryLayout.PersonBusyBit) != 0
                    || identity < San9Pk101MemoryLayout.MinimumValidResidentIdentity
                    || identity > San9Pk101MemoryLayout.MaximumValidResidentIdentity)
                    return false;
                int ability = checked((int)persons.GetUInt32(pointer, abilityOffset));
                if (ability > previousAbility) return false;
                previousAbility = ability;
            }
            return true;
        }

        private static int? CityIdFromPointer(uint pointer)
        {
            if (pointer < San9Pk101MemoryLayout.CityBase) return null;
            uint relative = pointer - San9Pk101MemoryLayout.CityBase;
            if (relative % San9Pk101MemoryLayout.CityStride != 0) return null;
            uint id = relative / San9Pk101MemoryLayout.CityStride;
            return id < San9Pk101MemoryLayout.CityCount ? (int?)checked((int)id) : null;
        }

        private static int? CityIdFromResidencePointer(uint pointer)
        {
            uint first = checked(San9Pk101MemoryLayout.CityBase
                + unchecked((uint)San9Pk101MemoryLayout.CityResidentUnitPointerOffset));
            if (pointer < first) return null;
            uint relative = pointer - first;
            if (relative % San9Pk101MemoryLayout.CityStride != 0) return null;
            uint id = relative / San9Pk101MemoryLayout.CityStride;
            return id < San9Pk101MemoryLayout.CityCount ? (int?)checked((int)id) : null;
        }

        private static uint ReadTopModalWindowHandle(IReadOnlyProcessMemory memory)
        {
            uint count = ReadUInt32(memory, TrackedModalCountAddress, "tracked modal count");
            if (count == 0) return 0;
            if (count > MaximumTrackedModalDepth)
                throw new UiObservationReadException("UI_MODAL_DEPTH_INVALID", "The tracked modal depth exceeds 64.");
            uint address = checked(TrackedModalArrayMinusOneAddress + count * 4);
            return ReadUInt32(memory, address, "top tracked modal HWND");
        }

        private static ProcessIdentitySnapshot CaptureIdentity(IReadOnlyProcessMemory memory, string code)
        {
            ProcessIdentitySnapshot identity;
            string error;
            if (!memory.TryCaptureIdentity(out identity, out error) || identity == null)
                throw new UiObservationReadException(code, error ?? "Unable to capture process identity.");
            return identity;
        }

        private static bool ValidateInitialBinding(
            IReadOnlyProcessMemory memory,
            ProcessIdentitySnapshot identity,
            out string error)
        {
            error = null;
            if (identity == null)
            {
                error = "Initial process identity is null.";
                return false;
            }
            if (memory.ProcessId != identity.ProcessId)
            {
                error = "Read source PID differs from the initial identity PID.";
                return false;
            }
            if (identity.MainModuleBaseAddress != San9Pk101Target.ExpectedImageBase
                || identity.MainModuleSize != San9Pk101Target.ExpectedSizeOfImage)
            {
                error = "The live main-module base/size does not match the exact San9PK 1.01 target.";
                return false;
            }
            return true;
        }

        private static void RequireSameIdentity(
            ProcessIdentitySnapshot expected,
            ProcessIdentitySnapshot actual,
            string code)
        {
            if (expected == null || !expected.SameGenerationAndImage(actual))
                throw new UiObservationReadException(code, "PID/creation time/path/main-module identity changed.");
        }

        private static long ReadUniqueGameWindow(int processId)
        {
            List<long> matches = new List<long>();
            bool success = NativeMethods.EnumWindows(
                delegate(IntPtr handle, IntPtr parameter)
                {
                    uint pid;
                    NativeMethods.GetWindowThreadProcessId(handle, out pid);
                    if (pid != unchecked((uint)processId)) return true;
                    StringBuilder className = new StringBuilder(128);
                    int length = NativeMethods.GetClassName(handle, className, className.Capacity);
                    if (length > 0 && string.Equals(className.ToString(), San9Pk101Target.ExpectedWindowClass, StringComparison.Ordinal))
                        matches.Add(handle.ToInt64());
                    return true;
                },
                IntPtr.Zero);
            if (!success)
                throw new UiObservationReadException("UI_WINDOW_ENUMERATION_FAILED", "EnumWindows failed.");
            if (matches.Count != 1)
                throw new UiObservationReadException(
                    "UI_WINDOW_NOT_UNIQUE",
                    string.Format("Expected one KOEI_SAN9WINDOW for PID {0}; observed {1}.", processId, matches.Count));
            return matches[0];
        }

        private static uint ReadRequiredPointer(IReadOnlyProcessMemory memory, uint address, string label)
        {
            uint value = ReadUInt32(memory, address, label);
            RequireAlignedPointer(value, label);
            return value;
        }

        private static void RequireAlignedPointer(uint value, string label)
        {
            if (value < 0x10000 || value > San9Pk101MemoryLayout.MaximumUserAddress32 || (value & 3) != 0)
                throw new UiObservationReadException("UI_POINTER_INVALID", label + " is not a valid aligned 32-bit user pointer.");
        }

        private static void RequireMainModuleAddress(uint value, ProcessIdentitySnapshot identity, string label)
        {
            ulong end = (ulong)identity.MainModuleBaseAddress + identity.MainModuleSize;
            if (value < identity.MainModuleBaseAddress || value >= end)
                throw new UiObservationReadException("UI_CODE_POINTER_OUTSIDE_TARGET", label + " is outside the exact main module.");
        }

        private static uint ReadUInt32(IReadOnlyProcessMemory memory, uint address, string label)
        {
            try { return ReadUInt32(memory.ReadBytes(address, 4), 0); }
            catch (Exception exception)
            {
                throw new UiObservationReadException("UI_READ_FAILED", label + ": " + exception.Message);
            }
        }

        private static uint ReadUInt32(byte[] bytes, int offset)
        {
            if (bytes == null || offset < 0 || offset > bytes.Length - 4)
                throw new ArgumentOutOfRangeException("offset");
            return unchecked((uint)(bytes[offset]
                | (bytes[offset + 1] << 8)
                | (bytes[offset + 2] << 16)
                | (bytes[offset + 3] << 24)));
        }
    }

    internal sealed class CaptureValues
    {
        internal int ProcessId;
        internal long ProcessCreationFileTimeUtc;
        internal long MainWindowHandle;
        internal uint RawCurrentOperationBuildingPointer;
        internal uint RawSceneSelector;
        internal uint RawSceneMode;
        internal uint RawStrategicTime;
        internal uint TopModalWindowHandle;
        internal RootValues Root;
        internal UiCandidateScan Scan;
        internal OuterValues Outer;
        internal MenuValues Menu;
        internal SelectorValues Selector;
        internal PersonListValues GlobalSelected;

        internal bool CanonicalEquals(CaptureValues other)
        {
            return other != null && string.Equals(Canonical(), other.Canonical(), StringComparison.Ordinal);
        }

        private string Canonical()
        {
            StringBuilder builder = new StringBuilder();
            builder.Append(ProcessId).Append('|').Append(ProcessCreationFileTimeUtc).Append('|')
                .Append(MainWindowHandle).Append('|').Append(RawCurrentOperationBuildingPointer).Append('|')
                .Append(RawSceneSelector).Append('|').Append(RawSceneMode).Append('|').Append(RawStrategicTime).Append('|')
                .Append(TopModalWindowHandle).Append('|');
            if (Root != null) Root.AppendCanonical(builder);
            builder.Append('|');
            if (Outer != null) Outer.AppendCanonical(builder);
            builder.Append('|');
            if (Menu != null) Menu.AppendCanonical(builder);
            builder.Append('|');
            if (Selector != null) Selector.AppendCanonical(builder);
            builder.Append('|');
            if (GlobalSelected != null) GlobalSelected.AppendCanonical(builder);
            return builder.ToString();
        }
    }

    internal sealed class RootValues
    {
        internal uint WindowPointer;
        internal uint OwnerPointer;
        internal uint ScenePointer;
        internal uint SceneVtable;
        internal uint SchedulerRootPointer;
        internal uint SchedulerRootVtable;
        internal TaskNodeValues[] Nodes = new TaskNodeValues[0];
        internal uint? ControllerPointer;
        internal uint? ControllerCorpsPointer;
        internal uint? ControllerState;
        internal uint? ControllerTargetPointer;

        internal void AppendCanonical(StringBuilder builder)
        {
            builder.Append(WindowPointer).Append(',').Append(OwnerPointer).Append(',').Append(ScenePointer).Append(',')
                .Append(SceneVtable).Append(',').Append(SchedulerRootPointer).Append(',').Append(SchedulerRootVtable).Append(',')
                .Append(ControllerPointer).Append(',').Append(ControllerCorpsPointer).Append(',')
                .Append(ControllerState).Append(',').Append(ControllerTargetPointer);
            foreach (TaskNodeValues node in Nodes) node.AppendCanonical(builder);
        }
    }

    internal sealed class TaskNodeValues
    {
        internal int Depth;
        internal uint Address;
        internal uint Vtable;
        internal uint ChildPointer;
        internal uint PendingPointer;

        internal void AppendCanonical(StringBuilder builder)
        {
            builder.Append(';').Append(Depth).Append(',').Append(Address).Append(',').Append(Vtable)
                .Append(',').Append(ChildPointer).Append(',').Append(PendingPointer);
        }
    }

    internal sealed class PersonListValues
    {
        internal int[] OfficerIds = new int[0];
        internal uint[] PersonPointers = new uint[0];

        internal void AppendCanonical(StringBuilder builder)
        {
            builder.Append('[').Append(string.Join(",", OfficerIds.Select(item => item.ToString()).ToArray())).Append(']');
        }
    }

    internal sealed class OuterValues
    {
        internal bool Present;
        internal int RawCandidateCount;
        internal string Error;
        internal uint Address;
        internal uint Vtable;
        internal string CommandName;
        internal int AbilityOffset;
        internal uint Result;
        internal PersonListValues Source = new PersonListValues();
        internal PersonListValues Working = new PersonListValues();
        internal PersonListValues Committed = new PersonListValues();
        internal int? OwnerCityId;
        internal bool SourceReadyAndStable;

        internal void AppendCanonical(StringBuilder builder)
        {
            builder.Append(Present).Append(',').Append(RawCandidateCount).Append(',').Append(Error).Append(',')
                .Append(Address).Append(',').Append(Vtable).Append(',').Append(CommandName).Append(',')
                .Append(AbilityOffset).Append(',').Append(Result).Append(',').Append(OwnerCityId).Append(',')
                .Append(SourceReadyAndStable);
            Source.AppendCanonical(builder);
            Working.AppendCanonical(builder);
            Committed.AppendCanonical(builder);
        }
    }

    internal sealed class MenuValues
    {
        internal bool Present;
        internal int RawCandidateCount;
        internal string Error;
        internal uint Address;
        internal uint Vtable;
        internal uint WindowHandle;
        internal uint CorpsPointer;
        internal uint TargetPointer;
        internal uint CommerceHover;
        internal uint CultivateHover;
        internal uint RepairHover;
        internal string HoveredCommand;
        internal string HoverError;

        internal void AppendCanonical(StringBuilder builder)
        {
            builder.Append(Present).Append(',').Append(RawCandidateCount).Append(',').Append(Error).Append(',')
                .Append(Address).Append(',').Append(Vtable).Append(',').Append(WindowHandle).Append(',')
                .Append(CorpsPointer).Append(',').Append(TargetPointer).Append(',')
                .Append(CommerceHover).Append(',').Append(CultivateHover).Append(',').Append(RepairHover).Append(',')
                .Append(HoveredCommand).Append(',').Append(HoverError);
        }
    }

    internal sealed class SelectorValues
    {
        internal bool Present;
        internal int RawCandidateCount;
        internal string Error;
        internal uint Address;
        internal uint Vtable;
        internal uint WindowHandle;
        internal uint Maximum;
        internal uint RowPointer;
        internal uint Capacity;
        internal int[] OfficerIds = new int[0];
        internal uint[] PersonPointers = new uint[0];
        internal int[] SelectedOfficerIds = new int[0];
        internal bool SourceMatchesOuter;
        internal int? OwnerCityId;

        internal void AppendCanonical(StringBuilder builder)
        {
            builder.Append(Present).Append(',').Append(RawCandidateCount).Append(',').Append(Error).Append(',')
                .Append(Address).Append(',').Append(Vtable).Append(',').Append(WindowHandle).Append(',')
                .Append(Maximum).Append(',').Append(RowPointer).Append(',').Append(Capacity).Append(',')
                .Append(SourceMatchesOuter).Append(',').Append(OwnerCityId).Append('[')
                .Append(string.Join(",", OfficerIds.Select(item => item.ToString()).ToArray())).Append("][")
                .Append(string.Join(",", SelectedOfficerIds.Select(item => item.ToString()).ToArray())).Append(']');
        }
    }

    internal sealed class CommandShape
    {
        internal CommandShape(string name, int abilityOffset)
        {
            Name = name;
            AbilityOffset = abilityOffset;
        }

        internal readonly string Name;
        internal readonly int AbilityOffset;
    }

    internal sealed class PersonTable
    {
        private readonly byte[] bytes;

        internal PersonTable(byte[] bytes)
        {
            int expected = checked(San9Pk101MemoryLayout.PersonStride * San9Pk101MemoryLayout.PersonCount);
            if (bytes == null || bytes.Length != expected)
                throw new UiObservationReadException("UI_PERSON_TABLE_READ_INVALID", "The complete PERSON table was not read.");
            this.bytes = bytes;
        }

        internal int GetPersonId(uint pointer, string label)
        {
            int index = GetIndex(pointer, label);
            int stored = ReadUInt16(bytes, checked(index * San9Pk101MemoryLayout.PersonStride + San9Pk101MemoryLayout.PersonIdOffset));
            if (stored != index)
                throw new UiObservationReadException("UI_PERSON_ID_INVALID", label + " PERSON ID does not equal its table index.");
            return index;
        }

        internal uint GetResidencePointer(uint pointer)
        {
            return GetUInt32(pointer, San9Pk101MemoryLayout.PersonResidencePointerOffset);
        }

        internal uint GetUInt32(uint pointer, int fieldOffset)
        {
            int index = GetIndex(pointer, "PERSON field");
            return ReadUInt32(bytes, checked(index * San9Pk101MemoryLayout.PersonStride + fieldOffset));
        }

        private static int GetIndex(uint pointer, string label)
        {
            if (pointer < San9Pk101MemoryLayout.PersonBase)
                throw new UiObservationReadException("UI_PERSON_POINTER_INVALID", label + " pointer precedes the PERSON table.");
            uint relative = pointer - San9Pk101MemoryLayout.PersonBase;
            if (relative % San9Pk101MemoryLayout.PersonStride != 0)
                throw new UiObservationReadException("UI_PERSON_POINTER_INVALID", label + " pointer is not PERSON-aligned.");
            uint index = relative / San9Pk101MemoryLayout.PersonStride;
            if (index >= San9Pk101MemoryLayout.PersonCount)
                throw new UiObservationReadException("UI_PERSON_POINTER_INVALID", label + " pointer exceeds the PERSON table.");
            return checked((int)index);
        }

        private static int ReadUInt16(byte[] source, int offset)
        {
            return source[offset] | (source[offset + 1] << 8);
        }

        private static uint ReadUInt32(byte[] source, int offset)
        {
            return unchecked((uint)(source[offset]
                | (source[offset + 1] << 8)
                | (source[offset + 2] << 16)
                | (source[offset + 3] << 24)));
        }
    }

    internal sealed class UiObservationReadException : Exception
    {
        internal UiObservationReadException(string code, string message)
            : base(message)
        {
            Code = code;
        }

        internal string Code { get; private set; }
    }

    internal sealed class ObservationLifetimeTracker
    {
        private readonly object sync = new object();
        private readonly Dictionary<string, long> firstSeen = new Dictionary<string, long>(StringComparer.Ordinal);
        private HashSet<string> active = new HashSet<string>(StringComparer.Ordinal);
        private long sequence;
        private string sceneIdentity;
        private long sceneGeneration;

        internal Dictionary<string, string> Observe(
            ProcessIdentitySnapshot identity,
            CaptureValues values,
            out string sceneToken,
            out string windowToken)
        {
            lock (sync)
            {
                sequence++;
                string nextSceneIdentity = string.Format(
                    "{0}:{1}:{2:X8}:{3:X8}:{4}",
                    identity.ProcessId,
                    identity.CreationFileTimeUtc,
                    values.Root.ScenePointer,
                    values.Root.SceneVtable,
                    values.RawSceneSelector);
                if (!string.Equals(sceneIdentity, nextSceneIdentity, StringComparison.Ordinal))
                {
                    sceneIdentity = nextSceneIdentity;
                    sceneGeneration++;
                    active.Clear();
                }

                Dictionary<string, ObjectIdentity> objects = new Dictionary<string, ObjectIdentity>(StringComparer.Ordinal);
                objects.Add("scene", new ObjectIdentity(values.Root.ScenePointer, values.Root.SceneVtable));
                objects.Add("root", new ObjectIdentity(values.Root.SchedulerRootPointer, values.Root.SchedulerRootVtable));
                for (int index = 0; index < values.Root.Nodes.Length; index++)
                    objects.Add("task:" + index, new ObjectIdentity(values.Root.Nodes[index].Address, values.Root.Nodes[index].Vtable));
                if (values.Root.ControllerPointer.HasValue)
                    objects.Add("controller", new ObjectIdentity(values.Root.ControllerPointer.Value, 0x00610bc8));
                if (values.Menu.Present) objects.Add("menu", new ObjectIdentity(values.Menu.Address, values.Menu.Vtable));
                if (values.Outer.Present) objects.Add("outer", new ObjectIdentity(values.Outer.Address, values.Outer.Vtable));
                if (values.Selector.Present) objects.Add("selector", new ObjectIdentity(values.Selector.Address, values.Selector.Vtable));

                HashSet<string> nextActive = new HashSet<string>(StringComparer.Ordinal);
                Dictionary<string, string> tokens = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (KeyValuePair<string, ObjectIdentity> pair in objects)
                {
                    string identityKey = string.Format(
                        "{0}:{1}:{2}:{3:X8}:{4:X8}",
                        identity.ProcessId,
                        identity.CreationFileTimeUtc,
                        sceneGeneration,
                        pair.Value.Address,
                        pair.Value.Vtable);
                    nextActive.Add(identityKey);
                    long first;
                    if (!active.Contains(identityKey) || !firstSeen.TryGetValue(identityKey, out first))
                    {
                        first = sequence;
                        firstSeen[identityKey] = first;
                    }
                    tokens[pair.Key] = string.Format(
                        "pid={0};created={1};sceneGen={2};addr=0x{3:X8};vptr=0x{4:X8};first={5}",
                        identity.ProcessId,
                        identity.CreationFileTimeUtc,
                        sceneGeneration,
                        pair.Value.Address,
                        pair.Value.Vtable,
                        first);
                }
                active = nextActive;
                sceneToken = tokens["scene"];
                windowToken = string.Format(
                    "pid={0};created={1};sceneGen={2};hwnd=0x{3:X8};first={4}",
                    identity.ProcessId,
                    identity.CreationFileTimeUtc,
                    sceneGeneration,
                    unchecked((uint)values.MainWindowHandle),
                    sequence);
                return tokens;
            }
        }

        private sealed class ObjectIdentity
        {
            internal ObjectIdentity(uint address, uint vtable)
            {
                Address = address;
                Vtable = vtable;
            }

            internal readonly uint Address;
            internal readonly uint Vtable;
        }
    }

    internal sealed class Win32UiCandidateScanner : IUiCandidateScanner
    {
        private const uint MemCommit = 0x1000;
        private const uint MemPrivate = 0x20000;
        private const uint PageNoAccess = 0x01;
        private const uint PageGuard = 0x100;
        private const int MaximumChunk = 1024 * 1024;
        private const int MaximumRegions = 200000;
        private const long MaximumBytes = 768L * 1024 * 1024;
        private const int MaximumMatchesPerVtable = 32;

        private static readonly uint[] TargetVtables =
        {
            San9Pk101UiObservationReader.CommandMenuVtable,
            San9Pk101UiObservationReader.SelectorVtable,
            0x0060b920,
            0x0060ccb0,
            0x0060bba0,
            0x0060bce8,
            0x0060c370
        };

        private SafeNativeHandle handle;

        internal Win32UiCandidateScanner(int processId)
        {
            handle = NativeMethods.OpenProcess(
                ProcessAccessFlags.QueryLimitedInformation | ProcessAccessFlags.VmRead,
                false,
                processId);
            if (handle == null || handle.IsInvalid)
                throw new UiObservationReadException("UI_SCANNER_OPEN_FAILED", "Unable to open the exact process read-only for VirtualQueryEx.");
        }

        public UiCandidateScan Scan(IReadOnlyProcessMemory memory, ProcessIdentitySnapshot expectedIdentity)
        {
            if (memory == null) throw new ArgumentNullException("memory");
            UiCandidateScan result = new UiCandidateScan();
            Dictionary<uint, List<uint>> matches = new Dictionary<uint, List<uint>>();
            foreach (uint vtable in TargetVtables) matches.Add(vtable, new List<uint>());

            ulong address = 0x10000;
            while (address <= San9Pk101MemoryLayout.MaximumUserAddress32)
            {
                if (++result.QueriedRegionCount > MaximumRegions)
                    throw new UiObservationReadException("UI_SCANNER_REGION_LIMIT", "VirtualQueryEx exceeded its bounded region count.");
                MemoryBasicInformation32 information;
                UIntPtr queried = VirtualQueryEx(
                    handle,
                    new IntPtr(unchecked((int)(uint)address)),
                    out information,
                    new UIntPtr(unchecked((uint)Marshal.SizeOf(typeof(MemoryBasicInformation32)))));
                if (queried == UIntPtr.Zero)
                    break;
                ulong regionBase = information.BaseAddress;
                ulong regionSize = information.RegionSize;
                if (regionSize == 0 || regionBase > uint.MaxValue)
                    throw new UiObservationReadException("UI_SCANNER_REGION_INVALID", "VirtualQueryEx returned an invalid region.");
                ulong next = regionBase + regionSize;
                if (next <= address) throw new UiObservationReadException("UI_SCANNER_REGION_OVERFLOW", "VirtualQueryEx did not advance.");

                if (information.State == MemCommit
                    && information.Type == MemPrivate
                    && (information.Protect & (PageNoAccess | PageGuard)) == 0
                    && IsWritable(information.Protect))
                {
                    if (regionBase < 0x10000 || next > (ulong)San9Pk101MemoryLayout.MaximumUserAddress32 + 1UL)
                        throw new UiObservationReadException("UI_SCANNER_REGION_RANGE", "A selected scan region is outside the 32-bit user range.");
                    result.ScannedRegionCount++;
                    result.ScannedByteCount = checked(result.ScannedByteCount + checked((long)regionSize));
                    if (result.ScannedByteCount > MaximumBytes)
                        throw new UiObservationReadException("UI_SCANNER_BYTE_LIMIT", "Writable private-memory scanning exceeded 768 MiB.");
                    ScanRegion(memory, unchecked((uint)regionBase), checked((uint)regionSize), matches);
                }
                address = next;
            }

            foreach (KeyValuePair<uint, List<uint>> pair in matches)
                result.AddressesByVtable[pair.Key] = pair.Value.ToArray();
            return result;
        }

        private static void ScanRegion(
            IReadOnlyProcessMemory memory,
            uint baseAddress,
            uint size,
            IDictionary<uint, List<uint>> matches)
        {
            ulong offset = 0;
            while (offset < size)
            {
                int count = checked((int)Math.Min((ulong)MaximumChunk, size - offset));
                byte[] bytes;
                try { bytes = memory.ReadBytes(checked((long)((ulong)baseAddress + offset)), count); }
                catch (Exception exception)
                {
                    throw new UiObservationReadException("UI_SCANNER_READ_FAILED", exception.Message);
                }
                for (int index = 0; index <= bytes.Length - 4; index += 4)
                {
                    uint value = unchecked((uint)(bytes[index]
                        | (bytes[index + 1] << 8)
                        | (bytes[index + 2] << 16)
                        | (bytes[index + 3] << 24)));
                    List<uint> list;
                    if (!matches.TryGetValue(value, out list)) continue;
                    if (list.Count >= MaximumMatchesPerVtable)
                        throw new UiObservationReadException("UI_SCANNER_MATCH_LIMIT", "A UI vptr produced more than 32 aligned candidates.");
                    list.Add(checked((uint)((ulong)baseAddress + offset + unchecked((uint)index))));
                }
                offset += unchecked((uint)count);
            }
        }

        private static bool IsWritable(uint protect)
        {
            uint basic = protect & 0xff;
            return basic == 0x04 || basic == 0x08 || basic == 0x40 || basic == 0x80;
        }

        public void Dispose()
        {
            if (handle != null)
            {
                handle.Dispose();
                handle = null;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MemoryBasicInformation32
        {
            internal uint BaseAddress;
            internal uint AllocationBase;
            internal uint AllocationProtect;
            internal uint RegionSize;
            internal uint State;
            internal uint Protect;
            internal uint Type;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern UIntPtr VirtualQueryEx(
            SafeNativeHandle process,
            IntPtr address,
            out MemoryBasicInformation32 information,
            UIntPtr length);
    }
}
