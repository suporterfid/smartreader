using SmartReaderStandalone.ViewModel.Read.Epcis.Enums;

namespace SmartReaderStandalone.ViewModel.Read.Epcis;

public class SourceDestination
{
    public string Type { get; set; }
    public string Id { get; set; }
    public SourceDestinationType Direction { get; set; }
}