using System;
using San9AutoDomestic.Core.Domain;

namespace San9AutoDomestic.Adapter.San9Pk101
{
    public enum ReadEntityKind
    {
        Snapshot,
        Force,
        City,
        Person
    }

    public sealed class ReadInvariantDiagnostic
    {
        internal ReadInvariantDiagnostic(
            string code,
            DiagnosticSeverity severity,
            ReadEntityKind entityKind,
            int? entityId,
            string message)
        {
            Code = code ?? string.Empty;
            Severity = severity;
            EntityKind = entityKind;
            EntityId = entityId;
            Message = message ?? string.Empty;
        }

        public string Code { get; private set; }
        public DiagnosticSeverity Severity { get; private set; }
        public ReadEntityKind EntityKind { get; private set; }
        public int? EntityId { get; private set; }
        public string Message { get; private set; }
    }

    public sealed class ForceReadRecord
    {
        internal ForceReadRecord()
        {
        }

        public int Id { get; internal set; }
        public uint Address { get; internal set; }
        public int Money { get; internal set; }
        public uint RawFlags { get; internal set; }
        public bool IsPlayerControlled { get; internal set; }
        public bool IsBarbarian { get; internal set; }
        public bool IsValidCorps { get; internal set; }
        public bool IsMainForce { get; internal set; }
        public uint MainForcePointer { get; internal set; }
        public int? MainForceId { get; internal set; }
        public uint LeaderPersonPointer { get; internal set; }
        public int? LeaderPersonId { get; internal set; }
        public bool IsReferencedByCity { get; internal set; }
    }

    public sealed class CityReadRecord
    {
        internal CityReadRecord()
        {
            ResidentOfficerIds = new int[0];
            ResidentOfficerIdsInCityListOrder = new int[0];
        }

        public int Id { get; internal set; }
        public uint Address { get; internal set; }
        public string Name { get; internal set; }
        public string RawNameHex { get; internal set; }
        public bool? IsInCombat { get; internal set; }
        public byte RawCandidateFlags7A { get; internal set; }
        public uint SelfPointer { get; internal set; }
        public uint CorpsPointer { get; internal set; }
        public int? CorpsId { get; internal set; }
        public int? OwnerForceId { get; internal set; }
        public bool IsDirectlyControlled { get; internal set; }
        public int DeclaredValidResidentOfficerCount { get; internal set; }
        public int ObservedValidResidentOfficerCount { get; internal set; }
        public int ObservedExcludedResidentPersonCount { get; internal set; }
        public int[] ResidentOfficerIds { get; internal set; }
        public int[] ResidentOfficerIdsInCityListOrder { get; internal set; }
    }

    public sealed class PersonReadRecord
    {
        internal PersonReadRecord()
        {
        }

        public int Id { get; internal set; }
        public uint Address { get; internal set; }
        public string RawSurnameHex { get; internal set; }
        public string RawGivenNameHex { get; internal set; }
        public string RawDecodedSurname { get; internal set; }
        public string RawDecodedGivenName { get; internal set; }
        public string Surname { get; internal set; }
        public string GivenName { get; internal set; }
        public string Name { get; internal set; }
        public bool SurnameHexPrefixStripped { get; internal set; }
        public uint RawIdentity { get; internal set; }
        public bool IsValidResidentOfficerIdentity { get; internal set; }
        public uint ResidencePointer { get; internal set; }
        public int? ResidentCityId { get; internal set; }
        public ushort RecordId { get; internal set; }
        public uint ResidenceContainerFlags { get; internal set; }
        public uint ResidenceOwnerCityPointer { get; internal set; }
        public int EffectiveMight { get; internal set; }
        public int EffectiveIntelligence { get; internal set; }
        public int EffectivePolitics { get; internal set; }
        public int EffectiveLeadership { get; internal set; }
        public bool? CanAct { get; internal set; }
        public bool IsIncludedInCoreSnapshot { get; internal set; }
    }

    public sealed class San9Pk101ReadReport
    {
        internal San9Pk101ReadReport()
        {
            Forces = new ForceReadRecord[0];
            Cities = new CityReadRecord[0];
            Persons = new PersonReadRecord[0];
            Issues = new ReadInvariantDiagnostic[0];
        }

        public San9Pk101DiagnosticReport Baseline { get; internal set; }
        public bool ReadSucceeded { get; internal set; }
        public bool RelevantFieldsWereStable { get; internal set; }
        public int StableReadAttempts { get; internal set; }
        public int? PlayerForceId { get; internal set; }
        public ForceReadRecord[] Forces { get; internal set; }
        public CityReadRecord[] Cities { get; internal set; }
        public PersonReadRecord[] Persons { get; internal set; }
        public GameSnapshot Snapshot { get; internal set; }
        public bool CoreCanActValuesAreConservativeFalse { get; internal set; }
        public bool CoreCommandListsAreEmpty { get; internal set; }
        public bool ConflictsIgnoredForReadOnlyScan { get; internal set; }
        public int? ProcessId { get; internal set; }
        public long? ProcessCreationFileTimeUtc { get; internal set; }
        public DateTimeOffset? ProcessCreationTimeUtc { get; internal set; }
        public uint? MainModuleBaseAddress { get; internal set; }
        public uint? MainModuleSize { get; internal set; }
        public bool ProcessIdentityRevalidatedAfterRead { get; internal set; }
        public DateTimeOffset? ReadCompletedUtc { get; internal set; }
        public string StableSummarySha256 { get; internal set; }
        public ReadInvariantDiagnostic[] Issues { get; internal set; }
    }
}
