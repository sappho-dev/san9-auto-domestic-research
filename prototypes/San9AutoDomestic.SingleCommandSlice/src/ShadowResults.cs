using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using San9AutoDomestic.Core.Configuration;
using San9AutoDomestic.V8Transaction;

namespace San9AutoDomestic.SingleCommandSlice
{
    /// <summary>
    /// A one-use challenge for exactly the current city/task pair.  Its
    /// constructor and owner binding are internal; even a genuine ticket is
    /// shadow evidence only and cannot authorize a native action.
    /// </summary>
    public sealed class SingleCommandSliceTicket
    {
        internal SingleCommandSliceTicket(
            object ownerToken,
            Guid ticketId,
            Guid runId,
            ulong sequence,
            int cityOrdinal,
            int taskOrdinal,
            int cityId,
            DomesticCommand command)
        {
            OwnerToken = ownerToken;
            TicketId = ticketId;
            RunId = runId;
            Sequence = sequence;
            CityOrdinal = cityOrdinal;
            TaskOrdinal = taskOrdinal;
            CityId = cityId;
            Command = command;
        }

        internal object OwnerToken { get; private set; }

        public Guid TicketId { get; private set; }

        public Guid RunId { get; private set; }

        public ulong Sequence { get; private set; }

        public int CityOrdinal { get; private set; }

        public int TaskOrdinal { get; private set; }

        public int CityId { get; private set; }

        public DomesticCommand Command { get; private set; }

        public bool ShadowOnly { get { return true; } }

        public bool LiveAuthorized { get { return false; } }
    }

    /// <summary>
    /// Fully structured one-command description produced after a fresh fake
    /// observation.  The embedded V8 request remains a description to validate;
    /// neither object is an execution capability.
    /// </summary>
    public sealed class ValidatedSingleCommand
    {
        private readonly ReadOnlyCollection<int> _officerIds;

        internal ValidatedSingleCommand(
            object ownerToken,
            SingleCommandSliceTicket ticket,
            int corpsId,
            IEnumerable<int> officerIds,
            int expectedCost,
            int reserveMoney,
            SingleCommandRequest request)
        {
            OwnerToken = ownerToken;
            Ticket = ticket;
            CorpsId = corpsId;
            _officerIds = new ReadOnlyCollection<int>(new List<int>(officerIds));
            ExpectedCost = expectedCost;
            ReserveMoney = reserveMoney;
            V8Request = request;
        }

        internal object OwnerToken { get; private set; }

        internal SingleCommandSliceTicket Ticket { get; private set; }

        public Guid TicketId { get { return Ticket.TicketId; } }

        public ulong Sequence { get { return Ticket.Sequence; } }

        public int CityOrdinal { get { return Ticket.CityOrdinal; } }

        public int TaskOrdinal { get { return Ticket.TaskOrdinal; } }

        public int CityId { get { return Ticket.CityId; } }

        public DomesticCommand Command { get { return Ticket.Command; } }

        public int CorpsId { get; private set; }

        public ReadOnlyCollection<int> OfficerIds
        {
            get { return _officerIds; }
        }

        public int ExpectedCost { get; private set; }

        public int ReserveMoney { get; private set; }

        public SingleCommandRequest V8Request { get; private set; }

        public bool ShadowOnly { get { return true; } }

        public bool LiveAuthorized { get { return false; } }
    }

    public sealed class ShadowStepResult
    {
        internal ShadowStepResult(
            ShadowStepDisposition disposition,
            SingleCommandSliceTicket ticket,
            ShadowSkipReason skipReason,
            ShadowHaltReason haltReason,
            string detail,
            ValidatedSingleCommand validatedCommand)
        {
            Disposition = disposition;
            Ticket = ticket;
            SkipReason = skipReason;
            HaltReason = haltReason;
            Detail = detail ?? string.Empty;
            ValidatedCommand = validatedCommand;
        }

        public ShadowStepDisposition Disposition { get; private set; }

        public SingleCommandSliceTicket Ticket { get; private set; }

        public ShadowSkipReason SkipReason { get; private set; }

        public ShadowHaltReason HaltReason { get; private set; }

        public string Detail { get; private set; }

        public ValidatedSingleCommand ValidatedCommand { get; private set; }

        public bool IsSkipped { get { return Disposition == ShadowStepDisposition.Skipped; } }

        public bool IsValidated { get { return Disposition == ShadowStepDisposition.Validated; } }

        public bool IsHalted { get { return Disposition == ShadowStepDisposition.Halted; } }

        public bool ShadowOnly { get { return true; } }

        public bool LiveAuthorized { get { return false; } }
    }

    public sealed class ShadowCommitReceipt
    {
        internal ShadowCommitReceipt(
            ValidatedSingleCommand command,
            int remainingCityOfficerCount,
            int remainingCorpsMoney)
        {
            Command = command;
            RemainingCityOfficerCount = remainingCityOfficerCount;
            RemainingCorpsMoney = remainingCorpsMoney;
        }

        public ValidatedSingleCommand Command { get; private set; }

        public SingleCommandRequest V8Request { get { return Command.V8Request; } }

        public int RemainingCityOfficerCount { get; private set; }

        public int RemainingCorpsMoney { get; private set; }

        public bool ShadowOnly { get { return true; } }

        public bool LiveAuthorized { get { return false; } }

        public bool NativeSubmissionPerformed { get { return false; } }
    }
}
