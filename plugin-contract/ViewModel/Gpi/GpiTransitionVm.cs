using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace plugin_contract.ViewModel.Gpi
{
    public class GpiTransitionVm
    {
        [JsonPropertyName("timestamp")]
        public DateTime Timestamp { get; set; }

        [JsonPropertyName("hostname")]
        public string Hostname { get; set; } = string.Empty;

        [JsonPropertyName("eventType")]
        public string EventType { get; set; } = "gpiTransition";

        [JsonPropertyName("gpiTransitionEvent")]
        public GpiEventDetails GpiTransitionEvent { get; set; } = new();

        public override string ToString()
        {
            return $"Timestamp: {Timestamp}, Hostname: {Hostname}, EventType: {EventType}, GPI: {GpiTransitionEvent}";
        }
    }

    public class GpiEventDetails
    {
        [JsonPropertyName("gpi")]
        public int Gpi { get; set; }

        [JsonPropertyName("transition")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public TransitionType Transition { get; set; }

        public override string ToString()
        {
            return $"GPI: {Gpi}, Transition: {Transition}";
        }
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum TransitionType
    {
        [EnumMember(Value = "low-to-high")]
        LowToHigh,

        [EnumMember(Value = "high-to-low")]
        HighToLow
    }
}
