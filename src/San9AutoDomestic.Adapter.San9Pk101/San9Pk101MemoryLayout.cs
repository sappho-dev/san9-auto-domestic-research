namespace San9AutoDomestic.Adapter.San9Pk101
{
    internal static class San9Pk101MemoryLayout
    {
        internal const uint RawCurrentCityPointerAddress = 0x01232474;
        internal const uint RawContextValue2480Address = 0x01232480;
        internal const uint RawPhaseCandidateAddress = 0x01232484;
        internal const uint RawStrategicTimeCandidateAddress = 0x0123269c;
        internal const uint ForceBase = 0x01253c38;
        internal const int ForceStride = 0x00d4;
        internal const int ForceCount = 50;
        internal const int ForceMoneyOffset = 0x14;
        internal const int ForceFlagsOffset = 0x34;
        internal const int ForceMainForcePointerOffset = 0xb8;
        internal const int ForceLeaderPointerOffset = 0xbc;
        internal const uint ForcePlayerControlFlag = 0x00000001;
        internal const uint ForceBarbarianFlag = 0x00000004;

        internal const uint CityBase = 0x0124db58;
        internal const int CityStride = 0x01f0;
        internal const int CityCount = 50;
        internal const int CityTypeOffset = 0x06;
        internal const uint ExpectedCityVtable = 0x00605938;
        internal const uint ExpectedEmbeddedCityVtable = 0x00606490;
        internal const byte CityTypeValue = 5;
        internal const int CityNameOffset = 0x20;
        internal const int CityNameLength = 16;
        internal const int CityRawCandidateFlags7aOffset = 0x7a;
        internal const int CityEmbeddedFlagsOffset = 0x78;
        internal const int CityStateCodeOffset = 0x7c;
        internal const int CityTroopsOffset = 0x88;
        internal const int CityMoraleOffset = 0x90;
        internal const int CityRepairCurrentOffset = 0x3c;
        internal const int CityOrderFlags3eOffset = 0x3e;
        internal const int CitySelfPointerOffset = 0xbc;
        internal const int CityCorpsPointerOffset = 0xcc;
        internal const int CityResidentListOffset = 0xdc;
        internal const int CityResidentListFirstPointerOffset = 0xe0;
        internal const int CityResidentListLastPointerOffset = 0xe4;
        internal const int CityValidResidentOfficerCountOffset = 0xe8;
        internal const int CityResidentUnitPointerOffset = 0x58;
        internal const int CityPatrolCurrentOffset = 0x1c4;
        internal const int CityRepairMaximumOffset = 0x1c8;
        internal const int CityCommerceCurrentOffset = 0x1cc;
        internal const int CityCultivateCurrentOffset = 0x1d0;
        internal const int CityCommerceMaximumOffset = 0x1d4;
        internal const int CityCultivateMaximumOffset = 0x1d8;
        internal const int CityOrderFlags1e0Offset = 0x1e0;

        internal const uint PersonBase = 0x01258ee0;
        internal const int PersonStride = 0x0128;
        internal const int PersonCount = 850;
        internal const int PersonIdOffset = 0x04;
        internal const int PersonSurnameOffset = 0x10;
        internal const int PersonSurnameLength = 5;
        internal const int PersonGivenNameOffset = 0x15;
        internal const int PersonGivenNameLength = 5;
        internal const int PersonEffectiveMightOffset = 0x50;
        internal const int PersonEffectiveIntelligenceOffset = 0x58;
        internal const int PersonEffectivePoliticsOffset = 0x60;
        internal const int PersonEffectiveLeadershipOffset = 0x68;
        internal const int PersonIdentityOffset = 0x84;
        internal const int PersonResidencePointerOffset = 0xf4;
        internal const int PersonReadyFlagsOffset = 0xe8;
        internal const uint PersonBusyBit = 0x00001000;

        internal const int ContainerEmbeddedFlagsOffset = 0x20;
        internal const uint ContainerEmbeddedFlag = 0x00000001;
        internal const int ContainerOwnerCityPointerOffset = 0x64;

        internal const int ResidentNodeNextPointerOffset = 0x00;
        internal const int ResidentNodePreviousPointerOffset = 0x04;
        internal const int ResidentNodePersonPointerOffset = 0x08;
        internal const int ResidentNodeSize = 0x0c;

        internal const uint MaximumUserAddress32 = 0x7fffffff;

        internal const uint MinimumValidResidentIdentity = 0;
        internal const uint MaximumValidResidentIdentity = 3;
        internal const uint MaximumObservedEffectiveAbility = 255;
    }
}
