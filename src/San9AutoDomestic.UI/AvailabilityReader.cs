using San9AutoDomestic.Adapter.San9Pk101;

namespace San9AutoDomestic.UI
{
    internal interface IAvailabilityReader
    {
        San9Pk101AvailabilityReport ReadAvailability();
    }

    internal sealed class AdapterAvailabilityReader : IAvailabilityReader
    {
        public San9Pk101AvailabilityReport ReadAvailability()
        {
            return new San9Pk101Adapter().ReadAvailability();
        }
    }
}
