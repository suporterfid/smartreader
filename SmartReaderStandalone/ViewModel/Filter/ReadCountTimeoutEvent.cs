namespace SmartReaderStandalone.ViewModel.Filter;

public class ReadCountTimeoutEvent
{
    public string Epc { get; set; }

    public int Antenna { get; set; }

    public int Timeout { get; set; }

    public int Count { get; set; }

    public long EventTimestamp { get; set; }
}