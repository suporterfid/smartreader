using Newtonsoft.Json;

namespace SmartReaderStandalone.ViewModel;

public class SmartReaderCapabilities
{
    [JsonProperty("txTable", NullValueHandling = NullValueHandling.Ignore)]
    public List<int> TxTable { get; set; }

    [JsonProperty("rxTable", NullValueHandling = NullValueHandling.Ignore)]
    public List<int> RxTable { get; set; }

    [JsonProperty("rfModeTable", NullValueHandling = NullValueHandling.Ignore)]
    public List<int> RfModeTable { get; set; }

    [JsonProperty("searchModeTable", NullValueHandling = NullValueHandling.Ignore)]
    public List<int> SearchModeTable { get; set; }

    [JsonProperty("maxAntennas", NullValueHandling = NullValueHandling.Ignore)]
    public int MaxAntennas { get; set; }

    [JsonProperty("licenseValid", NullValueHandling = NullValueHandling.Ignore)]
    public int LicenseValid { get; set; }

    [JsonProperty("validAntennas", NullValueHandling = NullValueHandling.Ignore)]
    public string ValidAntennas { get; set; }

    [JsonProperty("modelName", NullValueHandling = NullValueHandling.Ignore)]
    public string ModelName { get; set; }
}