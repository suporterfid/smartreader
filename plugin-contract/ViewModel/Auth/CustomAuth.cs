using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace SmartReader.ViewModel.Auth
{
    public partial class CustomAuth
    {
        public CustomAuth() 
        {
            BasicAuth = new Auth();

            RShellAuth = new Auth();
        }
        [JsonProperty("BasicAuth")]
        public Auth BasicAuth { get; set; }

        [JsonProperty("RShellAuth")]
        public Auth RShellAuth { get; set; }
    }

    public partial class Auth
    {
        [JsonProperty("UserName")]
        public string UserName { get; set; }

        [JsonProperty("Password")]
        public string Password { get; set; }
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
        public static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
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
