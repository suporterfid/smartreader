using Newtonsoft.Json.Linq;
using SmartReader.Infrastructure.ViewModel;
using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text;

namespace SmartReaderStandalone.Services
{
    public interface IWebSocketService
    {
        Task EnsureSocketServerConnectionAsync();
        bool IsHealthy();
        bool IsSocketServerHealthy();
        bool IsSocketServerConnectedToClients();

        Task<int> ProcessMessageBatchAsync(ConcurrentQueue<JObject> messageQueue, int batchSize, int maxQueueSize);
        void InitializeSocketServer();
        void Start(int port);
        void Stop();
        bool TryRestartSocketServer();
        Task<bool> TrySetSocketProcessingFlagAsync(int timeoutMilliseconds);
    }

    public class WebSocketService : IWebSocketService
    {
        private readonly IServiceProvider _services;
        private readonly IConfiguration _configuration;
        private readonly ILogger<WebSocketService> _logger;
        private readonly IConfigurationService _configurationService;

        private HttpListener? _httpListener;
        private ConcurrentDictionary<string, WebSocket> _connectedClients;
        private CancellationTokenSource? _serverCancellation;
        private readonly SemaphoreSlim _socketLock = new(1, 1);
        private volatile bool _isSocketProcessing = false;
        private StandaloneConfigDTO _standaloneConfigDTO;

        public WebSocketService(
            IServiceProvider services,
            IConfiguration configuration,
            ILogger<WebSocketService> logger,
            IConfigurationService configurationService)
        {
            _services = services;
            _configuration = configuration;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _configurationService = configurationService;
            _connectedClients = new ConcurrentDictionary<string, WebSocket>();
            _standaloneConfigDTO = _configurationService.LoadConfig();
        }

        public void InitializeSocketServer()
        {
            if (_standaloneConfigDTO == null ||
                !string.Equals("1", _standaloneConfigDTO.webSocketServer, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            Start(int.Parse(_standaloneConfigDTO.webSocketPort));
        }

        private const int DEFAULT_PORT = 50080;

        public void Start(int port = DEFAULT_PORT)
        {
            if (_httpListener != null && _httpListener.IsListening)
            {
                _logger.LogWarning("WebSocket server is already running.");
                return;
            }

            if (port <= 0)
            {
                _logger.LogWarning($"Invalid port {port} specified, using default port {DEFAULT_PORT}");
                port = DEFAULT_PORT;
            }

            _serverCancellation = new CancellationTokenSource();
            _httpListener = new HttpListener();
            _httpListener.Prefixes.Add($"http://*:{port}/ws/");

            try
            {
                _httpListener.Start();
                _logger.LogInformation($"WebSocket server started on port {port}");
                _ = AcceptWebSocketClientsAsync(_serverCancellation.Token);
            }
            catch (HttpListenerException ex) when (ex.ErrorCode == 5)
            {
                _logger.LogError(ex, "Access denied when starting the WebSocket server. " +
                    "Ensure the URL prefix is reserved for your user. " +
                    "You can reserve it by running (windows example): netsh http add urlacl url=http://*:50080/ws/ user=YourUserName");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to start WebSocket server on port {port}");
                throw;
            }
        }



        private async Task AcceptWebSocketClientsAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var context = await _httpListener!.GetContextAsync();
                    if (context.Request.IsWebSocketRequest)
                    {
                        _ = HandleWebSocketClientAsync(context, cancellationToken);
                    }
                    else
                    {
                        context.Response.StatusCode = 400;
                        context.Response.Close();
                    }
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    _logger.LogError(ex, "Error accepting WebSocket client");
                }
            }
        }

        private async Task HandleWebSocketClientAsync(HttpListenerContext context, CancellationToken cancellationToken)
        {
            WebSocket? webSocket = null;
            string clientId = string.Empty;

            try
            {
                var webSocketContext = await context.AcceptWebSocketAsync(null);
                webSocket = webSocketContext.WebSocket;
                clientId = $"{context.Request.RemoteEndPoint}";

                _ = _connectedClients.TryAdd(clientId, webSocket);
                _logger.LogInformation($"Client connected from {clientId}");

                // Keep the connection alive and handle incoming messages
                var buffer = new byte[1024 * 4];
                while (webSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
                {
                    var result = await webSocket.ReceiveAsync(
                        new ArraySegment<byte>(buffer), cancellationToken);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await HandleClientDisconnectionAsync(clientId, webSocket);
                        break;
                    }
                }
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogError(ex, $"Error handling WebSocket client {clientId}");
                await HandleClientDisconnectionAsync(clientId, webSocket);
            }
        }

        private async Task HandleClientDisconnectionAsync(string clientId, WebSocket? webSocket)
        {
            try
            {
                if (webSocket != null)
                {
                    await webSocket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Closing connection",
                        CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error closing WebSocket for client {clientId}");
            }
            finally
            {
                _ = _connectedClients.TryRemove(clientId, out _);
                _logger.LogInformation($"Client disconnected: {clientId}");
            }
        }

        public void Stop()
        {
            // Cancel any pending operations
            _serverCancellation?.Cancel();

            // Disconnect all client connections
            foreach (var client in _connectedClients)
            {
                _ = HandleClientDisconnectionAsync(client.Key, client.Value);
            }

            // Properly stop and dispose the HttpListener
            if (_httpListener != null)
            {
                if (_httpListener.IsListening)
                {
                    _httpListener.Stop();
                }
                _httpListener.Close(); // Ensures the listener is disposed and the port is freed
                _httpListener = null;
            }

            _logger.LogInformation("WebSocket server stopped");
        }



        public bool IsHealthy() => _httpListener?.IsListening == true;

        public bool IsSocketServerHealthy()
        {
            if (_httpListener == null || !_httpListener.IsListening)
            {
                _logger.LogWarning("WebSocket server is null or not listening");
                return false;
            }

            try
            {
                // Check if we have any active connections
                var activeConnections = _connectedClients.Count(c =>
                    c.Value.State == WebSocketState.Open);

                if (activeConnections == 0)
                {
                    _logger.LogWarning("WebSocket server has no connected clients");
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking WebSocket server health");
                return false;
            }
        }

        public bool IsSocketServerConnectedToClients()
        {
            try
            {
                return _connectedClients.Any(c => c.Value.State == WebSocketState.Open);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking WebSocket client connections");
                return false;
            }
        }

        public async Task EnsureSocketServerConnectionAsync()
        {
            if (_standaloneConfigDTO != null &&
                string.Equals("1", _standaloneConfigDTO.webSocketServer, StringComparison.OrdinalIgnoreCase))
            {
                if (!IsSocketServerHealthy())
                {
                    await _socketLock.WaitAsync();
                    try
                    {
                        if (!IsSocketServerHealthy())
                        {
                            _logger.LogWarning("Restarting WebSocket Server based on health check");
                            Stop();
                            await Task.Delay(1000); // Cool down
                            Start(int.Parse(_standaloneConfigDTO.webSocketPort));
                        }
                    }
                    finally
                    {
                        _ = _socketLock.Release();
                    }
                }

                // Log connection statistics
                var activeConnections = _connectedClients.Count(c =>
                    c.Value.State == WebSocketState.Open);
                _logger.LogInformation($"Active WebSocket connections: {activeConnections}");

                foreach (var client in _connectedClients)
                {
                    _logger.LogInformation($"Client connected: {client.Key}, State: {client.Value.State}");
                }
            }
        }

        public async Task<bool> TrySetSocketProcessingFlagAsync(int timeoutMilliseconds)
        {
            var start = DateTime.UtcNow;
            while (_isSocketProcessing)
            {
                if ((DateTime.UtcNow - start).TotalMilliseconds > timeoutMilliseconds)
                {
                    return false;
                }
                await Task.Delay(100);
            }
            _isSocketProcessing = true;
            return true;
        }

        public bool TryRestartSocketServer()
        {
            try
            {
                _logger.LogWarning("Trying to restart the WebSocket Server");
                Stop();
                // Allow time for the port to be released by the OS
                Thread.Sleep(1000);
                Start(int.Parse(_standaloneConfigDTO.webSocketPort));
                return IsSocketServerHealthy();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error restarting WebSocket server");
                return false;
            }
        }


        public async Task<int> ProcessMessageBatchAsync(
            ConcurrentQueue<JObject> messageQueue,
            int batchSize,
            int maxQueueSize)
        {
            if (!await _socketLock.WaitAsync(TimeSpan.FromSeconds(5)))
            {
                _logger.LogDebug("Another process is already handling WebSocket messages");
                return 0;
            }

            try
            {
                int processedCount = 0;
                const int SendTimeoutMs = 5000;
                const int MaxRetries = 1;
                const int RetryDelayMs = 1000;

                while (processedCount < batchSize && messageQueue.TryDequeue(out var message))
                {
                    if (_connectedClients.Any(c => c.Value.State == WebSocketState.Open))
                    {
                        _logger.LogInformation($"Publishing data to {_connectedClients.Count} clients. Queue size: {messageQueue.Count}");

                        var line = SmartReaderJobs.Utils.Utils.ExtractLineFromJsonObject(
                            message, _standaloneConfigDTO, _logger);

                        if (string.IsNullOrEmpty(line))
                        {
                            _logger.LogWarning("Empty line extracted from event data");
                            continue;
                        }

                        byte[] messageBytes = Encoding.UTF8.GetBytes(line);
                        var messageSegment = new ArraySegment<byte>(messageBytes);

                        foreach (var client in _connectedClients)
                        {
                            if (client.Value.State != WebSocketState.Open)
                            {
                                continue;
                            }

                            int retryCount = 0;
                            bool sendSuccess = false;

                            while (!sendSuccess && retryCount < MaxRetries)
                            {
                                try
                                {
                                    using var cts = new CancellationTokenSource(SendTimeoutMs);

                                    await client.Value.SendAsync(
                                        messageSegment,
                                        WebSocketMessageType.Text,
                                        true,
                                        cts.Token);

                                    sendSuccess = true;
                                    _logger.LogDebug($"Successfully sent message to client {client.Key} after {retryCount} retries");
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(ex, $"Error sending to client {client.Key} (attempt {retryCount + 1}/{MaxRetries})");
                                    retryCount++;

                                    if (retryCount < MaxRetries)
                                    {
                                        await Task.Delay(RetryDelayMs);
                                    }
                                }
                            }
                        }

                        processedCount++;
                    }
                    else
                    {
                        _logger.LogWarning("No clients connected; discarding message");
                        break;
                    }
                }

                if (processedCount > 0)
                {
                    _logger.LogInformation($"Processed {processedCount} WebSocket message(s)");
                }

                return processedCount;
            }
            finally
            {
                _ = _socketLock.Release();
            }
        }
    }
}