using MQTTnet.Client;
using MQTTnet.Extensions.ManagedClient;
using MQTTnet.Protocol;
using MQTTnet;
using Impinj.Utils.DebugLogger;
using Newtonsoft.Json;
using SmartReader.Infrastructure.ViewModel;
using System.Security.Authentication;
using System.Text;
using System.Globalization;
using SmartReaderJobs.ViewModel.Mqtt.Endpoint;
using MQTTnet.Packets;

namespace SmartReaderStandalone.Services
{
    public class MqttMessageEventArgs : EventArgs
    {
        public string ClientId { get; }
        public string Topic { get; }
        public string Payload { get; }

        public MqttMessageEventArgs(string clientId, string topic, string payload)
        {
            ClientId = clientId;
            Topic = topic;
            Payload = payload;
        }
    }

    public class MqttErrorEventArgs : EventArgs
    {
        public string Details { get; }

        public MqttErrorEventArgs(string details)
        {
            Details = details;
        }
    }

    public interface IMqttService
    {
        event EventHandler<MqttErrorEventArgs>? GpoErrorRequested;
        event EventHandler<MqttMessageEventArgs>? MessageReceived;

        List<MqttTopicFilter> BuildMqttTopicList(StandaloneConfigDTO smartReaderSetupData);
        bool CheckConnection();
        Task ConnectToMqttBrokerAsync(string mqttClientId, StandaloneConfigDTO standaloneConfigDTO);
        Task DisconnectAsync();
        Task PublishAsync(string topic, string payload, string deviceMacAddress = "", MqttQualityOfServiceLevel qos = MqttQualityOfServiceLevel.AtMostOnce, bool retain = false);
        Task PublishMqttManagementEventAsync(Dictionary<string, object> mqttManagementEvents, string deviceMacAddress = "");
    }

    public class MqttService : IMqttService
    {
        private readonly ILogger<MqttService> _logger;
        private bool _isStopping;
        private StandaloneConfigDTO _standaloneConfigDTO;
        private string? _deviceMacAddress = "";
        private string? DeviceIdMqtt = null;
        private static IManagedMqttClient? _mqttClient = null;
        private static ManagedMqttClientOptions? _mqttClientOptions = null;
        private static ManagedMqttClientOptions? _mqttCommandClientOptions = null;


        // Event to notify subscribers when a message is received with standardized parameters
        public event EventHandler<MqttMessageEventArgs>? MessageReceived;

        public event EventHandler<MqttErrorEventArgs>? GpoErrorRequested;


        public MqttService(ILogger<MqttService> logger, StandaloneConfigDTO standaloneConfigDTO)
        {
            _mqttClient = new MqttFactory().CreateManagedMqttClient();
            _logger = logger;

            // Subscribe to client events.
            _mqttClient.ConnectedAsync += OnConnectedAsync;
            _mqttClient.DisconnectedAsync += OnDisconnectedAsync;
            _standaloneConfigDTO = standaloneConfigDTO;            
        }

        public List<MqttTopicFilter> BuildMqttTopicList(StandaloneConfigDTO smartReaderSetupData)
        {
            try
            {
                var mqttTopicFilters = new List<MqttTopicFilter>();

                var managementCommandQoslevel = 0;
                var controlCommandQoslevel = 0;

                int.TryParse(smartReaderSetupData.mqttManagementCommandQoS, out managementCommandQoslevel);
                int.TryParse(smartReaderSetupData.mqttControlCommandQoS, out controlCommandQoslevel);

                if (smartReaderSetupData != null)
                {
                    var mqttManagementCommandTopic = smartReaderSetupData.mqttManagementCommandTopic;
                    if (!string.IsNullOrEmpty(mqttManagementCommandTopic))
                    {
                        if (mqttManagementCommandTopic.Contains("{{deviceId}}"))
                            mqttManagementCommandTopic =
                                mqttManagementCommandTopic.Replace("{{deviceId}}", smartReaderSetupData.readerName);
                        _logger.LogDebug($"mqttManagementCommandTopic: {mqttManagementCommandTopic}");

                        var managementCommandsFilter = new MqttTopicFilter();
                        managementCommandsFilter = new MqttTopicFilterBuilder().WithTopic($"{mqttManagementCommandTopic}/#")
                            .Build();
                        managementCommandsFilter.QualityOfServiceLevel = managementCommandQoslevel switch
                        {
                            1 => MqttQualityOfServiceLevel.AtLeastOnce,
                            2 => MqttQualityOfServiceLevel.ExactlyOnce,
                            _ => MqttQualityOfServiceLevel.AtMostOnce
                        };

                        if (string.Equals("true", smartReaderSetupData.mqttManagementCommandRetainMessages,
                                StringComparison.OrdinalIgnoreCase))
                            managementCommandsFilter.RetainHandling = MqttRetainHandling.SendAtSubscribe;
                        mqttTopicFilters.Add(managementCommandsFilter);
                    }

                    var mqttControlCommandTopic = smartReaderSetupData.mqttControlCommandTopic;
                    if (!string.IsNullOrEmpty(mqttControlCommandTopic))
                    {
                        if (mqttControlCommandTopic.Contains("{{deviceId}}"))
                            mqttControlCommandTopic =
                                mqttControlCommandTopic.Replace("{{deviceId}}", smartReaderSetupData.readerName);

                        _logger.LogDebug($"mqttControlCommandTopic: {mqttControlCommandTopic}");

                        if (!string.Equals(mqttManagementCommandTopic, mqttControlCommandTopic,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            var controlCommandsFilter = new MqttTopicFilter();
                            controlCommandsFilter = new MqttTopicFilterBuilder().WithTopic($"{mqttControlCommandTopic}/#")
                                .Build();
                            controlCommandsFilter.QualityOfServiceLevel = controlCommandQoslevel switch
                            {
                                1 => MqttQualityOfServiceLevel.AtLeastOnce,
                                2 => MqttQualityOfServiceLevel.ExactlyOnce,
                                _ => MqttQualityOfServiceLevel.AtMostOnce
                            };


                            if (string.Equals("true", smartReaderSetupData.mqttControlCommandRetainMessages,
                                    StringComparison.OrdinalIgnoreCase))
                                controlCommandsFilter.RetainHandling = MqttRetainHandling.SendAtSubscribe;

                            mqttTopicFilters.Add(controlCommandsFilter);
                        }
                    }

                    try
                    {
                        if (string.Equals("1", smartReaderSetupData.mqttEnableSmartreaderDefaultTopics,
                                    StringComparison.OrdinalIgnoreCase))
                        {
                            switch (managementCommandQoslevel)
                            {
                                case 1:
                                    mqttTopicFilters.Add(new MqttTopicFilterBuilder().WithAtLeastOnceQoS()
                                        .WithTopic("smartreader/+/cmd/settings/get").Build());
                                    mqttTopicFilters.Add(new MqttTopicFilterBuilder().WithAtLeastOnceQoS()
                                        .WithTopic("smartreader/+/cmd/settings/post").Build());
                                    mqttTopicFilters.Add(new MqttTopicFilterBuilder().WithAtLeastOnceQoS()
                                        .WithTopic("smartreader/+/cmd/getserial").Build());
                                    mqttTopicFilters.Add(new MqttTopicFilterBuilder().WithAtLeastOnceQoS()
                                        .WithTopic("smartreader/+/cmd/getstatus").Build());
                                    mqttTopicFilters.Add(new MqttTopicFilterBuilder().WithAtLeastOnceQoS()
                                        .WithTopic("smartreader/+/cmd/start-inventory").Build());
                                    mqttTopicFilters.Add(new MqttTopicFilterBuilder().WithAtLeastOnceQoS()
                                        .WithTopic("smartreader/+/cmd/stop-inventory").Build());
                                    mqttTopicFilters.Add(new MqttTopicFilterBuilder().WithExactlyOnceQoS()
                                        .WithTopic("smartreader/+/api/v1/#").Build());
                                    break;
                                case 2:
                                    mqttTopicFilters.Add(new MqttTopicFilterBuilder().WithExactlyOnceQoS()
                                        .WithTopic("smartreader/+/cmd/settings/get").Build());
                                    mqttTopicFilters.Add(new MqttTopicFilterBuilder().WithExactlyOnceQoS()
                                        .WithTopic("smartreader/+/cmd/settings/post").Build());
                                    mqttTopicFilters.Add(new MqttTopicFilterBuilder().WithExactlyOnceQoS()
                                        .WithTopic("smartreader/+/cmd/getserial").Build());
                                    mqttTopicFilters.Add(new MqttTopicFilterBuilder().WithExactlyOnceQoS()
                                        .WithTopic("smartreader/+/cmd/getstatus").Build());
                                    mqttTopicFilters.Add(new MqttTopicFilterBuilder().WithExactlyOnceQoS()
                                        .WithTopic("smartreader/+/cmd/start-inventory").Build());
                                    mqttTopicFilters.Add(new MqttTopicFilterBuilder().WithExactlyOnceQoS()
                                        .WithTopic("smartreader/+/cmd/stop-inventory").Build());
                                    mqttTopicFilters.Add(new MqttTopicFilterBuilder().WithExactlyOnceQoS()
                                        .WithTopic("smartreader/+/api/v1/#").Build());
                                    break;
                                default:
                                    mqttTopicFilters.Add(new MqttTopicFilterBuilder().WithAtMostOnceQoS()
                                        .WithTopic("smartreader/+/cmd/settings/get").Build());
                                    mqttTopicFilters.Add(new MqttTopicFilterBuilder().WithAtMostOnceQoS()
                                        .WithTopic("smartreader/+/cmd/settings/post").Build());
                                    mqttTopicFilters.Add(new MqttTopicFilterBuilder().WithAtMostOnceQoS()
                                        .WithTopic("smartreader/+/cmd/getserial").Build());
                                    mqttTopicFilters.Add(new MqttTopicFilterBuilder().WithAtMostOnceQoS()
                                        .WithTopic("smartreader/+/cmd/getstatus").Build());
                                    mqttTopicFilters.Add(new MqttTopicFilterBuilder().WithAtMostOnceQoS()
                                        .WithTopic("smartreader/+/cmd/start-inventory").Build());
                                    mqttTopicFilters.Add(new MqttTopicFilterBuilder().WithAtMostOnceQoS()
                                        .WithTopic("smartreader/+/cmd/stop-inventory").Build());
                                    mqttTopicFilters.Add(new MqttTopicFilterBuilder().WithExactlyOnceQoS()
                                        .WithTopic("smartreader/+/api/v1/#").Build());
                                    break;
                            }
                        }


                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "BuildMqttTopicList: " + ex.Message);
                    }
                }

                _logger.LogDebug($"prepared {mqttTopicFilters.Count} topic to subscribe.");
                
                return mqttTopicFilters;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BuildMqttTopicList: Unexpected error. " + ex.Message);
                throw;
            }
        }

        public bool CheckConnection()
        {
            if (_mqttClient != null && _mqttClient.IsConnected)
            {
                _logger.LogInformation("MQTT is connected to the broker.");
                return true;
            }
            else
            {
                _logger.LogWarning("MQTT is not connected to the broker.");
                return false;
            }
        }

        public async Task ConnectToMqttBrokerAsync(string mqttClientId, StandaloneConfigDTO standaloneConfigDTO)
        {
            try
            {
                DeviceIdMqtt = mqttClientId;
                _standaloneConfigDTO = standaloneConfigDTO;
                if (string.IsNullOrEmpty(mqttClientId))
                {
                    mqttClientId = _standaloneConfigDTO.readerName;
                }

                var mqttBrokerAddress = _standaloneConfigDTO.mqttBrokerAddress;
                var mqttBrokerPort = int.Parse(_standaloneConfigDTO.mqttBrokerPort);
                var mqttUsername = _standaloneConfigDTO.mqttUsername;
                var mqttPassword = _standaloneConfigDTO.mqttPassword;
                var mqttTagEventsTopic = _standaloneConfigDTO.mqttTagEventsTopic;
                var mqttTagEventsQoS = int.Parse(_standaloneConfigDTO.mqttTagEventsQoS);
                var lastWillMessage = BuildMqttLastWillMessage();
                int mqttKeepAlivePeriod = 30;
#pragma warning disable CS8602 // Dereference of a possibly null reference.
                int.TryParse(_standaloneConfigDTO.mqttBrokerKeepAlive, out mqttKeepAlivePeriod);
#pragma warning restore CS8602 // Dereference of a possibly null reference.

                //var mqttBrokerAddress = _configuration.GetValue<string>("MQTTInfo:Address");
                //var mqttBrokerPort = _configuration.GetValue<int>("MQTTInfo:Port");
                //var mqttBrokerUsername = _configuration.GetValue<string>("MQTTInfo:username");
                //var mqttBrokerPassword = _configuration.GetValue<string>("MQTTInfo:password");
                // Setup and start a managed MQTT client.
                // 1 - The managed client is started once and will maintain the connection automatically including reconnecting etc.
                // 2 - All MQTT application messages are added to an internal queue and processed once the server is available.
                // 3 - All MQTT application messages can be stored to support sending them after a restart of the application
                // 4 - All subscriptions are managed across server connections. There is no need to subscribe manually after the connection with the server is lost.


                // Setup and start a managed MQTT client.
                //ManagedMqttClientOptions localMqttClientOptions;


                if (string.IsNullOrEmpty(mqttUsername))
                {
                    //string localClientId = mqttClientId + "-" + DateTime.Now.ToFileTimeUtc();
                    var localClientId = mqttClientId; // + "-" + DateTime.Now.ToFileTimeUtc();


                    if (!string.IsNullOrEmpty(_standaloneConfigDTO.mqttBrokerProtocol)
                    && _standaloneConfigDTO.mqttBrokerProtocol.ToLower().Contains("ws"))
                    {

                        var mqttBrokerWebSocketPath = "/mqtt";
                        if (!string.IsNullOrEmpty(_standaloneConfigDTO.mqttBrokerWebSocketPath))
                        {
                            mqttBrokerWebSocketPath = _standaloneConfigDTO.mqttBrokerWebSocketPath;
                        }



                        _mqttClientOptions = new ManagedMqttClientOptionsBuilder()
                        .WithClientOptions(new MqttClientOptionsBuilder()
                            //.WithCleanSession()
                            .WithKeepAlivePeriod(new TimeSpan(0, 0, 0, mqttKeepAlivePeriod))
                            //.WithCommunicationTimeout(TimeSpan.FromMilliseconds(60 * 1000))
                            .WithClientId(localClientId)
                            .WithWebSocketServer(o => o.WithUri($"{mqttBrokerAddress}:{mqttBrokerPort}{mqttBrokerWebSocketPath}"))
                            .WithWillPayload(lastWillMessage.PayloadSegment)
                            .WithWillQualityOfServiceLevel(lastWillMessage.QualityOfServiceLevel)
                            .WithWillTopic(lastWillMessage.Topic)
                            .WithWillRetain(lastWillMessage.Retain)
                            .WithWillContentType(lastWillMessage.ContentType)
                            //.WithWillMessage(lastWillMessage)
                            .Build())
                        .Build();
                    }
                    else
                    {
                        _mqttClientOptions = new ManagedMqttClientOptionsBuilder()
                        .WithClientOptions(new MqttClientOptionsBuilder()
                            //.WithCleanSession()
                            .WithKeepAlivePeriod(new TimeSpan(0, 0, 0, mqttKeepAlivePeriod))
                            //.WithCommunicationTimeout(TimeSpan.FromMilliseconds(60 * 1000))
                            .WithClientId(localClientId)
                            .WithTcpServer(mqttBrokerAddress, mqttBrokerPort)
                                                    //.WithWebSocketServer("wss://mymqttserver:443")
                                                    .WithWillPayload(lastWillMessage.PayloadSegment)
                            .WithWillQualityOfServiceLevel(lastWillMessage.QualityOfServiceLevel)
                            .WithWillTopic(lastWillMessage.Topic)
                            .WithWillRetain(lastWillMessage.Retain)
                            .WithWillContentType(lastWillMessage.ContentType)
                            //.WithWillMessage(lastWillMessage)
                            .Build())
                        .Build();
                    }

                }
                else
                {
                    var localClientId = mqttClientId; // + "-" + DateTime.Now.ToFileTimeUtc();

                    if (!string.IsNullOrEmpty(_standaloneConfigDTO.mqttBrokerProtocol)
                        && _standaloneConfigDTO.mqttBrokerProtocol.ToLower().Contains("ws"))
                    {
                        var mqttBrokerWebSocketPath = "/mqtt";
                        if (!string.IsNullOrEmpty(_standaloneConfigDTO.mqttBrokerWebSocketPath))
                        {
                            mqttBrokerWebSocketPath = _standaloneConfigDTO.mqttBrokerWebSocketPath;
                        }
                        if (_standaloneConfigDTO.mqttBrokerProtocol.ToLower().Contains("wss"))
                        {
                            _mqttClientOptions = new ManagedMqttClientOptionsBuilder()
                        .WithClientOptions(new MqttClientOptionsBuilder()
                            //.WithCleanSession()
                            .WithKeepAlivePeriod(new TimeSpan(0, 0, 0, mqttKeepAlivePeriod))
                            //.WithCommunicationTimeout(TimeSpan.FromMilliseconds(60 * 1000))
                            .WithClientId(localClientId)
                             .WithWebSocketServer(o => o.WithUri($"{mqttBrokerAddress}:{mqttBrokerPort}{mqttBrokerWebSocketPath}"))
                            .WithCredentials(mqttUsername, mqttPassword)
                                                    .WithWillPayload(lastWillMessage.PayloadSegment)
                            .WithWillQualityOfServiceLevel(lastWillMessage.QualityOfServiceLevel)
                            .WithWillTopic(lastWillMessage.Topic)
                            .WithWillRetain(lastWillMessage.Retain)
                            .WithWillContentType(lastWillMessage.ContentType)
                            .WithTlsOptions(o =>
                            {
                                // The used public broker sometimes has invalid certificates. This sample accepts all
                                // certificates. This should not be used in live environments.
                                o.WithCertificateValidationHandler(_ => true);

                                // The default value is determined by the OS. Set manually to force version.
                                o.WithSslProtocols(SslProtocols.Tls12);
                            })
                            //.WithWillMessage(lastWillMessage)
                            .Build())
                        .Build();
                        }
                        else
                        {
                            _mqttClientOptions = new ManagedMqttClientOptionsBuilder()
                                                .WithClientOptions(new MqttClientOptionsBuilder()
                                                    //.WithCleanSession()
                                                    .WithKeepAlivePeriod(new TimeSpan(0, 0, 0, mqttKeepAlivePeriod))
                                                    //.WithCommunicationTimeout(TimeSpan.FromMilliseconds(60 * 1000))
                                                    .WithClientId(localClientId)
                                                     .WithWebSocketServer(o => o.WithUri($"{mqttBrokerAddress}:{mqttBrokerPort}{mqttBrokerWebSocketPath}"))
                                                    .WithCredentials(mqttUsername, mqttPassword)
                                                                            .WithWillPayload(lastWillMessage.PayloadSegment)
                                                    .WithWillQualityOfServiceLevel(lastWillMessage.QualityOfServiceLevel)
                                                    .WithWillTopic(lastWillMessage.Topic)
                                                    .WithWillRetain(lastWillMessage.Retain)
                                                    .WithWillContentType(lastWillMessage.ContentType)
                                                    //.WithWillMessage(lastWillMessage)
                                                    .Build())
                                                .Build();
                        }
                        _mqttClientOptions = new ManagedMqttClientOptionsBuilder()
                        .WithClientOptions(new MqttClientOptionsBuilder()
                            //.WithCleanSession()
                            .WithKeepAlivePeriod(new TimeSpan(0, 0, 0, mqttKeepAlivePeriod))
                            //.WithCommunicationTimeout(TimeSpan.FromMilliseconds(60 * 1000))
                            .WithClientId(localClientId)
                             .WithWebSocketServer(o => o.WithUri($"{mqttBrokerAddress}:{mqttBrokerPort}{mqttBrokerWebSocketPath}"))
                            .WithCredentials(mqttUsername, mqttPassword)
                                                    .WithWillPayload(lastWillMessage.PayloadSegment)
                            .WithWillQualityOfServiceLevel(lastWillMessage.QualityOfServiceLevel)
                            .WithWillTopic(lastWillMessage.Topic)
                            .WithWillRetain(lastWillMessage.Retain)
                            .WithWillContentType(lastWillMessage.ContentType)
                        //.WithWillMessage(lastWillMessage)
                            .Build())
                        .Build();
                    }
                    else
                    {
                        if (_standaloneConfigDTO.mqttBrokerProtocol.ToLower().Contains("mqtts"))
                        {
                            _mqttClientOptions = new ManagedMqttClientOptionsBuilder()
                                .WithClientOptions(new MqttClientOptionsBuilder()
                                    //.WithCleanSession()
                                    .WithKeepAlivePeriod(new TimeSpan(0, 0, 0, mqttKeepAlivePeriod))
                                    //.WithCommunicationTimeout(TimeSpan.FromMilliseconds(60 * 1000))
                                    .WithClientId(localClientId)
                                    .WithTcpServer(mqttBrokerAddress, mqttBrokerPort)
                                    .WithCredentials(mqttUsername, mqttPassword)
                                                            .WithWillPayload(lastWillMessage.PayloadSegment)
                                    .WithWillQualityOfServiceLevel(lastWillMessage.QualityOfServiceLevel)
                                    .WithWillTopic(lastWillMessage.Topic)
                                    .WithWillRetain(lastWillMessage.Retain)
                                    .WithWillContentType(lastWillMessage.ContentType)
                                    .WithTlsOptions(o =>
                                    {
                                        // The used public broker sometimes has invalid certificates. This sample accepts all
                                        // certificates. This should not be used in live environments.
                                        o.WithCertificateValidationHandler(_ => true);

                                        // The default value is determined by the OS. Set manually to force version.
                                        o.WithSslProtocols(SslProtocols.Tls12);
                                    })
                                    //.WithWillMessage(lastWillMessage)
                                    .Build())
                                .Build();
                        }
                        else
                        {
                            _mqttClientOptions = new ManagedMqttClientOptionsBuilder()
                                .WithClientOptions(new MqttClientOptionsBuilder()
                                    //.WithCleanSession()
                                    .WithKeepAlivePeriod(new TimeSpan(0, 0, 0, mqttKeepAlivePeriod))
                                    //.WithCommunicationTimeout(TimeSpan.FromMilliseconds(60 * 1000))
                                    .WithClientId(localClientId)
                                    .WithTcpServer(mqttBrokerAddress, mqttBrokerPort)
                                    .WithCredentials(mqttUsername, mqttPassword)
                                                            .WithWillPayload(lastWillMessage.PayloadSegment)
                                    .WithWillQualityOfServiceLevel(lastWillMessage.QualityOfServiceLevel)
                                    .WithWillTopic(lastWillMessage.Topic)
                                    .WithWillRetain(lastWillMessage.Retain)
                                    .WithWillContentType(lastWillMessage.ContentType)
                                    //.WithWillMessage(lastWillMessage)
                                    .Build())
                                .Build();
                        }

                    }
                }


                _mqttClient = new MqttFactory().CreateManagedMqttClient();
                //_mqttClient = new MqttFactory().CreateMqttClient();


                await _mqttClient.StartAsync(_mqttClientOptions);




                //_mqttClient.UseApplicationMessageReceivedHandler(e => { });



                _mqttClient.ApplicationMessageReceivedAsync += async e =>
                {
                    //e.AutoAcknowledge = false;

                    _logger.LogInformation("### RECEIVED APPLICATION MESSAGE ###");
                    _logger.LogInformation($"+ Topic = {e.ApplicationMessage.Topic}");
                    if (e.ApplicationMessage.PayloadSegment != null)
                        _logger.LogInformation($"+ Payload = {Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment)}");

                    _logger.LogInformation($"+ QoS = {e.ApplicationMessage.QualityOfServiceLevel}");
                    _logger.LogInformation($"+ Retain = {e.ApplicationMessage.Retain}");
                    _logger.LogInformation($"+ ClientId = {e.ClientId}");

                    var payload = "";
                    if (e.ApplicationMessage.PayloadSegment != null)
                        payload = Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment);
                    try
                    {
                        //ProcessMqttCommandMessage(e.ClientId, e.ApplicationMessage.Topic, payload);
                        // Raise the standardized event
                        MessageReceived?.Invoke(this, new MqttMessageEventArgs(e.ClientId, e.ApplicationMessage.Topic, payload));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError("MessageReceived: Unexpected error. " + ex.Message);
                    }

                    _logger.LogInformation(" ");
                };




                _mqttClient.ConnectedAsync += async e =>
                {
                    Console.WriteLine("### CONNECTED WITH SERVER ###");
                    if (!string.IsNullOrEmpty(_standaloneConfigDTO.mqttBrokerAddress))
                        _logger.LogInformation("### CONNECTED WITH SERVER ### " + _standaloneConfigDTO.mqttBrokerAddress,
                            SeverityType.Debug);
                    var mqttManagementEvents = new Dictionary<string, object>();
                    mqttManagementEvents.Add("smartreader-mqtt-status", "connected");
                    mqttManagementEvents.Add("readerName", _standaloneConfigDTO.readerName);
#pragma warning disable CS8602 // Dereference of a possibly null reference.
                    mqttManagementEvents.Add("mac", _deviceMacAddress);
#pragma warning restore CS8602 // Dereference of a possibly null reference.
                    mqttManagementEvents.Add("timestamp", SmartReaderJobs.Utils.Utils.CSharpMillisToJavaLongMicroseconds(DateTime.Now));
                    await PublishMqttManagementEventAsync(mqttManagementEvents);

                    try
                    {
                        var mqttTopicFilters = BuildMqttTopicList(_standaloneConfigDTO);


                        await _mqttClient.SubscribeAsync(mqttTopicFilters.ToArray());
                        _logger.LogInformation("### Subscribed to topics. ### ");
                        //_ = ProcessGpoErrorPortRecoveryAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Subscribe: Unexpected error. " + ex.Message);
                    }
                };


                _mqttClient.DisconnectedAsync += async e =>
                {
                    Console.WriteLine("### DISCONNECTED FROM SERVER ###");



                    if (!string.IsNullOrEmpty(_standaloneConfigDTO.mqttBrokerAddress))
                        _logger.LogInformation($"### DISCONNECTED FROM SERVER ### {_standaloneConfigDTO.mqttBrokerAddress}");

                    try
                    {
                        var disconnectDetails = $"Disconnection: ConnectResult {e.ConnectResult} \n";
                        disconnectDetails += $"Disconnection: Reason {e.Reason} \n";
                        if (e.ConnectResult != null)
                        {
                            disconnectDetails += $" ResultCode {e.ConnectResult.ResultCode} \n";
                        }
                        disconnectDetails += $" ClientWasConnected {e.ClientWasConnected} \n";
                        if (e.Exception != null && !string.IsNullOrEmpty(e.Exception.Message))
                        {
                            disconnectDetails += $" Message {e.Exception.Message} \n";
                        }

                        _logger.LogInformation($"### Details {disconnectDetails}");

                        // Raise the event instead of directly calling the method
                        GpoErrorRequested?.Invoke(this, new MqttErrorEventArgs(disconnectDetails));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "MQTT Disconnected.");
                    }

                };

                _mqttClient.ConnectingFailedAsync += async e =>
                {
                    Console.WriteLine("### CONNECTION FAILED ###");



                    if (!string.IsNullOrEmpty(_standaloneConfigDTO.mqttBrokerAddress))
                        _logger.LogInformation($"### CONNECTION FAILED ### {_standaloneConfigDTO.mqttBrokerAddress}");

                    try
                    {
                        var disconnectDetails = $"CONNECTION FAILED: ConnectResult {e.ConnectResult} \n";

                        _logger.LogInformation($"### Details {disconnectDetails}");

                        // Raise the event instead of directly calling the method
                        GpoErrorRequested?.Invoke(this, new MqttErrorEventArgs(disconnectDetails));
                    }
                    catch (Exception)
                    {

                    }

                };

                _mqttClient.ConnectionStateChangedAsync += async e =>
                {
                    Console.WriteLine("### CONNECTION STATE CHANGED ###");



                    if (!string.IsNullOrEmpty(_standaloneConfigDTO.mqttBrokerAddress))
                        _logger.LogInformation($"### CONNECTION STATE CHANGED ### {_standaloneConfigDTO.mqttBrokerAddress}");
                    
                    var disconnectDetails = "";
                    
                    try
                    {
                        if (_mqttClient.InternalClient.IsConnected)
                        {
                            _logger.LogInformation("The MQTT client is currently connected.");
                        }
                        else
                        {
                            _logger.LogInformation("The MQTT client is disconnected.");
                        }

                        //disconnectDetails = $"CONNECTION STATE CHANGED: ConnectResult {e.ToString()} \n";

                        //_logger.LogInformation($"### Details {disconnectDetails}");
   
                    }
                    catch (Exception)
                    {
                        // Raise the event instead of directly calling the method
                        GpoErrorRequested?.Invoke(this, new MqttErrorEventArgs(disconnectDetails));
                    }

                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error" + ex.Message);
            }
        }

        private MqttApplicationMessage BuildMqttLastWillMessage()
        {
            var lwtTopic = "/events/";
            var lwtRetainMessage = true;

            if (_standaloneConfigDTO != null)
            {
                if (!string.IsNullOrEmpty(_standaloneConfigDTO.mqttLwtTopic))
                {
                    lwtTopic = _standaloneConfigDTO.mqttLwtTopic;
                }
                if (!string.IsNullOrEmpty(_standaloneConfigDTO.mqttLwtQoS))
                {
                    bool.TryParse(_standaloneConfigDTO.mqttLwtQoS, out lwtRetainMessage);
                }

            }
            var mqttManagementEvents = new Dictionary<string, string>();
            mqttManagementEvents.Add("smartreader-mqtt-status", "disconnected");
            var jsonParam = JsonConvert.SerializeObject(mqttManagementEvents);

            var lastWillMessage = new MqttApplicationMessageBuilder()
                .WithTopic(lwtTopic)
                .WithRetainFlag(lwtRetainMessage)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .WithPayload(Encoding.ASCII.GetBytes(jsonParam))
                .Build();


            return lastWillMessage;
        }

        public async Task PublishMqttManagementEventAsync(Dictionary<string, object> mqttManagementEvents,
            string deviceMacAddress = "")
        {
            _deviceMacAddress = deviceMacAddress;
            if (_standaloneConfigDTO != null)
            {
                var mqttManagementEventsTopic = _standaloneConfigDTO.mqttManagementEventsTopic;
                if (string.Equals("1", _standaloneConfigDTO.mqttEnabled, StringComparison.OrdinalIgnoreCase)
                    && _mqttClient != null
                    && _mqttClient.IsConnected)
                    if (!string.IsNullOrEmpty(_standaloneConfigDTO.mqttManagementEventsTopic))
                    {
                        //Dictionary<string, string> mqttManagementEvents = new Dictionary<string, string>();
                        //mqttManagementEvents.Add("smartreader-mqtt-status", "connected");
                        var jsonParam = JsonConvert.SerializeObject(mqttManagementEvents);

                        if (_standaloneConfigDTO.mqttManagementEventsTopic.Contains("{{deviceId}}"))
                            mqttManagementEventsTopic =
                                _standaloneConfigDTO.mqttControlResponseTopic.Replace("{{deviceId}}",
                                    _standaloneConfigDTO.readerName);
                        try
                        {
                            var qos = 0;
                            var retain = false;
                            var mqttQualityOfServiceLevel = MqttQualityOfServiceLevel.AtMostOnce;
                            try
                            {
                                int.TryParse(_standaloneConfigDTO.mqttManagementEventsQoS, out qos);
                                bool.TryParse(_standaloneConfigDTO.mqttManagementEventsRetainMessages, out retain);

                                mqttQualityOfServiceLevel = qos switch
                                {
                                    1 => MqttQualityOfServiceLevel.AtLeastOnce,
                                    2 => MqttQualityOfServiceLevel.ExactlyOnce,
                                    _ => MqttQualityOfServiceLevel.AtMostOnce
                                };
                            }
                            catch (Exception)
                            {
                            }
                            if (mqttManagementEvents.Count > 0)
                            {
                                _logger.LogInformation($"Publishing management event: {jsonParam}");
                                var mqttCommandResponseTopic = $"{_standaloneConfigDTO.mqttManagementResponseTopic}";
                                await _mqttClient.EnqueueAsync(mqttManagementEventsTopic, jsonParam,
                                    mqttQualityOfServiceLevel, retain);
                                // Wait until the queue is fully processed.
                                SpinWait.SpinUntil(() => _mqttClient.PendingApplicationMessagesCount == 0, 1000);
                                //_ = ProcessGpoErrorPortRecoveryAsync();
                            }

                        }
                        catch (Exception ex)
                        {
                            // Raise the event instead of directly calling the method
                            GpoErrorRequested?.Invoke(this, new MqttErrorEventArgs(ex.Message));
                        }
                    }
            }
        }

        public async Task PublishAsync(string topic, 
            string payload,
            string deviceMacAddress = "",
            MqttQualityOfServiceLevel qos = MqttQualityOfServiceLevel.AtMostOnce, 
            bool retain = false)
        {
            _deviceMacAddress = deviceMacAddress;
            if (_mqttClient != null && _mqttClient.IsConnected)
            {
                var message = new MqttApplicationMessageBuilder()
                    .WithTopic(topic)
                    .WithPayload(payload)
                    .WithQualityOfServiceLevel(qos)
                    .WithRetainFlag(retain)
                    .Build();

                //await _mqttClient.EnqueueAsync(topic, payload, qos, retain);

                await _mqttClient.EnqueueAsync(message);
                _logger.LogInformation($"Message published to topic '{topic}': {payload}");
            }
            else
            {
                _logger.LogWarning("MQTT client is not connected. Cannot publish message.");
            }
        }

        private void LogAsync(string threshold, string message)
        {
            try
            {
                if (_mqttClient != null && _mqttClient.IsConnected)
                {
                    //namespace/group_id/message_type/edge_node_id/[device_id]
                    //topic: spBv1.0/{GroupID}/+/{EdgeID}/#
                    //namespace/group_id/DDATA/edge_node_id/device_id
                    var payload = DateTime.Now.ToString(CultureInfo.InvariantCulture) + "|" + DeviceIdMqtt + "|" +
                                  threshold +
                                  "|" + message;
                    //
                    Task.Run(() =>
                        _mqttClient.EnqueueAsync("smartreader/log/" + DeviceIdMqtt, payload,
                            MqttQualityOfServiceLevel.AtLeastOnce));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MqttLog: Unexpected error. " + ex.Message);
            }
        }

        public async Task DisconnectAsync()
        {
            _isStopping = true;

            try
            {
                await _mqttClient.StopAsync();
                _logger.LogInformation("MQTT client disconnected.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to disconnect MQTT client.");
            }
        }

        // Event handler: Called when the client successfully connects.
        private Task OnConnectedAsync(MqttClientConnectedEventArgs args)
        {
            _logger.LogInformation("MQTT client connected successfully.");
            return Task.CompletedTask;
        }

        // Event handler: Called when the client disconnects.
        private async Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs args)
        {
            _logger.LogWarning($"MQTT client disconnected: {args.Reason}");

            if (!_isStopping)
            {
                _logger.LogInformation("Attempting to reconnect...");

                // Reconnect logic with exponential backoff
                //for (int attempt = 1; attempt <= 5; attempt++)
                //{
                //    try
                //    {
                //        if (_mqttClientOptions != null)
                //        {
                //            await _mqttClient.StartAsync(_mqttClientOptions);
                //            _logger.LogInformation("Reconnected to MQTT broker.");
                //            return;
                //        }
                //    }
                //    catch (Exception ex)
                //    {
                //        _logger.LogError(ex, $"Reconnection attempt {attempt} failed.");

                //        // Wait before retrying (exponential backoff)
                //        int delay = Math.Min(1000 * (int)Math.Pow(2, attempt), 30000); // Max delay: 30 seconds
                //        await Task.Delay(delay);
                //    }
                //}
            }
        }

    }

}
