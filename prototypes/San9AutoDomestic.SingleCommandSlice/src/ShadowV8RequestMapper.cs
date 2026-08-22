using System;
using System.Collections.Generic;
using San9AutoDomestic.Core.Configuration;
using San9AutoDomestic.V8Transaction;

namespace San9AutoDomestic.SingleCommandSlice
{
    /// <summary>
    /// Total, explicit mapping between the five Core commands and the frozen V8
    /// request registry.  Mapping produces data only and never submits it.
    /// </summary>
    public static class ShadowV8RequestMapper
    {
        public static RegisteredDomesticCommand MapCommand(DomesticCommand command)
        {
            switch (command)
            {
                case DomesticCommand.Patrol:
                    return RegisteredDomesticCommand.Patrol;
                case DomesticCommand.Commerce:
                    return RegisteredDomesticCommand.Commerce;
                case DomesticCommand.Cultivate:
                    return RegisteredDomesticCommand.Cultivate;
                case DomesticCommand.Train:
                    return RegisteredDomesticCommand.Train;
                case DomesticCommand.Repair:
                    return RegisteredDomesticCommand.Repair;
                default:
                    throw new ArgumentOutOfRangeException(
                        "command",
                        "Only the five frozen domestic commands can be mapped.");
            }
        }

        internal static SingleCommandRequest CreateRequest(
            SingleCommandSliceTicket ticket,
            FrozenTaskPlan task,
            int corpsId,
            IEnumerable<int> officerIds,
            FixedDigest contextDigest,
            FixedDigest generationDigest,
            int timeToLiveMilliseconds)
        {
            if (ticket == null)
            {
                throw new ArgumentNullException("ticket");
            }

            if (task == null)
            {
                throw new ArgumentNullException("task");
            }

            return new SingleCommandRequest(
                ticket.TicketId,
                ticket.Sequence,
                MapCommand(task.Command),
                ticket.CityId,
                corpsId,
                officerIds,
                task.ReserveMoney,
                contextDigest,
                generationDigest,
                timeToLiveMilliseconds);
        }
    }
}
