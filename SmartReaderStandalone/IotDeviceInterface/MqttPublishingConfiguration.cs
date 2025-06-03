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
