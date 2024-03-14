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
namespace SmartReader.Infrastructure.Entities;

public class SmartReaderConfig
{
    public int Id { get; set; }

    public string ReaderName { get; set; }

    //public string Serial { get; set; }

    public bool IsEnabled { get; set; }

    public string IpAddress { get; set; }

    public string LicenseKey { get; set; }

    public string ProfileName { get; set; }

    public bool IsCurrentProfile { get; set; }

    public int StartTriggerType { get; set; }

    public int StartTriggerPeriod { get; set; }

    public int StartTriggerOffset { get; set; }

    public long StartTriggerUTCTimestamp { get; set; }

    public int StartTriggerGpiEvent { get; set; }

    public int StartTriggerGpiPort { get; set; }

    public int StopTriggerType { get; set; }

    public long StopTriggerDuration { get; set; }

    public int StopTriggerTimeout { get; set; }

    public int StopTriggerGpiEvent { get; set; }

    public int StopTriggerGpiPort { get; set; }

    public bool SocketServer { get; set; }

    public int SocketPort { get; set; }

    public bool SerialPort { get; set; }

    public bool UsbHid { get; set; }

    public string LineEnd { get; set; }

    public string FieldDelim { get; set; }

    public bool SoftwareFilterEnabled { get; set; }

    public int SoftwareFilterWindowSec { get; set; }

    public int SoftwareFilterField { get; set; }

    public bool SoftwareFilterReadCountAndTimeEnabled { get; set; }

    public int SoftwareFilterReadCountAndTimeSeenCount { get; set; }

    public int SoftwareFilterReadCountAndTimeWindowSec { get; set; }

    public bool IncludeReaderName { get; set; }

    public bool IncludeAntennaPort { get; set; }

    public bool IncludeAntennaZone { get; set; }

    public bool IncludeFirstSeenTimestamp { get; set; }

    public bool IncludePeakRssi { get; set; }

    public bool IncludeRFPhaseAngle { get; set; }

    public bool IncludeRFDopplerFrequency { get; set; }

    public bool IncludeRFChannelIndex { get; set; }

    public bool IncludeGpiEvent { get; set; }

    public bool IncludeTid { get; set; }

    public int TidWordStart { get; set; }

    public int TidWordCount { get; set; }

    public bool IncludeUserMemory { get; set; }

    public int UserMemoryWordStart { get; set; }

    public int UserMemoryWordCount { get; set; }

    public bool SiteEnabled { get; set; }

    public string Site { get; set; }

    public bool HttpPostEnabled { get; set; }

    public int HttpPostType { get; set; }

    public int HttpPostIntervalSec { get; set; }

    public string HttpPostURL { get; set; }

    public int HttpAuthenticationType { get; set; }

    public string HttpAuthenticationUsername { get; set; }

    public string HttpAuthenticationPassword { get; set; }

    public bool HttpAuthenticationTokenApiEnabled { get; set; }

    public string HttpAuthenticationTokenApiUrl { get; set; }

    public string HttpAuthenticationTokenApiBody { get; set; }

    public string HttpAuthenticationTokenApiValue { get; set; }

    public bool HttpVerifyPostHttpReturnCode { get; set; }

    public bool TruncateEpc { get; set; }

    public int TruncateStart { get; set; }

    public int TruncateLen { get; set; }

    public bool AdvancedGpoEnabled { get; set; }

    public int AdvancedGpoMode1 { get; set; }

    public int AdvancedGpoMode2 { get; set; }

    public int AdvancedGpoMode3 { get; set; }

    public int AdvancedGpoMode4 { get; set; }

    public bool HeartbeatEnabled { get; set; }

    public int HeartbeatPeriodSec { get; set; }

    public bool UsbFlashDrive { get; set; }

    public bool LowDutyCycleEnabled { get; set; }

    public int EmptyFieldTimeout { get; set; }

    public int FieldPingInterval { get; set; }

    public int BaudRate { get; set; }

    public bool C1g2FilterEnabled { get; set; }

    public int C1g2FilterBank { get; set; }

    public int C1g2FilterPointer { get; set; }

    public string C1g2FilterMask { get; set; }

    public int C1g2FilterLen { get; set; }

    public string DataPrefix { get; set; }

    public string DataSuffix { get; set; }

    public bool BackupToFlashDriveOnGpiEventEnabled { get; set; }

    public bool MaxTxPowerOnGpiEventEnabled { get; set; }

    public bool BackupToInternalFlashEnabled { get; set; }

    public bool TagValidationEnabled { get; set; }

    public bool KeepFilenameOnDayChange { get; set; }

    public bool PromptBeforeChanging { get; set; }

    public bool ConnectionStatus { get; set; }

    public bool MqttEnabled { get; set; }

    public bool MqttUseSsl { get; set; }

    public string MqttBrokerAddress { get; set; }

    public int MqttBrokerPort { get; set; }

    public string MqttTopic { get; set; }

    public int MqttQoS { get; set; }

    public string MqttCommandTopic { get; set; }

    public int MqttCommandQoS { get; set; }

    public string MqttUsername { get; set; }

    public string MqttPassword { get; set; }

    public string MqttProxyUrl { get; set; }

    public string MqttProxyUsername { get; set; }

    public string MqttProxyPassword { get; set; }

    public int MqttPuslishIntervalSec { get; set; }

    public bool IsCloudInterface { get; set; }

    public bool ApplyIpSettingsOnStartup { get; set; }

    public int IpAddressMode { get; set; }

    public string IpMask { get; set; }

    public string GatewayAddress { get; set; }

    public string BroadcastAddress { get; set; }

    public bool ParseSgtinEnabled { get; set; }

    public int GtinOutputType { get; set; }

    public bool HttpVerifyPeer { get; set; }

    public bool HttpVerifyHost { get; set; }

    public int JsonFormat { get; set; }

    public int CsvFileFormat { get; set; }

    public string HeartbeatUrl { get; set; }

    public string HeartbeatHttpAuthenticationType { get; set; }

    public string HeartbeatHttpAuthenticationUsername { get; set; }

    public string HeartbeatHttpAuthenticationPassword { get; set; }

    public bool HeartbeatHttpAuthenticationTokenApiEnabled { get; set; }

    public string HeartbeatHttpAuthenticationTokenApiUrl { get; set; }

    public string HeartbeatHttpAuthenticationTokenApiBody { get; set; }

    public string HeartbeatHttpAuthenticationTokenApiUsernameField { get; set; }

    public string HeartbeatHttpAuthenticationTokenApiUsernameValue { get; set; }

    public string HeartbeatHttpAuthenticationTokenApiPasswordField { get; set; }

    public string HeartbeatHttpAuthenticationTokenApiPasswordValue { get; set; }

    public string HeartbeatHttpAuthenticationTokenApiValue { get; set; }

    public string HttpAuthenticationTokenApiUsernameField { get; set; }

    public string HttpAuthenticationTokenApiUsernameValue { get; set; }

    public string HttpAuthenticationTokenApiPasswordField { get; set; }

    public string HttpAuthenticationTokenApiPasswordValue { get; set; }

    public bool ToiValidationEnabled { get; set; }

    public string ToiValidationUrl { get; set; }

    public int ToiValidationGpoDuration { get; set; }

    public int ToiGpoOk { get; set; }

    public int ToiGpoNok { get; set; }

    public int ToiGpoError { get; set; }

    public bool ToiGpi { get; set; }

    public int ToiGpoPriority { get; set; }

    public int ToiGpoMode { get; set; }

    public bool CustomField1Enabled { get; set; }

    public string CustomField1Name { get; set; }

    public string CustomField1Value { get; set; }

    public bool CustomField2Enabled { get; set; }

    public string CustomField2Name { get; set; }

    public string CustomField2Value { get; set; }

    public bool CustomField3Enabled { get; set; }

    public string CustomField3Name { get; set; }

    public string CustomField3Value { get; set; }

    public bool CustomField4Enabled { get; set; }

    public string CustomField4Name { get; set; }

    public string CustomField4Value { get; set; }

    public bool WriteUsbJson { get; set; }

    public int ReportingIntervalSeconds { get; set; }

    public int TagCacheSize { get; set; }

    public int AntennaIdentifier { get; set; }

    public int TagIdentifier { get; set; }
    public List<SmartReaderAntennaConfig> SmartReaderAntennaConfigs { get; set; }
}