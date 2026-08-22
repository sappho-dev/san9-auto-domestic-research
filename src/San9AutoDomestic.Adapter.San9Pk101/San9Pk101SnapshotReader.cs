using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using San9AutoDomestic.Core.Configuration;
using San9AutoDomestic.Core.Domain;

namespace San9AutoDomestic.Adapter.San9Pk101
{
    internal sealed class San9Pk101SnapshotReader
    {
        private sealed class TableImage
        {
            internal byte[] Raw;
            internal byte[] Forces;
            internal byte[] Cities;
            internal byte[] Persons;
        }

        private sealed class ContainerImage
        {
            internal uint Address;
            internal bool ReadSucceeded;
            internal uint Flags;
            internal uint OwnerCityPointer;
            internal string Error;
        }

        private sealed class ResidentNodeImage
        {
            internal uint Address;
            internal uint Next;
            internal uint Previous;
            internal uint PersonPointer;
            internal int? PersonId;
        }

        private sealed class ResidentListImage
        {
            internal int CityId;
            internal uint First;
            internal uint Last;
            internal uint DeclaredCount;
            internal uint NextAfterDeclaredNodes;
            internal readonly List<ResidentNodeImage> Nodes = new List<ResidentNodeImage>();
            internal readonly List<string> Faults = new List<string>();
        }

        private sealed class ExternalImage
        {
            internal readonly Dictionary<uint, ContainerImage> Containers =
                new Dictionary<uint, ContainerImage>();
            internal ResidentListImage[] Lists;
            internal byte[] CanonicalBytes;
        }

        private sealed class StableImage
        {
            internal TableImage Tables;
            internal ExternalImage External;
        }

        internal San9Pk101ReadReport Read(
            ReadOnlyProcessConnection connection,
            San9Pk101DiagnosticReport baseline)
        {
            return Read((IReadOnlyProcessMemory)connection, baseline);
        }

        internal San9Pk101ReadReport Read(
            IReadOnlyProcessMemory connection,
            San9Pk101DiagnosticReport baseline)
        {
            if (connection == null)
            {
                throw new ArgumentNullException("connection");
            }

            San9Pk101ReadReport report = new San9Pk101ReadReport();
            report.Baseline = baseline;
            List<ReadInvariantDiagnostic> issues = new List<ReadInvariantDiagnostic>();
            ProcessIdentitySnapshot initialIdentity = connection.InitialIdentity;
            ApplyInitialIdentity(report, initialIdentity, issues);

            if (baseline != null
                && baseline.ConflictScan != null
                && baseline.ConflictScan.HasBlockingConflicts)
            {
                report.ConflictsIgnoredForReadOnlyScan = true;
                issues.Add(new ReadInvariantDiagnostic(
                    "CONFLICTS_PRESENT_READ_ONLY_CONTINUES",
                    DiagnosticSeverity.Warning,
                    ReadEntityKind.Snapshot,
                    null,
                    "Known modifiers close the execution gate but do not prevent a V1 read-only snapshot."));
            }

            StableImage image = null;
            try
            {
                image = ReadStableImage(connection, report);
                if (image == null)
                {
                    issues.Add(Block(
                        "SNAPSHOT_UNSTABLE",
                        ReadEntityKind.Snapshot,
                        null,
                        "The complete table region or external resident/container graph changed during all three A/B attempts."));
                }
                else
                {
                    report.StableSummarySha256 = ComputeStableSummarySha256(image);
                    ParseStableImage(image, report, issues);
                }
            }
            catch (Exception exception)
            {
                issues.Add(Block(
                    "SNAPSHOT_READ_OR_PARSE_FAILED",
                    ReadEntityKind.Snapshot,
                    null,
                    exception.GetType().Name + ": " + exception.Message));
            }

            RevalidateIdentityAfterRead(connection, initialIdentity, report, issues);
            report.CoreCanActValuesAreConservativeFalse = true;
            report.CoreCommandListsAreEmpty = true;
            issues.Add(Warn(
                "CAN_ACT_UNKNOWN",
                ReadEntityKind.Snapshot,
                null,
                "No verified CanAct field is available. Core officer CanAct values are conservatively false."));
            issues.Add(Warn(
                "COMMAND_STATE_NOT_READ",
                ReadEntityKind.Snapshot,
                null,
                "No verified native command-state layout is available. Every Core command list is empty."));
            issues.Add(Warn(
                "CITY_COMBAT_STATE_UNKNOWN",
                ReadEntityKind.Snapshot,
                null,
                "Byte city+0x7A is retained only as a raw candidate; no combat-state meaning is claimed."));

            report.ReadSucceeded = report.Snapshot != null
                && report.RelevantFieldsWereStable
                && report.ProcessIdentityRevalidatedAfterRead
                && !issues.Any(issue => issue.Severity == DiagnosticSeverity.Blocking);
            report.Issues = issues.ToArray();
            return report;
        }

        private static void ParseStableImage(
            StableImage image,
            San9Pk101ReadReport report,
            List<ReadInvariantDiagnostic> issues)
        {
            ForceReadRecord[] forces = ParseForces(image.Tables.Forces, issues);
            int? playerForceId = ResolvePlayerForce(forces, issues);
            CityReadRecord[] cities = ParseCities(image.Tables.Cities, forces, playerForceId, issues);
            PersonReadRecord[] persons = ParsePersons(
                image.Tables.Persons,
                cities,
                image.External,
                issues);
            AttachAndVerifyResidents(cities, persons, image.External, issues);

            report.PlayerForceId = playerForceId;
            report.Forces = forces;
            report.Cities = cities;
            report.Persons = persons;

            if (!issues.Any(issue => issue.Severity == DiagnosticSeverity.Blocking)
                && playerForceId.HasValue)
            {
                report.Snapshot = BuildCoreSnapshot(
                    playerForceId.Value,
                    forces,
                    cities,
                    persons,
                    report);
            }
        }

        private static StableImage ReadStableImage(
            IReadOnlyProcessMemory connection,
            San9Pk101ReadReport report)
        {
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                TableImage firstTables = ReadCompleteTableRegion(connection);
                ExternalImage firstExternal = CaptureExternalImage(connection, firstTables);
                TableImage secondTables = ReadCompleteTableRegion(connection);
                ExternalImage secondExternal = CaptureExternalImage(connection, secondTables);
                report.StableReadAttempts = attempt;

                if (firstTables.Raw.SequenceEqual(secondTables.Raw)
                    && firstExternal.CanonicalBytes.SequenceEqual(secondExternal.CanonicalBytes))
                {
                    report.RelevantFieldsWereStable = true;
                    return new StableImage
                    {
                        Tables = secondTables,
                        External = secondExternal
                    };
                }
            }

            return null;
        }

        private static TableImage ReadCompleteTableRegion(IReadOnlyProcessMemory connection)
        {
            uint regionBase = San9Pk101MemoryLayout.CityBase;
            uint personEnd = checked(
                San9Pk101MemoryLayout.PersonBase
                + unchecked((uint)(San9Pk101MemoryLayout.PersonStride * San9Pk101MemoryLayout.PersonCount)));
            int regionLength = checked((int)(personEnd - regionBase));
            byte[] raw = connection.ReadBytes(regionBase, regionLength);

            return new TableImage
            {
                Raw = raw,
                Cities = Slice(
                    raw,
                    checked((int)(San9Pk101MemoryLayout.CityBase - regionBase)),
                    San9Pk101MemoryLayout.CityStride * San9Pk101MemoryLayout.CityCount),
                Forces = Slice(
                    raw,
                    checked((int)(San9Pk101MemoryLayout.ForceBase - regionBase)),
                    San9Pk101MemoryLayout.ForceStride * San9Pk101MemoryLayout.ForceCount),
                Persons = Slice(
                    raw,
                    checked((int)(San9Pk101MemoryLayout.PersonBase - regionBase)),
                    San9Pk101MemoryLayout.PersonStride * San9Pk101MemoryLayout.PersonCount)
            };
        }

        private static ExternalImage CaptureExternalImage(
            IReadOnlyProcessMemory connection,
            TableImage tables)
        {
            ExternalImage external = new ExternalImage();
            CaptureContainers(connection, tables.Persons, external);
            external.Lists = new ResidentListImage[San9Pk101MemoryLayout.CityCount];
            for (int cityId = 0; cityId < external.Lists.Length; cityId++)
            {
                external.Lists[cityId] = CaptureResidentList(connection, tables.Cities, cityId);
            }

            external.CanonicalBytes = BuildExternalCanonicalBytes(external);
            return external;
        }

        private static void CaptureContainers(
            IReadOnlyProcessMemory connection,
            byte[] personTable,
            ExternalImage external)
        {
            SortedSet<uint> pointers = new SortedSet<uint>();
            for (int id = 0; id < San9Pk101MemoryLayout.PersonCount; id++)
            {
                int record = id * San9Pk101MemoryLayout.PersonStride;
                uint pointer = ReadUInt32(
                    personTable,
                    record + San9Pk101MemoryLayout.PersonResidencePointerOffset);
                if (pointer != 0)
                {
                    pointers.Add(pointer);
                }
            }

            foreach (uint pointer in pointers)
            {
                ContainerImage container = new ContainerImage { Address = pointer };
                try
                {
                    int bytesNeeded = San9Pk101MemoryLayout.ContainerOwnerCityPointerOffset
                        - San9Pk101MemoryLayout.ContainerEmbeddedFlagsOffset + 4;
                    if (!IsPlausibleReadRange(
                        checked(pointer + unchecked((uint)San9Pk101MemoryLayout.ContainerEmbeddedFlagsOffset)),
                        bytesNeeded))
                    {
                        throw new InvalidDataException("Container address is outside the supported 32-bit user range.");
                    }

                    byte[] bytes = connection.ReadBytes(
                        checked(pointer + unchecked((uint)San9Pk101MemoryLayout.ContainerEmbeddedFlagsOffset)),
                        bytesNeeded);
                    container.Flags = ReadUInt32(bytes, 0);
                    container.OwnerCityPointer = ReadUInt32(
                        bytes,
                        San9Pk101MemoryLayout.ContainerOwnerCityPointerOffset
                            - San9Pk101MemoryLayout.ContainerEmbeddedFlagsOffset);
                    container.ReadSucceeded = true;
                }
                catch (Exception exception)
                {
                    container.Error = exception.GetType().Name + ": " + exception.Message;
                }

                external.Containers.Add(pointer, container);
            }
        }

        private static ResidentListImage CaptureResidentList(
            IReadOnlyProcessMemory connection,
            byte[] cityTable,
            int cityId)
        {
            int record = cityId * San9Pk101MemoryLayout.CityStride;
            ResidentListImage list = new ResidentListImage
            {
                CityId = cityId,
                First = ReadUInt32(
                    cityTable,
                    record + San9Pk101MemoryLayout.CityResidentListFirstPointerOffset),
                Last = ReadUInt32(
                    cityTable,
                    record + San9Pk101MemoryLayout.CityResidentListLastPointerOffset),
                DeclaredCount = ReadUInt32(
                    cityTable,
                    record + San9Pk101MemoryLayout.CityValidResidentOfficerCountOffset)
            };

            if (list.DeclaredCount > San9Pk101MemoryLayout.PersonCount)
            {
                list.Faults.Add("declared-count-out-of-range");
                return list;
            }

            uint current = list.First;
            HashSet<uint> seenNodes = new HashSet<uint>();
            for (uint index = 0; index < list.DeclaredCount; index++)
            {
                if (current == 0)
                {
                    list.Faults.Add("chain-ended-before-declared-count");
                    break;
                }

                if (!seenNodes.Add(current))
                {
                    list.Faults.Add("node-cycle-or-duplicate");
                    break;
                }

                if (!IsPlausibleReadRange(current, San9Pk101MemoryLayout.ResidentNodeSize))
                {
                    list.Faults.Add("node-address-out-of-range");
                    break;
                }

                try
                {
                    byte[] bytes = connection.ReadBytes(current, San9Pk101MemoryLayout.ResidentNodeSize);
                    uint personPointer = ReadUInt32(
                        bytes,
                        San9Pk101MemoryLayout.ResidentNodePersonPointerOffset);
                    int personId;
                    bool personAligned = TryTableIndex(
                        personPointer,
                        San9Pk101MemoryLayout.PersonBase,
                        San9Pk101MemoryLayout.PersonStride,
                        San9Pk101MemoryLayout.PersonCount,
                        out personId);
                    ResidentNodeImage node = new ResidentNodeImage
                    {
                        Address = current,
                        Next = ReadUInt32(bytes, San9Pk101MemoryLayout.ResidentNodeNextPointerOffset),
                        Previous = ReadUInt32(bytes, San9Pk101MemoryLayout.ResidentNodePreviousPointerOffset),
                        PersonPointer = personPointer,
                        PersonId = personAligned ? (int?)personId : null
                    };
                    list.Nodes.Add(node);
                    current = node.Next;
                }
                catch (Exception exception)
                {
                    list.Faults.Add("node-read-failed:" + exception.GetType().Name);
                    break;
                }
            }

            list.NextAfterDeclaredNodes = current;
            if (current != 0 && seenNodes.Contains(current))
            {
                list.Faults.Add("node-cycle-or-duplicate");
            }
            return list;
        }

        private static byte[] BuildExternalCanonicalBytes(ExternalImage external)
        {
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                foreach (ContainerImage container in external.Containers.Values.OrderBy(item => item.Address))
                {
                    writer.Write(container.Address);
                    writer.Write(container.ReadSucceeded);
                    writer.Write(container.Flags);
                    writer.Write(container.OwnerCityPointer);
                    writer.Write(container.Error ?? string.Empty);
                }

                foreach (ResidentListImage list in external.Lists.OrderBy(item => item.CityId))
                {
                    writer.Write(list.CityId);
                    writer.Write(list.First);
                    writer.Write(list.Last);
                    writer.Write(list.DeclaredCount);
                    writer.Write(list.NextAfterDeclaredNodes);
                    writer.Write(list.Faults.Count);
                    foreach (string fault in list.Faults)
                    {
                        writer.Write(fault);
                    }

                    writer.Write(list.Nodes.Count);
                    foreach (ResidentNodeImage node in list.Nodes)
                    {
                        writer.Write(node.Address);
                        writer.Write(node.Next);
                        writer.Write(node.Previous);
                        writer.Write(node.PersonPointer);
                        writer.Write(node.PersonId.HasValue ? node.PersonId.Value : -1);
                    }
                }

                writer.Flush();
                return stream.ToArray();
            }
        }

        private static ForceReadRecord[] ParseForces(
            byte[] table,
            List<ReadInvariantDiagnostic> issues)
        {
            ForceReadRecord[] records = new ForceReadRecord[San9Pk101MemoryLayout.ForceCount];
            for (int id = 0; id < records.Length; id++)
            {
                int record = id * San9Pk101MemoryLayout.ForceStride;
                uint address = AddressFor(
                    San9Pk101MemoryLayout.ForceBase,
                    San9Pk101MemoryLayout.ForceStride,
                    id);
                uint rawMoney = ReadUInt32(table, record + San9Pk101MemoryLayout.ForceMoneyOffset);
                uint flags = ReadUInt32(table, record + San9Pk101MemoryLayout.ForceFlagsOffset);
                uint mainPointer = ReadUInt32(
                    table,
                    record + San9Pk101MemoryLayout.ForceMainForcePointerOffset);
                uint leaderPointer = ReadUInt32(
                    table,
                    record + San9Pk101MemoryLayout.ForceLeaderPointerOffset);
                int mainId;
                bool mainValid = TryTableIndex(
                    mainPointer,
                    San9Pk101MemoryLayout.ForceBase,
                    San9Pk101MemoryLayout.ForceStride,
                    San9Pk101MemoryLayout.ForceCount,
                    out mainId);
                int leaderId = -1;
                bool leaderValid = leaderPointer != 0 && TryTableIndex(
                    leaderPointer,
                    San9Pk101MemoryLayout.PersonBase,
                    San9Pk101MemoryLayout.PersonStride,
                    San9Pk101MemoryLayout.PersonCount,
                    out leaderId);
                bool populated = leaderPointer != 0;

                if (populated && !leaderValid)
                {
                    issues.Add(Block(
                        "FORCE_LEADER_POINTER_INVALID",
                        ReadEntityKind.Force,
                        id,
                        string.Format("0x{0:X8} is not an aligned person-table pointer.", leaderPointer)));
                }

                if (populated && !mainValid)
                {
                    issues.Add(Block(
                        "FORCE_MAIN_POINTER_INVALID",
                        ReadEntityKind.Force,
                        id,
                        string.Format("0x{0:X8} is not an aligned force-table pointer.", mainPointer)));
                }

                if (populated && rawMoney > GameRules.GameMoneyMax)
                {
                    issues.Add(Block(
                        "FORCE_MONEY_OUT_OF_RANGE",
                        ReadEntityKind.Force,
                        id,
                        string.Format("Money {0} exceeds the Core game maximum.", rawMoney)));
                }

                records[id] = new ForceReadRecord
                {
                    Id = id,
                    Address = address,
                    Money = rawMoney <= int.MaxValue ? unchecked((int)rawMoney) : 0,
                    RawFlags = flags,
                    IsPlayerControlled = (flags & San9Pk101MemoryLayout.ForcePlayerControlFlag) != 0,
                    IsBarbarian = (flags & San9Pk101MemoryLayout.ForceBarbarianFlag) != 0,
                    MainForcePointer = mainPointer,
                    MainForceId = mainValid ? (int?)mainId : null,
                    LeaderPersonPointer = leaderPointer,
                    LeaderPersonId = leaderValid ? (int?)leaderId : null,
                    IsValidCorps = populated && leaderValid && mainValid,
                    IsMainForce = populated && leaderValid && mainValid && mainId == id
                };
            }

            foreach (ForceReadRecord corps in records.Where(item => item.IsValidCorps))
            {
                ForceReadRecord main = records[corps.MainForceId.Value];
                if (!main.IsValidCorps || !main.IsMainForce)
                {
                    issues.Add(Block(
                        "FORCE_MAIN_RECORD_NOT_VALID_SELF",
                        ReadEntityKind.Force,
                        corps.Id,
                        string.Format("Resolved main force {0} is not a populated self-main record.", main.Id)));
                }
            }

            return records;
        }

        private static int? ResolvePlayerForce(
            ForceReadRecord[] forces,
            List<ReadInvariantDiagnostic> issues)
        {
            ForceReadRecord[] controlled = forces
                .Where(force => force.IsValidCorps
                    && force.IsPlayerControlled
                    && !force.IsBarbarian)
                .ToArray();
            if (controlled.Length == 0)
            {
                issues.Add(Block(
                    "PLAYER_CONTROLLED_CORPS_NOT_FOUND",
                    ReadEntityKind.Snapshot,
                    null,
                    "No valid non-barbarian corps has the player-control flag."));
                return null;
            }

            int[] mainIds = controlled.Select(force => force.MainForceId.Value).Distinct().ToArray();
            if (mainIds.Length != 1)
            {
                issues.Add(Block(
                    "PLAYER_MAIN_FORCE_NOT_UNIQUE",
                    ReadEntityKind.Snapshot,
                    null,
                    string.Format(
                        "Player-controlled valid corps resolve to {0} distinct main forces: {1}.",
                        mainIds.Length,
                        string.Join(",", mainIds))));
                return null;
            }

            ForceReadRecord main = forces[mainIds[0]];
            if (!main.IsValidCorps || !main.IsMainForce || main.IsBarbarian)
            {
                issues.Add(Block(
                    "PLAYER_MAIN_FORCE_INVALID",
                    ReadEntityKind.Snapshot,
                    null,
                    "The resolved player main force is not a valid non-barbarian self-main record."));
                return null;
            }

            return main.Id;
        }

        private static CityReadRecord[] ParseCities(
            byte[] table,
            ForceReadRecord[] forces,
            int? playerForceId,
            List<ReadInvariantDiagnostic> issues)
        {
            CityReadRecord[] records = new CityReadRecord[San9Pk101MemoryLayout.CityCount];
            for (int id = 0; id < records.Length; id++)
            {
                int record = id * San9Pk101MemoryLayout.CityStride;
                uint address = AddressFor(
                    San9Pk101MemoryLayout.CityBase,
                    San9Pk101MemoryLayout.CityStride,
                    id);
                byte cityType = table[record + San9Pk101MemoryLayout.CityTypeOffset];
                if (cityType != San9Pk101MemoryLayout.CityTypeValue)
                {
                    issues.Add(Block(
                        "CITY_TYPE_MISMATCH",
                        ReadEntityKind.City,
                        id,
                        string.Format("Expected type 5, got {0}.", cityType)));
                }

                DecodedFixedBig5 decodedName = Big5NameDecoder.Decode(
                    table,
                    record + San9Pk101MemoryLayout.CityNameOffset,
                    San9Pk101MemoryLayout.CityNameLength,
                    false);
                string cityName = decodedName.DecodeSucceeded
                    && decodedName.HadTerminator
                    && !string.IsNullOrEmpty(decodedName.Normalized)
                    ? decodedName.Normalized
                    : "城市#" + id;
                if (cityName.StartsWith("城市#", StringComparison.Ordinal))
                {
                    issues.Add(Warn(
                        "CITY_NAME_DISPLAY_FALLBACK",
                        ReadEntityKind.City,
                        id,
                        string.Format("Big5 display name was replaced by an ID placeholder; raw={0}.", decodedName.RawHex)));
                }

                uint selfPointer = ReadUInt32(table, record + San9Pk101MemoryLayout.CitySelfPointerOffset);
                if (selfPointer != address)
                {
                    issues.Add(Block(
                        "CITY_SELF_POINTER_MISMATCH",
                        ReadEntityKind.City,
                        id,
                        string.Format("Expected 0x{0:X8}, got 0x{1:X8}.", address, selfPointer)));
                }

                uint corpsPointer = ReadUInt32(table, record + San9Pk101MemoryLayout.CityCorpsPointerOffset);
                int corpsId = -1;
                bool corpsAligned = corpsPointer == 0 || TryTableIndex(
                    corpsPointer,
                    San9Pk101MemoryLayout.ForceBase,
                    San9Pk101MemoryLayout.ForceStride,
                    San9Pk101MemoryLayout.ForceCount,
                    out corpsId);
                if (!corpsAligned)
                {
                    issues.Add(Block(
                        "CITY_CORPS_POINTER_INVALID",
                        ReadEntityKind.City,
                        id,
                        string.Format("0x{0:X8} is not zero or an aligned corps pointer.", corpsPointer)));
                }

                int? resolvedCorpsId = corpsPointer != 0 && corpsAligned ? (int?)corpsId : null;
                int? ownerForceId = null;
                if (resolvedCorpsId.HasValue)
                {
                    ForceReadRecord corps = forces[resolvedCorpsId.Value];
                    corps.IsReferencedByCity = true;
                    if (!corps.IsValidCorps)
                    {
                        issues.Add(Block(
                            "CITY_REFERENCES_INVALID_CORPS",
                            ReadEntityKind.City,
                            id,
                            string.Format("Corps {0} is not populated with valid leader/main pointers.", corps.Id)));
                    }
                    else
                    {
                        ownerForceId = corps.MainForceId;
                    }
                }

                uint declaredCount = ReadUInt32(
                    table,
                    record + San9Pk101MemoryLayout.CityValidResidentOfficerCountOffset);
                if (declaredCount > San9Pk101MemoryLayout.PersonCount)
                {
                    issues.Add(Block(
                        "CITY_RESIDENT_COUNT_OUT_OF_RANGE",
                        ReadEntityKind.City,
                        id,
                        string.Format("Declared count {0} exceeds the person table.", declaredCount)));
                }
                else if (!resolvedCorpsId.HasValue && declaredCount != 0)
                {
                    issues.Add(Block(
                        "UNOWNED_CITY_HAS_VALID_RESIDENT_COUNT",
                        ReadEntityKind.City,
                        id,
                        string.Format("Unowned city declares {0} resident officers.", declaredCount)));
                }

                ForceReadRecord resolvedCorps = resolvedCorpsId.HasValue
                    ? forces[resolvedCorpsId.Value]
                    : null;
                records[id] = new CityReadRecord
                {
                    Id = id,
                    Address = address,
                    Name = cityName,
                    RawNameHex = decodedName.RawHex,
                    IsInCombat = null,
                    RawCandidateFlags7A = table[
                        record + San9Pk101MemoryLayout.CityRawCandidateFlags7aOffset],
                    SelfPointer = selfPointer,
                    CorpsPointer = corpsPointer,
                    CorpsId = resolvedCorpsId,
                    OwnerForceId = ownerForceId,
                    IsDirectlyControlled = playerForceId.HasValue
                        && resolvedCorps != null
                        && resolvedCorps.IsValidCorps
                        && !resolvedCorps.IsBarbarian
                        && resolvedCorps.IsPlayerControlled
                        && resolvedCorps.MainForceId.Value == playerForceId.Value,
                    DeclaredValidResidentOfficerCount = declaredCount <= int.MaxValue
                        ? unchecked((int)declaredCount)
                        : 0
                };
            }

            return records;
        }

        private static PersonReadRecord[] ParsePersons(
            byte[] table,
            CityReadRecord[] cities,
            ExternalImage external,
            List<ReadInvariantDiagnostic> issues)
        {
            Dictionary<uint, int> cityByAddress = cities.ToDictionary(city => city.Address, city => city.Id);
            PersonReadRecord[] records = new PersonReadRecord[San9Pk101MemoryLayout.PersonCount];
            for (int id = 0; id < records.Length; id++)
            {
                int record = id * San9Pk101MemoryLayout.PersonStride;
                ushort recordId = ReadUInt16(table, record + San9Pk101MemoryLayout.PersonIdOffset);
                if (recordId != id)
                {
                    issues.Add(Block(
                        "PERSON_RECORD_ID_MISMATCH",
                        ReadEntityKind.Person,
                        id,
                        string.Format("Record contains ID {0}.", recordId)));
                }

                DecodedFixedBig5 surname = Big5NameDecoder.Decode(
                    table,
                    record + San9Pk101MemoryLayout.PersonSurnameOffset,
                    San9Pk101MemoryLayout.PersonSurnameLength,
                    true);
                DecodedFixedBig5 givenName = Big5NameDecoder.Decode(
                    table,
                    record + San9Pk101MemoryLayout.PersonGivenNameOffset,
                    San9Pk101MemoryLayout.PersonGivenNameLength,
                    false);
                uint identity = ReadUInt32(table, record + San9Pk101MemoryLayout.PersonIdentityOffset);
                bool activeIdentity = identity >= San9Pk101MemoryLayout.MinimumValidResidentIdentity
                    && identity <= San9Pk101MemoryLayout.MaximumValidResidentIdentity;
                uint containerPointer = ReadUInt32(
                    table,
                    record + San9Pk101MemoryLayout.PersonResidencePointerOffset);
                ContainerImage container = null;
                bool hasContainer = containerPointer != 0
                    && external.Containers.TryGetValue(containerPointer, out container);
                int? residentCityId = null;
                uint containerFlags = hasContainer && container.ReadSucceeded ? container.Flags : 0;
                uint ownerPointer = hasContainer && container.ReadSucceeded
                    ? container.OwnerCityPointer
                    : 0;

                if (activeIdentity && (!hasContainer || !container.ReadSucceeded))
                {
                    issues.Add(Block(
                        "ACTIVE_PERSON_CONTAINER_UNREADABLE",
                        ReadEntityKind.Person,
                        id,
                        hasContainer ? container.Error : "Residence container pointer is zero or absent."));
                }
                else if (hasContainer
                    && container.ReadSucceeded
                    && (container.Flags & San9Pk101MemoryLayout.ContainerEmbeddedFlag) != 0)
                {
                    int cityId;
                    if (cityByAddress.TryGetValue(container.OwnerCityPointer, out cityId))
                    {
                        residentCityId = cityId;
                    }
                    // Active officers can also be embedded in gates, ports, camps, or field
                    // units. Only an exact CITY-table owner is classified as a city resident;
                    // the city list equality check below proves the in-city subset.
                }

                bool included = activeIdentity && residentCityId.HasValue;
                uint might = ReadUInt32(table, record + San9Pk101MemoryLayout.PersonEffectiveMightOffset);
                uint intelligence = ReadUInt32(
                    table,
                    record + San9Pk101MemoryLayout.PersonEffectiveIntelligenceOffset);
                uint politics = ReadUInt32(
                    table,
                    record + San9Pk101MemoryLayout.PersonEffectivePoliticsOffset);
                uint leadership = ReadUInt32(
                    table,
                    record + San9Pk101MemoryLayout.PersonEffectiveLeadershipOffset);

                string normalizedName = (surname.Normalized ?? string.Empty)
                    + (givenName.Normalized ?? string.Empty);
                bool nameValid = surname.HadTerminator
                    && surname.DecodeSucceeded
                    && !string.IsNullOrEmpty(surname.Normalized)
                    && givenName.HadTerminator
                    && givenName.DecodeSucceeded;
                if (included && !nameValid)
                {
                    normalizedName = "武将#" + id;
                    issues.Add(Warn(
                        "RESIDENT_PERSON_NAME_DISPLAY_FALLBACK",
                        ReadEntityKind.Person,
                        id,
                        string.Format(
                            "Big5 display name was replaced by an ID placeholder; surname={0}, given={1}.",
                            surname.RawHex,
                            givenName.RawHex)));
                }

                if (included)
                {
                    ValidateAbility(id, "might", might, issues);
                    ValidateAbility(id, "intelligence", intelligence, issues);
                    ValidateAbility(id, "politics", politics, issues);
                    ValidateAbility(id, "leadership", leadership, issues);
                }

                records[id] = new PersonReadRecord
                {
                    Id = id,
                    RecordId = recordId,
                    Address = AddressFor(
                        San9Pk101MemoryLayout.PersonBase,
                        San9Pk101MemoryLayout.PersonStride,
                        id),
                    RawSurnameHex = surname.RawHex,
                    RawGivenNameHex = givenName.RawHex,
                    RawDecodedSurname = surname.Decoded ?? string.Empty,
                    RawDecodedGivenName = givenName.Decoded ?? string.Empty,
                    Surname = surname.Normalized ?? string.Empty,
                    GivenName = givenName.Normalized ?? string.Empty,
                    Name = string.IsNullOrEmpty(normalizedName) ? "武将#" + id : normalizedName,
                    SurnameHexPrefixStripped = surname.PrefixStripped,
                    RawIdentity = identity,
                    IsValidResidentOfficerIdentity = activeIdentity,
                    ResidencePointer = containerPointer,
                    ResidenceContainerFlags = containerFlags,
                    ResidenceOwnerCityPointer = ownerPointer,
                    ResidentCityId = residentCityId,
                    EffectiveMight = SafeInt(might),
                    EffectiveIntelligence = SafeInt(intelligence),
                    EffectivePolitics = SafeInt(politics),
                    EffectiveLeadership = SafeInt(leadership),
                    CanAct = null,
                    IsIncludedInCoreSnapshot = included
                };
            }

            return records;
        }

        private static void AttachAndVerifyResidents(
            CityReadRecord[] cities,
            PersonReadRecord[] persons,
            ExternalImage external,
            List<ReadInvariantDiagnostic> issues)
        {
            foreach (CityReadRecord city in cities)
            {
                ResidentListImage list = external.Lists[city.Id];
                ValidateResidentListStructure(list, issues);

                int[] byPosition = persons
                    .Where(person => person.IsValidResidentOfficerIdentity
                        && person.ResidentCityId == city.Id)
                    .Select(person => person.Id)
                    .OrderBy(id => id)
                    .ToArray();
                int[] byListOrder = list.Nodes
                    .Where(node => node.PersonId.HasValue)
                    .Select(node => node.PersonId.Value)
                    .ToArray();
                int[] byList = byListOrder
                    .OrderBy(id => id)
                    .ToArray();
                if (!byPosition.SequenceEqual(byList))
                {
                    issues.Add(Block(
                        "CITY_RESIDENT_SET_MISMATCH",
                        ReadEntityKind.City,
                        city.Id,
                        string.Format(
                            "Position-chain set [{0}] differs from city-list set [{1}].",
                            string.Join(",", byPosition),
                            string.Join(",", byList))));
                }

                city.ResidentOfficerIds = byPosition;
                city.ResidentOfficerIdsInCityListOrder = byListOrder;
                city.ObservedValidResidentOfficerCount = byPosition.Length;
                city.ObservedExcludedResidentPersonCount = persons.Count(person =>
                    person.ResidentCityId == city.Id
                    && !person.IsValidResidentOfficerIdentity);
                if (city.DeclaredValidResidentOfficerCount != byPosition.Length)
                {
                    issues.Add(Block(
                        "CITY_RESIDENT_COUNT_MISMATCH",
                        ReadEntityKind.City,
                        city.Id,
                        string.Format(
                            "City declares {0} residents; verified position/list set contains {1}.",
                            city.DeclaredValidResidentOfficerCount,
                            byPosition.Length)));
                }
            }
        }

        private static void ValidateResidentListStructure(
            ResidentListImage list,
            List<ReadInvariantDiagnostic> issues)
        {
            foreach (string fault in list.Faults)
            {
                issues.Add(Block(
                    "CITY_RESIDENT_LIST_CAPTURE_FAILED",
                    ReadEntityKind.City,
                    list.CityId,
                    fault));
            }

            if (list.DeclaredCount == 0)
            {
                if (list.First != 0 || list.Last != 0 || list.Nodes.Count != 0)
                {
                    issues.Add(Block(
                        "CITY_RESIDENT_EMPTY_LIST_INVALID",
                        ReadEntityKind.City,
                        list.CityId,
                        "A zero-count list must have zero first/last pointers and no nodes."));
                }

                return;
            }

            if (list.First == 0 || list.Last == 0)
            {
                issues.Add(Block(
                    "CITY_RESIDENT_LIST_ENDPOINT_MISSING",
                    ReadEntityKind.City,
                    list.CityId,
                    "A non-empty list has a zero first or last pointer."));
            }

            if (list.Nodes.Count != list.DeclaredCount)
            {
                issues.Add(Block(
                    "CITY_RESIDENT_LIST_COUNT_MISMATCH",
                    ReadEntityKind.City,
                    list.CityId,
                    string.Format("Declared {0} nodes; captured {1}.", list.DeclaredCount, list.Nodes.Count)));
            }

            if (list.Nodes.Count == 0)
            {
                return;
            }

            if (list.Nodes[0].Address != list.First
                || list.Nodes[list.Nodes.Count - 1].Address != list.Last)
            {
                issues.Add(Block(
                    "CITY_RESIDENT_LIST_TAIL_MISMATCH",
                    ReadEntityKind.City,
                    list.CityId,
                    "First/last header pointers do not match the captured chain endpoints."));
            }

            if (list.NextAfterDeclaredNodes != 0)
            {
                issues.Add(Block(
                    "CITY_RESIDENT_LIST_NOT_TERMINATED",
                    ReadEntityKind.City,
                    list.CityId,
                    string.Format(
                        "Next pointer after the declared node count is 0x{0:X8}.",
                        list.NextAfterDeclaredNodes)));
            }

            HashSet<int> personIds = new HashSet<int>();
            for (int index = 0; index < list.Nodes.Count; index++)
            {
                ResidentNodeImage node = list.Nodes[index];
                uint expectedPrevious = index == 0 ? 0 : list.Nodes[index - 1].Address;
                if (node.Previous != expectedPrevious)
                {
                    issues.Add(Block(
                        "CITY_RESIDENT_LIST_PREVIOUS_MISMATCH",
                        ReadEntityKind.City,
                        list.CityId,
                        string.Format(
                            "Node 0x{0:X8} previous=0x{1:X8}, expected 0x{2:X8}.",
                            node.Address,
                            node.Previous,
                            expectedPrevious)));
                }

                if (!node.PersonId.HasValue)
                {
                    issues.Add(Block(
                        "CITY_RESIDENT_NODE_PERSON_POINTER_INVALID",
                        ReadEntityKind.City,
                        list.CityId,
                        string.Format("Node 0x{0:X8} payload 0x{1:X8} is not person-aligned.",
                            node.Address,
                            node.PersonPointer)));
                }
                else if (!personIds.Add(node.PersonId.Value))
                {
                    issues.Add(Block(
                        "CITY_RESIDENT_NODE_PERSON_DUPLICATE",
                        ReadEntityKind.City,
                        list.CityId,
                        string.Format("Person {0} appears more than once.", node.PersonId.Value)));
                }
            }
        }

        private static GameSnapshot BuildCoreSnapshot(
            int playerForceId,
            ForceReadRecord[] forces,
            CityReadRecord[] cities,
            PersonReadRecord[] persons,
            San9Pk101ReadReport report)
        {
            Dictionary<int, PersonReadRecord> personById = persons.ToDictionary(person => person.Id);
            List<FacilitySnapshot> facilities = new List<FacilitySnapshot>();
            foreach (CityReadRecord city in cities.OrderBy(item => item.Id))
            {
                List<OfficerSnapshot> officers = city.ResidentOfficerIds
                    .Select(id => personById[id])
                    .Select(person => new OfficerSnapshot(
                        person.Id,
                        person.Name,
                        false,
                        person.EffectiveLeadership,
                        person.EffectiveMight,
                        person.EffectiveIntelligence,
                        person.EffectivePolitics))
                    .ToList();
                facilities.Add(new FacilitySnapshot(
                    city.Id,
                    city.Name,
                    FacilityType.City,
                    city.OwnerForceId ?? -1,
                    city.CorpsId ?? -1,
                    city.IsDirectlyControlled,
                    officers,
                    new NativeCommandSnapshot[0]));
            }

            List<CorpsMoneySnapshot> corpsMoney = cities
                .Where(city => city.CorpsId.HasValue)
                .Select(city => city.CorpsId.Value)
                .Distinct()
                .OrderBy(id => id)
                .Select(id => new CorpsMoneySnapshot(id, forces[id].Money))
                .ToList();
            GameSnapshotContext context = new GameSnapshotContext(
                report.ProcessId,
                report.ProcessCreationTimeUtc.HasValue
                    ? (long?)report.ProcessCreationTimeUtc.Value.UtcDateTime.Ticks
                    : null,
                playerForceId,
                null,
                null,
                null,
                null,
                SnapshotReadiness.StructureOnly,
                "V1-read structure only: CanAct, native command state, scenario, turn, and phase are unverified. "
                    + "Stable structure SHA-256=" + (report.StableSummarySha256 ?? "unavailable") + ".");
            return new GameSnapshot(playerForceId, facilities, corpsMoney, context);
        }

        private static void ApplyInitialIdentity(
            San9Pk101ReadReport report,
            ProcessIdentitySnapshot identity,
            List<ReadInvariantDiagnostic> issues)
        {
            if (identity == null)
            {
                issues.Add(Block(
                    "PROCESS_IDENTITY_INITIAL_MISSING",
                    ReadEntityKind.Snapshot,
                    null,
                    "The read-only connection did not provide an initial process identity."));
                return;
            }

            report.ProcessId = identity.ProcessId;
            report.ProcessCreationFileTimeUtc = identity.CreationFileTimeUtc;
            try
            {
                report.ProcessCreationTimeUtc = new DateTimeOffset(
                    DateTime.FromFileTimeUtc(identity.CreationFileTimeUtc));
            }
            catch (ArgumentOutOfRangeException)
            {
                issues.Add(Block(
                    "PROCESS_CREATION_TIME_INVALID",
                    ReadEntityKind.Snapshot,
                    null,
                    "The process creation FILETIME is outside the supported DateTime range."));
            }

            report.MainModuleBaseAddress = identity.MainModuleBaseAddress;
            report.MainModuleSize = identity.MainModuleSize;
            if (identity.MainModuleBaseAddress != San9Pk101Target.ExpectedImageBase
                || identity.MainModuleSize != San9Pk101Target.ExpectedSizeOfImage)
            {
                issues.Add(Block(
                    "MAIN_MODULE_LAYOUT_MISMATCH",
                    ReadEntityKind.Snapshot,
                    null,
                    string.Format(
                        "Expected base/size 0x{0:X8}/0x{1:X8}, got 0x{2:X8}/0x{3:X8}.",
                        San9Pk101Target.ExpectedImageBase,
                        San9Pk101Target.ExpectedSizeOfImage,
                        identity.MainModuleBaseAddress,
                        identity.MainModuleSize)));
            }
            ulong moduleEnd = unchecked((ulong)identity.MainModuleBaseAddress) + identity.MainModuleSize;
            ulong tableEnd = unchecked((ulong)San9Pk101MemoryLayout.PersonBase)
                + unchecked((ulong)(San9Pk101MemoryLayout.PersonStride * San9Pk101MemoryLayout.PersonCount));
            if (San9Pk101MemoryLayout.CityBase < identity.MainModuleBaseAddress || tableEnd > moduleEnd)
            {
                issues.Add(Block(
                    "TABLES_OUTSIDE_MAIN_MODULE",
                    ReadEntityKind.Snapshot,
                    null,
                    string.Format(
                        "Table region 0x{0:X8}..0x{1:X8} is outside module 0x{2:X8}..0x{3:X8}.",
                        San9Pk101MemoryLayout.CityBase,
                        tableEnd,
                        identity.MainModuleBaseAddress,
                        moduleEnd)));
            }
        }

        private static void RevalidateIdentityAfterRead(
            IReadOnlyProcessMemory connection,
            ProcessIdentitySnapshot initialIdentity,
            San9Pk101ReadReport report,
            List<ReadInvariantDiagnostic> issues)
        {
            ProcessIdentitySnapshot after;
            string error;
            if (!connection.TryCaptureIdentity(out after, out error))
            {
                issues.Add(Block(
                    "PROCESS_IDENTITY_POSTCHECK_FAILED",
                    ReadEntityKind.Snapshot,
                    null,
                    error ?? "The process identity post-check failed."));
            }
            else if (initialIdentity == null || !initialIdentity.SameGenerationAndImage(after))
            {
                issues.Add(Block(
                    "PROCESS_IDENTITY_CHANGED_DURING_READ",
                    ReadEntityKind.Snapshot,
                    null,
                    "PID generation, image path, module base, or module size changed during the snapshot."));
            }
            else
            {
                report.ProcessIdentityRevalidatedAfterRead = true;
            }

            report.ReadCompletedUtc = DateTimeOffset.UtcNow;
        }

        private static string ComputeStableSummarySha256(StableImage image)
        {
            using (SHA256 algorithm = SHA256.Create())
            using (MemoryStream stream = new MemoryStream())
            {
                stream.Write(image.Tables.Raw, 0, image.Tables.Raw.Length);
                stream.Write(image.External.CanonicalBytes, 0, image.External.CanonicalBytes.Length);
                stream.Position = 0;
                return BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", string.Empty);
            }
        }

        private static void ValidateAbility(
            int personId,
            string abilityName,
            uint value,
            List<ReadInvariantDiagnostic> issues)
        {
            if (value > San9Pk101MemoryLayout.MaximumObservedEffectiveAbility)
            {
                issues.Add(Block(
                    "PERSON_EFFECTIVE_ABILITY_OUT_OF_RANGE",
                    ReadEntityKind.Person,
                    personId,
                    string.Format("Effective {0} value {1} exceeds 255.", abilityName, value)));
            }
        }

        private static bool IsPlausibleReadRange(uint address, int count)
        {
            if (address < 0x10000 || count <= 0)
            {
                return false;
            }

            ulong end = unchecked((ulong)address) + unchecked((uint)count);
            return address <= San9Pk101MemoryLayout.MaximumUserAddress32
                && end <= unchecked((ulong)San9Pk101MemoryLayout.MaximumUserAddress32) + 1UL;
        }

        private static byte[] Slice(byte[] source, int offset, int count)
        {
            byte[] result = new byte[count];
            Buffer.BlockCopy(source, offset, result, 0, count);
            return result;
        }

        private static ushort ReadUInt16(byte[] source, int offset)
        {
            return BitConverter.ToUInt16(source, offset);
        }

        private static uint ReadUInt32(byte[] source, int offset)
        {
            return BitConverter.ToUInt32(source, offset);
        }

        private static uint AddressFor(uint baseAddress, int stride, int id)
        {
            return checked(baseAddress + unchecked((uint)(stride * id)));
        }

        private static bool TryTableIndex(
            uint pointer,
            uint baseAddress,
            int stride,
            int count,
            out int index)
        {
            index = -1;
            if (pointer < baseAddress)
            {
                return false;
            }

            uint delta = pointer - baseAddress;
            if (delta % unchecked((uint)stride) != 0)
            {
                return false;
            }

            uint candidate = delta / unchecked((uint)stride);
            if (candidate >= unchecked((uint)count))
            {
                return false;
            }

            index = unchecked((int)candidate);
            return true;
        }

        private static int SafeInt(uint value)
        {
            return value <= int.MaxValue ? unchecked((int)value) : 0;
        }

        private static ReadInvariantDiagnostic Warn(
            string code,
            ReadEntityKind entityKind,
            int? entityId,
            string message)
        {
            return new ReadInvariantDiagnostic(
                code,
                DiagnosticSeverity.Warning,
                entityKind,
                entityId,
                message);
        }

        private static ReadInvariantDiagnostic Block(
            string code,
            ReadEntityKind entityKind,
            int? entityId,
            string message)
        {
            return new ReadInvariantDiagnostic(
                code,
                DiagnosticSeverity.Blocking,
                entityKind,
                entityId,
                message);
        }
    }
}
