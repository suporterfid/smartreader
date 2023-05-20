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
using SmartReader.Infrastructure.Entities;
using SmartReader.Infrastructure.ViewModel;

namespace SmartReader.Infrastructure.Utils;

public static class AgentConfigJsonConverter
{
    public static StandaloneConfigDTO FromEntityToDto(SmartReaderConfig smartReaderConfig)
    {
        return FromEntityToDto(smartReaderConfig, smartReaderConfig.SmartReaderAntennaConfigs);
    }

    public static StandaloneConfigDTO FromEntityToDto(SmartReaderConfig smartReaderConfig,
        List<SmartReaderAntennaConfig> smartReaderAntennaConfigs)
    {
        StandaloneConfigDTO dto = null;
        dto = new StandaloneConfigDTO();

        var antennaPorts = "";
        var antennaStates = "";
        var antennaZones = "";
        var transmitPower = "";
        var receiveSensitivity = "";

        var readerMode = "4";
        var session = "1";
        var tagPopulation = "32";
        for (var i = 0; i < smartReaderAntennaConfigs.Count; i++)
        {
            antennaPorts += smartReaderAntennaConfigs[i].AntennaPort.ToString();
            antennaStates += UtilConverter.ConvertBoolToNumericString(smartReaderAntennaConfigs[i].AntennaState);
            antennaZones += "" + smartReaderAntennaConfigs[i].AntennaZone;
            transmitPower += smartReaderAntennaConfigs[i].TransmitPower.ToString();
            receiveSensitivity += smartReaderAntennaConfigs[i].ReceiveSensitivity.ToString();

            if (i == 0)
            {
                readerMode = smartReaderAntennaConfigs[i].ReaderMode.ToString();
                session = smartReaderAntennaConfigs[i].Session.ToString();
                tagPopulation = smartReaderAntennaConfigs[i].TagPopulation.ToString();
            }

            if (i < smartReaderAntennaConfigs.Count - 1)
            {
                antennaPorts += ",";
                antennaStates += ",";
                antennaZones += ",";
                transmitPower += ",";
                receiveSensitivity += ",";
            }
        }

        dto.antennaPorts = antennaPorts;

        dto.antennaStates = antennaStates;

        dto.antennaZones = antennaZones;

        dto.transmitPower = transmitPower;

        dto.receiveSensitivity = receiveSensitivity;

        dto.readerMode = readerMode;

        dto.session = session;

        dto.tagPopulation = tagPopulation;

        dto.advancedGpoEnabled = UtilConverter.ConvertBoolToNumericString(smartReaderConfig.AdvancedGpoEnabled);
        dto.advancedGpoMode1 = smartReaderConfig.AdvancedGpoMode1.ToString();
        dto.advancedGpoMode2 = smartReaderConfig.AdvancedGpoMode2.ToString();
        dto.advancedGpoMode3 = smartReaderConfig.AdvancedGpoMode3.ToString();
        dto.advancedGpoMode4 = smartReaderConfig.AdvancedGpoMode4.ToString();

        dto.id = smartReaderConfig.Id.ToString();
        dto.readerName = smartReaderConfig.ReaderName;
        dto.serial = smartReaderConfig.Serial;
        dto.isEnabled = UtilConverter.ConvertBoolToNumericString(smartReaderConfig.IsEnabled);
        dto.licenseKey = smartReaderConfig.LicenseKey;
        dto.profileName = smartReaderConfig.ProfileName;
        dto.isCurrentProfile = UtilConverter.ConvertBoolToNumericString(smartReaderConfig.IsCurrentProfile);

        dto.startTriggerType = smartReaderConfig.StartTriggerType.ToString();
        dto.startTriggerPeriod = smartReaderConfig.StartTriggerPeriod.ToString();
        dto.startTriggerOffset = smartReaderConfig.StartTriggerOffset.ToString();
        dto.startTriggerUTCTimestamp = smartReaderConfig.StartTriggerUTCTimestamp.ToString();
        dto.startTriggerGpiEvent = smartReaderConfig.StartTriggerGpiEvent.ToString();
        dto.startTriggerGpiPort = smartReaderConfig.StartTriggerGpiPort.ToString();
        dto.stopTriggerType = smartReaderConfig.StopTriggerType.ToString();
        dto.stopTriggerDuration = smartReaderConfig.StopTriggerDuration.ToString();
        dto.stopTriggerTimeout = smartReaderConfig.StopTriggerTimeout.ToString();
        dto.stopTriggerGpiEvent = smartReaderConfig.StopTriggerGpiEvent.ToString();
        dto.stopTriggerGpiPort = smartReaderConfig.StopTriggerGpiPort.ToString();
        dto.socketServer = smartReaderConfig.SocketServer.ToString();
        dto.socketPort = smartReaderConfig.SocketPort.ToString();
        dto.serialPort = smartReaderConfig.SerialPort.ToString();
        dto.usbHid = smartReaderConfig.UsbHid.ToString();
        dto.lineEnd = smartReaderConfig.LineEnd;
        dto.fieldDelim = smartReaderConfig.FieldDelim;
        dto.softwareFilterEnabled = UtilConverter.ConvertBoolToNumericString(smartReaderConfig.SoftwareFilterEnabled);
        dto.softwareFilterWindowSec = smartReaderConfig.SoftwareFilterWindowSec.ToString();
        dto.softwareFilterField = smartReaderConfig.SoftwareFilterField.ToString();
        dto.softwareFilterReadCountTimeoutEnabled =
            UtilConverter.ConvertBoolToNumericString(smartReaderConfig.SoftwareFilterReadCountAndTimeEnabled);
        dto.softwareFilterReadCountTimeoutSeenCount =
            smartReaderConfig.SoftwareFilterReadCountAndTimeSeenCount.ToString();
        dto.softwareFilterReadCountTimeoutIntervalInSec =
            smartReaderConfig.SoftwareFilterReadCountAndTimeWindowSec.ToString();
        dto.includeReaderName = UtilConverter.ConvertBoolToNumericString(smartReaderConfig.IncludeReaderName);

        dto.includeAntennaPort = UtilConverter.ConvertBoolToNumericString(smartReaderConfig.IncludeAntennaPort);

        dto.includeAntennaZone = UtilConverter.ConvertBoolToNumericString(smartReaderConfig.IncludeAntennaZone);

        dto.includeFirstSeenTimestamp =
            UtilConverter.ConvertBoolToNumericString(smartReaderConfig.IncludeFirstSeenTimestamp);

        dto.includePeakRssi = UtilConverter.ConvertBoolToNumericString(smartReaderConfig.IncludePeakRssi);

        dto.includeRFPhaseAngle = UtilConverter.ConvertBoolToNumericString(smartReaderConfig.IncludeRFPhaseAngle);

        dto.includeRFDopplerFrequency =
            UtilConverter.ConvertBoolToNumericString(smartReaderConfig.IncludeRFDopplerFrequency);

        dto.includeRFChannelIndex = UtilConverter.ConvertBoolToNumericString(smartReaderConfig.IncludeRFChannelIndex);

        dto.includeGpiEvent = UtilConverter.ConvertBoolToNumericString(smartReaderConfig.IncludeGpiEvent);

        dto.includeTid = UtilConverter.ConvertBoolToNumericString(smartReaderConfig.IncludeTid);

        dto.tidWordStart = smartReaderConfig.TidWordStart.ToString();

        dto.tidWordCount = smartReaderConfig.TidWordCount.ToString();

        dto.includeUserMemory = UtilConverter.ConvertBoolToNumericString(smartReaderConfig.IncludeUserMemory);

        dto.userMemoryWordStart = smartReaderConfig.UserMemoryWordStart.ToString();

        dto.userMemoryWordCount = smartReaderConfig.UserMemoryWordCount.ToString();

        dto.siteEnabled = UtilConverter.ConvertBoolToNumericString(smartReaderConfig.SiteEnabled);

        dto.site = smartReaderConfig.Site;

        dto.httpPostEnabled = UtilConverter.ConvertBoolToNumericString(smartReaderConfig.HttpPostEnabled);

        dto.httpPostType = smartReaderConfig.HttpPostType.ToString();

        dto.httpPostIntervalSec = smartReaderConfig.HttpPostIntervalSec.ToString();

        dto.httpPostURL = smartReaderConfig.HttpPostURL;

        dto.httpAuthenticationType = smartReaderConfig.HttpAuthenticationType.ToString();

        dto.httpAuthenticationUsername = smartReaderConfig.HttpAuthenticationUsername;

        dto.httpAuthenticationPassword = smartReaderConfig.HttpAuthenticationPassword;

        dto.httpAuthenticationTokenApiEnabled =
            UtilConverter.ConvertBoolToNumericString(smartReaderConfig.HttpAuthenticationTokenApiEnabled);

        dto.httpAuthenticationTokenApiUrl = smartReaderConfig.HttpAuthenticationTokenApiUrl;

        dto.httpAuthenticationTokenApiBody = smartReaderConfig.HttpAuthenticationTokenApiBody;

        dto.httpAuthenticationTokenApiValue = smartReaderConfig.HttpAuthenticationTokenApiValue;

        dto.httpVerifyPostHttpReturnCode =
            UtilConverter.ConvertBoolToNumericString(smartReaderConfig.HttpVerifyPostHttpReturnCode);

        dto.truncateEpc = UtilConverter.ConvertBoolToNumericString(smartReaderConfig.TruncateEpc);

        dto.truncateStart = smartReaderConfig.TruncateStart.ToString();

        dto.truncateLen = smartReaderConfig.TruncateLen.ToString();

        dto.heartbeatEnabled = UtilConverter.ConvertBoolToNumericString(smartReaderConfig.HeartbeatEnabled);

        dto.heartbeatPeriodSec = smartReaderConfig.HeartbeatPeriodSec.ToString();

        dto.usbFlashDrive = UtilConverter.ConvertBoolToNumericString(smartReaderConfig.UsbFlashDrive);

        dto.lowDutyCycleEnabled = UtilConverter.ConvertBoolToNumericString(smartReaderConfig.LowDutyCycleEnabled);

        dto.emptyFieldTimeout = smartReaderConfig.EmptyFieldTimeout.ToString();

        dto.fieldPingInterval = smartReaderConfig.FieldPingInterval.ToString();

        dto.baudRate = smartReaderConfig.BaudRate.ToString();

        dto.c1g2FilterEnabled = UtilConverter.ConvertBoolToNumericString(smartReaderConfig.C1g2FilterEnabled);

        dto.c1g2FilterBank = smartReaderConfig.C1g2FilterBank.ToString();

        dto.c1g2FilterPointer = smartReaderConfig.C1g2FilterPointer.ToString();

        dto.c1g2FilterMask = smartReaderConfig.C1g2FilterMask;

        dto.c1g2FilterLen = smartReaderConfig.C1g2FilterLen.ToString();

        dto.dataPrefix = smartReaderConfig.DataPrefix;

        dto.dataSuffix = smartReaderConfig.DataSuffix;

        dto.backupToFlashDriveOnGpiEventEnabled =
            UtilConverter.ConvertBoolToNumericString(smartReaderConfig.BackupToFlashDriveOnGpiEventEnabled);

        dto.maxTxPowerOnGpiEventEnabled =
            UtilConverter.ConvertBoolToNumericString(smartReaderConfig.MaxTxPowerOnGpiEventEnabled);

        dto.backupToInternalFlashEnabled =
            UtilConverter.ConvertBoolToNumericString(smartReaderConfig.BackupToInternalFlashEnabled);

        dto.tagValidationEnabled = UtilConverter.ConvertBoolToNumericString(smartReaderConfig.TagValidationEnabled);

        dto.keepFilenameOnDayChange =
            UtilConverter.ConvertBoolToNumericString(smartReaderConfig.KeepFilenameOnDayChange);

        dto.promptBeforeChanging = UtilConverter.ConvertBoolToNumericString(smartReaderConfig.PromptBeforeChanging);

        dto.connectionStatus = UtilConverter.ConvertBoolToNumericString(smartReaderConfig.ConnectionStatus);

        dto.mqttEnabled = UtilConverter.ConvertBoolToNumericString(smartReaderConfig.MqttEnabled);

        dto.mqttUseSsl = UtilConverter.ConvertBoolToNumericString(smartReaderConfig.MqttUseSsl);

        dto.mqttBrokerAddress = smartReaderConfig.MqttBrokerAddress;

        dto.mqttBrokerPort = smartReaderConfig.MqttBrokerPort.ToString();

        dto.mqttTagEventsTopic = smartReaderConfig.MqttTopic;

        dto.mqttTagEventsQoS = smartReaderConfig.MqttQoS.ToString();

        dto.mqttManagementCommandTopic = smartReaderConfig.MqttCommandTopic;

        dto.mqttManagementCommandQoS = smartReaderConfig.MqttCommandQoS.ToString();

        dto.mqttUsername = smartReaderConfig.MqttUsername;

        dto.mqttPassword = smartReaderConfig.MqttPassword;

        dto.mqttProxyUrl = smartReaderConfig.MqttProxyUrl;

        dto.mqttProxyUsername = smartReaderConfig.MqttProxyUsername;

        dto.mqttProxyPassword = smartReaderConfig.MqttProxyPassword;

        dto.mqttPuslishIntervalSec = smartReaderConfig.MqttPuslishIntervalSec.ToString();

        dto.isCloudInterface = UtilConverter.ConvertBoolToNumericString(smartReaderConfig.IsCloudInterface);

        dto.applyIpSettingsOnStartup =
            UtilConverter.ConvertBoolToNumericString(smartReaderConfig.ApplyIpSettingsOnStartup);

        dto.ipAddressMode = smartReaderConfig.IpAddressMode.ToString();
        ;

        dto.ipAddress = smartReaderConfig.IpAddress;

        dto.ipMask = smartReaderConfig.IpMask;

        dto.gatewayAddress = smartReaderConfig.GatewayAddress;

        dto.broadcastAddress = smartReaderConfig.BroadcastAddress;

        dto.parseSgtinEnabled = UtilConverter.ConvertBoolToNumericString(smartReaderConfig.ParseSgtinEnabled);

        dto.gtinOutputType = smartReaderConfig.GtinOutputType.ToString();

        dto.httpVerifyPeer = UtilConverter.ConvertBoolToNumericString(smartReaderConfig.HttpVerifyPeer);

        dto.httpVerifyHost = UtilConverter.ConvertBoolToNumericString(smartReaderConfig.HttpVerifyHost);

        dto.jsonFormat = smartReaderConfig.JsonFormat.ToString();

        dto.csvFileFormat = smartReaderConfig.CsvFileFormat.ToString();

        dto.heartbeatUrl = smartReaderConfig.HeartbeatUrl;

        dto.heartbeatHttpAuthenticationType = smartReaderConfig.HeartbeatHttpAuthenticationType;

        dto.heartbeatHttpAuthenticationUsername = smartReaderConfig.HeartbeatHttpAuthenticationUsername;

        dto.heartbeatHttpAuthenticationPassword = smartReaderConfig.HeartbeatHttpAuthenticationPassword;

        dto.heartbeatHttpAuthenticationTokenApiEnabled =
            UtilConverter.ConvertBoolToNumericString(smartReaderConfig.HeartbeatHttpAuthenticationTokenApiEnabled);

        dto.heartbeatHttpAuthenticationTokenApiUrl = smartReaderConfig.HeartbeatHttpAuthenticationTokenApiUrl;

        dto.heartbeatHttpAuthenticationTokenApiBody = smartReaderConfig.HeartbeatHttpAuthenticationTokenApiBody;

        dto.heartbeatHttpAuthenticationTokenApiUsernameField =
            smartReaderConfig.HeartbeatHttpAuthenticationTokenApiUsernameField;

        dto.heartbeatHttpAuthenticationTokenApiUsernameValue =
            smartReaderConfig.HeartbeatHttpAuthenticationTokenApiUsernameValue;

        dto.heartbeatHttpAuthenticationTokenApiPasswordField =
            smartReaderConfig.HeartbeatHttpAuthenticationTokenApiPasswordField;

        dto.heartbeatHttpAuthenticationTokenApiPasswordValue =
            smartReaderConfig.HeartbeatHttpAuthenticationTokenApiPasswordValue;

        dto.heartbeatHttpAuthenticationTokenApiValue = smartReaderConfig.HeartbeatHttpAuthenticationTokenApiValue;

        dto.httpAuthenticationTokenApiUsernameField = smartReaderConfig.HttpAuthenticationTokenApiUsernameField;

        dto.httpAuthenticationTokenApiUsernameValue = smartReaderConfig.HttpAuthenticationTokenApiUsernameValue;

        dto.httpAuthenticationTokenApiPasswordField = smartReaderConfig.HttpAuthenticationTokenApiPasswordField;

        dto.httpAuthenticationTokenApiPasswordValue = smartReaderConfig.HttpAuthenticationTokenApiPasswordValue;

        dto.toiValidationEnabled = UtilConverter.ConvertBoolToNumericString(smartReaderConfig.ToiValidationEnabled);

        dto.toiValidationUrl = smartReaderConfig.ToiValidationUrl;

        dto.toiValidationGpoDuration = smartReaderConfig.ToiValidationGpoDuration.ToString();

        dto.toiGpoOk = smartReaderConfig.ToiGpoOk.ToString();

        dto.toiGpoNok = smartReaderConfig.ToiGpoNok.ToString();

        dto.toiGpoError = smartReaderConfig.ToiGpoError.ToString();

        dto.toiGpi = smartReaderConfig.ToiGpi.ToString();

        dto.toiGpoPriority = smartReaderConfig.ToiGpoPriority.ToString();

        dto.toiGpoMode = smartReaderConfig.ToiGpoMode.ToString();

        dto.customField1Enabled = UtilConverter.ConvertBoolToNumericString(smartReaderConfig.CustomField1Enabled);

        dto.customField1Name = smartReaderConfig.CustomField1Name;

        dto.customField1Value = smartReaderConfig.CustomField1Value;

        dto.customField2Enabled = UtilConverter.ConvertBoolToNumericString(smartReaderConfig.CustomField2Enabled);

        dto.customField2Name = smartReaderConfig.CustomField2Name;

        dto.customField2Value = smartReaderConfig.CustomField2Value;

        dto.customField3Enabled = UtilConverter.ConvertBoolToNumericString(smartReaderConfig.CustomField3Enabled);

        dto.customField3Name = smartReaderConfig.CustomField3Name;

        dto.customField3Value = smartReaderConfig.CustomField3Value;

        dto.customField4Enabled = UtilConverter.ConvertBoolToNumericString(smartReaderConfig.CustomField4Enabled);

        dto.customField4Name = smartReaderConfig.CustomField4Name;

        dto.customField4Value = smartReaderConfig.CustomField4Value;

        dto.writeUsbJson = UtilConverter.ConvertBoolToNumericString(smartReaderConfig.WriteUsbJson);

        dto.reportingIntervalSeconds = smartReaderConfig.ReportingIntervalSeconds.ToString();

        dto.tagCacheSize = smartReaderConfig.TagCacheSize.ToString();

        dto.antennaIdentifier = smartReaderConfig.AntennaIdentifier.ToString();

        dto.tagIdentifier = smartReaderConfig.TagIdentifier.ToString();

        return dto;
    }

    public static SmartReaderConfig FromDtoToEntity(StandaloneConfigDTO dto, SmartReaderConfig smartReaderConfig,
        List<SmartReaderAntennaConfig> smartReaderAntennaConfigs)
    {
        if (smartReaderAntennaConfigs == null) smartReaderAntennaConfigs = new List<SmartReaderAntennaConfig>();

        try
        {
            var antennaPorts = dto.antennaPorts.Split(','); //\u002C
            var antennaStates = dto.antennaPorts.Split(','); //\u002C
            var antennaZones = dto.antennaZones.Split(','); //\u002C
            var transmitPower = dto.transmitPower.Split(','); //\u002C
            var receiveSensitivity = dto.receiveSensitivity.Split(','); //\u002C

            var readerMode = dto.readerMode;
            var session = dto.session;
            var tagPopulation = dto.tagPopulation;

            for (var i = 0; i < smartReaderAntennaConfigs.Count; i++) smartReaderAntennaConfigs[i].AntennaState = false;

            var newAntennasToAdd = new List<SmartReaderAntennaConfig>();
            for (var i = 0; i < antennaPorts.Length; i++)
            {
                var currentAntennaFound = false;

                if (smartReaderAntennaConfigs.Count > 0)
                    for (var j = 0; j < smartReaderAntennaConfigs.Count; j++)
                        if (smartReaderAntennaConfigs[j].AntennaPort ==
                            UtilConverter.ConvertfromNumericStringToInt(antennaPorts[i]))
                        {
                            smartReaderAntennaConfigs[j].AntennaState =
                                UtilConverter.ConvertfromNumericStringToBool(antennaStates[i]);

                            smartReaderAntennaConfigs[j].ReaderMode =
                                UtilConverter.ConvertfromNumericStringToInt(readerMode);

                            smartReaderAntennaConfigs[j].Session = UtilConverter.ConvertfromNumericStringToInt(session);

                            smartReaderAntennaConfigs[j].TagPopulation =
                                UtilConverter.ConvertfromNumericStringToInt(tagPopulation);

                            smartReaderAntennaConfigs[j].TransmitPower =
                                UtilConverter.ConvertfromNumericStringToInt(transmitPower[i]);

                            smartReaderAntennaConfigs[j].ReceiveSensitivity =
                                UtilConverter.ConvertfromNumericStringToInt(receiveSensitivity[i]);
                            currentAntennaFound = true;
                            break;
                        }

                if (!currentAntennaFound)
                {
                    var newAntenna = new SmartReaderAntennaConfig();

                    newAntenna.AntennaPort = UtilConverter.ConvertfromNumericStringToInt(antennaPorts[i]);

                    newAntenna.AntennaState = UtilConverter.ConvertfromNumericStringToBool(antennaStates[i]);

                    newAntenna.ReaderMode = UtilConverter.ConvertfromNumericStringToInt(readerMode);

                    newAntenna.Session = UtilConverter.ConvertfromNumericStringToInt(session);

                    newAntenna.TagPopulation = UtilConverter.ConvertfromNumericStringToInt(tagPopulation);

                    newAntenna.TransmitPower = UtilConverter.ConvertfromNumericStringToInt(transmitPower[i]);

                    newAntenna.ReceiveSensitivity = UtilConverter.ConvertfromNumericStringToInt(receiveSensitivity[i]);

                    newAntennasToAdd.Add(newAntenna);
                }
            }

            smartReaderAntennaConfigs.AddRange(newAntennasToAdd);
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error: " + ex.Message);
        }

        if (!string.IsNullOrEmpty(dto.id)) smartReaderConfig.Id = UtilConverter.ConvertfromNumericStringToInt(dto.id);

        smartReaderConfig.ReaderName = dto.readerName;
        smartReaderConfig.Serial = dto.serial;
        smartReaderConfig.LicenseKey = dto.licenseKey;
        smartReaderConfig.IpAddress = dto.ipAddress;
        smartReaderConfig.IsEnabled = UtilConverter.ConvertfromNumericStringToBool(dto.isEnabled);
        smartReaderConfig.ProfileName = dto.profileName;
        smartReaderConfig.IsCurrentProfile = UtilConverter.ConvertfromNumericStringToBool(dto.isCurrentProfile);

        smartReaderConfig.AdvancedGpoEnabled = UtilConverter.ConvertfromNumericStringToBool(dto.advancedGpoEnabled);
        smartReaderConfig.AdvancedGpoMode1 = UtilConverter.ConvertfromNumericStringToInt(dto.advancedGpoMode1);
        smartReaderConfig.AdvancedGpoMode2 = UtilConverter.ConvertfromNumericStringToInt(dto.advancedGpoMode2);
        smartReaderConfig.AdvancedGpoMode3 = UtilConverter.ConvertfromNumericStringToInt(dto.advancedGpoMode3);
        smartReaderConfig.AdvancedGpoMode4 = UtilConverter.ConvertfromNumericStringToInt(dto.advancedGpoMode4);
        smartReaderConfig.StartTriggerType = UtilConverter.ConvertfromNumericStringToInt(dto.startTriggerType);
        smartReaderConfig.StartTriggerPeriod = UtilConverter.ConvertfromNumericStringToInt(dto.startTriggerPeriod);
        smartReaderConfig.StartTriggerOffset = UtilConverter.ConvertfromNumericStringToInt(dto.startTriggerOffset);
        smartReaderConfig.StartTriggerUTCTimestamp =
            UtilConverter.ConvertfromNumericStringToLong(dto.startTriggerUTCTimestamp);
        smartReaderConfig.StartTriggerGpiEvent = UtilConverter.ConvertfromNumericStringToInt(dto.startTriggerGpiEvent);
        smartReaderConfig.StartTriggerGpiPort = UtilConverter.ConvertfromNumericStringToInt(dto.startTriggerGpiPort);
        smartReaderConfig.StopTriggerType = UtilConverter.ConvertfromNumericStringToInt(dto.stopTriggerType);
        smartReaderConfig.StopTriggerDuration = UtilConverter.ConvertfromNumericStringToLong(dto.stopTriggerDuration);
        smartReaderConfig.StopTriggerTimeout = UtilConverter.ConvertfromNumericStringToInt(dto.stopTriggerTimeout);
        smartReaderConfig.StopTriggerGpiEvent = UtilConverter.ConvertfromNumericStringToInt(dto.stopTriggerGpiEvent);
        smartReaderConfig.StopTriggerGpiPort = UtilConverter.ConvertfromNumericStringToInt(dto.stopTriggerGpiPort);
        smartReaderConfig.SocketServer = UtilConverter.ConvertfromNumericStringToBool(dto.socketServer);
        smartReaderConfig.SocketPort = UtilConverter.ConvertfromNumericStringToInt(dto.socketPort);
        smartReaderConfig.SerialPort = UtilConverter.ConvertfromNumericStringToBool(dto.serialPort);
        smartReaderConfig.UsbHid = UtilConverter.ConvertfromNumericStringToBool(dto.usbHid);
        smartReaderConfig.LineEnd = dto.lineEnd;
        smartReaderConfig.FieldDelim = dto.fieldDelim;
        smartReaderConfig.SoftwareFilterEnabled =
            UtilConverter.ConvertfromNumericStringToBool(dto.softwareFilterEnabled);
        smartReaderConfig.SoftwareFilterWindowSec =
            UtilConverter.ConvertfromNumericStringToInt(dto.softwareFilterWindowSec);
        smartReaderConfig.SoftwareFilterField = UtilConverter.ConvertfromNumericStringToInt(dto.softwareFilterField);
        smartReaderConfig.SoftwareFilterReadCountAndTimeEnabled =
            UtilConverter.ConvertfromNumericStringToBool(dto.softwareFilterReadCountTimeoutEnabled);
        smartReaderConfig.SoftwareFilterReadCountAndTimeSeenCount =
            UtilConverter.ConvertfromNumericStringToInt(dto.softwareFilterReadCountTimeoutSeenCount);
        smartReaderConfig.SoftwareFilterReadCountAndTimeWindowSec =
            UtilConverter.ConvertfromNumericStringToInt(dto.softwareFilterReadCountTimeoutIntervalInSec);
        smartReaderConfig.IncludeReaderName = UtilConverter.ConvertfromNumericStringToBool(dto.includeReaderName);
        smartReaderConfig.IncludeAntennaPort = UtilConverter.ConvertfromNumericStringToBool(dto.includeAntennaPort);
        smartReaderConfig.IncludeAntennaZone = UtilConverter.ConvertfromNumericStringToBool(dto.includeAntennaZone);
        smartReaderConfig.IncludeFirstSeenTimestamp =
            UtilConverter.ConvertfromNumericStringToBool(dto.includeFirstSeenTimestamp);
        smartReaderConfig.IncludePeakRssi = UtilConverter.ConvertfromNumericStringToBool(dto.includePeakRssi);
        smartReaderConfig.IncludeRFPhaseAngle = UtilConverter.ConvertfromNumericStringToBool(dto.includeRFPhaseAngle);
        smartReaderConfig.IncludeRFDopplerFrequency =
            UtilConverter.ConvertfromNumericStringToBool(dto.includeRFDopplerFrequency);
        smartReaderConfig.IncludeRFChannelIndex =
            UtilConverter.ConvertfromNumericStringToBool(dto.includeRFChannelIndex);
        smartReaderConfig.IncludeGpiEvent = UtilConverter.ConvertfromNumericStringToBool(dto.includeGpiEvent);
        smartReaderConfig.IncludeTid = UtilConverter.ConvertfromNumericStringToBool(dto.includeTid);
        smartReaderConfig.TidWordStart = UtilConverter.ConvertfromNumericStringToInt(dto.tidWordStart);
        smartReaderConfig.TidWordCount = UtilConverter.ConvertfromNumericStringToInt(dto.tidWordCount);
        smartReaderConfig.IncludeUserMemory = UtilConverter.ConvertfromNumericStringToBool(dto.includeUserMemory);
        smartReaderConfig.UserMemoryWordStart = UtilConverter.ConvertfromNumericStringToInt(dto.userMemoryWordStart);
        smartReaderConfig.UserMemoryWordCount = UtilConverter.ConvertfromNumericStringToInt(dto.userMemoryWordCount);
        smartReaderConfig.SiteEnabled = UtilConverter.ConvertfromNumericStringToBool(dto.siteEnabled);
        smartReaderConfig.Site = dto.site;
        smartReaderConfig.HttpPostEnabled = UtilConverter.ConvertfromNumericStringToBool(dto.httpPostEnabled);
        smartReaderConfig.HttpPostType = UtilConverter.ConvertfromNumericStringToInt(dto.httpPostType);
        smartReaderConfig.HttpPostIntervalSec = UtilConverter.ConvertfromNumericStringToInt(dto.httpPostIntervalSec);
        smartReaderConfig.HttpPostURL = dto.httpPostURL;
        smartReaderConfig.HttpAuthenticationType =
            UtilConverter.ConvertfromNumericStringToInt(dto.httpAuthenticationType);
        smartReaderConfig.HttpAuthenticationUsername = dto.httpAuthenticationUsername;
        smartReaderConfig.HttpAuthenticationPassword = dto.httpAuthenticationPassword;
        smartReaderConfig.HttpAuthenticationTokenApiEnabled =
            UtilConverter.ConvertfromNumericStringToBool(dto.httpAuthenticationTokenApiEnabled);
        smartReaderConfig.HttpAuthenticationTokenApiUrl = dto.httpAuthenticationTokenApiUrl;
        smartReaderConfig.HttpAuthenticationTokenApiBody = dto.httpAuthenticationTokenApiBody;
        smartReaderConfig.HttpAuthenticationTokenApiValue = dto.httpAuthenticationTokenApiValue;
        smartReaderConfig.HttpVerifyPostHttpReturnCode =
            UtilConverter.ConvertfromNumericStringToBool(dto.httpVerifyPostHttpReturnCode);
        smartReaderConfig.TruncateEpc = UtilConverter.ConvertfromNumericStringToBool(dto.truncateEpc);
        smartReaderConfig.TruncateStart = UtilConverter.ConvertfromNumericStringToInt(dto.truncateStart);
        smartReaderConfig.TruncateLen = UtilConverter.ConvertfromNumericStringToInt(dto.truncateLen);
        smartReaderConfig.HeartbeatEnabled = UtilConverter.ConvertfromNumericStringToBool(dto.heartbeatEnabled);
        smartReaderConfig.HeartbeatPeriodSec = UtilConverter.ConvertfromNumericStringToInt(dto.heartbeatPeriodSec);
        smartReaderConfig.UsbFlashDrive = UtilConverter.ConvertfromNumericStringToBool(dto.usbFlashDrive);
        smartReaderConfig.LowDutyCycleEnabled = UtilConverter.ConvertfromNumericStringToBool(dto.lowDutyCycleEnabled);
        smartReaderConfig.EmptyFieldTimeout = UtilConverter.ConvertfromNumericStringToInt(dto.emptyFieldTimeout);
        smartReaderConfig.FieldPingInterval = UtilConverter.ConvertfromNumericStringToInt(dto.fieldPingInterval);
        smartReaderConfig.BaudRate = UtilConverter.ConvertfromNumericStringToInt(dto.baudRate);
        smartReaderConfig.C1g2FilterEnabled = UtilConverter.ConvertfromNumericStringToBool(dto.c1g2FilterEnabled);
        smartReaderConfig.C1g2FilterBank = UtilConverter.ConvertfromNumericStringToInt(dto.c1g2FilterBank);
        smartReaderConfig.C1g2FilterPointer = UtilConverter.ConvertfromNumericStringToInt(dto.c1g2FilterPointer);
        smartReaderConfig.C1g2FilterMask = dto.c1g2FilterMask;
        smartReaderConfig.C1g2FilterLen = UtilConverter.ConvertfromNumericStringToInt(dto.c1g2FilterLen);
        smartReaderConfig.DataPrefix = dto.dataPrefix;
        smartReaderConfig.DataSuffix = dto.dataSuffix;
        smartReaderConfig.BackupToFlashDriveOnGpiEventEnabled =
            UtilConverter.ConvertfromNumericStringToBool(dto.backupToFlashDriveOnGpiEventEnabled);
        smartReaderConfig.MaxTxPowerOnGpiEventEnabled =
            UtilConverter.ConvertfromNumericStringToBool(dto.maxTxPowerOnGpiEventEnabled);
        smartReaderConfig.BackupToInternalFlashEnabled =
            UtilConverter.ConvertfromNumericStringToBool(dto.backupToInternalFlashEnabled);
        smartReaderConfig.TagValidationEnabled = UtilConverter.ConvertfromNumericStringToBool(dto.tagValidationEnabled);
        smartReaderConfig.KeepFilenameOnDayChange =
            UtilConverter.ConvertfromNumericStringToBool(dto.keepFilenameOnDayChange);
        smartReaderConfig.PromptBeforeChanging = UtilConverter.ConvertfromNumericStringToBool(dto.promptBeforeChanging);
        smartReaderConfig.ConnectionStatus = UtilConverter.ConvertfromNumericStringToBool(dto.connectionStatus);
        smartReaderConfig.MqttEnabled = UtilConverter.ConvertfromNumericStringToBool(dto.mqttEnabled);
        smartReaderConfig.MqttUseSsl = UtilConverter.ConvertfromNumericStringToBool(dto.mqttUseSsl);
        smartReaderConfig.MqttBrokerAddress = dto.mqttBrokerAddress;
        smartReaderConfig.MqttBrokerPort = UtilConverter.ConvertfromNumericStringToInt(dto.mqttBrokerPort);
        smartReaderConfig.MqttTopic = dto.mqttTagEventsTopic;
        smartReaderConfig.MqttCommandTopic = dto.mqttManagementCommandTopic;
        smartReaderConfig.MqttCommandQoS = UtilConverter.ConvertfromNumericStringToInt(dto.mqttManagementCommandQoS);
        smartReaderConfig.MqttUsername = dto.mqttUsername;
        smartReaderConfig.MqttPassword = dto.mqttPassword;
        smartReaderConfig.MqttProxyUrl = dto.mqttProxyUrl;
        smartReaderConfig.MqttProxyUsername = dto.mqttProxyUsername;
        smartReaderConfig.MqttProxyPassword = dto.mqttProxyPassword;
        smartReaderConfig.MqttPuslishIntervalSec =
            UtilConverter.ConvertfromNumericStringToInt(dto.mqttPuslishIntervalSec);
        smartReaderConfig.IsCloudInterface = UtilConverter.ConvertfromNumericStringToBool(dto.isCloudInterface);
        smartReaderConfig.ApplyIpSettingsOnStartup =
            UtilConverter.ConvertfromNumericStringToBool(dto.applyIpSettingsOnStartup);
        smartReaderConfig.IpAddressMode = UtilConverter.ConvertfromNumericStringToInt(dto.ipAddressMode);
        smartReaderConfig.IpMask = dto.ipMask;
        smartReaderConfig.GatewayAddress = dto.gatewayAddress;
        smartReaderConfig.BroadcastAddress = dto.broadcastAddress;
        smartReaderConfig.ParseSgtinEnabled = UtilConverter.ConvertfromNumericStringToBool(dto.parseSgtinEnabled);
        smartReaderConfig.GtinOutputType = UtilConverter.ConvertfromNumericStringToInt(dto.gtinOutputType);
        smartReaderConfig.HttpVerifyPeer = UtilConverter.ConvertfromNumericStringToBool(dto.httpVerifyPeer);
        smartReaderConfig.HttpVerifyHost = UtilConverter.ConvertfromNumericStringToBool(dto.httpVerifyHost);
        smartReaderConfig.JsonFormat = UtilConverter.ConvertfromNumericStringToInt(dto.jsonFormat);
        smartReaderConfig.CsvFileFormat = UtilConverter.ConvertfromNumericStringToInt(dto.csvFileFormat);
        smartReaderConfig.HeartbeatUrl = dto.heartbeatUrl;
        smartReaderConfig.HeartbeatHttpAuthenticationType = dto.heartbeatHttpAuthenticationType;
        smartReaderConfig.HeartbeatHttpAuthenticationUsername = dto.heartbeatHttpAuthenticationUsername;
        smartReaderConfig.HeartbeatHttpAuthenticationPassword = dto.heartbeatHttpAuthenticationPassword;
        smartReaderConfig.HeartbeatHttpAuthenticationTokenApiEnabled =
            UtilConverter.ConvertfromNumericStringToBool(dto.heartbeatHttpAuthenticationTokenApiEnabled);
        smartReaderConfig.HeartbeatHttpAuthenticationTokenApiUrl = dto.heartbeatHttpAuthenticationTokenApiUrl;
        smartReaderConfig.HeartbeatHttpAuthenticationTokenApiBody = dto.heartbeatHttpAuthenticationTokenApiBody;
        smartReaderConfig.HeartbeatHttpAuthenticationTokenApiUsernameField =
            dto.heartbeatHttpAuthenticationTokenApiUsernameField;
        smartReaderConfig.HeartbeatHttpAuthenticationTokenApiUsernameValue =
            dto.heartbeatHttpAuthenticationTokenApiUsernameValue;
        smartReaderConfig.HeartbeatHttpAuthenticationTokenApiPasswordField =
            dto.heartbeatHttpAuthenticationTokenApiPasswordField;
        smartReaderConfig.HeartbeatHttpAuthenticationTokenApiPasswordValue =
            dto.heartbeatHttpAuthenticationTokenApiPasswordValue;
        smartReaderConfig.HeartbeatHttpAuthenticationTokenApiValue = dto.heartbeatHttpAuthenticationTokenApiValue;
        smartReaderConfig.HttpAuthenticationTokenApiUsernameField = dto.httpAuthenticationTokenApiUsernameField;
        smartReaderConfig.HttpAuthenticationTokenApiUsernameValue = dto.httpAuthenticationTokenApiUsernameValue;
        smartReaderConfig.HttpAuthenticationTokenApiPasswordField = dto.httpAuthenticationTokenApiPasswordField;
        smartReaderConfig.HttpAuthenticationTokenApiPasswordValue = dto.httpAuthenticationTokenApiPasswordValue;
        smartReaderConfig.ToiValidationEnabled = UtilConverter.ConvertfromNumericStringToBool(dto.toiValidationEnabled);
        smartReaderConfig.ToiValidationUrl = dto.toiValidationUrl;
        smartReaderConfig.ToiValidationGpoDuration =
            UtilConverter.ConvertfromNumericStringToInt(dto.toiValidationGpoDuration);
        smartReaderConfig.ToiGpoOk = UtilConverter.ConvertfromNumericStringToInt(dto.toiGpoOk);
        smartReaderConfig.ToiGpoNok = UtilConverter.ConvertfromNumericStringToInt(dto.toiGpoNok);
        smartReaderConfig.ToiGpoError = UtilConverter.ConvertfromNumericStringToInt(dto.toiGpoError);
        smartReaderConfig.ToiGpi = UtilConverter.ConvertfromNumericStringToBool(dto.toiGpi);
        smartReaderConfig.ToiGpoPriority = UtilConverter.ConvertfromNumericStringToInt(dto.toiGpoPriority);
        smartReaderConfig.ToiGpoMode = UtilConverter.ConvertfromNumericStringToInt(dto.toiGpoMode);
        smartReaderConfig.CustomField1Enabled = UtilConverter.ConvertfromNumericStringToBool(dto.customField1Enabled);
        smartReaderConfig.CustomField1Name = dto.customField1Name;
        smartReaderConfig.CustomField1Value = dto.customField1Value;
        smartReaderConfig.CustomField2Enabled = UtilConverter.ConvertfromNumericStringToBool(dto.customField2Enabled);
        smartReaderConfig.CustomField2Name = dto.customField2Name;
        smartReaderConfig.CustomField2Value = dto.customField2Value;
        smartReaderConfig.CustomField3Enabled = UtilConverter.ConvertfromNumericStringToBool(dto.customField3Enabled);
        smartReaderConfig.CustomField3Name = dto.customField3Name;
        smartReaderConfig.CustomField3Value = dto.customField3Value;
        smartReaderConfig.CustomField4Enabled = UtilConverter.ConvertfromNumericStringToBool(dto.customField4Enabled);
        smartReaderConfig.CustomField4Name = dto.customField4Name;
        smartReaderConfig.CustomField4Value = dto.customField4Value;
        smartReaderConfig.WriteUsbJson = UtilConverter.ConvertfromNumericStringToBool(dto.writeUsbJson);
        smartReaderConfig.ReportingIntervalSeconds =
            UtilConverter.ConvertfromNumericStringToInt(dto.reportingIntervalSeconds);
        smartReaderConfig.TagCacheSize = UtilConverter.ConvertfromNumericStringToInt(dto.tagCacheSize);
        smartReaderConfig.AntennaIdentifier = UtilConverter.ConvertfromNumericStringToInt(dto.antennaIdentifier);
        smartReaderConfig.TagIdentifier = UtilConverter.ConvertfromNumericStringToInt(dto.tagIdentifier);

        smartReaderConfig.SmartReaderAntennaConfigs = smartReaderAntennaConfigs;
        return smartReaderConfig;
    }
}