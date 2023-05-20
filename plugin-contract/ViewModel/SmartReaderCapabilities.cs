#region copyright
//****************************************************************************************************
// Copyright ©2023 Impinj, Inc.All rights reserved.              
//                                    
// You may use and modify this code under the terms of the Impinj Software Tools License & Disclaimer. 
// Visit https://support.impinj.com/hc/en-us/articles/360000468370-Software-Tools-License-Disclaimer   
// for full license details, or contact Impinj, Inc.at support@impinj.com for a copy of the license.   
//
//****************************************************************************************************
#endregion
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