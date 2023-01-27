using System.Diagnostics.CodeAnalysis;
using System.Web;

namespace SmartReader.Infrastructure.ViewModel;

public class StandaloneConfigDTO : IEquatable<StandaloneConfigDTO>
{
    public string id { get; set; }

    public string readerName { get; set; }

    public string serial { get; set; }

    public string isEnabled { get; set; }

    public string licenseKey { get; set; }

    public string profileName { get; set; }

    public string isCurrentProfile { get; set; }

    public string antennaPorts { get; set; }

    public string antennaStates { get; set; }

    public string antennaZones { get; set; }

    public string transmitPower { get; set; }

    public string receiveSensitivity { get; set; }

    public string readerMode { get; set; }

    public string searchMode { get; set; }

    public string session { get; set; }

    public string tagPopulation { get; set; }

    public string startTriggerType { get; set; }

    public string startTriggerPeriod { get; set; }

    public string startTriggerOffset { get; set; }

    public string startTriggerUTCTimestamp { get; set; }

    public string startTriggerGpiEvent { get; set; }

    public string startTriggerGpiPort { get; set; }

    public string stopTriggerType { get; set; }

    public string stopTriggerDuration { get; set; }

    public string stopTriggerTimeout { get; set; }

    public string stopTriggerGpiEvent { get; set; }

    public string stopTriggerGpiPort { get; set; }

    public string socketServer { get; set; }

    public string socketPort { get; set; }

    public string socketCommandServer { get; set; }

    public string socketCommandServerPort { get; set; }

    public string udpServer { get; set; }

    public string udpIpAddress { get; set; }

    public string udpReaderPort { get; set; }

    public string udpRemoteIpAddress { get; set; }

    public string udpRemotePort { get; set; }

    public string serialPort { get; set; }

    public string usbHid { get; set; }

    public string lineEnd { get; set; }

    public string fieldDelim { get; set; }

    public string softwareFilterEnabled { get; set; }

    public string softwareFilterWindowSec { get; set; }

    public string softwareFilterField { get; set; }

    public string softwareFilterReadCountTimeoutEnabled { get; set; }

    public string softwareFilterReadCountTimeoutSeenCount { get; set; }

    public string softwareFilterReadCountTimeoutIntervalInSec { get; set; }

    public string includeReaderName { get; set; }

    public string includeAntennaPort { get; set; }

    public string includeAntennaZone { get; set; }

    public string includeFirstSeenTimestamp { get; set; }

    public string includePeakRssi { get; set; }

    public string includeRFPhaseAngle { get; set; }

    public string includeRFDopplerFrequency { get; set; }

    public string includeRFChannelIndex { get; set; }

    public string includeGpiEvent { get; set; }

    public string includeInventoryStatusEvent { get; set; }

    public string includeInventoryStatusEventId { get; set; }

    public string includeInventoryStatusEventTotalCount { get; set; }

    public string includeTid { get; set; }

    public string tidWordStart { get; set; }

    public string tidWordCount { get; set; }

    public string includeUserMemory { get; set; }

    public string userMemoryWordStart { get; set; }

    public string userMemoryWordCount { get; set; }

    public string siteEnabled { get; set; }

    public string site { get; set; }

    public string httpPostEnabled { get; set; }

    public string httpPostType { get; set; }

    public string httpPostIntervalSec { get; set; }

    public string httpPostURL { get; set; }

    public string httpAuthenticationType { get; set; }

    public string httpAuthenticationUsername { get; set; }

    public string httpAuthenticationPassword { get; set; }

    public string httpAuthenticationTokenApiEnabled { get; set; }

    public string httpAuthenticationTokenApiUrl { get; set; }

    public string httpAuthenticationTokenApiBody { get; set; }

    public string httpAuthenticationTokenApiValue { get; set; }

    public string httpAuthenticationHeader { get; set; }

    public string httpAuthenticationHeaderValue { get; set; }

    public string httpVerifyPostHttpReturnCode { get; set; }

    public string truncateEpc { get; set; }

    public string truncateStart { get; set; }

    public string truncateLen { get; set; }

    public string advancedGpoEnabled { get; set; }

    public string advancedGpoMode1 { get; set; }

    public string advancedGpoMode2 { get; set; }

    public string advancedGpoMode3 { get; set; }

    public string advancedGpoMode4 { get; set; }

    public string heartbeatEnabled { get; set; }

    public string heartbeatPeriodSec { get; set; }

    public string usbFlashDrive { get; set; }

    public string lowDutyCycleEnabled { get; set; }

    public string emptyFieldTimeout { get; set; }

    public string fieldPingInterval { get; set; }

    public string baudRate { get; set; }

    public string c1g2FilterEnabled { get; set; }

    public string c1g2FilterBank { get; set; }

    public string c1g2FilterPointer { get; set; }

    public string c1g2FilterMask { get; set; }

    public string c1g2FilterLen { get; set; }

    public string dataPrefix { get; set; }

    public string dataSuffix { get; set; }

    public string backupToFlashDriveOnGpiEventEnabled { get; set; }

    public string maxTxPowerOnGpiEventEnabled { get; set; }

    public string backupToInternalFlashEnabled { get; set; }

    public string tagValidationEnabled { get; set; }

    public string keepFilenameOnDayChange { get; set; }

    public string promptBeforeChanging { get; set; }

    public string connectionStatus { get; set; }

    public string mqttEnabled { get; set; }

    public string mqttUseSsl { get; set; }

    public string mqttSslCaCertificate { get; set; }

    public string mqttSslClientCertificate { get; set; }

    public string mqttSslClientCertificatePassword { get; set; }

    public string mqttBrokerName { get; set; }

    public string mqttBrokerDescription { get; set; }

    public string mqttBrokerType { get; set; }

    public string mqttBrokerProtocol { get; set; }

    public string mqttBrokerAddress { get; set; }

    public string mqttBrokerPort { get; set; }

    public string mqttBrokerCleanSession { get; set; }

    public string mqttBrokerKeepAlive { get; set; }

    public string mqttBrokerDebug { get; set; }

    public string mqttTagEventsTopic { get; set; }

    public string mqttTagEventsQoS { get; set; }

    public string mqttTagEventsRetainMessages { get; set; }

    public string mqttManagementEventsTopic { get; set; }

    public string mqttManagementEventsQoS { get; set; }

    public string mqttManagementEventsRetainMessages { get; set; }

    public string mqttMetricEventsTopic { get; set; }

    public string mqttMetricEventsQoS { get; set; }

    public string mqttMetricEventsRetainMessages { get; set; }

    public string mqttManagementCommandTopic { get; set; }

    public string mqttManagementCommandQoS { get; set; }

    public string mqttManagementCommandRetainMessages { get; set; }

    public string mqttManagementResponseTopic { get; set; }

    public string mqttManagementResponseQoS { get; set; }

    public string mqttManagementResponseRetainMessages { get; set; }

    public string mqttControlCommandTopic { get; set; }

    public string mqttControlCommandQoS { get; set; }

    public string mqttControlCommandRetainMessages { get; set; }

    public string mqttControlResponseTopic { get; set; }

    public string mqttControlResponseQoS { get; set; }

    public string mqttControlResponseRetainMessages { get; set; }

    public string mqttUsername { get; set; }

    public string mqttPassword { get; set; }

    public string mqttProxyUrl { get; set; }

    public string mqttProxyUsername { get; set; }

    public string mqttProxyPassword { get; set; }

    public string mqttPuslishIntervalSec { get; set; }

    public string isCloudInterface { get; set; }

    public string applyIpSettingsOnStartup { get; set; }

    public string ipAddressMode { get; set; }

    public string ipAddress { get; set; }

    public string ipMask { get; set; }

    public string gatewayAddress { get; set; }

    public string broadcastAddress { get; set; }

    public string parseSgtinEnabled { get; set; }

    public string gtinOutputType { get; set; }

    public string parseSgtinIncludeKeyType { get; set; }

    public string parseSgtinIncludeSerial { get; set; }

    public string parseSgtinIncludePureIdentity { get; set; }

    public string httpVerifyPeer { get; set; }

    public string httpVerifyHost { get; set; }

    public string jsonFormat { get; set; }

    public string csvFileFormat { get; set; }

    public string heartbeatUrl { get; set; }

    public string heartbeatHttpAuthenticationType { get; set; }

    public string heartbeatHttpAuthenticationUsername { get; set; }

    public string heartbeatHttpAuthenticationPassword { get; set; }

    public string heartbeatHttpAuthenticationTokenApiEnabled { get; set; }

    public string heartbeatHttpAuthenticationTokenApiUrl { get; set; }

    public string heartbeatHttpAuthenticationTokenApiBody { get; set; }

    public string heartbeatHttpAuthenticationTokenApiUsernameField { get; set; }

    public string heartbeatHttpAuthenticationTokenApiUsernameValue { get; set; }

    public string heartbeatHttpAuthenticationTokenApiPasswordField { get; set; }

    public string heartbeatHttpAuthenticationTokenApiPasswordValue { get; set; }

    public string heartbeatHttpAuthenticationTokenApiValue { get; set; }

    public string httpAuthenticationTokenApiUsernameField { get; set; }

    public string httpAuthenticationTokenApiUsernameValue { get; set; }

    public string httpAuthenticationTokenApiPasswordField { get; set; }

    public string httpAuthenticationTokenApiPasswordValue { get; set; }

    public string toiValidationEnabled { get; set; }

    public string toiValidationUrl { get; set; }

    public string toiValidationGpoDuration { get; set; }

    public string toiGpoOk { get; set; }

    public string toiGpoNok { get; set; }

    public string toiGpoError { get; set; }

    public string toiGpi { get; set; }

    public string toiGpoPriority { get; set; }

    public string toiGpoMode { get; set; }

    public string customField1Enabled { get; set; }

    public string customField1Name { get; set; }

    public string customField1Value { get; set; }

    public string customField2Enabled { get; set; }

    public string customField2Name { get; set; }

    public string customField2Value { get; set; }

    public string customField3Enabled { get; set; }

    public string customField3Name { get; set; }

    public string customField3Value { get; set; }

    public string customField4Enabled { get; set; }

    public string customField4Name { get; set; }

    public string customField4Value { get; set; }

    public string writeUsbJson { get; set; }

    public string reportingIntervalSeconds { get; set; }

    public string tagCacheSize { get; set; }

    public string antennaIdentifier { get; set; }

    public string tagIdentifier { get; set; }

    public string positioningEpcsEnabled { get; set; }

    public string positioningAntennaPorts { get; set; }

    public string positioningEpcsHeaderList { get; set; }

    public string positioningEpcsFilter { get; set; }

    public string positioningExpirationInSec { get; set; }

    public string positioningReportIntervalInSec { get; set; }

    public string enableUniqueTagRead { get; set; }

    public string enableAntennaTask { get; set; }

    public string packageHeaders { get; set; }

    public string enablePartialValidation { get; set; }

    public string validationAcceptanceThreshold { get; set; }

    public string validationAcceptanceThresholdTimeout { get; set; }

    public string publishFullShipmentValidationListOnAcceptanceThreshold { get; set; }

    public string publishSingleTimeOnAcceptanceThreshold { get; set; }

    public string readerSerial { get; set; }

    public string activePlugin { get; set; }

    public string pluginServer { get; set; }

    public string enablePluginShipmentVerification { get; set; }

    public string softwareFilterIncludeEpcsHeaderListEnabled { get; set; }

    public string softwareFilterIncludeEpcsHeaderList { get; set; }

    public string isLogFileEnabled { get; set; }

    public string rciSpotReportEnabled { get; set; }

    public string rciSpotReportIncludePc { get; set; }

    public string rciSpotReportIncludeScheme { get; set; }

    public string rciSpotReportIncludeEpcUri { get; set; }

    public string rciSpotReportIncludeAnt { get; set; }

    public string rciSpotReportIncludeDwnCnt { get; set; }

    public string rciSpotReportIncludeInvCnt { get; set; }

    public string rciSpotReportIncludePhase { get; set; }

    public string rciSpotReportIncludeProf { get; set; }

    public string rciSpotReportIncludeRange { get; set; }

    public string rciSpotReportIncludeRssi { get; set; }

    public string rciSpotReportIncludeRz { get; set; }

    public string rciSpotReportIncludeSpot { get; set; }

    public string rciSpotReportIncludeTimeStamp { get; set; }

    public string enableOpcUaClient { get; set; }

    public string opcUaConnectionName { get; set; }

    public string opcUaConnectionPublisherId { get; set; }

    public string opcUaConnectionUrl { get; set; }

    public string opcUaConnectionDiscoveryAddress { get; set; }

    public string opcUaWriterGroupName { get; set; }

    public string opcUaWriterGroupId { get; set; }

    public string opcUaWriterPublishingInterval { get; set; }

    public string opcUaWriterKeepAliveTime { get; set; }

    public string opcUaWriterMaxNetworkMessageSize { get; set; }

    public string opcUaWriterHeaderLayoutUri { get; set; }

    public string opcUaDataSetWriterName { get; set; }

    public string opcUaDataSetWriterId { get; set; }

    public string opcUaDataSetName { get; set; }

    public string opcUaDataSetKeyFrameCount { get; set; }

    public string enablePlugin { get; set; }

    public string enableBarcodeTcp { get; set; }

    public string enableBarcodeSerial { get; set; }

    public string groupEventsOnInventoryStatus { get; set; }

    public string barcodeTcpAddress { get; set; }

    public string barcodeTcpPort { get; set; }

    public string barcodeTcpLen { get; set; }

    public string barcodeTcpNoDataString { get; set; }

    public string barcodeProcessNoDataString { get; set; }

    public string barcodeEnableQueue { get; set; }

    public string enableValidation { get; set; }

    public string requireUniqueProductCode { get; set; }

    public string enableTagEventStream { get; set; }

    public string enableSummaryStream { get; set; }

    public string enableExternalApiVerification { get; set; }

    public string externalApiVerificationSearchOrderUrl { get; set; }

    public string externalApiVerificationSearchProductUrl { get; set; }

    public string externalApiVerificationPublishDataUrl { get; set; }

    public string externalApiVerificationHttpHeaderName { get; set; }

    public string externalApiVerificationHttpHeaderValue { get; set; }

    public string operatingRegion { get; set; }

    public string systemImageUpgradeUrl { get; set; }


    public bool Equals([AllowNull] StandaloneConfigDTO otherStandaloneConfigDTO)
    {
        if (otherStandaloneConfigDTO == null)
            return false;

        if (!id.Equals(otherStandaloneConfigDTO.id)) return false;

        if (!readerName.Equals(otherStandaloneConfigDTO.readerName)) return false;

        if (!serial.Equals(otherStandaloneConfigDTO.serial)) return false;

        if (!isEnabled.Equals(otherStandaloneConfigDTO.isEnabled)) return false;

        if (!licenseKey.Equals(otherStandaloneConfigDTO.licenseKey)) return false;

        if (!profileName.Equals(otherStandaloneConfigDTO.profileName)) return false;

        if (!isCurrentProfile.Equals(otherStandaloneConfigDTO.isCurrentProfile)) return false;

        if (!antennaPorts.Equals(otherStandaloneConfigDTO.antennaPorts)) return false;

        if (!antennaStates.Equals(otherStandaloneConfigDTO.antennaStates)) return false;

        if (!antennaZones.Equals(otherStandaloneConfigDTO.antennaZones)) return false;

        if (!transmitPower.Equals(otherStandaloneConfigDTO.transmitPower)) return false;

        if (!receiveSensitivity.Equals(otherStandaloneConfigDTO.receiveSensitivity)) return false;

        if (!readerMode.Equals(otherStandaloneConfigDTO.readerMode)) return false;

        if (!searchMode.Equals(otherStandaloneConfigDTO.searchMode)) return false;

        if (!session.Equals(otherStandaloneConfigDTO.session)) return false;

        if (!tagPopulation.Equals(otherStandaloneConfigDTO.tagPopulation)) return false;

        if (!startTriggerType.Equals(otherStandaloneConfigDTO.startTriggerType)) return false;

        if (!startTriggerPeriod.Equals(otherStandaloneConfigDTO.startTriggerPeriod)) return false;

        if (!startTriggerOffset.Equals(otherStandaloneConfigDTO.startTriggerOffset)) return false;

        if (!startTriggerUTCTimestamp.Equals(otherStandaloneConfigDTO.startTriggerUTCTimestamp)) return false;

        if (!startTriggerGpiEvent.Equals(otherStandaloneConfigDTO.startTriggerGpiEvent)) return false;

        if (!startTriggerGpiPort.Equals(otherStandaloneConfigDTO.startTriggerGpiPort)) return false;

        if (!stopTriggerType.Equals(otherStandaloneConfigDTO.stopTriggerType)) return false;

        if (!stopTriggerDuration.Equals(otherStandaloneConfigDTO.stopTriggerDuration)) return false;

        if (!stopTriggerTimeout.Equals(otherStandaloneConfigDTO.stopTriggerTimeout)) return false;

        if (!stopTriggerGpiEvent.Equals(otherStandaloneConfigDTO.stopTriggerGpiEvent)) return false;

        if (!stopTriggerGpiPort.Equals(otherStandaloneConfigDTO.stopTriggerGpiPort)) return false;

        if (!socketServer.Equals(otherStandaloneConfigDTO.socketServer)) return false;

        if (!socketPort.Equals(otherStandaloneConfigDTO.socketPort)) return false;

        if (!socketCommandServer.Equals(otherStandaloneConfigDTO.socketCommandServer)) return false;

        if (!socketCommandServerPort.Equals(otherStandaloneConfigDTO.socketCommandServerPort)) return false;

        if (!udpServer.Equals(otherStandaloneConfigDTO.udpServer)) return false;

        if (!udpIpAddress.Equals(otherStandaloneConfigDTO.udpIpAddress)) return false;

        if (!udpReaderPort.Equals(otherStandaloneConfigDTO.udpReaderPort)) return false;

        if (!udpRemoteIpAddress.Equals(otherStandaloneConfigDTO.udpRemoteIpAddress)) return false;

        if (!udpRemotePort.Equals(otherStandaloneConfigDTO.udpRemotePort)) return false;

        if (!serialPort.Equals(otherStandaloneConfigDTO.serialPort)) return false;

        if (!usbHid.Equals(otherStandaloneConfigDTO.usbHid)) return false;

        if (!lineEnd.Equals(otherStandaloneConfigDTO.lineEnd)) return false;

        if (!fieldDelim.Equals(otherStandaloneConfigDTO.fieldDelim)) return false;

        if (!softwareFilterEnabled.Equals(otherStandaloneConfigDTO.softwareFilterEnabled)) return false;

        if (!softwareFilterWindowSec.Equals(otherStandaloneConfigDTO.softwareFilterWindowSec)) return false;

        if (!softwareFilterField.Equals(otherStandaloneConfigDTO.softwareFilterField)) return false;

        if (!softwareFilterReadCountTimeoutEnabled.Equals(
                otherStandaloneConfigDTO.softwareFilterReadCountTimeoutEnabled)) return false;

        if (!softwareFilterReadCountTimeoutSeenCount.Equals(otherStandaloneConfigDTO
                .softwareFilterReadCountTimeoutSeenCount)) return false;

        if (!softwareFilterReadCountTimeoutIntervalInSec.Equals(otherStandaloneConfigDTO
                .softwareFilterReadCountTimeoutIntervalInSec)) return false;

        if (!includeReaderName.Equals(otherStandaloneConfigDTO.includeReaderName)) return false;

        if (!includeAntennaPort.Equals(otherStandaloneConfigDTO.includeAntennaPort)) return false;

        if (!includeAntennaZone.Equals(otherStandaloneConfigDTO.includeAntennaZone)) return false;

        if (!includeFirstSeenTimestamp.Equals(otherStandaloneConfigDTO.includeFirstSeenTimestamp)) return false;

        if (!includePeakRssi.Equals(otherStandaloneConfigDTO.includePeakRssi)) return false;

        if (!includeRFPhaseAngle.Equals(otherStandaloneConfigDTO.includeRFPhaseAngle)) return false;

        if (!includeRFDopplerFrequency.Equals(otherStandaloneConfigDTO.includeRFDopplerFrequency)) return false;

        if (!includeRFChannelIndex.Equals(otherStandaloneConfigDTO.includeRFChannelIndex)) return false;

        if (!includeGpiEvent.Equals(otherStandaloneConfigDTO.includeGpiEvent)) return false;

        if (!includeInventoryStatusEvent.Equals(otherStandaloneConfigDTO.includeInventoryStatusEvent)) return false;

        if (!includeInventoryStatusEventId.Equals(otherStandaloneConfigDTO.includeInventoryStatusEventId)) return false;


        if (!includeInventoryStatusEventTotalCount.Equals(
                otherStandaloneConfigDTO.includeInventoryStatusEventTotalCount)) return false;


        if (!includeTid.Equals(otherStandaloneConfigDTO.includeTid)) return false;


        if (!tidWordStart.Equals(otherStandaloneConfigDTO.tidWordStart)) return false;


        if (!tidWordCount.Equals(otherStandaloneConfigDTO.tidWordCount)) return false;


        if (!includeUserMemory.Equals(otherStandaloneConfigDTO.includeUserMemory)) return false;


        if (!userMemoryWordStart.Equals(otherStandaloneConfigDTO.userMemoryWordStart)) return false;


        if (!userMemoryWordCount.Equals(otherStandaloneConfigDTO.userMemoryWordCount)) return false;


        if (!siteEnabled.Equals(otherStandaloneConfigDTO.siteEnabled)) return false;


        if (!site.Equals(otherStandaloneConfigDTO.site)) return false;


        if (!httpPostEnabled.Equals(otherStandaloneConfigDTO.httpPostEnabled)) return false;


        if (!httpPostType.Equals(otherStandaloneConfigDTO.httpPostType)) return false;


        if (!httpPostIntervalSec.Equals(otherStandaloneConfigDTO.httpPostIntervalSec)) return false;


        if (!httpPostURL.Equals(otherStandaloneConfigDTO.httpPostURL)) return false;


        if (!httpAuthenticationType.Equals(otherStandaloneConfigDTO.httpAuthenticationType)) return false;


        if (!httpAuthenticationUsername.Equals(otherStandaloneConfigDTO.httpAuthenticationUsername)) return false;


        if (!httpAuthenticationPassword.Equals(otherStandaloneConfigDTO.httpAuthenticationPassword)) return false;


        if (!httpAuthenticationTokenApiEnabled.Equals(otherStandaloneConfigDTO.httpAuthenticationTokenApiEnabled))
            return false;


        if (!httpAuthenticationTokenApiUrl.Equals(otherStandaloneConfigDTO.httpAuthenticationTokenApiUrl)) return false;


        if (!httpAuthenticationTokenApiBody.Equals(otherStandaloneConfigDTO.httpAuthenticationTokenApiBody))
            return false;


        if (!httpAuthenticationTokenApiValue.Equals(otherStandaloneConfigDTO.httpAuthenticationTokenApiValue))
            return false;


        if (!httpVerifyPostHttpReturnCode.Equals(otherStandaloneConfigDTO.httpVerifyPostHttpReturnCode)) return false;


        if (!truncateEpc.Equals(otherStandaloneConfigDTO.truncateEpc)) return false;


        if (!truncateStart.Equals(otherStandaloneConfigDTO.truncateStart)) return false;


        if (!truncateLen.Equals(otherStandaloneConfigDTO.truncateLen)) return false;


        if (!advancedGpoEnabled.Equals(otherStandaloneConfigDTO.advancedGpoEnabled)) return false;


        if (!advancedGpoMode1.Equals(otherStandaloneConfigDTO.advancedGpoMode1)) return false;


        if (!advancedGpoMode2.Equals(otherStandaloneConfigDTO.advancedGpoMode2)) return false;


        if (!advancedGpoMode3.Equals(otherStandaloneConfigDTO.advancedGpoMode3)) return false;


        if (!advancedGpoMode4.Equals(otherStandaloneConfigDTO.advancedGpoMode4)) return false;


        if (!heartbeatEnabled.Equals(otherStandaloneConfigDTO.heartbeatEnabled)) return false;


        if (!heartbeatPeriodSec.Equals(otherStandaloneConfigDTO.heartbeatPeriodSec)) return false;


        if (!usbFlashDrive.Equals(otherStandaloneConfigDTO.usbFlashDrive)) return false;


        if (!lowDutyCycleEnabled.Equals(otherStandaloneConfigDTO.lowDutyCycleEnabled)) return false;


        if (!emptyFieldTimeout.Equals(otherStandaloneConfigDTO.emptyFieldTimeout)) return false;


        if (!fieldPingInterval.Equals(otherStandaloneConfigDTO.fieldPingInterval)) return false;


        if (!baudRate.Equals(otherStandaloneConfigDTO.baudRate)) return false;


        if (!c1g2FilterEnabled.Equals(otherStandaloneConfigDTO.c1g2FilterEnabled)) return false;


        if (!c1g2FilterBank.Equals(otherStandaloneConfigDTO.c1g2FilterBank)) return false;


        if (!c1g2FilterPointer.Equals(otherStandaloneConfigDTO.c1g2FilterPointer)) return false;


        if (!c1g2FilterMask.Equals(otherStandaloneConfigDTO.c1g2FilterMask)) return false;


        if (!c1g2FilterLen.Equals(otherStandaloneConfigDTO.c1g2FilterLen)) return false;


        if (!dataPrefix.Equals(otherStandaloneConfigDTO.dataPrefix)) return false;


        if (!dataSuffix.Equals(otherStandaloneConfigDTO.dataSuffix)) return false;


        if (!backupToFlashDriveOnGpiEventEnabled.Equals(otherStandaloneConfigDTO.backupToFlashDriveOnGpiEventEnabled))
            return false;


        if (!maxTxPowerOnGpiEventEnabled.Equals(otherStandaloneConfigDTO.maxTxPowerOnGpiEventEnabled)) return false;


        if (!backupToInternalFlashEnabled.Equals(otherStandaloneConfigDTO.backupToInternalFlashEnabled)) return false;


        if (!tagValidationEnabled.Equals(otherStandaloneConfigDTO.tagValidationEnabled)) return false;


        if (!keepFilenameOnDayChange.Equals(otherStandaloneConfigDTO.keepFilenameOnDayChange)) return false;


        if (!promptBeforeChanging.Equals(otherStandaloneConfigDTO.promptBeforeChanging)) return false;


        if (!connectionStatus.Equals(otherStandaloneConfigDTO.connectionStatus)) return false;


        if (!mqttEnabled.Equals(otherStandaloneConfigDTO.mqttEnabled)) return false;


        if (!mqttUseSsl.Equals(otherStandaloneConfigDTO.mqttUseSsl)) return false;

        if (!mqttSslCaCertificate.Equals(otherStandaloneConfigDTO.mqttSslCaCertificate)) return false;

        if (!mqttSslClientCertificate.Equals(otherStandaloneConfigDTO.mqttSslClientCertificate)) return false;

        if (!mqttBrokerAddress.Equals(otherStandaloneConfigDTO.mqttBrokerAddress)) return false;

        if (!mqttBrokerName.Equals(otherStandaloneConfigDTO.mqttBrokerName)) return false;

        if (!mqttBrokerDescription.Equals(otherStandaloneConfigDTO.mqttBrokerDescription)) return false;

        if (!mqttBrokerType.Equals(otherStandaloneConfigDTO.mqttBrokerType)) return false;

        if (!mqttBrokerProtocol.Equals(otherStandaloneConfigDTO.mqttBrokerProtocol)) return false;

        if (!mqttBrokerCleanSession.Equals(otherStandaloneConfigDTO.mqttBrokerCleanSession)) return false;

        if (!mqttBrokerKeepAlive.Equals(otherStandaloneConfigDTO.mqttBrokerKeepAlive)) return false;

        if (!mqttBrokerDebug.Equals(otherStandaloneConfigDTO.mqttBrokerDebug)) return false;


        if (!mqttBrokerPort.Equals(otherStandaloneConfigDTO.mqttBrokerPort)) return false;


        if (!mqttTagEventsTopic.Equals(otherStandaloneConfigDTO.mqttTagEventsTopic)) return false;


        if (!mqttTagEventsQoS.Equals(otherStandaloneConfigDTO.mqttTagEventsQoS)) return false;

        if (!mqttTagEventsRetainMessages.Equals(otherStandaloneConfigDTO.mqttTagEventsRetainMessages)) return false;

        if (!mqttManagementEventsTopic.Equals(otherStandaloneConfigDTO.mqttManagementEventsTopic)) return false;

        if (!mqttManagementEventsQoS.Equals(otherStandaloneConfigDTO.mqttManagementEventsQoS)) return false;

        if (!mqttManagementEventsRetainMessages.Equals(otherStandaloneConfigDTO.mqttManagementEventsRetainMessages))
            return false;

        if (!mqttMetricEventsTopic.Equals(otherStandaloneConfigDTO.mqttMetricEventsTopic)) return false;

        if (!mqttMetricEventsQoS.Equals(otherStandaloneConfigDTO.mqttMetricEventsQoS)) return false;

        if (!mqttMetricEventsRetainMessages.Equals(otherStandaloneConfigDTO.mqttMetricEventsRetainMessages))
            return false;

        if (!mqttManagementCommandTopic.Equals(otherStandaloneConfigDTO.mqttManagementCommandTopic)) return false;

        if (!mqttManagementCommandQoS.Equals(otherStandaloneConfigDTO.mqttManagementCommandQoS)) return false;

        if (!mqttManagementCommandRetainMessages.Equals(otherStandaloneConfigDTO.mqttManagementCommandRetainMessages))
            return false;

        if (!mqttManagementResponseTopic.Equals(otherStandaloneConfigDTO.mqttManagementResponseTopic)) return false;

        if (!mqttManagementResponseQoS.Equals(otherStandaloneConfigDTO.mqttManagementResponseQoS)) return false;

        if (!mqttManagementResponseRetainMessages.Equals(otherStandaloneConfigDTO.mqttManagementResponseRetainMessages))
            return false;

        if (!mqttControlCommandTopic.Equals(otherStandaloneConfigDTO.mqttControlCommandTopic)) return false;

        if (!mqttControlCommandQoS.Equals(otherStandaloneConfigDTO.mqttControlCommandQoS)) return false;

        if (!mqttControlCommandRetainMessages.Equals(otherStandaloneConfigDTO.mqttControlCommandRetainMessages))
            return false;

        if (!mqttControlResponseTopic.Equals(otherStandaloneConfigDTO.mqttControlResponseTopic)) return false;

        if (!mqttControlResponseQoS.Equals(otherStandaloneConfigDTO.mqttControlResponseQoS)) return false;

        if (!mqttControlResponseRetainMessages.Equals(otherStandaloneConfigDTO.mqttControlResponseRetainMessages))
            return false;

        if (!mqttUsername.Equals(otherStandaloneConfigDTO.mqttUsername)) return false;


        if (!mqttPassword.Equals(otherStandaloneConfigDTO.mqttPassword)) return false;


        if (!mqttProxyUrl.Equals(otherStandaloneConfigDTO.mqttProxyUrl)) return false;


        if (!mqttProxyUsername.Equals(otherStandaloneConfigDTO.mqttProxyUsername)) return false;


        if (!mqttProxyPassword.Equals(otherStandaloneConfigDTO.mqttProxyPassword)) return false;


        if (!mqttPuslishIntervalSec.Equals(otherStandaloneConfigDTO.mqttPuslishIntervalSec)) return false;


        if (!isCloudInterface.Equals(otherStandaloneConfigDTO.isCloudInterface)) return false;


        if (!applyIpSettingsOnStartup.Equals(otherStandaloneConfigDTO.applyIpSettingsOnStartup)) return false;


        if (!ipAddressMode.Equals(otherStandaloneConfigDTO.ipAddressMode)) return false;


        if (!ipAddress.Equals(otherStandaloneConfigDTO.ipAddress)) return false;


        if (!ipMask.Equals(otherStandaloneConfigDTO.ipMask)) return false;


        if (!gatewayAddress.Equals(otherStandaloneConfigDTO.gatewayAddress)) return false;


        if (!broadcastAddress.Equals(otherStandaloneConfigDTO.broadcastAddress)) return false;


        if (!parseSgtinEnabled.Equals(otherStandaloneConfigDTO.parseSgtinEnabled)) return false;


        if (!gtinOutputType.Equals(otherStandaloneConfigDTO.gtinOutputType)) return false;


        if (!parseSgtinIncludeKeyType.Equals(otherStandaloneConfigDTO.parseSgtinIncludeKeyType)) return false;


        if (!parseSgtinIncludeSerial.Equals(otherStandaloneConfigDTO.parseSgtinIncludeSerial)) return false;


        if (!parseSgtinIncludePureIdentity.Equals(otherStandaloneConfigDTO.parseSgtinIncludePureIdentity)) return false;


        if (!httpVerifyPeer.Equals(otherStandaloneConfigDTO.httpVerifyPeer)) return false;


        if (!httpVerifyHost.Equals(otherStandaloneConfigDTO.httpVerifyHost)) return false;


        if (!jsonFormat.Equals(otherStandaloneConfigDTO.jsonFormat)) return false;


        if (!csvFileFormat.Equals(otherStandaloneConfigDTO.csvFileFormat)) return false;


        if (!heartbeatUrl.Equals(otherStandaloneConfigDTO.heartbeatUrl)) return false;


        if (!heartbeatHttpAuthenticationType.Equals(otherStandaloneConfigDTO.heartbeatHttpAuthenticationType))
            return false;


        if (!heartbeatHttpAuthenticationUsername.Equals(otherStandaloneConfigDTO.heartbeatHttpAuthenticationUsername))
            return false;


        if (!heartbeatHttpAuthenticationPassword.Equals(otherStandaloneConfigDTO.heartbeatHttpAuthenticationPassword))
            return false;


        if (!heartbeatHttpAuthenticationTokenApiEnabled.Equals(otherStandaloneConfigDTO
                .heartbeatHttpAuthenticationTokenApiEnabled)) return false;


        if (!heartbeatHttpAuthenticationTokenApiUrl.Equals(otherStandaloneConfigDTO
                .heartbeatHttpAuthenticationTokenApiUrl)) return false;


        if (!heartbeatHttpAuthenticationTokenApiBody.Equals(otherStandaloneConfigDTO
                .heartbeatHttpAuthenticationTokenApiBody)) return false;


        if (!heartbeatHttpAuthenticationTokenApiUsernameField.Equals(otherStandaloneConfigDTO
                .heartbeatHttpAuthenticationTokenApiUsernameField)) return false;


        if (!heartbeatHttpAuthenticationTokenApiUsernameValue.Equals(otherStandaloneConfigDTO
                .heartbeatHttpAuthenticationTokenApiUsernameValue)) return false;


        if (!heartbeatHttpAuthenticationTokenApiPasswordField.Equals(otherStandaloneConfigDTO
                .heartbeatHttpAuthenticationTokenApiPasswordField)) return false;


        if (!heartbeatHttpAuthenticationTokenApiPasswordValue.Equals(otherStandaloneConfigDTO
                .heartbeatHttpAuthenticationTokenApiPasswordValue)) return false;


        if (!heartbeatHttpAuthenticationTokenApiValue.Equals(otherStandaloneConfigDTO
                .heartbeatHttpAuthenticationTokenApiValue)) return false;


        if (!httpAuthenticationTokenApiUsernameField.Equals(otherStandaloneConfigDTO
                .httpAuthenticationTokenApiUsernameField)) return false;


        if (!httpAuthenticationTokenApiUsernameValue.Equals(otherStandaloneConfigDTO
                .httpAuthenticationTokenApiUsernameValue)) return false;


        if (!httpAuthenticationTokenApiPasswordField.Equals(otherStandaloneConfigDTO
                .httpAuthenticationTokenApiPasswordField)) return false;


        if (!httpAuthenticationTokenApiPasswordValue.Equals(otherStandaloneConfigDTO
                .httpAuthenticationTokenApiPasswordValue)) return false;


        if (!toiValidationEnabled.Equals(otherStandaloneConfigDTO.toiValidationEnabled)) return false;


        if (!toiValidationUrl.Equals(otherStandaloneConfigDTO.toiValidationUrl)) return false;


        if (!toiValidationGpoDuration.Equals(otherStandaloneConfigDTO.toiValidationGpoDuration)) return false;


        if (!toiGpoOk.Equals(otherStandaloneConfigDTO.toiGpoOk)) return false;


        if (!toiGpoNok.Equals(otherStandaloneConfigDTO.toiGpoNok)) return false;


        if (!toiGpoError.Equals(otherStandaloneConfigDTO.toiGpoError)) return false;


        if (!toiGpi.Equals(otherStandaloneConfigDTO.toiGpi)) return false;


        if (!toiGpoPriority.Equals(otherStandaloneConfigDTO.toiGpoPriority)) return false;


        if (!toiGpoMode.Equals(otherStandaloneConfigDTO.toiGpoMode)) return false;


        if (!customField1Enabled.Equals(otherStandaloneConfigDTO.customField1Enabled)) return false;


        if (!customField1Name.Equals(otherStandaloneConfigDTO.customField1Name)) return false;


        if (!customField1Value.Equals(otherStandaloneConfigDTO.customField1Value)) return false;


        if (!customField2Enabled.Equals(otherStandaloneConfigDTO.customField2Enabled)) return false;


        if (!customField2Name.Equals(otherStandaloneConfigDTO.customField2Name)) return false;


        if (!customField2Value.Equals(otherStandaloneConfigDTO.customField2Value)) return false;


        if (!customField3Enabled.Equals(otherStandaloneConfigDTO.customField3Enabled)) return false;


        if (!customField3Name.Equals(otherStandaloneConfigDTO.customField3Name)) return false;


        if (!customField3Value.Equals(otherStandaloneConfigDTO.customField3Value)) return false;


        if (!customField4Enabled.Equals(otherStandaloneConfigDTO.customField4Enabled)) return false;


        if (!customField4Name.Equals(otherStandaloneConfigDTO.customField4Name)) return false;


        if (!customField4Value.Equals(otherStandaloneConfigDTO.customField4Value)) return false;


        if (!writeUsbJson.Equals(otherStandaloneConfigDTO.writeUsbJson)) return false;


        if (!reportingIntervalSeconds.Equals(otherStandaloneConfigDTO.reportingIntervalSeconds)) return false;


        if (!tagCacheSize.Equals(otherStandaloneConfigDTO.tagCacheSize)) return false;


        if (!antennaIdentifier.Equals(otherStandaloneConfigDTO.antennaIdentifier)) return false;


        if (!tagIdentifier.Equals(otherStandaloneConfigDTO.tagIdentifier)) return false;


        if (!positioningEpcsEnabled.Equals(otherStandaloneConfigDTO.positioningEpcsEnabled)) return false;


        if (!positioningAntennaPorts.Equals(otherStandaloneConfigDTO.positioningAntennaPorts)) return false;


        if (!positioningEpcsHeaderList.Equals(otherStandaloneConfigDTO.positioningEpcsHeaderList)) return false;


        if (!positioningEpcsFilter.Equals(otherStandaloneConfigDTO.positioningEpcsFilter)) return false;


        if (!positioningExpirationInSec.Equals(otherStandaloneConfigDTO.positioningExpirationInSec)) return false;


        if (!positioningReportIntervalInSec.Equals(otherStandaloneConfigDTO.positioningReportIntervalInSec))
            return false;


        if (!enableUniqueTagRead.Equals(otherStandaloneConfigDTO.enableUniqueTagRead)) return false;


        if (!enableAntennaTask.Equals(otherStandaloneConfigDTO.enableAntennaTask)) return false;


        if (!packageHeaders.Equals(otherStandaloneConfigDTO.packageHeaders)) return false;


        if (!enablePartialValidation.Equals(otherStandaloneConfigDTO.enablePartialValidation)) return false;


        if (!validationAcceptanceThreshold.Equals(otherStandaloneConfigDTO.validationAcceptanceThreshold)) return false;


        if (!validationAcceptanceThresholdTimeout.Equals(otherStandaloneConfigDTO.validationAcceptanceThresholdTimeout))
            return false;


        if (!publishFullShipmentValidationListOnAcceptanceThreshold.Equals(otherStandaloneConfigDTO
                .publishFullShipmentValidationListOnAcceptanceThreshold)) return false;


        if (!publishSingleTimeOnAcceptanceThreshold.Equals(otherStandaloneConfigDTO
                .publishSingleTimeOnAcceptanceThreshold)) return false;


        if (!readerSerial.Equals(otherStandaloneConfigDTO.readerSerial)) return false;


        if (!activePlugin.Equals(otherStandaloneConfigDTO.activePlugin)) return false;


        if (!pluginServer.Equals(otherStandaloneConfigDTO.pluginServer)) return false;


        if (!enablePluginShipmentVerification.Equals(otherStandaloneConfigDTO.enablePluginShipmentVerification))
            return false;


        if (!softwareFilterIncludeEpcsHeaderListEnabled.Equals(otherStandaloneConfigDTO
                .softwareFilterIncludeEpcsHeaderListEnabled)) return false;


        if (!softwareFilterIncludeEpcsHeaderList.Equals(otherStandaloneConfigDTO.softwareFilterIncludeEpcsHeaderList))
            return false;


        if (!isLogFileEnabled.Equals(otherStandaloneConfigDTO.isLogFileEnabled)) return false;


        if (!rciSpotReportEnabled.Equals(otherStandaloneConfigDTO.rciSpotReportEnabled)) return false;


        if (!rciSpotReportIncludePc.Equals(otherStandaloneConfigDTO.rciSpotReportIncludePc)) return false;


        if (!rciSpotReportIncludeScheme.Equals(otherStandaloneConfigDTO.rciSpotReportIncludeScheme)) return false;


        if (!rciSpotReportIncludeEpcUri.Equals(otherStandaloneConfigDTO.rciSpotReportIncludeEpcUri)) return false;


        if (!rciSpotReportIncludeAnt.Equals(otherStandaloneConfigDTO.rciSpotReportIncludeAnt)) return false;


        if (!rciSpotReportIncludeDwnCnt.Equals(otherStandaloneConfigDTO.rciSpotReportIncludeDwnCnt)) return false;


        if (!rciSpotReportIncludeInvCnt.Equals(otherStandaloneConfigDTO.rciSpotReportIncludeInvCnt)) return false;


        if (!rciSpotReportIncludePhase.Equals(otherStandaloneConfigDTO.rciSpotReportIncludePhase)) return false;


        if (!rciSpotReportIncludeProf.Equals(otherStandaloneConfigDTO.rciSpotReportIncludeProf)) return false;


        if (!rciSpotReportIncludeRange.Equals(otherStandaloneConfigDTO.rciSpotReportIncludeRange)) return false;


        if (!rciSpotReportIncludeRssi.Equals(otherStandaloneConfigDTO.rciSpotReportIncludeRssi)) return false;


        if (!rciSpotReportIncludeRz.Equals(otherStandaloneConfigDTO.rciSpotReportIncludeRz)) return false;


        if (!rciSpotReportIncludeSpot.Equals(otherStandaloneConfigDTO.rciSpotReportIncludeSpot)) return false;


        if (!rciSpotReportIncludeTimeStamp.Equals(otherStandaloneConfigDTO.rciSpotReportIncludeTimeStamp)) return false;


        if (!enableOpcUaClient.Equals(otherStandaloneConfigDTO.enableOpcUaClient)) return false;


        if (!opcUaConnectionName.Equals(otherStandaloneConfigDTO.opcUaConnectionName)) return false;


        if (!opcUaConnectionPublisherId.Equals(otherStandaloneConfigDTO.opcUaConnectionPublisherId)) return false;


        if (!opcUaConnectionUrl.Equals(otherStandaloneConfigDTO.opcUaConnectionUrl)) return false;


        if (!opcUaConnectionDiscoveryAddress.Equals(otherStandaloneConfigDTO.opcUaConnectionDiscoveryAddress))
            return false;


        if (!opcUaWriterGroupName.Equals(otherStandaloneConfigDTO.opcUaWriterGroupName)) return false;


        if (!opcUaWriterGroupId.Equals(otherStandaloneConfigDTO.opcUaWriterGroupId)) return false;


        if (!opcUaWriterPublishingInterval.Equals(otherStandaloneConfigDTO.opcUaWriterPublishingInterval)) return false;


        if (!opcUaWriterKeepAliveTime.Equals(otherStandaloneConfigDTO.opcUaWriterKeepAliveTime)) return false;


        if (!opcUaWriterMaxNetworkMessageSize.Equals(otherStandaloneConfigDTO.opcUaWriterMaxNetworkMessageSize))
            return false;


        if (!opcUaWriterHeaderLayoutUri.Equals(otherStandaloneConfigDTO.opcUaWriterHeaderLayoutUri)) return false;


        if (!opcUaDataSetWriterName.Equals(otherStandaloneConfigDTO.opcUaDataSetWriterName)) return false;


        if (!opcUaDataSetWriterId.Equals(otherStandaloneConfigDTO.opcUaDataSetWriterId)) return false;


        if (!opcUaDataSetName.Equals(otherStandaloneConfigDTO.opcUaDataSetName)) return false;


        if (!opcUaDataSetKeyFrameCount.Equals(otherStandaloneConfigDTO.opcUaDataSetKeyFrameCount)) return false;


        if (!enablePlugin.Equals(otherStandaloneConfigDTO.enablePlugin)) return false;

        if (!enableBarcodeTcp.Equals(otherStandaloneConfigDTO.enableBarcodeTcp)) return false;

        if (!enableBarcodeSerial.Equals(otherStandaloneConfigDTO.enableBarcodeSerial)) return false;

        if (!enableBarcodeSerial.Equals(otherStandaloneConfigDTO.enableBarcodeSerial)) return false;


        if (!groupEventsOnInventoryStatus.Equals(otherStandaloneConfigDTO.groupEventsOnInventoryStatus)) return false;

        if (!barcodeTcpAddress.Equals(otherStandaloneConfigDTO.barcodeTcpAddress)) return false;

        if (!barcodeTcpPort.Equals(otherStandaloneConfigDTO.barcodeTcpPort)) return false;

        if (!barcodeTcpLen.Equals(otherStandaloneConfigDTO.barcodeTcpLen)) return false;

        if (!barcodeEnableQueue.Equals(otherStandaloneConfigDTO.barcodeEnableQueue)) return false;


        if (!barcodeTcpNoDataString.Equals(otherStandaloneConfigDTO.barcodeTcpNoDataString)) return false;

        if (!barcodeProcessNoDataString.Equals(otherStandaloneConfigDTO.barcodeProcessNoDataString)) return false;


        if (!httpAuthenticationHeader.Equals(otherStandaloneConfigDTO.httpAuthenticationHeader)) return false;

        if (!httpAuthenticationHeaderValue.Equals(otherStandaloneConfigDTO.httpAuthenticationHeaderValue)) return false;

        if (!enableValidation.Equals(otherStandaloneConfigDTO.enableValidation)) return false;

        if (!requireUniqueProductCode.Equals(otherStandaloneConfigDTO.requireUniqueProductCode)) return false;

        if (!enableTagEventStream.Equals(otherStandaloneConfigDTO.enableTagEventStream)) return false;

        if (!enableSummaryStream.Equals(otherStandaloneConfigDTO.enableSummaryStream)) return false;

        if (!enableExternalApiVerification.Equals(otherStandaloneConfigDTO.enableExternalApiVerification)) return false;

        if (!externalApiVerificationSearchOrderUrl.Equals(
                otherStandaloneConfigDTO.externalApiVerificationSearchOrderUrl)) return false;

        if (!externalApiVerificationSearchProductUrl.Equals(otherStandaloneConfigDTO
                .externalApiVerificationSearchProductUrl)) return false;

        if (!externalApiVerificationPublishDataUrl.Equals(
                otherStandaloneConfigDTO.externalApiVerificationPublishDataUrl)) return false;

        if (!externalApiVerificationHttpHeaderName.Equals(
                otherStandaloneConfigDTO.externalApiVerificationHttpHeaderName)) return false;

        if (!externalApiVerificationHttpHeaderValue.Equals(otherStandaloneConfigDTO
                .externalApiVerificationHttpHeaderValue)) return false;

        if (!operatingRegion.Equals(otherStandaloneConfigDTO
                .operatingRegion)) return false;

        if (!systemImageUpgradeUrl.Equals(otherStandaloneConfigDTO
                .systemImageUpgradeUrl)) return false;


        //enableSummaryStream

        //
        //httpAuthenticationHeader

        return true;
    }

    public static StandaloneConfigDTO CleanupUrlEncoding(StandaloneConfigDTO config)
    {
        try
        {
            config.id = HttpUtility.UrlDecode(config.id);

            config.readerName = HttpUtility.UrlDecode(config.readerName);

            config.serial = HttpUtility.UrlDecode(config.serial);

            config.isEnabled = HttpUtility.UrlDecode(config.isEnabled);

            config.profileName = HttpUtility.UrlDecode(config.profileName);

            config.isCurrentProfile = HttpUtility.UrlDecode(config.isCurrentProfile);

            config.antennaPorts = HttpUtility.UrlDecode(config.antennaPorts);

            config.antennaStates = HttpUtility.UrlDecode(config.antennaStates);

            config.antennaZones = HttpUtility.UrlDecode(config.antennaZones);

            config.transmitPower = HttpUtility.UrlDecode(config.transmitPower);

            config.receiveSensitivity = HttpUtility.UrlDecode(config.receiveSensitivity);

            config.readerMode = HttpUtility.UrlDecode(config.readerMode);

            config.searchMode = HttpUtility.UrlDecode(config.searchMode);

            config.session = HttpUtility.UrlDecode(config.session);

            config.tagPopulation = HttpUtility.UrlDecode(config.tagPopulation);

            config.startTriggerType = HttpUtility.UrlDecode(config.startTriggerType);

            config.startTriggerPeriod = HttpUtility.UrlDecode(config.startTriggerPeriod);

            config.startTriggerOffset = HttpUtility.UrlDecode(config.startTriggerOffset);

            config.startTriggerUTCTimestamp = HttpUtility.UrlDecode(config.startTriggerUTCTimestamp);

            config.startTriggerGpiEvent = HttpUtility.UrlDecode(config.startTriggerGpiEvent);

            config.startTriggerGpiPort = HttpUtility.UrlDecode(config.startTriggerGpiPort);

            config.stopTriggerType = HttpUtility.UrlDecode(config.stopTriggerType);

            config.stopTriggerDuration = HttpUtility.UrlDecode(config.stopTriggerDuration);

            config.stopTriggerTimeout = HttpUtility.UrlDecode(config.stopTriggerTimeout);

            config.stopTriggerGpiEvent = HttpUtility.UrlDecode(config.stopTriggerGpiEvent);

            config.stopTriggerGpiPort = HttpUtility.UrlDecode(config.stopTriggerGpiPort);

            config.socketServer = HttpUtility.UrlDecode(config.socketServer);

            config.socketPort = HttpUtility.UrlDecode(config.socketPort);

            config.udpServer = HttpUtility.UrlDecode(config.udpServer);

            config.udpIpAddress = HttpUtility.UrlDecode(config.udpIpAddress);

            config.udpReaderPort = HttpUtility.UrlDecode(config.udpReaderPort);

            config.udpRemoteIpAddress = HttpUtility.UrlDecode(config.udpRemoteIpAddress);

            config.udpRemotePort = HttpUtility.UrlDecode(config.udpRemotePort);

            config.serialPort = HttpUtility.UrlDecode(config.serialPort);

            config.usbHid = HttpUtility.UrlDecode(config.usbHid);

            config.lineEnd = HttpUtility.UrlDecode(config.lineEnd);

            config.fieldDelim = HttpUtility.UrlDecode(config.fieldDelim);

            config.softwareFilterEnabled = HttpUtility.UrlDecode(config.softwareFilterEnabled);

            config.softwareFilterWindowSec = HttpUtility.UrlDecode(config.softwareFilterWindowSec);

            config.softwareFilterField = HttpUtility.UrlDecode(config.softwareFilterField);

            config.softwareFilterReadCountTimeoutEnabled =
                HttpUtility.UrlDecode(config.softwareFilterReadCountTimeoutEnabled);

            config.softwareFilterReadCountTimeoutSeenCount =
                HttpUtility.UrlDecode(config.softwareFilterReadCountTimeoutSeenCount);

            config.softwareFilterReadCountTimeoutIntervalInSec =
                HttpUtility.UrlDecode(config.softwareFilterReadCountTimeoutIntervalInSec);

            config.includeReaderName = HttpUtility.UrlDecode(config.includeReaderName);

            config.includeAntennaPort = HttpUtility.UrlDecode(config.includeAntennaPort);

            config.includeAntennaZone = HttpUtility.UrlDecode(config.includeAntennaZone);

            config.includeFirstSeenTimestamp = HttpUtility.UrlDecode(config.includeFirstSeenTimestamp);

            config.includePeakRssi = HttpUtility.UrlDecode(config.includePeakRssi);

            config.includeRFPhaseAngle = HttpUtility.UrlDecode(config.includeRFPhaseAngle);

            config.includeRFDopplerFrequency = HttpUtility.UrlDecode(config.includeRFDopplerFrequency);

            config.includeRFChannelIndex = HttpUtility.UrlDecode(config.includeRFChannelIndex);

            config.includeGpiEvent = HttpUtility.UrlDecode(config.includeGpiEvent);

            config.includeInventoryStatusEvent = HttpUtility.UrlDecode(config.includeInventoryStatusEvent);

            config.includeInventoryStatusEventId = HttpUtility.UrlDecode(config.includeInventoryStatusEventId);

            config.includeInventoryStatusEventTotalCount =
                HttpUtility.UrlDecode(config.includeInventoryStatusEventTotalCount);

            config.includeTid = HttpUtility.UrlDecode(config.includeTid);

            config.tidWordStart = HttpUtility.UrlDecode(config.tidWordStart);

            config.tidWordCount = HttpUtility.UrlDecode(config.tidWordCount);

            config.includeUserMemory = HttpUtility.UrlDecode(config.includeUserMemory);

            config.userMemoryWordStart = HttpUtility.UrlDecode(config.userMemoryWordStart);

            config.userMemoryWordCount = HttpUtility.UrlDecode(config.userMemoryWordCount);

            config.siteEnabled = HttpUtility.UrlDecode(config.siteEnabled);

            config.site = HttpUtility.UrlDecode(config.site);

            config.httpPostEnabled = HttpUtility.UrlDecode(config.httpPostEnabled);

            config.httpPostType = HttpUtility.UrlDecode(config.httpPostType);

            config.httpPostIntervalSec = HttpUtility.UrlDecode(config.httpPostIntervalSec);

            config.httpPostURL = HttpUtility.UrlDecode(config.httpPostURL);

            config.httpAuthenticationType = HttpUtility.UrlDecode(config.httpAuthenticationType);

            config.httpAuthenticationUsername = HttpUtility.UrlDecode(config.httpAuthenticationUsername);

            config.httpAuthenticationPassword = HttpUtility.UrlDecode(config.httpAuthenticationPassword);

            config.httpAuthenticationTokenApiEnabled = HttpUtility.UrlDecode(config.httpAuthenticationTokenApiEnabled);

            config.httpAuthenticationTokenApiUrl = HttpUtility.UrlDecode(config.httpAuthenticationTokenApiUrl);

            config.httpAuthenticationTokenApiBody = HttpUtility.UrlDecode(config.httpAuthenticationTokenApiBody);

            config.httpAuthenticationTokenApiValue = HttpUtility.UrlDecode(config.httpAuthenticationTokenApiValue);

            config.httpVerifyPostHttpReturnCode = HttpUtility.UrlDecode(config.httpVerifyPostHttpReturnCode);

            config.truncateEpc = HttpUtility.UrlDecode(config.truncateEpc);

            config.truncateStart = HttpUtility.UrlDecode(config.truncateStart);

            config.truncateLen = HttpUtility.UrlDecode(config.truncateLen);

            config.advancedGpoEnabled = HttpUtility.UrlDecode(config.advancedGpoEnabled);

            config.advancedGpoMode1 = HttpUtility.UrlDecode(config.advancedGpoMode1);

            config.advancedGpoMode2 = HttpUtility.UrlDecode(config.advancedGpoMode2);

            config.advancedGpoMode3 = HttpUtility.UrlDecode(config.advancedGpoMode3);

            config.advancedGpoMode4 = HttpUtility.UrlDecode(config.advancedGpoMode4);

            config.heartbeatEnabled = HttpUtility.UrlDecode(config.heartbeatEnabled);

            config.heartbeatPeriodSec = HttpUtility.UrlDecode(config.heartbeatPeriodSec);

            config.usbFlashDrive = HttpUtility.UrlDecode(config.usbFlashDrive);

            config.lowDutyCycleEnabled = HttpUtility.UrlDecode(config.lowDutyCycleEnabled);

            config.emptyFieldTimeout = HttpUtility.UrlDecode(config.emptyFieldTimeout);

            config.fieldPingInterval = HttpUtility.UrlDecode(config.fieldPingInterval);

            config.baudRate = HttpUtility.UrlDecode(config.baudRate);

            config.c1g2FilterEnabled = HttpUtility.UrlDecode(config.c1g2FilterEnabled);

            config.c1g2FilterBank = HttpUtility.UrlDecode(config.c1g2FilterBank);

            config.c1g2FilterPointer = HttpUtility.UrlDecode(config.c1g2FilterPointer);

            config.c1g2FilterMask = HttpUtility.UrlDecode(config.c1g2FilterMask);

            config.c1g2FilterLen = HttpUtility.UrlDecode(config.c1g2FilterLen);

            config.dataPrefix = HttpUtility.UrlDecode(config.dataPrefix);

            config.dataSuffix = HttpUtility.UrlDecode(config.dataSuffix);

            config.backupToFlashDriveOnGpiEventEnabled =
                HttpUtility.UrlDecode(config.backupToFlashDriveOnGpiEventEnabled);

            config.maxTxPowerOnGpiEventEnabled = HttpUtility.UrlDecode(config.maxTxPowerOnGpiEventEnabled);

            config.backupToInternalFlashEnabled = HttpUtility.UrlDecode(config.backupToInternalFlashEnabled);

            config.tagValidationEnabled = HttpUtility.UrlDecode(config.tagValidationEnabled);

            config.keepFilenameOnDayChange = HttpUtility.UrlDecode(config.keepFilenameOnDayChange);

            config.promptBeforeChanging = HttpUtility.UrlDecode(config.promptBeforeChanging);

            config.connectionStatus = HttpUtility.UrlDecode(config.connectionStatus);

            config.mqttEnabled = HttpUtility.UrlDecode(config.mqttEnabled);

            config.mqttUseSsl = HttpUtility.UrlDecode(config.mqttUseSsl);

            config.mqttSslCaCertificate = HttpUtility.UrlDecode(config.mqttSslCaCertificate);

            config.mqttSslClientCertificate = HttpUtility.UrlDecode(config.mqttSslClientCertificate);

            config.mqttBrokerAddress = HttpUtility.UrlDecode(config.mqttBrokerAddress);

            config.mqttBrokerName = HttpUtility.UrlDecode(config.mqttBrokerName);

            config.mqttBrokerDescription = HttpUtility.UrlDecode(config.mqttBrokerDescription);

            config.mqttBrokerType = HttpUtility.UrlDecode(config.mqttBrokerType);

            config.mqttBrokerProtocol = HttpUtility.UrlDecode(config.mqttBrokerProtocol);

            config.mqttBrokerCleanSession = HttpUtility.UrlDecode(config.mqttBrokerCleanSession);

            config.mqttBrokerKeepAlive = HttpUtility.UrlDecode(config.mqttBrokerKeepAlive);

            config.mqttBrokerDebug = HttpUtility.UrlDecode(config.mqttBrokerDebug);

            config.mqttBrokerPort = HttpUtility.UrlDecode(config.mqttBrokerPort);

            config.mqttTagEventsTopic = HttpUtility.UrlDecode(config.mqttTagEventsTopic);

            config.mqttTagEventsQoS = HttpUtility.UrlDecode(config.mqttTagEventsQoS);

            config.mqttTagEventsRetainMessages = HttpUtility.UrlDecode(config.mqttTagEventsRetainMessages);

            config.mqttManagementEventsTopic = HttpUtility.UrlDecode(config.mqttManagementEventsTopic);

            config.mqttManagementEventsQoS = HttpUtility.UrlDecode(config.mqttManagementEventsQoS);

            config.mqttManagementEventsRetainMessages =
                HttpUtility.UrlDecode(config.mqttManagementEventsRetainMessages);

            config.mqttMetricEventsTopic = HttpUtility.UrlDecode(config.mqttMetricEventsTopic);

            config.mqttMetricEventsQoS = HttpUtility.UrlDecode(config.mqttMetricEventsQoS);

            config.mqttMetricEventsRetainMessages = HttpUtility.UrlDecode(config.mqttMetricEventsRetainMessages);

            config.mqttManagementCommandTopic = HttpUtility.UrlDecode(config.mqttManagementCommandTopic);

            config.mqttManagementCommandQoS = HttpUtility.UrlDecode(config.mqttManagementCommandQoS);

            config.mqttManagementCommandRetainMessages =
                HttpUtility.UrlDecode(config.mqttManagementCommandRetainMessages);

            config.mqttManagementResponseTopic = HttpUtility.UrlDecode(config.mqttManagementResponseTopic);

            config.mqttManagementResponseQoS = HttpUtility.UrlDecode(config.mqttManagementResponseQoS);

            config.mqttManagementResponseRetainMessages =
                HttpUtility.UrlDecode(config.mqttManagementResponseRetainMessages);

            config.mqttControlCommandTopic = HttpUtility.UrlDecode(config.mqttControlCommandTopic);

            config.mqttControlCommandQoS = HttpUtility.UrlDecode(config.mqttControlCommandQoS);

            config.mqttControlCommandRetainMessages = HttpUtility.UrlDecode(config.mqttControlCommandRetainMessages);

            config.mqttControlResponseTopic = HttpUtility.UrlDecode(config.mqttControlResponseTopic);

            config.mqttControlResponseQoS = HttpUtility.UrlDecode(config.mqttControlResponseQoS);

            config.mqttControlResponseRetainMessages = HttpUtility.UrlDecode(config.mqttControlResponseRetainMessages);

            config.mqttUsername = HttpUtility.UrlDecode(config.mqttUsername);

            config.mqttPassword = HttpUtility.UrlDecode(config.mqttPassword);

            config.mqttProxyUrl = HttpUtility.UrlDecode(config.mqttProxyUrl);

            config.mqttProxyUsername = HttpUtility.UrlDecode(config.mqttProxyUsername);

            config.mqttProxyPassword = HttpUtility.UrlDecode(config.mqttProxyPassword);

            config.mqttPuslishIntervalSec = HttpUtility.UrlDecode(config.mqttPuslishIntervalSec);

            config.isCloudInterface = HttpUtility.UrlDecode(config.isCloudInterface);

            config.applyIpSettingsOnStartup = HttpUtility.UrlDecode(config.applyIpSettingsOnStartup);

            config.ipAddressMode = HttpUtility.UrlDecode(config.ipAddressMode);

            config.ipAddress = HttpUtility.UrlDecode(config.ipAddress);

            config.ipMask = HttpUtility.UrlDecode(config.ipMask);

            config.gatewayAddress = HttpUtility.UrlDecode(config.gatewayAddress);

            config.broadcastAddress = HttpUtility.UrlDecode(config.broadcastAddress);

            config.parseSgtinEnabled = HttpUtility.UrlDecode(config.parseSgtinEnabled);

            config.gtinOutputType = HttpUtility.UrlDecode(config.gtinOutputType);

            config.parseSgtinIncludeKeyType = HttpUtility.UrlDecode(config.parseSgtinIncludeKeyType);

            config.parseSgtinIncludeSerial = HttpUtility.UrlDecode(config.parseSgtinIncludeSerial);

            config.parseSgtinIncludePureIdentity = HttpUtility.UrlDecode(config.parseSgtinIncludePureIdentity);

            config.httpVerifyPeer = HttpUtility.UrlDecode(config.httpVerifyPeer);

            config.httpVerifyHost = HttpUtility.UrlDecode(config.httpVerifyHost);

            config.jsonFormat = HttpUtility.UrlDecode(config.jsonFormat);

            config.csvFileFormat = HttpUtility.UrlDecode(config.csvFileFormat);

            config.heartbeatUrl = HttpUtility.UrlDecode(config.heartbeatUrl);

            config.heartbeatHttpAuthenticationType = HttpUtility.UrlDecode(config.heartbeatHttpAuthenticationType);

            config.heartbeatHttpAuthenticationUsername =
                HttpUtility.UrlDecode(config.heartbeatHttpAuthenticationUsername);

            config.heartbeatHttpAuthenticationPassword =
                HttpUtility.UrlDecode(config.heartbeatHttpAuthenticationPassword);

            config.heartbeatHttpAuthenticationTokenApiEnabled =
                HttpUtility.UrlDecode(config.heartbeatHttpAuthenticationTokenApiEnabled);

            config.heartbeatHttpAuthenticationTokenApiUrl =
                HttpUtility.UrlDecode(config.heartbeatHttpAuthenticationTokenApiUrl);

            config.heartbeatHttpAuthenticationTokenApiBody =
                HttpUtility.UrlDecode(config.heartbeatHttpAuthenticationTokenApiBody);

            config.heartbeatHttpAuthenticationTokenApiUsernameField =
                HttpUtility.UrlDecode(config.heartbeatHttpAuthenticationTokenApiUsernameField);

            config.heartbeatHttpAuthenticationTokenApiUsernameValue =
                HttpUtility.UrlDecode(config.heartbeatHttpAuthenticationTokenApiUsernameValue);

            config.heartbeatHttpAuthenticationTokenApiPasswordField =
                HttpUtility.UrlDecode(config.heartbeatHttpAuthenticationTokenApiPasswordField);

            config.heartbeatHttpAuthenticationTokenApiPasswordValue =
                HttpUtility.UrlDecode(config.heartbeatHttpAuthenticationTokenApiPasswordValue);

            config.heartbeatHttpAuthenticationTokenApiValue =
                HttpUtility.UrlDecode(config.heartbeatHttpAuthenticationTokenApiValue);

            config.httpAuthenticationTokenApiUsernameField =
                HttpUtility.UrlDecode(config.httpAuthenticationTokenApiUsernameField);

            config.httpAuthenticationTokenApiUsernameValue =
                HttpUtility.UrlDecode(config.httpAuthenticationTokenApiUsernameValue);

            config.httpAuthenticationTokenApiPasswordField =
                HttpUtility.UrlDecode(config.httpAuthenticationTokenApiPasswordField);

            config.httpAuthenticationTokenApiPasswordValue =
                HttpUtility.UrlDecode(config.httpAuthenticationTokenApiPasswordValue);

            config.toiValidationEnabled = HttpUtility.UrlDecode(config.toiValidationEnabled);

            config.toiValidationUrl = HttpUtility.UrlDecode(config.toiValidationUrl);

            config.toiValidationGpoDuration = HttpUtility.UrlDecode(config.toiValidationGpoDuration);

            config.toiGpoOk = HttpUtility.UrlDecode(config.toiGpoOk);

            config.toiGpoNok = HttpUtility.UrlDecode(config.toiGpoNok);

            config.toiGpoError = HttpUtility.UrlDecode(config.toiGpoError);

            config.toiGpi = HttpUtility.UrlDecode(config.toiGpi);

            config.toiGpoPriority = HttpUtility.UrlDecode(config.toiGpoPriority);

            config.toiGpoMode = HttpUtility.UrlDecode(config.toiGpoMode);

            config.customField1Enabled = HttpUtility.UrlDecode(config.customField1Enabled);

            config.customField1Name = HttpUtility.UrlDecode(config.customField1Name);

            config.customField1Value = HttpUtility.UrlDecode(config.customField1Value);

            config.customField2Enabled = HttpUtility.UrlDecode(config.customField2Enabled);

            config.customField2Name = HttpUtility.UrlDecode(config.customField2Name);

            config.customField2Value = HttpUtility.UrlDecode(config.customField2Value);

            config.customField3Enabled = HttpUtility.UrlDecode(config.customField3Enabled);

            config.customField3Name = HttpUtility.UrlDecode(config.customField3Name);

            config.customField3Value = HttpUtility.UrlDecode(config.customField3Value);

            config.customField4Enabled = HttpUtility.UrlDecode(config.customField4Enabled);

            config.customField4Name = HttpUtility.UrlDecode(config.customField4Name);

            config.customField4Value = HttpUtility.UrlDecode(config.customField4Value);

            config.writeUsbJson = HttpUtility.UrlDecode(config.writeUsbJson);

            config.reportingIntervalSeconds = HttpUtility.UrlDecode(config.reportingIntervalSeconds);

            config.tagCacheSize = HttpUtility.UrlDecode(config.tagCacheSize);

            config.antennaIdentifier = HttpUtility.UrlDecode(config.antennaIdentifier);

            config.tagIdentifier = HttpUtility.UrlDecode(config.tagIdentifier);

            config.positioningEpcsEnabled = HttpUtility.UrlDecode(config.positioningEpcsEnabled);

            config.positioningAntennaPorts = HttpUtility.UrlDecode(config.positioningAntennaPorts);

            config.positioningEpcsHeaderList = HttpUtility.UrlDecode(config.positioningEpcsHeaderList);

            config.positioningEpcsFilter = HttpUtility.UrlDecode(config.positioningEpcsFilter);

            config.positioningExpirationInSec = HttpUtility.UrlDecode(config.positioningExpirationInSec);

            config.positioningReportIntervalInSec = HttpUtility.UrlDecode(config.positioningReportIntervalInSec);

            config.enableUniqueTagRead = HttpUtility.UrlDecode(config.enableUniqueTagRead);

            config.enableAntennaTask = HttpUtility.UrlDecode(config.enableAntennaTask);

            config.packageHeaders = HttpUtility.UrlDecode(config.packageHeaders);

            config.enablePartialValidation = HttpUtility.UrlDecode(config.enablePartialValidation);

            config.validationAcceptanceThreshold = HttpUtility.UrlDecode(config.validationAcceptanceThreshold);

            config.validationAcceptanceThresholdTimeout =
                HttpUtility.UrlDecode(config.validationAcceptanceThresholdTimeout);

            config.readerSerial = HttpUtility.UrlDecode(config.readerSerial);

            config.activePlugin = HttpUtility.UrlDecode(config.activePlugin);

            config.enablePluginShipmentVerification = HttpUtility.UrlDecode(config.enablePluginShipmentVerification);

            config.pluginServer = HttpUtility.UrlDecode(config.pluginServer);

            config.licenseKey = HttpUtility.UrlDecode(config.licenseKey);

            config.softwareFilterIncludeEpcsHeaderListEnabled =
                HttpUtility.UrlDecode(config.softwareFilterIncludeEpcsHeaderListEnabled);

            config.softwareFilterIncludeEpcsHeaderList =
                HttpUtility.UrlDecode(config.softwareFilterIncludeEpcsHeaderList);

            config.isLogFileEnabled = HttpUtility.UrlDecode(config.isLogFileEnabled);

            config.rciSpotReportEnabled = HttpUtility.UrlDecode(config.rciSpotReportEnabled);

            config.rciSpotReportIncludePc = HttpUtility.UrlDecode(config.rciSpotReportIncludePc);

            config.rciSpotReportIncludeScheme = HttpUtility.UrlDecode(config.rciSpotReportIncludeScheme);

            config.rciSpotReportIncludeEpcUri = HttpUtility.UrlDecode(config.rciSpotReportIncludeEpcUri);

            config.rciSpotReportIncludeAnt = HttpUtility.UrlDecode(config.rciSpotReportIncludeAnt);

            config.rciSpotReportIncludeDwnCnt = HttpUtility.UrlDecode(config.rciSpotReportIncludeDwnCnt);

            config.rciSpotReportIncludeInvCnt = HttpUtility.UrlDecode(config.rciSpotReportIncludeInvCnt);

            config.rciSpotReportIncludePhase = HttpUtility.UrlDecode(config.rciSpotReportIncludePhase);

            config.rciSpotReportIncludeProf = HttpUtility.UrlDecode(config.rciSpotReportIncludeProf);

            config.rciSpotReportIncludeRange = HttpUtility.UrlDecode(config.rciSpotReportIncludeRange);

            config.rciSpotReportIncludeRssi = HttpUtility.UrlDecode(config.rciSpotReportIncludeRssi);

            config.rciSpotReportIncludeRz = HttpUtility.UrlDecode(config.rciSpotReportIncludeRz);

            config.rciSpotReportIncludeSpot = HttpUtility.UrlDecode(config.rciSpotReportIncludeSpot);

            config.rciSpotReportIncludeTimeStamp = HttpUtility.UrlDecode(config.rciSpotReportIncludeTimeStamp);

            config.enableOpcUaClient = HttpUtility.UrlDecode(config.enableOpcUaClient);

            config.opcUaConnectionName = HttpUtility.UrlDecode(config.opcUaConnectionName);

            config.opcUaConnectionPublisherId = HttpUtility.UrlDecode(config.opcUaConnectionPublisherId);

            config.opcUaConnectionUrl = HttpUtility.UrlDecode(config.opcUaConnectionUrl);

            config.opcUaConnectionDiscoveryAddress = HttpUtility.UrlDecode(config.opcUaConnectionDiscoveryAddress);

            config.opcUaWriterGroupName = HttpUtility.UrlDecode(config.opcUaWriterGroupName);

            config.opcUaWriterGroupId = HttpUtility.UrlDecode(config.opcUaWriterGroupId);

            config.opcUaWriterPublishingInterval = HttpUtility.UrlDecode(config.opcUaWriterPublishingInterval);

            config.opcUaWriterKeepAliveTime = HttpUtility.UrlDecode(config.opcUaWriterKeepAliveTime);

            config.opcUaWriterMaxNetworkMessageSize = HttpUtility.UrlDecode(config.opcUaWriterMaxNetworkMessageSize);

            config.opcUaWriterHeaderLayoutUri = HttpUtility.UrlDecode(config.opcUaWriterHeaderLayoutUri);

            config.opcUaDataSetWriterName = HttpUtility.UrlDecode(config.opcUaDataSetWriterName);

            config.opcUaDataSetWriterId = HttpUtility.UrlDecode(config.opcUaDataSetWriterId);

            config.opcUaDataSetName = HttpUtility.UrlDecode(config.opcUaDataSetName);

            config.opcUaDataSetKeyFrameCount = HttpUtility.UrlDecode(config.opcUaDataSetKeyFrameCount);

            config.enablePlugin = HttpUtility.UrlDecode(config.enablePlugin);

            config.publishFullShipmentValidationListOnAcceptanceThreshold =
                HttpUtility.UrlDecode(config.publishFullShipmentValidationListOnAcceptanceThreshold);

            config.publishSingleTimeOnAcceptanceThreshold =
                HttpUtility.UrlDecode(config.publishSingleTimeOnAcceptanceThreshold);

            config.socketCommandServer = HttpUtility.UrlDecode(config.socketCommandServer);

            config.socketCommandServerPort = HttpUtility.UrlDecode(config.socketCommandServerPort);

            config.enableBarcodeTcp = HttpUtility.UrlDecode(config.enableBarcodeTcp);

            config.enableBarcodeSerial = HttpUtility.UrlDecode(config.enableBarcodeSerial);

            config.groupEventsOnInventoryStatus = HttpUtility.UrlDecode(config.groupEventsOnInventoryStatus);

            config.barcodeTcpAddress = HttpUtility.UrlDecode(config.barcodeTcpAddress);

            config.barcodeTcpPort = HttpUtility.UrlDecode(config.barcodeTcpPort);

            config.barcodeTcpLen = HttpUtility.UrlDecode(config.barcodeTcpLen);

            config.barcodeTcpNoDataString = HttpUtility.UrlDecode(config.barcodeTcpNoDataString);

            config.barcodeProcessNoDataString = HttpUtility.UrlDecode(config.barcodeProcessNoDataString);

            config.barcodeEnableQueue = HttpUtility.UrlDecode(config.barcodeEnableQueue);

            config.httpAuthenticationHeader = HttpUtility.UrlDecode(config.httpAuthenticationHeader);

            config.httpAuthenticationHeaderValue = HttpUtility.UrlDecode(config.httpAuthenticationHeaderValue);

            config.enableValidation = HttpUtility.UrlDecode(config.enableValidation);

            config.requireUniqueProductCode = HttpUtility.UrlDecode(config.requireUniqueProductCode);

            config.enableTagEventStream = HttpUtility.UrlDecode(config.enableTagEventStream);

            config.enableSummaryStream = HttpUtility.UrlDecode(config.enableSummaryStream);

            config.enableExternalApiVerification = HttpUtility.UrlDecode(config.enableExternalApiVerification);

            config.externalApiVerificationSearchOrderUrl =
                HttpUtility.UrlDecode(config.externalApiVerificationSearchOrderUrl);

            config.externalApiVerificationSearchProductUrl =
                HttpUtility.UrlDecode(config.externalApiVerificationSearchProductUrl);

            config.externalApiVerificationPublishDataUrl =
                HttpUtility.UrlDecode(config.externalApiVerificationPublishDataUrl);

            config.externalApiVerificationHttpHeaderName =
                HttpUtility.UrlDecode(config.externalApiVerificationHttpHeaderName);

            config.externalApiVerificationHttpHeaderValue =
                HttpUtility.UrlDecode(config.externalApiVerificationHttpHeaderValue);

            config.operatingRegion =
                HttpUtility.UrlDecode(config.operatingRegion);

            return config;
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
            throw;
        }
    }
}