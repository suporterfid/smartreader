using Newtonsoft.Json.Linq;
using SmartReader.Infrastructure.ViewModel;
using SuperSimpleTcp;
using System.Collections.Concurrent;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

namespace SmartReaderStandalone.Services
{
    public interface ITcpSocketService : IMetricProvider
    {
        Task EnsureSocketServerConnectionAsync();
        bool IsHealthy();
        bool IsSocketServerHealthy();
        bool IsSocketServerConnectedToClients();

        Task<int> ProcessMessageBatchAsync(BoundedConcurrentQueue<JObject> messageQueue, int batchSize, int maxQueueSize);
        void InitializeSocketServer();
        void Start(int port);
        void Stop();
        bool TryRestartSocketServer();
        Task<bool> TrySetSocketProcessingFlagAsync(int timeoutMilliseconds);
    }

    public class TcpSocketService : ITcpSocketService
    {
        public IServiceProvider Services { get; }

        private IConfiguration _configuration;

        private readonly ILogger<TcpSocketService> _logger;
        private readonly ISmartReaderConfigurationService _configurationService;

        private SimpleTcpServer? _tcpServer;

        private StandaloneConfigDTO _standaloneConfigDTO;

        private readonly SemaphoreSlim _socketLock = new(1, 1);

        private volatile bool _isSocketProcessing = false;

        private long _totalMessagesSent = 0;
        private DateTime _lastMessageSentTime = DateTime.MinValue;
        private DateTime _serverStartTime = DateTime.MinValue;

        public TcpSocketService(IServiceProvider services,
            IConfiguration configuration,
            ILogger<TcpSocketService> logger,
            ISmartReaderConfigurationService configurationService)
        {
            _configuration = configuration;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            Services = services;
            _configurationService = configurationService;

            _standaloneConfigDTO = _configurationService.LoadConfig();
        }


        private readonly BoundedConcurrentQueue<JObject> _messageQueue = new BoundedConcurrentQueue<JObject>(1000); // Adjust capacity as needed

        private readonly TokenBucketRateLimiter _sendRateLimiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokensPerPeriod = 10,
            QueueLimit = 0,
            AutoReplenishment = true,
            TokenLimit = 10, // Adjust as needed
            ReplenishmentPeriod = TimeSpan.FromSeconds(1), // Adjust as needed            
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst
        });

        public void InitializeSocketServer()
        {
            if (_standaloneConfigDTO == null ||
                !string.Equals("1", _standaloneConfigDTO.socketServer, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _tcpServer = CreateSocketServer();
            StartTcpSocketServer(_tcpServer);
        }



        private SimpleTcpServer CreateSocketServer()
        {
            var server = new SimpleTcpServer("*", int.Parse(_standaloneConfigDTO.socketPort));
            server.Settings.NoDelay = true;
            // server.Settings.IdleClientTimeoutMs = 30000; // Enable client timeout
            // server.Settings.IdleClientEvaluationIntervalMs = 10000; // Adjust interval as needed
            return server;
        }

        public void Start(int port)
        {
            if (_tcpServer == null)
            {
                _tcpServer = CreateSocketServer();
            }
            StartTcpSocketServer(_tcpServer);
            _serverStartTime = DateTime.UtcNow;  // Track when server starts
            _logger.LogInformation($"Socket server started on {port}");
        }

        public void Stop()
        {
            if (_tcpServer != null)
            {
                StopTcpSocketServer();
                _logger.LogInformation("Socket server stopped.");
            }
        }

        private void StartTcpSocketServer(SimpleTcpServer socketServer)
        {
            if (_standaloneConfigDTO != null && !string.IsNullOrEmpty(_standaloneConfigDTO.socketServer)
                                             && string.Equals("1", _standaloneConfigDTO.socketServer,
                                                 StringComparison.OrdinalIgnoreCase))
                try
                {
                    if (socketServer == null)
                    {
                        socketServer = new SimpleTcpServer("*", int.Parse(_standaloneConfigDTO.socketPort));

                        //_tcpServer.Settings.NoDelay = true;
                        //_tcpServer.Settings.IdleClientTimeoutMs = 30000;
                        //_tcpServer.Settings.IdleClientEvaluationIntervalMs = 10000;

                        // _logger.LogInformation("Creating tcp socket server. " + _standaloneConfigDTO.socketPort, SeverityType.Debug);
                        //_logger.LogInformation("Creating tcp socket server. [" + _standaloneConfigDTO.socketPort + "] ");
                        _logger.LogInformation("Creating tcp socket server. [" + _standaloneConfigDTO.socketPort + "] ");
                        //_tcpServer = new SimpleTcpServer("0.0.0.0:" + _standaloneConfigDTO.socketPort);
                        // set events
                        socketServer.Events.ClientConnected += ClientConnected!;
                        socketServer.Events.ClientDisconnected += ClientDisconnected!;
                        socketServer.Events.DataReceived += DataReceived!;
                        socketServer.Events.DataSent += DataSent!;

                        socketServer.Keepalive.EnableTcpKeepAlives = true;
                        socketServer.Keepalive.TcpKeepAliveInterval = 5;      // seconds to wait before sending subsequent keepalive
                        socketServer.Keepalive.TcpKeepAliveTime = 10;          // seconds to wait before sending a keepalive
                        socketServer.Keepalive.TcpKeepAliveRetryCount = 5;    // number of failed keepalive probes before terminating connection
                    }

                    if (socketServer != null && !socketServer.IsListening)
                    {
                        //_logger.LogInformation("Starting tcp socket server. " + _standaloneConfigDTO.socketPort, SeverityType.Debug);
                        _logger.LogInformation("Starting tcp socket server. [" + _standaloneConfigDTO.socketPort + "] ");
                        socketServer.Start();
                        _serverStartTime = DateTime.UtcNow; // Reset start time
                        //_ = ProcessGpoErrorPortRecoveryAsync();
                    }
                }
                catch (Exception ex)
                {
                    //_logger.LogInformation("Error starting tcp socket server. " + ex.Message, SeverityType.Error);
                    _logger.LogError(ex, "Error starting tcp socket server. [" + _standaloneConfigDTO.socketPort + "] ");
                }
        }

        private void StopTcpSocketServer()
        {
            try
            {
                if (_tcpServer != null)
                {
                    _tcpServer.Events.ClientConnected -= ClientConnected!;
                    _tcpServer.Events.ClientDisconnected -= ClientDisconnected!;
                    _tcpServer.Events.DataReceived -= DataReceived!;
                    _tcpServer.Stop();
                }
            }
            catch (Exception ex)
            {
                //_logger.LogInformation("Error stoping tcp socket server. " + ex.Message, SeverityType.Error);
                _logger.LogError(ex, "Error stoping tcp socket server. [" + _standaloneConfigDTO.socketPort + "] ");
            }
        }

        public bool IsHealthy() => _tcpServer?.IsListening == true;

        // Add health check method
        public bool IsSocketServerHealthy()
        {
            // Ensure server exists and is listening
            if (_tcpServer == null || !_tcpServer.IsListening)
            {
                _logger.LogWarning("Socket server is null or not listening.");
                return false;
            }

            try
            {
                // Retrieve the list of connected clients
                var clients = _tcpServer.GetClients();
                if (clients == null || !clients.Any())
                {
                    _logger.LogDebug("IsSocketServerHealthy: Socket server has no connected clients.");
                }
                else
                {
                    // Perform a simple health check on connected clients
                    foreach (var client in clients)
                    {
                        if (string.IsNullOrWhiteSpace(client))
                        {
                            _logger.LogWarning($"Invalid or disconnected client: {client}");
                            continue;
                        }
                        // try sending a lightweight message or ping to confirm client health
                        try
                        {
                            byte[] messageBytes = Encoding.UTF8.GetBytes("\n");
                            _tcpServer.Send(client, messageBytes);
                        }
                        catch (Exception exSocketTest)
                        {
                            _logger.LogError(exSocketTest, $"Unable to verify client: {client}");
                            continue;
                        }
                    }
                }



                // If all checks pass, the server is healthy
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while checking socket server health.");
                return false;
            }
        }

        public bool IsSocketServerConnectedToClients()
        {
            bool isConnected;
            // Ensure server exists and is listening
            if (_tcpServer == null || !_tcpServer.IsListening)
            {
                _logger.LogWarning("Socket server is null or not listening.");
                isConnected = false;
                return isConnected;
            }

            try
            {
                // Retrieve the list of connected clients
                var clients = _tcpServer.GetClients();
                if (clients == null || !clients.Any())
                {
                    _logger.LogDebug("Check for connections could not detect Socket clients connected.");
                    isConnected = false;
                }
                else
                {
                    isConnected = true;

                }

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while checking socket server health.");
                isConnected = false;
            }

            // If all checks pass, the server is healthy
            return isConnected;
        }



        public async Task EnsureSocketServerConnectionAsync()
        {
            if (_standaloneConfigDTO != null && !string.IsNullOrEmpty(_standaloneConfigDTO.socketServer)
                                             && string.Equals("1", _standaloneConfigDTO.socketServer,
                                                 StringComparison.OrdinalIgnoreCase))
            {
                if (!IsSocketServerHealthy())
                {
                    await _socketLock.WaitAsync();
                    try
                    {
                        // Double check after acquiring lock
                        if (!IsSocketServerHealthy())
                        {
                            _logger.LogWarning("Restarting Socket Server based on health check.");
                            StopTcpSocketServer();
                            await Task.Delay(1000); // Cool down
                            StartTcpSocketServer(_tcpServer);
                        }
                    }
                    finally
                    {
                        _ = _socketLock.Release();
                    }
                }
                try
                {
                    if (_tcpServer != null)
                    {
                        _logger.LogInformation("Socket Server Stat:");
                        _logger.LogInformation(_tcpServer.Statistics.ToString());


                        List<string> clients = _tcpServer.GetClients().ToList();
                        if (clients != null && clients.Count > 0)
                        {
                            _logger.LogInformation($"Socket clients connected: {clients.Count}");
                            foreach (string curr in clients)
                            {
                                _logger.LogInformation($"Client currently connect over Socket: {curr}");
                            }
                        }
                        else
                        {
                            _logger.LogInformation($"Socket clients connected: 0");
                        }
                    }

                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error verifying tcp socket server. ");
                }
            }

        }

        private void ClientConnected(object sender, ConnectionEventArgs e)
        {
            _logger.LogInformation($"Client connected from {e.IpPort}");
            var clients = _tcpServer?.GetClients()?.Count() ?? 0;
            _logger.LogInformation($"Total connected clients: {clients}");

            try
            {
                if (_standaloneConfigDTO != null &&
                    string.Equals("1", _standaloneConfigDTO.mqttEnabled, StringComparison.OrdinalIgnoreCase))
                {
                    var mqttManagementEvents = new Dictionary<string, object> {
                    { "smartreader-socket-status", "client-disconnected" },
                    { "client-ip", e.IpPort },
                    { "disconnect-reason", e.Reason },
                    { "connected-clients", clients }
                };
                    // PublishMqttManagementEventAsync(mqttManagementEvents).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to publish client connection event");
            }
        }


        private void ClientDisconnected(object sender, ConnectionEventArgs e)
        {
            _logger.LogWarning($"Client disconnected from {e.IpPort}. Reason: {e.Reason}");
            var clients = _tcpServer?.GetClients()?.Count() ?? 0;
            _logger.LogInformation($"Remaining connected clients: {clients}");

            try
            {
                if (clients == 0)
                {
                    _logger.LogWarning("No Socket clients connected - messages will be queued");
                }

                if (_standaloneConfigDTO != null &&
                    string.Equals("1", _standaloneConfigDTO.mqttEnabled, StringComparison.OrdinalIgnoreCase))
                {
                    var mqttManagementEvents = new Dictionary<string, object> {
                    { "smartreader-socket-status", "client-disconnected" },
                    { "client-ip", e.IpPort },
                    { "disconnect-reason", e.Reason },
                    { "connected-clients", clients }
                };
                    // PublishMqttManagementEventAsync(mqttManagementEvents).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to publish client disconnection event");
            }
        }

        private void DataReceived(object sender, DataReceivedEventArgs e)
        {

            _logger.LogInformation($"Socket Server - Data Received: [ {e.IpPort} ]:  {Encoding.UTF8.GetString(e.Data)}");
        }

        private void DataSent(object sender, DataSentEventArgs e)
        {
            _logger.LogInformation($"Socket Server - Data Sent: [ {e.IpPort} ]:  {e.BytesSent}");

        }

        public async Task<bool> TrySetSocketProcessingFlagAsync(int timeoutMilliseconds)
        {
            var start = DateTime.UtcNow;
            while (_isSocketProcessing)
            {
                if ((DateTime.UtcNow - start).TotalMilliseconds > timeoutMilliseconds)
                {
                    return false; // Timeout reached
                }
                await Task.Delay(100); // Poll every 100ms
            }
            _isSocketProcessing = true;
            return true;
        }

        public bool TryRestartSocketServer()
        {
            try
            {
                _logger.LogWarning("Trying to restart the Socket Server.");
                StartTcpSocketServer(_tcpServer);
                if (_tcpServer == null || !_tcpServer.IsListening)
                {
                    _logger.LogError("Failed to restart socket server");
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error restarting socket server");
                return false;
            }
        }

        public async Task<int> ProcessMessageBatchAsync(
            BoundedConcurrentQueue<JObject> messageQueue,
            int batchSize,
            int maxQueueSize)
        {
            try
            {
                // Use a lock to prevent multiple concurrent access.
                if (!await _socketLock.WaitAsync(TimeSpan.FromSeconds(5)))
                {
                    _logger.LogDebug("Another process is already handling socket messages.");
                    return 0;
                }

                try
                {
                    int processedCount = 0;
                    // Configure timeouts and retry parameters
                    const int SendTimeoutMs = 5000; // 5 second timeout for sends
                    const int MaxRetries = 1;
                    const int RetryDelayMs = 1000; // 1 second between retries


                    // Use the bounded queue for better memory management
                    while (processedCount < batchSize 
                        && messageQueue.TryDequeue(out var message))
                    {
                        try
                        {
                            if (_tcpServer?.GetClients()?.Any() ?? false)
                            {
                                // Retrieve clients only once per batch
                                var clients = _tcpServer.GetClients().ToList();
                                string jsonMessage = message.ToString(Newtonsoft.Json.Formatting.None);
                                var line = SmartReaderJobs.Utils.Utils.ExtractLineFromJsonObject(message, _standaloneConfigDTO, _logger);
                                if (string.IsNullOrEmpty(line))
                                {
                                    _logger.LogWarning("Empty line extracted from event data");
                                    continue;
                                }

                                byte[] messageBytes = Encoding.UTF8.GetBytes(line);
                                // Use rate limiter to avoid overwhelming the network
                                var sendTasks = clients.Select(client => SendMessageAsync(client, messageBytes, SendTimeoutMs, MaxRetries, RetryDelayMs));
                                await Task.WhenAll(sendTasks);

                                processedCount++;
                            }
                            else
                            {
                                processedCount++;
                                _logger.LogWarning($"No clients connected; discarding {processedCount} messages.");
                                messageQueue.Clear();
                                break;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error processing socket message");
                        }
                    }
                    if (processedCount > 0)
                        _logger.LogDebug($"Processed {processedCount} socket message(s).");

                    return processedCount;
                }
                finally
                {
                    _socketLock.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ProcessMessageBatchAsync - Error processing socket messages");
                return 0;
            }
            
        }

        //Asynchronous sending
        private async Task SendMessageAsync(string client, byte[] messageBytes, int timeoutMs, int maxRetries, int retryDelayMs)
        {
            int retryCount = 0;
            while (retryCount < maxRetries)
            {
                try
                {
                    // Acquire a permit from the rate limiter
                    using RateLimitLease lease = await _sendRateLimiter.AcquireAsync(1);
                    if (!lease.IsAcquired)
                    {
                        // If a token is not available (e.g. due to cancellation or immediate rate limit rejection),
                        // handle the situation appropriately.
                        throw new Exception("Rate limit exceeded. Please try again later.");
                    }
                    using (var cts = new CancellationTokenSource(timeoutMs))
                    {
                        await _tcpServer.SendAsync(client, messageBytes, cts.Token);
                    }
                    // Update Metrics
                    Interlocked.Increment(ref _totalMessagesSent);
                    _lastMessageSentTime = DateTime.UtcNow;

                    return; // Success
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning($"Send operation timed out for client {client} after {timeoutMs}ms");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error sending to client {client} (attempt {retryCount + 1}/{maxRetries})");
                }
                retryCount++;
                await Task.Delay(retryDelayMs);
            }
            _logger.LogError($"Failed to send message to client {client} after {maxRetries} retries.");
        }
        public async Task<Dictionary<string, object>> GetMetricsAsync()
        {
            var metrics = new Dictionary<string, object>();

            try
            {
                metrics["Socket Server Healthy"] = IsSocketServerHealthy();
                metrics["Connected Clients"] = _tcpServer?.GetClients().Count() ?? 0;
                metrics["Message Queue Size"] = _messageQueue.Count;
                metrics["Total Messages Sent"] = _totalMessagesSent;
                metrics["Last Message Sent (Seconds Ago)"] = (_lastMessageSentTime == DateTime.MinValue)
                    ? -1 // No message has been sent
                    : (DateTime.UtcNow - _lastMessageSentTime).TotalSeconds;
                metrics["Socket Server Uptime (Seconds)"] = (_serverStartTime == DateTime.MinValue)
                    ? 0
                    : (DateTime.UtcNow - _serverStartTime).TotalSeconds;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error collecting socket server metrics");
            }

            return await Task.FromResult(metrics);
        }
    }


}
