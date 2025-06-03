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
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Extensions.ManagedClient;
using MQTTnet.Packets;
using MQTTnet.Protocol;
using Newtonsoft.Json;
using SmartReader.Infrastructure.ViewModel;
using System.Globalization;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;

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

    public interface IMqttService : IMetricProvider
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


        // Event to notify subscribers when a message is received with standardized parameters
        public event EventHandler<MqttMessageEventArgs>? MessageReceived;

        public event EventHandler<MqttErrorEventArgs>? GpoErrorRequested;


        public MqttService(ILogger<MqttService> logger, StandaloneConfigDTO standaloneConfigDTO)
        {
            _mqttClient = new MqttFactory().CreateManagedMqttClient();
            _logger = logger;

            // Subscribe to client events.
            _mqttClient.ConnectedAsync += OnConnectedAsync;
            _mqttClient.DisconnectedAsync += OnDisconnected;
            _standaloneConfigDTO = standaloneConfigDTO;
        }

        public Task<Dictionary<string, object>> GetMetricsAsync()
        {
            var metrics = new Dictionary<string, object>
        {
            { "MQTT Connected", _mqttClient.IsConnected },
            { "Messages Pending", _mqttClient.PendingApplicationMessagesCount }
        };
            return Task.FromResult(metrics);
        }

        public List<MqttTopicFilter> BuildMqttTopicList(StandaloneConfigDTO smartReaderSetupData)
        {
            try
            {
                var mqttTopicFilters = new List<MqttTopicFilter>();

                var managementCommandQoslevel = 0;
                var controlCommandQoslevel = 0;

                _ = int.TryParse(smartReaderSetupData.mqttManagementCommandQoS, out managementCommandQoslevel);
                _ = int.TryParse(smartReaderSetupData.mqttControlCommandQoS, out controlCommandQoslevel);

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
                            controlCommandsFilter = new MqttTopicFilterBuilder().WithTopic($"{mqttControlCommandTopic}")
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
                // Armazenar o ID do dispositivo e as configurações
                DeviceIdMqtt = mqttClientId;
                _standaloneConfigDTO = standaloneConfigDTO;

                // Usar o nome do leitor como ID cliente se não foi fornecido
                if (string.IsNullOrEmpty(mqttClientId))
                {
                    mqttClientId = _standaloneConfigDTO.readerName;
                }

                // Extrair parâmetros de configuração
                var mqttBrokerAddress = _standaloneConfigDTO.mqttBrokerAddress;
                var mqttBrokerPort = int.Parse(_standaloneConfigDTO.mqttBrokerPort);
                var mqttUsername = _standaloneConfigDTO.mqttUsername;
                var mqttPassword = _standaloneConfigDTO.mqttPassword;
                var mqttTagEventsTopic = _standaloneConfigDTO.mqttTagEventsTopic;
                var mqttTagEventsQoS = int.Parse(_standaloneConfigDTO.mqttTagEventsQoS);
                var lastWillMessage = BuildMqttLastWillMessage();

                // Período Keep-Alive (padrão: 30 segundos)
                int mqttKeepAlivePeriod = 30;
                _ = int.TryParse(_standaloneConfigDTO.mqttBrokerKeepAlive, out mqttKeepAlivePeriod);

                // Determinar o protocolo
                var protocol = (_standaloneConfigDTO.mqttBrokerProtocol ?? "mqtt").ToLower();
                bool isWebSocket = protocol.Contains("ws");
                bool isSecure = protocol.Contains("mqtts") || protocol.Contains("wss");

                // Iniciar construtor de opções
                var clientOptionsBuilder = new MqttClientOptionsBuilder()
                    .WithKeepAlivePeriod(new TimeSpan(0, 0, 0, mqttKeepAlivePeriod))
                    .WithClientId(mqttClientId)
                    .WithWillPayload(lastWillMessage.PayloadSegment)
                    .WithWillQualityOfServiceLevel(lastWillMessage.QualityOfServiceLevel)
                    .WithWillTopic(lastWillMessage.Topic)
                    .WithWillRetain(lastWillMessage.Retain)
                    .WithWillContentType(lastWillMessage.ContentType);

                // Configurar servidor (WebSocket ou TCP)
                if (isWebSocket)
                {
                    // Determinar caminho do WebSocket
                    var mqttBrokerWebSocketPath = "/mqtt";
                    if (!string.IsNullOrEmpty(_standaloneConfigDTO.mqttBrokerWebSocketPath))
                    {
                        mqttBrokerWebSocketPath = _standaloneConfigDTO.mqttBrokerWebSocketPath;
                    }

                    // Configurar servidor WebSocket
                    _ = clientOptionsBuilder.WithWebSocketServer(o => o.WithUri($"{mqttBrokerAddress}:{mqttBrokerPort}{mqttBrokerWebSocketPath}"));
                }
                else
                {
                    // Configurar servidor TCP
                    _ = clientOptionsBuilder.WithTcpServer(mqttBrokerAddress, mqttBrokerPort);
                }

                // Configurar credenciais (se fornecidas)
                if (!string.IsNullOrEmpty(mqttUsername))
                {
                    _ = clientOptionsBuilder.WithCredentials(mqttUsername, mqttPassword);
                }

                // Configurar opções TLS (se usando SSL)
                if (isSecure)
                {
                    _ = clientOptionsBuilder.WithTlsOptions(o =>
                    {
                        //// Validação de certificados
                        //if (_standaloneConfigDTO.mqttAllowUntrustedCertificates == "1")
                        //{
                        //    _ = o.WithCertificateValidationHandler(_ => true);
                        //}

                        _ = o.WithAllowUntrustedCertificates(true);
                        _ = o.WithCertificateValidationHandler(_ => true);
                        // Forçar versão do protocolo TLS
                        _ = o.WithSslProtocols(SslProtocols.Tls12);

                        // Adicionar certificado cliente se configurado

                        if (!string.IsNullOrEmpty(_standaloneConfigDTO.mqttSslClientCertificate))
                        {

                            try
                            {
                                //var clientCert = new X509Certificate2(
                                //    _standaloneConfigDTO.mqttSslClientCertificate,
                                //    _standaloneConfigDTO.mqttSslClientCertificatePassword ?? string.Empty
                                //);


                                //// Criar uma coleção contendo o certificado
                                //var clientCertificates = new List<X509Certificate2> { clientCert };

                                //var caChain = new X509Certificate2Collection();
                                //caChain.ImportFromPem(_standaloneConfigDTO.mqttSslCaCertificate);
                                //_ = o.WithTrustChain(caChain);

                                List<X509Certificate2> certs = new List<X509Certificate2>
                                {
                                    new X509Certificate2(_standaloneConfigDTO.mqttSslClientCertificate, _standaloneConfigDTO.mqttSslClientCertificatePassword ?? string.Empty, X509KeyStorageFlags.Exportable)
                                };



                                // Passar a coleção diretamente para o método
                                _ = o.WithClientCertificates(certs);


                            }
                            catch (Exception certEx)
                            {
                                _logger.LogError(certEx, "Erro ao carregar certificado cliente MQTT");
                            }
                        }

                    });
                }

                // Criar opções para cliente gerenciado
                _mqttClientOptions = new ManagedMqttClientOptionsBuilder()
                    .WithClientOptions(clientOptionsBuilder.Build())
                    .Build();

                // Criar e iniciar cliente MQTT
                _mqttClient = new MqttFactory().CreateManagedMqttClient();

                // Configurar manipuladores de eventos
                ConfigureEventHandlers();

                // Iniciar o cliente
                await _mqttClient.StartAsync(_mqttClientOptions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro inesperado ao conectar ao broker MQTT: " + ex.Message);
            }
        }

        // Método auxiliar para configurar manipuladores de eventos
        private void ConfigureEventHandlers()
        {
            // Manipulador para mensagens recebidas
            _mqttClient.ApplicationMessageReceivedAsync += async e =>
            {
                _logger.LogInformation("### MENSAGEM RECEBIDA ###");
                _logger.LogInformation($"+ Tópico = {e.ApplicationMessage.Topic}");
                if (e.ApplicationMessage.PayloadSegment != null)
                    _logger.LogInformation($"+ Conteúdo = {Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment)}");

                _logger.LogInformation($"+ QoS = {e.ApplicationMessage.QualityOfServiceLevel}");
                _logger.LogInformation($"+ Retain = {e.ApplicationMessage.Retain}");
                _logger.LogInformation($"+ ClientId = {e.ClientId}");

                var payload = "";
                if (e.ApplicationMessage.PayloadSegment != null)
                    payload = Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment);
                try
                {
                    MessageReceived?.Invoke(this, new MqttMessageEventArgs(e.ClientId, e.ApplicationMessage.Topic, payload));
                }
                catch (Exception ex)
                {
                    _logger.LogError("Erro ao processar mensagem recebida: " + ex.Message);
                }

                await Task.CompletedTask;
            };

            // Manipulador para conexão bem-sucedida
            _mqttClient.ConnectedAsync += async e =>
            {
                _logger.LogInformation($"### CONECTADO AO SERVIDOR ### {_standaloneConfigDTO.mqttBrokerAddress}");

                var mqttManagementEvents = new Dictionary<string, object>
        {
            { "smartreader-mqtt-status", "connected" },
            { "readerName", _standaloneConfigDTO.readerName },
            { "mac", _deviceMacAddress },
            { "timestamp", SmartReaderJobs.Utils.Utils.CSharpMillisToJavaLongMicroseconds(DateTime.Now) }
        };
                await PublishMqttManagementEventAsync(mqttManagementEvents);

                try
                {
                    var mqttTopicFilters = BuildMqttTopicList(_standaloneConfigDTO);
                    await _mqttClient.SubscribeAsync(mqttTopicFilters.ToArray());
                    _logger.LogInformation("### Inscrito nos tópicos. ###");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Erro ao se inscrever nos tópicos: " + ex.Message);
                }

                await Task.CompletedTask;
            };

            // Manipulador para desconexão
            _mqttClient.DisconnectedAsync += async e =>
            {
                _logger.LogInformation($"### DESCONECTADO DO SERVIDOR ### {_standaloneConfigDTO.mqttBrokerAddress}");

                try
                {
                    var disconnectDetails = $"Desconexão: ConnectResult {e.ConnectResult} \n";
                    disconnectDetails += $"Desconexão: Reason {e.Reason} \n";
                    if (e.ConnectResult != null)
                    {
                        disconnectDetails += $" ResultCode {e.ConnectResult.ResultCode} \n";
                    }
                    disconnectDetails += $" ClientWasConnected {e.ClientWasConnected} \n";
                    if (e.Exception != null && !string.IsNullOrEmpty(e.Exception.Message))
                    {
                        disconnectDetails += $" Message {e.Exception.Message} \n";
                    }

                    _logger.LogInformation($"### Detalhes {disconnectDetails}");
                    GpoErrorRequested?.Invoke(this, new MqttErrorEventArgs(disconnectDetails));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Erro ao processar evento de desconexão MQTT.");
                }

                await Task.CompletedTask;
            };

            // Manipulador para falha na conexão
            _mqttClient.ConnectingFailedAsync += async e =>
            {
                _logger.LogInformation($"### FALHA NA CONEXÃO ### {_standaloneConfigDTO.mqttBrokerAddress}");

                try
                {
                    var disconnectDetails = $"FALHA NA CONEXÃO: ConnectResult {e.ConnectResult} \n";
                    _logger.LogInformation($"### Detalhes {disconnectDetails}");
                    GpoErrorRequested?.Invoke(this, new MqttErrorEventArgs(disconnectDetails));
                }
                catch (Exception)
                {
                    // Ignora exceções no manipulador
                }

                await Task.CompletedTask;
            };

            // Manipulador para mudança de estado da conexão
            _mqttClient.ConnectionStateChangedAsync += async e =>
            {
                _logger.LogInformation($"### ESTADO DA CONEXÃO ALTERADO ### {_standaloneConfigDTO.mqttBrokerAddress}");

                try
                {
                    if (_mqttClient.InternalClient.IsConnected)
                    {
                        _logger.LogInformation("O cliente MQTT está conectado.");
                    }
                    else
                    {
                        _logger.LogInformation("O cliente MQTT está desconectado.");
                    }
                }
                catch (Exception ex)
                {
                    GpoErrorRequested?.Invoke(this, new MqttErrorEventArgs(ex.Message));
                }

                await Task.CompletedTask;
            };
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
                    _ = bool.TryParse(_standaloneConfigDTO.mqttLwtQoS, out lwtRetainMessage);
                }

            }
            var mqttManagementEvents = new Dictionary<string, string>
            {
                { "smartreader-mqtt-status", "disconnected" }
            };
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
                                _ = int.TryParse(_standaloneConfigDTO.mqttManagementEventsQoS, out qos);
                                _ = bool.TryParse(_standaloneConfigDTO.mqttManagementEventsRetainMessages, out retain);

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
                                _ = SpinWait.SpinUntil(() => _mqttClient.PendingApplicationMessagesCount == 0, 1000);
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
                    _ = Task.Run(() =>
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
        private Task OnDisconnected(MqttClientDisconnectedEventArgs args)
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

            return Task.CompletedTask;
        }

    }

}
