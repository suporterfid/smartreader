namespace SmartReaderStandalone.IotDeviceInterface
{
    public class MqttPublishingConfiguration
    {
        public double PublishIntervalMs { get; set; }
        public double BatchUpdateIntervalMs { get; set; }

        public bool BatchListEnabled { get; set; }
        public bool BatchListPublishingEnabled { get; set; }

        public bool BatchListCleanupTagEventsOnReload { get; set; }

        public bool BatchListUpdateTagEventsOnChange { get; set; }

        public bool BatchListUpdateTagEventsOnAntennaZoneChange { get; set; }



    }
}
