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
using System.Collections.ObjectModel;
using Impinj.Atlas;
using SmartReader.Infrastructure.ViewModel;
using SmartReaderJobs.ViewModel.Antenna;
using SmartReaderJobs.ViewModel.Mqtt;
using SmartReaderJobs.ViewModel.Reader;

namespace SmartReader.IotDeviceInterface;

public class IoTInterfaceMapper
{
    public MqttConfiguration CreateMqttConfigurationRequest(SmartReaderMqttData smartMqttConfig)
    {
        var mqttConfigurationRequest = new MqttConfiguration();


        if (smartMqttConfig.Ativo.HasValue && smartMqttConfig.Ativo.Value == 1)
            mqttConfigurationRequest.Active = true;
        else
            mqttConfigurationRequest.Active = false;

        mqttConfigurationRequest.ClientId = smartMqttConfig.ClientId;
        mqttConfigurationRequest.BrokerHostname = smartMqttConfig.EnderecoBroker;

        if (smartMqttConfig.PortaBroker.HasValue)
            mqttConfigurationRequest.BrokerPort = unchecked((int) smartMqttConfig.PortaBroker);
        if (smartMqttConfig.CleanSession.HasValue && smartMqttConfig.CleanSession.Value == 1)
            mqttConfigurationRequest.CleanSession = true;
        else
            mqttConfigurationRequest.CleanSession = false;

        if (smartMqttConfig.TamanhoBufferEventos.HasValue)
            mqttConfigurationRequest.EventBufferSize = unchecked((int) smartMqttConfig.TamanhoBufferEventos);

        if (smartMqttConfig.LimiteEventosPendentesDeEntrega.HasValue)
            mqttConfigurationRequest.EventPendingDeliveryLimit =
                unchecked((int) smartMqttConfig.LimiteEventosPendentesDeEntrega);

        if (smartMqttConfig.LimiteDeEventosPorSegundo.HasValue)
            mqttConfigurationRequest.EventPerSecondLimit = unchecked((int) smartMqttConfig.LimiteDeEventosPorSegundo);

        if (smartMqttConfig.IntervaloKeepaliveSegundos.HasValue)
            mqttConfigurationRequest.KeepAliveIntervalSeconds =
                unchecked((int) smartMqttConfig.IntervaloKeepaliveSegundos);
        else
            mqttConfigurationRequest.KeepAliveIntervalSeconds = 60;

        if (smartMqttConfig.Qos.HasValue)
            mqttConfigurationRequest.EventQualityOfService = unchecked((int) smartMqttConfig.Qos);
        else
            mqttConfigurationRequest.EventQualityOfService = 1;

        mqttConfigurationRequest.EventTopic = smartMqttConfig.TopicoEventos;

        mqttConfigurationRequest.Username = smartMqttConfig.Usuario;

        mqttConfigurationRequest.Password = smartMqttConfig.Senha;

        mqttConfigurationRequest.WillMessage = smartMqttConfig.MensagemWill;

        mqttConfigurationRequest.WillTopic = smartMqttConfig.TopicoWill;

        if (smartMqttConfig.QosWill.HasValue)
            mqttConfigurationRequest.WillQualityOfService = unchecked((int) smartMqttConfig.QosWill);


        return mqttConfigurationRequest;
    }

    public InventoryRequest CreateInventoryRequest(SmartReaderSetupData smartReaderConfig,
        List<SmartReaderAntennaSetupListData> smartReaderAntennaSetupList)
    {
        var inventoryRequest = new InventoryRequest();
        inventoryRequest.EventConfig = new InventoryEventConfiguration();
        inventoryRequest.EventConfig.Common = new CommonEventConfiguration();
        inventoryRequest.EventConfig.Common.Hostname = CommonEventConfigurationHostname.Enabled;

        inventoryRequest.EventConfig.TagInventory = new TagInventoryEventConfiguration();

        inventoryRequest.EventConfig.TagInventory.TagReporting = new TagReportingConfiguration();

        inventoryRequest.EventConfig.TagInventory.TagReporting.AntennaIdentifier =
            TagReportingConfigurationAntennaIdentifier.AntennaPort;
        inventoryRequest.EventConfig.TagInventory.TagReporting.TagIdentifier =
            TagReportingConfigurationTagIdentifier.Epc;
        inventoryRequest.EventConfig.TagInventory.TagReporting.ReportingIntervalSeconds = 1;

        inventoryRequest.EventConfig.TagInventory.Epc = TagInventoryEventConfigurationEpc.Enabled;
        inventoryRequest.EventConfig.TagInventory.EpcHex = TagInventoryEventConfigurationEpcHex.Enabled;

        if (smartReaderConfig.Fastid.HasValue && smartReaderConfig.Fastid == 1)
        {
            inventoryRequest.EventConfig.TagInventory.Tid = TagInventoryEventConfigurationTid.Enabled;
            inventoryRequest.EventConfig.TagInventory.TidHex = TagInventoryEventConfigurationTidHex.Enabled;
        }
        else
        {
            inventoryRequest.EventConfig.TagInventory.Tid = TagInventoryEventConfigurationTid.Disabled;
            inventoryRequest.EventConfig.TagInventory.TidHex = TagInventoryEventConfigurationTidHex.Disabled;
        }

        inventoryRequest.EventConfig.TagInventory.AntennaPort = TagInventoryEventConfigurationAntennaPort.Enabled;
        inventoryRequest.EventConfig.TagInventory.LastSeenTime = TagInventoryEventConfigurationLastSeenTime.Enabled;
        inventoryRequest.EventConfig.TagInventory.PeakRssiCdbm = TagInventoryEventConfigurationPeakRssiCdbm.Enabled;
        inventoryRequest.EventConfig.TagInventory.PhaseAngle = TagInventoryEventConfigurationPhaseAngle.Disabled;
        inventoryRequest.EventConfig.TagInventory.Frequency = TagInventoryEventConfigurationFrequency.Disabled;

        //===================================================================================================================================
        // a default inventory preset:
        //===================================================================================================================================
        var inventoryAntennaConfiguration = new InventoryAntennaConfiguration();
        inventoryRequest.AntennaConfigs = new ObservableCollection<InventoryAntennaConfiguration>();
        for (var i = 0; i < smartReaderAntennaSetupList.Count; i++)
        {
            var currentAntenna = smartReaderAntennaSetupList[i];
            if (currentAntenna.Status.HasValue && currentAntenna.Status.Value == 1)
            {
                inventoryAntennaConfiguration.AntennaPort = unchecked((int) currentAntenna.Porta.Value);
                inventoryAntennaConfiguration.AntennaName = currentAntenna.Descricao;
                inventoryAntennaConfiguration.EstimatedTagPopulation =
                    unchecked((int) smartReaderConfig.PopulacaoEstimada);
                if (smartReaderConfig.Fastid.HasValue && smartReaderConfig.Fastid.Value == 1)
                    inventoryAntennaConfiguration.FastId = FastId.Enabled;
                else
                    inventoryAntennaConfiguration.FastId = FastId.Disabled;

                inventoryAntennaConfiguration.InventorySearchMode = InventorySearchMode.SingleTarget;
                inventoryAntennaConfiguration.InventorySession = unchecked((int) smartReaderConfig.Sessao);
                if (2 == smartReaderConfig.ModoBusca)
                    inventoryAntennaConfiguration.InventorySearchMode = InventorySearchMode.DualTarget;
                if (3 == smartReaderConfig.ModoBusca)
                {
                    inventoryAntennaConfiguration.InventorySearchMode = InventorySearchMode.SingleTargetWithTagfocus;
                    inventoryAntennaConfiguration.InventorySession = 1;
                }

                if (5 == smartReaderConfig.ModoBusca)
                    inventoryAntennaConfiguration.InventorySearchMode = InventorySearchMode.SingleTargetBToA;
                if (6 == smartReaderConfig.ModoBusca)
                    inventoryAntennaConfiguration.InventorySearchMode = InventorySearchMode.DualTargetWithBToASelect;

                inventoryAntennaConfiguration.RfMode = unchecked((int) smartReaderConfig.ModoLeitor);
                var potencia = double.Parse(currentAntenna.Potencia) * 10;
                inventoryAntennaConfiguration.TransmitPowerCdbm = Convert.ToInt32(potencia);
                if (!string.IsNullOrEmpty((string) currentAntenna.Sensibilidade))
                {
                    var sensbilidade = int.Parse((string) currentAntenna.Sensibilidade) * 10;
                    inventoryAntennaConfiguration.ReceiveSensitivityDbm = sensbilidade;
                }

                //===================================================================================================================================
                // Enable the Regular Inventory and try to read tags
                //===================================================================================================================================
                inventoryRequest.AntennaConfigs.Add(inventoryAntennaConfiguration);
            }
        }


        return inventoryRequest;
    }

    public InventoryRequest CreateStandaloneInventoryRequest(StandaloneConfigDTO smartReaderConfig)
    {
        var inventoryRequest = new InventoryRequest();
        inventoryRequest.EventConfig = new InventoryEventConfiguration();
        inventoryRequest.EventConfig.Common = new CommonEventConfiguration();
        inventoryRequest.EventConfig.Common.Hostname = CommonEventConfigurationHostname.Enabled;

        inventoryRequest.EventConfig.TagInventory = new TagInventoryEventConfiguration();
        inventoryRequest.EventConfig.TagInventory.TagReporting = new TagReportingConfiguration();

        inventoryRequest.EventConfig.TagInventory.TagReporting.AntennaIdentifier =
            TagReportingConfigurationAntennaIdentifier.AntennaPort;
        inventoryRequest.EventConfig.TagInventory.TagReporting.TagIdentifier =
            TagReportingConfigurationTagIdentifier.Epc;
        var reportInterval = 1;
        int.TryParse(smartReaderConfig.reportingIntervalSeconds, out reportInterval);
        inventoryRequest.EventConfig.TagInventory.TagReporting.ReportingIntervalSeconds = reportInterval;

        inventoryRequest.EventConfig.TagInventory.Epc = TagInventoryEventConfigurationEpc.Enabled;
        inventoryRequest.EventConfig.TagInventory.EpcHex = TagInventoryEventConfigurationEpcHex.Enabled;

        if (string.Equals("3", smartReaderConfig.startTriggerType, StringComparison.OrdinalIgnoreCase))
            try
            {
                inventoryRequest.StartTriggers = new ObservableCollection<InventoryStartTrigger>();
                var inventoryStartTrigger = new InventoryStartTrigger();
                var gpiTransitionEvent = new Impinj.Atlas.GpiTransitionEvent();
                var gpiPort = int.Parse(smartReaderConfig.startTriggerGpiPort);
                gpiTransitionEvent.Gpi = gpiPort;
                if (string.Equals("0", smartReaderConfig.startTriggerGpiEvent, StringComparison.OrdinalIgnoreCase))
                    gpiTransitionEvent.Transition = GpiTransitionEventTransition.HighToLow;
                else
                    gpiTransitionEvent.Transition = GpiTransitionEventTransition.LowToHigh;

                inventoryStartTrigger.GpiTransitionEvent = gpiTransitionEvent;
                inventoryRequest.StartTriggers.Add(inventoryStartTrigger);
            }
            catch (Exception)
            {
            }

        if (string.Equals("2", smartReaderConfig.stopTriggerType, StringComparison.OrdinalIgnoreCase))
            try
            {
                inventoryRequest.StopTriggers = new ObservableCollection<InventoryStopTrigger>();
                var inventoryStopTrigger = new InventoryStopTrigger();
                var gpiTransitionEvent = new Impinj.Atlas.GpiTransitionEvent();
                var gpiPort = int.Parse(smartReaderConfig.stopTriggerGpiPort);
                gpiTransitionEvent.Gpi = gpiPort;
                if (string.Equals("0", smartReaderConfig.stopTriggerGpiEvent, StringComparison.OrdinalIgnoreCase))
                    gpiTransitionEvent.Transition = GpiTransitionEventTransition.HighToLow;
                else
                    gpiTransitionEvent.Transition = GpiTransitionEventTransition.LowToHigh;

                inventoryStopTrigger.GpiTransitionEvent = gpiTransitionEvent;
                inventoryRequest.StopTriggers.Add(inventoryStopTrigger);
            }
            catch (Exception)
            {
            }

        if (string.Equals("1", smartReaderConfig.includeTid, StringComparison.OrdinalIgnoreCase))
        {
            inventoryRequest.EventConfig.TagInventory.Tid = TagInventoryEventConfigurationTid.Enabled;
            inventoryRequest.EventConfig.TagInventory.TidHex = TagInventoryEventConfigurationTidHex.Enabled;
        }
        else
        {
            inventoryRequest.EventConfig.TagInventory.Tid = TagInventoryEventConfigurationTid.Disabled;
            inventoryRequest.EventConfig.TagInventory.TidHex = TagInventoryEventConfigurationTidHex.Disabled;
        }

        inventoryRequest.EventConfig.TagInventory.AntennaPort = TagInventoryEventConfigurationAntennaPort.Enabled;
        //if (!string.IsNullOrEmpty(smartReaderConfig.includeAntennaPort) && 1 == int.Parse(smartReaderConfig.includeAntennaPort))
        //{
        //    inventoryRequest.EventConfig.TagInventory.AntennaPort = TagInventoryEventConfigurationAntennaPort.Enabled;
        //}
        //else
        //{
        //    inventoryRequest.EventConfig.TagInventory.AntennaPort = TagInventoryEventConfigurationAntennaPort.Disabled;
        //}

        if (!string.IsNullOrEmpty(smartReaderConfig.includeFirstSeenTimestamp) &&
            1 == int.Parse(smartReaderConfig.includeFirstSeenTimestamp))
            inventoryRequest.EventConfig.TagInventory.LastSeenTime = TagInventoryEventConfigurationLastSeenTime.Enabled;
        else
            inventoryRequest.EventConfig.TagInventory.LastSeenTime =
                TagInventoryEventConfigurationLastSeenTime.Disabled;

        if (!string.IsNullOrEmpty(smartReaderConfig.includePeakRssi) &&
            1 == int.Parse(smartReaderConfig.includePeakRssi))
            inventoryRequest.EventConfig.TagInventory.PeakRssiCdbm = TagInventoryEventConfigurationPeakRssiCdbm.Enabled;
        else
            inventoryRequest.EventConfig.TagInventory.PeakRssiCdbm =
                TagInventoryEventConfigurationPeakRssiCdbm.Disabled;

        if (string.Equals("1", smartReaderConfig.includeRFPhaseAngle, StringComparison.OrdinalIgnoreCase))
            inventoryRequest.EventConfig.TagInventory.PhaseAngle = TagInventoryEventConfigurationPhaseAngle.Enabled;
        else
            inventoryRequest.EventConfig.TagInventory.PhaseAngle = TagInventoryEventConfigurationPhaseAngle.Disabled;

        if (string.Equals("1", smartReaderConfig.includeRFChannelIndex, StringComparison.OrdinalIgnoreCase))
            inventoryRequest.EventConfig.TagInventory.Frequency = TagInventoryEventConfigurationFrequency.Enabled;
        else
            inventoryRequest.EventConfig.TagInventory.Frequency = TagInventoryEventConfigurationFrequency.Disabled;

        //===================================================================================================================================
        // a default inventory preset:
        //===================================================================================================================================

        inventoryRequest.AntennaConfigs = new ObservableCollection<InventoryAntennaConfiguration>();
        var antennaPorts = smartReaderConfig.antennaPorts.Split(','); //\u002C
        var antennaStates = smartReaderConfig.antennaStates.Split(','); //\u002C
        var antennaZones = smartReaderConfig.antennaZones.Split(','); //\u002C
        var transmitPower = smartReaderConfig.transmitPower.Split(','); //\u002C
        var receiveSensitivity = smartReaderConfig.receiveSensitivity.Split(','); //\u002C

        for (var i = 0; i < antennaPorts.Length; i++)
        {
            var inventoryAntennaConfiguration = new InventoryAntennaConfiguration();
            var currentAntenna = antennaPorts[i];
            var currentState = int.Parse(antennaStates[i]);
            if (!string.IsNullOrEmpty(antennaStates[i]) && 1 == currentState)
            {
                inventoryAntennaConfiguration.AntennaPort = int.Parse(currentAntenna);
                if (!string.IsNullOrEmpty(antennaZones[i])) inventoryAntennaConfiguration.AntennaName = antennaZones[i];

                inventoryAntennaConfiguration.EstimatedTagPopulation = int.Parse(smartReaderConfig.tagPopulation);
                if (inventoryAntennaConfiguration.EstimatedTagPopulation < 1)
                    inventoryAntennaConfiguration.EstimatedTagPopulation = 1;

                if (!string.IsNullOrEmpty(smartReaderConfig.includeTid) && 1 == int.Parse(smartReaderConfig.includeTid))
                    inventoryAntennaConfiguration.FastId = FastId.Enabled;
                else
                    inventoryAntennaConfiguration.FastId = FastId.Disabled;

                inventoryAntennaConfiguration.InventorySearchMode = InventorySearchMode.SingleTarget;
                inventoryAntennaConfiguration.InventorySession = int.Parse(smartReaderConfig.session);
                if (2 == int.Parse(smartReaderConfig.searchMode))
                    inventoryAntennaConfiguration.InventorySearchMode = InventorySearchMode.DualTarget;
                if (3 == int.Parse(smartReaderConfig.searchMode))
                {
                    inventoryAntennaConfiguration.InventorySearchMode = InventorySearchMode.SingleTargetWithTagfocus;
                    inventoryAntennaConfiguration.InventorySession = 1;
                }

                if (5 == int.Parse(smartReaderConfig.searchMode))
                    inventoryAntennaConfiguration.InventorySearchMode = InventorySearchMode.SingleTargetBToA;
                if (6 == int.Parse(smartReaderConfig.searchMode))
                    inventoryAntennaConfiguration.InventorySearchMode = InventorySearchMode.DualTargetWithBToASelect;
                inventoryAntennaConfiguration.RfMode = int.Parse(smartReaderConfig.readerMode);

                inventoryAntennaConfiguration.TransmitPowerCdbm = int.Parse(transmitPower[i]);

                if (int.Parse(receiveSensitivity[i]) > -81)
                    inventoryAntennaConfiguration.ReceiveSensitivityDbm = int.Parse(receiveSensitivity[i]);

                //===================================================================================================================================
                // Enable the Regular Inventory and try to read tags
                //===================================================================================================================================
                inventoryRequest.AntennaConfigs.Add(inventoryAntennaConfiguration);
            }
        }


        return inventoryRequest;
    }
}