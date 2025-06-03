#region copyright
//****************************************************************************************************
// Copyright ©2025 Impinj, Inc.All rights reserved.              
//                                    
// You may use and modify this code under the terms of the Impinj Software Tools License & Disclaimer. 
// Visit https://support.impinj.com/hc/en-us/articles/360000468370-Software-Tools-License-Disclaimer   
// for full license details, or contact Impinj, Inc.at support@impinj.com for a copy of the license.   
//
//****************************************************************************************************
#endregion
using System.Text.Json.Serialization;

namespace plugin_contract.ViewModel.Gpi
{
    public class GpiConfiguration
    {
        [JsonPropertyName("debounceMilliseconds")]
        public int DebounceMilliseconds { get; set; }

        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; }

        [JsonPropertyName("gpi")]
        public int Gpi { get; set; }

        [JsonPropertyName("state")]
        public string State { get; set; } = string.Empty;

        public override string ToString()
        {
            return $"GPI: {Gpi}, State: {State}, Enabled: {Enabled}, Debounce: {DebounceMilliseconds}";
        }
    }

    public class GpiConfigRoot
    {
        [JsonPropertyName("gpiConfigurations")]
        public List<GpiConfiguration> GpiConfigurations { get; set; } = [];

        [JsonPropertyName("gpiTransitionEvents")]
        public string GpiTransitionEvents { get; set; } = string.Empty;

        public override string ToString()
        {
            return $"GpiTransitionEvents: {GpiTransitionEvents}, Configurations: [{string.Join(", ", GpiConfigurations)}]";
        }
    }
}
