using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

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
        public List<GpiConfiguration> GpiConfigurations { get; set; } = new();

        [JsonPropertyName("gpiTransitionEvents")]
        public string GpiTransitionEvents { get; set; } = string.Empty;

        public override string ToString()
        {
            return $"GpiTransitionEvents: {GpiTransitionEvents}, Configurations: [{string.Join(", ", GpiConfigurations)}]";
        }
    }
}
