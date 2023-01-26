using Newtonsoft.Json;

namespace SmartReaderStandalone.ViewModel.Status;

public class SmartreaderSerialNumberDto
{
    [JsonProperty("serialNumber", NullValueHandling = NullValueHandling.Ignore)]
    public string SerialNumber { get; set; }
}