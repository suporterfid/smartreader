using Newtonsoft.Json;

namespace SmartReaderStandalone.ViewModel.Status;

public class SmartreaderRunningStatusDto
{
    [JsonProperty("status", NullValueHandling = NullValueHandling.Ignore)]
    public string Status { get; set; }
}