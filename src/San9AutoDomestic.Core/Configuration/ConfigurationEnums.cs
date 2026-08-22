namespace San9AutoDomestic.Core.Configuration
{
    public enum CityScope
    {
        Unknown = 0,
        DirectCities = 1
    }

    public enum CityOrder
    {
        Unknown = 0,
        GameIdAscending = 1
    }

    public enum DomesticCommand
    {
        Unknown = 0,
        Patrol = 1,
        Commerce = 2,
        Cultivate = 3,
        Train = 4,
        Repair = 5
    }

    public enum SelectionPolicy
    {
        Unknown = 0,
        NativeBest = 1,
        VerifiedStatFallback = 2
    }
}
