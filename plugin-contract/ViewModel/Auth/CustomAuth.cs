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
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.Globalization;

namespace SmartReader.ViewModel.Auth
{
    public partial class CustomAuth
    {
        public CustomAuth()
        {
            BasicAuth = new Auth();

            RShellAuth = new Auth();
        }
        [JsonProperty(nameof(BasicAuth))]
        public Auth? BasicAuth { get; set; }

        [JsonProperty(nameof(RShellAuth))]
        public Auth? RShellAuth { get; set; }
    }

    public partial class Auth
    {
        [JsonProperty(nameof(UserName))]
        public string? UserName { get; set; }

        [JsonProperty(nameof(Password))]
        public string? Password { get; set; }
    }

    public partial class CustomAuth
    {
        public static CustomAuth FromJson(string json) => JsonConvert.DeserializeObject<CustomAuth>(json, SmartReader.ViewModel.Auth.Converter.Settings);
    }

    public static class Serialize
    {
        public static string ToJson(this CustomAuth self) => JsonConvert.SerializeObject(self, SmartReader.ViewModel.Auth.Converter.Settings);
    }

    internal static class Converter
    {
        public static readonly JsonSerializerSettings Settings = new()
        {
            MetadataPropertyHandling = MetadataPropertyHandling.Ignore,
            DateParseHandling = DateParseHandling.None,
            Converters =
            {
                new IsoDateTimeConverter { DateTimeStyles = DateTimeStyles.AssumeUniversal }
            },
        };
    }
}
