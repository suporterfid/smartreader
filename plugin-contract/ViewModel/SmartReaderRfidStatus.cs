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
using System.Text.Json.Serialization;

namespace SmartReaderStandalone.ViewModel;

public class SmartReaderRfidStatus
{
    [JsonPropertyName("Status")]
    [JsonProperty("Status", NullValueHandling = NullValueHandling.Ignore)]
    public string? Status { get; set; }

    [JsonPropertyName("ReaderOperationalStatus")]
    [JsonProperty("ReaderOperationalStatus", NullValueHandling = NullValueHandling.Ignore)]
    public string? ReaderOperationalStatus { get; set; }

    [JsonPropertyName("ReaderAdministrativeStatus")]
    [JsonProperty("ReaderAdministrativeStatus", NullValueHandling = NullValueHandling.Ignore)]
    public string? ReaderAdministrativeStatus { get; set; }

    [JsonPropertyName("Antenna1AdministrativeStatus")]
    [JsonProperty("Antenna1AdministrativeStatus", NullValueHandling = NullValueHandling.Ignore)]
    public string? Antenna1AdministrativeStatus { get; set; }

    [JsonPropertyName("Antenna1OperationalStatus")]
    [JsonProperty("Antenna1OperationalStatus", NullValueHandling = NullValueHandling.Ignore)]
    public string? Antenna1OperationalStatus { get; set; }

    [JsonPropertyName("Antenna1LastPowerLevel")]
    [JsonProperty("Antenna1LastPowerLevel", NullValueHandling = NullValueHandling.Ignore)]
    public string? Antenna1LastPowerLevel { get; set; }

    [JsonPropertyName("Antenna2AdministrativeStatus")]
    [JsonProperty("Antenna2AdministrativeStatus", NullValueHandling = NullValueHandling.Ignore)]
    public string? Antenna2AdministrativeStatus { get; set; }

    [JsonPropertyName("Antenna2OperationalStatus")]
    [JsonProperty("Antenna2OperationalStatus", NullValueHandling = NullValueHandling.Ignore)]
    public string? Antenna2OperationalStatus { get; set; }

    [JsonPropertyName("Antenna2LastPowerLevel")]
    [JsonProperty("Antenna2LastPowerLevel", NullValueHandling = NullValueHandling.Ignore)]
    public string? Antenna2LastPowerLevel { get; set; }

    [JsonPropertyName("Antenna3AdministrativeStatus")]
    [JsonProperty("Antenna3AdministrativeStatus", NullValueHandling = NullValueHandling.Ignore)]
    public string? Antenna3AdministrativeStatus { get; set; }

    [JsonPropertyName("Antenna3OperationalStatus")]
    [JsonProperty("Antenna3OperationalStatus", NullValueHandling = NullValueHandling.Ignore)]
    public string? Antenna3OperationalStatus { get; set; }

    [JsonPropertyName("Antenna3LastPowerLevel")]
    [JsonProperty("Antenna3LastPowerLevel", NullValueHandling = NullValueHandling.Ignore)]
    public string? Antenna3LastPowerLevel { get; set; }

    [JsonPropertyName("Antenna4AdministrativeStatus")]
    [JsonProperty("Antenna4AdministrativeStatus", NullValueHandling = NullValueHandling.Ignore)]
    public string? Antenna4AdministrativeStatus { get; set; }

    [JsonPropertyName("Antenna4OperationalStatus")]
    [JsonProperty("Antenna4OperationalStatus", NullValueHandling = NullValueHandling.Ignore)]
    public string? Antenna4OperationalStatus { get; set; }

    [JsonPropertyName("Antenna4LastPowerLevel")]
    [JsonProperty("Antenna4LastPowerLevel", NullValueHandling = NullValueHandling.Ignore)]
    public string? Antenna4LastPowerLevel { get; set; }
}