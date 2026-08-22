using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace San9AutoDomestic.Adapter.San9Pk101
{
    internal sealed class San9Pk101AvailabilityReader
    {
        internal San9Pk101AvailabilityReport Read(
            IReadOnlyProcessMemory memory,
            San9Pk101DiagnosticReport baseline,
            string executablePath)
        {
            if (memory == null)
            {
                throw new ArgumentNullException("memory");
            }
            San9Pk101AvailabilityReport report = new San9Pk101AvailabilityReport
            {
                Baseline = baseline
            };
            List<AvailabilityIssue> issues = new List<AvailabilityIssue>();
            ProcessIdentitySnapshot initial = memory.InitialIdentity;

            if (baseline == null
                || baseline.FileValidation == null
                || !baseline.FileValidation.IsValid
                || baseline.ProcessDiscovery == null
                || baseline.ProcessDiscovery.Status != ProcessDiscoveryStatus.Unique
                || baseline.ReadOnlyConnection == null
                || !baseline.ReadOnlyConnection.Connected
                || baseline.ConflictScan == null
                || !baseline.ConflictScan.ProcessScanSucceeded
                || !baseline.ConflictScan.ModuleScanAttempted
                || !baseline.ConflictScan.ModuleScanSucceeded)
            {
                issues.Add(Block("V2_BASELINE_NOT_VALIDATED",
                    "V2 requires the exact validated file, a unique process, and the established read-only connection."));
            }

            report.StructureBefore = new San9Pk101SnapshotReader().Read(memory, baseline);
            if (!report.StructureBefore.ReadSucceeded)
            {
                issues.Add(Block("V1_STRUCTURE_BEFORE_BLOCKED",
                    "The strict V1 structure snapshot did not pass before V2 observation."));
            }

            byte[] stableRaw = ReadStableRawContext(memory);
            if (stableRaw == null)
            {
                issues.Add(Block("V2_RAW_CONTEXT_UNSTABLE",
                    "The global/city/corps/person region changed across every A/B/A attempt."));
            }
            else
            {
                report.RawFieldsWereStable = true;
                report.ContextToken = BuildContextToken(stableRaw, report.StructureBefore);
                if (!report.ContextToken.IsStrategicInputPhaseCandidate)
                {
                    issues.Add(Block("V2_PHASE_NOT_STRATEGIC_INPUT_CANDIDATE",
                        string.Format("Raw phase candidate is {0}; only raw value 1 is accepted fail-closed.",
                            report.ContextToken.PhaseCandidate)));
                }
                else
                {
                    issues.Add(Warn("V2_PHASE_SEMANTICS_UNVERIFIED",
                        "Raw global 0x01232484 equals 1, but its business meaning is still a candidate."));
                }

                try
                {
                    report.Cities = BuildCities(stableRaw, report.StructureBefore);
                }
                catch (Exception exception)
                {
                    issues.Add(Block("V2_RAW_PARSE_FAILED",
                        exception.GetType().Name + ": " + exception.Message));
                }
            }

            try
            {
                report.CodeAnchors = San9Pk101CodeAnchorValidator.Observe(memory, executablePath);
                report.CodeAnchorsWereStableAndExact = report.CodeAnchors.Length > 0
                    && report.CodeAnchors.All(anchor =>
                        string.IsNullOrEmpty(anchor.Error)
                        && anchor.DiskMatchesExpected
                        && anchor.LiveWasStable
                        && anchor.LiveMatchesDisk);
                if (!report.CodeAnchorsWereStableAndExact)
                {
                    foreach (CodeAnchorObservation anchor in report.CodeAnchors.Where(anchor =>
                        !string.IsNullOrEmpty(anchor.Error)
                        || !anchor.DiskMatchesExpected
                        || !anchor.LiveWasStable
                        || !anchor.LiveMatchesDisk))
                    {
                        issues.Add(Block("V2_CODE_ANCHOR_MISMATCH",
                            string.Format("{0} at 0x{1:X8}: diskExpected={2}, liveStable={3}, liveDisk={4}, error={5}",
                                anchor.Name,
                                anchor.Address,
                                anchor.DiskMatchesExpected,
                                anchor.LiveWasStable,
                                anchor.LiveMatchesDisk,
                                anchor.Error ?? "none")));
                    }
                }
            }
            catch (Exception exception)
            {
                issues.Add(Block("V2_CODE_ANCHOR_CHECK_FAILED",
                    exception.GetType().Name + ": " + exception.Message));
            }

            if (baseline != null
                && baseline.ConflictScan != null
                && baseline.ConflictScan.HasBlockingConflicts)
            {
                issues.Add(Block("V2_KNOWN_MODIFIER_CONFLICT",
                    "A known process/module conflict is present; observation cannot become actionable."));
            }

            report.StructureAfter = new San9Pk101SnapshotReader().Read(memory, baseline);
            report.StructureWasRevalidated = report.StructureBefore.ReadSucceeded
                && report.StructureAfter.ReadSucceeded
                && !string.IsNullOrEmpty(report.StructureBefore.StableSummarySha256)
                && string.Equals(
                    report.StructureBefore.StableSummarySha256,
                    report.StructureAfter.StableSummarySha256,
                    StringComparison.Ordinal);
            if (!report.StructureWasRevalidated)
            {
                issues.Add(Block("V1_STRUCTURE_CHANGED_ACROSS_V2",
                    "The strict V1 structure/stable table digest changed across V2 observation."));
            }

            ProcessIdentitySnapshot after;
            string identityError = null;
            if (initial != null
                && memory.TryCaptureIdentity(out after, out identityError)
                && initial.SameGenerationAndImage(after))
            {
                report.ProcessIdentityRevalidatedAfterObservation = true;
            }
            else
            {
                issues.Add(Block("V2_PROCESS_IDENTITY_CHANGED",
                    identityError ?? "The process generation/image identity changed during V2 observation."));
            }

            issues.Add(Warn("V2_NO_EXECUTION_CAPABILITY",
                "This report is an unverified read-only preview and never provides PlanningReady or native execution capability."));

            report.DataReadSucceeded = report.RawFieldsWereStable
                && report.StructureWasRevalidated
                && report.ProcessIdentityRevalidatedAfterObservation
                && report.Cities != null;
            report.ObservationBlocked = issues.Any(issue => issue.Severity == DiagnosticSeverity.Blocking);

            report.ReadCompletedUtc = DateTimeOffset.UtcNow;
            report.Issues = issues.ToArray();
            return report;
        }

        private static byte[] ReadStableRawContext(IReadOnlyProcessMemory memory)
        {
            uint end = checked(San9Pk101MemoryLayout.PersonBase
                + unchecked((uint)(San9Pk101MemoryLayout.PersonStride * San9Pk101MemoryLayout.PersonCount)));
            int length = checked((int)(end - San9Pk101MemoryLayout.RawCurrentCityPointerAddress));
            for (int attempt = 0; attempt < 3; attempt++)
            {
                byte[] a = memory.ReadBytes(San9Pk101MemoryLayout.RawCurrentCityPointerAddress, length);
                byte[] b = memory.ReadBytes(San9Pk101MemoryLayout.RawCurrentCityPointerAddress, length);
                byte[] aAgain = memory.ReadBytes(San9Pk101MemoryLayout.RawCurrentCityPointerAddress, length);
                if (a.SequenceEqual(b) && a.SequenceEqual(aAgain))
                {
                    return aAgain;
                }
            }

            return null;
        }

        private static RawContextToken BuildContextToken(byte[] raw, San9Pk101ReadReport structure)
        {
            RawContextToken token = new RawContextToken
            {
                CurrentCityPointerCandidate = ReadUInt32(raw, San9Pk101MemoryLayout.RawCurrentCityPointerAddress),
                RawValue2480 = ReadUInt32(raw, San9Pk101MemoryLayout.RawContextValue2480Address),
                PhaseCandidate = ReadUInt32(raw, San9Pk101MemoryLayout.RawPhaseCandidateAddress),
                StrategicTimeCounterCandidate = ReadUInt32(raw, San9Pk101MemoryLayout.RawStrategicTimeCandidateAddress),
                StableRawSha256 = San9Pk101CodeAnchorValidator.Sha256(raw)
            };
            token.IsStrategicInputPhaseCandidate = token.PhaseCandidate == 1;

            StringBuilder canonical = new StringBuilder();
            canonical.Append(token.CurrentCityPointerCandidate).Append('|')
                .Append(token.RawValue2480).Append('|')
                .Append(token.PhaseCandidate).Append('|')
                .Append(token.StrategicTimeCounterCandidate).Append('|')
                .Append(token.StableRawSha256).Append('|');
            if (structure != null)
            {
                foreach (CityReadRecord city in structure.Cities.Where(city => city.IsDirectlyControlled))
                {
                    uint flags = city.CorpsId.HasValue
                        ? ReadUInt32(raw, checked(San9Pk101MemoryLayout.ForceBase
                            + unchecked((uint)(city.CorpsId.Value * San9Pk101MemoryLayout.ForceStride))
                            + San9Pk101MemoryLayout.ForceFlagsOffset))
                        : 0;
                    canonical.Append(city.Id).Append(':').Append(city.CorpsPointer).Append(':')
                        .Append(flags).Append(';');
                }
            }

            token.TokenSha256 = Sha256Text(canonical.ToString());
            return token;
        }

        private static CityAvailabilityObservation[] BuildCities(
            byte[] raw,
            San9Pk101ReadReport structure)
        {
            if (structure == null || !structure.ReadSucceeded)
            {
                return new CityAvailabilityObservation[0];
            }

            Dictionary<int, PersonReadRecord> persons = structure.Persons.ToDictionary(person => person.Id);
            Dictionary<int, ForceReadRecord> forces = structure.Forces.ToDictionary(force => force.Id);
            List<CityAvailabilityObservation> cities = new List<CityAvailabilityObservation>();
            foreach (CityReadRecord city in structure.Cities.Where(city => city.IsDirectlyControlled))
            {
                ForceReadRecord corps = city.CorpsId.HasValue && forces.ContainsKey(city.CorpsId.Value)
                    ? forces[city.CorpsId.Value]
                    : null;
                CityAvailabilityObservation observation = new CityAvailabilityObservation
                {
                    CityId = city.Id,
                    CityName = city.Name,
                    CorpsId = city.CorpsId,
                    CorpsMoney = corps == null ? 0 : corps.Money,
                    CorpsFlags = corps == null ? 0 : corps.RawFlags,
                    IsDirectlyControlled = city.IsDirectlyControlled
                };
                List<OfficerRankingPreview> ready = new List<OfficerRankingPreview>();
                for (int index = 0; index < city.ResidentOfficerIdsInCityListOrder.Length; index++)
                {
                    int personId = city.ResidentOfficerIdsInCityListOrder[index];
                    uint personAddress = checked(San9Pk101MemoryLayout.PersonBase
                        + unchecked((uint)(personId * San9Pk101MemoryLayout.PersonStride)));
                    uint flags = ReadUInt32(raw, personAddress + San9Pk101MemoryLayout.PersonReadyFlagsOffset);
                    if ((flags & San9Pk101MemoryLayout.PersonBusyBit) == 0)
                    {
                        PersonReadRecord person = persons[personId];
                        ready.Add(new OfficerRankingPreview
                        {
                            PersonId = personId,
                            Name = person.Name,
                            SourceListIndex = index
                        });
                    }
                }

                observation.Commands = new[]
                {
                    BuildCommand(raw, city, corps, ready, DomesticCommandKind.Patrol),
                    BuildCommand(raw, city, corps, ready, DomesticCommandKind.Commerce),
                    BuildCommand(raw, city, corps, ready, DomesticCommandKind.Cultivate),
                    BuildCommand(raw, city, corps, ready, DomesticCommandKind.Repair),
                    BuildCommand(raw, city, corps, ready, DomesticCommandKind.Train)
                };
                cities.Add(observation);
            }

            return cities.ToArray();
        }

        private static CommandAvailabilityObservation BuildCommand(
            byte[] raw,
            CityReadRecord city,
            ForceReadRecord corps,
            List<OfficerRankingPreview> ready,
            DomesticCommandKind command)
        {
            uint cityAddress = city.Address;
            OfficerRankingPreview[] ranked = ready
                .Select(candidate => new OfficerRankingPreview
                {
                    PersonId = candidate.PersonId,
                    Name = candidate.Name,
                    SourceListIndex = candidate.SourceListIndex,
                    Score = ReadAbility(raw, candidate.PersonId, command)
                })
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.SourceListIndex)
                .ToArray();
            OfficerRankingPreview[] source = ready
                .Select(candidate => new OfficerRankingPreview
                {
                    PersonId = candidate.PersonId,
                    Name = candidate.Name,
                    SourceListIndex = candidate.SourceListIndex,
                    Score = ReadAbility(raw, candidate.PersonId, command)
                })
                .ToArray();

            bool direct = city.IsDirectlyControlled && corps != null && corps.IsValidCorps;
            bool commonCorpsGate = corps != null && (corps.RawFlags & 1) != 0;
            uint state = ReadUInt32(raw, cityAddress + San9Pk101MemoryLayout.CityStateCodeOffset);
            bool statePass = state != 6;
            uint cityVtable = ReadUInt32(raw, cityAddress);
            uint embeddedVtable = ReadUInt32(raw,
                cityAddress + San9Pk101MemoryLayout.CityResidentUnitPointerOffset);
            uint embeddedFlags = ReadUInt32(raw,
                cityAddress + San9Pk101MemoryLayout.CityEmbeddedFlagsOffset);
            bool commonEquivalentKnown = cityVtable == San9Pk101MemoryLayout.ExpectedCityVtable
                && embeddedVtable == San9Pk101MemoryLayout.ExpectedEmbeddedCityVtable;
            bool commonForbidden = (embeddedFlags & 1) != 0
                ? (embeddedFlags & (1u << 22)) != 0
                : (embeddedFlags & ((1u << 2) | (1u << 22))) != 0;
            bool commonPass = commonEquivalentKnown && !commonForbidden;
            bool candidatePass = ready.Count >= 1;
            int money = corps == null ? 0 : corps.Money;
            San9Pk101CommandDescriptor descriptor = San9Pk101CommandDescriptor.Get(command);
            int productCostPerOfficer = descriptor.ProvisionalCostPerOfficer;
            bool nativeMoneyRuleObserved = descriptor.NativeMoneyEvidence
                == NativeMoneyEvidenceKind.ObservedPerOfficerThreshold;
            bool nativeMoneyPass = !nativeMoneyRuleObserved
                || money >= descriptor.ProvisionalCostPerOfficer;
            bool orderPass = IsOrderBitClear(raw, cityAddress, descriptor);
            bool valuePass = IsValueGateOpen(raw, cityAddress, command);

            List<AvailabilityCondition> conditions = new List<AvailabilityCondition>
            {
                Condition("DIRECT_VALID_CORPS", direct, AvailabilityEvidence.ConfirmedRawEquivalent,
                    "City is direct-control and points to a structurally valid corps."),
                Condition("COMMON_HANDLER_CORPS_GATE", commonCorpsGate,
                    AvailabilityEvidence.ConfirmedRawEquivalent,
                    corps == null ? "No valid corps record." : string.Format("corps+0x34=0x{0:X8}", corps.RawFlags)),
                Condition("CITY_STATE_NOT_6", statePass, AvailabilityEvidence.ConfirmedRawEquivalent,
                    "city+0x7C=" + state),
                Condition("CITY_VTABLE_90_RAW_EQUIVALENT", commonEquivalentKnown ? (bool?)commonPass : null,
                    AvailabilityEvidence.ConfirmedRawEquivalent,
                    string.Format("cityVptr=0x{0:X8}, embeddedVptr=0x{1:X8}, flags=0x{2:X8}",
                        cityVtable, embeddedVtable, embeddedFlags)),
                Condition("READY_CANDIDATE_AT_LEAST_1", candidatePass,
                    AvailabilityEvidence.ConfirmedStaticRule, "ready=" + ready.Count),
                Condition(nativeMoneyRuleObserved ? "CORPS_MONEY_AT_LEAST_NATIVE_COST_CANDIDATE" : "NATIVE_MONEY_GATE_NOT_OBSERVED",
                    nativeMoneyRuleObserved ? (bool?)nativeMoneyPass : null,
                    nativeMoneyRuleObserved ? AvailabilityEvidence.DerivedRawCandidate : AvailabilityEvidence.Unknown,
                    nativeMoneyRuleObserved ? "money=" + money : "This command's CanExecute has no observed native money comparison."),
                Condition("COMMAND_ORDER_BIT_CLEAR", orderPass,
                    AvailabilityEvidence.ConfirmedRawEquivalent, "Static per-command order bit."),
                Condition("COMMAND_VALUE_GATE_OPEN", valuePass,
                    AvailabilityEvidence.ConfirmedRawEquivalent, ValueDetail(raw, cityAddress, command))
            };

            bool staticGate = direct && commonCorpsGate && statePass && commonPass && candidatePass
                && nativeMoneyPass && orderPass && valuePass;
            return new CommandAvailabilityObservation
            {
                Command = command,
                ReadyCandidateCount = ready.Count,
                ReadyCandidatesInSourceOrder = source,
                RankedCandidates = ranked,
                ProvisionalCostPerOfficer = productCostPerOfficer,
                NativeOneOfficerCostCandidate = nativeMoneyRuleObserved
                    ? (int?)descriptor.ProvisionalCostPerOfficer
                    : null,
                KnownStaticSubsetWouldPass = staticGate,
                Conditions = conditions.ToArray()
            };
        }

        private static bool IsOrderBitClear(
            byte[] raw,
            uint city,
            San9Pk101CommandDescriptor descriptor)
        {
            return (ReadByte(raw, checked(
                city + unchecked((uint)descriptor.OrderFlagsOffset)))
                & descriptor.OrderMask) == 0;
        }

        private static bool IsValueGateOpen(byte[] raw, uint city, DomesticCommandKind command)
        {
            switch (command)
            {
                case DomesticCommandKind.Patrol:
                    return ReadUInt32(raw, city + San9Pk101MemoryLayout.CityPatrolCurrentOffset) < 1000;
                case DomesticCommandKind.Commerce:
                    return ReadUInt32(raw, city + San9Pk101MemoryLayout.CityCommerceCurrentOffset)
                        < ReadUInt32(raw, city + San9Pk101MemoryLayout.CityCommerceMaximumOffset);
                case DomesticCommandKind.Cultivate:
                    return ReadUInt32(raw, city + San9Pk101MemoryLayout.CityCultivateCurrentOffset)
                        < ReadUInt32(raw, city + San9Pk101MemoryLayout.CityCultivateMaximumOffset);
                case DomesticCommandKind.Repair:
                    return ReadUInt16(raw, city + San9Pk101MemoryLayout.CityRepairCurrentOffset)
                        < ReadUInt32(raw, city + San9Pk101MemoryLayout.CityRepairMaximumOffset);
                case DomesticCommandKind.Train:
                    return ReadUInt32(raw, city + San9Pk101MemoryLayout.CityTroopsOffset) > 0
                        && ReadUInt32(raw, city + San9Pk101MemoryLayout.CityMoraleOffset) < 100;
                default:
                    throw new ArgumentOutOfRangeException("command", "No explicit value-gate mapping exists for this command.");
            }
        }

        private static string ValueDetail(byte[] raw, uint city, DomesticCommandKind command)
        {
            switch (command)
            {
                case DomesticCommandKind.Patrol:
                    return "patrol=" + ReadUInt32(raw, city + San9Pk101MemoryLayout.CityPatrolCurrentOffset) + "/1000";
                case DomesticCommandKind.Commerce:
                    return string.Format("commerce={0}/{1}",
                        ReadUInt32(raw, city + San9Pk101MemoryLayout.CityCommerceCurrentOffset),
                        ReadUInt32(raw, city + San9Pk101MemoryLayout.CityCommerceMaximumOffset));
                case DomesticCommandKind.Cultivate:
                    return string.Format("cultivate={0}/{1}",
                        ReadUInt32(raw, city + San9Pk101MemoryLayout.CityCultivateCurrentOffset),
                        ReadUInt32(raw, city + San9Pk101MemoryLayout.CityCultivateMaximumOffset));
                case DomesticCommandKind.Repair:
                    return string.Format("repair={0}/{1}",
                        ReadUInt16(raw, city + San9Pk101MemoryLayout.CityRepairCurrentOffset),
                        ReadUInt32(raw, city + San9Pk101MemoryLayout.CityRepairMaximumOffset));
                case DomesticCommandKind.Train:
                    return string.Format("troops={0}, morale={1}/100",
                        ReadUInt32(raw, city + San9Pk101MemoryLayout.CityTroopsOffset),
                        ReadUInt32(raw, city + San9Pk101MemoryLayout.CityMoraleOffset));
                default:
                    throw new ArgumentOutOfRangeException("command", "No explicit value-detail mapping exists for this command.");
            }
        }

        private static int ReadAbility(byte[] raw, int personId, DomesticCommandKind command)
        {
            uint person = checked(San9Pk101MemoryLayout.PersonBase
                + unchecked((uint)(personId * San9Pk101MemoryLayout.PersonStride)));
            int offset = San9Pk101CommandDescriptor.Get(command).EffectiveAbilityOffset;
            uint value = ReadUInt32(raw, checked(person + unchecked((uint)offset)));
            return value <= int.MaxValue ? unchecked((int)value) : 0;
        }

        private static AvailabilityCondition Condition(
            string code, bool? passed, AvailabilityEvidence evidence, string detail)
        {
            return new AvailabilityCondition(code, passed, evidence, detail);
        }

        private static byte ReadByte(byte[] raw, uint address)
        {
            return raw[Offset(raw, address, 1)];
        }

        private static ushort ReadUInt16(byte[] raw, uint address)
        {
            return BitConverter.ToUInt16(raw, Offset(raw, address, 2));
        }

        private static uint ReadUInt32(byte[] raw, uint address)
        {
            return BitConverter.ToUInt32(raw, Offset(raw, address, 4));
        }

        private static int Offset(byte[] raw, uint address, int count)
        {
            if (address < San9Pk101MemoryLayout.RawCurrentCityPointerAddress)
            {
                throw new ArgumentOutOfRangeException("address");
            }
            ulong offset = address - San9Pk101MemoryLayout.RawCurrentCityPointerAddress;
            if (offset + unchecked((uint)count) > unchecked((ulong)raw.Length))
            {
                throw new ArgumentOutOfRangeException("address");
            }
            return checked((int)offset);
        }

        private static string Sha256Text(string value)
        {
            using (SHA256 algorithm = SHA256.Create())
            {
                return BitConverter.ToString(algorithm.ComputeHash(Encoding.UTF8.GetBytes(value)))
                    .Replace("-", string.Empty);
            }
        }

        private static AvailabilityIssue Warn(string code, string message)
        {
            return new AvailabilityIssue(code, DiagnosticSeverity.Warning, message);
        }

        private static AvailabilityIssue Block(string code, string message)
        {
            return new AvailabilityIssue(code, DiagnosticSeverity.Blocking, message);
        }
    }
}
