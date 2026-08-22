using System;
using San9AutoDomestic.Core.Domain;

namespace San9AutoDomestic.Adapter.San9Pk101
{
    internal enum NativeMoneyEvidenceKind
    {
        ObservedPerOfficerThreshold = 1,
        NotObserved = 2
    }

    internal sealed class San9Pk101CommandDescriptor
    {
        private San9Pk101CommandDescriptor(
            DomesticCommandKind command,
            int provisionalCostPerOfficer,
            NativeMoneyEvidenceKind nativeMoneyEvidence,
            int effectiveAbilityOffset,
            int orderFlagsOffset,
            byte orderMask)
        {
            Command = command;
            ProvisionalCostPerOfficer = provisionalCostPerOfficer;
            NativeMoneyEvidence = nativeMoneyEvidence;
            EffectiveAbilityOffset = effectiveAbilityOffset;
            OrderFlagsOffset = orderFlagsOffset;
            OrderMask = orderMask;
        }

        internal DomesticCommandKind Command { get; private set; }

        internal int ProvisionalCostPerOfficer { get; private set; }

        internal NativeMoneyEvidenceKind NativeMoneyEvidence { get; private set; }

        internal int EffectiveAbilityOffset { get; private set; }

        internal int OrderFlagsOffset { get; private set; }

        internal byte OrderMask { get; private set; }

        internal static San9Pk101CommandDescriptor Get(DomesticCommandKind command)
        {
            switch (command)
            {
                case DomesticCommandKind.Patrol:
                    return new San9Pk101CommandDescriptor(
                        command,
                        GameRules.DefaultDomesticCostPerOfficer,
                        NativeMoneyEvidenceKind.ObservedPerOfficerThreshold,
                        San9Pk101MemoryLayout.PersonEffectiveIntelligenceOffset,
                        San9Pk101MemoryLayout.CityOrderFlags1e0Offset,
                        1 << 3);
                case DomesticCommandKind.Commerce:
                    return new San9Pk101CommandDescriptor(
                        command,
                        GameRules.DefaultDomesticCostPerOfficer,
                        NativeMoneyEvidenceKind.ObservedPerOfficerThreshold,
                        San9Pk101MemoryLayout.PersonEffectivePoliticsOffset,
                        San9Pk101MemoryLayout.CityOrderFlags1e0Offset,
                        1 << 4);
                case DomesticCommandKind.Cultivate:
                    return new San9Pk101CommandDescriptor(
                        command,
                        GameRules.DefaultDomesticCostPerOfficer,
                        NativeMoneyEvidenceKind.ObservedPerOfficerThreshold,
                        San9Pk101MemoryLayout.PersonEffectivePoliticsOffset,
                        San9Pk101MemoryLayout.CityOrderFlags1e0Offset,
                        1 << 5);
                case DomesticCommandKind.Repair:
                    return new San9Pk101CommandDescriptor(
                        command,
                        GameRules.DefaultDomesticCostPerOfficer,
                        NativeMoneyEvidenceKind.ObservedPerOfficerThreshold,
                        San9Pk101MemoryLayout.PersonEffectiveLeadershipOffset,
                        San9Pk101MemoryLayout.CityOrderFlags3eOffset,
                        1);
                case DomesticCommandKind.Train:
                    return new San9Pk101CommandDescriptor(
                        command,
                        0,
                        NativeMoneyEvidenceKind.NotObserved,
                        San9Pk101MemoryLayout.PersonEffectiveMightOffset,
                        San9Pk101MemoryLayout.CityOrderFlags3eOffset,
                        2);
                default:
                    throw new ArgumentOutOfRangeException(
                        "command",
                        "No target-locked descriptor exists for this domestic command.");
            }
        }
    }
}
