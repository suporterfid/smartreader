using SmartReader.Infrastructure.Utils.Epcis;

namespace SmartReaderStandalone.ViewModel.Read.Epcis.Enums;

public class SourceDestinationType : Enumeration
{
    public static readonly SourceDestinationType Source = new(0, "source");
    public static readonly SourceDestinationType Destination = new(1, "destination");

    public SourceDestinationType()
    {
    }

    public SourceDestinationType(short id, string displayName) : base(id, displayName)
    {
    }
}