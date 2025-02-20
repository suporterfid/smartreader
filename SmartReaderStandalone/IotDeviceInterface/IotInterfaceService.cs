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
using Impinj.Atlas;
using Impinj.Utils.DebugLogger;
using Microsoft.EntityFrameworkCore;
using MQTTnet.Protocol;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using plugin_contract.ViewModel.Gpi;
using plugin_contract.ViewModel.Stream;
using Serilog;
using SmartReader.Infrastructure.Database;
using SmartReader.Infrastructure.ViewModel;
using SmartReader.ViewModel.Auth;
using SmartReaderJobs.Utils;
using SmartReaderJobs.ViewModel.Events;
using SmartReaderStandalone.Entities;
using SmartReaderStandalone.IotDeviceInterface;
using SmartReaderStandalone.Plugins;
using SmartReaderStandalone.Services;
using SmartReaderStandalone.Utils;
using SmartReaderStandalone.Utils.InputUsb;
using SmartReaderStandalone.ViewModel.Filter;
using SmartReaderStandalone.ViewModel.Gpo;
using SmartReaderStandalone.ViewModel.Read;
using SmartReaderStandalone.ViewModel.Read.Sku.Summary;
using SmartReaderStandalone.ViewModel.Status;
using SuperSimpleTcp;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO.Ports;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Timers;
using TagDataTranslation;
using static SmartReader.IotDeviceInterface.R700IotReader;
using DataReceivedEventArgs = SuperSimpleTcp.DataReceivedEventArgs;
using ReaderStatus = SmartReaderStandalone.Entities.ReaderStatus;
using Timer = System.Timers.Timer;

namespace SmartReader.IotDeviceInterface;

public class IotInterfaceService : BackgroundService, IServiceProviderIsService
{
    public class SocketServerHealth
    {
        public bool IsListening { get; set; }
        public int ConnectedClients { get; set; }
        public DateTime LastSuccessfulSend { get; set; }
        public int PendingMessages { get; set; }
        public int FailedSendsCount { get; set; }
    }

    private class ThreadSafeCollections
    {
        private readonly Microsoft.Extensions.Logging.ILogger _logger;
        private readonly ConcurrentDictionary<string, long> _readEpcs;
        private readonly ConcurrentDictionary<string, int> _currentSkus;
        private readonly ConcurrentBag<string> _currentSkuReadEpcs;

        public ThreadSafeCollections(
            Microsoft.Extensions.Logging.ILogger logger,
            ConcurrentDictionary<string, long> readEpcs,
            ConcurrentDictionary<string, int> currentSkus,
            ConcurrentBag<string> currentSkuReadEpcs)
        {
            _logger = logger;
            _readEpcs = readEpcs;
            _currentSkus = currentSkus;
            _currentSkuReadEpcs = currentSkuReadEpcs;
        }

        public bool TryAddEpc(string epc, long timestamp)
        {
            try
            {
                return _readEpcs.TryAdd(epc, timestamp);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding EPC to _readEpcs");
                return false;
            }
        }

        public bool TryUpdateEpc(string epc, long timestamp)
        {
            try
            {
                return _readEpcs.TryUpdate(epc, timestamp, _readEpcs[epc]);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating EPC in _readEpcs");
                return false;
            }
        }

        public void AddSkuReadEpc(string epc)
        {
            try
            {
                _currentSkuReadEpcs.Add(epc);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding EPC to _currentSkuReadEpcs");
            }
        }

        public bool UpdateSkuCount(string sku, int increment = 1)
        {
            try
            {
                _ = _currentSkus.AddOrUpdate(
                    sku,
                    increment,
                    (key, oldValue) => oldValue + increment
                );
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating SKU count");
                return false;
            }
        }

        public bool ContainsEpc(string epc)
        {
            return _currentSkuReadEpcs.Contains(epc);
        }

        public void Clear()
        {
            try
            {
                _readEpcs.Clear();
                _currentSkus.Clear();
                while (_currentSkuReadEpcs.TryTake(out _)) { }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error clearing collections");
            }
        }
    }

    private class SocketSendRetryPolicy
    {
        private const int MaxRetries = 3;
        private const int RetryDelayMs = 1000; // 1 second between retries
        private const int SendTimeoutMs = 5000; // 5 second timeout for sends

        public async Task<bool> ExecuteWithRetryAsync(
            SimpleTcpServer server,
            string client,
            byte[] data,
            Microsoft.Extensions.Logging.ILogger logger)
        {
            int retryCount = 0;
            bool sendSuccess = false;

            while (!sendSuccess && retryCount < MaxRetries)
            {
                try
                {
                    using var cts = new CancellationTokenSource(SendTimeoutMs);

                    // Wrap the sync Send in a task to support timeout
                    await Task.Run(() =>
                    {
                        server.Send(client, data);
                    }, cts.Token);

                    sendSuccess = true;
                    logger.LogDebug($"Successfully sent message to client after {retryCount} retries");
                }
                catch (OperationCanceledException)
                {
                    logger.LogWarning($"Send operation timed out after {SendTimeoutMs}ms");
                    retryCount++;
                    if (retryCount < MaxRetries)
                    {
                        await Task.Delay(RetryDelayMs);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, $"Error sending to client (attempt {retryCount + 1}/{MaxRetries})");
                    retryCount++;
                    if (retryCount < MaxRetries)
                    {
                        await Task.Delay(RetryDelayMs);
                    }
                }
            }

            return sendSuccess;
        }
    }



    private static string? _readerAddress = null; // Nullable with default null value

    private static readonly string? _bearerToken = null; // Nullable with default null value

    private static readonly object _timerTagPublisherHttpLock = new();

    private static readonly object _timerTagPublisherOpcUaLock = new();

    // private readonly object _batchLock = new object();

    private readonly SemaphoreSlim _batchLock = new(1, 1);

    private readonly SemaphoreSlim _publishSemaphore = new(1, 1);

    private static int _positioningExpirationInSec = 3; // No change needed, value type with initializer

    private static IR700IotReader? _iotDeviceInterfaceClient = null; // Nullable with default null value

    private static string _readerUsername = "root";
    private static string _readerPassword = "impinj";

    private static string? _proxyAddress = ""; // Nullable with default empty string

    private static int _proxyPort = 8080;

    private static bool _isStarted;

    private static StandaloneConfigDTO? _standaloneConfigDTO = null; // Nullable with default null value

    //private static string _readerAddress;

    //private static readonly string _bearerToken;

    //private static readonly object _timerTagPublisherHttpLock = new();

    //private static readonly object _timerTagPublisherOpcUaLock = new();

    //private static int _positioningExpirationInSec = 3;

    //private static IR700IotReader? _iotDeviceInterfaceClient;

    //private static string _readerUsername = "root";
    //private static string _readerPassword = "impinj";

    //private static string _proxyAddress = "";

    //private static int _proxyPort = 8080;


    //private static bool _isStarted;

    //private static StandaloneConfigDTO _standaloneConfigDTO;

    //private static readonly SystemInfo _readerSystemInfo;

    // private static SimpleTcpServer? _socketServer = null;

    private static SimpleTcpServer? _socketCommandServer = null;

    private static SimpleTcpClient? _socketBarcodeClient = null;

    //private static UdpEndpoint _udpServer;

    private static UDPSocket? _udpSocketServer = null;

    //private static UdpClientUtil _udpClientUtil;

    private static string? _expectedLicense = null;

    private static TagRead? _lastTagRead = null;

    private static readonly TDTEngine _tdtEngine = new();

    private static readonly ConcurrentDictionary<string, DateTimeOffset> _knowTagsForSoftwareFilterWindowSec = new();

    public static string LastValidBarcode = "";

    public static string R700UsbDrive = @"/run/mount/external/";

    public static string R700UsbDrivePath = @"/run/mount/external/";

    private readonly IConfiguration _configuration;

    private readonly CancellationTokenSource _gpiCts;

    public static readonly ConcurrentDictionary<int, bool> _gpiPortStates = new();

    // private readonly PeriodicTimer _gpiTimer;

    // private readonly Task _gpiTimerTask;

    private readonly HttpUtil _httpUtil;

    private readonly ILogger<IotInterfaceService> _logger;

    private readonly ConcurrentQueue<string> _messageQueueBarcode = new();

    private readonly ConcurrentQueue<JObject> _messageQueueTagSmartReaderTagEventGroupToValidate = new();

    //private static object _locker = new object();

    //private readonly ConcurrentQueue<SmartReaderTagEventData> _messageQueueTagSmartReaderTagEventData = new ConcurrentQueue<SmartReaderTagEventData>();

    private readonly ConcurrentQueue<JObject> _messageQueueTagSmartReaderTagEventHttpPost = new();

    private readonly ConcurrentQueue<JObject> _messageQueueTagSmartReaderTagEventHttpPostRetry = new();

    private readonly ConcurrentQueue<JObject> _messageQueueTagSmartReaderTagEventMqtt = new();

    // private readonly ConcurrentQueue<JObject> _messageQueueTagSmartReaderTagEventSocketServerRetry = new ConcurrentQueue<JObject>();

    //private readonly ConcurrentQueue<JObject> _messageQueueTagSmartReaderTagEventSocketClient = new ConcurrentQueue<JObject>();

    //private readonly ConcurrentQueue<JObject> _messageQueueTagSmartReaderTagEventSocketClientRetry = new ConcurrentQueue<JObject>();

    //private readonly ConcurrentQueue<TagInventoryEvent> _messageQueueTagInventoryEvent = new ConcurrentQueue<TagInventoryEvent>();

    private readonly ConcurrentQueue<JObject> _messageQueueTagSmartReaderTagEventOpcUa = new();

    //private readonly ConcurrentQueue<JObject> _messageQueueTagSmartReaderTagEventMqttRetry = new ConcurrentQueue<JObject>();

    private readonly ConcurrentQueue<JObject> _messageQueueTagSmartReaderTagEventSocketServer = new();

    private readonly ConcurrentQueue<JObject> _messageQueueTagSmartReaderTagEventWebSocketServer = new();

    private readonly ConcurrentQueue<JObject> _messageQueueTagSmartReaderTagEventUdpServer = new();

    private readonly ConcurrentQueue<JObject> _messageQueueTagSmartReaderTagEventUsbDrive = new();

    private static List<string> _oldReadEpcList = [];

    private readonly List<int> _positioningAntennaPorts = [];

    private readonly List<string> _positioningEpcsHeaderList = [];

    public readonly ConcurrentDictionary<string, ReadCountTimeoutEvent> _softwareFilterReadCountTimeoutDictionary =
        new();

    public readonly ConcurrentDictionary<string, JObject> _smartReaderTagEventsListBatch = new();

    public readonly ConcurrentDictionary<string, JObject> _smartReaderTagEventsListBatchOnUpdate = new();

    public readonly ConcurrentDictionary<string, JObject> _smartReaderTagEventsAbsence = new();

    public readonly ConcurrentDictionary<string, int> _expectedItems = new();

    private readonly Stopwatch _stopwatchBearerToken = new();
    private readonly Stopwatch _stopwatchKeepalive = new();
    private readonly Stopwatch _stopwatchLastIddleEvent = new();
    private readonly Stopwatch _stopwatchPositioningExpiration = new();
    private readonly Stopwatch _stopwatchStopTriggerDuration = new();
    private readonly Stopwatch _stopwatchStopTagEventsListBatchOnChange = new();
    private readonly Stopwatch _mqttPublisherStopwatch = new();
    private readonly Stopwatch _gpoNoNewTagSeenStopwatch = new();


    private readonly Timer _timerStopTriggerDuration;

    private readonly Timer _timerTagFilterLists;

    private readonly Timer _timerKeepalive;

    private readonly Timer _timerTagPublisherHttp;

    //private readonly Timer _timer;

    //private readonly Timer _timerTagInventoryEvent;

    private readonly Timer _timerTagPublisherMqtt;

    private readonly Timer _timerTagPublisherOpcUa;

    private readonly Timer _timerTagPublisherRetry;

    private readonly Timer _timerTagPublisherSocket;

    private readonly Timer _timerTagPublisherWebSocket;

    private readonly Timer _timerSummaryStreamPublisher;

    private readonly Timer _timerTagPublisherUdpServer;

    private readonly Timer _timerTagPublisherUsbDrive;

    private readonly Timer _timerPeriodicTasksJob;

    protected object _timerPeriodicTasksJobManagerLock = new();

    private readonly CancellationTokenSource CancellationTokenSource = new();

    private readonly SemaphoreSlim periodicJobTaskLock = new(1, 1);
    private readonly SemaphoreSlim readLock = new(1, 1);

    private Stopwatch? _httpTimerStopwatch = null;

    //private static string? currentBarcode;
    //private static SmartReaderSetupData _mqttClientSmartReaderSetupDataSmartReaderSetupData;

    private static readonly ConcurrentDictionary<string, long> _readEpcs = new();
    private readonly ConcurrentDictionary<string, int> _currentSkus = new();
    private readonly ConcurrentBag<string> _currentSkuReadEpcs = [];

    private SerialPort? _serialTty = null;

    private AggregateInputReader? _aggInputReader = null;

    private ThreadSafeBatchProcessor _batchProcessor;

    private TagEventPublisher _tagEventPublisher;



    //private readonly RuntimeDb _db;

    private string? DeviceId = null;

    private string? DeviceIdMqtt = null;

    private string? DeviceIdWithDashes = null;

    private string? ExternalApiToken = null;

    private readonly ValidationService _validationService;

    private readonly IConfigurationService _configurationService;

    private readonly ITcpSocketService _tcpSocketService;

    private readonly IMqttService _mqttService;
    private readonly IWebSocketService _webSocketService;

    //private double _mqttPublishIntervalInSec = 1;
    //private double _mqttPublishIntervalInMs = 10;
    //private double _mqttUpdateTagEventsListBatchOnChangeIntervalInSec = 1;
    //private double _mqttUpdateTagEventsListBatchOnChangeIntervalInMs = 10;
    private MqttPublishingConfiguration _mqttPublishingConfiguration;

    public static Dictionary<string, IPlugin> _plugins = [];

    private readonly ILoggerFactory _loggerFactory;

    private string _buildConfiguration = "Release"; // Default to Debug if unable to determine

    public IotInterfaceService(IServiceProvider services,
        IConfiguration configuration,
        ILogger<IotInterfaceService> logger,
        ILoggerFactory loggerFactory,
        IHttpClientFactory httpClientFactory,
        IConfigurationService configurationService,
        ITcpSocketService tcpSocketService,
        IMqttService mqttService,
        IWebSocketService webSocketService)
    {
        _configuration = configuration;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        Services = services;
        HttpClientFactory = httpClientFactory;


        _validationService = new ValidationService(
            logger,
            _currentSkus,
            _currentSkuReadEpcs
        );

        _configurationService = configurationService;

        _tcpSocketService = tcpSocketService;

        _mqttService = mqttService;

        _webSocketService = webSocketService;

#if DEBUG
        _buildConfiguration = "Debug";
#endif

#pragma warning disable CS8602 // Dereference of a possibly null reference.
        var filePath = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
#pragma warning restore CS8602 // Dereference of a possibly null reference.


        // Use ConfigurationService to get values
        _readerAddress = _configurationService.GetReaderAddress();
        _readerUsername = _configurationService.GetReaderUsername();
        _readerPassword = _configurationService.GetReaderPassword();

        _httpUtil = _configurationService.CreateHttpUtil(HttpClientFactory);

        _logger.LogInformation($"Reader Address: {_readerAddress}, Reader Username: {_readerUsername}");


        _timerPeriodicTasksJob = new Timer(100);

        _timerTagFilterLists = new Timer(100);

        _timerKeepalive = new Timer(100);

        _timerStopTriggerDuration = new Timer(100);

        _timerTagPublisherHttp = new Timer(100);

        _timerTagPublisherSocket = new Timer(100);

        _timerTagPublisherWebSocket = new Timer(100);

        _timerSummaryStreamPublisher = new Timer(100);

        _timerTagPublisherUsbDrive = new Timer(100);

        _timerTagPublisherUdpServer = new Timer(100);

        _timerTagPublisherRetry = new Timer(100);

        _timerTagPublisherMqtt = new Timer(100);

        _timerTagPublisherOpcUa = new Timer(100);

        _gpiCts = new CancellationTokenSource();
        // _gpiTimer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
        // _gpiTimerTask = HandleGpiTimerAsync(_gpiTimer, _gpiCts.Token);

        _httpTimerStopwatch = new Stopwatch();

        try
        {
            _mqttPublishingConfiguration = LoadMqttPublishingConfiguration();
        }
        catch (Exception)
        {


        }

        try
        {
            InitializeReaderDeviceAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogDebug($"Error initializing device: {ex.Message}");


        }
        _batchProcessor = new ThreadSafeBatchProcessor(
                    _logger,
                    _standaloneConfigDTO,
                    ref _smartReaderTagEventsListBatch,
                    ref _smartReaderTagEventsListBatchOnUpdate,
                    ref _smartReaderTagEventsAbsence
                );

        _tagEventPublisher = new TagEventPublisher(
            _logger,
            _standaloneConfigDTO,
            _mqttPublishingConfiguration,
            _batchProcessor,
            _standaloneConfigDTO.readerName,
            _iotDeviceInterfaceClient.MacAddress
            );

        //try
        //{
        //    _gpiCts = new CancellationTokenSource();
        //    _gpiTimer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
        //    _gpiTimerTask = HandleGpiTimerAsync(_gpiTimer, _gpiCts.Token);
        //}
        //catch (Exception ex)
        //{
        //    _logger.LogError(ex, "HandleGpiTimerAsync init - " + ex.Message);

        //}
    }

    public IServiceProvider Services { get; }

    public IHttpClientFactory HttpClientFactory { get; }

    public bool IsService(Type serviceType)
    {
        return true;
    }

    public void CancelGpiTimer()
    {
        _gpiCts.Cancel();
    }

    //    private async Task HandleGpiTimerAsync(PeriodicTimer timer, CancellationToken cancel = default)
    //    {
    //        try
    //        {
    //            if (_standaloneConfigDTO == null)
    //            {
    //                _standaloneConfigDTO = ConfigFileHelper.ReadFile();
    //            }
    //#pragma warning disable CS8602 // Dereference of a possibly null reference.
    //            if (string.Equals("1", _standaloneConfigDTO.includeGpiEvent,
    //                                             StringComparison.OrdinalIgnoreCase))
    //            {
    //                while (await timer.WaitForNextTickAsync(cancel)) await Task.Run(() => QueryGpiStatus(cancel), cancel);
    //            }
    //            else
    //            {
    //                await Task.Delay(100);
    //            }
    //#pragma warning restore CS8602 // Dereference of a possibly null reference.

    //        }
    //        catch (Exception ex)
    //        {
    //            Console.WriteLine("QueryGpiStatus - " + ex.Message);
    //        }
    //    }

    //    private async Task QueryGpiStatus(CancellationToken cancel = default)
    //#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
    //    {
    //        try
    //        {


    //            var gpi1File = @"/dev/gpio/ext-gpi-0/value";
    //            var gpi2File = @"/dev/gpio/ext-gpi-1/value";
    //            var fileContent1 = "";
    //            var fileContent2 = "";
    //            if (File.Exists(gpi1File))
    //                fileContent1 = File.ReadAllText(gpi1File);
    //            if (File.Exists(gpi2File))
    //                fileContent2 = File.ReadAllText(gpi2File);

    //            if (!_gpiPortStates.ContainsKey(0)) _gpiPortStates.TryAdd(0, false);
    //            if (!_gpiPortStates.ContainsKey(1)) _gpiPortStates.TryAdd(1, false);

    //            if (!string.IsNullOrEmpty(fileContent1) && fileContent1.Contains("1"))
    //            {
    //                _gpiPortStates[0] = true;
    //                if (previousGpi1.HasValue)
    //                {
    //                    if (_gpiPortStates[0] != previousGpi1.Value)
    //                    {
    //                        gpi1HasChanged = true;
    //                    }
    //                    else
    //                    {
    //                        gpi1HasChanged = false;
    //                    }
    //                }
    //                else
    //                {
    //                    gpi1HasChanged = true;
    //                }

    //                previousGpi1 = _gpiPortStates[0];
    //            }
    //            else
    //            {
    //                _gpiPortStates[0] = false;
    //                if (previousGpi1.HasValue)
    //                {
    //                    if (_gpiPortStates[0] != previousGpi1.Value)
    //                    {
    //                        gpi1HasChanged = true;
    //                    }
    //                    else
    //                    {
    //                        gpi1HasChanged = false;
    //                    }
    //                }
    //                else
    //                {
    //                    gpi1HasChanged = true;
    //                }
    //                previousGpi1 = _gpiPortStates[0];
    //            }

    //            if (!string.IsNullOrEmpty(fileContent2) && fileContent2.Contains("1"))
    //            {
    //                _gpiPortStates[1] = true;
    //                if (previousGpi2.HasValue)
    //                {
    //                    if (_gpiPortStates[1] != previousGpi2.Value)
    //                    {
    //                        gpi2HasChanged = true;
    //                    }
    //                    else
    //                    {
    //                        gpi2HasChanged = false;
    //                    }
    //                }
    //                else
    //                {
    //                    gpi2HasChanged = true;
    //                }

    //                previousGpi2 = _gpiPortStates[1];
    //            }
    //            else
    //            {
    //                _gpiPortStates[1] = false;
    //                if (previousGpi2.HasValue)
    //                {
    //                    if (_gpiPortStates[1] != previousGpi2.Value)
    //                    {
    //                        gpi2HasChanged = true;
    //                    }
    //                    else
    //                    {
    //                        gpi2HasChanged = false;
    //                    }
    //                }
    //                else
    //                {
    //                    gpi2HasChanged = true;
    //                }
    //                previousGpi2 = _gpiPortStates[1];
    //            }



    //            //if (File.Exists(gpi1File))
    //            //{
    //            //    if (!_gpiPortStates.ContainsKey(0)) _gpiPortStates.TryAdd(0, false);
    //            //    // read file content into a string
    //            //    var fileContent = File.ReadAllText(gpi1File);

    //            //    // compare TextBox content with file content
    //            //    if (fileContent.Contains("1"))
    //            //    {
    //            //        _gpiPortStates[0] = true;
    //            //        if (previousGpi1.HasValue)
    //            //        {
    //            //            if (_gpiPortStates[0] != previousGpi1.Value)
    //            //            {
    //            //                gpi1HasChanged = true;
    //            //            }
    //            //            else
    //            //            {
    //            //                gpi1HasChanged = false;
    //            //            }
    //            //        }
    //            //        else
    //            //        {
    //            //            gpi1HasChanged = true;
    //            //        }

    //            //        previousGpi1 = _gpiPortStates[0];
    //            //    }
    //            //    else
    //            //    {
    //            //        _gpiPortStates[0] = false;
    //            //        if (previousGpi1.HasValue)
    //            //        {
    //            //            if (_gpiPortStates[0] != previousGpi1.Value)
    //            //            {
    //            //                gpi1HasChanged = true;
    //            //            }
    //            //            else
    //            //            {
    //            //                gpi1HasChanged = false;
    //            //            }
    //            //        }
    //            //        else
    //            //        {
    //            //            gpi1HasChanged = true;
    //            //        }

    //            //        //gpi1HasChanged = true;                    

    //            //        previousGpi1 = _gpiPortStates[0];
    //            //    }

    //            //}

    //            //if (File.Exists(gpi2File))
    //            //{
    //            //    if (!_gpiPortStates.ContainsKey(1)) _gpiPortStates.TryAdd(1, false);
    //            //    // read file content into a string
    //            //    var fileContent = File.ReadAllText(gpi2File);

    //            //    // compare TextBox content with file content
    //            //    //if (fileContent.Contains("1"))
    //            //    //    _gpiPortStates[1] = true;
    //            //    //else
    //            //    //    _gpiPortStates[1] = false;

    //            //    if (fileContent.Contains("1"))
    //            //    {
    //            //        _gpiPortStates[1] = true;
    //            //        if (previousGpi2.HasValue)
    //            //        {
    //            //            if (_gpiPortStates[1] != previousGpi2.Value)
    //            //            {
    //            //                gpi2HasChanged = true;
    //            //            }
    //            //            else
    //            //            {
    //            //                gpi2HasChanged = false;
    //            //            }
    //            //        }
    //            //        else
    //            //        {
    //            //            gpi2HasChanged = true;
    //            //        }

    //            //        previousGpi2 = _gpiPortStates[0];
    //            //    }
    //            //    else
    //            //    {
    //            //        _gpiPortStates[1] = false;
    //            //        if (previousGpi2.HasValue)
    //            //        {
    //            //            if (_gpiPortStates[1] != previousGpi2.Value)
    //            //            {
    //            //                gpi2HasChanged = true;
    //            //            }
    //            //            else
    //            //            {
    //            //                gpi2HasChanged = false;
    //            //            }
    //            //        }
    //            //        else
    //            //        {
    //            //            gpi2HasChanged = true;
    //            //        }
    //            //        //gpi1HasChanged = true;

    //            //        previousGpi2 = _gpiPortStates[1];
    //            //    }
    //            //}

    //            try
    //            {
    //                if (gpi1HasChanged || gpi2HasChanged)
    //                {
    //                    //fileContent1
    //                    _logger.LogInformation($"GPI status: gpi1: {fileContent1}, gpi2: {fileContent2}");
    //                    //_logger.LogInformation($"GPI status: gpi1HasChanged: {gpi1HasChanged}, gpi2HasChanged: {gpi2HasChanged}");
    //                    ProcessGpiStatus();
    //                }

    //            }
    //            catch (Exception)
    //            {


    //            }

    //        }
    //        catch (Exception ex)
    //        {
    //            Console.WriteLine("QueryGpiStatus - " + ex.Message);
    //        }
    //    }

    private void StartUdpServer()
    {
        if (IsUdpServerEnabled())
            try
            {
                _udpSocketServer = new UDPSocket();
                _udpSocketServer.Server(_standaloneConfigDTO.udpIpAddress,
                    int.Parse(_standaloneConfigDTO.udpReaderPort));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting udp server.");
            }
    }

    private void StopUdpServer()
    {
        if (IsUdpServerEnabled())
            try
            {
                _udpSocketServer?.Close();
            }
            catch (Exception ex)
            {
                //logger.("Error stoping udp server. " + ex.Message, SeverityType.Error);
                _logger.LogError(ex, "Error stoping udp server.");
            }
    }

    private void SetupUsbFlashDrive()
    {
        //if (_standaloneConfigDTO != null && !string.IsNullOrEmpty(_standaloneConfigDTO.usbFlashDrive)
        //                        && string.Equals("1", _standaloneConfigDTO.usbFlashDrive, StringComparison.OrdinalIgnoreCase))
        //{
        try
        {
            var allDrives = DriveInfo.GetDrives();
            foreach (var drive in allDrives)
                try
                {
                    if (drive.Name.Contains("/run/mount/external/"))
                    {
                        _logger.LogInformation("Current Drive Name: " + drive.Name + " Root Directory: " +
                                               drive.RootDirectory.Name + "Current Volume Label: " + drive.VolumeLabel);
                        R700UsbDrive = drive.Name;
                        R700UsbDrivePath = drive.RootDirectory.Name;
                        _logger.LogInformation("USB Directory: " + R700UsbDrive);
                        var dirInfo = drive.RootDirectory;
                        var fileNames = dirInfo.GetFiles("*.*");
                        foreach (var fi in fileNames)
                        {
                            _logger.LogInformation("USB Drive content:");
                            _logger.LogInformation("{0} : {1} : {2}", fi.Name, fi.LastAccessTime, fi.Length);
                        }

                        try
                        {
                            var webDir = "/customer/wwwroot/files";
                            try
                            {
                                if (Directory.Exists(webDir))
                                    try
                                    {
                                        Directory.Delete(webDir);
                                    }
                                    catch (Exception exSymLink)
                                    {
                                        _logger.LogError(exSymLink, "Error removing USB drive symbolic link as a dir.");
                                    }

                                _ = File.CreateSymbolicLink(webDir, R700UsbDrive);
                                _logger.LogInformation("Symbolic link created: " + webDir);
                                R700UsbDrivePath = webDir;

                                //_ = ProcessGpoErrorPortRecoveryAsync();
                            }
                            catch (Exception exSymLink1)
                            {
                                _logger.LogError(exSymLink1, "Error setting USB drive symbolic link.");
                                _ = ProcessGpoErrorPortAsync();
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error setting USB drive options.");
                            _ = ProcessGpoErrorPortAsync();
                        }

                        break;
                    }
                }
                catch (IOException ex)
                {
                    _logger.LogError(ex, "Error setting USB drive options.");
                    _ = ProcessGpoErrorPortAsync();
                }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error configuring USB Flash Drive..");
            _ = ProcessGpoErrorPortAsync();
        }


        //}
    }

    private void StartTcpBarcodeClient()
    {
        if (IsBarcodeTcpEnabled())
            try
            {
                if (_socketBarcodeClient == null)
                {
                    _logger.LogInformation("Creating tcp socket client. " + _standaloneConfigDTO.barcodeTcpPort,
                        SeverityType.Debug);
                    _socketBarcodeClient = new SimpleTcpClient(_standaloneConfigDTO.barcodeTcpAddress,
                        int.Parse(_standaloneConfigDTO.barcodeTcpPort));
                    // set events
                    _socketBarcodeClient.Events.DataReceived += BarcodeSocketClientEventsDataReceived;
                    _socketBarcodeClient.Events.Connected += BarcodeSocketClientEventsConnected;
                    _socketBarcodeClient.Events.Disconnected += BarcodeSocketClientEventsDisconnected;
                    //SimpleTcpKeepaliveSettings keepaliveSettings = new SimpleTcpKeepaliveSettings();
                    //keepaliveSettings.EnableTcpKeepAlives = true;
                    //keepaliveSettings.TcpKeepAliveTime = 0;
                    //keepaliveSettings.TcpKeepAliveInterval = 200;
                    //keepaliveSettings.TcpKeepAliveRetryCount = 1000;
                    //_socketBarcodeClient.Keepalive = keepaliveSettings;

                    _socketBarcodeClient.ConnectWithRetries(500);

                    //_ = ProcessGpoErrorPortRecoveryAsync();
                }
            }
            catch (Exception ex)
            {
                //_logger.LogInformation("Error starting tcp socket client. " + ex.Message, SeverityType.Error);
                _logger.LogError(ex, "Error starting tcp barcode socket client..");
                _ = ProcessGpoErrorPortAsync();
            }
    }

    private void StopTcpBarcodeClient()
    {
        try
        {
            if (_socketBarcodeClient != null)
            {
                _socketBarcodeClient.Events.DataReceived -= BarcodeSocketClientEventsDataReceived;
                _socketBarcodeClient.Events.Connected -= BarcodeSocketClientEventsConnected;
                _socketBarcodeClient.Events.Disconnected -= BarcodeSocketClientEventsDisconnected;
                _socketBarcodeClient.Disconnect();

                //_ = ProcessGpoErrorPortRecoveryAsync();
            }
        }
        catch (Exception ex)
        {
            //_logger.LogInformation("Error starting tcp socket client. " + ex.Message, SeverityType.Error);
            _logger.LogError(ex, "Error starting tcp socket client. ");
            _ = ProcessGpoErrorPortAsync();
        }
    }

    private void BarcodeSocketClientEventsDisconnected(object? sender, ConnectionEventArgs e)
    {
        _logger.LogInformation("Events_Disconnected  ");
        try
        {
            StopTcpBarcodeClient();
            StartTcpBarcodeClient();
        }
        catch (Exception)
        {


        }
    }

    private void BarcodeSocketClientEventsConnected(object? sender, ConnectionEventArgs e)
    {
        //_logger.LogInformation("Events_Connected ", SeverityType.Debug);
        _logger.LogInformation("Events_Connected  ");
    }

    private void BarcodeSocketClientEventsDataReceived(object? sender, DataReceivedEventArgs e)
    {
        try
        {
            //var byteData = e.Data;
            var messageData = "";
            // Check if the Array property is not null before using it
            if (e.Data.Array != null)
            {
                messageData = Encoding.UTF8.GetString(bytes: e.Data.Array, index: 0, count: e.Data.Count);
                // Use messageData for subsequent processing
            }
            Console.WriteLine($"BarcodeSocketClientEventsDataReceived: [{e.IpPort}] {messageData}");
            messageData = messageData.Replace("\n", "").Replace("\r", "");
            messageData = messageData.Trim();
            var receivedBarcode = ReceiveBarcode(messageData);
            if ("".Equals(receivedBarcode))
                //Console.WriteLine("Events_DataReceived - empty data received [" + messageData + "] ");
                _logger.LogInformation("Events_DataReceived - empty data received [" + messageData + "]   ");
            //_ = ProcessGpoErrorPortRecoveryAsync();
        }
        catch (Exception ex)
        {
            //_logger.LogInformation("Events_DataReceived. " + ex.Message, SeverityType.Error);
            _logger.LogError(ex, "Events_DataReceived. ");
            _ = ProcessGpoErrorPortAsync();
        }
    }

    private string ReceiveBarcode(string messageData)
    {
        try
        {
            //_logger.LogInformation("ReceiveBarcode: [" + messageData + "]", SeverityType.Debug);
            _logger.LogInformation("ReceiveBarcode: [" + messageData + "]   ");
            try
            {
                //string pattern = "(?<=\\[STX\\])(?:(?!\\[STX\\]).)*?(?=\\[ETX\\])";
                //Regex rgx = new Regex(pattern);
                //string rgxResult = rgx.Replace(messageData, "");
                //if (!string.IsNullOrEmpty(rgxResult))
                //{
                //    messageData = rgxResult;
                //}
                if (!string.IsNullOrEmpty(messageData))
                    messageData = Regex.Replace(messageData, @"\p{C}+", string.Empty);
            }
            catch (Exception ex)
            {
                //_logger.LogInformation("ReceiveBarcode: Regex.Replace [" + messageData + "]" + ex.Message);
                _logger.LogError(ex, "ReceiveBarcode: Regex.Replace [" + messageData + "]. ");
            }

            //_logger.LogInformation("ReceiveBarcode: [" + messageData + "]", SeverityType.Debug);
            _logger.LogInformation("ReceiveBarcode: [" + messageData + "]   ");

            if (string.IsNullOrEmpty(messageData))
            {
                //Console.WriteLine("ReceiveBarcode - ignoring empty [" + messageData + "] ");
                _logger.LogInformation("ReceiveBarcode:  - ignoring empty [" + messageData + "]   ");
                return "";
            }

            try
            {
#pragma warning disable CS8602 // Dereference of a possibly null reference.
                if (string.Equals("4", _standaloneConfigDTO.startTriggerType, StringComparison.OrdinalIgnoreCase))
                    _ = StartPresetAsync();
#pragma warning restore CS8602 // Dereference of a possibly null reference.
            }
            catch (Exception)
            {
            }

            if (messageData.Trim().Length < 3)
            {
                //Console.WriteLine("ReceiveBarcode - ignoring Length [" + messageData + "] ");
                _logger.LogInformation("ReceiveBarcode: - ignoring Length [" + messageData + "]   ");
                return "";
            }

            if (_standaloneConfigDTO != null)
            {
                if ("0".Equals(_standaloneConfigDTO.barcodeLineEnd))
                {

                    if (!string.IsNullOrEmpty(_standaloneConfigDTO.barcodeTcpLen))
                    {
                        var messageExpectedLen = int.Parse(_standaloneConfigDTO.barcodeTcpLen);
                        if (messageData.Length == messageExpectedLen)
                        {
                            if (!string.IsNullOrEmpty(_standaloneConfigDTO.barcodeTcpNoDataString)
                        && messageData.ToUpper().Contains(_standaloneConfigDTO.barcodeTcpNoDataString.ToUpper()))
                            {
                                if (!string.IsNullOrEmpty(_standaloneConfigDTO.barcodeProcessNoDataString)
                                    && "0".Equals(_standaloneConfigDTO.barcodeProcessNoDataString))
                                {
                                    //Console.WriteLine("ReceiveBarcode - ignoring NoDataString [" + messageData + "] ");
                                    _logger.LogInformation("ReceiveBarcode: - ignoring NoDataString [" + messageData + "]   ");
                                    return "";
                                }

                                _logger.LogInformation("ReceiveBarcode: - PROCESSING NoDataString [" + messageData + "]   ");
                                //Console.WriteLine("ReceiveBarcode - PROCESSING NoDataString [" + messageData + "] ");
                            }
                        }
                        else
                        {
                            //Console.WriteLine("ReceiveBarcode - ignoring [" + messageData + "] " + messageData.Length);
                            _logger.LogInformation("ReceiveBarcode: - ignoring [" + messageData + "] " +
                                                   messageData.Length);
                            return "";
                        }


                    }
                    else if ("2".Equals(_standaloneConfigDTO.barcodeLineEnd))
                    {
                        if (messageData.Contains("\r\n"))
                        {
                            messageData = messageData.Replace("\r\n", "");
                        }
                        else
                        {
                            _logger.LogInformation("ReceiveBarcode: - ignoring [" + messageData + "] ");
                            return "";
                        }
                    }
                    else if ("1".Equals(_standaloneConfigDTO.barcodeLineEnd))
                    {
                        if (messageData.Contains("\n"))
                        {
                            messageData = messageData.Replace("\n", "");
                        }
                        else
                        {
                            _logger.LogInformation("ReceiveBarcode: - ignoring [" + messageData + "] ");
                            return "";
                        }
                    }
                    else if ("3".Equals(_standaloneConfigDTO.barcodeLineEnd))
                    {
                        if (messageData.Contains("\r"))
                        {
                            messageData = messageData.Replace("\r", "");
                        }
                        else
                        {
                            _logger.LogInformation("ReceiveBarcode: - ignoring [" + messageData + "] ");
                            return "";
                        }
                    }
                }
            }



            if (_standaloneConfigDTO != null)
            {
                if (!string.IsNullOrEmpty(_standaloneConfigDTO.barcodeProcessNoDataString)
                         && "1".Equals(_standaloneConfigDTO.barcodeProcessNoDataString)
                         && messageData.ToUpper().Contains(_standaloneConfigDTO.barcodeTcpNoDataString.ToUpper()
                         ))
                {
                    //Console.WriteLine("ReceiveBarcode NOREAD  ======================================== ");
                    _logger.LogInformation("ReceiveBarcode: NOREAD ========================================");
                    //_logger.LogInformation("Events_DataReceived - setting NOREAD barcode " + messageData, SeverityType.Debug);
                    _logger.LogInformation("ReceiveBarcode: - setting NOREAD barcode [" + messageData + "]   ");
                    //Console.WriteLine("ReceiveBarcode - setting NOREAD barcode [" + messageData + "]");
                    //currentBarcode = messageData;
                    if (_standaloneConfigDTO != null
                        && !string.IsNullOrEmpty(_standaloneConfigDTO.barcodeEnableQueue)
                        && "1".Equals(_standaloneConfigDTO.barcodeEnableQueue))
                        _messageQueueBarcode.Enqueue(messageData);
                    else
                        LastValidBarcode = messageData;
                    //Console.WriteLine("ReceiveBarcode NOREAD  ======================================== ");
                    _logger.LogInformation("ReceiveBarcode: NOREAD ========================================");
                }
                else
                {
                    //Console.WriteLine("ReceiveBarcode ======================================== ");
                    _logger.LogInformation("ReceiveBarcode: ========================================");
                    _logger.LogInformation("ReceiveBarcode: - Events_DataReceived - setting barcode [" +
                                           messageData + "]   ");
                    //_logger.LogInformation("Events_DataReceived - setting barcode " + messageData, SeverityType.Debug);
                    //Console.WriteLine("ReceiveBarcode - setting barcode [" + messageData + "]");
                    _logger.LogInformation("ReceiveBarcode: - setting barcode [" + messageData + "]   ");
                    //currentBarcode = messageData;
                    if (_standaloneConfigDTO != null
                        && !string.IsNullOrEmpty(_standaloneConfigDTO.barcodeEnableQueue)
                        && "1".Equals(_standaloneConfigDTO.barcodeEnableQueue))
                        _messageQueueBarcode.Enqueue(messageData);
                    else
                        LastValidBarcode = messageData;
                    //Console.WriteLine("ReceiveBarcode ======================================== ");

                    if (_standaloneConfigDTO != null
                        && string.Equals("1", _standaloneConfigDTO.enableExternalApiVerification)
                                && string.Equals("1", _standaloneConfigDTO.enableValidation))
                    {
                        try
                        {
                            //_logger.LogInformation("Processing data: " + epc, SeverityType.Debug);
                            if (_plugins != null
                                && _plugins.Count > 0
                                && !string.IsNullOrEmpty(_standaloneConfigDTO.activePlugin)
                                && _plugins.ContainsKey(_standaloneConfigDTO.activePlugin)
                                && _plugins[_standaloneConfigDTO.activePlugin].IsProcessingExternalValidation())
                            {
                                if (!string.IsNullOrEmpty(_standaloneConfigDTO.externalApiVerificationSearchOrderUrl))
                                {
                                    _expectedItems.Clear();
                                    string[] epcArray = Array.Empty<string>();
                                    Dictionary<string, SkuSummary> skuSummaryList = [];
                                    var itemsFromExternalApi = _plugins[_standaloneConfigDTO.activePlugin].ExternalApiSearch(LastValidBarcode, skuSummaryList, epcArray);
                                    if (itemsFromExternalApi.Any())
                                    {
                                        foreach (KeyValuePair<string, int> entry in itemsFromExternalApi)
                                        {
                                            _ = _expectedItems.TryAdd(entry.Key, entry.Value);
                                        }
                                    }
                                    return messageData;
                                }
                                else if (!string.IsNullOrEmpty(_standaloneConfigDTO.externalApiVerificationAuthLoginUrl))
                                {
                                    try
                                    {
                                        _ = _plugins[_standaloneConfigDTO.activePlugin].ExternalApiLogin();
                                    }
                                    catch (Exception)
                                    {


                                    }
                                }

                            }
                        }
                        catch (Exception)
                        {

                        }

                    }


                    _logger.LogInformation("ReceiveBarcode: ========================================");
                    //_readEpcs.Clear();

                }
            }


        }
        catch (Exception)
        {
        }

        return messageData;
    }


    private void StartTcpSocketCommandServer()
    {
        if (IsSocketCommandServerEnabled())
            try
            {
                if (_socketCommandServer == null)
                {
                    _logger.LogInformation(
                        "Creating tcp socket cmd server. " + _standaloneConfigDTO.socketCommandServerPort,
                        SeverityType.Debug);
                    _socketCommandServer =
                        new SimpleTcpServer("0.0.0.0:" + _standaloneConfigDTO.socketCommandServerPort);
                    // set events
                    _socketCommandServer.Events.ClientConnected += ClientConnectedSocketCommand!;
                    _socketCommandServer.Events.ClientDisconnected += ClientDisconnectedSocketCommand!;
                    _socketCommandServer.Events.DataReceived += DataReceivedSocketCommand!;
                }

                if (_socketCommandServer != null && !_socketCommandServer.IsListening)
                {
                    _logger.LogInformation(
                        "Starting tcp socket cmd server. " + _standaloneConfigDTO.socketCommandServerPort,
                        SeverityType.Debug);
                    _socketCommandServer.Start();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting tcp socket command server. " + ex.Message);
            }
    }

    private void StopTcpSocketCommandServer()
    {
        try
        {
            if (_socketCommandServer != null)
            {
                _socketCommandServer.Events.ClientConnected -= ClientConnectedSocketCommand!;
                _socketCommandServer.Events.ClientDisconnected -= ClientDisconnectedSocketCommand!;
                _socketCommandServer.Events.DataReceived -= DataReceivedSocketCommand!;
                _socketCommandServer.Stop();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stoping tcp socket server. " + ex.Message);
        }
    }

    //private void LoadConfig()
    //{
    //    try
    //    {
    //        _standaloneConfigDTO = _configurationService.LoadConfig();
    //        _logger.LogInformation($"Configuration loaded: ReaderName = {_standaloneConfigDTO.readerName}");
    //    }
    //    catch (Exception ex)
    //    {
    //        _logger.LogError(ex, "An error occurred in IotInterfaceService loading the config.");
    //    }
    //}





    private void ClientConnectedSocketCommand(object sender, ConnectionEventArgs e)
    {
        _logger.LogInformation("[" + e.IpPort + "] client connected", SeverityType.Debug);
    }

    private void ClientDisconnectedSocketCommand(object sender, ConnectionEventArgs e)
    {
        _logger.LogInformation("[" + e.IpPort + "] client disconnected: " + e.Reason, SeverityType.Debug);
    }

    private void DataReceivedSocketCommand(object sender, DataReceivedEventArgs e)
    {
        try
        {
            if (_standaloneConfigDTO != null)
            {
                var receivedData = Encoding.UTF8.GetString(e.Data);

                try
                {
                    if (receivedData.Contains("|"))
                    {
                        receivedData = receivedData.Replace("\r\n", "");
                        receivedData = receivedData.Replace("\r", "");
                        receivedData = receivedData.Replace("\n", "");
                        var parsedData = receivedData.Split("|");
                        var shouldRestart = false;
                        foreach (var item in parsedData)
                            if (item.Contains("="))
                            {
                                var parsedItemParts = item.Split("=");
                                if (parsedItemParts[0].StartsWith("antennaPorts"))
                                {
                                    _standaloneConfigDTO.antennaPorts = parsedItemParts[1];
                                    _configurationService.SaveConfigDtoToDb(_standaloneConfigDTO);
                                    shouldRestart = true;
                                }
                                else if (parsedItemParts[0].StartsWith("antennaStates"))
                                {
                                    _standaloneConfigDTO.antennaStates = parsedItemParts[1];
                                    _configurationService.SaveConfigDtoToDb(_standaloneConfigDTO);
                                    shouldRestart = true;
                                }
                                else if (parsedItemParts[0].StartsWith("transmitPower"))
                                {
                                    _standaloneConfigDTO.transmitPower = parsedItemParts[1];
                                    _configurationService.SaveConfigDtoToDb(_standaloneConfigDTO);
                                    shouldRestart = true;
                                }
                                else if (parsedItemParts[0].StartsWith("receiveSensitivity"))
                                {
                                    _standaloneConfigDTO.receiveSensitivity = parsedItemParts[1];
                                    _configurationService.SaveConfigDtoToDb(_standaloneConfigDTO);
                                    shouldRestart = true;
                                }
                                else if (parsedItemParts[0].StartsWith("readerMode"))
                                {
                                    _standaloneConfigDTO.readerMode = parsedItemParts[1];
                                    _configurationService.SaveConfigDtoToDb(_standaloneConfigDTO);
                                    shouldRestart = true;
                                }
                                else if (parsedItemParts[0].StartsWith("searchMode"))
                                {
                                    _standaloneConfigDTO.searchMode = parsedItemParts[1];
                                    _configurationService.SaveConfigDtoToDb(_standaloneConfigDTO);
                                    shouldRestart = true;
                                }
                                else if (parsedItemParts[0].StartsWith("session"))
                                {
                                    _standaloneConfigDTO.session = parsedItemParts[1];
                                    _configurationService.SaveConfigDtoToDb(_standaloneConfigDTO);
                                    shouldRestart = true;
                                }
                            }

                        if (shouldRestart)
                            try
                            {
                                try
                                {
                                    _ = StopTasksAsync();
                                }
                                catch (Exception)
                                {
                                }

                                _ = StartTasksAsync();
                            }
                            catch (Exception)
                            {
                            }
                    }
                    else
                    {
                        if (receivedData.StartsWith("START"))
                            try
                            {
                                _ = StartTasksAsync();
                            }
                            catch (Exception)
                            {
                            }
                        else if (receivedData.StartsWith("STOP"))
                            try
                            {
                                _ = StopTasksAsync();
                            }
                            catch (Exception)
                            {
                            }
                    }
                }
                catch (Exception)
                {
                    //throw;
                }
            }

            _logger.LogInformation("[" + e.IpPort + "]: " + Encoding.UTF8.GetString(e.Data), SeverityType.Debug);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error. " + ex.Message);
        }
    }

    public async Task<List<SmartreaderSerialNumberDto>> GetSerialAsync()
    {
        var returnData = new List<SmartreaderSerialNumberDto>();
        try
        {
            InitR700IoTClient();

            var info = await _iotDeviceInterfaceClient.GetSystemInfoAsync();
            var serialData = new SmartreaderSerialNumberDto
            {
                SerialNumber = info.SerialNumber
            };
            try
            {
                if (!string.IsNullOrEmpty(serialData.SerialNumber) && serialData.SerialNumber.StartsWith("370"))
                {
                    var serialNumber = serialData.SerialNumber;

                    var serialNumberWithDashes = string.Format("{0:###-##-##-####}", Convert.ToInt64(serialNumber));

                    DeviceIdWithDashes = serialNumberWithDashes;
                }
            }
            catch (Exception)
            {
            }

            returnData.Add(serialData);
        }
        catch (Exception)
        {
        }

        return returnData;
    }

    private void InitR700IoTClient()
    {
        if (_iotDeviceInterfaceClient == null)
        {
            if (_readerAddress != null)
            {
                var healthMonitor = new HealthMonitor(_loggerFactory.CreateLogger<HealthMonitor>());
                var readerConfiguration = ReaderConfiguration.CreateDefault();
                readerConfiguration.Network.Hostname = _readerAddress;
                readerConfiguration.Security.Username = _readerUsername;
                readerConfiguration.Security.Password = _readerPassword;
                readerConfiguration.Security.UseBasicAuth = true;
                readerConfiguration.Network.UseHttps = true;



                if (!string.IsNullOrEmpty(_proxyAddress))
                {
                    readerConfiguration.Network.Proxy = ProxySettings.CreateDefault();
                    readerConfiguration.Network.Proxy.Address = _proxyAddress;
                    readerConfiguration.Network.Proxy.Port = _proxyPort;
                }

                _iotDeviceInterfaceClient = new R700IotReader(
                    _readerAddress,
                    readerConfiguration,
                    eventProcessorLogger: _loggerFactory.CreateLogger<R700IotReader>(),
                    _loggerFactory,
                    healthMonitor
                );
            }
            else
            {
                // Handle the null:
                throw new InvalidOperationException("Reader address cannot be null when initializing the IoT reader interface.");
            }
        }
    }

    public async Task<List<SmartreaderRunningStatusDto>> GetReaderStatusAsync()
    {
        var returnData = new List<SmartreaderRunningStatusDto>();
        if (_iotDeviceInterfaceClient == null)
        {
            if (_readerAddress != null)
            {
                if (_proxyAddress == null)
                {
                    _proxyAddress = "";
                }
                InitR700IoTClient();
            }
            else
            {
                // Handle the null:
                throw new InvalidOperationException("Reader address cannot be null when initializing the IoT reader interface.");
            }
        }
        var readerStatus = await _iotDeviceInterfaceClient.GetStatusAsync();
        if (!_iotDeviceInterfaceClient.IsNetworkConnected)
        {
            await ProcessGpoErrorPortAsync();
            await ProcessGpoErrorNetworkPortAsync(true);
        }
        else
        {
            //Task.Run(() => ProcessGpoErrorPortRecoveryAsync());
            //Task.Run(() => ProcessGpoErrorNetworkPortAsync(false));
        }
        var readerStatusData = new SmartreaderRunningStatusDto();

        if (readerStatus != null && readerStatus.Status.HasValue)
        {
            if ("IDLE".Equals(readerStatus.Status.Value.ToString().ToUpper()))
                readerStatusData.Status = "STOPPED";
            else
                readerStatusData.Status = "STARTED";
        }
        returnData.Add(readerStatusData);

        return returnData;
    }

    public async Task StartPresetAsync()
    {
        if (_iotDeviceInterfaceClient == null)
        {
            if (_readerAddress != null)
            {
                InitR700IoTClient();
            }
            else
            {
                // Handle the null case, e.g., log an error, throw an exception, or use a default value
                throw new InvalidOperationException("Reader address cannot be null when initializing the IoT reader interface.");
            }
        }



        if (_standaloneConfigDTO != null
                            && !string.IsNullOrEmpty(_standaloneConfigDTO.smartreaderEnabledForManagementOnly)
                            && !"1".Equals(_standaloneConfigDTO.smartreaderEnabledForManagementOnly))
        {
            try
            {
                _oldReadEpcList.Clear();
            }
            catch (Exception)
            {
            }

            try
            {
                _iotDeviceInterfaceClient.TagInventoryEvent -= OnTagInventoryEvent!;
                _iotDeviceInterfaceClient.GpiTransitionEvent -= OnGpiTransitionEvent!;
                await _iotDeviceInterfaceClient.StopAsync();
            }
            catch (Exception)
            {
            }

            try
            {
                _iotDeviceInterfaceClient.TagInventoryEvent += OnTagInventoryEvent!;
                _iotDeviceInterfaceClient.GpiTransitionEvent += OnGpiTransitionEvent!;
            }
            catch (Exception)
            {
            }

            try
            {
                await ApplySettingsAsync();
                //_ = ProcessGpoErrorPortRecoveryAsync();
            }
            catch (Exception ex)
            {
                _logger.LogInformation("Error applying settings. " + ex.Message);
                await ProcessGpoErrorPortAsync();
            }

            try
            {
                await _iotDeviceInterfaceClient.StartAsync("SmartReader");
            }
            catch (Exception)
            {
                if (_standaloneConfigDTO.isEnabled == "1") _configurationService.SaveStartCommandToDb();
            }

            try
            {
                if (_standaloneConfigDTO != null
                    && string.Equals("1", _standaloneConfigDTO.enableSummaryStream, StringComparison.OrdinalIgnoreCase))
                {
                    using var scope = Services.CreateScope();
                    var summaryQueueBackgroundService = scope.ServiceProvider.GetRequiredService<ISummaryQueueBackgroundService>();
                    summaryQueueBackgroundService.StartQueue();
                    //_ = ProcessGpoErrorPortRecoveryAsync();
                }
            }
            catch (Exception)
            {
                await ProcessGpoErrorPortAsync();
            }
        }

    }

    public async Task StopPresetAsync()
    {
        InitR700IoTClient();

        if (_standaloneConfigDTO != null
                            && !string.IsNullOrEmpty(_standaloneConfigDTO.smartreaderEnabledForManagementOnly)
                            && !"1".Equals(_standaloneConfigDTO.smartreaderEnabledForManagementOnly))
        {
            try
            {
                await _iotDeviceInterfaceClient.StopPresetAsync();
            }
            catch (Exception)
            {
                //await ProcessGpoErrorPortAsync();
            }

            try
            {
                await _iotDeviceInterfaceClient.StopAsync();
            }
            catch (Exception)
            {
            }


            try
            {
                if (_standaloneConfigDTO != null
                    && string.Equals("1", _standaloneConfigDTO.enableSummaryStream, StringComparison.OrdinalIgnoreCase))
                {
                    using var scope = Services.CreateScope();
                    var summaryQueueBackgroundService = scope.ServiceProvider.GetRequiredService<ISummaryQueueBackgroundService>();
                    summaryQueueBackgroundService.StopQueue();
                }
            }
            catch (Exception)
            {
            }
        }

    }

    public async Task StartTasksAsync()
    {

        try
        {
            _oldReadEpcList.Clear();
        }
        catch (Exception)
        {
        }

        InitR700IoTClient();

        if (_isStarted)
            try
            {
                if (_standaloneConfigDTO != null
                            && !string.IsNullOrEmpty(_standaloneConfigDTO.smartreaderEnabledForManagementOnly)
                            && !"1".Equals(_standaloneConfigDTO.smartreaderEnabledForManagementOnly))
                {
                    _iotDeviceInterfaceClient.TagInventoryEvent -= OnTagInventoryEvent!;
                    _iotDeviceInterfaceClient.GpiTransitionEvent -= OnGpiTransitionEvent!;
                    await _iotDeviceInterfaceClient.StopAsync();
                }

            }
            catch (Exception)
            {
            }



        try
        {
            if (_standaloneConfigDTO != null && !string.IsNullOrEmpty(_standaloneConfigDTO.isEnabled))
                try
                {
                    _standaloneConfigDTO.isEnabled = "1";
                    _configurationService.SaveConfigDtoToDb(_standaloneConfigDTO);
                    //var configHelper = new IniConfigHelper();
                    //configHelper.SaveDtoToFile(_standaloneConfigDTO);
                }
                catch (Exception)
                {
                    //throw;
                }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving state to config file. " + ex.Message);
        }

        //_isStarted = true;
        if (_standaloneConfigDTO != null
                            && !string.IsNullOrEmpty(_standaloneConfigDTO.smartreaderEnabledForManagementOnly)
                            && !"1".Equals(_standaloneConfigDTO.smartreaderEnabledForManagementOnly))
        {
            try
            {
                await ApplySettingsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex, "Error applying settings. " + ex.Message);
            }

            _iotDeviceInterfaceClient.TagInventoryEvent += OnTagInventoryEvent!;
            _iotDeviceInterfaceClient.InventoryStatusEvent += OnInventoryStatusEvent!;
            _iotDeviceInterfaceClient.GpiTransitionEvent += OnGpiTransitionEvent!;
            try
            {
                await _iotDeviceInterfaceClient.StartAsync("SmartReader");
                _isStarted = true;
                //await _iotDeviceInterfaceClient.StartPresetAsync("SmartReader");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting SmartReader preset, scheduling start command to background worker. " + ex.Message);
                //if (_standaloneConfigDTO.isEnabled == "1" && _isStarted) SaveStartCommandToDb();
                if (_standaloneConfigDTO.isEnabled == "1") _configurationService.SaveStartCommandToDb();
                try
                {
                    await ApplySettingsAsync();
                    //_ = ProcessGpoErrorPortRecoveryAsync();
                }
                catch (Exception exPreset)
                {
                    _logger.LogInformation("Error applying settings. " + exPreset.Message);
                    await ProcessGpoErrorPortAsync();
                }
            }
        }


        try
        {
            StartTcpBarcodeClient();
        }
        catch (Exception ex)
        {

            _logger.LogError(ex, "Error starting tcp barcode. " + ex.Message);
        }

        try
        {
            _tcpSocketService.Start(int.Parse(_standaloneConfigDTO.socketPort));
        }
        catch (Exception ex)
        {

            _logger.LogError(ex, "Error starting tcp socket server. " + ex.Message);
        }

        //try
        //{
        //    _webSocketService.Start(_standaloneConfigDTO.webSocketPort);
        //}
        //catch (Exception ex)
        //{

        //    _logger.LogError(ex, "Error starting web socket server. " + ex.Message);
        //}

        try
        {
            StartUdpServer();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting udp socket server. " + ex.Message);

        }


        try
        {
            StartTcpSocketCommandServer();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting tcp socket server commands receiver. " + ex.Message);

        }


        try
        {
            if (IsMqttEnabled())
            {
                var mqttManagementEvents = new Dictionary<string, object>
                {
                    { "smartreader-status", "started" }
                };
                await _mqttService.PublishMqttManagementEventAsync(mqttManagementEvents);
            }


        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing mqtt app status. " + ex.Message);

        }


    }

    public async Task StopTasksAsync()
    {
        _isStarted = false;
        InitR700IoTClient();

        if (_standaloneConfigDTO != null
                            && !string.IsNullOrEmpty(_standaloneConfigDTO.smartreaderEnabledForManagementOnly)
                            && !"1".Equals(_standaloneConfigDTO.smartreaderEnabledForManagementOnly))
        {
            try
            {
                await _iotDeviceInterfaceClient.StopAsync();
            }
            catch (Exception)
            {
            }


            try
            {
                await _iotDeviceInterfaceClient.DeleteInventoryPresetAsync("SmartReader");
            }
            catch (Exception ex)
            {
                _logger.LogInformation("Unable to clean-up previous preset. " + ex.Message);
            }

            try
            {
                if (_standaloneConfigDTO != null
                    && string.Equals("1", _standaloneConfigDTO.enableSummaryStream, StringComparison.OrdinalIgnoreCase))
                {
                    using var scope = Services.CreateScope();
                    var summaryQueueBackgroundService = scope.ServiceProvider.GetRequiredService<ISummaryQueueBackgroundService>();
                    summaryQueueBackgroundService.StartQueue();
                }
            }
            catch (Exception)
            {
            }
        }


        StopTcpBarcodeClient();
        try
        {
            _tcpSocketService.Stop();
        }
        catch (Exception ex)
        {
            _logger.LogInformation("Unable to stop TCP Socket Server. " + ex.Message);
        }

        try
        {
            _webSocketService.Stop();
        }
        catch (Exception ex)
        {
            _logger.LogInformation("Unable to stop Web Socket Server. " + ex.Message);
        }

        StopUdpServer();
        StopTcpSocketCommandServer();

        if (IsMqttEnabled())
        {
            var mqttManagementEvents = new Dictionary<string, object>
            {
                { "smartreader-status", "stopped" }
            };
            await _mqttService.PublishMqttManagementEventAsync(mqttManagementEvents);
        }
    }

    public async Task ApplySettingsAsync()
    {
#pragma warning disable CS8600, CS8602, CS8604 // Dereference of a possibly null reference.
        InitR700IoTClient();

        try
        {
            //var configHelper = new IniConfigHelper();
            //_standaloneConfigDTO = configHelper.LoadDtoFromFile();
            if (_standaloneConfigDTO == null) _standaloneConfigDTO = ConfigFileHelper.ReadFile();

            if (_standaloneConfigDTO != null)
            {
                _validationService.UpdateConfig(_standaloneConfigDTO);
            }

            var mapper = new IoTInterfaceMapper();
            var presetRequest = mapper.CreateStandaloneInventoryRequest(_standaloneConfigDTO);
            var request = JsonConvert.SerializeObject(presetRequest);
            try
            {
                await _iotDeviceInterfaceClient.SaveInventoryPresetAsync("SmartReader", presetRequest);
            }
            catch (Exception exClientReturn)
            {
                if (exClientReturn.Message.Contains("204"))
                    _logger.LogInformation($"Preset saved. {exClientReturn.Message}");
                else if (exClientReturn.Message.Contains("409"))
                    _logger.LogInformation($"Preset is running. {exClientReturn.Message}");
                else
                    _logger.LogError(exClientReturn, "Unexpected saving preset (ApplySettingsAsync).");
            }

            if (IsMqttEnabled())
            {
                var mqttManagementEvents = new Dictionary<string, object>
                {
                    { "smartreader-status", "settings-applied" }
                };
                await _mqttService.PublishMqttManagementEventAsync(mqttManagementEvents);
            }

            _ = await _iotDeviceInterfaceClient.GetReaderInventoryPresetListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error applying settings (ApplySettingsAsync).");
        }
#pragma warning restore CS8600, CS8602, CS8604 // Dereference of a possibly null reference.
    }

    public async Task ProcessGpoBlinkAnyTagGpoPortAsync()
    {
        try
        {
#pragma warning disable CS8602 // Dereference of a possibly null reference.
            if (string.Equals("1", _standaloneConfigDTO.advancedGpoEnabled,
                                           StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals("9", _standaloneConfigDTO.advancedGpoMode1,
                                        StringComparison.OrdinalIgnoreCase)
                    || string.Equals("9", _standaloneConfigDTO.advancedGpoMode2,
                                        StringComparison.OrdinalIgnoreCase)
                    || string.Equals("9", _standaloneConfigDTO.advancedGpoMode3,
                                        StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        await ProcessGpoAnyTagPortAsync(true);
                        await Task.Delay(1000);
                        await ProcessGpoAnyTagPortAsync(false);
                    }
                    catch (Exception)
                    {
                    }
                }
            }
#pragma warning restore CS8602 // Dereference of a possibly null reference.
        }
        catch (Exception)
        {

        }
    }
    public async Task ProcessGpoBlinkNewTagStatusGpoPortAsync(int timeInSec)
    {
        try
        {
#pragma warning disable CS8602 // Dereference of a possibly null reference.
            if (!string.IsNullOrEmpty(_standaloneConfigDTO.advancedGpoEnabled)
                && string.Equals("1", _standaloneConfigDTO.advancedGpoEnabled, StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(_standaloneConfigDTO.advancedGpoMode1)
                    && string.Equals("6", _standaloneConfigDTO.advancedGpoMode1, StringComparison.OrdinalIgnoreCase))
                {
                    _ = SetGpoPortAsync(1, true);
                    await Task.Delay(timeInSec * 1000);
                    _ = SetGpoPortAsync(1, false);
                }

                if (!string.IsNullOrEmpty(_standaloneConfigDTO.advancedGpoMode2)
                    && string.Equals("6", _standaloneConfigDTO.advancedGpoMode2, StringComparison.OrdinalIgnoreCase))
                {
                    _ = SetGpoPortAsync(2, true);
                    await Task.Delay(timeInSec * 1000);
                    _ = SetGpoPortAsync(2, false);
                }

                if (!string.IsNullOrEmpty(_standaloneConfigDTO.advancedGpoMode3)
                    && string.Equals("6", _standaloneConfigDTO.advancedGpoMode3, StringComparison.OrdinalIgnoreCase))
                {
                    _ = SetGpoPortAsync(3, true);
                    await Task.Delay(timeInSec * 1000);
                    _ = SetGpoPortAsync(3, false);
                }
            }
#pragma warning restore CS8602 // Dereference of a possibly null reference.
        }
        catch (Exception)
        {
        }
    }

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
    public async Task SetGpoPortValidationAsync(bool ValidationState)
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
    {
        try
        {
#pragma warning disable CS8602 // Dereference of a possibly null reference.
            if (!string.IsNullOrEmpty(_standaloneConfigDTO.advancedGpoEnabled)
                && string.Equals("1", _standaloneConfigDTO.advancedGpoEnabled, StringComparison.OrdinalIgnoreCase))
            {
                if (ValidationState && !string.IsNullOrEmpty(_standaloneConfigDTO.advancedGpoMode1)
                                    && string.Equals("7", _standaloneConfigDTO.advancedGpoMode1,
                                        StringComparison.OrdinalIgnoreCase))
                {
                    _ = SetGpoPortAsync(1, true);
                    Task.Delay(1000).Wait();
                    _ = SetGpoPortAsync(1, false);
                }
                else if (!ValidationState && !string.IsNullOrEmpty(_standaloneConfigDTO.advancedGpoMode1)
                                          && string.Equals("8", _standaloneConfigDTO.advancedGpoMode1,
                                              StringComparison.OrdinalIgnoreCase))
                {
                    _ = SetGpoPortAsync(1, true);
                    Task.Delay(2000).Wait();
                    _ = SetGpoPortAsync(1, false);
                }

                if (ValidationState && !string.IsNullOrEmpty(_standaloneConfigDTO.advancedGpoMode2)
                                    && string.Equals("7", _standaloneConfigDTO.advancedGpoMode2,
                                        StringComparison.OrdinalIgnoreCase))
                {
                    _ = SetGpoPortAsync(2, true);
                    Task.Delay(2000).Wait();
                    _ = SetGpoPortAsync(2, false);
                }
                else if (!ValidationState && !string.IsNullOrEmpty(_standaloneConfigDTO.advancedGpoMode2)
                                          && string.Equals("8", _standaloneConfigDTO.advancedGpoMode2,
                                              StringComparison.OrdinalIgnoreCase))
                {
                    _ = SetGpoPortAsync(2, true);
                    Task.Delay(2000).Wait();
                    _ = SetGpoPortAsync(2, false);
                }

                if (ValidationState && !string.IsNullOrEmpty(_standaloneConfigDTO.advancedGpoMode3)
                                    && string.Equals("7", _standaloneConfigDTO.advancedGpoMode3,
                                        StringComparison.OrdinalIgnoreCase))
                {
                    _ = SetGpoPortAsync(3, true);
                    Task.Delay(2000).Wait();
                    _ = SetGpoPortAsync(3, false);
                }
                else if (!ValidationState && !string.IsNullOrEmpty(_standaloneConfigDTO.advancedGpoMode3)
                                          && string.Equals("8", _standaloneConfigDTO.advancedGpoMode3,
                                              StringComparison.OrdinalIgnoreCase))
                {
                    _ = SetGpoPortAsync(3, true);
                    Task.Delay(2000).Wait();
                    _ = SetGpoPortAsync(3, false);
                }
            }
#pragma warning restore CS8602 // Dereference of a possibly null reference.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving state to config file. " + ex.Message);
        }
    }

    public async Task SetGpiPortsAsync()
    {
        //_isStarted = false;
        InitR700IoTClient();


        try
        {
            ProcessGpiStatus();

            var gpiConfig = new GpiConfigRoot
            {
                GpiConfigurations =
            [
                new GpiConfiguration { DebounceMilliseconds = 20, Enabled = true, Gpi = 1, State = "low" },
                new GpiConfiguration { DebounceMilliseconds = 20, Enabled = true, Gpi = 2, State = "low" }
            ],
                GpiTransitionEvents = "enabled"
            };

            _logger.LogInformation("Configuring GPI ports.");
            _logger.LogInformation(gpiConfig.ToString());

            await _iotDeviceInterfaceClient.UpdateReaderGpiAsync(gpiConfig);
            _logger.LogInformation("GPI ports set.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error configuring GPIs. " + ex.Message);
        }
    }

    public async Task SetGpoPortAsync(int port, bool state)
    {
        //_isStarted = false;
        InitR700IoTClient();


        try
        {
            var gpoConfigurations = new GpoConfigurations();
            var gpoConfigList = new ObservableCollection<GpoConfiguration>();
            gpoConfigurations.GpoConfigurations1 = gpoConfigList;
            var gpoConfiguration = new GpoConfiguration
            {
                Gpo = port
            };
            if (state)
                gpoConfiguration.State = GpoConfigurationState.High;
            else
                gpoConfiguration.State = GpoConfigurationState.Low;
            gpoConfigurations.GpoConfigurations1.Add(gpoConfiguration);
            await _iotDeviceInterfaceClient.UpdateReaderGpoAsync(gpoConfigurations);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving state to config file. " + ex.Message);
        }
    }

    public async Task SetGpoPortsAsync(GpoVm gpos)
    {
        //_isStarted = false;
        InitR700IoTClient();

        try
        {
            var gpoConfigurations = new GpoConfigurations();
            var gpoConfigList = new ObservableCollection<GpoConfiguration>();
            gpoConfigurations.GpoConfigurations1 = gpoConfigList;

            foreach (var gpoVmConfig in gpos.GpoConfigurations)
            {
                var gpoConfiguration = new GpoConfiguration
                {
                    Gpo = gpoVmConfig.Gpo
                };
                if (gpoVmConfig.State)
                    gpoConfiguration.State = GpoConfigurationState.High;
                else
                    gpoConfiguration.State = GpoConfigurationState.Low;
                gpoConfigurations.GpoConfigurations1.Add(gpoConfiguration);
            }

            await _iotDeviceInterfaceClient.UpdateReaderGpoAsync(gpoConfigurations);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving gpo. " + ex.Message);
        }
    }

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
    public async Task ProcessGpoAnyTagPortAsync(bool setPortOn)
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
    {
        try
        {
            if (_standaloneConfigDTO != null)
            {
                if (string.Equals("1", _standaloneConfigDTO.advancedGpoEnabled,
                                            StringComparison.OrdinalIgnoreCase))
                {
                    if (string.Equals("9", _standaloneConfigDTO.advancedGpoMode1,
                                            StringComparison.OrdinalIgnoreCase)
                        || string.Equals("9", _standaloneConfigDTO.advancedGpoMode2,
                                            StringComparison.OrdinalIgnoreCase)
                        || string.Equals("9", _standaloneConfigDTO.advancedGpoMode3,
                                            StringComparison.OrdinalIgnoreCase))
                    {


                        if (setPortOn)
                        {
                            if (string.Equals("9", _standaloneConfigDTO.advancedGpoMode1,
                                           StringComparison.OrdinalIgnoreCase))
                            {
                                _ = SetGpoPortAsync(1, true);
                            }
                            if (string.Equals("9", _standaloneConfigDTO.advancedGpoMode2,
                                                    StringComparison.OrdinalIgnoreCase))
                            {
                                _ = SetGpoPortAsync(2, true);
                            }
                            if (string.Equals("9", _standaloneConfigDTO.advancedGpoMode3,
                                                    StringComparison.OrdinalIgnoreCase))
                            {
                                _ = SetGpoPortAsync(3, true);
                            }

                        }
                        else
                        {
                            if (string.Equals("9", _standaloneConfigDTO.advancedGpoMode1,
                                           StringComparison.OrdinalIgnoreCase))
                            {
                                _ = SetGpoPortAsync(1, false);
                            }
                            if (string.Equals("9", _standaloneConfigDTO.advancedGpoMode2,
                                                    StringComparison.OrdinalIgnoreCase))
                            {
                                _ = SetGpoPortAsync(2, false);
                            }
                            if (string.Equals("9", _standaloneConfigDTO.advancedGpoMode3,
                                                    StringComparison.OrdinalIgnoreCase))
                            {
                                _ = SetGpoPortAsync(3, false);
                            }

                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing no new tags seen state. ");
            _ = ProcessGpoErrorPortAsync();
        }
    }

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
    public async Task ProcessGpoErrorPortAsync()
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
    {
        try
        {
            if (_standaloneConfigDTO != null)
            {
                if (string.Equals("1", _standaloneConfigDTO.advancedGpoEnabled,
                                            StringComparison.OrdinalIgnoreCase))
                {
                    if (string.Equals("10", _standaloneConfigDTO.advancedGpoMode1,
                                            StringComparison.OrdinalIgnoreCase))
                    {
                        _ = SetGpoPortAsync(1, true);

                    }
                    if (string.Equals("10", _standaloneConfigDTO.advancedGpoMode2,
                                            StringComparison.OrdinalIgnoreCase))
                    {
                        _ = SetGpoPortAsync(2, true);
                    }
                    if (string.Equals("10", _standaloneConfigDTO.advancedGpoMode3,
                                            StringComparison.OrdinalIgnoreCase))
                    {
                        _ = SetGpoPortAsync(3, true);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing GPO error status. " + ex.Message);
        }
    }

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
    public async Task ProcessGpoErrorNetworkPortAsync(bool status)
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
    {
        try
        {
            if (_standaloneConfigDTO != null)
            {
                if (string.Equals("1", _standaloneConfigDTO.advancedGpoEnabled,
                                            StringComparison.OrdinalIgnoreCase))
                {
                    if (string.Equals("11", _standaloneConfigDTO.advancedGpoMode1,
                                            StringComparison.OrdinalIgnoreCase))
                    {
                        _ = SetGpoPortAsync(1, status);

                    }
                    if (string.Equals("11", _standaloneConfigDTO.advancedGpoMode2,
                                            StringComparison.OrdinalIgnoreCase))
                    {
                        _ = SetGpoPortAsync(2, status);
                    }
                    if (string.Equals("11", _standaloneConfigDTO.advancedGpoMode3,
                                            StringComparison.OrdinalIgnoreCase))
                    {
                        _ = SetGpoPortAsync(3, status);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing GPO error status for network connectivity. " + ex.Message);
        }
    }

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
    public async Task ProcessGpoErrorPortRecoveryAsync()
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
    {
        try
        {
            if (_standaloneConfigDTO != null)
            {
                if (string.Equals("1", _standaloneConfigDTO.advancedGpoEnabled,
                                            StringComparison.OrdinalIgnoreCase))
                {
                    if (string.Equals("10", _standaloneConfigDTO.advancedGpoMode1,
                                            StringComparison.OrdinalIgnoreCase))
                    {
                        _ = SetGpoPortAsync(1, false);
                    }
                    if (string.Equals("10", _standaloneConfigDTO.advancedGpoMode2,
                                            StringComparison.OrdinalIgnoreCase))
                    {
                        _ = SetGpoPortAsync(2, false);
                    }
                    if (string.Equals("10", _standaloneConfigDTO.advancedGpoMode3,
                                            StringComparison.OrdinalIgnoreCase))
                    {
                        _ = SetGpoPortAsync(3, false);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing GPO recovery status. " + ex.Message);
        }
    }

    private void HandleMessageReceived(object? sender, MqttMessageEventArgs e)
    {
        Console.WriteLine($"Received message from ClientId='{e.ClientId}', Topic='{e.Topic}': Payload='{e.Payload}'");

        ProcessMqttCommandMessage(e.ClientId, e.Topic, e.Payload);
    }

    private async void OnGpoErrorRequested(object? sender, MqttErrorEventArgs e)
    {
        _logger.LogInformation($"GPO Error triggered with details: {e.Details}");

        try
        {
            await ProcessGpoErrorPortAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing GPO port.");
        }
    }

    public async Task<ObservableCollection<string>> GetPresetListAsync()
    {
        InitR700IoTClient();

        var presets = await _iotDeviceInterfaceClient.GetReaderInventoryPresetListAsync();
        return presets;
    }

    private async void ProcessGpiStatus()
    {

#pragma warning disable CS8602 // Dereference of a possibly null reference.
        if (string.Equals("1", _standaloneConfigDTO.mqttEnabled, StringComparison.OrdinalIgnoreCase))
        {
            Dictionary<string, object> gpiStatusEvent = [];
            Dictionary<object, object> gpiConfigurations1 = [];
            Dictionary<object, object> gpiConfigurations2 = [];

            InitR700IoTClient();

            gpiStatusEvent.Add("eventType", "gpi-status");
            gpiStatusEvent.Add("readerName", _standaloneConfigDTO.readerName);
            gpiStatusEvent.Add("mac", _iotDeviceInterfaceClient.MacAddress);
            gpiStatusEvent.Add("timestamp", Utils.CSharpMillisToJavaLongMicroseconds(DateTime.Now));

            if (_gpiPortStates.ContainsKey(0))
            {
                if (_gpiPortStates[0])
                {
                    gpiConfigurations1.Add("gpi", 1);
                    gpiConfigurations1.Add("state", "high");
                }
                else
                {
                    gpiConfigurations1.Add("gpi", 1);
                    gpiConfigurations1.Add("state", "low");
                }

            }
            else
            {
                gpiConfigurations1.Add("gpi", 1);
                gpiConfigurations1.Add("state", "low");
            }

            if (_gpiPortStates.ContainsKey(1))
            {
                if (_gpiPortStates[1])
                {
                    gpiConfigurations2.Add("gpi", 2);
                    gpiConfigurations2.Add("state", "high");
                }
                else
                {
                    gpiConfigurations2.Add("gpi", 2);
                    gpiConfigurations2.Add("state", "low");
                }
            }
            else
            {
                gpiConfigurations2.Add("gpi", 2);
                gpiConfigurations2.Add("state", "low");
            }

            var gpiConfigurationsList = new List<object>();
            {
                gpiConfigurationsList.Add(gpiConfigurations1);
                gpiConfigurationsList.Add(gpiConfigurations2);
            }
            gpiStatusEvent.Add("gpiConfigurations", gpiConfigurationsList);


            var jsonData = JsonConvert.SerializeObject(gpiStatusEvent);
            var dataToPublish = JObject.Parse(jsonData);

            if (IsMqttEnabled())
            {
                try
                {
                    var mqttManagementEventsTopic = _standaloneConfigDTO.mqttManagementEventsTopic;
                    if (!string.IsNullOrEmpty(mqttManagementEventsTopic))
                        if (mqttManagementEventsTopic.Contains("{{deviceId}}"))
                            mqttManagementEventsTopic =
                                mqttManagementEventsTopic.Replace("{{deviceId}}", _standaloneConfigDTO.readerName);
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


                    //_messageQueueTagSmartReaderTagEventMqtt.Enqueue(dataToPublish);
                    var mqttCommandResponseTopic = $"{mqttManagementEventsTopic}";
                    var serializedData = JsonConvert.SerializeObject(dataToPublish);
                    if (gpiStatusEvent.Count > 0)
                    {
                        _logger.LogInformation($"Publishing gpi status event: {serializedData}");
                        await _mqttService.PublishAsync(mqttCommandResponseTopic, serializedData, _iotDeviceInterfaceClient.MacAddress, mqttQualityOfServiceLevel, retain);
                    }

                }
                catch (Exception)
                {
                }
            }


        }
#pragma warning restore CS8602 // Dereference of a possibly null reference.

    }

    private string ProcessAdminPasswordUpdate(string updateAdminPasswordCommandPayload)
    {
        string commandResult = "success";
        string username = "admin";
        string newPassword = "admin";
        try
        {
            JObject commandPayloadJObject = [];
            var deserializedConfigData = JsonConvert.DeserializeObject<JObject>(updateAdminPasswordCommandPayload);
#pragma warning disable CS8600, CS8602, CS8604 // Dereference of a possibly null reference.
            if (deserializedConfigData.ContainsKey("payload"))
            {
                commandPayloadJObject = deserializedConfigData["payload"].Value<JObject>();
            }
            else if (deserializedConfigData.ContainsKey("fields"))
            {
                commandPayloadJObject = deserializedConfigData["fields"].Value<JObject>();
            }
#pragma warning restore CS8602 // Dereference of a possibly null reference.
            if (commandPayloadJObject != null && commandPayloadJObject.ContainsKey("username"))
            {
                try
                {
                    var commandUsername = commandPayloadJObject["username"].Value<string>();
                    if (!string.IsNullOrEmpty(commandUsername))
                    {
                        username = commandUsername;
                    }
                }
                catch (Exception ex)
                {
                    commandResult = "error setting the new username";
                    _logger.LogError(ex,
                        "ProcessAdminPasswordUpdate (username): Unexpected error" + ex.Message);
                    return commandResult;
                }

            }
            if (commandPayloadJObject != null && commandPayloadJObject.ContainsKey("currentPassword"))
            {

                try
                {
                    var commandCurrentPassword = commandPayloadJObject["currentPassword"].Value<string>();
                    if (!string.IsNullOrEmpty(commandCurrentPassword))
                    {
                        string currentPassword = commandCurrentPassword;
                    }
                }
                catch (Exception ex)
                {
                    commandResult = "error setting the current password";
                    _logger.LogError(ex,
                        "ProcessAdminPasswordUpdate (password): Unexpected error" + ex.Message);
                    return commandResult;
                }
            }
            if (commandPayloadJObject != null && commandPayloadJObject.ContainsKey("newPassword"))
            {

                try
                {
                    var commandNewPassword = commandPayloadJObject["newPassword"].Value<string>();
                    if (!string.IsNullOrEmpty(commandNewPassword))
                    {
                        newPassword = commandNewPassword;
                    }
                }
                catch (Exception ex)
                {
                    commandResult = "error setting the new password";
                    _logger.LogError(ex,
                        "ProcessAdminPasswordUpdate (password): Unexpected error" + ex.Message);
                    return commandResult;
                }
            }
            var dtos = new List<StandaloneConfigDTO>();
            var configDto = ConfigFileHelper.GetSmartreaderDefaultConfigDTO();

            if (configDto != null)
            {
                try
                {
                    // Read the JSON file content
                    string json = "";
                    CustomAuth data = new();
                    try
                    {
                        using (StreamReader reader = File.OpenText("customsettings.json"))
                        {
                            json = reader.ReadToEndAsync().Result;
                        }
                        if (!string.IsNullOrEmpty(json))
                        {
                            // Deserialize JSON to a data structure
                            data = System.Text.Json.JsonSerializer.Deserialize<CustomAuth>(json);
                        }
                    }
                    catch (Exception ex)
                    {
                        commandResult = "error parsing data.";
                        _logger.LogError(ex,
                        "ProcessAdminPasswordUpdate error parsing data" + ex.Message);
                        return commandResult;
                    }

                    // Modify the data
#pragma warning disable CS8602 // Dereference of a possibly null reference.
                    data.BasicAuth.UserName = username;
#pragma warning restore CS8602 // Dereference of a possibly null reference.
                    if (!data.BasicAuth.Password.Equals(newPassword))
                    {
                        data.BasicAuth.Password = newPassword;
                    }

                    //data.RShellAuth.UserName = "root";
                    //data.RShellAuth.Password = "impinj";
                    if (string.IsNullOrEmpty(data.RShellAuth.UserName))
                    {
                        data.RShellAuth.UserName = "root";
                    }
                    if (string.IsNullOrEmpty(data.RShellAuth.Password))
                    {
                        data.RShellAuth.Password = "impinj";
                    }


                    // Serialize the modified data back to JSON
                    var options = new JsonSerializerOptions { WriteIndented = true };
                    var updatedJson = System.Text.Json.JsonSerializer.Serialize(data, options);

                    // Write the updated JSON back to the file
                    using (StreamWriter writer = File.CreateText("customsettings.json"))
                    {
                        writer.WriteAsync(updatedJson).Wait();
                    }
                }
                catch (Exception ex)
                {
                    commandResult = "error saving data.";
                    _logger.LogError(ex,
                    "ProcessAdminPasswordUpdate error saving data" + ex.Message);
                    return commandResult;

                }

                return commandResult;
            }
        }
        catch (Exception ex)
        {

            commandResult = "error processing command.";
            _logger.LogError(ex,
            "ProcessAdminPasswordUpdate error processing command" + ex.Message);
            return commandResult;
        }
        return commandResult;
    }

    private string ProcessRShellPasswordUpdate(string updateRShellPasswordCommandPayload)
    {
        string commandResult = "success";
        string username = "root";
        string newPassword = "impinj";
        try
        {
            JObject commandPayloadJObject = [];
            var deserializedConfigData = JsonConvert.DeserializeObject<JObject>(updateRShellPasswordCommandPayload);
#pragma warning disable CS8602 // Dereference of a possibly null reference.
            if (deserializedConfigData.ContainsKey("payload"))
            {
                commandPayloadJObject = deserializedConfigData["payload"].Value<JObject>();
            }
            else if (deserializedConfigData.ContainsKey("fields"))
            {
                commandPayloadJObject = deserializedConfigData["fields"].Value<JObject>();
            }
#pragma warning restore CS8602 // Dereference of a possibly null reference.
            if (commandPayloadJObject != null && commandPayloadJObject.ContainsKey("username"))
            {
                try
                {
                    var commandUsername = commandPayloadJObject["username"].Value<string>();
                    if (!string.IsNullOrEmpty(commandUsername))
                    {
                        username = commandUsername;
                    }
                }
                catch (Exception ex)
                {
                    commandResult = "error setting the new username";
                    _logger.LogError(ex,
                        "ProcessAdminPasswordUpdate (username): Unexpected error" + ex.Message);
                    return commandResult;
                }

            }
            if (commandPayloadJObject != null && commandPayloadJObject.ContainsKey("currentPassword"))
            {

                try
                {
                    var commandCurrentPassword = commandPayloadJObject["currentPassword"].Value<string>();
                    if (!string.IsNullOrEmpty(commandCurrentPassword))
                    {
                        string currentPassword = commandCurrentPassword;
                    }
                }
                catch (Exception ex)
                {
                    commandResult = "error setting the current password";
                    _logger.LogError(ex,
                        "ProcessAdminPasswordUpdate (password): Unexpected error" + ex.Message);
                    return commandResult;
                }
            }
            if (commandPayloadJObject != null && commandPayloadJObject.ContainsKey("newPassword"))
            {

                try
                {
                    var commandNewPassword = commandPayloadJObject["newPassword"].Value<string>();
                    if (!string.IsNullOrEmpty(commandNewPassword))
                    {
                        newPassword = commandNewPassword;
                    }
                }
                catch (Exception ex)
                {
                    commandResult = "error setting the new password";
                    _logger.LogError(ex,
                        "ProcessAdminPasswordUpdate (password): Unexpected error" + ex.Message);
                    return commandResult;
                }
            }
            var dtos = new List<StandaloneConfigDTO>();
            var configDto = ConfigFileHelper.GetSmartreaderDefaultConfigDTO();

            if (configDto != null)
            {
                try
                {
                    // Read the JSON file content
                    string json = "";
                    CustomAuth data = new();
                    try
                    {
                        using (StreamReader reader = File.OpenText("customsettings.json"))
                        {
                            json = reader.ReadToEndAsync().Result;
                        }
                        if (!string.IsNullOrEmpty(json))
                        {
                            // Deserialize JSON to a data structure
                            data = System.Text.Json.JsonSerializer.Deserialize<CustomAuth>(json);
                        }
                    }
                    catch (Exception ex)
                    {
                        commandResult = "error parsing data.";
                        _logger.LogError(ex,
                        "ProcessAdminPasswordUpdate error parsing data" + ex.Message);
                        return commandResult;
                    }


#pragma warning disable CS8602 // Dereference of a possibly null reference.
                    if (string.IsNullOrEmpty(data.BasicAuth.UserName))
                    {
                        data.BasicAuth.UserName = "admin";
                    }
#pragma warning restore CS8602 // Dereference of a possibly null reference.
                    if (string.IsNullOrEmpty(data.BasicAuth.Password))
                    {
                        data.BasicAuth.Password = "admin";
                    }
                    // Modify the data
                    data.RShellAuth.UserName = username;
                    if (!data.RShellAuth.Password.Equals(newPassword))
                    {
                        data.RShellAuth.Password = newPassword;
                    }

                    // Serialize the modified data back to JSON
                    var options = new JsonSerializerOptions { WriteIndented = true };
                    var updatedJson = System.Text.Json.JsonSerializer.Serialize(data, options);

                    // Write the updated JSON back to the file
                    using (StreamWriter writer = File.CreateText("customsettings.json"))
                    {
                        writer.WriteAsync(updatedJson).Wait();
                    }
                }
                catch (Exception ex)
                {
                    commandResult = "error saving data.";
                    _logger.LogError(ex,
                    "ProcessAdminPasswordUpdate error saving data" + ex.Message);
                    return commandResult;

                }

                return commandResult;
            }
        }
        catch (Exception ex)
        {

            commandResult = "error processing command.";
            _logger.LogError(ex,
            "ProcessAdminPasswordUpdate error processing command" + ex.Message);
            return commandResult;
        }
        return commandResult;
    }

    private async void ProcessAppStatus()
    {
        Dictionary<string, string> statusEvent = new()
        {
            { "eventType", "status" },
            { "component", "smartreader" },
#pragma warning disable CS8602 // Dereference of a possibly null reference.
            { "readerName", _standaloneConfigDTO.readerName },
#pragma warning restore CS8602 // Dereference of a possibly null reference.
#pragma warning disable CS8602 // Dereference of a possibly null reference.
            { "serialNumber", $"{_iotDeviceInterfaceClient.UniqueId}" },
#pragma warning restore CS8602 // Dereference of a possibly null reference.
            { "timestamp", DateTime.Now.ToUniversalTime().ToString("o") },
            //statusEvent.Add("timestamp", $"{currentReaderStatus.Time.Value.ToUniversalTime().ToString("o")}");
            //statusEvent.Add("displayName", _iotDeviceInterfaceClient.DisplayName);
            //statusEvent.Add("hostname", _iotDeviceInterfaceClient.Hostname);
            { "macAddress", _iotDeviceInterfaceClient.MacAddress }
        };
        try
        {
            var antennaPorts = _standaloneConfigDTO.antennaPorts.Split(",");
            var antennaPortStates = _standaloneConfigDTO.antennaStates.Split(",");
            var antennaZones = _standaloneConfigDTO.antennaZones.Split(",");
            var txPower = _standaloneConfigDTO.transmitPower.Split(",");
            var rxSensitivity = _standaloneConfigDTO.receiveSensitivity.Split(",");

            for (int i = 0; i < antennaPorts.Length; i++)
            {
                var antennaStatusDescription = $"antenna{antennaPorts[i]}Enabled";
                var currentAntennaStatus = false;
                if (antennaPortStates[i] != "0")
                {
                    currentAntennaStatus = true;
                }
                var antennaStatusValue = $"{currentAntennaStatus}";
                statusEvent.Add(antennaStatusDescription, antennaStatusValue);

                var antennaZoneDescription = $"antenna{antennaPorts[i]}Zone";
                var antennaZoneValue = $"{antennaZones[i]}";
                statusEvent.Add(antennaZoneDescription, antennaZoneValue);

                var antennaTxPowerDescription = $"antenna{antennaPorts[i]}TxPower";
                double txPowerValuecDbm = 30.00;
                _ = double.TryParse(txPower[i], NumberStyles.Float, CultureInfo.InvariantCulture, out txPowerValuecDbm);
                double txPowerValue = txPowerValuecDbm / 100;
                var antennaTxPowerValue = $"{txPowerValue}";
                statusEvent.Add(antennaTxPowerDescription, antennaTxPowerValue);

                var antennaRxSensitivityDescription = $"antenna{antennaPorts[i]}RxSensitivity";
                var antennaRxSensitivityValue = $"{rxSensitivity[i]}";
                statusEvent.Add(antennaRxSensitivityDescription, antennaRxSensitivityValue);
            }

        }
        catch (Exception)
        {


        }

        try
        {
            if (_iotDeviceInterfaceClient.IpAddresses != null
                && _iotDeviceInterfaceClient.IpAddresses.Any())
            {
                string ipAddresses = "";
                foreach (var ipAddress in _iotDeviceInterfaceClient.IpAddresses)
                {
                    ipAddresses += $"{ipAddress};";
                }
                statusEvent.Add("ipAddresses", ipAddresses);
            }
            var currentReaderStatus = await _iotDeviceInterfaceClient.GetStatusAsync();
            if (currentReaderStatus != null && currentReaderStatus.Status.HasValue)
            {
                statusEvent.Add("status", $"{currentReaderStatus.Status.Value}");
                if (currentReaderStatus.ActivePreset != null)
                {
                    statusEvent.Add("activePreset", $"{currentReaderStatus.ActivePreset.Id}");
                }
                else
                {
                    statusEvent.Add("activePreset", "");
                }

            }
            else
            {
                statusEvent.Add("status", "unknown");
            }
        }
        catch (Exception)
        {
            statusEvent.Add("status", "unknown");
        }

        try
        {
            var currentSystemInfo = await _iotDeviceInterfaceClient.GetSystemInfoAsync();
            if (currentSystemInfo != null)
            {
                statusEvent.Add("manufacturer", $"{currentSystemInfo.Manufacturer}");
                statusEvent.Add("productHla", $"{currentSystemInfo.ProductHla}");
                statusEvent.Add("productModel", $"{currentSystemInfo.ProductModel}");
                statusEvent.Add("productSku", $"{currentSystemInfo.ProductSku}");
                statusEvent.Add("productDescription", $"{currentSystemInfo.ProductDescription}");
            }
        }
        catch (Exception)
        {

        }

        statusEvent.Add("isAntennaHubEnabled", $"{_iotDeviceInterfaceClient.IsAntennaHubEnabled}");
        statusEvent.Add("readerOperatingRegion", $"{_iotDeviceInterfaceClient.ReaderOperatingRegion}");

        if (string.Equals("1", _standaloneConfigDTO.siteEnabled, StringComparison.OrdinalIgnoreCase))
        {
            statusEvent.Add("site", _standaloneConfigDTO.site);
        }



        if (IsGpiEnabled())
        {
            if (_gpiPortStates.ContainsKey(0))
            {
                if (_gpiPortStates[0])
                    statusEvent.Add("gpi1", "high");
                else
                    statusEvent.Add("gpi1", "low");
            }

            if (_gpiPortStates.ContainsKey(1))
            {
                if (_gpiPortStates[1])
                    statusEvent.Add("gpi2", "high");
                else
                    statusEvent.Add("gpi2", "low");
            }
        }


        if (!string.IsNullOrEmpty(_standaloneConfigDTO.customField1Enabled)
            && string.Equals("1", _standaloneConfigDTO.customField1Enabled, StringComparison.OrdinalIgnoreCase))
        {
            statusEvent.Add(_standaloneConfigDTO.customField1Name, _standaloneConfigDTO.customField1Value);
        }

        if (!string.IsNullOrEmpty(_standaloneConfigDTO.customField2Enabled)
            && string.Equals("1", _standaloneConfigDTO.customField2Enabled, StringComparison.OrdinalIgnoreCase))
        {
            statusEvent.Add(_standaloneConfigDTO.customField2Name, _standaloneConfigDTO.customField2Value);
        }

        if (!string.IsNullOrEmpty(_standaloneConfigDTO.customField3Enabled)
            && string.Equals("1", _standaloneConfigDTO.customField3Enabled, StringComparison.OrdinalIgnoreCase))
        {
            statusEvent.Add(_standaloneConfigDTO.customField3Name, _standaloneConfigDTO.customField3Value);
        }

        if (!string.IsNullOrEmpty(_standaloneConfigDTO.customField4Enabled)
            && string.Equals("1", _standaloneConfigDTO.customField4Enabled, StringComparison.OrdinalIgnoreCase))
        {
            statusEvent.Add(_standaloneConfigDTO.customField4Name, _standaloneConfigDTO.customField4Value);
        }

        var jsonData = JsonConvert.SerializeObject(statusEvent);
        var dataToPublish = JObject.Parse(jsonData);

        //if (string.Equals("1", _standaloneConfigDTO.httpPostEnabled, StringComparison.OrdinalIgnoreCase))
        //    try
        //    {
        //        _messageQueueTagSmartReaderTagEventHttpPost.Enqueue(dataToPublish);
        //    }
        //    catch (Exception)
        //    {
        //    }

        //if (string.Equals("1", _standaloneConfigDTO.socketServer, StringComparison.OrdinalIgnoreCase))
        //    try
        //    {
        //        _messageQueueTagSmartReaderTagEventSocketServer.Enqueue(dataToPublish);
        //    }
        //    catch (Exception)
        //    {
        //    }

        //if (string.Equals("1", _standaloneConfigDTO.usbFlashDrive, StringComparison.OrdinalIgnoreCase))
        //    try
        //    {
        //        _messageQueueTagSmartReaderTagEventUsbDrive.Enqueue(dataToPublish);
        //    }
        //    catch (Exception)
        //    {
        //    }


        if (string.Equals("1", _standaloneConfigDTO.mqttEnabled, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var mqttManagementEventsTopic = _standaloneConfigDTO.mqttManagementEventsTopic;
                if (!string.IsNullOrEmpty(mqttManagementEventsTopic))
                    if (mqttManagementEventsTopic.Contains("{{deviceId}}"))
                        mqttManagementEventsTopic =
                            mqttManagementEventsTopic.Replace("{{deviceId}}", _standaloneConfigDTO.readerName);
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


                //_messageQueueTagSmartReaderTagEventMqtt.Enqueue(dataToPublish);
                var mqttCommandResponseTopic = $"{mqttManagementEventsTopic}";
                var serializedData = JsonConvert.SerializeObject(dataToPublish);
                if (statusEvent.Count > 0)
                {
                    _logger.LogInformation($"Publishing app status event: {serializedData}");
                    await _mqttService.PublishAsync(mqttCommandResponseTopic, serializedData, _iotDeviceInterfaceClient.MacAddress, mqttQualityOfServiceLevel, retain);
                }

            }
            catch (Exception)
            {
                _ = ProcessGpoErrorPortAsync();
            }
        }

    }

    private bool ProcessBatteryStatusCheck()
    {
        bool currentStatus = false;
        try
        {

            try
            {
                //cat /proc/driver/rtc
                // Create a process info
                ProcessStartInfo startInfo = new()
                {
                    FileName = "/bin/cat",  // Set the command (cat)
                    Arguments = "/proc/driver/rtc",  // Set the file to read
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                // Start the process
                using Process process = new();
                process.StartInfo = startInfo;
                _ = process.Start();

                // Read the output
                using var reader = process.StandardOutput;
                string output = reader.ReadToEnd();
                var lines = output.Split("\n");
                foreach (var line in lines)
                {
                    if (line.Contains("batt_status") && line.Contains("okay"))
                    {
                        currentStatus = true;
                        break;
                    }

                }
            }
            catch (Exception)
            {
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "error loading image status");
        }
        return currentStatus;
    }



    private async void ProcessAppStatusDetailed()
    {


        Dictionary<string, string> statusEvent = new()
        {
            { "eventType", "status" },
            { "component", "smartreader" },
#pragma warning disable CS8602 // Dereference of a possibly null reference.
            { "readerName", _standaloneConfigDTO.readerName },
#pragma warning restore CS8602 // Dereference of a possibly null reference.
            //statusEvent.Add("serialNumber", $"{_iotDeviceInterfaceClient.UniqueId}");
            { "timestamp", DateTime.Now.ToUniversalTime().ToString("o") },
            //statusEvent.Add("timestamp", $"{currentReaderStatus.Time.Value.ToUniversalTime().ToString("o")}");
            //statusEvent.Add("displayName", _iotDeviceInterfaceClient.DisplayName);
            //statusEvent.Add("hostname", _iotDeviceInterfaceClient.Hostname);
#pragma warning disable CS8602 // Dereference of a possibly null reference.
            { "macAddress", _iotDeviceInterfaceClient.MacAddress }
        };
#pragma warning restore CS8602 // Dereference of a possibly null reference.
        try
        {
            var antennaPorts = _standaloneConfigDTO.antennaPorts.Split(",");
            var antennaPortStates = _standaloneConfigDTO.antennaStates.Split(",");
            var antennaZones = _standaloneConfigDTO.antennaZones.Split(",");
            var txPower = _standaloneConfigDTO.transmitPower.Split(",");
            var rxSensitivity = _standaloneConfigDTO.receiveSensitivity.Split(",");

            for (int i = 0; i < antennaPorts.Length; i++)
            {
                var antennaStatusDescription = $"antenna{antennaPorts[i]}Enabled";
                var currentAntennaStatus = false;
                if (antennaPortStates[i] != "0")
                {
                    currentAntennaStatus = true;
                }
                var antennaStatusValue = $"{currentAntennaStatus}";
                statusEvent.Add(antennaStatusDescription, antennaStatusValue);

                var antennaZoneDescription = $"antenna{antennaPorts[i]}Zone";
                var antennaZoneValue = $"{antennaZones[i]}";
                statusEvent.Add(antennaZoneDescription, antennaZoneValue);

                var antennaTxPowerDescription = $"antenna{antennaPorts[i]}TxPower";
                double txPowerValuecDbm = 30.00;
                _ = double.TryParse(txPower[i], NumberStyles.Float, CultureInfo.InvariantCulture, out txPowerValuecDbm);
                double txPowerValue = txPowerValuecDbm / 100;
                var antennaTxPowerValue = $"{txPowerValue}";
                statusEvent.Add(antennaTxPowerDescription, antennaTxPowerValue);

                var antennaRxSensitivityDescription = $"antenna{antennaPorts[i]}RxSensitivity";
                var antennaRxSensitivityValue = $"{rxSensitivity[i]}";
                statusEvent.Add(antennaRxSensitivityDescription, antennaRxSensitivityValue);
            }

        }
        catch (Exception)
        {


        }

        try
        {
            if (_iotDeviceInterfaceClient.IpAddresses != null
                && _iotDeviceInterfaceClient.IpAddresses.Any())
            {
                string ipAddresses = "";
                foreach (var ipAddress in _iotDeviceInterfaceClient.IpAddresses)
                {
                    ipAddresses += $"{ipAddress};";
                }
                statusEvent.Add("ipAddresses", ipAddresses);
            }
            var currentReaderStatus = await _iotDeviceInterfaceClient.GetStatusAsync();
            if (currentReaderStatus != null && currentReaderStatus.Status.HasValue)
            {
                statusEvent.Add("status", $"{currentReaderStatus.Status.Value}");
                if (currentReaderStatus.ActivePreset != null)
                {
                    statusEvent.Add("activePreset", $"{currentReaderStatus.ActivePreset.Id}");
                }
                else
                {
                    statusEvent.Add("activePreset", "");
                }

            }
            else
            {
                statusEvent.Add("status", "unknown");
            }
        }
        catch (Exception)
        {
            statusEvent.Add("status", "unknown");
        }

        try
        {
            var currentSystemInfo = await _iotDeviceInterfaceClient.GetSystemInfoAsync();
            if (currentSystemInfo != null)
            {
                statusEvent.Add("manufacturer", $"{currentSystemInfo.Manufacturer}");
                statusEvent.Add("productHla", $"{currentSystemInfo.ProductHla}");
                statusEvent.Add("productModel", $"{currentSystemInfo.ProductModel}");
                statusEvent.Add("productSku", $"{currentSystemInfo.ProductSku}");
                statusEvent.Add("productDescription", $"{currentSystemInfo.ProductDescription}");
            }
        }
        catch (Exception)
        {

        }

        statusEvent.Add("isAntennaHubEnabled", $"{_iotDeviceInterfaceClient.IsAntennaHubEnabled}");
        statusEvent.Add("readerOperatingRegion", $"{_iotDeviceInterfaceClient.ReaderOperatingRegion}");

        if (string.Equals("1", _standaloneConfigDTO.siteEnabled, StringComparison.OrdinalIgnoreCase))
        {
            statusEvent.Add("site", _standaloneConfigDTO.site);
        }

        try
        {
            var currentBatStatus = ProcessBatteryStatusCheck();
            if (currentBatStatus)
                statusEvent.Add("batteryCondition", "ok");
            else
                statusEvent.Add("batteryCondition", "unknown");
        }
        catch (Exception)
        {


        }


        if (IsGpiEnabled())
        {
            if (_gpiPortStates.ContainsKey(0))
            {
                if (_gpiPortStates[0])
                    statusEvent.Add("gpi1", "high");
                else
                    statusEvent.Add("gpi1", "low");
            }

            if (_gpiPortStates.ContainsKey(1))
            {
                if (_gpiPortStates[1])
                    statusEvent.Add("gpi2", "high");
                else
                    statusEvent.Add("gpi2", "low");
            }
        }


        if (!string.IsNullOrEmpty(_standaloneConfigDTO.customField1Enabled)
            && string.Equals("1", _standaloneConfigDTO.customField1Enabled, StringComparison.OrdinalIgnoreCase))
        {
            statusEvent.Add(_standaloneConfigDTO.customField1Name, _standaloneConfigDTO.customField1Value);
        }

        if (!string.IsNullOrEmpty(_standaloneConfigDTO.customField2Enabled)
            && string.Equals("1", _standaloneConfigDTO.customField2Enabled, StringComparison.OrdinalIgnoreCase))
        {
            statusEvent.Add(_standaloneConfigDTO.customField2Name, _standaloneConfigDTO.customField2Value);
        }

        if (!string.IsNullOrEmpty(_standaloneConfigDTO.customField3Enabled)
            && string.Equals("1", _standaloneConfigDTO.customField3Enabled, StringComparison.OrdinalIgnoreCase))
        {
            statusEvent.Add(_standaloneConfigDTO.customField3Name, _standaloneConfigDTO.customField3Value);
        }

        if (!string.IsNullOrEmpty(_standaloneConfigDTO.customField4Enabled)
            && string.Equals("1", _standaloneConfigDTO.customField4Enabled, StringComparison.OrdinalIgnoreCase))
        {
            statusEvent.Add(_standaloneConfigDTO.customField4Name, _standaloneConfigDTO.customField4Value);
        }

        try
        {
            var rshell = new RShellUtil(_readerAddress, _readerUsername, _readerPassword);
            try
            {
                var resultRfidStat = rshell.SendCommand("show rfid stat");
                var lines = resultRfidStat.Split("\n");
                foreach (var line in lines)
                {
                    if (line.StartsWith("status") || line.StartsWith("Status"))
                    {
                        continue;
                    }
                    var values = line.Split("=");
                    try
                    {
                        statusEvent.Add(values[0], values[1].Replace("'", String.Empty));
                    }
                    catch (Exception)
                    {
                    }
                }
            }
            catch (Exception)
            {
            }

            try
            {
                var resultRfidStat = rshell.SendCommand("show system platform");
                var lines = resultRfidStat.Split("\n");
                foreach (var line in lines)
                {
                    if (line.StartsWith("status") || line.StartsWith("Status"))
                    {
                        continue;
                    }
                    var values = line.Split("=");
                    try
                    {
                        statusEvent.Add(values[0], values[1].Replace("'", String.Empty));
                    }
                    catch (Exception)
                    {
                    }
                }
            }
            catch (Exception)
            {
            }

            try
            {
                var resultSystemCpu = rshell.SendCommand("show system cpu");
                var lines = resultSystemCpu.Split("\n");
                foreach (var line in lines)
                {
                    if (line.StartsWith("status") || line.StartsWith("Status"))
                    {
                        continue;
                    }
                    var values = line.Split("=");
                    try
                    {
                        statusEvent.Add(values[0], values[1].Replace("'", String.Empty));
                    }
                    catch (Exception)
                    {
                    }
                }
            }
            catch (Exception)
            {
            }

            try
            {
                var resultSystemPower = rshell.SendCommand("show system power");
                var lines = resultSystemPower.Split("\n");
                foreach (var line in lines)
                {
                    if (line.StartsWith("status") || line.StartsWith("Status"))
                    {
                        continue;
                    }
                    var values = line.Split("=");
                    try
                    {

                        statusEvent.Add(values[0], values[1].Replace("'", String.Empty));
                    }
                    catch (Exception)
                    {
                    }

                }
            }
            catch (Exception)
            {
            }

            _logger.LogInformation("Requesting image status. ");

            try
            {
                var resultImageStatus = rshell.SendCommand("show image summary");
                var lines = resultImageStatus.Split("\n");
                foreach (var line in lines)
                {
                    _logger.LogInformation(line);
                    if (line.ToUpper().Contains("STATUS"))
                    {
                        continue;
                    }
                    var lineData = line.Split("=");
                    if (lineData.Length > 1)
                    {
                        var lineValueContent = "";
                        if (lineData[1].Contains("'"))
                        {
                            lineValueContent = lineData[1].Replace("'", "");
                        }
                        statusEvent.Add(lineData[0], lineValueContent);
                    }

                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "error loading image status");
            }

            try
            {
                rshell.Disconnect();
            }
            catch (Exception)
            {


            }
        }
        catch (Exception)
        {

        }




        var jsonData = JsonConvert.SerializeObject(statusEvent);
        var dataToPublish = JObject.Parse(jsonData);

        //if (string.Equals("1", _standaloneConfigDTO.httpPostEnabled, StringComparison.OrdinalIgnoreCase))
        //    try
        //    {
        //        _messageQueueTagSmartReaderTagEventHttpPost.Enqueue(dataToPublish);
        //    }
        //    catch (Exception)
        //    {
        //    }

        //if (string.Equals("1", _standaloneConfigDTO.socketServer, StringComparison.OrdinalIgnoreCase))
        //    try
        //    {
        //        _messageQueueTagSmartReaderTagEventSocketServer.Enqueue(dataToPublish);
        //    }
        //    catch (Exception)
        //    {
        //    }

        //if (string.Equals("1", _standaloneConfigDTO.usbFlashDrive, StringComparison.OrdinalIgnoreCase))
        //    try
        //    {
        //        _messageQueueTagSmartReaderTagEventUsbDrive.Enqueue(dataToPublish);
        //    }
        //    catch (Exception)
        //    {
        //    }


        if (string.Equals("1", _standaloneConfigDTO.mqttEnabled, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var mqttManagementEventsTopic = _standaloneConfigDTO.mqttManagementEventsTopic;
                if (!string.IsNullOrEmpty(mqttManagementEventsTopic))
                    if (mqttManagementEventsTopic.Contains("{{deviceId}}"))
                        mqttManagementEventsTopic =
                            mqttManagementEventsTopic.Replace("{{deviceId}}", _standaloneConfigDTO.readerName);
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


                //_messageQueueTagSmartReaderTagEventMqtt.Enqueue(dataToPublish);
                var mqttCommandResponseTopic = $"{mqttManagementEventsTopic}";
                var serializedData = JsonConvert.SerializeObject(dataToPublish);
                if (statusEvent.Count > 0)
                {
                    _logger.LogInformation($"Publishing status event (1): {serializedData}");
                    await _mqttService.PublishAsync(mqttCommandResponseTopic,
                        serializedData,
                        _iotDeviceInterfaceClient.MacAddress,
                        mqttQualityOfServiceLevel,
                        retain);
                }

            }
            catch (Exception)
            {
                await ProcessGpoErrorPortAsync();
            }
        }

    }

    private JObject CreateSmartReaderTagReadEvent(bool addEmptytagRead)
    {
        var smartReaderTagReadEvent = new SmartReaderTagReadEvent
        {
            TagReads = [],
            ReaderName = _standaloneConfigDTO.readerName,
#pragma warning disable CS8602 // Dereference of a possibly null reference.
            Mac = _iotDeviceInterfaceClient.MacAddress
        };
#pragma warning restore CS8602 // Dereference of a possibly null reference.

        if (string.Equals("1", _standaloneConfigDTO.siteEnabled, StringComparison.OrdinalIgnoreCase))
            smartReaderTagReadEvent.Site = _standaloneConfigDTO.site;


        if (addEmptytagRead)
        {
            var tagRead = new TagRead
            {
                FirstSeenTimestamp = Utils.CSharpMillisToJavaLongMicroseconds(DateTime.Now),
                Epc = "*****",
                IsHeartBeat = true
            };
            smartReaderTagReadEvent.TagReads.Add(tagRead);

            if (IsGpiEnabled())
            {
                if (_gpiPortStates.ContainsKey(0))
                {
                    if (_gpiPortStates[0])
                    {
                        tagRead.Gpi1Status = "high";
                    }
                    else
                    {
                        tagRead.Gpi1Status = "low";
                    }
                }
                if (_gpiPortStates.ContainsKey(1))
                {
                    // tagRead.Gpi2Status = _gpiPortStates[1] ? "high" : "low";
                    if (_gpiPortStates[1])
                    {
                        tagRead.Gpi2Status = "high";
                    }
                    else
                    {
                        tagRead.Gpi2Status = "low";
                    }
                }
            }
        }

        var jsonData = JsonConvert.SerializeObject(smartReaderTagReadEvent);
        var dataToPublish = JObject.Parse(jsonData);

        // Add custom fields if enabled
        for (int i = 1; i <= 4; i++)
        {
            var enabledProperty = $"customField{i}Enabled";
            var nameProperty = $"customField{i}Name";
            var valueProperty = $"customField{i}Value";

            if (!string.IsNullOrEmpty(_standaloneConfigDTO.GetType().GetProperty(enabledProperty)?.GetValue(_standaloneConfigDTO)?.ToString())
                && string.Equals("1", _standaloneConfigDTO.GetType().GetProperty(enabledProperty)?.GetValue(_standaloneConfigDTO)?.ToString(),
                StringComparison.OrdinalIgnoreCase))
            {
                var name = _standaloneConfigDTO.GetType().GetProperty(nameProperty)?.GetValue(_standaloneConfigDTO)?.ToString();
                var value = _standaloneConfigDTO.GetType().GetProperty(valueProperty)?.GetValue(_standaloneConfigDTO)?.ToString();

                if (name != null && value != null)
                {
                    var newPropertyData = new JProperty(name, value);
                    dataToPublish.Add(newPropertyData);
                }
            }
        }

        return dataToPublish;
    }
    private async Task ProcessKeepalive()
    {
        if (!IsHeartbeatEnabled()) return;

        var dataToPublish = CreateSmartReaderTagReadEvent(true);

        if (IsHttpPostEnabled())
        {
            try
            {
                _messageQueueTagSmartReaderTagEventHttpPost.Enqueue(dataToPublish);
            }
            catch (Exception)
            {
                _logger.LogWarning($"Error enqueuing keepalive event.");
            }
        }


        if (IsSocketServerEnabled())
        {
            try
            {
                if (_tcpSocketService.IsSocketServerConnectedToClients())
                {
                    _messageQueueTagSmartReaderTagEventSocketServer.Enqueue(dataToPublish);
                }
            }
            catch (Exception)
            {
                _logger.LogWarning($"Error enqueuing keepalive event.");
            }
        }

        try
        {
            if (_webSocketService.IsSocketServerConnectedToClients())
            {
                _messageQueueTagSmartReaderTagEventWebSocketServer.Enqueue(dataToPublish);
            }
        }
        catch (Exception)
        {
            _logger.LogWarning($"Error enqueuing keepalive event.");
        }






        if (IsUsbFlashDriveEnabled())
        {
            try
            {
                _messageQueueTagSmartReaderTagEventUsbDrive.Enqueue(dataToPublish);
            }
            catch (Exception)
            {
                _logger.LogWarning($"Error enqueuing keepalive event.");
            }
        }



        if (dataToPublish != null
            && IsMqttEnabled())
        {
            try
            {
                var mqttManagementEventsTopic = _standaloneConfigDTO.mqttManagementEventsTopic;
                if (!string.IsNullOrEmpty(mqttManagementEventsTopic))
                    if (mqttManagementEventsTopic.Contains("{{deviceId}}"))
                        mqttManagementEventsTopic =
                            mqttManagementEventsTopic.Replace("{{deviceId}}", _standaloneConfigDTO.readerName);
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


                //_messageQueueTagSmartReaderTagEventMqtt.Enqueue(dataToPublish);
                var mqttCommandResponseTopic = $"{mqttManagementEventsTopic}";
                var serializedData = JsonConvert.SerializeObject(dataToPublish);
                if (serializedData != null)
                {
                    _logger.LogInformation($"Publishing tag event: {serializedData}");
                    await _mqttService.PublishAsync(mqttCommandResponseTopic, serializedData, _iotDeviceInterfaceClient.MacAddress, mqttQualityOfServiceLevel, retain);
                }

            }
            catch (Exception)
            {
                await ProcessGpoErrorPortAsync();
            }
        }

    }

    private async void OnInventoryStatusEvent(object sender, InventoryStatusEvent eventStatus)
    {


        try
        {
#pragma warning disable CS8602 // Dereference of a possibly null reference.
            if (string.Equals("1", _standaloneConfigDTO.mqttEnabled, StringComparison.OrdinalIgnoreCase))
                //try
                //{
                //    if (!string.IsNullOrEmpty(_standaloneConfigDTO.mqttManagementEventsTopic))
                //        if (_standaloneConfigDTO.mqttManagementEventsTopic.Contains("{{deviceId}}"))
                //            _standaloneConfigDTO.mqttManagementEventsTopic =
                //                _standaloneConfigDTO.mqttManagementEventsTopic.Replace("{{deviceId}}",
                //                    _standaloneConfigDTO.readerName);
                //    var qos = 0;
                //    var retain = false;
                //    var mqttQualityOfServiceLevel = MqttQualityOfServiceLevel.AtMostOnce;
                //    try
                //    {
                //        int.TryParse(_standaloneConfigDTO.mqttManagementEventsQoS, out qos);
                //        bool.TryParse(_standaloneConfigDTO.mqttManagementEventsRetainMessages, out retain);

                //        mqttQualityOfServiceLevel = qos switch
                //        {
                //            1 => MqttQualityOfServiceLevel.AtLeastOnce,
                //            2 => MqttQualityOfServiceLevel.ExactlyOnce,
                //            _ => MqttQualityOfServiceLevel.AtMostOnce
                //        };
                //    }
                //    catch (Exception)
                //    {
                //    }

                //    var mqttManagementEventsTopic = $"{_standaloneConfigDTO.mqttManagementEventsTopic}";


                //    var serializedData = JsonConvert.SerializeObject(eventStatus);
                //    await _mqttService.PublishAsync(mqttManagementEventsTopic, serializedData,
                //        mqttQualityOfServiceLevel, retain);
                //    // Wait until the queue is fully processed.
                //    
                //}
                //catch (Exception)
                //{
                //}

                //_logger.LogInformation("Inventory status : " + eventStatus.Status, SeverityType.Debug);
                if (eventStatus.Status == InventoryStatusEventStatus.Running)
                {
                    try
                    {
                        _isStarted = true;

                        if (_standaloneConfigDTO != null
                            && string.Equals("1", _standaloneConfigDTO.enableSummaryStream, StringComparison.OrdinalIgnoreCase))
                        {
                            using var scope = Services.CreateScope();
                            var summaryQueueBackgroundService = scope.ServiceProvider.GetRequiredService<ISummaryQueueBackgroundService>();
                            summaryQueueBackgroundService.StartQueue();
                        }
                    }
                    catch (Exception)
                    {
                    }
                    _logger.LogInformation("Inventory Running.  (START)");
                    //if(string.IsNullOrEmpty(_standaloneConfigDTO.stopTriggerDuration) 
                    //    || string.Equals("0", _standaloneConfigDTO.stopTriggerDuration, StringComparison.OrdinalIgnoreCase))
                    //{
                    //    ////_stopwatchStopTriggerDuration
                    //    //if (string.Equals("1", _standaloneConfigDTO.enableBarcodeTcp, StringComparison.OrdinalIgnoreCase))
                    //    //{

                    //    //}
                    //    if (string.Equals("1", _standaloneConfigDTO.groupEventsOnInventoryStatus, StringComparison.OrdinalIgnoreCase))
                    //    {
                    //        //currentBarcode = "";
                    //        _ = Task.Run(() => ProcessValidationTagQueue());

                    //    }
                    //}

#pragma warning disable CS8602 // Dereference of a possibly null reference.
                    if (!string.IsNullOrEmpty(_standaloneConfigDTO.stopTriggerDuration)
                        && !string.Equals("0", _standaloneConfigDTO.stopTriggerDuration,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        long stopTiggerDuration = 100;
                        _ = long.TryParse(_standaloneConfigDTO.stopTriggerDuration, out stopTiggerDuration);
                        //if (stopTiggerDuration > 0 && _stopwatchStopTriggerDuration.IsRunning && _stopwatchStopTriggerDuration.ElapsedMilliseconds < stopTiggerDuration)
                        //{
                        //    _logger.LogInformation("OnRunPeriodicStopTriggerDurationEvent - Inventory already Running. for duration " + stopTiggerDuration + " [" + _stopwatchStopTriggerDuration.ElapsedMilliseconds + "]", SeverityType.Debug);
                        //    Console.WriteLine("OnRunPeriodicStopTriggerDurationEvent - Inventory already Running. for duration " + stopTiggerDuration + " [" + _stopwatchStopTriggerDuration.ElapsedMilliseconds + "]");
                        //}

                        var cleanUpTags = false;
                        if (stopTiggerDuration > 0 && _stopwatchStopTriggerDuration.IsRunning
                                                   && _stopwatchStopTriggerDuration.ElapsedMilliseconds >=
                                                   stopTiggerDuration)
                        {
                            _stopwatchStopTriggerDuration.Stop();
                            _stopwatchStopTriggerDuration.Reset();
                            _stopwatchStopTriggerDuration.Start();
                            cleanUpTags = true;
                        }
                        else if (stopTiggerDuration > 0 && !_stopwatchStopTriggerDuration.IsRunning)
                        {
                            _stopwatchStopTriggerDuration.Start();
                        }
                        else if (stopTiggerDuration > 0 && !_stopwatchStopTriggerDuration.IsRunning
                                                        && _stopwatchStopTriggerDuration.ElapsedMilliseconds >=
                                                        stopTiggerDuration)
                        {
                            _stopwatchStopTriggerDuration.Stop();
                            _stopwatchStopTriggerDuration.Reset();
                            _stopwatchStopTriggerDuration.Start();
                            cleanUpTags = true;
                        }

                        //_stopwatchStopTriggerDuration
                        //if (string.Equals("1", _standaloneConfigDTO.enableBarcodeTcp, StringComparison.OrdinalIgnoreCase))
                        //{


                        //}
                        if (string.Equals("1", _standaloneConfigDTO.groupEventsOnInventoryStatus,
                                StringComparison.OrdinalIgnoreCase))
                            if (cleanUpTags)
                                //_ = Task.Run(() => ProcessValidationTagQueue());
                                ProcessValidationTagQueue();
                    }
#pragma warning restore CS8602 // Dereference of a possibly null reference.

                    if (!string.IsNullOrEmpty(_standaloneConfigDTO.advancedGpoEnabled)
                        && string.Equals("1", _standaloneConfigDTO.advancedGpoEnabled, StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrEmpty(_standaloneConfigDTO.advancedGpoMode1)
                            && string.Equals("4", _standaloneConfigDTO.advancedGpoMode1,
                                StringComparison.OrdinalIgnoreCase))
                            _ = SetGpoPortAsync(1, true);

                        if (!string.IsNullOrEmpty(_standaloneConfigDTO.advancedGpoMode2)
                            && string.Equals("4", _standaloneConfigDTO.advancedGpoMode2,
                                StringComparison.OrdinalIgnoreCase))
                            _ = SetGpoPortAsync(2, true);

                        if (!string.IsNullOrEmpty(_standaloneConfigDTO.advancedGpoMode3)
                            && string.Equals("4", _standaloneConfigDTO.advancedGpoMode3,
                                StringComparison.OrdinalIgnoreCase))
                            _ = SetGpoPortAsync(3, true);
                    }
                    try
                    {
                        if (IsMqttEnabled())
                        {
                            var mqttManagementEvents = new Dictionary<string, object>();
                            if (eventStatus.Status == InventoryStatusEventStatus.Running)
                            {
                                mqttManagementEvents.Add("smartreader-status", "inventory-running");
                            }

                            if (mqttManagementEvents.Count > 0)
                            {
                                await _mqttService.PublishMqttManagementEventAsync(mqttManagementEvents);
                            }
                        }



                    }
                    catch (Exception)
                    {
                    }
                }
                else if (eventStatus.Status == null || eventStatus.Status == InventoryStatusEventStatus.Idle)
                {

                    var shouldAcceptEventStatus = false;
                    _isStarted = false;
                    if (!_stopwatchLastIddleEvent.IsRunning)
                    {
                        _stopwatchLastIddleEvent.Start();
                        shouldAcceptEventStatus = true;
                    }

                    if (_stopwatchLastIddleEvent.ElapsedMilliseconds > 1000)
                    {
                        _stopwatchLastIddleEvent.Restart();
                        shouldAcceptEventStatus = true;
                    }

                    if (!shouldAcceptEventStatus)
                        return;
                    //if (string.Equals("1", _standaloneConfigDTO.groupEventsOnInventoryStatus, StringComparison.OrdinalIgnoreCase)
                    //    && string.Equals("2", _standaloneConfigDTO.stopTriggerType, StringComparison.OrdinalIgnoreCase))
                    //{

                    //}


                    _logger.LogInformation("Inventory Iddle. (STOP) ");

                    //if (string.Equals("1", _standaloneConfigDTO.enableBarcodeTcp, StringComparison.OrdinalIgnoreCase))
                    //{
                    if (string.Equals("1", _standaloneConfigDTO.groupEventsOnInventoryStatus,
                            StringComparison.OrdinalIgnoreCase)
                        && string.Equals("0", _standaloneConfigDTO.stopTriggerDuration, StringComparison.OrdinalIgnoreCase))
                        try
                        {
                            //_ = Task.Run(() => ProcessValidationTagQueue());
                            ProcessValidationTagQueue();
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Unexpected error on ProcessBarcodeQueue " + ex.Message);
                            //throw;
                        }
                    ////if (string.Equals("3", _standaloneConfigDTO.startTriggerType, StringComparison.OrdinalIgnoreCase))
                    ////{
                    ////    try
                    ////    {
                    ////        StartPresetAsync();
                    ////    }
                    ////    catch (Exception)
                    ////    {


                    ////    }
                    ////}
                    //}

                    if (!string.IsNullOrEmpty(_standaloneConfigDTO.advancedGpoEnabled)
                        && string.Equals("1", _standaloneConfigDTO.advancedGpoEnabled, StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrEmpty(_standaloneConfigDTO.advancedGpoMode1)
                            && string.Equals("4", _standaloneConfigDTO.advancedGpoMode1,
                                StringComparison.OrdinalIgnoreCase))
                            _ = SetGpoPortAsync(1, false);

                        if (!string.IsNullOrEmpty(_standaloneConfigDTO.advancedGpoMode2)
                            && string.Equals("4", _standaloneConfigDTO.advancedGpoMode2,
                                StringComparison.OrdinalIgnoreCase))
                            _ = SetGpoPortAsync(2, false);

                        if (!string.IsNullOrEmpty(_standaloneConfigDTO.advancedGpoMode3)
                            && string.Equals("4", _standaloneConfigDTO.advancedGpoMode3,
                                StringComparison.OrdinalIgnoreCase))
                            _ = SetGpoPortAsync(3, false);
                    }

                    try
                    {
                        if (IsMqttEnabled())
                        {
                            var mqttManagementEvents = new Dictionary<string, object>();
                            if (eventStatus.Status == null || eventStatus.Status == InventoryStatusEventStatus.Idle)
                            {
                                mqttManagementEvents.Add("smartreader-status", "inventory-idle");
                            }

                            if (mqttManagementEvents.Count > 0)
                            {
                                await _mqttService.PublishMqttManagementEventAsync(mqttManagementEvents);
                            }
                        }


                    }
                    catch (Exception)
                    {
                    }
                }
        }
        catch (Exception exOnInventoryStatusEvent)
        {
            _logger.LogError(exOnInventoryStatusEvent,
                "Unexpected error on OnInventoryStatusEvent " + exOnInventoryStatusEvent.Message);
        }


    }

    private string? ProcessBarcodeQueue()
    {
        string localBarcode = "";
        try
        {
            if (_standaloneConfigDTO != null
                && !string.IsNullOrEmpty(_standaloneConfigDTO.barcodeEnableQueue)
                && "1".Equals(_standaloneConfigDTO.barcodeEnableQueue))
            {
                while (_messageQueueBarcode.Count > 0
                       && string.IsNullOrEmpty(localBarcode))
                {
                    _ = _messageQueueBarcode.TryDequeue(out localBarcode);

                    _logger.LogInformation("ProcessBarcodeQueue - Dequeued barcode: " + localBarcode);
                    if (!string.IsNullOrEmpty(localBarcode))
                    {
                        if (_standaloneConfigDTO != null
                            && !string.IsNullOrEmpty(_standaloneConfigDTO.barcodeTcpNoDataString)
                            && localBarcode.ToUpper().Contains(_standaloneConfigDTO.barcodeTcpNoDataString.ToUpper()))
                        {
                            if (!string.IsNullOrEmpty(_standaloneConfigDTO.barcodeProcessNoDataString)
                                && "0".Equals(_standaloneConfigDTO.barcodeProcessNoDataString))
                            {
                                _logger.LogInformation("ProcessBarcodeQueue - ignoring NoDataString [" + localBarcode +
                                                       "] ");
                                continue;
                            }

                            _logger.LogInformation("ProcessBarcodeQueue - PROCESSING NoDataString [" + localBarcode +
                                                   "] ");
                        }

                        _logger.LogInformation("ProcessBarcodeQueue - Selecting barcode: [" + localBarcode + "] ");
                        break;
                    }
                }
            }
            else
            {
                _logger.LogInformation("ProcessBarcode - Selecting barcode: [" + LastValidBarcode + "] ");
                localBarcode = new string(LastValidBarcode);

                LastValidBarcode = "";
                _logger.LogInformation("ProcessBarcode - Cleaningup old barcode: [" + LastValidBarcode + "] ");
            }


            _logger.LogInformation("ProcessBarcode - Barcode to use: [" + localBarcode + "]");
        }
        catch (Exception)
        {
        }

        return localBarcode;
    }

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
    private async void OnGpiTransitionEvent(object sender, GpiTransitionVm gpiEvent)
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
    {
        try
        {
            var currentGpiStatus = false;
            _logger.LogInformation("Gpi Transition : " + gpiEvent.GpiTransitionEvent);
            int gpiInternalPort = gpiEvent.GpiTransitionEvent.Gpi - 1;

            if (gpiEvent.GpiTransitionEvent.Transition == TransitionType.HighToLow)
            {
                currentGpiStatus = false;
                _logger.LogInformation($"Setting Gpi {gpiInternalPort} to [{currentGpiStatus}]");
            }
            else if (gpiEvent.GpiTransitionEvent.Transition == TransitionType.LowToHigh)
            {
                currentGpiStatus = true;
                _logger.LogInformation($"Setting Gpi {gpiInternalPort} to [{currentGpiStatus}]");
            }


            if (gpiInternalPort == 0 || gpiInternalPort == 1)
            {
                if (_gpiPortStates.ContainsKey(gpiInternalPort))
                {
                    _gpiPortStates[gpiInternalPort] = currentGpiStatus;
                    _logger.LogInformation($"Gpi {gpiInternalPort} updated to [{_gpiPortStates[gpiInternalPort]}]");
                }
                else
                {
                    _ = _gpiPortStates.TryAdd(gpiInternalPort, currentGpiStatus);
                    _logger.LogInformation($"Gpi {gpiInternalPort} updated to [{_gpiPortStates[gpiInternalPort]}]");
                }
            }
            else
            {
                _logger.LogWarning($"Gpi port number not supported: {gpiInternalPort}");
            }





        }
        catch (Exception exOnGpiStatusEvent)
        {
            _logger.LogError(exOnGpiStatusEvent,
                "Unexpected error on OnGpiTransitionEvent " + exOnGpiStatusEvent.Message);
        }
    }


    private async void OnTagInventoryEvent(object sender,
                                           TagInventoryEvent tagEvent)
    {
        try
        {
            // Validate input
            if (tagEvent == null)
            {
                _logger.LogError("Received null tag event");
                return;
            }

            if (!_isStarted)
            {
                _isStarted = true;
            }

            var shouldProcess = false;
            _logger.LogDebug($"EPC Hex: {tagEvent.EpcHex} Antenna : {tagEvent.AntennaPort} LastSeenTime {tagEvent.LastSeenTime}");

            await Task.Run(() => ProcessGpoBlinkAnyTagGpoPortAsync());


            //if (string.Equals("1", _standaloneConfigDTO.tagPresenceTimeoutEnabled,
            //                StringComparison.OrdinalIgnoreCase))
            //{
            //    if (_smartReaderTagEventsListBatch.ContainsKey(tagEvent.EpcHex))
            //    {
            //        var epcObject = _smartReaderTagEventsListBatch[tagEvent.EpcHex];
            //        var updatedCurrentEventTimestamp = Utils.CSharpMillisToJavaLong(DateTime.Now) * 1000;
            //        var threadSafeBatchProcessor = new ThreadSafeBatchProcessor(
            //            _logger,
            //            _smartReaderTagEventsListBatch,
            //            _smartReaderTagEventsListBatchOnUpdate);

            //        await threadSafeBatchProcessor.ProcessEventBatchAsync(
            //            new TagRead
            //            {
            //                Epc = tagEvent.EpcHex,
            //                AntennaPort = tagEvent.AntennaPort,
            //                AntennaZone = tagEvent.AntennaName,
            //                FirstSeenTimestamp = updatedCurrentEventTimestamp
            //            },
            //            epcObject, // Populate with relevant data
            //            _standaloneConfigDTO);
            //        //try
            //        //{
            //        //    var updatedCurrentEventTimestamp = Utils.CSharpMillisToJavaLong(DateTime.Now) * 1000;

            //        //    // Retrieve the JObject for the specific EPC
            //        //    var epcObject = _smartReaderTagEventsListBatch[tagEvent.EpcHex];

            //        //    // Safely update the property (overwrite if exists or add otherwise)
            //        //    epcObject["firstSeenTimestamp"] = updatedCurrentEventTimestamp;
            //        //}
            //        //catch (KeyNotFoundException ex)
            //        //{
            //        //    _logger.LogDebug(ex, $"EPC {tagEvent.EpcHex} expired from batch list");
            //        //}
            //        //catch (Exception ex)
            //        //{
            //        //    _logger.LogError(ex, $"Error updating timestamp for EPC {tagEvent.EpcHex} in batch list");
            //        //}
            //    }

            //}
            if (!string.IsNullOrEmpty(_standaloneConfigDTO.stopTriggerDuration)
                && string.Equals("1", _standaloneConfigDTO.stopTriggerType,
                            StringComparison.OrdinalIgnoreCase))
            {
                long stopTiggerDuration = 100;
                _ = long.TryParse(_standaloneConfigDTO.stopTriggerDuration, out stopTiggerDuration);
                //if (stopTiggerDuration > 0 && _stopwatchStopTriggerDuration.IsRunning && _stopwatchStopTriggerDuration.ElapsedMilliseconds < stopTiggerDuration)
                //{
                //    _logger.LogInformation("OnRunPeriodicStopTriggerDurationEvent - Inventory already Running. for duration " + stopTiggerDuration + " [" + _stopwatchStopTriggerDuration.ElapsedMilliseconds + "]", SeverityType.Debug);
                //    Console.WriteLine("OnRunPeriodicStopTriggerDurationEvent - Inventory already Running. for duration " + stopTiggerDuration + " [" + _stopwatchStopTriggerDuration.ElapsedMilliseconds + "]");
                //}

                if (stopTiggerDuration > 0
                    && _stopwatchStopTriggerDuration.IsRunning
                    && _stopwatchStopTriggerDuration.ElapsedMilliseconds >= stopTiggerDuration)
                {
                    _logger.LogInformation("Ignoring tag event due to StopTriggerDuration on OnTagInventoryEvent ");
                    return;
                }
            }

            if (!string.Equals("0", _standaloneConfigDTO.softwareFilterReadCountTimeoutEnabled,
                    StringComparison.OrdinalIgnoreCase))
                try
                {
                    const int MaxTimeoutDictionarySize = 1000;

                    long timeoutInSec = 0;
                    var seenCountThreshold = 1;
                    var currentEventTimestamp = Utils.CSharpMillisToJavaLong(DateTime.Now);
                    // Parse configuration values with validation
                    if (!long.TryParse(_standaloneConfigDTO.softwareFilterReadCountTimeoutIntervalInSec, out timeoutInSec))
                    {
                        _logger.LogWarning("Invalid timeout value in configuration");
                    }
                    if (!int.TryParse(_standaloneConfigDTO.softwareFilterReadCountTimeoutSeenCount, out seenCountThreshold))
                    {
                        _logger.LogWarning("Invalid seen count threshold in configuration");
                    }
                    if (!string.Equals("0", _standaloneConfigDTO.softwareFilterReadCountTimeoutIntervalInSec,
                            StringComparison.OrdinalIgnoreCase))
                        _ = long.TryParse(_standaloneConfigDTO.softwareFilterReadCountTimeoutIntervalInSec,
                            out timeoutInSec);

                    if (!string.Equals("0", _standaloneConfigDTO.softwareFilterReadCountTimeoutSeenCount,
                            StringComparison.OrdinalIgnoreCase))
                        _ = int.TryParse(_standaloneConfigDTO.softwareFilterReadCountTimeoutSeenCount,
                            out seenCountThreshold);

                    if (!_softwareFilterReadCountTimeoutDictionary.ContainsKey(tagEvent.EpcHex))
                    {
                        // Add size check
                        if (_softwareFilterReadCountTimeoutDictionary.Count >= MaxTimeoutDictionarySize)
                        {
                            _logger.LogWarning($"_softwareFilterReadCountTimeoutDictionary reached max size {MaxTimeoutDictionarySize}");
                            // Optionally remove oldest entries
                            RemoveOldestEntries(_softwareFilterReadCountTimeoutDictionary, MaxTimeoutDictionarySize * 0.2);
                        }
                        var readCountTimeoutEvent = new ReadCountTimeoutEvent
                        {
                            Epc = tagEvent.EpcHex,
                            EventTimestamp = currentEventTimestamp
                        };
                        if (tagEvent.AntennaPort.HasValue) readCountTimeoutEvent.Antenna = tagEvent.AntennaPort.Value;
                        _ = _softwareFilterReadCountTimeoutDictionary.TryAdd(tagEvent.EpcHex, readCountTimeoutEvent);
                        shouldProcess = true;
                    }
                    else
                    {
                        var dateTimeOffsetCurrentEventTimestamp =
                            DateTimeOffset.FromUnixTimeMilliseconds(currentEventTimestamp);
                        var dateTimeOffsetLastSeenTimestamp =
                            DateTimeOffset.FromUnixTimeMilliseconds(
                                _softwareFilterReadCountTimeoutDictionary[tagEvent.EpcHex].EventTimestamp);

                        _softwareFilterReadCountTimeoutDictionary[tagEvent.EpcHex].Count =
                            _softwareFilterReadCountTimeoutDictionary[tagEvent.EpcHex].Count + 1;

                        if (_softwareFilterReadCountTimeoutDictionary[tagEvent.EpcHex].Count >= seenCountThreshold)
                        {
                            if (timeoutInSec > 0)
                            {
                                var timeDiff =
                                    dateTimeOffsetCurrentEventTimestamp.Subtract(dateTimeOffsetLastSeenTimestamp);
                                if (timeDiff.TotalSeconds < timeoutInSec)
                                {
                                    shouldProcess = true;
                                    _softwareFilterReadCountTimeoutDictionary[tagEvent.EpcHex].EventTimestamp =
                                        currentEventTimestamp;
                                    _softwareFilterReadCountTimeoutDictionary[tagEvent.EpcHex].Count = 1;
                                }
                                else
                                {
                                    shouldProcess = false;
                                    _logger.LogInformation(
                                        "Ignoring tag event due to softwareFilterReadCountTimeout on OnTagInventoryEvent ");
                                    return;
                                }
                            }
                            else
                            {
                                shouldProcess = true;
                                _softwareFilterReadCountTimeoutDictionary[tagEvent.EpcHex].EventTimestamp =
                                    currentEventTimestamp;
                                _softwareFilterReadCountTimeoutDictionary[tagEvent.EpcHex].Count = 1;
                            }
                        }
                        else
                        {
                            shouldProcess = false;
                            _logger.LogInformation(
                                "Ignoring tag event due to softwareFilterReadCountTimeout [seenCountThreshold] on OnTagInventoryEvent ");
                            return;
                        }
                    }
                }
                catch (ArgumentException ex)
                {
                    _logger.LogError(ex, "Invalid argument in software filter configuration");
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error processing software filter for EPC {tagEvent.EpcHex}");
                    return;
                }

            var tagCollections = new ThreadSafeCollections(
            _logger,
            _readEpcs,
            _currentSkus,
            _currentSkuReadEpcs);


            if (!string.Equals("0", _standaloneConfigDTO.softwareFilterEnabled, StringComparison.OrdinalIgnoreCase))
            {
                const int MaxReadEpcsSize = 1000; // Define a reasonable maximum size
                var currentEventTimestamp = Utils.CSharpMillisToJavaLong(DateTime.Now);
                // Dictionary operations with validation
                if (_readEpcs == null)
                {
                    _logger.LogWarning("Read EPCs dictionary not initialized");
                }

                if (!_readEpcs.ContainsKey(tagEvent.EpcHex))
                {
                    // Add size check before adding new entries
                    if (_readEpcs.Count >= MaxReadEpcsSize)
                    {
                        _logger.LogWarning($"_readEpcs collection reached max size {MaxReadEpcsSize}, skipping new entries");
                        return;
                    }
                    if (!tagCollections.TryAddEpc(tagEvent.EpcHex, currentEventTimestamp))
                    {
                        _logger.LogWarning($"Failed to add EPC {tagEvent.EpcHex}");
                        return;
                    }
                    await ProcessGpoBlinkNewTagStatusGpoPortAsync(2);
                    shouldProcess = true;
                }
                else
                {
                    var expiration = long.Parse(_standaloneConfigDTO.softwareFilterWindowSec);
                    var dateTimeOffsetCurrentEventTimestamp =
                        DateTimeOffset.FromUnixTimeMilliseconds(currentEventTimestamp);
                    var dateTimeOffsetLastSeenTimestamp =
                        DateTimeOffset.FromUnixTimeMilliseconds(_readEpcs[tagEvent.EpcHex]);

                    var timeDiff = dateTimeOffsetCurrentEventTimestamp.Subtract(dateTimeOffsetLastSeenTimestamp);
                    if (timeDiff.TotalSeconds > expiration)
                    {
                        if (!tagCollections.TryUpdateEpc(tagEvent.EpcHex, currentEventTimestamp))
                        {
                            _logger.LogWarning($"Failed to update EPC timestamp");
                            return;
                        }
                        await ProcessGpoBlinkNewTagStatusGpoPortAsync(2);
                        shouldProcess = true;
                    }
                    else
                    {
                        shouldProcess = false;
                        if (!tagCollections.TryUpdateEpc(tagEvent.EpcHex, currentEventTimestamp))
                        {
                            _logger.LogWarning($"Failed to update EPC {tagEvent.EpcHex} timestamp");
                        }
                        _logger.LogInformation(message: "Ignoring tag event due to softwareFilterWindowSec on OnTagInventoryEvent ");
                        return;
                    }
                }
            }

            if (!string.Equals("1", _standaloneConfigDTO.groupEventsOnInventoryStatus,
                    StringComparison.OrdinalIgnoreCase))
            {
                if (_lastTagRead == null)
                {
                    _lastTagRead = new TagRead();
                    if (tagEvent == null)
                    {
                        throw new ArgumentNullException(nameof(tagEvent), "Tag event cannot be null");
                    }
                    _lastTagRead.Epc = tagEvent.EpcHex;
                    _lastTagRead.AntennaPort = tagEvent.AntennaPort;

                    AdjustTagReadEventTimestamp(tagEvent, _lastTagRead);

                    shouldProcess = true;
                }
                else
                {
                    if (_lastTagRead.Epc == tagEvent.EpcHex && _lastTagRead.AntennaPort == tagEvent.AntennaPort)
                    {
                        AdjustTagReadEventTimestamp(tagEvent, _lastTagRead);
                        shouldProcess = true;
                    }
                    else
                    {
                        shouldProcess = true;
                    }

                    _lastTagRead.Epc = tagEvent.EpcHex;
                    _lastTagRead.AntennaPort = tagEvent.AntennaPort;
                    AdjustTagReadEventTimestamp(tagEvent, _lastTagRead);
                }
            }

            if (string.Equals("0", _standaloneConfigDTO.softwareFilterEnabled, StringComparison.OrdinalIgnoreCase)
                && string.Equals("0", _standaloneConfigDTO.softwareFilterReadCountTimeoutEnabled,
                    StringComparison.OrdinalIgnoreCase)
                && !shouldProcess)
                shouldProcess = true;



            //_messageQueueTagInventoryEvent.Enqueue(tagEvent);
            try
            {
                await ProcessTagInventoryEventAsync(tagEvent);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to process tag inventory event for EPC {tagEvent.EpcHex}");
                await ProcessGpoErrorPortAsync();
            }
        }
        catch (ArgumentException ex)
        {
            _logger.LogError(ex, "Invalid argument while processing last tag read");
            return;
        }
        catch (OverflowException ex)
        {
            _logger.LogError(ex, "Timestamp conversion error");
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Critical error processing tag event for EPC: {tagEvent?.EpcHex ?? "unknown"}");
            await ProcessGpoErrorPortAsync();
        }
    }

    private void AdjustTagReadEventTimestamp(TagInventoryEvent tagInventoryEvent, TagRead tagEvent)
    {
        if ("Release".Equals(_buildConfiguration))
        {
            if (tagInventoryEvent.LastSeenTime.HasValue)
                tagEvent.FirstSeenTimestamp = tagInventoryEvent.LastSeenTime.Value.ToFileTimeUtc();
        }
        else
        {
            var currentEventTimestamp = Utils.CSharpMillisToJavaLong(DateTime.Now);
            tagEvent.FirstSeenTimestamp = currentEventTimestamp;
        }
    }

    private async void OnRunPeriodicTagPublisherRetryTasksEvent(object sender, ElapsedEventArgs e)
    {
        if (Monitor.TryEnter(_timerTagPublisherHttpLock))
            try
            {
                JObject smartReaderTagReadEvent;
                while (_messageQueueTagSmartReaderTagEventHttpPostRetry.TryDequeue(out smartReaderTagReadEvent))
                    try
                    {
                        await ProcessHttpJsonPostTagEventDataAsync(smartReaderTagReadEvent);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex,
                            "Unexpected error on OnRunPeriodicTagPublisherRetryTasksEvent " + ex.Message);
                        //Console.WriteLine("Unexpected error on OnRunPeriodicTagPublisherRetryTasksEvent " + ex.Message);
                    }
            }
            catch (Exception)
            {
            }
            finally
            {
                Monitor.Exit(_timerTagPublisherHttpLock);
            }
    }

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
    private async void ProcessValidationTagQueue()
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
    {
        try
        {
            var dataListToPublish = _messageQueueTagSmartReaderTagEventGroupToValidate.ToArray().ToList();
            _messageQueueTagSmartReaderTagEventGroupToValidate.Clear();



            _logger.LogInformation("ProcessBarcodeQueue -  =============================================");
            _logger.LogInformation("ProcessBarcodeQueue - Processing barcode...");
            var eventTimestamp = Utils.CSharpMillisToJavaLongMicroseconds(DateTime.Now);
            var barcode = "";
            barcode = ProcessBarcodeQueue();

            JProperty? newPropertyBarcodeData = null;

            //if (!string.IsNullOrEmpty(barcode))
            //{
            //    LastValidBarcode = barcode;
            //}

            //if (!string.IsNullOrEmpty(LastValidBarcode) && string.IsNullOrEmpty(barcode))
            //{
            //    barcode = LastValidBarcode;
            //}

            if (!string.IsNullOrEmpty(barcode))
                // LastValidBarcode = barcode;
                newPropertyBarcodeData = new JProperty("barcode", barcode);
            _logger.LogInformation("ProcessBarcodeQueue - using barcode [" + barcode + "]");
            //dataToPublish.Add(newPropertyData);
            _logger.LogInformation(
                "ProcessValidationTagQueue - dataListToPublish events size [" +
                dataListToPublish.Count + "]");
            var skuSummaryList = new Dictionary<string, SkuSummary>();
            if (dataListToPublish.Count == 0)
            {
                _logger.LogInformation(
                    "ProcessBarcodeQueue - NO DATA FOUND TO PROCESS, processing empty event to barcode [" + barcode +
                    "]");
                var skuSummary = new SkuSummary
                {
                    Sku = "00000000000000",
                    Qty = 0
                };
                if (!string.IsNullOrEmpty(barcode))
                    skuSummary.Barcode = barcode;
                else
                    skuSummary.Barcode = "";
                skuSummary.EventTimestamp = eventTimestamp;
                skuSummaryList.Add(skuSummary.Sku, skuSummary);
                _logger.LogInformation("skuSummaryList.count [" + skuSummaryList.Count + "] barcode [" + barcode +
                                       "] on empty event.");
#pragma warning disable CS8602 // Dereference of a possibly null reference.
                if (!string.IsNullOrEmpty(_standaloneConfigDTO.enableSummaryStream)
                    && string.Equals("1", _standaloneConfigDTO.enableSummaryStream, StringComparison.OrdinalIgnoreCase))
                {
                    var skuArray = JArray.FromObject(skuSummaryList.Values);
                    AddJsonSkuSummaryToQueue(skuArray);
                }
#pragma warning restore CS8602 // Dereference of a possibly null reference.

                return;
            }


            //_readEpcs.Clear();
            _logger.LogInformation("ProcessValidationTagQueue - Processing data [" + dataListToPublish.Count +
                                   "] barcode [" + barcode + "]");
            _logger.LogInformation("ProcessValidationTagQueue -  =============================================");
            //JObject smartReaderTagReadEvent;
            JObject smartReaderTagReadEventAggregated = null;
            var smartReaderTagReadEventsArray = new JArray();


            var defaultTagEventEpcs = new List<string>();
            foreach (var smartReaderTagReadEvent in dataListToPublish)
                try
                {
                    if (smartReaderTagReadEvent == null)
                        continue;

                    var defaultTagEvent = smartReaderTagReadEvent.Property("tag_reads").ToList().FirstOrDefault()
                        .FirstOrDefault();
                    if (smartReaderTagReadEventAggregated == null)
                    {
                        smartReaderTagReadEventAggregated = smartReaderTagReadEvent;

                        if (defaultTagEvent != null)
                        {
                            if (newPropertyBarcodeData != null)
                            {
                                if (((JObject)defaultTagEvent).ContainsKey("barcode"))
                                {
                                    ((JObject)defaultTagEvent)["barcode"] = barcode;
                                }
                                else
                                {
                                    if (newPropertyBarcodeData != null)
                                        ((JObject)defaultTagEvent).Add(newPropertyBarcodeData);
                                }
                                //smartReaderTagReadEventAggregated.Add(defaultTagEvent);
                            }

                            smartReaderTagReadEventsArray.Add(defaultTagEvent);
                        }
                    }
                    else
                    {
                        try
                        {
                            if (defaultTagEvent != null)
                            {
                                if (newPropertyBarcodeData != null)
                                {
                                    if (((JObject)defaultTagEvent).ContainsKey("barcode"))
                                    {
                                        ((JObject)defaultTagEvent)["barcode"] = barcode;
                                    }
                                    else
                                    {
                                        if (newPropertyBarcodeData != null)
                                            ((JObject)defaultTagEvent).Add(newPropertyBarcodeData);
                                    }
                                    //smartReaderTagReadEventAggregated.Add(newPropertyBarcodeData);
                                }

                                smartReaderTagReadEventsArray.Add(defaultTagEvent);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Unexpected error on ProcessValidationTagQueue " + ex.Message);
                            //Console.WriteLine("Unexpected error on ProcessValidationTagQueue " + ex.Message);
                        }
                    }

                    try
                    {
                        if (defaultTagEvent != null && ((JObject)defaultTagEvent).ContainsKey("tagDataKey"))
                        {
                            var epcValue = "";
                            try
                            {
                                if (((JObject)defaultTagEvent).ContainsKey("epc"))
                                {
                                    epcValue = ((JObject)defaultTagEvent)["epc"].Value<string>();
                                    if (!string.IsNullOrEmpty(epcValue))
                                    {
                                        if (!defaultTagEventEpcs.Contains(epcValue))
                                            defaultTagEventEpcs.Add(epcValue);
                                        else
                                            continue;
                                    }
                                }
                            }
                            catch (Exception)
                            {
                            }


                            var tagDataKeyValue = ((JObject)defaultTagEvent)["tagDataKey"].Value<string>();
                            if (!string.IsNullOrEmpty(tagDataKeyValue))
                            {
                                if (skuSummaryList.ContainsKey(tagDataKeyValue))
                                {
                                    skuSummaryList[tagDataKeyValue].Qty = skuSummaryList[tagDataKeyValue].Qty + 1;
                                    if (skuSummaryList[tagDataKeyValue].Epcs == null)
                                        skuSummaryList[tagDataKeyValue].Epcs = [];
                                    if (!string.IsNullOrEmpty(epcValue) &&
                                        !skuSummaryList[tagDataKeyValue].Epcs.Contains(epcValue))
                                        skuSummaryList[tagDataKeyValue].Epcs.Add(epcValue);
                                }
                                else
                                {
                                    var skuSummary = new SkuSummary
                                    {
                                        Sku = tagDataKeyValue,
                                        Qty = 1
                                    };
                                    if (!string.IsNullOrEmpty(barcode))
                                        skuSummary.Barcode = barcode;
                                    else
                                        skuSummary.Barcode = "";
                                    if (skuSummary.Epcs == null) skuSummary.Epcs = [];
                                    if (!string.IsNullOrEmpty(epcValue) && !skuSummary.Epcs.Contains(epcValue))
                                        skuSummary.Epcs.Add(epcValue);
                                    skuSummary.EventTimestamp = eventTimestamp;
                                    if (smartReaderTagReadEventAggregated.ContainsKey("site"))
                                    {
                                        var site = smartReaderTagReadEventAggregated["site"].Value<string>();
                                        if (skuSummary.AdditionalData == null)
                                            skuSummary.AdditionalData = [];
                                        if (!skuSummary.AdditionalData.ContainsKey("site"))
                                            skuSummary.AdditionalData.Add("site", site);
                                    }

                                    if (smartReaderTagReadEventAggregated.ContainsKey("status"))
                                    {
                                        var customField = smartReaderTagReadEventAggregated["status"].Value<string>();
                                        if (!string.IsNullOrEmpty(customField))
                                        {
                                            if (skuSummary.AdditionalData == null)
                                                skuSummary.AdditionalData = [];
                                            if (!skuSummary.AdditionalData.ContainsKey("status"))
                                                skuSummary.AdditionalData.Add("status", customField);
                                        }
                                    }

                                    if (smartReaderTagReadEventAggregated.ContainsKey("bizStep"))
                                    {
                                        var customField = smartReaderTagReadEventAggregated["bizStep"].Value<string>();
                                        if (!string.IsNullOrEmpty(customField))
                                        {
                                            if (skuSummary.AdditionalData == null)
                                                skuSummary.AdditionalData = [];
                                            if (!skuSummary.AdditionalData.ContainsKey("bizStep"))
                                                skuSummary.AdditionalData.Add("bizStep", customField);
                                        }
                                    }

                                    if (smartReaderTagReadEventAggregated.ContainsKey("bizLocation"))
                                    {
                                        var customField = smartReaderTagReadEventAggregated["bizLocation"]
                                            .Value<string>();
                                        if (!string.IsNullOrEmpty(customField))
                                        {
                                            if (skuSummary.AdditionalData == null)
                                                skuSummary.AdditionalData = [];
                                            if (!skuSummary.AdditionalData.ContainsKey("bizLocation"))
                                                skuSummary.AdditionalData.Add("bizLocation", customField);
                                        }
                                    }

                                    if (smartReaderTagReadEventAggregated.ContainsKey("contentFormat"))
                                    {
                                        var customField = smartReaderTagReadEventAggregated["contentFormat"]
                                            .Value<string>();
                                        if (!string.IsNullOrEmpty(customField))
                                        {
                                            smartReaderTagReadEventAggregated["contentFormat"] = customField;
                                            if (skuSummary.AdditionalData == null)
                                                skuSummary.AdditionalData = [];
                                            if (!skuSummary.AdditionalData.ContainsKey("contentFormat"))
                                                skuSummary.AdditionalData.Add("contentFormat", customField);
                                        }
                                    }

                                    skuSummaryList.Add(tagDataKeyValue, skuSummary);
                                }
                            }
                        }
                    }
                    catch (Exception)
                    {
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error on ProcessValidationTagQueue " + ex.Message);
                    //Console.WriteLine("Unexpected error on ProcessValidationTagQueue " + ex.Message);
                }

            try
            {
                if (smartReaderTagReadEventAggregated != null && smartReaderTagReadEventsArray != null &&
                    smartReaderTagReadEventsArray.Count > 0)
                {
                    if (smartReaderTagReadEventAggregated.ContainsKey("barcode"))
                    {
                        _logger.LogInformation("Setting barcode on aggregated event: " + barcode);
                        smartReaderTagReadEventAggregated["barcode"] = barcode;
                    }
                    else
                    {
                        if (newPropertyBarcodeData != null)
                        {
                            _logger.LogInformation("Adding barcode property on aggregated event: " + barcode);
                            smartReaderTagReadEventAggregated.Add(newPropertyBarcodeData);
                        }
                        else
                        {
                            try
                            {
                                _logger.LogInformation("Creating barcode property on aggregated event: " + barcode);
                                newPropertyBarcodeData = new JProperty("barcode", barcode);
                                smartReaderTagReadEventAggregated.Add(newPropertyBarcodeData);
                            }
                            catch (Exception exBc)
                            {
                                _logger.LogError(exBc, "Creating barcode property on aggregated event " + exBc.Message);
                            }
                        }
                    }

                    smartReaderTagReadEventAggregated["tag_reads"] = smartReaderTagReadEventsArray;


                    if (skuSummaryList.Count > 0)
                    {
                        try
                        {
                            var shouldPublish = false;
                            if (_standaloneConfigDTO != null && !string.IsNullOrEmpty(_standaloneConfigDTO
                                                                 .enableBarcodeTcp)
                                                             && string.Equals("1",
                                                                 _standaloneConfigDTO.enableBarcodeTcp,
                                                                 StringComparison.OrdinalIgnoreCase)
                                                             && !string.IsNullOrEmpty(barcode))
                            {
                                _logger.LogInformation("skuSummaryList.count [" + skuSummaryList.Count + "] barcode [" +
                                                       barcode + "] total items [" + defaultTagEventEpcs.Count + "]");
                                if (!string.IsNullOrEmpty(_standaloneConfigDTO.enableSummaryStream)
                                    && string.Equals("1", _standaloneConfigDTO.enableSummaryStream,
                                        StringComparison.OrdinalIgnoreCase))
                                {
                                    var skuArray = JArray.FromObject(skuSummaryList.Values);
                                    AddJsonSkuSummaryToQueue(skuArray);
                                }

                                shouldPublish = true;
                            }

                            if (_standaloneConfigDTO != null && (
                                                                    (!string.IsNullOrEmpty(_standaloneConfigDTO.enableBarcodeSerial)
                                                                    && string.Equals("1", _standaloneConfigDTO.enableBarcodeSerial, StringComparison.OrdinalIgnoreCase))
                                                                || (!string.IsNullOrEmpty(_standaloneConfigDTO.enableBarcodeHid)
                                                                        && string.Equals("1", _standaloneConfigDTO.enableBarcodeHid, StringComparison.OrdinalIgnoreCase))
                                                                )
                                                             && !string.IsNullOrEmpty(barcode))
                            {
                                _logger.LogInformation("skuSummaryList.count [" + skuSummaryList.Count +
                                                       "] total items [" + defaultTagEventEpcs.Count + "]");
                                if (!string.IsNullOrEmpty(_standaloneConfigDTO.enableSummaryStream)
                                    && string.Equals("1", _standaloneConfigDTO.enableSummaryStream,
                                        StringComparison.OrdinalIgnoreCase))
                                {
                                    var skuArray = JArray.FromObject(skuSummaryList.Values);
                                    AddJsonSkuSummaryToQueue(skuArray);
                                }

                                shouldPublish = true;
                            }
                            else if (_standaloneConfigDTO != null && !string.IsNullOrEmpty(_standaloneConfigDTO
                                                                      .enableBarcodeTcp)
                                                                  && string.Equals("0",
                                                                      _standaloneConfigDTO.enableBarcodeTcp,
                                                                      StringComparison.OrdinalIgnoreCase))
                            {
                                _logger.LogInformation("skuSummaryList.count [" + skuSummaryList.Count +
                                                       "] total items [" + defaultTagEventEpcs.Count + "]");
                                if (!string.IsNullOrEmpty(_standaloneConfigDTO.enableSummaryStream)
                                    && string.Equals("1", _standaloneConfigDTO.enableSummaryStream,
                                        StringComparison.OrdinalIgnoreCase))
                                {
                                    var skuArray = JArray.FromObject(skuSummaryList.Values);
                                    AddJsonSkuSummaryToQueue(skuArray);
                                }

                                shouldPublish = true;
                            }
                            else if (_standaloneConfigDTO != null &&
                                                                    !string.IsNullOrEmpty(_standaloneConfigDTO.enableBarcodeSerial)
                                                                    && string.Equals("0", _standaloneConfigDTO.enableBarcodeSerial, StringComparison.OrdinalIgnoreCase)
                                                                && !string.IsNullOrEmpty(_standaloneConfigDTO.enableBarcodeHid)
                                                                        && string.Equals("0", _standaloneConfigDTO.enableBarcodeHid, StringComparison.OrdinalIgnoreCase)

                                                             && !string.IsNullOrEmpty(barcode))
                            {
                                _logger.LogInformation("skuSummaryList.count [" + skuSummaryList.Count +
                                                       "] total items [" + defaultTagEventEpcs.Count + "]");
                                if (!string.IsNullOrEmpty(_standaloneConfigDTO.enableSummaryStream)
                                    && string.Equals("1", _standaloneConfigDTO.enableSummaryStream,
                                        StringComparison.OrdinalIgnoreCase))
                                {
                                    var skuArray = JArray.FromObject(skuSummaryList.Values);
                                    AddJsonSkuSummaryToQueue(skuArray);
                                }

                                shouldPublish = true;
                            }

                            try
                            {
                                bool isValidated = false;



#pragma warning disable CS8602 // Dereference of a possibly null reference.
                                if (string.Equals("1", _standaloneConfigDTO.enableValidation, StringComparison.OrdinalIgnoreCase))
                                {
                                    if (string.Equals("1", _standaloneConfigDTO.requireUniqueProductCode,
                                            StringComparison.OrdinalIgnoreCase))
                                    {
                                        if (skuSummaryList.Keys.Count > 1)
                                            isValidated = false;
                                        else if (skuSummaryList.Keys.Count == 0 || skuSummaryList.Keys.Count == 1)
                                            isValidated = true;
                                    }


                                }
#pragma warning restore CS8602 // Dereference of a possibly null reference.

                                if (string.Equals("1", _standaloneConfigDTO.enableExternalApiVerification,
                                            StringComparison.OrdinalIgnoreCase)
                                    && _expectedItems.Count > 0
                                    && string.Equals("1", _standaloneConfigDTO.enableValidation, StringComparison.OrdinalIgnoreCase))
                                {
                                    var skusToValidate = new ConcurrentDictionary<string, int>();

                                    foreach (KeyValuePair<string, SkuSummary> skuEntry in skuSummaryList)
                                    {
                                        try
                                        {
                                            if (skuEntry.Value != null && skuEntry.Value.Qty != null)
                                                _ = skusToValidate.TryAdd(skuEntry.Key, unchecked((int)skuEntry.Value.Qty.Value));
                                        }
                                        catch (Exception ex)
                                        {

                                            _logger.LogError(ex, "ValidateCurrentProductContent error (skusToValidate). ");
                                        }

                                    }

                                    if (_plugins != null
                                                        && _plugins.Count > 0
                                                        && !string.IsNullOrEmpty(_standaloneConfigDTO.activePlugin)
                                                        && _plugins.ContainsKey(_standaloneConfigDTO.activePlugin)
                                                        && _plugins[_standaloneConfigDTO.activePlugin].IsProcessingExternalValidation())
                                    {
                                        isValidated = _plugins[_standaloneConfigDTO.activePlugin].ValidateCurrentProductContent(_expectedItems, skusToValidate);
                                    }
                                    else
                                    {
                                        try
                                        {
                                            if (_expectedItems.Any())
                                            {
                                                _logger.LogInformation($"ValidateCurrentProductContent - Starting validation: {_expectedItems.Count} expected SKUs, {skusToValidate.Count} SKUs read. ");

                                                foreach (KeyValuePair<string, int> entry in _expectedItems)
                                                {

                                                    try
                                                    {
                                                        _logger.LogInformation($"ValidateCurrentProductContent - SKU:{entry.Key}");
                                                        if (skusToValidate.ContainsKey(entry.Key))
                                                        {
                                                            _logger.LogInformation($"ValidateCurrentProductContent - SKU:{entry.Key}, {entry.Value} , {skusToValidate[entry.Key]}");
                                                            if (entry.Value != skusToValidate[entry.Key])
                                                            {
                                                                _logger.LogError($"ValidateCurrentProductContent - SKU: {entry.Key}, qty error {entry.Value} vs {skusToValidate[entry.Key]}.");
                                                                isValidated = false;
                                                                break;
                                                            }
                                                        }
                                                        else
                                                        {
                                                            _logger.LogError($"ValidateCurrentProductContent - error: {entry.Key} not found on read items.");
                                                            isValidated = false;
                                                            break;
                                                        }
                                                    }
                                                    catch (Exception ex)
                                                    {
                                                        _logger.LogError(ex, "ValidateCurrentProductContent error. ");
                                                        isValidated = false;
                                                    }


                                                }
                                                if (_expectedItems.Count != skusToValidate.Count)
                                                {
                                                    _logger.LogError($"ValidateCurrentProductContent - Expected: {_expectedItems.Count}  Found: {skusToValidate.Count}.");
                                                    isValidated = false;
                                                }

                                                if (_expectedItems.Keys.Except(skusToValidate.Keys).Any())
                                                {
                                                    _logger.LogError($"ValidateCurrentProductContent - SKU divergency.");
                                                    isValidated = false;
                                                }

                                                if (skusToValidate.Keys.Except(_expectedItems.Keys).Any())
                                                {
                                                    _logger.LogError($"ValidateCurrentProductContent - SKU divergency..");
                                                    isValidated = false;
                                                }

                                            }
                                            else
                                            {
                                                _logger.LogError($"ValidateCurrentProductContent - Expected content was not found.");
                                                isValidated = false;
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            _logger.LogError(ex, "ValidateCurrentProductContent error. ");
                                            isValidated = false;

                                        }
                                    }


                                    //int detected = _currentSkus.Values.Sum();
                                    //int exptected = _expectedItems.Values.Sum();

                                    //if (detected != exptected)
                                    //{
                                    //    isValidated = false;
                                    //}
                                    //else
                                    //{
                                    //    isValidated = true;
                                    //}
                                }




                                if (string.Equals("1", _standaloneConfigDTO.enableValidation, StringComparison.OrdinalIgnoreCase))
                                {
                                    if (isValidated)
                                    {
                                        _ = SetGpoPortValidationAsync(true);
                                    }
                                    else
                                    {
                                        _ = SetGpoPortValidationAsync(false);
                                    }
                                }

                            }
                            catch (Exception)
                            {
                            }

#pragma warning disable CS8602 // Dereference of a possibly null reference.
                            if (string.Equals("1", _standaloneConfigDTO.enableExternalApiVerification)
                                && string.Equals("1", _standaloneConfigDTO.enableValidation))
                            {

                                try
                                {
                                    //_logger.LogInformation("Processing data: " + epc, SeverityType.Debug);
                                    if (_plugins != null
                                        && _plugins.Count > 0
                                        && !string.IsNullOrEmpty(_standaloneConfigDTO.activePlugin)
                                        && _plugins.ContainsKey(_standaloneConfigDTO.activePlugin)
                                        && _plugins[_standaloneConfigDTO.activePlugin].IsProcessingExternalValidation())
                                    {
                                        _logger.LogInformation("Publishing data to external API...");
                                        string[] epcArray = Array.Empty<string>();
                                        _ = _plugins[_standaloneConfigDTO.activePlugin].ExternalApiPublish(barcode, skuSummaryList, epcArray);
                                        //return;
                                    }
                                }
                                catch (Exception)
                                {

                                }

                            }
#pragma warning restore CS8602 // Dereference of a possibly null reference.

                            if (shouldPublish) EnqueueToExternalPublishers(smartReaderTagReadEventAggregated, false);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Unexpected error on ProcessBarcodeQueue " + ex.Message);
                            //Console.WriteLine("Unexpected error on ProcessBarcodeQueue " + ex.Message);
                        }

                        skuSummaryList.Clear();
                    }
                    //SaveJsonTagEventToDb(smartReaderTagReadEventAggregated);
                    //await ProcessHttpJsonPostTagEventDataAsync(smartReaderTagReadEventAggregated);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error on ProcessBarcodeQueue " + ex.Message);
                //Console.WriteLine("Unexpected error on ProcessBarcodeQueue " + ex.Message);
            }
        }
        catch (Exception)
        {
        }

        try
        {
            _logger.LogInformation(
                "ProcessValidationTagQueue - Final Check: _messageQueueTagSmartReaderTagEventGroupToValidate events size [" +
                _messageQueueTagSmartReaderTagEventGroupToValidate.Count + "]");
            _currentSkus.Clear();
            _currentSkuReadEpcs.Clear();
            _messageQueueTagSmartReaderTagEventGroupToValidate.Clear();
        }
        catch (Exception)
        {


        }

    }

    //    private async void OnRunPeriodicTagFilterListsEvent(object sender, ElapsedEventArgs e)
    //    {
    //        const int WarningThreshold = 800; // 80% of max size
    //#pragma warning disable CS8600, CS8602, CS8604 // Dereference of a possibly null reference.
    //        _timerTagFilterLists.Enabled = false;
    //        _timerTagFilterLists.Stop();

    //        try
    //        {
    //            try
    //            {
    //                if (_readEpcs.Count > WarningThreshold)
    //                {
    //                    _logger.LogWarning($"_readEpcs size ({_readEpcs.Count}) approaching limit");
    //                }

    //                if (_softwareFilterReadCountTimeoutDictionary.Count > WarningThreshold)
    //                {
    //                    _logger.LogWarning($"_softwareFilterReadCountTimeoutDictionary size ({_softwareFilterReadCountTimeoutDictionary.Count}) approaching limit");
    //                }

    //                if (_smartReaderTagEventsListBatch.Count > WarningThreshold)
    //                {
    //                    _logger.LogWarning($"_smartReaderTagEventsListBatch size ({_smartReaderTagEventsListBatch.Count}) approaching limit");
    //                }
    //            }
    //            catch (System.Exception)
    //            {

    //            }


    //            if (!string.IsNullOrEmpty(_standaloneConfigDTO.softwareFilterEnabled)
    //                && !"0".Equals(_standaloneConfigDTO.softwareFilterEnabled))
    //            {
    //                try
    //                {
    //                    if (_readEpcs.Count > 1000)
    //                    {
    //                        var count = _readEpcs.Count - 1000;
    //                        if (count > 0)
    //                        {
    //                            // remove that number of items from the start of the list
    //                            long eventTimestampToRemove = 0;
    //                            foreach (var k in _readEpcs.Keys.Take(count))
    //                                _readEpcs.TryRemove(k, out eventTimestampToRemove);
    //                        }
    //                    }

    //                    var oldTimestamp = DateTime.Now.AddHours(-1);
    //                    var oldTimestampToCheck = Utils.CSharpMillisToJavaLong(oldTimestamp);
    //                    foreach (var kvp in _readEpcs.Where(x => x.Value < oldTimestampToCheck).ToList())
    //                    {
    //                        var val = kvp.Value;
    //                        _readEpcs.TryRemove(kvp.Key, out val);
    //                    }
    //                }
    //                catch (Exception ex)
    //                {
    //                    _logger.LogError(ex, "Unexpected error on remove _readEpcs" + ex.Message);
    //                }

    //                try
    //                {

    //                    var expiration = long.Parse(_standaloneConfigDTO.softwareFilterWindowSec);

    //                    var oldTimestamp = DateTime.Now.AddHours(-1);
    //                    var oldTimestampToCheck = Utils.CSharpMillisToJavaLong(oldTimestamp);
    //                    foreach (var kvp in _readEpcs.ToList())
    //                    {
    //                        var val = kvp.Value;
    //                        var currentEventTimestamp = DateTime.Now;
    //                        var currentTimestampToCheck = Utils.CSharpMillisToJavaLong(currentEventTimestamp);
    //                        var dateTimeOffsetCurrentEventTimestamp = DateTimeOffset.FromUnixTimeMilliseconds(currentTimestampToCheck);
    //                        var dateTimeOffsetLastSeenTimestamp = DateTimeOffset.FromUnixTimeMilliseconds(val);
    //                        var timeDiff = dateTimeOffsetCurrentEventTimestamp.Subtract(dateTimeOffsetLastSeenTimestamp);
    //                        if (timeDiff.TotalSeconds > expiration)
    //                        {
    //                            _readEpcs.TryRemove(kvp.Key, out val);
    //                        }
    //                    }
    //                }
    //                catch (Exception ex)
    //                {
    //                    _logger.LogError(ex, "Unexpected error on remove _readEpcs" + ex.Message);
    //                }
    //            }

    //            if (!string.IsNullOrEmpty(_standaloneConfigDTO.softwareFilterReadCountTimeoutEnabled)
    //                && !"0".Equals(_standaloneConfigDTO.softwareFilterReadCountTimeoutEnabled))
    //                try
    //                {
    //                    if (_softwareFilterReadCountTimeoutDictionary.Count > 1000)
    //                    {
    //                        var count = _softwareFilterReadCountTimeoutDictionary.Count - 1000;
    //                        if (count > 0)
    //                        {
    //                            // remove that number of items from the start of the list
    //                            ReadCountTimeoutEvent? eventTimestampToRemove;
    //                            foreach (var k in _softwareFilterReadCountTimeoutDictionary.Keys.Take(count))
    //                                _softwareFilterReadCountTimeoutDictionary.TryRemove(k, out eventTimestampToRemove);
    //                        }
    //                    }

    //                    var currentTimestamp = DateTime.Now;
    //                    var currentTimestampToCheck = Utils.CSharpMillisToJavaLong(currentTimestamp);
    //                    var dateTimeOffsetCurrentEventTimestamp =
    //                        DateTimeOffset.FromUnixTimeMilliseconds(currentTimestampToCheck);
    //                    foreach (var kvp in _softwareFilterReadCountTimeoutDictionary)
    //                    {
    //                        var dateTimeOffsetLastSeenTimestamp =
    //                            DateTimeOffset.FromUnixTimeMilliseconds(kvp.Value.EventTimestamp);
    //                        var timeDiff = dateTimeOffsetCurrentEventTimestamp.Subtract(dateTimeOffsetLastSeenTimestamp);
    //                        if (timeDiff.TotalHours > 1)
    //                        {
    //                            var val = kvp.Value;
    //                            _softwareFilterReadCountTimeoutDictionary.TryRemove(kvp.Key, out val);
    //                        }
    //                    }
    //                }
    //                catch (Exception ex)
    //                {
    //                    _logger.LogError(ex,
    //                        "Unexpected error on remove _softwareFilterReadCountTimeoutDictionary" + ex.Message);
    //                }

    //            if (string.Equals("1", _standaloneConfigDTO.tagPresenceTimeoutEnabled, StringComparison.OrdinalIgnoreCase))
    //            {
    //                if (_isStarted)
    //                {
    //                    try
    //                    {
    //                        var currentEventTimestamp = Utils.CSharpMillisToJavaLong(DateTime.Now);

    //                        double expirationInSec = 2;
    //                        double expirationInMillis = 2000;
    //                        double.TryParse(_standaloneConfigDTO.tagPresenceTimeoutInSec, NumberStyles.Float, CultureInfo.InvariantCulture, out expirationInSec);
    //                        expirationInMillis = expirationInSec * 1000;

    //                        var dateTimeOffsetCurrentEventTimestamp = DateTimeOffset.FromUnixTimeMilliseconds(currentEventTimestamp);
    //                        //var dateTimeOffsetLastSeenTimestamp = DateTimeOffset.FromUnixTimeMilliseconds(_readEpcs[tagEvent.EpcHex]);

    //                        var secondsToAdd = expirationInSec * -1;
    //                        var expiredEventTimestamp = Utils.CSharpMillisToJavaLong(DateTime.Now.AddSeconds(secondsToAdd));

    //                        //var expiredEvents = _smartReaderTagEventsListBatch.Values.Where(t => t["tag_reads"]["firstSeenTimestamp"].Where(q => (long)q["firstSeenTimestamp"] <= expiredEventTimestamp));
    //                        var currentEvents = _smartReaderTagEventsListBatch.Values;
    //                        //var expiredEvents = _smartReaderTagEventsListBatch.Values.Where(t => (long)t["tag_reads"]["firstSeenTimestamp"] <= expiredEventTimestamp);
    //                        //var existingEventOnCurrentAntenna = (JObject)(retrievedValue["tag_reads"].FirstOrDefault(q => (long)q["antennaPort"] == tagRead.AntennaPort));

    //                        foreach (JObject currentEvent in currentEvents)
    //                        {

    //                            foreach (JObject currentEventItem in currentEvent["tag_reads"])
    //                            {
    //                                long firstSeenTimestamp = currentEventItem["firstSeenTimestamp"].Value<long>() / 1000;
    //                                string expiredEpc = currentEventItem["epc"].Value<string>();
    //                                var dateTimeOffsetLastSeenTimestamp = DateTimeOffset.FromUnixTimeMilliseconds(firstSeenTimestamp);

    //                                var timeDiff = dateTimeOffsetCurrentEventTimestamp.Subtract(dateTimeOffsetLastSeenTimestamp);
    //                                if (timeDiff.TotalSeconds > expirationInSec)
    //                                {

    //                                    JObject eventToRemove = null;
    //                                    if (_smartReaderTagEventsListBatch.TryGetValue(expiredEpc, out eventToRemove))
    //                                    {
    //                                        if (eventToRemove != null)
    //                                        {
    //                                            if (_smartReaderTagEventsListBatch.ContainsKey(expiredEpc))
    //                                            {

    //                                                if (_smartReaderTagEventsListBatch.TryRemove(expiredEpc, out eventToRemove))
    //                                                {
    //                                                    _logger.LogInformation($"Expired EPC detected {timeDiff.TotalSeconds}: {expiredEpc} - firstSeenTimestamp: {dateTimeOffsetLastSeenTimestamp.ToString("o")}, current timestamp: {dateTimeOffsetCurrentEventTimestamp.ToString("o")} current timeout set {expirationInSec}");
    //                                                }
    //                                            }

    //                                            if (!_smartReaderTagEventsAbsence.ContainsKey(expiredEpc))
    //                                            {
    //                                                _logger.LogInformation($"On-Change event requested for expired EPC: {expiredEpc}");
    //                                                _smartReaderTagEventsAbsence.TryAdd(expiredEpc, eventToRemove);

    //                                            }

    //                                        }
    //                                    }
    //                                }
    //                            }

    //                        }

    //                        if (string.Equals("1", _standaloneConfigDTO.tagPresenceTimeoutEnabled,
    //                                StringComparison.OrdinalIgnoreCase) && _smartReaderTagEventsAbsence.Any())
    //                        {
    //                            JObject smartReaderTagReadEvent;
    //                            JObject? smartReaderTagReadEventAggregated = null;
    //                            var smartReaderTagReadEventsArray = new JArray();

    //                            foreach (var tagEpcToRemove in _smartReaderTagEventsAbsence.Keys)
    //                            {
    //                                JObject? expiredTagEvent = null;

    //                                if (_smartReaderTagEventsListBatch.ContainsKey(tagEpcToRemove))
    //                                {
    //                                    _smartReaderTagEventsListBatch.TryRemove(tagEpcToRemove, out expiredTagEvent);
    //                                }

    //                            }

    //                            _smartReaderTagEventsAbsence.Clear();

    //                            if (_smartReaderTagEventsListBatch.Count > 0)
    //                            {
    //                                foreach (var smartReaderTagReadEventBatch in _smartReaderTagEventsListBatch.Values)
    //                                {
    //                                    smartReaderTagReadEvent = smartReaderTagReadEventBatch;
    //                                    try
    //                                    {
    //                                        if (smartReaderTagReadEvent == null)
    //                                            continue;
    //                                        if (smartReaderTagReadEventAggregated == null)
    //                                        {
    //                                            smartReaderTagReadEventAggregated = smartReaderTagReadEvent;
    //                                            smartReaderTagReadEventsArray.Add(smartReaderTagReadEvent.Property("tag_reads").ToList()
    //                                                .FirstOrDefault().FirstOrDefault());
    //                                        }
    //                                        else
    //                                        {
    //                                            try
    //                                            {
    //                                                smartReaderTagReadEventsArray.Add(smartReaderTagReadEvent.Property("tag_reads").ToList()
    //                                                    .FirstOrDefault().FirstOrDefault());
    //                                            }
    //                                            catch (Exception ex)
    //                                            {
    //                                                _logger.LogInformation(ex,
    //                                                    "Unexpected error while filtering tags " + ex.Message);
    //                                            }
    //                                        }
    //                                    }
    //                                    catch (Exception ex)
    //                                    {
    //                                        _logger.LogError(ex, "Unexpected error on while filtering tags " + ex.Message);
    //                                    }
    //                                }
    //                                try
    //                                {
    //                                    if (smartReaderTagReadEventAggregated != null && smartReaderTagReadEventsArray != null &&
    //                                        smartReaderTagReadEventsArray.Count > 0)
    //                                    {
    //                                        smartReaderTagReadEventAggregated["tag_reads"] = smartReaderTagReadEventsArray;

    //                                        await ProcessMqttJsonTagEventDataAsync(smartReaderTagReadEventAggregated);
    //                                    }
    //                                }
    //                                catch (Exception ex)
    //                                {
    //                                    _logger.LogError(ex, "Unexpected error on while filtering tags " + ex.Message);
    //                                }
    //                            }
    //                            else
    //                            {
    //                                // generate empty event...


    //                                var emptyTagData = new SmartReaderTagReadEvent();
    //                                emptyTagData.ReaderName = _standaloneConfigDTO.readerName;
    //#pragma warning disable CS8602 // Dereference of a possibly null reference.
    //                                emptyTagData.Mac = _iotDeviceInterfaceClient.MacAddress;
    //#pragma warning restore CS8602 // Dereference of a possibly null reference.
    //                                emptyTagData.TagReads = new List<TagRead>();
    //                                JObject emptyTagDataObject = JObject.FromObject(emptyTagData);
    //                                smartReaderTagReadEventsArray.Add(emptyTagDataObject);

    //                                try
    //                                {

    //                                    await ProcessMqttJsonTagEventDataAsync(emptyTagDataObject);
    //                                }
    //                                catch (Exception ex)
    //                                {
    //                                    _logger.LogError(ex, "Unexpected error on while filtering tags " + ex.Message);
    //                                }
    //                            }


    //                        }


    //                    }
    //                    catch (Exception)
    //                    {


    //                    }
    //                }
    //                else
    //                {
    //                    _smartReaderTagEventsAbsence.Clear();
    //                    _smartReaderTagEventsListBatch.Clear();
    //                    _smartReaderTagEventsAbsence.Clear();
    //                }

    //            }
    //        }
    //        catch (Exception)
    //        {
    //        }


    //        _timerTagFilterLists.Enabled = true;
    //        _timerTagFilterLists.Start();
    //#pragma warning restore CS8600, CS8602, CS8604 // Dereference of a possibly null reference.
    //    }

    private async void OnRunPeriodicTagFilterListsEvent(object sender, ElapsedEventArgs e)
    {
        _timerTagFilterLists.Enabled = false;
        _timerTagFilterLists.Stop();

        try
        {
            // Use the same batch lock as the processor
            using (await _batchLock.WaitAsyncWithTimeout(TimeSpan.FromSeconds(5)))
            {
                // Monitor collection sizes
                MonitorCollectionSizes();

                // Process software filters
                await ProcessSoftwareFiltersAsync();

                //// Process tag presence timeout
                //if (_isStarted && _batchProcessor.IsTagPresenceTimeoutEnabled(_standaloneConfigDTO))
                //{
                //    await ProcessTagPresenceTimeoutAsync();
                //}
            }
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("Timed out waiting for batch lock in tag filter processing");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in periodic tag filter processing");
        }
        finally
        {
            _timerTagFilterLists.Enabled = true;
            _timerTagFilterLists.Start();
        }
    }

    private void MonitorCollectionSizes()
    {
        const int WarningThreshold = 800;

        if (_readEpcs.Count > WarningThreshold)
        {
            _logger.LogWarning($"_readEpcs size ({_readEpcs.Count}) approaching limit");
        }

        if (_softwareFilterReadCountTimeoutDictionary.Count > WarningThreshold)
        {
            _logger.LogWarning($"_softwareFilterReadCountTimeoutDictionary size ({_softwareFilterReadCountTimeoutDictionary.Count}) approaching limit");
        }

        if (_smartReaderTagEventsListBatch.Count > WarningThreshold)
        {
            _logger.LogWarning($"_smartReaderTagEventsListBatch size ({_smartReaderTagEventsListBatch.Count}) approaching limit");
        }
    }


    private bool IsSoftwareFilterEnabled()
    {
        return (_standaloneConfigDTO != null &&
               !string.IsNullOrEmpty(_standaloneConfigDTO.softwareFilterEnabled) &&
                string.Equals("1", _standaloneConfigDTO.softwareFilterEnabled, StringComparison.OrdinalIgnoreCase)) ||
               (!string.IsNullOrEmpty(_standaloneConfigDTO.softwareFilterReadCountTimeoutEnabled) &&
                string.Equals("1", _standaloneConfigDTO.softwareFilterReadCountTimeoutEnabled, StringComparison.OrdinalIgnoreCase));
    }

    private async Task ProcessSoftwareFiltersAsync()
    {
        if (IsSoftwareFilterEnabled())
        {
            CleanupExpiredReadEpcs();
            await CleanupTimeoutDictionaryAsync();
        }
    }

    private void CleanupExpiredReadEpcs()
    {
        try
        {
            // Check if _readEpcs exceeds size limit
            if (_readEpcs.Count > 1000)
            {
                var count = _readEpcs.Count - 1000;
                if (count > 0)
                {
                    // Remove oldest items first
                    var oldestKeys = _readEpcs.OrderBy(kvp => kvp.Value)
                                            .Take(count)
                                            .Select(kvp => kvp.Key)
                                            .ToList();

                    foreach (var key in oldestKeys)
                    {
                        _ = _readEpcs.TryRemove(key, out _);
                    }
                    _logger.LogInformation($"Removed {count} oldest entries from _readEpcs to maintain size limit");
                }
            }

            // Remove expired entries based on timeout window
            if (!string.IsNullOrEmpty(_standaloneConfigDTO?.softwareFilterWindowSec))
            {
                var expiration = long.Parse(_standaloneConfigDTO.softwareFilterWindowSec);
                var currentTimestamp = Utils.CSharpMillisToJavaLong(DateTime.Now);
                var dateTimeOffsetCurrentEventTimestamp = DateTimeOffset.FromUnixTimeMilliseconds(currentTimestamp);

                var expiredKeys = _readEpcs.Where(kvp =>
                {
                    var dateTimeOffsetLastSeenTimestamp = DateTimeOffset.FromUnixTimeMilliseconds(kvp.Value);
                    var timeDiff = dateTimeOffsetCurrentEventTimestamp.Subtract(dateTimeOffsetLastSeenTimestamp);
                    return timeDiff.TotalSeconds > expiration;
                })
                .Select(kvp => kvp.Key)
                .ToList();

                foreach (var key in expiredKeys)
                {
                    _ = _readEpcs.TryRemove(key, out _);
                }

                if (expiredKeys.Any())
                {
                    _logger.LogInformation($"Removed {expiredKeys.Count} expired entries from _readEpcs");
                }
            }

            // Additional cleanup for entries older than 1 hour
            var oneHourAgo = Utils.CSharpMillisToJavaLong(DateTime.Now.AddHours(-1));
            var oldKeys = _readEpcs.Where(kvp => kvp.Value < oneHourAgo)
                                  .Select(kvp => kvp.Key)
                                  .ToList();

            foreach (var key in oldKeys)
            {
                _ = _readEpcs.TryRemove(key, out _);
            }

            if (oldKeys.Any())
            {
                _logger.LogInformation($"Removed {oldKeys.Count} entries older than 1 hour from _readEpcs");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cleaning up expired read EPCs");
        }
    }

    private async Task CleanupTimeoutDictionaryAsync()
    {
        try
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            // Iterate through the dictionary and remove expired entries
            foreach (var key in _softwareFilterReadCountTimeoutDictionary.Keys)
            {
                if (_softwareFilterReadCountTimeoutDictionary.TryGetValue(key, out var entry))
                {
                    // Calculate expiration based on EventTimestamp + Timeout
                    var expirationTime = entry.EventTimestamp + entry.Timeout;

                    // Remove the entry if it has expired
                    if (expirationTime < now)
                    {
                        _ = _softwareFilterReadCountTimeoutDictionary.TryRemove(key, out _);
                        _logger.LogDebug($"Removed expired entry for key {key} with Epc {entry.Epc}");
                    }
                }
            }

            _logger.LogInformation("Completed cleanup of expired entries in _softwareFilterReadCountTimeoutDictionary.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred during CleanupTimeoutDictionaryAsync.");
        }

        // Simulate async operation (e.g., database or additional cleanup tasks)
        await Task.CompletedTask;
    }




    //private async Task HandleExpiredTagEventAsync(string epc, JObject removedEvent)
    //{
    //    // Example: Log the expired tag or perform additional cleanup if necessary
    //    _logger.LogDebug($"Handling expired tag event for EPC: {epc}");

    //    // Simulate an async operation if needed (e.g., database updates, event publishing)
    //    await Task.CompletedTask;
    //}

    private async void OnRunPeriodicKeepaliveCheck(object sender, ElapsedEventArgs e)
    {
        _timerKeepalive.Enabled = false;
        _timerKeepalive.Stop();

        try
        {
#pragma warning disable CS8602 // Dereference of a possibly null reference.
            if (_isStarted &&
                string.Equals("1", _standaloneConfigDTO.heartbeatEnabled, StringComparison.OrdinalIgnoreCase))
            {
                if (_stopwatchKeepalive.Elapsed.TotalMilliseconds > double.Parse(_standaloneConfigDTO.heartbeatPeriodSec) * 1000)
                {

                    //_stopwatchKeepalive.Stop();
                    //_stopwatchKeepalive.Reset();
                    _stopwatchKeepalive.Restart();
                    //ProcessKeepalive();
                    await SendHeartbeatAsync();
                }
            }
#pragma warning restore CS8602 // Dereference of a possibly null reference.

        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                    "Unexpected error running keepalive manager on OnRunPeriodicKeepaliveCheck. " + ex.Message);
        }

        _timerKeepalive.Enabled = true;
        _timerKeepalive.Start();
    }

    private async void OnRunPeriodicStopTriggerDurationEvent(object sender, ElapsedEventArgs e)
    {
        _timerStopTriggerDuration.Enabled = false;
        _timerStopTriggerDuration.Stop();

        try
        {
#pragma warning disable CS8602 // Dereference of a possibly null reference.
            if (!string.IsNullOrEmpty(_standaloneConfigDTO.stopTriggerDuration))
            {
                long stopTiggerDuration = 100;
                _ = long.TryParse(_standaloneConfigDTO.stopTriggerDuration, out stopTiggerDuration);
                //if (stopTiggerDuration > 0 && _stopwatchStopTriggerDuration.IsRunning && _stopwatchStopTriggerDuration.ElapsedMilliseconds < stopTiggerDuration)
                //{
                //    _logger.LogInformation("OnRunPeriodicStopTriggerDurationEvent - Inventory already Running. for duration " + stopTiggerDuration + " [" + _stopwatchStopTriggerDuration.ElapsedMilliseconds + "]", SeverityType.Debug);
                //    Console.WriteLine("OnRunPeriodicStopTriggerDurationEvent - Inventory already Running. for duration " + stopTiggerDuration + " [" + _stopwatchStopTriggerDuration.ElapsedMilliseconds + "]");
                //}

                if (stopTiggerDuration > 0
                    && !string.Equals("0", _standaloneConfigDTO.stopTriggerDuration, StringComparison.OrdinalIgnoreCase)
                    && _stopwatchStopTriggerDuration.IsRunning
                    && _stopwatchStopTriggerDuration.ElapsedMilliseconds >= stopTiggerDuration)
                {
                    _stopwatchStopTriggerDuration.Stop();
                    _stopwatchStopTriggerDuration.Reset();
                    //_stopwatchStopTriggerDuration.Start();

                    if (string.Equals("1", _standaloneConfigDTO.groupEventsOnInventoryStatus,
                            StringComparison.OrdinalIgnoreCase))
                        try
                        {
                            //if (_messageQueueTagSmartReaderTagEventGroupToValidate.Count > 0)
                            //{
                            try
                            {
                                //_ = Task.Run(() => ProcessValidationTagQueue());
                                try
                                {
                                    await StopPresetAsync();
                                }
                                catch (Exception)
                                {


                                }

                                //ProcessValidationTagQueue();
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Unexpected error on ProcessBarcodeQueue " + ex.Message);
                                //throw;
                            }
                            //}
                            //else if (string.Equals("1", _standaloneConfigDTO.enableBarcodeTcp, StringComparison.OrdinalIgnoreCase) 
                            //    &&_messageQueueBarcode.Count > 0)
                            //{
                            //    try
                            //    {
                            //        string barcode = "";
                            //        while(_messageQueueBarcode.Count > 0 
                            //            && ("".Equals(barcode.Trim()) || barcode.ToUpper().Contains(_standaloneConfigDTO.barcodeTcpNoDataString.ToUpper())))
                            //        {                                            
                            //            _messageQueueBarcode.TryDequeue(out barcode);
                            //            _logger.LogInformation("removed barcode: " + barcode);
                            //        }
                            //        _logger.LogInformation("Barcode detected: " + barcode);
                            //    }
                            //    catch (Exception)
                            //    {
                            //    }
                            //}
                        }
                        catch (Exception)
                        {
                        }
                    //currentBarcode = "";
                    //_readEpcs.Clear();
                    //_messageQueueTagSmartReaderTagEventBarcodeGroup.Clear();
                }
            }
#pragma warning restore CS8602 // Dereference of a possibly null reference.
        }
        catch (Exception)
        {
        }


        _timerStopTriggerDuration.Enabled = true;
        _timerStopTriggerDuration.Start();
    }

    private async void OnRunPeriodicTagPublisherHttpTasksEvent(object sender, ElapsedEventArgs e)
    {
#pragma warning disable CS8600, CS8602, CS8604 // Dereference of a possibly null reference.
        _timerTagPublisherHttp.Enabled = false;
        _timerTagPublisherHttp.Stop();

        try
        {
            JObject smartReaderTagReadEvent;
            JObject? smartReaderTagReadEventAggregated = null;
            var smartReaderTagReadEventsArray = new JArray();
            var httpPostTimer = Stopwatch.StartNew();
            var httpPostIntervalInSec = 1;

            if (!string.IsNullOrEmpty(_standaloneConfigDTO.httpPostIntervalSec))
            {
                _ = int.TryParse(_standaloneConfigDTO.httpPostIntervalSec, out httpPostIntervalInSec);
                if (httpPostIntervalInSec < 1) httpPostIntervalInSec = 1;
            }


            if (!string.IsNullOrEmpty(_standaloneConfigDTO.httpPostEnabled)
                        && string.Equals("1", _standaloneConfigDTO.httpPostEnabled,
                            StringComparison.OrdinalIgnoreCase))
            {
                if (_mqttPublishingConfiguration.BatchListEnabled)
                {
                    const int MaxBatchSize = 1000;

                    while (httpPostTimer.Elapsed.TotalSeconds < httpPostIntervalInSec)
                    {
                        await Task.Delay(10);
                    }
                    try
                    {
                        if (_smartReaderTagEventsListBatch.Count >= MaxBatchSize)
                        {
                            _logger.LogWarning($"_smartReaderTagEventsListBatch reached max size {MaxBatchSize}");
                            // Remove oldest entries to make room for new ones
                            RemoveOldestBatchEntries(_smartReaderTagEventsListBatch, MaxBatchSize * 0.2);
                        }

                        if (_smartReaderTagEventsListBatch.Count > 0)
                        {
                            foreach (var smartReaderTagReadEventBatch in _smartReaderTagEventsListBatch.Values)
                            {
                                smartReaderTagReadEvent = smartReaderTagReadEventBatch;
                                try
                                {
                                    if (smartReaderTagReadEvent == null)
                                        continue;
                                    if (smartReaderTagReadEventAggregated == null)
                                    {
                                        smartReaderTagReadEventAggregated = smartReaderTagReadEvent;
                                        smartReaderTagReadEventsArray.Add(smartReaderTagReadEvent.Property("tag_reads").ToList()
                                            .FirstOrDefault().FirstOrDefault());
                                    }
                                    else
                                    {
                                        try
                                        {
                                            smartReaderTagReadEventsArray.Add(smartReaderTagReadEvent.Property("tag_reads").ToList()
                                                .FirstOrDefault().FirstOrDefault());
                                        }
                                        catch (Exception ex)
                                        {
                                            _logger.LogInformation(ex,
                                                "Unexpected error on OnRunPeriodicTagPublisherHttpTasksEvent " + ex.Message);
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(ex, "Unexpected error on OnRunPeriodicTagPublisherHttpTasksEvent " + ex.Message);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error managing batch size");
                    }


                }
                else
                {
                    while (httpPostTimer.Elapsed.TotalSeconds < httpPostIntervalInSec)
                        while (_messageQueueTagSmartReaderTagEventHttpPost.TryDequeue(out smartReaderTagReadEvent))
                            try
                            {
                                if (smartReaderTagReadEvent == null)
                                    continue;
                                if (smartReaderTagReadEventAggregated == null)
                                {
                                    smartReaderTagReadEventAggregated = smartReaderTagReadEvent;
                                    smartReaderTagReadEventsArray.Add(smartReaderTagReadEvent.Property("tag_reads").ToList()
                                        .FirstOrDefault().FirstOrDefault());
                                }
                                else
                                {
                                    try
                                    {
                                        smartReaderTagReadEventsArray.Add(smartReaderTagReadEvent.Property("tag_reads").ToList()
                                            .FirstOrDefault().FirstOrDefault());
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger.LogInformation(ex,
                                            "Unexpected error on OnRunPeriodicTagPublisherTasksEvent " + ex.Message);
                                        //Console.WriteLine("Unexpected error on OnRunPeriodicTagPublisherTasksEvent " + ex.Message);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogInformation("Unexpected error on OnRunPeriodicTagPublisherTasksEvent " + ex.Message,
                                    SeverityType.Error);
                                //Console.WriteLine("Unexpected error on OnRunPeriodicTagPublisherTasksEvent " + ex.Message);
                            }
                }
            }




            httpPostTimer.Restart();

            try
            {
                if (smartReaderTagReadEventAggregated != null && smartReaderTagReadEventsArray != null &&
                    smartReaderTagReadEventsArray.Count > 0)
                {
                    smartReaderTagReadEventAggregated["tag_reads"] = smartReaderTagReadEventsArray;

                    await ProcessHttpJsonPostTagEventDataAsync(smartReaderTagReadEventAggregated);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error on OnRunPeriodicTagPublisherTasksEvent " + ex.Message);
                //Console.WriteLine("Unexpected error on OnRunPeriodicTagPublisherTasksEvent " + ex.Message);
            }
        }
        catch (Exception)
        {
        }


        _timerTagPublisherHttp.Enabled = true;
        _timerTagPublisherHttp.Start();
#pragma warning restore CS8600, CS8602, CS8604 // Dereference of a possibly null reference.
    }

    private readonly object _socketQueueLock = new();
    //private volatile bool _isSocketProcessing = false;


    private async void OnRunPeriodicTagPublisherSocketTasksEvent(object sender, ElapsedEventArgs e)
    {
        const int BatchSize = 100;
        const int MaxQueueSize = 1000;

        try
        {
            _timerTagPublisherSocket.Enabled = false;

            // Verify if socket server is enabled
            if (!string.Equals("1", _standaloneConfigDTO?.socketServer, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // Delegate batch processing to TcpSocketService
            _ = await _tcpSocketService.ProcessMessageBatchAsync(
                _messageQueueTagSmartReaderTagEventSocketServer,
                BatchSize,
                MaxQueueSize
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in periodic socket task.");
        }
        finally
        {
            _timerTagPublisherSocket.Enabled = true;
        }
    }

    private async void OnRunPeriodicTagPublisherWebSocketTasksEvent(object sender, ElapsedEventArgs e)
    {
        const int BatchSize = 100;
        const int MaxQueueSize = 1000;

        try
        {
            _timerTagPublisherWebSocket.Enabled = false;

            // Delegate batch processing to TcpSocketService
            _ = await _webSocketService.ProcessMessageBatchAsync(
                _messageQueueTagSmartReaderTagEventWebSocketServer,
                BatchSize,
                MaxQueueSize
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in periodic web socket task.");
        }
        finally
        {
            _timerTagPublisherWebSocket.Enabled = true;
        }
    }



    //private void HandleQueueOverflow()
    //{
    //    while (_messageQueueTagSmartReaderTagEventSocketServer.Count > 1000)
    //    {
    //        if (_messageQueueTagSmartReaderTagEventSocketServer.TryDequeue(out var _))
    //        {
    //            _logger.LogWarning("Dropped oldest message from socket queue due to server unavailability");
    //        }
    //    }
    //}

    private async void OnRunPeriodicSummaryStreamPublisherTasksEvent(object sender, ElapsedEventArgs e)
    {
        _timerSummaryStreamPublisher.Enabled = false;
        _timerSummaryStreamPublisher.Stop();
        try
        {
#pragma warning disable CS8602 // Dereference of a possibly null reference.
            if (string.Equals("0", _standaloneConfigDTO.stopTriggerType, StringComparison.OrdinalIgnoreCase)
                && _messageQueueTagSmartReaderTagEventGroupToValidate.Count > 0)
            {
                ProcessValidationTagQueue();
            }
            if (_isStarted && string.Equals("2", _standaloneConfigDTO.stopTriggerType, StringComparison.OrdinalIgnoreCase)
                && _messageQueueTagSmartReaderTagEventGroupToValidate.Count > 0)
            {
                ProcessValidationTagQueue();
            }
            else
            {
                await Task.Delay(100);
            }
#pragma warning restore CS8602 // Dereference of a possibly null reference.

        }
        catch (Exception)
        {
        }


        _timerSummaryStreamPublisher.Enabled = true;
        _timerSummaryStreamPublisher.Start();
    }

    private async void OnRunPeriodicUsbDriveTasksEvent(object sender, ElapsedEventArgs e)
    {
        _timerTagPublisherUsbDrive.Enabled = false;
        _timerTagPublisherUsbDrive.Stop();


        try
        {
            JObject? smartReaderTagReadEvent;
            while (_messageQueueTagSmartReaderTagEventUsbDrive.TryDequeue(out smartReaderTagReadEvent))
                try
                {
                    await ProcessUsbDriveJsonTagEventDataAsync(smartReaderTagReadEvent);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error on OnRunPeriodicUsbDriveTasksEvent " + ex.Message);
                }
        }
        catch (Exception)
        {
        }


        _timerTagPublisherUsbDrive.Enabled = true;
        _timerTagPublisherUsbDrive.Start();
    }


#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
    private async void OnRunPeriodicTagPublisherOpcUaTasksEvent(object sender, ElapsedEventArgs e)
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
    {
#pragma warning disable CS8600, CS8602, CS8604 // Dereference of a possibly null reference.
        if (Monitor.TryEnter(_timerTagPublisherOpcUaLock))
            try
            {
                JObject? smartReaderTagReadEvent;
                while (_messageQueueTagSmartReaderTagEventOpcUa.TryDequeue(out smartReaderTagReadEvent))
                    try
                    {
                        if (_standaloneConfigDTO != null && string.Equals("1", _standaloneConfigDTO.enableOpcUaClient,
                                StringComparison.OrdinalIgnoreCase))
                            if (smartReaderTagReadEvent.ContainsKey("tag_reads"))
                            {
                                var tagReads = smartReaderTagReadEvent.GetValue("tag_reads").FirstOrDefault();
                                var epc = (string)tagReads["epc"];
                                if (!string.IsNullOrEmpty(epc))
                                {
                                    //OpcUaHelper.WriteFieldData("String", 2, epc);
                                }
                            }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex,
                            "Unexpected error on OnRunPeriodicTagPublisherUdpTasksEvent " + ex.Message);
                    }
            }
            catch (Exception)
            {
            }
            finally
            {
                Monitor.Exit(_timerTagPublisherOpcUaLock);
            }
#pragma warning restore CS8600, CS8602, CS8604 // Dereference of a possibly null reference.
    }

    private async void OnRunPeriodicTagPublisherUdpTasksEvent(object sender, ElapsedEventArgs e)
    {
#pragma warning disable CS8600, CS8602, CS8604 // Dereference of a possibly null reference.
        _timerTagPublisherUdpServer.Enabled = false;
        _timerTagPublisherUdpServer.Stop();

        try
        {
            JObject smartReaderTagReadEvent;
            while (_messageQueueTagSmartReaderTagEventUdpServer.TryDequeue(out smartReaderTagReadEvent))
                try
                {
                    await ProcessUdpDataTagEventDataAsync(smartReaderTagReadEvent);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error on OnRunPeriodicTagPublisherUdpTasksEvent " + ex.Message);
                }
        }
        catch (Exception)
        {
        }


        _timerTagPublisherUdpServer.Enabled = true;
        _timerTagPublisherUdpServer.Start();
#pragma warning restore CS8600, CS8602, CS8604 // Dereference of a possibly null reference.
    }

    //    private async void OnRunPeriodicTagPublisherMqttTasksEvent(object sender, ElapsedEventArgs e)
    //    {
    //#pragma warning disable CS8600, CS8602, CS8604 // Dereference of a possibly null reference.
    //        _timerTagPublisherMqtt.Enabled = false;
    //        _timerTagPublisherMqtt.Stop();

    //        //_logger.LogInformation($"OnRunPeriodicTagPublisherMqttTasksEvent: >>>");

    //        JObject smartReaderTagReadEvent;
    //        JObject? smartReaderTagReadEventAggregated = null;
    //        var smartReaderTagReadEventsArray = new JArray();

    //        double mqttPublishIntervalInSec = 1;
    //        double mqttPublishIntervalInMillis = 10;
    //        double mqttUpdateTagEventsListBatchOnChangeIntervalInSec = 1;
    //        double mqttUpdateTagEventsListBatchOnChangeIntervalInMillis = 10;
    //        //bool isUpdateTimeForOnChangeEvent = false;

    //        if (!string.IsNullOrEmpty(_standaloneConfigDTO.mqttPuslishIntervalSec))
    //            double.TryParse(_standaloneConfigDTO.mqttPuslishIntervalSec, NumberStyles.Float,
    //                CultureInfo.InvariantCulture, out mqttPublishIntervalInSec);

    //        if (mqttPublishIntervalInSec == 0)
    //            mqttPublishIntervalInMillis = 5;
    //        else
    //            mqttPublishIntervalInMillis = mqttPublishIntervalInSec * 1000;

    //        if (!string.IsNullOrEmpty(_standaloneConfigDTO.updateTagEventsListBatchOnChangeIntervalInSec))
    //            double.TryParse(_standaloneConfigDTO.updateTagEventsListBatchOnChangeIntervalInSec, NumberStyles.Float,
    //                CultureInfo.InvariantCulture, out mqttUpdateTagEventsListBatchOnChangeIntervalInSec);

    //        if (mqttUpdateTagEventsListBatchOnChangeIntervalInSec == 0)
    //            mqttUpdateTagEventsListBatchOnChangeIntervalInMillis = 5;
    //        else
    //            mqttUpdateTagEventsListBatchOnChangeIntervalInMillis = mqttUpdateTagEventsListBatchOnChangeIntervalInSec * 1000;

    //        try
    //        {
    //            if (!string.IsNullOrEmpty(_standaloneConfigDTO.mqttEnabled)
    //                        && string.Equals("1", _standaloneConfigDTO.mqttEnabled,
    //                            StringComparison.OrdinalIgnoreCase))
    //            {
    //                if (!string.IsNullOrEmpty(_standaloneConfigDTO.enableTagEventsListBatch)
    //                        && string.Equals("1", _standaloneConfigDTO.enableTagEventsListBatch,
    //                            StringComparison.OrdinalIgnoreCase))
    //                {

    //                    //_logger.LogInformation($"_smartReaderTagEventsListBatchOnUpdate: {_smartReaderTagEventsListBatchOnUpdate.Count}");
    //                    //_logger.LogInformation($"_smartReaderTagEventsListBatch: {_smartReaderTagEventsListBatch.Count}");
    //                    if (!string.IsNullOrEmpty(_standaloneConfigDTO.updateTagEventsListBatchOnChange)
    //                        && string.Equals("1", _standaloneConfigDTO.updateTagEventsListBatchOnChange)
    //                        && !_stopwatchStopTagEventsListBatchOnChange.IsRunning)
    //                    {
    //                        _stopwatchStopTagEventsListBatchOnChange.Start();
    //                    }


    //                    if (!string.IsNullOrEmpty(_standaloneConfigDTO.updateTagEventsListBatchOnChange)
    //                        && string.Equals("1", _standaloneConfigDTO.updateTagEventsListBatchOnChange,
    //                            StringComparison.OrdinalIgnoreCase)
    //                        && _smartReaderTagEventsListBatchOnUpdate.Count > 0)
    //                    {

    //                        //while (_stopwatchStopTagEventsListBatchOnChange.Elapsed.Seconds * 1000 < mqttUpdateTagEventsListBatchOnChangeIntervalInMillis)
    //                        //{
    //                        //    await Task.Delay(10);
    //                        //}
    //                        if (_stopwatchStopTagEventsListBatchOnChange.Elapsed.Seconds * 1000 >= mqttUpdateTagEventsListBatchOnChangeIntervalInMillis)
    //                        {
    //                            if (_stopwatchStopTagEventsListBatchOnChange.IsRunning)
    //                            {
    //                                _stopwatchStopTagEventsListBatchOnChange.Stop();
    //                                _stopwatchStopTagEventsListBatchOnChange.Reset();
    //                            }

    //                            if (_smartReaderTagEventsListBatchOnUpdate.Count > 0)
    //                            {
    //                                _logger.LogInformation($"OnRunPeriodicTagPublisherMqttTasksEvent: publishing new events due to OnChange {_smartReaderTagEventsListBatchOnUpdate.Count}");
    //                                foreach (var smartReaderTagReadEventBatchKV in _smartReaderTagEventsListBatchOnUpdate)
    //                                {
    //                                    if (!_smartReaderTagEventsListBatch.ContainsKey(smartReaderTagReadEventBatchKV.Key))
    //                                    {
    //                                        _smartReaderTagEventsListBatch.TryAdd(smartReaderTagReadEventBatchKV.Key, smartReaderTagReadEventBatchKV.Value);
    //                                    }
    //                                    else
    //                                    {
    //                                        _smartReaderTagEventsListBatch[smartReaderTagReadEventBatchKV.Key] = smartReaderTagReadEventBatchKV.Value;
    //                                    }

    //                                }

    //                                _smartReaderTagEventsListBatchOnUpdate.Clear();

    //                                if (_smartReaderTagEventsListBatch.Count > 0)
    //                                {
    //                                    foreach (var smartReaderTagReadEventBatch in _smartReaderTagEventsListBatch.Values)
    //                                    {
    //                                        smartReaderTagReadEvent = smartReaderTagReadEventBatch;
    //                                        try
    //                                        {
    //                                            if (smartReaderTagReadEvent == null)
    //                                                continue;
    //                                            if (smartReaderTagReadEventAggregated == null)
    //                                            {
    //                                                smartReaderTagReadEventAggregated = smartReaderTagReadEvent;
    //                                                smartReaderTagReadEventsArray.Add(smartReaderTagReadEvent.Property("tag_reads").ToList()
    //                                                    .FirstOrDefault().FirstOrDefault());
    //                                            }
    //                                            else
    //                                            {
    //                                                try
    //                                                {
    //                                                    smartReaderTagReadEventsArray.Add(smartReaderTagReadEvent.Property("tag_reads").ToList()
    //                                                        .FirstOrDefault().FirstOrDefault());
    //                                                }
    //                                                catch (Exception ex)
    //                                                {
    //                                                    _logger.LogInformation(ex,
    //                                                        "Unexpected error on OnRunPeriodicTagPublisherMqttTasksEvent " + ex.Message);
    //                                                }
    //                                            }
    //                                        }
    //                                        catch (Exception ex)
    //                                        {
    //                                            _logger.LogError(ex, "Unexpected error on OnRunPeriodicTagPublisherMqttTasksEvent " + ex.Message);
    //                                        }
    //                                    }
    //                                }
    //                            }
    //                        }
    //                    }
    //                    else
    //                    {
    //                        if (_smartReaderTagEventsListBatch.Count > 0
    //                            && _smartReaderTagEventsListBatchOnUpdate.Count == 0)
    //                        {
    //                            if (string.Equals("1", _standaloneConfigDTO.enableTagEventsListBatchPublishing))
    //                            {
    //                                if (!_mqttPublisherStopwatch.IsRunning)
    //                                {
    //                                    _mqttPublisherStopwatch.Start();
    //                                }
    //                                //while (mqttPublisherTimer.Elapsed.Seconds * 1000 < mqttPublishIntervalInMillis)
    //                                //{
    //                                //    await Task.Delay(10);
    //                                //}
    //                                if (_mqttPublisherStopwatch.Elapsed.Seconds * 1000 >= mqttPublishIntervalInMillis)
    //                                {
    //                                    _mqttPublisherStopwatch.Restart();
    //                                    _logger.LogInformation($"OnRunPeriodicTagPublisherMqttTasksEvent: publishing batch events list due to MqttPublishInterval {_smartReaderTagEventsListBatch.Count}");
    //                                    foreach (var smartReaderTagReadEventBatch in _smartReaderTagEventsListBatch.Values)
    //                                    {
    //                                        smartReaderTagReadEvent = smartReaderTagReadEventBatch;
    //                                        try
    //                                        {
    //                                            if (smartReaderTagReadEvent == null)
    //                                                continue;
    //                                            if (smartReaderTagReadEventAggregated == null)
    //                                            {
    //                                                smartReaderTagReadEventAggregated = smartReaderTagReadEvent;
    //                                                smartReaderTagReadEventsArray.Add(smartReaderTagReadEvent.Property("tag_reads").ToList()
    //                                                    .FirstOrDefault().FirstOrDefault());
    //                                            }
    //                                            else
    //                                            {
    //                                                try
    //                                                {
    //                                                    smartReaderTagReadEventsArray.Add(smartReaderTagReadEvent.Property("tag_reads").ToList()
    //                                                        .FirstOrDefault().FirstOrDefault());
    //                                                }
    //                                                catch (Exception ex)
    //                                                {
    //                                                    _logger.LogInformation(ex,
    //                                                        "Unexpected error on OnRunPeriodicTagPublisherMqttTasksEvent " + ex.Message);
    //                                                }
    //                                            }
    //                                        }
    //                                        catch (Exception ex)
    //                                        {
    //                                            _logger.LogError(ex, "Unexpected error on OnRunPeriodicTagPublisherMqttTasksEvent " + ex.Message);
    //                                        }
    //                                    }
    //                                }
    //                            }
    //                        }
    //                    }
    //                }
    //                else
    //                {
    //                    if (!_mqttPublisherStopwatch.IsRunning)
    //                    {
    //                        _mqttPublisherStopwatch.Start();
    //                    }

    //                    while (_mqttPublisherStopwatch.Elapsed.Seconds * 1000 < mqttPublishIntervalInMillis)
    //                    {
    //                        while (_messageQueueTagSmartReaderTagEventMqtt.TryDequeue(out smartReaderTagReadEvent))
    //                            try
    //                            {
    //                                if (smartReaderTagReadEvent == null)
    //                                    continue;
    //                                if (smartReaderTagReadEventAggregated == null)
    //                                {
    //                                    smartReaderTagReadEventAggregated = smartReaderTagReadEvent;
    //                                    smartReaderTagReadEventsArray.Add(smartReaderTagReadEvent.Property("tag_reads").ToList()
    //                                        .FirstOrDefault().FirstOrDefault());
    //                                }
    //                                else
    //                                {
    //                                    try
    //                                    {
    //                                        smartReaderTagReadEventsArray.Add(smartReaderTagReadEvent.Property("tag_reads").ToList()
    //                                            .FirstOrDefault().FirstOrDefault());
    //                                    }
    //                                    catch (Exception ex)
    //                                    {
    //                                        _logger.LogInformation(ex,
    //                                            "Unexpected error on OnRunPeriodicTagPublisherMqttTasksEvent " + ex.Message);
    //                                    }
    //                                }
    //                            }
    //                            catch (Exception ex)
    //                            {
    //                                _logger.LogError(ex, "Unexpected error on OnRunPeriodicTagPublisherMqttTasksEvent " + ex.Message);
    //                            }
    //                    }
    //                    _mqttPublisherStopwatch.Restart();
    //                }
    //            }

    private async void OnRunPeriodicTagPublisherMqttTasksEvent(object sender, ElapsedEventArgs e)
    {
        _timerTagPublisherMqtt.Enabled = false;
        _timerTagPublisherMqtt.Stop();

        try
        {
            // If we can't acquire the semaphore immediately, return
            if (!_publishSemaphore.Wait(0))
            {
                _logger.LogWarning("OnRunPeriodicTagPublisherMqttTasksEvent: can't acquire the semaphore immediately.");
                return;
            }

            try
            {
                if (IsMqttEnabled())
                {
                    if (IsMqttBatchListsEnabled())
                    {
                        await ProcessTagPublishingMqtt();
                    }
                    else
                    {
                        await ProcessMqttMessagesAsync();
                    }
                }
                else
                {
                    _ = Task.Delay(100);
                }

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ProcessTagPublishingMqtt");
            }
            finally
            {
                _ = _publishSemaphore.Release();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in OnRunPeriodicTagPublisherMqttTasksEvent");
        }
        finally
        {
            _timerTagPublisherMqtt.Enabled = true;
            _timerTagPublisherMqtt.Start();
        }


    }

    private MqttPublishingConfiguration LoadMqttPublishingConfiguration()
    {
        var config = new MqttPublishingConfiguration();

        double mqttPublishIntervalInSec = 1;
        if (_standaloneConfigDTO == null)
        {
            _standaloneConfigDTO = _configurationService.LoadConfig();
        }
        if (!string.IsNullOrEmpty(_standaloneConfigDTO.mqttPuslishIntervalSec))
        {
            _ = double.TryParse(_standaloneConfigDTO.mqttPuslishIntervalSec,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out mqttPublishIntervalInSec);
        }
        config.PublishIntervalMs = mqttPublishIntervalInSec == 0 ? 5 : mqttPublishIntervalInSec * 1000;

        double batchUpdateIntervalInSec = 1;
        if (!string.IsNullOrEmpty(_standaloneConfigDTO.updateTagEventsListBatchOnChangeIntervalInSec))
        {
            _ = double.TryParse(_standaloneConfigDTO.updateTagEventsListBatchOnChangeIntervalInSec,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out batchUpdateIntervalInSec);
        }
        config.BatchUpdateIntervalMs = batchUpdateIntervalInSec == 0 ? 5 : batchUpdateIntervalInSec * 1000;


        bool enableTagEventsListBatch = true;
        if (!string.IsNullOrEmpty(_standaloneConfigDTO.enableTagEventsListBatch))
        {
            _ = Utils.TryParseBoolFromString(_standaloneConfigDTO.enableTagEventsListBatch, out enableTagEventsListBatch);
        }
        config.BatchListEnabled = enableTagEventsListBatch;

        bool enableTagEventsListBatchPublishing = true;
        if (!string.IsNullOrEmpty(_standaloneConfigDTO.enableTagEventsListBatchPublishing))
        {
            _ = Utils.TryParseBoolFromString(_standaloneConfigDTO.enableTagEventsListBatchPublishing, out enableTagEventsListBatchPublishing);
        }
        config.BatchListPublishingEnabled = enableTagEventsListBatchPublishing;

        bool batchListCleanupTagEventsOnReload = true;
        if (!string.IsNullOrEmpty(_standaloneConfigDTO.cleanupTagEventsListBatchOnReload))
        {
            _ = Utils.TryParseBoolFromString(_standaloneConfigDTO.cleanupTagEventsListBatchOnReload, out batchListCleanupTagEventsOnReload);
        }
        config.BatchListCleanupTagEventsOnReload = batchListCleanupTagEventsOnReload;

        bool updateTagEventsListBatchOnChange = true;
        if (!string.IsNullOrEmpty(_standaloneConfigDTO.updateTagEventsListBatchOnChange))
        {
            _ = Utils.TryParseBoolFromString(_standaloneConfigDTO.updateTagEventsListBatchOnChange, out updateTagEventsListBatchOnChange);
        }
        config.BatchListUpdateTagEventsOnChange = updateTagEventsListBatchOnChange;

        bool filterTagEventsListBatchOnChangeBasedOnAntennaZone = true;
        if (!string.IsNullOrEmpty(_standaloneConfigDTO.filterTagEventsListBatchOnChangeBasedOnAntennaZone))
        {
            _ = Utils.TryParseBoolFromString(_standaloneConfigDTO.filterTagEventsListBatchOnChangeBasedOnAntennaZone, out filterTagEventsListBatchOnChangeBasedOnAntennaZone);

        }
        config.BatchListUpdateTagEventsOnAntennaZoneChange = filterTagEventsListBatchOnChangeBasedOnAntennaZone;


        return config;
    }

    private bool IsSoftwareFilterReadCountTimeoutEnabled()
    {
        bool isEnabled = !string.IsNullOrEmpty(_standaloneConfigDTO.softwareFilterReadCountTimeoutEnabled) &&
            string.Equals("1", _standaloneConfigDTO.softwareFilterReadCountTimeoutEnabled, StringComparison.OrdinalIgnoreCase);

        if (!isEnabled)
        {
            _logger.LogDebug("Software filter read count timeout is disabled");
        }
        return isEnabled;
    }

    private bool IsHttpPostEnabled()
    {
        bool isHttpPostEnabled = !string.IsNullOrEmpty(_standaloneConfigDTO.httpPostEnabled) &&
            string.Equals("1", _standaloneConfigDTO.httpPostEnabled, StringComparison.OrdinalIgnoreCase);

        if (!isHttpPostEnabled)
        {
            _logger.LogDebug("HTTP POST is disabled");
        }
        return isHttpPostEnabled;
    }

    private bool IsBarcodeTcpEnabled()
    {
        bool isEnabled = !string.IsNullOrEmpty(_standaloneConfigDTO.enableBarcodeTcp) &&
            string.Equals("1", _standaloneConfigDTO.enableBarcodeTcp, StringComparison.OrdinalIgnoreCase);

        if (!isEnabled)
        {
            _logger.LogDebug("Barcode TCP is disabled");
        }
        return isEnabled;
    }

    private bool IsSocketServerEnabled()
    {
        bool isSocketServerEnabled = !string.IsNullOrEmpty(_standaloneConfigDTO.socketServer) &&
            string.Equals("1", _standaloneConfigDTO.socketServer, StringComparison.OrdinalIgnoreCase);

        if (!isSocketServerEnabled)
        {
            _logger.LogDebug("Socket Server is disabled");
        }
        return isSocketServerEnabled;
    }

    private bool IsSocketCommandServerEnabled()
    {
        bool isSocketCommandServerEnabled = !string.IsNullOrEmpty(_standaloneConfigDTO.socketCommandServer) &&
            string.Equals("1", _standaloneConfigDTO.socketCommandServer, StringComparison.OrdinalIgnoreCase);

        if (!isSocketCommandServerEnabled)
        {
            _logger.LogDebug("Socket Server is disabled");
        }
        return isSocketCommandServerEnabled;
    }

    private bool IsAdvancedGpoEnabled()
    {
        bool isAdvancedGpoEnabled = !string.IsNullOrEmpty(_standaloneConfigDTO.advancedGpoEnabled) &&
            string.Equals("1", _standaloneConfigDTO.advancedGpoEnabled, StringComparison.OrdinalIgnoreCase);

        if (!isAdvancedGpoEnabled)
        {
            _logger.LogDebug("Advanced GPO is disabled");
        }
        return isAdvancedGpoEnabled;
    }

    private bool IsHeartbeatEnabled()
    {
        bool isHeartbeatEnabled = !string.IsNullOrEmpty(_standaloneConfigDTO.heartbeatEnabled) &&
            string.Equals("1", _standaloneConfigDTO.heartbeatEnabled, StringComparison.OrdinalIgnoreCase);

        if (!isHeartbeatEnabled)
        {
            _logger.LogDebug("Heartbeat is disabled");
        }
        return isHeartbeatEnabled;
    }

    private bool IsC1g2FilterEnabled()
    {
        bool isC1g2FilterEnabled = !string.IsNullOrEmpty(_standaloneConfigDTO.c1g2FilterEnabled) &&
            string.Equals("1", _standaloneConfigDTO.c1g2FilterEnabled, StringComparison.OrdinalIgnoreCase);

        if (!isC1g2FilterEnabled)
        {
            _logger.LogDebug("C1G2 filtering is disabled");
        }
        return isC1g2FilterEnabled;
    }

    private bool IsBackupToFlashDriveOnGpiEventEnabled()
    {
        bool isEnabled = !string.IsNullOrEmpty(_standaloneConfigDTO.backupToFlashDriveOnGpiEventEnabled) &&
            string.Equals("1", _standaloneConfigDTO.backupToFlashDriveOnGpiEventEnabled, StringComparison.OrdinalIgnoreCase);

        if (!isEnabled)
        {
            _logger.LogDebug("Backup to flash drive on GPI event is disabled");
        }
        return isEnabled;
    }

    private bool IsUsbFlashDriveEnabled()
    {
        bool isEnabled = !string.IsNullOrEmpty(_standaloneConfigDTO.usbFlashDrive) &&
            string.Equals("1", _standaloneConfigDTO.usbFlashDrive, StringComparison.OrdinalIgnoreCase);

        if (!isEnabled)
        {
            _logger.LogDebug("USB Flash Drive disabled");
        }
        return isEnabled;
    }

    private bool IsBackupToInternalFlashEnabled()
    {
        bool isEnabled = !string.IsNullOrEmpty(_standaloneConfigDTO.backupToInternalFlashEnabled) &&
            string.Equals("1", _standaloneConfigDTO.backupToInternalFlashEnabled, StringComparison.OrdinalIgnoreCase);

        if (!isEnabled)
        {
            _logger.LogDebug("Backup to internal flash is disabled");
        }
        return isEnabled;
    }

    private bool IsUsbHidEnabled()
    {
        bool isEnabled = !string.IsNullOrEmpty(_standaloneConfigDTO.usbHid) &&
            string.Equals("1", _standaloneConfigDTO.usbHid, StringComparison.OrdinalIgnoreCase);

        if (!isEnabled)
        {
            _logger.LogDebug("USB HID is disabled");
        }
        return isEnabled;
    }
    private bool IsUdpServerEnabled()
    {
        bool isEnabled = !string.IsNullOrEmpty(_standaloneConfigDTO.udpServer) &&
            string.Equals("1", _standaloneConfigDTO.udpServer, StringComparison.OrdinalIgnoreCase);

        if (!isEnabled)
        {
            _logger.LogDebug("UDP server is disabled");
        }
        return isEnabled;
    }

    private bool IsGpiEnabled()
    {
        bool isEnabled = !string.IsNullOrEmpty(_standaloneConfigDTO.includeGpiEvent) &&
            string.Equals("1", _standaloneConfigDTO.includeGpiEvent, StringComparison.OrdinalIgnoreCase);

        if (!isEnabled)
        {
            _logger.LogDebug("GPI event is disabled");
        }
        return isEnabled;
    }

    private bool IsOpcUaClientEnabled()
    {
        bool isEnabled = !string.IsNullOrEmpty(_standaloneConfigDTO.enableOpcUaClient) &&
            string.Equals("1", _standaloneConfigDTO.enableOpcUaClient, StringComparison.OrdinalIgnoreCase);

        if (!isEnabled)
        {
            _logger.LogDebug("OPC UA is disabled");
        }
        return isEnabled;
    }

    private bool IsSummaryStreamEnabled()
    {
        bool isEnabled = !string.IsNullOrEmpty(_standaloneConfigDTO.enableSummaryStream) &&
            string.Equals("1", _standaloneConfigDTO.enableSummaryStream, StringComparison.OrdinalIgnoreCase);

        if (!isEnabled)
        {
            _logger.LogDebug("Summary Stream is disabled");
        }
        return isEnabled;
    }

    private bool IsTagValidationEnabled()
    {
        bool isTagValidationEnabled = !string.IsNullOrEmpty(_standaloneConfigDTO.tagValidationEnabled) &&
            string.Equals("1", _standaloneConfigDTO.tagValidationEnabled, StringComparison.OrdinalIgnoreCase);

        if (!isTagValidationEnabled)
        {
            _logger.LogDebug("Tag validation is disabled");
        }
        return isTagValidationEnabled;
    }

    private bool IsLogFileEnabled()
    {
        bool isLogFileEnabled = !string.IsNullOrEmpty(_standaloneConfigDTO.isLogFileEnabled) &&
            string.Equals("1", _standaloneConfigDTO.isLogFileEnabled, StringComparison.OrdinalIgnoreCase);

        if (!isLogFileEnabled)
        {
            _logger.LogDebug("Log file is disabled");
        }
        return isLogFileEnabled;
    }

    private bool IsTagPresenceTimeoutEnabled()
    {
        bool isEnabled = !string.IsNullOrEmpty(_standaloneConfigDTO.tagPresenceTimeoutEnabled) &&
            string.Equals("1", _standaloneConfigDTO.tagPresenceTimeoutEnabled, StringComparison.OrdinalIgnoreCase);

        if (!isEnabled)
        {
            _logger.LogDebug("Tag presence timeout is disabled");
        }
        return isEnabled;
    }

    private bool IsSmartreaderEnabledForManagementOnly()
    {
        bool isEnabled = !string.IsNullOrEmpty(_standaloneConfigDTO.smartreaderEnabledForManagementOnly) &&
            string.Equals("1", _standaloneConfigDTO.smartreaderEnabledForManagementOnly, StringComparison.OrdinalIgnoreCase);

        if (!isEnabled)
        {
            _logger.LogDebug("Smartreader is not enabled for management only");
        }
        return isEnabled;
    }
    private bool IsMqttEnabled()
    {
        // Check if MQTT is enabled in configuration
        bool isMqttEnabled = !string.IsNullOrEmpty(_standaloneConfigDTO.mqttEnabled) &&
            string.Equals("1", _standaloneConfigDTO.mqttEnabled, StringComparison.OrdinalIgnoreCase);

        // Log the MQTT status for debugging
        if (!isMqttEnabled)
        {
            _logger.LogDebug("MQTT publishing is disabled");
        }

        return isMqttEnabled;
    }

    private bool IsMqttBatchListsEnabled()
    {
        // Check if MQTT is enabled in configuration
        bool isMqttBatchListsEnabled = !string.IsNullOrEmpty(_standaloneConfigDTO.enableTagEventsListBatch) &&
            string.Equals("1", _standaloneConfigDTO.enableTagEventsListBatch, StringComparison.OrdinalIgnoreCase);

        // Log the MQTT status for debugging
        if (!isMqttBatchListsEnabled)
        {
            _logger.LogDebug("MQTT batch list publishing is disabled");
        }

        return isMqttBatchListsEnabled;
    }

    private async Task ProcessTagPublishingMqtt()
    {
        if (!IsMqttEnabled())
            return;

        var aggregatedEvent = default(JObject);
        var eventsArray = new JArray();

        _tagEventPublisher.ProcessPublishing(ref aggregatedEvent, eventsArray);

        if (aggregatedEvent != null)
        {
            // Publish the aggregated event
            await PublishMqttJsonTagEventDataAsync(aggregatedEvent);
        }
    }

    //private async Task ProcessBatchPublishing()
    //{
    //    JObject? smartReaderTagReadEventAggregated = null;
    //    var smartReaderTagReadEventsArray = new JArray();
    //    try
    //    {
    //        // Use SemaphoreSlim for async-safe locking
    //        await _batchLock.WaitAsync();


    //        try
    //        {
    //            if (IsOnChangeUpdateEnabled())
    //            {
    //                ProcessOnChangeUpdates(ref smartReaderTagReadEventAggregated, smartReaderTagReadEventsArray);
    //            }
    //            else
    //            {
    //                ProcessRegularIntervalPublishing(ref smartReaderTagReadEventAggregated, smartReaderTagReadEventsArray);
    //            }

    //            // Modified condition to also publish when we have an aggregated event but empty array
    //            // This covers the case where we want to publish an empty event list
    //            if (smartReaderTagReadEventAggregated != null)
    //            {
    //                smartReaderTagReadEventAggregated["tag_reads"] = smartReaderTagReadEventsArray;
    //                await PublishMqttJsonTagEventDataAsync(smartReaderTagReadEventAggregated);

    //                _logger.LogInformation(
    //                    "Published event with {Count} tag reads",
    //                    smartReaderTagReadEventsArray.Count);
    //            }
    //        }
    //        catch (Exception ex)
    //        {
    //            _logger.LogError(ex, "Error in ProcessBatchPublishing");
    //        }
    //        finally
    //        {
    //            // Always release the semaphore
    //            _batchLock.Release();
    //        }
    //    }
    //    catch (Exception ex)
    //    {
    //        _logger.LogError(ex, "Error in ProcessBatchPublishing.");
    //    }


    //}

    //private bool IsOnChangeUpdateEnabled()
    //{
    //    return !string.IsNullOrEmpty(_standaloneConfigDTO.updateTagEventsListBatchOnChange) &&
    //           string.Equals("1", _standaloneConfigDTO.updateTagEventsListBatchOnChange,
    //               StringComparison.OrdinalIgnoreCase);
    //}

    ///// <summary>
    ///// Filters changes based on whether the antenna zone value has changed for the same EPC.
    ///// </summary>
    ///// <param name="tagRead">The tag read event to process.</param>
    ///// <param name="existingEvent">The existing event for the same EPC.</param>
    ///// <returns>True if the antenna zone value has changed, otherwise false.</returns>
    //private bool HasAntennaZoneChanged(JObject tagRead, JObject existingEvent)
    //{
    //    if (tagRead == null || existingEvent == null)
    //    {
    //        return false;
    //    }

    //    // Extract the antenna zone from the tag read and the existing event
    //    var newAntennaZone = tagRead["antennaZone"]?.Value<string>();
    //    var existingAntennaZone = existingEvent["antennaZone"]?.Value<string>();

    //    // Determine if the antenna zone has changed
    //    return !string.Equals(newAntennaZone, existingAntennaZone, StringComparison.OrdinalIgnoreCase);
    //}







    //private void ProcessTagEvent(
    //JObject? smartReaderTagReadEvent,
    //ref JObject? smartReaderTagReadEventAggregated,
    //JArray smartReaderTagReadEventsArray)
    //{
    //    // Skip null events
    //    if (smartReaderTagReadEvent == null)
    //    {
    //        return;
    //    }

    //    try
    //    {
    //        // Initialize aggregated event if it's null
    //        if (smartReaderTagReadEventAggregated == null)
    //        {
    //            smartReaderTagReadEventAggregated = smartReaderTagReadEvent;

    //            // Extract and add the first tag read
    //            var firstTagRead = ExtractTagRead(smartReaderTagReadEvent);
    //            if (firstTagRead != null)
    //            {
    //                smartReaderTagReadEventsArray.Add(firstTagRead);
    //                _logger.LogDebug("Initialized aggregated event with first tag read");
    //            }
    //        }
    //        else
    //        {
    //            // Add subsequent tag reads to the array
    //            var tagRead = ExtractTagRead(smartReaderTagReadEvent);
    //            if (tagRead != null)
    //            {
    //                smartReaderTagReadEventsArray.Add(tagRead);
    //                _logger.LogDebug("Added tag read to existing aggregated event");
    //            }
    //        }
    //    }
    //    catch (Exception ex)
    //    {
    //        _logger.LogError(ex,
    //            "Unexpected error processing tag event: {ErrorMessage}", ex.Message);
    //        throw; // Rethrow to be handled by caller
    //    }
    //}




    //private void ProcessOnChangeUpdates(ref JObject? smartReaderTagReadEventAggregated, JArray smartReaderTagReadEventsArray)
    //{
    //    if (!_stopwatchStopTagEventsListBatchOnChange.IsRunning)
    //    {
    //        _stopwatchStopTagEventsListBatchOnChange.Start();
    //    }

    //    if(IsTagPresenceTimeoutEnabled() 
    //        && _smartReaderTagEventsAbsence.Count > 0
    //        && _smartReaderTagEventsListBatch.Count > 0)
    //    {
    //        JObject smartReaderTagReadEvent;

    //        foreach (var tagEpcToRemove in _smartReaderTagEventsAbsence.Keys)
    //        {
    //            JObject? expiredTagEvent = null;

    //            if (_smartReaderTagEventsListBatch.ContainsKey(tagEpcToRemove))
    //            {
    //                _smartReaderTagEventsListBatch.TryRemove(tagEpcToRemove, out expiredTagEvent);
    //            }

    //        }

    //        _smartReaderTagEventsAbsence.Clear();
    //    }
    //    if (_stopwatchStopTagEventsListBatchOnChange.ElapsedMilliseconds >= _mqttPublishingConfiguration.BatchUpdateIntervalMs)
    //    {
    //        _stopwatchStopTagEventsListBatchOnChange.Reset();

    //        // Check if we need to publish an empty event list - only if:
    //        // 1. We currently have tags in the batch
    //        // 2. No new updates
    //        // 3. Haven't already published an empty event
    //        bool shouldPublishEmptyEvent = _smartReaderTagEventsListBatch.Count == 0 &&
    //                                        _smartReaderTagEventsAbsence.Count > 0 &&
    //                                     !_emptyEventPublished;

    //        // Process updates if we have any
    //        if (_smartReaderTagEventsListBatchOnUpdate.Count > 0)
    //        {
    //            _logger.LogInformation($"Processing {_smartReaderTagEventsListBatchOnUpdate.Count} on-change updates");

    //            // Reset empty event flag since we have new tags
    //            _emptyEventPublished = false;

    //            // Update main batch with changes
    //            foreach (var kvp in _smartReaderTagEventsListBatchOnUpdate)
    //            {
    //                if(_smartReaderTagEventsListBatch.ContainsKey(kvp.Key))
    //                {
    //                    _smartReaderTagEventsListBatch[kvp.Key] = kvp.Value;
    //                }
    //                else
    //                {
    //                    _smartReaderTagEventsListBatch.TryAdd(kvp.Key, kvp.Value);
    //                }

    //            }

    //            _smartReaderTagEventsListBatchOnUpdate.Clear();

    //            // Aggregate events for publishing
    //            AggregateEvents(_smartReaderTagEventsListBatch.Values,
    //                ref smartReaderTagReadEventAggregated,
    //                smartReaderTagReadEventsArray);
    //        }
    //        // Handle empty read zone case - only once when tags leave
    //        else if (shouldPublishEmptyEvent)
    //        {
    //            _logger.LogInformation("Tags left read zone, publishing empty event list");

    //            // Create empty event structure
    //            var lastEvent = _smartReaderTagEventsAbsence.Values.FirstOrDefault();
    //            if (lastEvent != null)
    //            {
    //                smartReaderTagReadEventAggregated = new JObject(lastEvent);
    //                smartReaderTagReadEventAggregated["tag_reads"] = new JArray();

    //                // Clear the batch since the zone is empty
    //                _smartReaderTagEventsListBatch.Clear();
    //                _smartReaderTagEventsAbsence.Clear();

    //                // Set flag to prevent repeated empty events
    //                _emptyEventPublished = true;

    //                _logger.LogDebug("Empty event published, waiting for new tags");
    //            }
    //        }
    //    }
    //}


    //private void ProcessOnChangeUpdates(ref JObject? smartReaderTagReadEventAggregated, JArray smartReaderTagReadEventsArray)
    //{
    //    bool hasBeenUpdated = false;

    //    // Start the stopwatch if not already running
    //    if (!_stopwatchStopTagEventsListBatchOnChange.IsRunning)
    //    {
    //        _stopwatchStopTagEventsListBatchOnChange.Start();
    //    }

    //    //// Remove tags that have left the reading zone and clear absence list
    //    if (_standaloneConfigDTO.tagPresenceTimeoutEnabled == "1" &&
    //        _smartReaderTagEventsAbsence.Count > 0 &&
    //        _smartReaderTagEventsListBatch.Count > 0)
    //    {
    //        _logger.LogWarning("Removing tags due to presence timeout...");
    //        foreach (var tagEpcToRemove in _smartReaderTagEventsAbsence.Keys)
    //        {
    //            _smartReaderTagEventsListBatch.TryRemove(tagEpcToRemove, out _);
    //            hasBeenUpdated = true;
    //        }

    //        //_smartReaderTagEventsAbsence.Clear();
    //    }

    //    if (_mqttPublishingConfiguration.BatchListUpdateTagEventsOnChange)
    //    {
    //        // Check if an empty event should be published immediately when the reading zone becomes empty
    //        if (_smartReaderTagEventsListBatch.Count == 0 &&
    //            _smartReaderTagEventsAbsence.Count > 0 &&
    //            !_emptyEventPublished)
    //        {
    //            _smartReaderTagEventsAbsence.Clear();
    //            PublishEmptyEvent(ref smartReaderTagReadEventAggregated);
    //            return; // Exit early since an empty event has been published
    //        }

    //        // Publish on-change updates immediately if there are updates
    //        if (_smartReaderTagEventsListBatchOnUpdate.Count > 0)
    //        {
    //            _logger.LogInformation($"Processing {_smartReaderTagEventsListBatchOnUpdate.Count} immediately on-change updates");

    //            // Reset empty event flag since new tags are being processed
    //            _emptyEventPublished = false;

    //            // Update the main batch with on-change updates
    //            foreach (var kvp in _smartReaderTagEventsListBatchOnUpdate)
    //            {
    //                if(_smartReaderTagEventsListBatch.ContainsKey(kvp.Key))
    //                {
    //                    _smartReaderTagEventsListBatch[kvp.Key] = kvp.Value;
    //                }
    //                else
    //                {
    //                    _smartReaderTagEventsListBatch.TryAdd(kvp.Key, kvp.Value);
    //                }

    //            }

    //            // Clear the on-change updates list after processing
    //            _smartReaderTagEventsListBatchOnUpdate.Clear();

    //            // Aggregate events for publishing
    //            AggregateEvents(_smartReaderTagEventsListBatch.Values,
    //                            ref smartReaderTagReadEventAggregated,
    //                            smartReaderTagReadEventsArray);

    //            return; // Exit early as on-change updates are published immediately
    //        }
    //    }

    //    // Run periodically batch list publishing
    //    if(_mqttPublishingConfiguration.BatchListPublishingEnabled)
    //    {
    //        // Check if an empty event should be published immediately when the reading zone becomes empty
    //        if (_smartReaderTagEventsListBatch.Count == 0 &&
    //            _smartReaderTagEventsAbsence.Count > 0 &&
    //            !_emptyEventPublished)
    //        {
    //            PublishEmptyEvent(ref smartReaderTagReadEventAggregated);
    //            return; // Exit early since an empty event has been published
    //        }

    //        // Check if it is time to publish batch updates based on the stopwatch
    //        if (_stopwatchStopTagEventsListBatchOnChange.ElapsedMilliseconds >= _mqttPublishingConfiguration.BatchUpdateIntervalMs)
    //        {
    //            // Reset the stopwatch for the next interval
    //            _stopwatchStopTagEventsListBatchOnChange.Reset();

    //            // Aggregate and publish batch updates
    //            AggregateEvents(_smartReaderTagEventsListBatch.Values,
    //                            ref smartReaderTagReadEventAggregated,
    //                            smartReaderTagReadEventsArray);
    //        }
    //    }
    //}

    //private void PublishEmptyEvent(ref JObject? smartReaderTagReadEventAggregated)
    //{
    //    _logger.LogInformation("Tags left read zone, publishing empty event list");

    //    var lastEvent = _smartReaderTagEventsAbsence.Values.FirstOrDefault();
    //    if (lastEvent != null)
    //    {
    //        smartReaderTagReadEventAggregated = new JObject(lastEvent)
    //        {
    //            ["tag_reads"] = new JArray()
    //        };

    //        _smartReaderTagEventsListBatch.Clear();
    //        _smartReaderTagEventsAbsence.Clear();

    //        _emptyEventPublished = true;
    //        _logger.LogDebug("Empty event published, waiting for new tags");
    //    }
    //}

    //private void ProcessRegularIntervalPublishing(ref JObject? smartReaderTagReadEventAggregated,
    //    JArray smartReaderTagReadEventsArray)
    //{
    //    if (!_mqttPublisherStopwatch.IsRunning)
    //    {
    //        _mqttPublisherStopwatch.Start();
    //    }

    //    if (_mqttPublisherStopwatch.ElapsedMilliseconds >= _mqttPublishingConfiguration.PublishIntervalMs)
    //    {
    //        _mqttPublisherStopwatch.Restart();
    //        _logger.LogInformation($"Publishing batch of {_smartReaderTagEventsListBatch.Count} events");

    //        AggregateEvents(_smartReaderTagEventsListBatch.Values,
    //            ref smartReaderTagReadEventAggregated, smartReaderTagReadEventsArray);
    //    }
    //}

    //private async Task PublishEvents(JObject smartReaderTagReadEventAggregated, JArray tagReadsArray)
    //{
    //    try
    //    {
    //        // Skip if no events to publish
    //        if (smartReaderTagReadEventAggregated == null || tagReadsArray == null)
    //        {
    //            _logger.LogDebug("No events to publish - aggregated event or tag reads array is null");
    //            return;
    //        }

    //        // Update the aggregated event with the collected tag reads
    //        smartReaderTagReadEventAggregated["tag_reads"] = tagReadsArray;

    //        // Log publishing attempt
    //        _logger.LogDebug(
    //            "Publishing MQTT event with {TagCount} tag reads",
    //            tagReadsArray.Count);

    //        try
    //        {
    //            // Process and publish the MQTT event
    //            await PublishMqttJsonTagEventDataAsync(smartReaderTagReadEventAggregated);

    //            _logger.LogDebug(
    //                "Successfully published MQTT event with payload: {Payload}",
    //                smartReaderTagReadEventAggregated.ToString(Formatting.None));
    //        }
    //        catch (Exception ex)
    //        {
    //            _logger.LogError(ex,
    //                "Failed to publish MQTT event with {TagCount} tag reads",
    //                tagReadsArray.Count);
    //            throw; // Rethrow to be handled by caller
    //        }
    //    }
    //    catch (Exception ex)
    //    {
    //        _logger.LogError(ex, "Error preparing MQTT event for publishing");
    //        throw; // Rethrow to be handled by caller
    //    }
    //}

    //private async Task ProcessImmediatePublishing()
    //{
    //    JObject? smartReaderTagReadEvent;
    //    JObject? smartReaderTagReadEventAggregated = null;
    //    var smartReaderTagReadEventsArray = new JArray();

    //    if (!_mqttPublisherStopwatch.IsRunning)
    //    {
    //        _mqttPublisherStopwatch.Start();
    //    }

    //    // Process messages until publish interval is reached
    //    while (_mqttPublisherStopwatch.ElapsedMilliseconds < _mqttPublishingConfiguration.PublishIntervalMs)
    //    {
    //        while (_messageQueueTagSmartReaderTagEventMqtt.TryDequeue(out smartReaderTagReadEvent))
    //        {
    //            try
    //            {
    //                ProcessTagEvent(smartReaderTagReadEvent,
    //                    ref smartReaderTagReadEventAggregated,
    //                    smartReaderTagReadEventsArray);
    //            }
    //            catch (Exception ex)
    //            {
    //                _logger.LogError(ex, "Error processing tag event");
    //            }
    //        }
    //        await Task.Delay(10); // Small delay to prevent tight loop
    //    }

    //    _mqttPublisherStopwatch.Restart();

    //    // Publish accumulated events
    //    if (smartReaderTagReadEventAggregated != null && smartReaderTagReadEventsArray.Count > 0)
    //    {
    //        await PublishEvents(smartReaderTagReadEventAggregated, smartReaderTagReadEventsArray);
    //    }
    //}

    //private JArray AggregateTagReads(IEnumerable<JObject> events)
    //{
    //    var tagReadsArray = new JArray();

    //    foreach (var evt in events)
    //    {
    //        if (evt == null) continue;

    //        var tagReads = evt.Property("tag_reads")?.Values();
    //        if (tagReads == null || !tagReads.Any()) continue;

    //        foreach (var tagRead in tagReads)
    //        {
    //            if (tagRead != null && IsValidTagRead(tagRead))
    //            {
    //                tagReadsArray.Add(tagRead);
    //            }
    //        }
    //    }

    //    return tagReadsArray;
    //}

    //private bool IsTimeToPublishBatch()
    //{
    //    var elapsedMs = _mqttPublisherStopwatch.ElapsedMilliseconds;
    //    return elapsedMs >= _mqttPublishingConfiguration.PublishIntervalMs;
    //}

    //private bool IsBatchModeEnabled()
    //{
    //    // For debugging and monitoring
    //    if (_mqttPublishingConfiguration.BatchListEnabled)
    //    {
    //        _logger.LogDebug("Tag Events List Batch Mode is enabled");
    //    }

    //    return _mqttPublishingConfiguration.BatchListEnabled;
    //}

    private void ValidateConfiguration()
    {
        if (_mqttPublishingConfiguration.PublishIntervalMs <= 0)
        {
            _logger.LogWarning("Invalid MQTT publish interval. Using default of 1 second.");
            _mqttPublishingConfiguration.PublishIntervalMs = 1000;
        }

        // More validation as needed...
    }


    //            try
    //            {
    //                if (smartReaderTagReadEventAggregated != null && smartReaderTagReadEventsArray != null &&
    //                    smartReaderTagReadEventsArray.Count > 0)
    //                {
    //                    smartReaderTagReadEventAggregated["tag_reads"] = smartReaderTagReadEventsArray;

    //                    await ProcessMqttJsonTagEventDataAsync(smartReaderTagReadEventAggregated);
    //                }
    //            }
    //            catch (Exception ex)
    //            {
    //                _logger.LogError(ex, "Unexpected error on OnRunPeriodicTagPublisherMqttTasksEvent " + ex.Message);
    //            }

    //        }
    //        catch (Exception ex)
    //        {
    //            _logger.LogError(ex, "Unexpected error on OnRunPeriodicTagPublisherMqttTasksEvent " + ex.Message);
    //        }


    //        _timerTagPublisherMqtt.Enabled = true;
    //        _timerTagPublisherMqtt.Start();
    //#pragma warning restore CS8600, CS8602, CS8604 // Dereference of a possibly null reference.
    //    }

    //private async void OnRunPeriodicTagPublisherMqttTasksEvent(object sender, ElapsedEventArgs e)
    //{
    //    _timerTagPublisherMqtt.Enabled = false;
    //    _timerTagPublisherMqtt.Stop();

    //    try
    //    {
    //        JObject smartReaderTagReadEvent;
    //        while (_messageQueueTagSmartReaderTagEventMqtt.TryDequeue(out smartReaderTagReadEvent))
    //            try
    //            {
    //                await ProcessMqttJsonTagEventDataAsync(smartReaderTagReadEvent);
    //            }
    //            catch (Exception ex)
    //            {
    //                _logger.LogError(ex, "Unexpected error on OnRunPeriodicTagPublisherMqttTasksEvent " + ex.Message);
    //            }
    //    }
    //    catch (Exception)
    //    {
    //    }


    //    _timerTagPublisherMqtt.Enabled = true;
    //    _timerTagPublisherMqtt.Start();
    //}

    private async Task PublishMqttJsonTagEventDataAsync(JObject smartReaderTagEventData)
    {
        try
        {
            var jsonParam = JsonConvert.SerializeObject(smartReaderTagEventData);
#pragma warning disable CS8602 // Dereference of a possibly null reference.
            var mqttDataTopic = $"{_standaloneConfigDTO.mqttTagEventsTopic}";
#pragma warning restore CS8602 // Dereference of a possibly null reference.

            await _mqttService.PublishAsync(mqttDataTopic,
                                jsonParam,
                                _iotDeviceInterfaceClient.MacAddress);


            _logger.LogInformation($"Data sent: {jsonParam}");
            //_ = ProcessGpoErrorPortRecoveryAsync();
        }
        catch (Exception ex)
        {

            _logger.LogInformation(ex, "Unexpected error on ProcessTagEventData " + ex.Message);
            await ProcessGpoErrorPortAsync();
        }
    }

    private async Task ProcessMqttJsonJarrayAsync(JArray eventData)
    {
        try
        {
            var jsonParam = JsonConvert.SerializeObject(eventData);
#pragma warning disable CS8602 // Dereference of a possibly null reference.
            var mqttDataTopic = $"{_standaloneConfigDTO.mqttTagEventsTopic}";
#pragma warning restore CS8602 // Dereference of a possibly null reference.

            await _mqttService.PublishAsync(mqttDataTopic, jsonParam, _iotDeviceInterfaceClient.MacAddress, MqttQualityOfServiceLevel.AtLeastOnce, false);
            _logger.LogInformation($"Data sent: {jsonParam}");
        }
        catch (Exception ex)
        {

            _logger.LogInformation(ex, "Unexpected error on ProcessMqttJsonJarrayAsync " + ex.Message);
            await ProcessGpoErrorPortAsync();
        }
    }

    //private async Task ProcessUdpQueueDataAsync(string ip, int port)
    //{
    //    try
    //    {
    //        JObject smartReaderTagReadEvent;
    //        while (_messageQueueTagSmartReaderTagEventUdpServer.TryDequeue(out smartReaderTagReadEvent))
    //            try
    //            {
    //                await ProcessUdpDataTagEventDataAsync(smartReaderTagReadEvent, ip, port);
    //            }
    //            catch (Exception ex)
    //            {
    //                _logger.LogError(ex, "Unexpected error on OnRunPeriodicTagPublisherUdpServerTasksEvent");
    //            }
    //    }
    //    catch (Exception ex)
    //    {


    //    }
    //}

    //private async void OnRunPeriodicTagPublisherUdpServerTasksEvent(object sender, ElapsedEventArgs e)
    //{
    //    _timerTagPublisherUdpServer.Enabled = false;
    //    _timerTagPublisherUdpServer.Stop();

    //    try
    //    {
    //        JObject smartReaderTagReadEvent;
    //        while (_messageQueueTagSmartReaderTagEventUdpServer.TryDequeue(out smartReaderTagReadEvent))
    //            try
    //            {
    //                await ProcessUdpDataTagEventDataAsync(smartReaderTagReadEvent);
    //            }
    //            catch (Exception ex)
    //            {
    //                _logger.LogError(ex, "Unexpected error on OnRunPeriodicTagPublisherUdpServerTasksEvent");
    //            }
    //    }
    //    catch (Exception ex)
    //    {


    //    }


    //    _timerTagPublisherUdpServer.Enabled = true;
    //    _timerTagPublisherUdpServer.Start();
    //}

    private async Task ProcessUdpDataTagEventDataAsync(JObject smartReaderTagEventData)
#pragma warning disable CS8600, CS8602, CS8604 // Dereference of a possibly null reference.
    {
        try
        {
            if (smartReaderTagEventData == null)
                return;

            _logger.LogInformation("ProcessUdpDataTagEventDataAsync...");

            var sb = new StringBuilder();
            var fieldDelim = ";";
            var lineEnd = "\r\n";

            if (string.Equals("1", _standaloneConfigDTO.fieldDelim, StringComparison.OrdinalIgnoreCase))
                fieldDelim = ",";

            if (string.Equals("2", _standaloneConfigDTO.fieldDelim, StringComparison.OrdinalIgnoreCase))
                fieldDelim = " ";
            if (string.Equals("3", _standaloneConfigDTO.fieldDelim, StringComparison.OrdinalIgnoreCase))
                fieldDelim = "\t";
            if (string.Equals("4", _standaloneConfigDTO.fieldDelim, StringComparison.OrdinalIgnoreCase))
                fieldDelim = " ";

            if (string.Equals("0", _standaloneConfigDTO.lineEnd, StringComparison.OrdinalIgnoreCase)) lineEnd = "\r";
            if (string.Equals("1", _standaloneConfigDTO.lineEnd, StringComparison.OrdinalIgnoreCase)) lineEnd = "\n";
            if (string.Equals("2", _standaloneConfigDTO.lineEnd, StringComparison.OrdinalIgnoreCase)) lineEnd = "\r\n";

            if (smartReaderTagEventData.ContainsKey("tag_reads"))
            {
                var tagReads = smartReaderTagEventData.GetValue("tag_reads").FirstOrDefault();


                var antennaPort = (string)tagReads["antennaPort"];

                if (string.Equals("1", _standaloneConfigDTO.includeAntennaPort, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrEmpty(antennaPort))
                {
                    _ = sb.Append(antennaPort);
                    _ = sb.Append(fieldDelim);
                }

                var antennaZone = (string)tagReads["antennaZone"];
                if (string.Equals("1", _standaloneConfigDTO.includeAntennaZone, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrEmpty(antennaZone))
                {
                    _ = sb.Append(antennaZone);
                    _ = sb.Append(fieldDelim);
                }

                var epc = (string)tagReads["epc"];
                if (!string.IsNullOrEmpty(epc))
                {
                    _ = sb.Append(epc);
                    _ = sb.Append(fieldDelim);
                }

                var firstSeenTimestamp = (string)tagReads["firstSeenTimestamp"];
                if (string.Equals("1", _standaloneConfigDTO.includeFirstSeenTimestamp,
                        StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(firstSeenTimestamp))
                {
                    _ = sb.Append(firstSeenTimestamp);
                    _ = sb.Append(fieldDelim);
                }

                var peakRssi = (string)tagReads["peakRssi"];
                if (string.Equals("1", _standaloneConfigDTO.includePeakRssi, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrEmpty(peakRssi))
                {
                    _ = sb.Append(peakRssi);
                    _ = sb.Append(fieldDelim);
                }

                var tid = (string)tagReads["tid"];
                if (string.Equals("1", _standaloneConfigDTO.includeTid, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrEmpty(tid))
                {
                    _ = sb.Append(tid);
                    _ = sb.Append(fieldDelim);
                }

                var rfPhase = (string)tagReads["rfPhase"];
                if (string.Equals("1", _standaloneConfigDTO.includeRFPhaseAngle, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrEmpty(rfPhase))
                {
                    _ = sb.Append(rfPhase);
                    _ = sb.Append(fieldDelim);
                }

                var rfDoppler = (string)tagReads["frequency"];
                if (string.Equals("1", _standaloneConfigDTO.includeRFDopplerFrequency,
                        StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(rfDoppler))
                {
                    _ = sb.Append(rfDoppler);
                    _ = sb.Append(fieldDelim);
                }

                var rfChannel = (string)tagReads["rfChannel"];
                if (string.Equals("1", _standaloneConfigDTO.includeRFChannelIndex,
                        StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(rfChannel))
                {
                    _ = sb.Append(rfChannel);
                    _ = sb.Append(fieldDelim);
                }


                if (IsGpiEnabled())
                {
                    var gpi1Status = (string)tagReads["gpi1Status"];
                    if (!string.IsNullOrEmpty(gpi1Status))
                    {
                        _ = sb.Append(gpi1Status);
                        _ = sb.Append(fieldDelim);
                    }
                    else
                    {
                        _ = sb.Append("0");
                        _ = sb.Append(fieldDelim);
                    }

                    var gpi2Status = (string)tagReads["gpi2Status"];
                    if (!string.IsNullOrEmpty(gpi2Status))
                    {
                        _ = sb.Append(gpi2Status);
                        _ = sb.Append(fieldDelim);
                    }
                    else
                    {
                        _ = sb.Append("0");
                        _ = sb.Append(fieldDelim);
                    }

                    var gpi3Status = (string)tagReads["gpi3Status"];
                    if (!string.IsNullOrEmpty(gpi3Status))
                    {
                        _ = sb.Append(gpi3Status);
                        _ = sb.Append(fieldDelim);
                    }
                    else
                    {
                        _ = sb.Append("0");
                        _ = sb.Append(fieldDelim);
                    }

                    var gpi4Status = (string)tagReads["gpi4Status"];
                    if (!string.IsNullOrEmpty(gpi4Status))
                    {
                        _ = sb.Append(gpi4Status);
                        _ = sb.Append(fieldDelim);
                    }
                    else
                    {
                        _ = sb.Append("0");
                        _ = sb.Append(fieldDelim);
                    }
                }

                var tagDataKeyName = (string)tagReads["tagDataKeyName"];
                if (string.Equals("1", _standaloneConfigDTO.parseSgtinEnabled, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrEmpty(tagDataKeyName))
                    if (string.Equals("1", _standaloneConfigDTO.parseSgtinIncludeKeyType,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        _ = sb.Append(tagDataKeyName);
                        _ = sb.Append(fieldDelim);
                    }

                var tagDataKey = (string)tagReads["tagDataKey"];
                if (string.Equals("1", _standaloneConfigDTO.parseSgtinEnabled, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrEmpty(tagDataKey))
                {
                    _ = sb.Append(tagDataKey);
                    _ = sb.Append(fieldDelim);
                }

                var tagDataSerial = (string)tagReads["tagDataSerial"];
                if (string.Equals("1", _standaloneConfigDTO.parseSgtinEnabled, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrEmpty(tagDataSerial))
                    if (string.Equals("1", _standaloneConfigDTO.parseSgtinIncludeSerial,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        _ = sb.Append(tagDataSerial);
                        _ = sb.Append(fieldDelim);
                    }

                var tagDataPureIdentity = (string)tagReads["tagDataPureIdentity"];
                if (string.Equals("1", _standaloneConfigDTO.parseSgtinEnabled, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrEmpty(tagDataSerial))
                    if (string.Equals("1", _standaloneConfigDTO.parseSgtinIncludePureIdentity,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        _ = sb.Append(tagDataPureIdentity);
                        _ = sb.Append(fieldDelim);
                    }

                var readerName = (string)smartReaderTagEventData["readerName"];
                if (!string.IsNullOrEmpty(_standaloneConfigDTO.readerName))
                {
                    _ = sb.Append(readerName);
                    _ = sb.Append(fieldDelim);
                }

                var site = (string)smartReaderTagEventData["site"];
                if (!string.IsNullOrEmpty(_standaloneConfigDTO.site))
                {
                    _ = sb.Append(site);
                    _ = sb.Append(fieldDelim);
                }

                if (string.Equals("1", _standaloneConfigDTO.customField1Enabled, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrEmpty(_standaloneConfigDTO.customField1Name))
                {
                    var customField1Value = (string)smartReaderTagEventData[_standaloneConfigDTO.customField1Name];
                    if (!string.IsNullOrEmpty(customField1Value))
                    {
                        _ = sb.Append(customField1Value);
                        _ = sb.Append(fieldDelim);
                    }
                }


                if (string.Equals("1", _standaloneConfigDTO.customField2Enabled, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrEmpty(_standaloneConfigDTO.customField2Name))
                {
                    var customField2Value = (string)smartReaderTagEventData[_standaloneConfigDTO.customField2Name];
                    if (!string.IsNullOrEmpty(customField2Value))
                    {
                        _ = sb.Append(customField2Value);
                        _ = sb.Append(fieldDelim);
                    }
                }

                if (string.Equals("1", _standaloneConfigDTO.customField3Enabled, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrEmpty(_standaloneConfigDTO.customField3Name))
                {
                    var customField3Value = (string)smartReaderTagEventData[_standaloneConfigDTO.customField3Name];
                    if (!string.IsNullOrEmpty(customField3Value))
                    {
                        _ = sb.Append(customField3Value);
                        _ = sb.Append(fieldDelim);
                    }
                }

                if (string.Equals("1", _standaloneConfigDTO.customField4Enabled, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrEmpty(_standaloneConfigDTO.customField4Name))
                {
                    var customField4Value = (string)smartReaderTagEventData[_standaloneConfigDTO.customField4Name];
                    if (!string.IsNullOrEmpty(customField4Value))
                    {
                        _ = sb.Append(customField4Value);
                        _ = sb.Append(fieldDelim);
                    }
                }

                _ = sb.Append(lineEnd);
            }

            var line = sb.ToString();
            line = line.Replace(fieldDelim + lineEnd, lineEnd);

            try
            {
                //try
                //{
                //    _udpSocketServer.Send(line);
                //}
                //catch (Exception)
                //{


                //}

                var udpRemotePort = 0;

                try
                {
                    string? udpRemoteIpAddress = null;


                    if (!string.IsNullOrEmpty(_standaloneConfigDTO.udpRemoteIpAddress))
                        udpRemoteIpAddress = _standaloneConfigDTO.udpRemoteIpAddress;

                    if (!string.IsNullOrEmpty(_standaloneConfigDTO.udpRemotePort))
                        udpRemotePort = int.Parse(_standaloneConfigDTO.udpRemotePort);
                    if (!string.IsNullOrEmpty(udpRemoteIpAddress) && udpRemotePort > 0)
                    {
                        var udpSocket = new UDPSocket();
                        udpSocket.Client(udpRemoteIpAddress, udpRemotePort);
                        udpSocket.Send(line);
                        udpSocket.Close();
                    }
                }
                catch (Exception)
                {
                }

                //_udpSocketServer.Send(line);
#pragma warning disable CS8602 // Dereference of a possibly null reference.
                foreach (var udpClient in _udpSocketServer._udpClients)
                    try
                    {
                        //_udpSocketServer.Client(udpClient.Key, udpClient.Value);
                        var udpSocket = new UDPSocket();
                        if (udpRemotePort > 0)
                            udpSocket.Client(udpClient.Key, udpRemotePort);
                        else
                            udpSocket.Client(udpClient.Key, udpClient.Value);

                        udpSocket.Send(line);
                        udpSocket.Close();
                        //_ = ProcessGpoErrorPortRecoveryAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex,
                            "Unexpected error on ProcessUdpDataTagEventDataAsync for UDP " + udpClient.Key + " " +
                            ex.Message);
                        await ProcessGpoErrorPortAsync();
                        //_messageQueueTagSmartReaderTagEventSocketServerRetry.Enqueue(smartReaderTagEventData);
                    }
#pragma warning restore CS8602 // Dereference of a possibly null reference.
                //_udpSocketServer.Send(line);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error on ProcessUdpDataTagEventDataAsync for UDP " + ex.Message);
                await ProcessGpoErrorPortAsync();
            }
            //foreach (KeyValuePair<string, int> udpClient in _udpClients)
            //{
            //    try
            //    {
            //        //var clientData = client.Split(":");
            //        _udpServer.Send(udpClient.Key, udpClient.Value, Encoding.UTF8.GetBytes(line));
            //    }
            //    catch (Exception ex)
            //    {
            //        _logger.LogError(ex, "Unexpected error on ProcessUdpDataTagEventDataAsync for UDP");

            //        //_messageQueueTagSmartReaderTagEventSocketServerRetry.Enqueue(smartReaderTagEventData);
            //    }
            //}


            //var clients = _udpServer.Endpoints;
            //if (clients != null && clients.Any())
            //{
            //    //string jsonParam = JsonConvert.SerializeObject(smartReaderTagEventData);
            //    foreach (var client in clients)
            //    {

            //    }
            //}
        }
        catch (Exception ex)
        {
            //try
            //{
            //    _messageQueueTagSmartReaderTagEventSocketServerRetry.Enqueue(smartReaderTagEventData);
            //}
            //catch (Exception)
            //{
            //    _logger.LogError(exception, "Unexpected error on ProcessSocketJsonTagEventDataAsync");
            //}
            _logger.LogError(ex, "Unexpected error on ProcessUdpDataTagEventDataAsync");
            await ProcessGpoErrorPortAsync();
            //_logger.LogInformation("Unexpected error on ProcessUdpDataTagEventDataAsync " + ex.Message, SeverityType.Error);
        }
#pragma warning restore CS8600, CS8602, CS8604 // Dereference of a possibly null reference.    
    }

    private async Task ProcessHttpJsonPostTagEventDataAsync(JObject smartReaderTagEventData)
    {
#pragma warning disable CS8600, CS8601, CS8602, CS8603, CS8604, CS8625, CS8629 // Dereference of a possibly null reference.
        try
        {
            string? username = null;
            string? password = null;
            string? httpAuthenticationHeader = null;
            string? httpAuthenticationHeaderValue = null;
            string? bearerToken = null;
            var checkResult = false;
            var jArray = new JArray();
            //jArray.Add(smartReaderTagEventData);
            //string jsonParam = JsonConvert.SerializeObject(smartReaderTagEventData);

            if (!string.IsNullOrEmpty(_standaloneConfigDTO.httpAuthenticationType)
                && string.Equals("BASIC", _standaloneConfigDTO.httpAuthenticationType,
                    StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(_standaloneConfigDTO.httpAuthenticationUsername))
                    username = _standaloneConfigDTO.httpAuthenticationUsername;

                if (!string.IsNullOrEmpty(_standaloneConfigDTO.httpAuthenticationPassword))
                    password = _standaloneConfigDTO.httpAuthenticationPassword;
            }

            if (!string.IsNullOrEmpty(_standaloneConfigDTO.httpAuthenticationType)
                && string.Equals("HEADER", _standaloneConfigDTO.httpAuthenticationType,
                    StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(_standaloneConfigDTO.httpAuthenticationHeader))
                    httpAuthenticationHeader = _standaloneConfigDTO.httpAuthenticationHeader;

                if (!string.IsNullOrEmpty(_standaloneConfigDTO.httpAuthenticationHeaderValue))
                    httpAuthenticationHeaderValue = _standaloneConfigDTO.httpAuthenticationHeaderValue;
            }

            if (!string.IsNullOrEmpty(_standaloneConfigDTO.httpAuthenticationType)
                && string.Equals("BEARER", _standaloneConfigDTO.httpAuthenticationType,
                    StringComparison.OrdinalIgnoreCase)
                && string.Equals("1", _standaloneConfigDTO.httpAuthenticationTokenApiEnabled,
                    StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrEmpty(_standaloneConfigDTO.httpAuthenticationTokenApiValue))
                bearerToken = _standaloneConfigDTO.httpAuthenticationTokenApiValue;

            object dataToPost = smartReaderTagEventData;
            if (!string.IsNullOrEmpty(_standaloneConfigDTO.jsonFormat))
            {
                if (string.Equals("1", _standaloneConfigDTO.jsonFormat,
                  StringComparison.OrdinalIgnoreCase))
                {
                    dataToPost = GetIotInterfaceTagEventReport(smartReaderTagEventData);
                }
            }


            //if (!_httpUtil.PostJsonListBodyDataAsync(_standaloneConfigDTO.httpPostURL, smartReaderTagEventData, username, password, bearerToken, checkResult, null).Result)
            var postResult = _httpUtil.PostJsonObjectDataAsync(_standaloneConfigDTO.httpPostURL, dataToPost, username,
                    password, bearerToken, checkResult, null, httpAuthenticationHeader,
                    httpAuthenticationHeaderValue)
                .Result;
            if (!postResult.StartsWith("20"))
            {
                _ = ProcessGpoErrorPortAsync();
                //if (checkResult)
                //{
                //    ProcessGpoErrorPortAsync();
                //}

                if (!string.IsNullOrEmpty(_standaloneConfigDTO.httpAuthenticationType)
                    && string.Equals("BEARER", _standaloneConfigDTO.httpAuthenticationType,
                        StringComparison.OrdinalIgnoreCase)
                    && string.Equals("1", _standaloneConfigDTO.httpAuthenticationTokenApiEnabled,
                        StringComparison.OrdinalIgnoreCase))
                    try
                    {
                        var updatedBearerToken = _httpUtil
                            .GetBearerTokenUsingJsonBodyAsync(_standaloneConfigDTO.httpAuthenticationTokenApiUrl,
                                _standaloneConfigDTO.httpAuthenticationTokenApiBody).Result;
                        if (!string.IsNullOrEmpty(updatedBearerToken) && updatedBearerToken.Length > 10)
                            _standaloneConfigDTO.httpAuthenticationTokenApiValue = updatedBearerToken;
                    }
                    catch (Exception)
                    {
                        _messageQueueTagSmartReaderTagEventHttpPostRetry.Enqueue(smartReaderTagEventData);
                    }
            }
            //else
            //{
            //    _ = ProcessGpoErrorPortRecoveryAsync();
            //}

        }
        catch (Exception ex)
        {
            try
            {
                _messageQueueTagSmartReaderTagEventHttpPostRetry.Enqueue(smartReaderTagEventData);
            }
            catch (Exception)
            {
            }

            _logger.LogError(ex, "Unexpected error on ProcessTagEventData " + ex.Message);
            await ProcessGpoErrorPortAsync();
        }

        try
        {
            _httpTimerStopwatch.Restart();
        }
        catch (Exception ex)
        {
            Console.WriteLine("Unexpected error on ProcessTagEventData restarting _timerStopwatch" + ex.Message);
            _logger.LogError(ex, "Unexpected error on ProcessTagEventData restarting _timerStopwatch" + ex.Message);
            await ProcessGpoErrorPortAsync();
        }
#pragma warning restore CS8600, CS8601, CS8602, CS8603, CS8604, CS8625, CS8629 // Dereference of a possibly null reference.
    }

    //private async Task ProcessSocketJsonTagEventDataAsync(JObject smartReaderTagEventData)
    //{
    //    // Validate input
    //    if (smartReaderTagEventData == null)
    //    {
    //        _logger.LogWarning("Received null event data for socket processing");
    //        return;
    //    }

    //    // Configure timeouts and retry parameters
    //    const int SendTimeoutMs = 5000; // 5 second timeout for sends
    //    const int MaxRetries = 3;
    //    const int RetryDelayMs = 1000; // 1 second between retries

    //    try
    //    {
    //        // Extract data once to avoid repeated processing
    //        var line = ExtractLineFromJsonObject(smartReaderTagEventData, _standaloneConfigDTO, _logger);
    //        if (string.IsNullOrEmpty(line))
    //        {
    //            _logger.LogWarning("Empty line extracted from event data");
    //            return;
    //        }

    //        byte[] messageBytes = Encoding.UTF8.GetBytes(line);

    //        // Validate socket server state
    //        if (_socketServer == null || !_socketServer.IsListening)
    //        {
    //            _logger.LogWarning("Socket server not available, attempting restart...");
    //            try
    //            {
    //                TryRestartSocketServer();
    //                if (_socketServer == null || !_socketServer.IsListening)
    //                {
    //                    _logger.LogError("Failed to restart socket server");
    //                    // Requeue message rather than dropping it
    //                    if (_messageQueueTagSmartReaderTagEventSocketServer.Count < 1000)
    //                    {
    //                        _messageQueueTagSmartReaderTagEventSocketServer.Enqueue(smartReaderTagEventData);
    //                        _logger.LogInformation("Requeued message due to server unavailability");
    //                    }
    //                    return;
    //                }
    //            }
    //            catch (Exception ex)
    //            {
    //                _logger.LogError(ex, "Error restarting socket server");
    //                return;
    //            }
    //        }

    //        // Get current clients
    //        var clients = _socketServer.GetClients();
    //        if (clients == null || !clients.Any())
    //        {
    //            _logger.LogInformation("No connected clients to receive message");
    //            return;
    //        }

    //        // Process each client with retries
    //        foreach (var client in clients)
    //        {
    //            int retryCount = 0;
    //            bool sendSuccess = false;

    //            while (!sendSuccess && retryCount < MaxRetries)
    //            {
    //                try
    //                {
    //                    using var cts = new CancellationTokenSource(SendTimeoutMs);

    //                    // Wrap the sync Send in a task to support timeout
    //                    await Task.Run(() => {
    //                        _socketServer.Send(client, messageBytes);
    //                    }, cts.Token);

    //                    sendSuccess = true;
    //                    _logger.LogDebug($"Successfully sent message to client after {retryCount} retries");
    //                }
    //                catch (OperationCanceledException)
    //                {
    //                    _logger.LogWarning($"Send operation timed out for client after {SendTimeoutMs}ms");
    //                    retryCount++;
    //                    if (retryCount < MaxRetries)
    //                    {
    //                        await Task.Delay(RetryDelayMs);
    //                    }
    //                }
    //                catch (Exception ex)
    //                {
    //                    _logger.LogError(ex, $"Error sending to client (attempt {retryCount + 1}/{MaxRetries})");
    //                    retryCount++;
    //                    if (retryCount < MaxRetries)
    //                    {
    //                        await Task.Delay(RetryDelayMs);
    //                    }
    //                }
    //            }

    //            if (!sendSuccess)
    //            {
    //                _logger.LogError($"Failed to send message to client after {MaxRetries} attempts");
    //                await ProcessGpoErrorPortAsync();

    //                // Consider removing failed client
    //                try
    //                {
    //                    _socketServer.DisconnectClient(client);
    //                    _logger.LogInformation("Disconnected failed client");
    //                }
    //                catch (Exception ex)
    //                {
    //                    _logger.LogError(ex, "Error disconnecting failed client");
    //                }
    //            }
    //        }
    //    }
    //    catch (Exception ex)
    //    {
    //        _logger.LogError(ex, "Fatal error in socket processing");
    //        await ProcessGpoErrorPortAsync();

    //        // Requeue message if possible
    //        if (_messageQueueTagSmartReaderTagEventSocketServer.Count < 1000)
    //        {
    //            _messageQueueTagSmartReaderTagEventSocketServer.Enqueue(smartReaderTagEventData);
    //            _logger.LogInformation("Requeued message after processing error");
    //        }
    //    }
    //}

    private async Task ProcessUsbDriveJsonTagEventDataAsync(JObject smartReaderTagEventData)
    {
        try
        {
            if (smartReaderTagEventData == null)
                return;

            var line = SmartReaderJobs.Utils.Utils.ExtractLineFromJsonObject(smartReaderTagEventData, _standaloneConfigDTO, _logger);
            try
            {
                // If directory does not exist, don't even try   
                if (Directory.Exists(R700UsbDrivePath))
                {
                    var filePath = R700UsbDrivePath;

                    var additionalFilename = DateTime.Now.ToString("yyyyMMddHH", CultureInfo.InvariantCulture) + ".txt";

                    var filename = filePath + "/" + additionalFilename;

                    File.AppendAllText(filename, line);
                    _logger.LogInformation("### Publisher USB Drive >>> " + filename);
                    _logger.LogInformation("### Publisher USB Drive >>> " + line);
                    //_ = ProcessGpoErrorPortRecoveryAsync();
                }
                else
                {
                    _logger.LogWarning("### Publisher USB Drive >>> path " + R700UsbDrivePath + " not found.");
                    await ProcessGpoErrorPortAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error on ProcessUsbDriveJsonTagEventDataAsync " + ex.Message);
                await ProcessGpoErrorPortAsync();

                //_messageQueueTagSmartReaderTagEventSocketServerRetry.Enqueue(smartReaderTagEventData);
            }
        }
        catch (Exception ex)
        {
            //try
            //{
            //    _messageQueueTagSmartReaderTagEventSocketServerRetry.Enqueue(smartReaderTagEventData);
            //}
            //catch (Exception)
            //{
            //    _logger.LogError(exception, "Unexpected error on ProcessSocketJsonTagEventDataAsync");
            //}
            _logger.LogError(ex, "Unexpected error on ProcessUsbDriveJsonTagEventDataAsync " + ex.Message,
                SeverityType.Error);
            await ProcessGpoErrorPortAsync();
        }
    }

    private async void OnRunPeriodicTasksJobManagerEvent(object sender, ElapsedEventArgs e)
    {
        DisablePeriodicTasks();

        try
        {
            if (!ValidateConfig()) return;

            await UpdateReaderSerialAsync();
            await _configurationService.UpdateLicenseAndConfigAsync(DeviceId, _plugins);
            await UpdateReaderStatusAsync();
            await ProcessReaderCommandsAsync();

            if (!IsOnPause())
            {
                await HandleHttpPostTimeoutAsync();
                await RefreshBearerTokenAsync();

            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Critical error in OnRunPeriodicTasksJobManagerEvent: {Message}", ex.Message);
        }
        finally
        {
            EnablePeriodicTasks();
        }
    }

    private void DisablePeriodicTasks()
    {
        _timerPeriodicTasksJob.Enabled = false;
        _timerPeriodicTasksJob.Stop();
    }

    private void EnablePeriodicTasks()
    {
        _timerPeriodicTasksJob.Enabled = true;
        _timerPeriodicTasksJob.Start();
    }

    private bool ValidateConfig()
    {
        if (_standaloneConfigDTO == null)
        {
            _logger.LogWarning("Configuration DTO is null. Exiting periodic tasks.");
            return false;
        }
        return true;
    }

    private async Task HandleHttpPostTimeoutAsync()
    {
        if (_messageQueueTagSmartReaderTagEventHttpPost.Count > 0 && _httpTimerStopwatch.Elapsed.Minutes > 10)
        {
            _logger.LogInformation("HTTP POST timeout detected. Restarting process...");
            await RestartApplicationAsync();
        }
    }

    private async Task RestartApplicationAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(2));
        var process = Process.GetCurrentProcess();
        process.StartInfo.WorkingDirectory = Directory.GetCurrentDirectory();
        _ = Process.Start(process.MainModule?.FileName ?? throw new InvalidOperationException("Main module not found"));
        Environment.Exit(1); // Exit the current process
    }

    private async Task RefreshBearerTokenAsync()
    {
        if (string.Equals("BEARER", _standaloneConfigDTO.httpAuthenticationType, StringComparison.OrdinalIgnoreCase) &&
            string.Equals("1", _standaloneConfigDTO.httpAuthenticationTokenApiEnabled, StringComparison.OrdinalIgnoreCase) &&
            _stopwatchBearerToken.IsRunning && _stopwatchBearerToken.Elapsed.Minutes > 30)
        {
            _stopwatchBearerToken.Reset();

            try
            {
                var updatedBearerToken = await _httpUtil.GetBearerTokenUsingJsonBodyAsync(
                    _standaloneConfigDTO.httpAuthenticationTokenApiUrl,
                    _standaloneConfigDTO.httpAuthenticationTokenApiBody);

                if (!string.IsNullOrEmpty(updatedBearerToken))
                {
                    _standaloneConfigDTO.httpAuthenticationTokenApiValue = updatedBearerToken;
                    _logger.LogInformation("Bearer token refreshed successfully.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to refresh bearer token.");
            }
            finally
            {
                _stopwatchBearerToken.Start();
            }
        }
    }




    private async Task ProcessReaderCommandsAsync()
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RuntimeDb>();

        var commands = await dbContext.ReaderCommands.OrderBy(f => f.Timestamp).ToListAsync();

        foreach (var command in commands)
        {
            try
            {
                _logger.LogDebug($"Processing Reader Command {command.Id}");
                await ExecuteCommandAsync(command);
                _ = dbContext.ReaderCommands.Remove(command);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute command: {CommandId}", command.Id);
            }
        }

        _ = await dbContext.SaveChangesAsync();
    }

    private async Task ExecuteCommandAsync(ReaderCommands command)
    {
        if (command == null)
        {
            _logger.LogWarning("Attempted to execute a null command.");
            return;
        }

        _logger.LogInformation("Processing command: {CommandId}, Value: {CommandValue}", command.Id, command.Value);

        try
        {
            switch (command.Id.ToUpperInvariant())
            {
                case "START_INVENTORY":
                    await HandleStartInventoryAsync();
                    break;

                case "STOP_INVENTORY":
                    await HandleStopInventoryAsync();
                    break;

                case "START_PRESET":
                    await HandleStartPresetAsync();
                    break;

                case "STOP_PRESET":
                    await HandleStopPresetAsync();
                    break;

                case string id when id.StartsWith("SET_GPO_"):
                    await HandleSetGpoCommandAsync(command);
                    break;

                case "CLEAN_EPC_SOFTWARE_HISTORY_FILTERS":
                    HandleCleanEpcSoftwareHistoryFilters();
                    break;

                case "UPGRADE_SYSTEM_IMAGE":
                    HandleUpgradeSystemImage(command);
                    break;

                case "MODE_COMMAND":
                    HandleModeCommand(command.Value);
                    break;

                default:
                    _logger.LogWarning("Unknown command: {CommandId}", command.Id);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while executing command: {CommandId}", command.Id);
            throw; // Optionally rethrow if it should propagate
        }
    }

    private async Task HandleStartInventoryAsync()
    {
        _logger.LogInformation("Starting inventory tasks...");
        try
        {
            var pauseRequestPath = Path.Combine("/customer", "pause-request.txt");
            if (File.Exists(pauseRequestPath))
            {
                File.Delete(pauseRequestPath);
            }
            await StartTasksAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start inventory.");
        }
    }

    private async Task HandleStopInventoryAsync()
    {
        _logger.LogInformation("Stopping inventory tasks...");
        try
        {
            var pauseRequestPath = Path.Combine("/customer", "pause-request.txt");
            File.WriteAllText(pauseRequestPath, "pause");

            await StopPresetAsync();
            await StopTasksAsync();

            _logger.LogInformation("Inventory tasks stopped.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop inventory.");
        }
    }

    private async Task HandleStartPresetAsync()
    {
        _logger.LogInformation("Starting preset tasks...");
        try
        {
            var pauseRequestPath = Path.Combine("/customer", "pause-request.txt");
            if (File.Exists(pauseRequestPath))
            {
                File.Delete(pauseRequestPath);
            }
            await StartPresetAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start preset tasks.");
        }
    }

    private async Task HandleStopPresetAsync()
    {
        _logger.LogInformation("Stopping preset tasks...");
        try
        {
            var pauseRequestPath = Path.Combine("/customer", "pause-request.txt");
            File.WriteAllText(pauseRequestPath, "pause");

            await StopPresetAsync();
            _logger.LogInformation("Preset tasks stopped.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop preset tasks.");
        }
    }

    private async Task HandleSetGpoCommandAsync(ReaderCommands command)
    {
        _logger.LogInformation("Setting GPO port...");
        try
        {
            var parts = command.Id.Split('_');
            if (parts.Length < 3)
            {
                _logger.LogWarning("Invalid SET_GPO command format: {CommandId}", command.Id);
                return;
            }

            if (int.TryParse(parts[2], out var port) && bool.TryParse(command.Value, out var status))
            {
                await SetGpoPortAsync(port, status);
                _logger.LogInformation("GPO port {Port} set to {Status}.", port, status);
            }
            else
            {
                _logger.LogWarning("Invalid port or status in SET_GPO command: {CommandValue}", command.Value);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set GPO port.");
        }
    }



    private void HandleCleanEpcSoftwareHistoryFilters()
    {
        _logger.LogInformation("Cleaning up EPC software history filters...");
        try
        {
            _readEpcs.Clear();
            _softwareFilterReadCountTimeoutDictionary.Clear();
            _knowTagsForSoftwareFilterWindowSec.Clear();
            _logger.LogInformation("EPC software history filters cleaned.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clean EPC software history filters.");
        }
    }

    private void HandleUpgradeSystemImage(ReaderCommands command)
    {
        _logger.LogInformation("Upgrading system image...");
        try
        {
            var payload = JsonConvert.DeserializeObject<Dictionary<string, object>>(command.Value);
            if (payload == null)
            {
                _logger.LogWarning("Invalid UPGRADE_SYSTEM_IMAGE command payload.");
                return;
            }

            var remoteUrl = payload.GetValueOrDefault("remoteUrl", string.Empty)?.ToString();
            var timeout = payload.GetValueOrDefault("timeoutInMinutes", 3);
            var maxRetries = payload.GetValueOrDefault("maxRetries", 1);

            if (string.IsNullOrEmpty(remoteUrl))
            {
                _logger.LogWarning("Remote URL is missing in UPGRADE_SYSTEM_IMAGE command.");
                return;
            }

            UpgradeSystemImage(remoteUrl, Convert.ToInt64(timeout), Convert.ToInt64(maxRetries));
            _logger.LogInformation("System image upgraded successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upgrade system image.");
        }
    }

    private void HandleModeCommand(string commandValue)
    {
        _logger.LogInformation("Processing mode command: {CommandValue}", commandValue);
        try
        {
            _ = ProcessMqttModeJsonCommand(commandValue);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process mode command.");
        }
    }

    private async Task UpdateReaderStatusAsync()
    {
        //_logger.LogInformation("Starting UpdateReaderStatusAsync...");

        try
        {
            // Retrieve status data
            var statusData = await GetReaderStatusAsync();
            if (statusData == null || !statusData.Any())
            {
                _logger.LogWarning("No status data retrieved from the reader.");
                return;
            }

            //_logger.LogInformation("Reader status data retrieved successfully.");

            // Create a database scope
            using var scope = Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<RuntimeDb>();

            try
            {
                // Check existing status in the database
                var existingStatus = await dbContext.ReaderStatus.FindAsync("READER_STATUS");
                if (existingStatus != null)
                {
                    existingStatus.Value = JsonConvert.SerializeObject(statusData);
                    _ = dbContext.ReaderStatus.Update(existingStatus);
                    //_logger.LogInformation("Reader status updated in the database.");
                }
                else
                {
                    _ = dbContext.ReaderStatus.Add(new ReaderStatus
                    {
                        Id = "READER_STATUS",
                        Value = JsonConvert.SerializeObject(statusData)
                    });
                    //_logger.LogInformation("New reader status added to the database.");
                }

                // Save changes to the database
                _ = await dbContext.SaveChangesAsync();
                //_logger.LogInformation("Database changes committed successfully.");
            }
            catch (Exception dbEx)
            {
                _logger.LogError(dbEx, "An error occurred while updating the reader status in the database.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while updating the reader status.");
        }
    }


    private async Task UpdateReaderSerialAsync()
    {
        try
        {
            //_logger.LogInformation("Starting UpdateReaderSerialAsync...");

            // Fetch serial data from the reader
            var serialData = await GetSerialAsync();
            if (serialData == null || !serialData.Any())
            {
                _logger.LogWarning("No serial data received from the reader.");
                return;
            }

            var serialFromReader = serialData.FirstOrDefault()?.SerialNumber;

            if (string.IsNullOrEmpty(serialFromReader))
            {
                _logger.LogWarning("Serial number from reader is null or empty.");
                return;
            }

            //_logger.LogInformation("Retrieved serial number: {Serial}", serialFromReader);
            await _configurationService.SaveSerialToDbAsync(serialFromReader);

            // Update the configuration DTO
            if (_standaloneConfigDTO == null)
            {
                _logger.LogError("StandaloneConfigDTO is null. Cannot update reader serial.");
                return;

            }

            if (!string.Equals(_standaloneConfigDTO.readerSerial, serialFromReader, StringComparison.OrdinalIgnoreCase))
            {
                _standaloneConfigDTO.readerSerial = serialFromReader;

                try
                {
                    _configurationService.SaveConfigDtoToDb(_standaloneConfigDTO);
                    _logger.LogInformation("Reader serial updated in configuration database: {Serial}", serialFromReader);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to save updated reader serial to the database.");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while updating the reader serial.");
        }

    }


    private async Task SendHeartbeatAsync()
    {
        if (IsHeartbeatEnabled())
        {
            await ProcessKeepalive();
        }
    }


    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {

            await InitializeServicesAsync();
            await ConfigurePluginsAsync();
            await ConfigureCommunicationChannelsAsync();
            await ConfigureAndStartTimersAsync();
            await StartMainProcessingLoopAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            await HandleFatalErrorAsync(ex, stoppingToken);
        }
    }

    private async Task InitializeServicesAsync()
    {
        _standaloneConfigDTO = _configurationService.LoadConfig();
        await InitializeReaderDeviceAsync();
        await ConfigureRegionalParametersAsync();
        InitializeProxySettings();
        InitializeMqttTopics();
        await ConfigureAntennaHubAsync();
        InitializePositioningConfiguration();
        await UpdateReaderConfigurationAsync();
        await InitializeAuthenticationAsync();
        await ValidateLicenseAsync();
    }


    private async Task InitializeReaderDeviceAsync()
    {
        var readPointIpAddress = _configuration.GetValue<string>("ReaderInfo:Address");

        // Get the value based on the build configuration
        if ("Debug".Equals(_buildConfiguration))
        {
            readPointIpAddress = _configuration.GetValue<string>("ReaderInfo:DebugAddress") ?? readPointIpAddress;
        }
        _logger.LogInformation("Using initial ip address to get serial: " + readPointIpAddress, SeverityType.Debug);

        _readerAddress = readPointIpAddress;

        if (_iotDeviceInterfaceClient == null)
        {
            if (_readerAddress != null)
            {
                InitR700IoTClient();

                await ConfigureReaderInterfaceAsync();
            }
            else
            {
                throw new InvalidOperationException("Reader address cannot be null when initializing IoT reader interface.");
            }
        }
    }

    private void InitializeProxySettings()
    {
        _standaloneConfigDTO = StandaloneConfigDTO.CleanupUrlEncoding(_standaloneConfigDTO);
        if (_standaloneConfigDTO == null) return;

        _configurationService.SaveConfigDtoToDb(_standaloneConfigDTO);
        _proxyAddress = _standaloneConfigDTO.networkProxy;
        _proxyPort = !string.IsNullOrEmpty(_standaloneConfigDTO?.networkProxyPort)
                        ? int.Parse(_standaloneConfigDTO.networkProxyPort)
                        : 8080; // Default proxy port
    }

    private void InitializeMqttTopics()
    {
        if (_standaloneConfigDTO == null) return;

        var topics = new Dictionary<string, string>
   {
       { "mqttTagEventsTopic", _standaloneConfigDTO.mqttTagEventsTopic },
       { "mqttManagementEventsTopic", _standaloneConfigDTO.mqttManagementEventsTopic },
       { "mqttMetricEventsTopic", _standaloneConfigDTO.mqttMetricEventsTopic },
       { "mqttManagementCommandTopic", _standaloneConfigDTO.mqttManagementCommandTopic },
       { "mqttManagementResponseTopic", _standaloneConfigDTO.mqttManagementResponseTopic },
       { "mqttControlCommandTopic", _standaloneConfigDTO.mqttControlCommandTopic },
       { "mqttControlResponseTopic", _standaloneConfigDTO.mqttControlResponseTopic }
   };

        foreach (var topic in topics.Where(t => !string.IsNullOrEmpty(t.Value)))
        {
            var propertyInfo = typeof(StandaloneConfigDTO).GetProperty(topic.Key);
            if (propertyInfo != null && topic.Value.Contains("{{deviceId}}"))
            {
                propertyInfo.SetValue(_standaloneConfigDTO,
                    topic.Value.Replace("{{deviceId}}", _standaloneConfigDTO.readerName));
            }
        }

        ConfigFileHelper.SaveFile(_standaloneConfigDTO);
        _configurationService.SaveConfigDtoToDb(_standaloneConfigDTO);
    }

    private void InitializePositioningConfiguration()
    {
        if (!IsPositioningEnabled()) return;

        ConfigurePositioningAntennaPorts();
        ConfigurePositioningHeaders();
        ConfigurePositioningExpiration();
    }

    private bool IsPositioningEnabled() =>
   _standaloneConfigDTO?.positioningEpcsEnabled?.Equals("1", StringComparison.OrdinalIgnoreCase) ?? false;

    private void ConfigurePositioningAntennaPorts()
    {
        if (string.IsNullOrEmpty(_standaloneConfigDTO?.positioningAntennaPorts)) return;

        try
        {
            var ports = _standaloneConfigDTO.positioningAntennaPorts
                .Split(",")
                .Select(p => int.Parse(p.Trim()));
            _positioningAntennaPorts.AddRange(ports);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error configuring positioning antenna ports");
        }
    }

    private void ConfigurePositioningHeaders()
    {
        if (string.IsNullOrEmpty(_standaloneConfigDTO?.positioningEpcsHeaderList)) return;

        _positioningEpcsHeaderList.AddRange(_standaloneConfigDTO.positioningEpcsHeaderList.Split(","));
    }

    private void ConfigurePositioningExpiration()
    {
        if (string.IsNullOrEmpty(_standaloneConfigDTO?.positioningExpirationInSec)) return;

        if (int.TryParse(_standaloneConfigDTO.positioningExpirationInSec.Trim(), out var expiration))
        {
            _positioningExpirationInSec = expiration;
        }
    }

    private async Task ConfigureRegionalParametersAsync()
    {
        if (_iotDeviceInterfaceClient == null)
        {
            await InitializeReaderDeviceAsync();
        }

        var systemInfo = await _iotDeviceInterfaceClient.GetSystemInfoAsync();
        var readerRegionInfo = await _iotDeviceInterfaceClient.GetSystemRegionInfoAsync();
        await ConfigurePowerAndTransmitLevels(systemInfo, readerRegionInfo.OperatingRegion);
        ConfigureRegionSpecificSettings(readerRegionInfo);
    }

    private async Task ConfigurePowerAndTransmitLevels(SystemInfo systemInfo, string operatingRegion)
    {
        var systemPower = await _iotDeviceInterfaceClient.GetSystemPowerAsync();
        bool isPoePlus = false;
        if (systemPower.PowerSource.Equals(Impinj.Atlas.PowerSource.Poeplus))
        {
            isPoePlus = true;
        }
        var txTable = Utils.GetDefaultTxTable(systemInfo.ProductModel, isPoePlus, operatingRegion);

        if (_standaloneConfigDTO == null) return;

        var txPowers = _standaloneConfigDTO.transmitPower.Split(",")
            .Select(p => int.TryParse(p, out var power) ? Math.Min(power, txTable.Max()) : txTable.Max())
            .ToList();

        _standaloneConfigDTO.transmitPower = string.Join(",", txPowers);
        _logger.LogInformation($"Configuring transmit power: {_standaloneConfigDTO.transmitPower}");
        _configurationService.SaveConfigDtoToDb(_standaloneConfigDTO);
    }

    private void ConfigureRegionSpecificSettings(Impinj.Atlas.RegionInfo regionInfo)
    {
        if (_standaloneConfigDTO == null || string.IsNullOrEmpty(regionInfo?.OperatingRegion)) return;

        _standaloneConfigDTO.operatingRegion = regionInfo.OperatingRegion.ToUpper();

        var isEtsiRegion = regionInfo.OperatingRegion.ToUpper().Contains("ETSI");
        if ((isEtsiRegion && _standaloneConfigDTO.readerMode == "4") ||
            (!isEtsiRegion && _standaloneConfigDTO.readerMode == "5"))
        {
            _standaloneConfigDTO.readerMode = "1002";
        }

        _configurationService.SaveConfigDtoToDb(_standaloneConfigDTO);
    }

    private async Task ConfigureReaderInterfaceAsync()
    {
        try
        {
            if (_iotDeviceInterfaceClient == null)
            {
                throw new InvalidOperationException("IoT device interface client not initialized");
            }
            try
            {
                try
                {
                    if (_standaloneConfigDTO == null)
                    {
                        _standaloneConfigDTO = _configurationService.LoadConfig();
                    }
                    _standaloneConfigDTO.readerSerial = _iotDeviceInterfaceClient.GetStatusAsync().Result.SerialNumber;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error retrieving SN");
                }


                if (_standaloneConfigDTO.readerName.Equals("impinj-xx-xx-xx"))
                {
                    _standaloneConfigDTO.readerName = "impinj" + _iotDeviceInterfaceClient.MacAddress.Replace("-", String.Empty).Replace(":", String.Empty);
                }

            }
            catch (Exception)
            {


            }



            // Get system info to verify connection and get device details
            var systemInfo = await _iotDeviceInterfaceClient.GetSystemInfoAsync();
            if (systemInfo == null)
            {
                _logger.LogError("Failed to retrieve system information from reader");
                await ProcessGpoErrorPortAsync();
                return;
            }

            // Store device identification information
            DeviceId = systemInfo.SerialNumber;
            if (!string.IsNullOrEmpty(systemInfo.SerialNumber) && systemInfo.SerialNumber.StartsWith("370"))
            {
                DeviceIdWithDashes = string.Format("{0:###-##-##-####}", Convert.ToInt64(systemInfo.SerialNumber));
            }

            // Set up event handlers if not in management-only mode
            if (_standaloneConfigDTO != null &&
                (string.IsNullOrEmpty(_standaloneConfigDTO.smartreaderEnabledForManagementOnly) ||
                 !string.Equals("1", _standaloneConfigDTO.smartreaderEnabledForManagementOnly)))
            {
                _iotDeviceInterfaceClient.TagInventoryEvent += OnTagInventoryEvent!;
                _iotDeviceInterfaceClient.InventoryStatusEvent += OnInventoryStatusEvent!;
                _iotDeviceInterfaceClient.GpiTransitionEvent += OnGpiTransitionEvent!;
            }

            // Check for existing presets and clean up if necessary
            try
            {
                var existingPresets = await _iotDeviceInterfaceClient.GetReaderInventoryPresetListAsync();
                if (existingPresets?.Contains("SmartReader") == true)
                {
                    await _iotDeviceInterfaceClient.DeleteInventoryPresetAsync("SmartReader");
                    _logger.LogInformation("Cleaned up existing SmartReader preset");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Non-critical error while cleaning up existing presets");
            }

            // Initialize GPI port states
            if (!_gpiPortStates.ContainsKey(0)) _ = _gpiPortStates.TryAdd(0, false);
            if (!_gpiPortStates.ContainsKey(1)) _ = _gpiPortStates.TryAdd(1, false);

            // Initialize the HTTP Stream
            try
            {
                _logger.LogInformation("Initializing the HTTP Stream setup.");
                var httpStreamConfig = new HttpStreamConfig
                {
                    EventBufferSize = 0,
                    EventPerSecondLimit = 50,
                    EventAgeLimitMinutes = 1,
                    KeepAliveIntervalSeconds = 1
                };
                await _iotDeviceInterfaceClient.UpdateHttpStreamConfigAsync(httpStreamConfig);

            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Non-critical error while initializing the HTTP Stream setup.");
            }

            // Verify network connectivity
            if (!_iotDeviceInterfaceClient.IsNetworkConnected)
            {
                _logger.LogWarning("Reader network connection not established");
                await ProcessGpoErrorPortAsync();
                await ProcessGpoErrorNetworkPortAsync(true);
            }
            else
            {
                _logger.LogInformation("Reader interface successfully configured");

                // Publish initial status if MQTT is enabled
                if (IsMqttEnabled())
                {
                    var mqttManagementEvents = new Dictionary<string, object>
                    {
                        { "smartreader-status", "initialized" },
                        { "readerName", _standaloneConfigDTO.readerName },
                        { "mac", _iotDeviceInterfaceClient.MacAddress },
                        { "timestamp", Utils.CSharpMillisToJavaLongMicroseconds(DateTime.Now) }
                    };
                    await _mqttService.PublishMqttManagementEventAsync(mqttManagementEvents);
                }
            }

            // Start stopwatch for monitoring
            _stopwatchLastIddleEvent.Start();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to configure reader interface");
            await ProcessGpoErrorPortAsync();
            throw;
        }
    }

    private async Task ConfigureAntennaHubAsync()
    {
        // Check if the device interface client is available
        if (_iotDeviceInterfaceClient == null)
        {
            _logger.LogError("IoT device interface client not initialized");
            return;
        }

        var hubInfo = await _iotDeviceInterfaceClient.GetSystemAntennaHubInfoAsync();
        if (hubInfo?.Status == AntennaHubInfoStatus.Enabled)
        {
            ConfigureEnabledAntennaHub();
        }
        else if (hubInfo?.Status == AntennaHubInfoStatus.Disabled)
        {
            ConfigureDisabledAntennaHub();
        }
    }

    private void ConfigureEnabledAntennaHub()
    {
        if (_standaloneConfigDTO?.antennaPorts?.Split(",").Length < 32)
        {
            var config = BuildAntennaConfig(32);
            SaveAntennaConfiguration(config);
        }
    }

    private void ConfigureDisabledAntennaHub()
    {
        if (_standaloneConfigDTO?.antennaPorts?.Split(",").Length > 4)
        {
            var config = BuildAntennaConfig(4);
            SaveAntennaConfiguration(config);
        }
    }

    private record AntennaConfig(
        string Ports,
        string States,
        string Zones,
        string Power,
        string Sensitivity
    );

    private AntennaConfig BuildAntennaConfig(int portCount)
    {
        var ports = new List<string>();
        var states = new List<string>();
        var zones = new List<string>();
        var power = new List<string>();
        var sensitivity = new List<string>();

        for (var i = 1; i < portCount + 1; i++)
        {
            ports.Add(i.ToString());
            states.Add(i == 1 ? "1" : "0");
            zones.Add($"ANT{i}");
            power.Add("3000");
            sensitivity.Add("-92");
        }

        return new AntennaConfig(
            string.Join(",", ports),
            string.Join(",", states),
            string.Join(",", zones),
            string.Join(",", power),
            string.Join(",", sensitivity)
        );
    }

    private void SaveAntennaConfiguration(AntennaConfig config)
    {
        _standaloneConfigDTO.antennaPorts = config.Ports;
        _standaloneConfigDTO.antennaStates = config.States;
        _standaloneConfigDTO.antennaZones = config.Zones;
        _standaloneConfigDTO.transmitPower = config.Power;
        _standaloneConfigDTO.receiveSensitivity = config.Sensitivity;

        ConfigFileHelper.SaveFile(_standaloneConfigDTO);
        _configurationService.SaveConfigDtoToDb(_standaloneConfigDTO);
    }

    private async Task UpdateReaderConfigurationAsync()
    {
        try
        {
            // Check if the device interface client is available
            if (_iotDeviceInterfaceClient == null)
            {
                _logger.LogError("IoT device interface client not initialized");
                return;
            }

            // Fetch the current system configuration
            var systemInfo = await _iotDeviceInterfaceClient.GetSystemInfoAsync();
            if (systemInfo == null)
            {
                _logger.LogError("Failed to retrieve system information");
                return;
            }

            // Update standalone configuration with system info if needed
            if (_standaloneConfigDTO != null)
            {
                _standaloneConfigDTO.readerSerial = systemInfo.SerialNumber;

                if (!string.IsNullOrEmpty(systemInfo.SerialNumber) && systemInfo.SerialNumber.StartsWith("370"))
                {
                    var serialNumber = systemInfo.SerialNumber;
                    DeviceIdWithDashes = string.Format("{0:###-##-##-####}", Convert.ToInt64(serialNumber));
                    DeviceId = serialNumber;
                }

                // Save updated configuration to database
                await Task.Run(() => _configurationService.SaveConfigDtoToDb(_standaloneConfigDTO));
            }

            // Ensure configuration is loaded
            if (_standaloneConfigDTO == null)
            {
                _standaloneConfigDTO = ConfigFileHelper.ReadFile();
            }

            // Apply reader settings if management-only mode is not enabled
            if ((_standaloneConfigDTO != null &&
                string.IsNullOrEmpty(_standaloneConfigDTO.smartreaderEnabledForManagementOnly)) ||
                !string.Equals("1", _standaloneConfigDTO.smartreaderEnabledForManagementOnly))
            {
                await ApplySettingsAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update reader configuration");
            await ProcessGpoErrorPortAsync();
            throw;
        }
    }

    private async Task InitializeAuthenticationAsync()
    {
        try
        {
            // Check if configuration exists
            if (_standaloneConfigDTO == null)
            {
                _logger.LogError("Configuration not loaded during authentication initialization");
                return;
            }

            // Initialize ExternalApiToken if external API verification is enabled
            if (string.Equals("1", _standaloneConfigDTO.enableExternalApiVerification))
            {
                if (!string.IsNullOrEmpty(_standaloneConfigDTO.externalApiVerificationAuthLoginUrl))
                {
                    try
                    {
                        // Attempt to get bearer token using the configured authentication endpoint
                        ExternalApiToken = await _httpUtil.GetBearerTokenUsingJsonBodyAsync(
                        _standaloneConfigDTO.httpAuthenticationTokenApiUrl,
                        _standaloneConfigDTO.httpAuthenticationTokenApiBody);

                        if (!string.IsNullOrEmpty(ExternalApiToken) && ExternalApiToken.Length > 10)
                            _standaloneConfigDTO.httpAuthenticationTokenApiValue = ExternalApiToken;

                        if (string.IsNullOrEmpty(ExternalApiToken))
                        {
                            _logger.LogWarning("Failed to obtain external API authentication token");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error during external API authentication");
                        await ProcessGpoErrorPortAsync();
                    }
                }
            }

            // Start the authentication token refresh timer if bearer token is used
            if (!string.IsNullOrEmpty(_standaloneConfigDTO.httpAuthenticationTokenApiValue))
            {
                _stopwatchBearerToken.Start();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize authentication");
            await ProcessGpoErrorPortAsync();
            throw;
        }
    }
    private async Task ConfigurePluginsAsync()
    {
        if (!string.Equals("1", _standaloneConfigDTO?.enablePlugin, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var pluginsDir = Path.Combine(AppContext.BaseDirectory, "plugins");
        var pluginLoaders = _configurationService.LoadPluginAssemblies(pluginsDir);
        _ = await _configurationService.InitializePluginsAsync(DeviceId, pluginLoaders);
    }

    private async Task ConfigureCommunicationChannelsAsync()
    {
        await InitializeMqttAsync();
        InitializeSerialPorts();
        _tcpSocketService?.InitializeSocketServer();
        InitializeUdpServer();
        SetupUsbFlashDrive();
        StartBarcodeHidUsbPort();
    }

    private async Task ConfigureAndStartTimersAsync()
    {
        ConfigurePeriodicTimers();
        StartPeriodicTimers();
        await ConfigureEventHandlersAsync();
    }

    private async Task StartMainProcessingLoopAsync(CancellationToken stoppingToken)
    {
        if (!IsOnPause())
        {
            try
            {
                _logger.LogInformation("Starting inventory...");
                await HandleStartInventoryAsync();
                _logger.LogInformation("Inventory started successfully");
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Failed to start inventory due to invalid operation");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error starting inventory");
                await ProcessGpoErrorPortAsync();
            }

            try
            {
                if (IsGpiEnabled())
                {
                    await SetGpiPortsAsync();
                }

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error setting GPI ports");
            }
        }


        _logger.LogInformation("App started. ");
        UpdateSystemImageFallbackFlag();

        while (!stoppingToken.IsCancellationRequested)
        {
            await EnsureConnectionsAsync();
            // await ProcessPendingMessagesAsync();
            await UpdatePositioningDataAsync();
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }

    private async Task HandleFatalErrorAsync(Exception ex, CancellationToken stoppingToken)
    {
        _logger.LogError(ex, "Fatal error in ExecuteAsync. Restarting process.");
        CleanupResources();

        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        RestartProcess();
    }

    private void CleanupResources()
    {

        _serialTty?.Dispose();
        // Cleanup other resources
    }

    private void RestartProcess()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.ProcessPath ?? throw new InvalidOperationException("Process path not found"),
            WorkingDirectory = Directory.GetCurrentDirectory(),
            UseShellExecute = true
        };
        _ = Process.Start(startInfo);
        Environment.Exit(1);
    }

    private async Task EnsureConnectionsAsync()
    {
        if (_standaloneConfigDTO != null &&
            !string.IsNullOrEmpty(_standaloneConfigDTO.socketServer) &&
            string.Equals("1", _standaloneConfigDTO.socketServer, StringComparison.OrdinalIgnoreCase))
        {
            await _tcpSocketService.EnsureSocketServerConnectionAsync();
        }
    }

    //private async Task ProcessPendingMessagesAsync()
    //{
    //    // await ProcessQueuedMessagesAsync();
    //    //await ProcessMqttMessagesAsync();
    //    //await ProcessSocketMessagesAsync();
    //}

    private void InitializeUdpServer()
    {
        try
        {
            if (_standaloneConfigDTO == null)
            {
                _logger.LogWarning("Configuration not loaded, skipping UDP server initialization");
                return;
            }

            // Check if UDP server is enabled in configuration
            if (string.IsNullOrEmpty(_standaloneConfigDTO.udpServer) ||
                !string.Equals("1", _standaloneConfigDTO.udpServer, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("UDP server not enabled in configuration");
                return;
            }

            // Validate UDP configuration
            if (string.IsNullOrEmpty(_standaloneConfigDTO.udpIpAddress))
            {
                _logger.LogError("UDP IP address not configured");
                return;
            }

            if (!int.TryParse(_standaloneConfigDTO.udpReaderPort, out int port))
            {
                _logger.LogError("Invalid UDP port configuration");
                return;
            }

            // Initialize UDP server
            try
            {
                _udpSocketServer = new UDPSocket();
                _udpSocketServer.Server(_standaloneConfigDTO.udpIpAddress, port);

                _logger.LogInformation($"UDP server initialized successfully on {_standaloneConfigDTO.udpIpAddress}:{port}");

                // Set up remote endpoint if configured
                if (!string.IsNullOrEmpty(_standaloneConfigDTO.udpRemoteIpAddress) &&
                    !string.IsNullOrEmpty(_standaloneConfigDTO.udpRemotePort))
                {
                    if (int.TryParse(_standaloneConfigDTO.udpRemotePort, out int remotePort))
                    {
                        _logger.LogInformation($"Configured UDP remote endpoint: {_standaloneConfigDTO.udpRemoteIpAddress}:{remotePort}");
                    }
                    else
                    {
                        _logger.LogWarning("Invalid UDP remote port configuration");
                    }
                }

                // Start processing messages from the queue
                _timerTagPublisherUdpServer.Start();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize UDP server");
                _ = ProcessGpoErrorPortAsync();

                // Cleanup any partially initialized resources
                if (_udpSocketServer != null)
                {
                    try
                    {
                        _udpSocketServer.Close();
                        _udpSocketServer = null;
                    }
                    catch (Exception cleanupEx)
                    {
                        _logger.LogError(cleanupEx, "Error cleaning up UDP server resources");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Critical error during UDP server initialization");
            _ = ProcessGpoErrorPortAsync();
        }
    }
    private async Task InitializeMqttAsync()
    {
        if (_standaloneConfigDTO == null ||
            !string.Equals("1", _standaloneConfigDTO.mqttEnabled, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var deviceId = _standaloneConfigDTO.readerSerial;
        DeviceIdMqtt = _standaloneConfigDTO.readerName;

        if (string.IsNullOrEmpty(DeviceIdMqtt))
        {
            DeviceIdMqtt = $"{deviceId}{_standaloneConfigDTO.readerName}";
        }

        if (!string.IsNullOrEmpty(_standaloneConfigDTO.mqttTagEventsTopic))
        {
            await _mqttService.ConnectToMqttBrokerAsync(
                DeviceIdMqtt,
                _standaloneConfigDTO
            );
            _mqttService.GpoErrorRequested += OnGpoErrorRequested;
            _mqttService.MessageReceived += HandleMessageReceived;
        }
    }

    private void InitializeSerialPorts()
    {
        if (!ShouldInitializeSerialPorts()) return;

        var ports = SerialPort.GetPortNames();
        var serialPortName = GetSerialPortName(ports);
        ConfigureSerialPort(serialPortName);
    }

    private bool ShouldInitializeSerialPorts()
    {
        return _standaloneConfigDTO != null &&
               (string.Equals("1", _standaloneConfigDTO.serialPort, StringComparison.OrdinalIgnoreCase) ||
                string.Equals("1", _standaloneConfigDTO.enableBarcodeSerial, StringComparison.OrdinalIgnoreCase));
    }

    private async Task ProcessMqttMessagesAsync()
    {
        while (_messageQueueTagSmartReaderTagEventMqtt.TryDequeue(out var message))
        {
            try
            {
                if (_mqttService?.CheckConnection() == true)
                {
                    await PublishMqttJsonTagEventDataAsync(message);
                }
                else
                {
                    _logger.LogDebug("_mqttService is not connected.");
                    _messageQueueTagSmartReaderTagEventMqtt.Enqueue(message);
                    await Task.Delay(1000); // Back off if disconnected
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing MQTT message");
            }
        }
    }

    //private async Task ProcessSocketMessagesAsync()
    //{
    //    const int MaxRetries = 3;
    //    const int RetryDelay = 1000;

    //    while (_messageQueueTagSmartReaderTagEventSocketServer.TryDequeue(out var message))
    //    {
    //        for (int retry = 0; retry < MaxRetries; retry++)
    //        {
    //            try
    //            {
    //                if (!IsSocketServerHealthy())
    //                {
    //                    await EnsureSocketServerConnectionAsync();
    //                }

    //                await ProcessSocketJsonTagEventDataAsync(message);
    //                break;
    //            }
    //            catch (Exception ex) when (retry < MaxRetries - 1)
    //            {
    //                _logger.LogError(ex, $"Error processing socket message, attempt {retry + 1} of {MaxRetries}");
    //                await Task.Delay(RetryDelay);
    //            }
    //        }
    //    }
    //}

    private async Task ProcessUdpMessages()
    {
        while (_messageQueueTagSmartReaderTagEventUdpServer.TryDequeue(out var message))
        {
            try
            {
                await ProcessUdpDataTagEventDataAsync(message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing UDP message");
            }
        }
    }

    private async Task ProcessUsbDriveMessages()
    {
        if (!Directory.Exists(R700UsbDrivePath))
        {
            return;
        }

        while (_messageQueueTagSmartReaderTagEventUsbDrive.TryDequeue(out var message))
        {
            try
            {
                await ProcessUsbDriveJsonTagEventDataAsync(message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing USB drive message");
            }
        }
    }

    public async Task ValidateLicenseAsync()
    {
        if (_plugins?.ContainsKey("LICENSE") == false &&
            _standaloneConfigDTO != null &&
            !string.IsNullOrEmpty(_expectedLicense) &&
            !_expectedLicense.Equals(_standaloneConfigDTO.licenseKey.Trim()))
        {
            _logger.LogError("Invalid license key");
            if (IsMqttEnabled())
            {
                var mqttEvent = new Dictionary<string, object>
                {
                    { "readerName", _standaloneConfigDTO.readerName },
                    { "mac", _iotDeviceInterfaceClient.MacAddress },
                    { "timestamp", Utils.CSharpMillisToJavaLongMicroseconds(DateTime.Now) },
                    { "smartreader-status", "invalid-license-key" }
                };
                await _mqttService.PublishMqttManagementEventAsync(mqttEvent);
            }
            throw new InvalidOperationException("Invalid license key");
        }
    }

    private void ConfigureSerialPort(string portName)
    {
        try
        {
            var baudrate = int.Parse(_standaloneConfigDTO.baudRate);
            _serialTty = new SerialPort(portName, baudrate)
            {
                ReadTimeout = 1000,
                DiscardNull = true
            };

            _serialTty.DataReceived += BarcodeSerialDataReceivedHandler;

            if (!_serialTty.IsOpen)
            {
                _serialTty.Open();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error configuring serial port {portName}");
        }
    }

    private string GetSerialPortName(string[] ports)
    {
        var serialPortName = "/dev/ttyUSB0";
        foreach (var port in ports)
        {
            _logger.LogInformation($"Found serial port: {port}");
            if (port.Contains("/dev/ttyUSB"))
            {
                serialPortName = port;
                break;
            }
        }
        _logger.LogInformation($"Selected serial port: {serialPortName}");
        return serialPortName;
    }

    private async Task ConfigureEventHandlersAsync()
    {
        if (_standaloneConfigDTO == null || string.Equals("1", _standaloneConfigDTO.smartreaderEnabledForManagementOnly))
        {
            return;
        }

        _iotDeviceInterfaceClient.TagInventoryEvent += OnTagInventoryEvent!;
        _iotDeviceInterfaceClient.InventoryStatusEvent += OnInventoryStatusEvent!;
        _iotDeviceInterfaceClient.GpiTransitionEvent += OnGpiTransitionEvent!;

        try
        {
            await ApplySettingsAsync();
            await _iotDeviceInterfaceClient.StartAsync("SmartReader");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error configuring event handlers");
            if (_standaloneConfigDTO.isEnabled == "1")
            {
                _configurationService.SaveStartCommandToDb();
            }
        }
    }

    private void ConfigurePeriodicTimers()
    {

        ConfigureTimer(_timerPeriodicTasksJob, OnRunPeriodicTasksJobManagerEvent, 1000);
        ConfigureTimer(_timerTagFilterLists, OnRunPeriodicTagFilterListsEvent, 100);
        if (IsHeartbeatEnabled())
        {
            ConfigureTimer(_timerKeepalive, OnRunPeriodicKeepaliveCheck, 100);
        }

        ConfigureTimer(_timerStopTriggerDuration, OnRunPeriodicStopTriggerDurationEvent, 100);

        if (IsHttpPostEnabled())
        {
            ConfigureTimer(_timerTagPublisherHttp, OnRunPeriodicTagPublisherHttpTasksEvent, 100);
            ConfigureTimer(_timerTagPublisherRetry, OnRunPeriodicTagPublisherRetryTasksEvent, 500);
        }

        if (IsSocketServerEnabled())
        {
            ConfigureTimer(_timerTagPublisherSocket, OnRunPeriodicTagPublisherSocketTasksEvent, 10);
        }

        ConfigureTimer(_timerTagPublisherWebSocket, OnRunPeriodicTagPublisherWebSocketTasksEvent, 10);

        if (IsSummaryStreamEnabled())
        {
            ConfigureTimer(_timerSummaryStreamPublisher, OnRunPeriodicSummaryStreamPublisherTasksEvent, 10);
        }

        if (IsBackupToInternalFlashEnabled() ||
            IsBackupToFlashDriveOnGpiEventEnabled() ||
            IsUsbFlashDriveEnabled())
        {
            ConfigureTimer(_timerTagPublisherUsbDrive, OnRunPeriodicUsbDriveTasksEvent, 100);
        }
        if (IsUdpServerEnabled())
        {
            ConfigureTimer(_timerTagPublisherUdpServer, OnRunPeriodicTagPublisherUdpTasksEvent, 500);
        }


        if (IsMqttEnabled())
        {
            ConfigureTimer(_timerTagPublisherMqtt, OnRunPeriodicTagPublisherMqttTasksEvent, 10);
        }
        if (IsOpcUaClientEnabled())
        {
            ConfigureTimer(_timerTagPublisherOpcUa, OnRunPeriodicTagPublisherOpcUaTasksEvent, 500);
        }
    }

    private void ConfigureTimer(Timer timer, ElapsedEventHandler handler, double interval)
    {
        timer.Elapsed += handler;
        timer.Interval = interval;
        timer.AutoReset = false;
    }

    private void StartPeriodicTimers()
    {
        _timerPeriodicTasksJob.Start();
        _timerTagFilterLists.Start();

        if (_standaloneConfigDTO == null)
        {
            try
            {
                _standaloneConfigDTO = _configurationService.LoadConfig();
            }
            catch (Exception)
            {
                _logger.LogError("Error loading _standaloneConfigDTO to trigger timers.");
            }

        }


        if (IsHeartbeatEnabled())
        {
            _timerKeepalive.Start();
            _stopwatchKeepalive.Start();
        }

        if (_standaloneConfigDTO.stopTriggerDuration != "0")
        {
            _timerStopTriggerDuration.Start();
        }

        if (IsSocketServerEnabled())
        {
            _timerTagPublisherSocket.Start();
        }

        if (IsHttpPostEnabled())
        {
            _timerTagPublisherHttp.Start();
        }

        if (IsUdpServerEnabled())
        {
            _timerTagPublisherUdpServer.Start();
        }

        if (IsUsbHidEnabled())
        {
            _timerTagPublisherUsbDrive.Start();
        }

        if (IsMqttEnabled())
        {
            _timerTagPublisherMqtt.Start();
        }

        if (IsSummaryStreamEnabled())
        {
            _timerSummaryStreamPublisher.Start();
        }

        if (IsOpcUaClientEnabled())
        {
            _timerTagPublisherOpcUa.Start();
        }

        // Assuming _timerTagPublisherRetry should always start
        _timerTagPublisherRetry.Start();
    }

    //private async Task ProcessQueuedMessagesAsync()
    //{
    //    await ProcessHttpMessages();
    //    // await ProcessMqttMessagesAsync();
    //    //await _tcpSocketService.ProcessSocketMessagesAsync();
    //    await ProcessUdpMessages();
    //    await ProcessUsbDriveMessages();
    //}

    private async Task ProcessHttpMessages()
    {
        while (_messageQueueTagSmartReaderTagEventHttpPost.TryDequeue(out var message))
        {
            try
            {
                await ProcessHttpJsonPostTagEventDataAsync(message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing HTTP message");
                _messageQueueTagSmartReaderTagEventHttpPostRetry.Enqueue(message);
            }
        }
    }

    private void StartBarcodeHidUsbPort()
    {
        try
        {
            if (_standaloneConfigDTO != null
                    && string.Equals("1", _standaloneConfigDTO.enableBarcodeHid, StringComparison.OrdinalIgnoreCase))
            {
                string barcodeHidUsbPortName = "/dev/input/event0";
                if (File.Exists(barcodeHidUsbPortName))
                {
                    _logger.LogInformation("Starting HID port");
                    _aggInputReader = new AggregateInputReader(barcodeHidUsbPortName);

                    _aggInputReader.OnKeyPress += BarcodeHidDataReceivedHandler;

                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error opening port HID");
        }
    }

    private void BarcodeHidDataReceivedHandler(KeyPressEvent e)
    {

        try
        {
            System.Console.WriteLine($"Code:{e.Code} State:{e.State} Data:{e.Data}");
            _logger.LogInformation($"Code:{e.Code} State:{e.State} Data:{e.Data}");

            var receivedBarcode = ReceiveBarcode(e.Data);
            if ("".Equals(receivedBarcode))
                _logger.LogInformation("SerialDataReceivedHandler - ignoring [" + receivedBarcode + "] ");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error receiving data from HID port.");

        }
    }

    private void StartBarcodeSerialPort()
    {
        try
        {
            if (_standaloneConfigDTO != null
                && ((!string.IsNullOrEmpty(_standaloneConfigDTO.serialPort)
                     && string.Equals("1", _standaloneConfigDTO.serialPort, StringComparison.OrdinalIgnoreCase))
                    ||
                    (!string.IsNullOrEmpty(_standaloneConfigDTO.enableBarcodeSerial)
                     && string.Equals("1", _standaloneConfigDTO.enableBarcodeSerial,
                         StringComparison.OrdinalIgnoreCase))
                )
               )
            {
                var ports = SerialPort.GetPortNames();
                var serialPortName = "/dev/ttyUSB0";
                foreach (var port in ports)
                {
                    _logger.LogInformation("serial port: " + port);
                    if (port.Contains("/dev/ttyUSB")) serialPortName = port;
                }

                _logger.LogInformation("Selected serial port: " + serialPortName);

                var baudrate = int.Parse(_standaloneConfigDTO.baudRate);
                _serialTty = new SerialPort(serialPortName, baudrate)
                {
                    ReadTimeout = 1000,
                    DiscardNull = true
                };

                _serialTty.DataReceived += BarcodeSerialDataReceivedHandler;
                if (!_serialTty.IsOpen)
                    try
                    {
                        _serialTty.Open();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error opening port " + serialPortName);
                    }
                //{
                //    DataBits = 8,
                //    //Parity = Parity.None,
                //    //StopBits = StopBits.None,
                //    WriteTimeout = TimeSpan.FromSeconds(3).Seconds,
                //    ReadTimeout = TimeSpan.FromMilliseconds(30).Seconds
                //};
                if (_serialTty != null && _serialTty.IsOpen)
                {

                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("Serial init: " + ex.Message);
        }
    }

    private void UpdateSystemImageFallbackFlag()
    {
        try
        {
            if (_standaloneConfigDTO != null
                && _readerAddress != null
               && string.Equals("1", _standaloneConfigDTO.systemDisableImageFallbackStatus, StringComparison.OrdinalIgnoreCase))
            {
                var rshell = new RShellUtil(_readerAddress, _readerUsername, _readerPassword);

                try
                {
                    var resultSystemImageSummary = rshell.SendCommand("show image summary");
                }
                catch (Exception)
                {


                }
                if (!File.Exists("/customer/disable-fallback"))
                {
                    File.WriteAllText("/customer/disable-fallback", "ok");
                    _logger.LogInformation("Disabling image fallback. ");

                    try
                    {
                        var resultDisableImageFallback = rshell.SendCommand("config image disablefallback");
                        if (!string.IsNullOrEmpty(resultDisableImageFallback))
                        {
                            var lines = resultDisableImageFallback.Split("\n");
                            foreach (var line in lines)
                            {
                                _logger.LogInformation(line);
                            }

                            var resultDisableImageFallbackReboot = rshell.SendCommand("reboot");
                        }
                    }
                    catch (Exception)
                    {
                    }
                }

            }
        }
        catch (Exception exFallback)
        {
            _logger.LogError(exFallback, "unexpected error checking image fallback settings.");
        }
    }

    private void StartAsync()
    {
        throw new NotImplementedException();
    }

    private void BarcodeSerialDataReceivedHandler(object sender, SerialDataReceivedEventArgs e)
    {
        try
        {
            var sp = (SerialPort)sender;

            var bytesToRead = sp.BytesToRead;
            var bytes = new byte[bytesToRead];

            _ = sp.Read(bytes, 0, bytesToRead);
            var messageData = Encoding.ASCII.GetString(bytes);

            sp.DiscardInBuffer();
            sp.DiscardOutBuffer();
            //string messageData = sp.ReadExisting();
            var receivedBarcode = ReceiveBarcode(messageData);
            if ("".Equals(receivedBarcode))
                _logger.LogInformation("BarcodeSerialDataReceivedHandler - ignoring [" + messageData + "] ");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BarcodeSerialDataReceivedHandler: unexpected error.");
        }
    }

    private void SerialDataReceivedHandler(object sender, SerialDataReceivedEventArgs e)
    {
        try
        {
            var sp = (SerialPort)sender;

            var bytesToRead = sp.BytesToRead;
            var bytes = new byte[bytesToRead];

            _ = sp.Read(bytes, 0, bytesToRead);
            var messageData = Encoding.ASCII.GetString(bytes);

            sp.DiscardInBuffer();
            sp.DiscardOutBuffer();
            //string messageData = sp.ReadExisting();
            var receivedBarcode = ReceiveBarcode(messageData);
            if ("".Equals(receivedBarcode))
                _logger.LogInformation("SerialDataReceivedHandler - ignoring [" + messageData + "] ");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SerialDataReceivedHandler: unexpected error.");
        }
    }

    private void GpsSerialDataReceivedHandler(object sender, SerialDataReceivedEventArgs e)
    {
        try
        {
            var sp = (SerialPort)sender;

            var bytesToRead = sp.BytesToRead;
            //var bytes = new byte[bytesToRead];

            //sp.Read(bytes, 0, bytesToRead);
            //var messageData = Encoding.ASCII.GetString(bytes);

            //sp.DiscardInBuffer();
            //sp.DiscardOutBuffer();
            ////string messageData = sp.ReadExisting();
            //var receivedBarcode = ReceiveBarcode(messageData);
            //if ("".Equals(receivedBarcode))
            //    _logger.LogInformation("SerialDataReceivedHandler - ignoring [" + messageData + "] ");

            string line = sp.ReadLine();
            // Read a line of data from the serial port.

            if ("".Equals(line))
                _logger.LogInformation("GpsSerialDataReceivedHandler - ignoring [" + line + "] ");

            if (line.StartsWith("$GPGGA"))
            {
                string[] parts = line.Split(',');
                // Split the line into an array of strings using a comma as the delimiter.



                if (parts.Length >= 12)
                {
                    string lat = parts[2];
                    string latDir = parts[3];
                    string lon = parts[4];
                    string lonDir = parts[5];
                    int altitude = int.Parse(parts[9]);
                    // Extract the latitude, longitude, and altitude from the parts array.



                    if (lat != "" && lon != "")
                    {
                        double latDegrees = double.Parse(lat.Substring(0, 2));
                        double latMinutes = double.Parse(lat.Substring(2));
                        double latDecimal = latDegrees + (latMinutes / 60);
                        if (latDir == "S")
                        {
                            latDecimal = -latDecimal;
                        }
                        // Convert the latitude from NMEA format to decimal degrees.



                        double lonDegrees = double.Parse(lon.Substring(0, 3));
                        double lonMinutes = double.Parse(lon.Substring(3));
                        double lonDecimal = lonDegrees + (lonMinutes / 60);
                        if (lonDir == "W")
                        {
                            lonDecimal = -lonDecimal;
                        }
                        // Convert the longitude from NMEA format to decimal degrees.



                        // DateTime timestamp = DateTime.UtcNow;
                        // Get the current UTC time.



                        string json = JsonConvert.SerializeObject(new { latitude = latDecimal, longitude = lonDecimal, altitude = altitude });
                        // Create a JSON object with the latitude, longitude, altitude, and timestamp, and serialize it to a JSON string.



                        _logger.LogInformation(json);
                        // Print the JSON string to the console.
                    }
                }
            }
            else
            {
                _logger.LogError("Invalid NMEA sentence: " + line);
                // If the NMEA sentence is not in the expected format, print an error message to the console.
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GpsSerialDataReceivedHandler: unexpected error.");
        }
    }

    //private async void OnTagInventoryEvent(object sender, Impinj.Atlas.TagInventoryEvent tagEvent)
    //{
    //    try
    //    {


    //    }
    //    catch (Exception)
    //    {

    //    }

    //}

    //private async Task ConnectToMqttCommandBrokerAsync(string mqttClientId, string mqttBrokerAddress,
    //    int mqttBrokerPort, string mqttUsername, string mqttPassword, string mqttTopic, int mqttQos,
    //    StandaloneConfigDTO smartReaderSetupData)
    //{
    //    try
    //    {
    //        DeviceIdMqtt = mqttClientId;
    //        //var mqttBrokerAddress = _configuration.GetValue<string>("MQTTInfo:Address");
    //        //var mqttBrokerPort = _configuration.GetValue<int>("MQTTInfo:Port");
    //        //var mqttBrokerUsername = _configuration.GetValue<string>("MQTTInfo:username");
    //        //var mqttBrokerPassword = _configuration.GetValue<string>("MQTTInfo:password");
    //        // Setup and start a managed MQTT client.
    //        // 1 - The managed client is started once and will maintain the connection automatically including reconnecting etc.
    //        // 2 - All MQTT application messages are added to an internal queue and processed once the server is available.
    //        // 3 - All MQTT application messages can be stored to support sending them after a restart of the application
    //        // 4 - All subscriptions are managed across server connections. There is no need to subscribe manually after the connection with the server is lost.


    //        // Setup and start a managed MQTT client.
    //        //ManagedMqttClientOptions localMqttClientOptions;
    //        int mqttKeepAlivePeriod = 30;
    //        int.TryParse(_standaloneConfigDTO.mqttBrokerKeepAlive, out mqttKeepAlivePeriod);

    //        var localClientId = mqttClientId + "-" + DateTime.Now.ToFileTimeUtc();
    //        if (string.IsNullOrEmpty(mqttUsername))
    //        {
    //            if (!string.IsNullOrEmpty(_standaloneConfigDTO.mqttBrokerProtocol)
    //                && _standaloneConfigDTO.mqttBrokerProtocol.ToLower().Contains("ws"))
    //            {
    //                var mqttBrokerWebSocketPath = "/mqtt";
    //                if (!string.IsNullOrEmpty(_standaloneConfigDTO.mqttBrokerWebSocketPath))
    //                {
    //                    mqttBrokerWebSocketPath = _standaloneConfigDTO.mqttBrokerWebSocketPath;
    //                }
    //                _mqttCommandClientOptions = new ManagedMqttClientOptionsBuilder()
    //                .WithClientOptions(new MqttClientOptionsBuilder()
    //                    //.WithCleanSession()
    //                    .WithKeepAlivePeriod(new TimeSpan(0, 0, 0, mqttKeepAlivePeriod))
    //                    //.WithCommunicationTimeout(TimeSpan.FromMilliseconds(60 * 1000))
    //                    .WithClientId(localClientId)
    //                    .WithWebSocketServer($"{_standaloneConfigDTO.mqttBrokerProtocol}://{mqttBrokerAddress}:{mqttBrokerPort}{mqttBrokerWebSocketPath}")
    //                    .Build())
    //                .Build();
    //            }
    //            else
    //            {
    //                _mqttCommandClientOptions = new ManagedMqttClientOptionsBuilder()
    //                .WithClientOptions(new MqttClientOptionsBuilder()
    //                    //.WithCleanSession()
    //                    .WithKeepAlivePeriod(new TimeSpan(0, 0, 0, mqttKeepAlivePeriod))
    //                    //.WithCommunicationTimeout(TimeSpan.FromMilliseconds(60 * 1000))
    //                    .WithClientId(localClientId)
    //                    .WithTcpServer(mqttBrokerAddress, mqttBrokerPort)
    //                    .Build())
    //                .Build();
    //            }

    //        }
    //        else
    //        {

    //            if (!string.IsNullOrEmpty(_standaloneConfigDTO.mqttBrokerProtocol)
    //                && _standaloneConfigDTO.mqttBrokerProtocol.ToLower().Contains("ws"))
    //            {
    //                var mqttBrokerWebSocketPath = "/mqtt";
    //                if (!string.IsNullOrEmpty(_standaloneConfigDTO.mqttBrokerWebSocketPath))
    //                {
    //                    mqttBrokerWebSocketPath = _standaloneConfigDTO.mqttBrokerWebSocketPath;
    //                }
    //                _mqttCommandClientOptions = new ManagedMqttClientOptionsBuilder()
    //                .WithClientOptions(new MqttClientOptionsBuilder()
    //                    //.WithCleanSession()
    //                    .WithKeepAlivePeriod(new TimeSpan(0, 0, 0, mqttKeepAlivePeriod))
    //                    //.WithCommunicationTimeout(TimeSpan.FromMilliseconds(60 * 1000))
    //                    .WithClientId(localClientId)
    //                    .WithWebSocketServer($"{_standaloneConfigDTO.mqttBrokerProtocol}://{mqttBrokerAddress}:{mqttBrokerPort}{mqttBrokerWebSocketPath}")
    //                    .WithCredentials(mqttUsername, mqttPassword)
    //                    .Build())
    //                .Build();
    //            }
    //            else
    //            {
    //                _mqttCommandClientOptions = new ManagedMqttClientOptionsBuilder()
    //                .WithClientOptions(new MqttClientOptionsBuilder()
    //                    //.WithCleanSession()
    //                    .WithKeepAlivePeriod(new TimeSpan(0, 0, 0, mqttKeepAlivePeriod))
    //                    //.WithCommunicationTimeout(TimeSpan.FromMilliseconds(60 * 1000))
    //                    .WithClientId(localClientId)
    //                    .WithTcpServer(mqttBrokerAddress, mqttBrokerPort)
    //                    .WithCredentials(mqttUsername, mqttPassword)
    //                    .Build())
    //                .Build();
    //            }
    //        }

    //        _mqttCommandClient = new MqttFactory().CreateManagedMqttClient();


    //        await _mqttCommandClient.StartAsync(_mqttCommandClientOptions);


    //        _mqttCommandClient.UseApplicationMessageReceivedHandler(e =>
    //        {
    //            _logger.LogInformation("### RECEIVED APPLICATION MESSAGE ###");
    //            _logger.LogInformation($"+ Topic = {e.ApplicationMessage.Topic}");
    //            if (e.ApplicationMessage.PayloadSegment != null)
    //                _logger.LogInformation($"+ Payload = {Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment)}");

    //            _logger.LogInformation($"+ QoS = {e.ApplicationMessage.QualityOfServiceLevel}");
    //            _logger.LogInformation($"+ Retain = {e.ApplicationMessage.Retain}");
    //            _logger.LogInformation($"+ ClientId = {e.ClientId}");

    //            var payload = "";
    //            if (e.ApplicationMessage.PayloadSegment != null)
    //                payload = Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment);
    //            try
    //            {
    //                ProcessMqttCommandMessage(e.ClientId, e.ApplicationMessage.Topic, payload);
    //            }
    //            catch (Exception ex)
    //            {
    //                _logger.LogError("[[COMMAND]] ProcessMqttMessage: Unexpected error. " + ex.Message);
    //            }

    //            _logger.LogInformation(" ");
    //        });


    //        _ = _mqttCommandClient.UseConnectedHandler(async e =>
    //        {
    //            Console.WriteLine("### [[COMMAND]] CONNECTED WITH SERVER ###");
    //            if (!string.IsNullOrEmpty(_standaloneConfigDTO.mqttBrokerAddress))
    //                _logger.LogInformation(
    //                    "### [[COMMAND]] CONNECTED WITH SERVER ### " + _standaloneConfigDTO.mqttBrokerAddress,
    //                    SeverityType.Debug);


    //            try
    //            {
    //                var mqttTopicFilters = BuildMqttTopicList(smartReaderSetupData);


    //                await _mqttCommandClient.SubscribeAsync(mqttTopicFilters.ToArray());
    //            }
    //            catch (Exception ex)
    //            {
    //                _logger.LogError(ex, "[[COMMAND]] Subscribe: Unexpected error. " + ex.Message);
    //            }

    //            // Subscribe to a topic
    //            //await _mqttClient.SubscribeAsync(new MqttClientSubscribeOptionsBuilder().WithTopic("my/topic").Build());

    //            //Console.WriteLine("### SUBSCRIBED ###");
    //        });

    //        _ = _mqttCommandClient.UseDisconnectedHandler(async e =>
    //        {
    //            Console.WriteLine("### [[COMMAND]] DISCONNECTED FROM SERVER ###" );
    //            if (!string.IsNullOrEmpty(_standaloneConfigDTO.mqttBrokerAddress))
    //                _logger.LogInformation(
    //                    "### [[COMMAND]] DISCONNECTED FROM SERVER ### " + _standaloneConfigDTO.mqttBrokerAddress,
    //                    SeverityType.Debug);
    //        });
    //    }
    //    catch (Exception ex)
    //    {
    //        _logger.LogError(ex, "Unexpected error. " + ex.Message);
    //    }
    //}


    public async Task<string> ProcessMqttControlCommandJsonAsync(string controlCommandJson, bool shouldPublishToMqtt)
    {
#pragma warning disable CS8600, CS8601, CS8602, CS8603, CS8604, CS8625, CS8629 // Dereference of a possibly null reference.
        string result = "{\"status\": \"success\"}";
        try
        {
            var deserializedCmdData = JsonConvert.DeserializeObject<JObject>(controlCommandJson);
            if (deserializedCmdData != null)
                if (deserializedCmdData.ContainsKey("command") || deserializedCmdData.ContainsKey("cmd"))
                {

                    var commandStatus = "success";
                    var commandValue = "";
                    if (deserializedCmdData.ContainsKey("command"))
                    {
                        commandValue = deserializedCmdData["command"].Value<string>();
                    }
                    if (deserializedCmdData.ContainsKey("cmd"))
                    {
                        commandValue = deserializedCmdData["cmd"].Value<string>();
                    }
                    if (deserializedCmdData.ContainsKey("command_id") || deserializedCmdData.ContainsKey("id"))
                    {
                        var previousCommandId = _configurationService.ReadMqttCommandIdFromFile().Result;
                        var commandIdValue = "";
                        if (deserializedCmdData.ContainsKey("command_id"))
                        {
                            commandIdValue = deserializedCmdData["command_id"].Value<string>();
                        }
                        if (deserializedCmdData.ContainsKey("id"))
                        {
                            commandIdValue = deserializedCmdData["id"].Value<string>();
                        }
                        if (!string.IsNullOrEmpty(commandIdValue))
                        {
                            _ = await _configurationService.WriteMqttCommandIdToFile(commandIdValue);
                            if (!string.IsNullOrEmpty(previousCommandId))
                                if (previousCommandId.Trim().Equals(commandIdValue.Trim()))
                                {
                                    commandStatus = "error";
                                    if (!string.IsNullOrEmpty(_standaloneConfigDTO.mqttControlResponseTopic))
                                        if (_standaloneConfigDTO.mqttControlResponseTopic.Contains(
                                                "{{deviceId}}"))
                                            _standaloneConfigDTO.mqttControlResponseTopic =
                                                _standaloneConfigDTO.mqttControlResponseTopic.Replace(
                                                    "{{deviceId}}", _standaloneConfigDTO.readerName);

                                    if (deserializedCmdData.ContainsKey("response"))
                                    {
                                        deserializedCmdData["response"] = commandStatus;
                                    }
                                    else
                                    {
                                        var commandResponse = new JProperty("response", commandStatus);
                                        deserializedCmdData.Add(commandResponse);
                                    }

                                    var payloadCommandStatus = new Dictionary<string, string>
                                    {
                                        {
                                            "detail",
                                            $"id {commandIdValue} already processed."
                                        }
                                    };
                                    if (deserializedCmdData.ContainsKey("message"))
                                    {
                                        deserializedCmdData["message"] =
                                            JObject.FromObject(payloadCommandStatus);
                                    }
                                    else
                                    {
                                        var commandResponsePayload = new JProperty("message",
                                            JObject.FromObject(payloadCommandStatus));
                                        deserializedCmdData.Add(commandResponsePayload);
                                    }

                                    var serializedData = JsonConvert.SerializeObject(deserializedCmdData);
                                    if (shouldPublishToMqtt)
                                    {
                                        var qos = 0;
                                        var retain = false;
                                        var mqttQualityOfServiceLevel = MqttQualityOfServiceLevel.AtMostOnce;
                                        try
                                        {
                                            _ = int.TryParse(_standaloneConfigDTO.mqttControlResponseQoS, out qos);
                                            _ = bool.TryParse(_standaloneConfigDTO.mqttControlResponseRetainMessages,
                                                out retain);

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

                                        var mqttCommandResponseTopic =
                                            $"{_standaloneConfigDTO.mqttControlResponseTopic}";
                                        await _mqttService.PublishAsync(mqttCommandResponseTopic,
                                            serializedData,
                                            _iotDeviceInterfaceClient.MacAddress,
                                            mqttQualityOfServiceLevel,
                                            retain);

                                    }

                                    return serializedData;
                                }
                        }
                    }

                    if ("start".Equals(commandValue))
                    {
                        try
                        {
                            //StartPresetAsync();
                            //_ = StartTasksAsync();
                            //SaveStartCommandToDb();
                            _configurationService.SaveStartPresetCommandToDb();
                        }
                        catch (Exception e)
                        {
                            commandStatus = "error";
                            _logger.LogError(e, "Unexpected error");
                        }

                        if (deserializedCmdData.ContainsKey("response"))
                        {
                            deserializedCmdData["response"] = commandStatus;
                        }
                        else
                        {
                            var commandResponse = new JProperty("response", commandStatus);
                            deserializedCmdData.Add(commandResponse);
                        }

                        var serializedData = JsonConvert.SerializeObject(deserializedCmdData);
                        result = serializedData;
                        if (!string.IsNullOrEmpty(_standaloneConfigDTO.mqttControlResponseTopic))
                            if (_standaloneConfigDTO.mqttControlResponseTopic.Contains("{{deviceId}}"))
                                _standaloneConfigDTO.mqttControlResponseTopic =
                                    _standaloneConfigDTO.mqttControlResponseTopic.Replace("{{deviceId}}",
                                        _standaloneConfigDTO.readerName);

                        if (shouldPublishToMqtt)
                        {
                            var qos = 0;
                            var retain = false;
                            var mqttQualityOfServiceLevel = MqttQualityOfServiceLevel.AtMostOnce;
                            try
                            {
                                _ = int.TryParse(_standaloneConfigDTO.mqttControlResponseQoS, out qos);
                                _ = bool.TryParse(_standaloneConfigDTO.mqttControlResponseRetainMessages, out retain);

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

                            var mqttCommandResponseTopic = $"{_standaloneConfigDTO.mqttControlResponseTopic}";
                            await _mqttService.PublishAsync(mqttCommandResponseTopic, serializedData,
                                _iotDeviceInterfaceClient.MacAddress, mqttQualityOfServiceLevel, retain);
                        }

                    }
                    else if ("stop".Equals(commandValue))
                    {
                        try
                        {
                            //StopPresetAsync();
                            //_ = StopTasksAsync();
                            //SaveStopCommandToDb();
                            _configurationService.SaveStopPresetCommandToDb();
                        }
                        catch (Exception e)
                        {
                            commandStatus = "error";
                            _logger.LogError(e, "Unexpected error");
                        }



                        if (deserializedCmdData.ContainsKey("response"))
                        {
                            deserializedCmdData["response"] = commandStatus;
                        }
                        else
                        {
                            var commandResponse = new JProperty("response", commandStatus);
                            deserializedCmdData.Add(commandResponse);
                        }

                        var serializedData = JsonConvert.SerializeObject(deserializedCmdData);
                        result = serializedData;

                        if (!string.IsNullOrEmpty(_standaloneConfigDTO.mqttControlResponseTopic))
                            if (_standaloneConfigDTO.mqttControlResponseTopic.Contains("{{deviceId}}"))
                                _standaloneConfigDTO.mqttControlResponseTopic =
                                    _standaloneConfigDTO.mqttControlResponseTopic.Replace("{{deviceId}}",
                                        _standaloneConfigDTO.readerName);

                        if (shouldPublishToMqtt)
                        {
                            var qos = 0;
                            var retain = false;
                            var mqttQualityOfServiceLevel = MqttQualityOfServiceLevel.AtMostOnce;
                            try
                            {
                                _ = int.TryParse(_standaloneConfigDTO.mqttControlResponseQoS, out qos);
                                _ = bool.TryParse(_standaloneConfigDTO.mqttControlResponseRetainMessages, out retain);

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

                            var mqttCommandResponseTopic = $"{_standaloneConfigDTO.mqttControlResponseTopic}";
                            await _mqttService.PublishAsync(mqttCommandResponseTopic,
                                serializedData,
                                _iotDeviceInterfaceClient.MacAddress,
                                mqttQualityOfServiceLevel,
                                retain);

                        }

                    }
                    else if ("reboot".Equals(commandValue))
                    {
                        var resultPayload = "";
                        try
                        {
                            resultPayload = await CallIotRestFulInterfaceAsync(controlCommandJson, "/system/reboot", "POST",
                                $"{_standaloneConfigDTO.mqttControlResponseTopic}", false);
                        }
                        catch (Exception e)
                        {
                            commandStatus = "error";
                            _logger.LogError(e, "Unexpected error");
                        }

                        if (deserializedCmdData.ContainsKey("payload"))
                        {
                            deserializedCmdData["payload"] = JObject.Parse(resultPayload);
                        }
                        else if (deserializedCmdData.ContainsKey("fields"))
                        {
                            deserializedCmdData["fields"] = JObject.Parse(resultPayload);
                        }
                        else
                        {
                            var commandResponsePayload = new JProperty("payload", JObject.Parse(resultPayload));
                            deserializedCmdData.Add(commandResponsePayload);
                        }

                        if (deserializedCmdData.ContainsKey("response"))
                        {
                            deserializedCmdData["response"] = commandStatus;
                        }
                        else
                        {
                            var commandResponse = new JProperty("response", commandStatus);
                            deserializedCmdData.Add(commandResponse);
                        }

                        var serializedData = JsonConvert.SerializeObject(deserializedCmdData);
                        result = serializedData;

                        //var mqttCommandResponseTopic = $"{_standaloneConfigDTO.mqttCommandTopic}/response";
                        if (!string.IsNullOrEmpty(_standaloneConfigDTO.mqttControlResponseTopic))
                            if (_standaloneConfigDTO.mqttControlResponseTopic.Contains("{{deviceId}}"))
                                _standaloneConfigDTO.mqttControlResponseTopic =
                                    _standaloneConfigDTO.mqttControlResponseTopic.Replace("{{deviceId}}",
                                        _standaloneConfigDTO.readerName);

                        if (shouldPublishToMqtt)
                        {
                            var qos = 0;
                            var retain = false;
                            var mqttQualityOfServiceLevel = MqttQualityOfServiceLevel.AtMostOnce;
                            try
                            {
                                _ = int.TryParse(_standaloneConfigDTO.mqttControlResponseQoS, out qos);
                                _ = bool.TryParse(_standaloneConfigDTO.mqttControlResponseRetainMessages, out retain);

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

                            var mqttCommandResponseTopic = $"{_standaloneConfigDTO.mqttControlResponseTopic}";
                            await _mqttService.PublishAsync(mqttCommandResponseTopic,
                                serializedData,
                                _iotDeviceInterfaceClient.MacAddress,
                                mqttQualityOfServiceLevel,
                                retain);
                        }

                    }
                    else if ("mode".Equals(commandValue))
                    {
                        var modeCmdResult = "success";
                        try
                        {
                            if (deserializedCmdData.ContainsKey("payload") || deserializedCmdData.ContainsKey("fields"))
                                try
                                {
                                    modeCmdResult = ProcessMqttModeJsonCommand(controlCommandJson);
                                    if (!"success".Equals(modeCmdResult)) commandStatus = "error";
                                }
                                catch (Exception ex)
                                {
                                    commandStatus = "error";
                                    _logger.LogError(ex, "Unexpected error");
                                }
                        }
                        catch (Exception e)
                        {
                            commandStatus = "error";
                            _logger.LogError(e, "Unexpected error");
                        }

                        if (deserializedCmdData.ContainsKey("response"))
                        {
                            deserializedCmdData["response"] = commandStatus;
                        }
                        else
                        {
                            var commandResponse = new JProperty("response", commandStatus);
                            deserializedCmdData.Add(commandResponse);
                        }

                        if (deserializedCmdData.ContainsKey("message"))
                        {
                            deserializedCmdData["message"] = modeCmdResult;
                        }
                        else
                        {
                            var commandResponse = new JProperty("message", modeCmdResult);
                            deserializedCmdData.Add(commandResponse);
                        }

                        var serializedData = JsonConvert.SerializeObject(deserializedCmdData);

                        if (!string.IsNullOrEmpty(_standaloneConfigDTO.mqttControlResponseTopic))
                            if (_standaloneConfigDTO.mqttControlResponseTopic.Contains("{{deviceId}}"))
                                _standaloneConfigDTO.mqttControlResponseTopic =
                                    _standaloneConfigDTO.mqttControlResponseTopic.Replace("{{deviceId}}",
                                        _standaloneConfigDTO.readerName);
                        var qos = 0;
                        var retain = false;
                        var mqttQualityOfServiceLevel = MqttQualityOfServiceLevel.AtMostOnce;
                        try
                        {
                            _ = int.TryParse(_standaloneConfigDTO.mqttControlResponseQoS, out qos);
                            _ = bool.TryParse(_standaloneConfigDTO.mqttControlResponseRetainMessages, out retain);

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

                        var mqttCommandResponseTopic = $"{_standaloneConfigDTO.mqttControlResponseTopic}";
                        await _mqttService.PublishAsync(mqttCommandResponseTopic,
                            serializedData,
                            _iotDeviceInterfaceClient.MacAddress,
                            mqttQualityOfServiceLevel,
                            retain);

                        //if ("success".Equals(commandStatus))
                        //{
                        //    // exits the app to reload the settings
                        //    Task.Delay(3000);
                        //    Environment.Exit(0);
                        //}
                    }
                    else if ("set-gpo".Equals(commandValue))
                    {
                        var gpoCmdResult = "success";
                        try
                        {
                            if (deserializedCmdData.ContainsKey("payload") || deserializedCmdData.ContainsKey("fields"))
                                try
                                {
                                    JObject commandPayloadJObject = [];
                                    if (deserializedCmdData.ContainsKey("payload"))
                                    {
                                        commandPayloadJObject = deserializedCmdData["payload"].Value<JObject>();
                                    }
                                    else if (deserializedCmdData.ContainsKey("fields"))
                                    {
                                        commandPayloadJObject = deserializedCmdData["fields"].Value<JObject>();
                                    }
                                    if (commandPayloadJObject.ContainsKey("gpoConfigurations"))
                                        try
                                        {
                                            var impinjIotApiRequest = JsonConvert.SerializeObject(commandPayloadJObject);
                                            gpoCmdResult = await CallIotRestFulInterfaceAsync(impinjIotApiRequest, "/device/gpos", "PUT", _standaloneConfigDTO.mqttControlResponseTopic, false);

                                        }
                                        catch (Exception ex)
                                        {
                                            commandStatus = "error setting GPO status.";
                                            _logger.LogError(ex,
                                                "set-gpo: Unexpected error" + ex.Message);

                                        }


                                    if (!"success".Equals(commandStatus)) commandStatus = "error";
                                }
                                catch (Exception ex)
                                {
                                    commandStatus = "error";
                                    _logger.LogError(ex, "Unexpected error");
                                }
                        }
                        catch (Exception e)
                        {
                            commandStatus = "error";
                            _logger.LogError(e, "Unexpected error");
                        }

                        if (deserializedCmdData.ContainsKey("response"))
                        {
                            deserializedCmdData["response"] = commandStatus;
                        }
                        else
                        {
                            var commandResponse = new JProperty("response", commandStatus);
                            deserializedCmdData.Add(commandResponse);
                        }

                        if (deserializedCmdData.ContainsKey("message"))
                        {
                            deserializedCmdData["message"] = gpoCmdResult;
                        }
                        else
                        {
                            var commandResponse = new JProperty("message", gpoCmdResult);
                            deserializedCmdData.Add(commandResponse);
                        }

                        var serializedData = JsonConvert.SerializeObject(deserializedCmdData);
                        result = serializedData;

                        if (!string.IsNullOrEmpty(_standaloneConfigDTO.mqttControlResponseTopic))
                            if (_standaloneConfigDTO.mqttControlResponseTopic.Contains("{{deviceId}}"))
                                _standaloneConfigDTO.mqttControlResponseTopic =
                                    _standaloneConfigDTO.mqttControlResponseTopic.Replace("{{deviceId}}",
                                        _standaloneConfigDTO.readerName);
                        if (shouldPublishToMqtt)
                        {
                            var qos = 0;
                            var retain = false;
                            var mqttQualityOfServiceLevel = MqttQualityOfServiceLevel.AtMostOnce;
                            try
                            {
                                _ = int.TryParse(_standaloneConfigDTO.mqttControlResponseQoS, out qos);
                                _ = bool.TryParse(_standaloneConfigDTO.mqttControlResponseRetainMessages, out retain);

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

                            var mqttCommandResponseTopic = $"{_standaloneConfigDTO.mqttControlResponseTopic}";
                            await _mqttService.PublishAsync(mqttCommandResponseTopic,
                                serializedData,
                                _iotDeviceInterfaceClient.MacAddress,
                                mqttQualityOfServiceLevel,
                                retain);


                        }

                        //if ("success".Equals(commandStatus))
                        //{
                        //    // exits the app to reload the settings
                        //    Task.Delay(3000);
                        //    Environment.Exit(0);
                        //}
                    }
                    else if ("get-gpo".Equals(commandValue))
                    {
                        var gpoCmdResult = "success";
                        try
                        {

                            try
                            {
                                gpoCmdResult = await CallIotRestFulInterfaceAsync("", "/device/gpos", "GET", _standaloneConfigDTO.mqttControlResponseTopic, false);

                            }
                            catch (Exception ex)
                            {
                                commandStatus = "error getting GPO status.";
                                _logger.LogError(ex,
                                    "get-gpo: Unexpected error" + ex.Message);

                            }


                            if (!"success".Equals(commandStatus)) commandStatus = "error";
                        }
                        catch (Exception ex)
                        {
                            commandStatus = "error";
                            _logger.LogError(ex, "Unexpected error");
                        }

                        if (deserializedCmdData.ContainsKey("response"))
                        {
                            deserializedCmdData["response"] = commandStatus;
                        }
                        else
                        {
                            var commandResponse = new JProperty("response", commandStatus);
                            deserializedCmdData.Add(commandResponse);
                        }


                        var gpoCmdResultObj = JToken.Parse(gpoCmdResult);
                        if (deserializedCmdData.ContainsKey("message"))
                        {
                            deserializedCmdData["message"] = gpoCmdResultObj;
                        }
                        else
                        {
                            var commandResponse = new JProperty("message", gpoCmdResultObj);
                            deserializedCmdData.Add(commandResponse);
                        }

                        var serializedData = JsonConvert.SerializeObject(deserializedCmdData);
                        result = serializedData;

                        if (!string.IsNullOrEmpty(_standaloneConfigDTO.mqttControlResponseTopic))
                            if (_standaloneConfigDTO.mqttControlResponseTopic.Contains("{{deviceId}}"))
                                _standaloneConfigDTO.mqttControlResponseTopic =
                                    _standaloneConfigDTO.mqttControlResponseTopic.Replace("{{deviceId}}",
                                        _standaloneConfigDTO.readerName);

                        if (shouldPublishToMqtt)
                        {
                            var qos = 0;
                            var retain = false;
                            var mqttQualityOfServiceLevel = MqttQualityOfServiceLevel.AtMostOnce;
                            try
                            {
                                _ = int.TryParse(_standaloneConfigDTO.mqttControlResponseQoS, out qos);
                                _ = bool.TryParse(_standaloneConfigDTO.mqttControlResponseRetainMessages, out retain);

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

                            var mqttCommandResponseTopic = $"{_standaloneConfigDTO.mqttControlResponseTopic}";
                            await _mqttService.PublishAsync(mqttCommandResponseTopic,
                                serializedData,
                                _iotDeviceInterfaceClient.MacAddress,
                                mqttQualityOfServiceLevel,
                                retain);


                        }
                    }
                    else if ("impinj_iot_device_interface".Equals(commandValue))
                    {
                        var resultPayload = "";
                        try
                        {
                            if (deserializedCmdData.ContainsKey("payload") || deserializedCmdData.ContainsKey("fields"))
                            {
                                var impinjIotApiEndpoint = "";
                                var impinjIotApiVerb = "";
                                var impinjIotApiRequest = "";
                                JObject commandPayloadJObject = [];
                                if (deserializedCmdData.ContainsKey("payload"))
                                {
                                    commandPayloadJObject = deserializedCmdData["payload"].Value<JObject>();
                                }
                                else if (deserializedCmdData.ContainsKey("fields"))
                                {
                                    commandPayloadJObject = deserializedCmdData["fields"].Value<JObject>();
                                }

                                if (commandPayloadJObject != null &&
                                    commandPayloadJObject.ContainsKey("impinjIotApiEndpoint"))
                                {
                                    impinjIotApiEndpoint = commandPayloadJObject["impinjIotApiEndpoint"]
                                        .Value<string>();
                                    if (!string.IsNullOrEmpty(impinjIotApiEndpoint))
                                        _logger.LogInformation($"impinjIotApiEndpoint {impinjIotApiEndpoint}");
                                }

                                if (commandPayloadJObject != null &&
                                    commandPayloadJObject.ContainsKey("impinjIotApiVerb"))
                                {
                                    impinjIotApiVerb = commandPayloadJObject["impinjIotApiVerb"]
                                        .Value<string>();
                                    if (!string.IsNullOrEmpty(impinjIotApiVerb))
                                        _logger.LogInformation($"impinjIotApiVerb {impinjIotApiVerb}");
                                }

                                if (commandPayloadJObject != null &&
                                    commandPayloadJObject.ContainsKey("impinjIotApiRequest"))
                                {
                                    var impinjIotApiRequestJObject =
                                        commandPayloadJObject["impinjIotApiRequest"].Value<JObject>();
                                    if (impinjIotApiRequestJObject != null)
                                    {
                                        impinjIotApiRequest =
                                            JsonConvert.SerializeObject(impinjIotApiRequestJObject);
                                        if (!string.IsNullOrEmpty(impinjIotApiRequest))
                                            _logger.LogInformation(
                                                $"impinjIotApiRequest {impinjIotApiRequest}");
                                    }
                                }

                                try
                                {
                                    resultPayload = await CallIotRestFulInterfaceAsync(impinjIotApiRequest,
                                        impinjIotApiEndpoint, impinjIotApiVerb,
                                        _standaloneConfigDTO.mqttControlResponseTopic, false);
                                }
                                catch (Exception ex)
                                {
                                    commandStatus = "error";
                                    _logger.LogError(ex, "Unexpected error");
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            commandStatus = "error";
                            _logger.LogError(e, "Unexpected error");
                        }

                        if (deserializedCmdData.ContainsKey("payload"))
                        {
                            deserializedCmdData["payload"] = JObject.Parse(resultPayload);
                        }
                        else if (deserializedCmdData.ContainsKey("fields"))
                        {
                            deserializedCmdData["fields"] = JObject.Parse(resultPayload);
                        }
                        else
                        {
                            var commandResponsePayload = new JProperty("payload", JObject.Parse(resultPayload));
                            deserializedCmdData.Add(commandResponsePayload);
                        }

                        if (deserializedCmdData.ContainsKey("response"))
                        {
                            deserializedCmdData["response"] = commandStatus;
                        }
                        else
                        {
                            var commandResponse = new JProperty("response", commandStatus);
                            deserializedCmdData.Add(commandResponse);
                        }

                        var serializedData = JsonConvert.SerializeObject(deserializedCmdData);
                        result = serializedData;

                        if (!string.IsNullOrEmpty(_standaloneConfigDTO.mqttControlResponseTopic))
                            if (_standaloneConfigDTO.mqttControlResponseTopic.Contains("{{deviceId}}"))
                                _standaloneConfigDTO.mqttControlResponseTopic =
                                    _standaloneConfigDTO.mqttControlResponseTopic.Replace("{{deviceId}}",
                                        _standaloneConfigDTO.readerName);

                        if (shouldPublishToMqtt)
                        {
                            var qos = 0;
                            var retain = false;
                            var mqttQualityOfServiceLevel = MqttQualityOfServiceLevel.AtMostOnce;
                            try
                            {
                                _ = int.TryParse(_standaloneConfigDTO.mqttControlResponseQoS, out qos);
                                _ = bool.TryParse(_standaloneConfigDTO.mqttControlResponseRetainMessages, out retain);

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

                            var mqttCommandResponseTopic = $"{_standaloneConfigDTO.mqttControlResponseTopic}";
                            await _mqttService.PublishAsync(mqttCommandResponseTopic,
                                serializedData,
                                _iotDeviceInterfaceClient.MacAddress,
                                mqttQualityOfServiceLevel,
                                retain);



                        }

                    }
                }
        }
        catch (Exception)
        {


        }

        return result;
#pragma warning restore CS8600, CS8601, CS8602, CS8603, CS8604, CS8625, CS8629 // Dereference of a possibly null reference.
    }
    public async Task<string> ProcessMqttManagementCommandJsonAsync(string managementCommandJson, bool shouldPublishToMqtt)
    {
#pragma warning disable CS8600, CS8601, CS8602, CS8603, CS8604, CS8625, CS8629 // Dereference of a possibly null reference.
        string result = "{\"status\": \"success\"}";
        try
        {
            if (managementCommandJson.StartsWith("{") && !managementCommandJson.Contains("antennaFilter"))
            {
                var deserializedCmdData = JsonConvert.DeserializeObject<JObject>(managementCommandJson);
                if (deserializedCmdData != null)
                    if (deserializedCmdData.ContainsKey("command") || deserializedCmdData.ContainsKey("cmd"))
                    {
                        var commandValue = "";
                        var commandStatus = "success";
                        if (deserializedCmdData.ContainsKey("command"))
                        {
                            commandValue = deserializedCmdData["command"].Value<string>();
                        }
                        if (deserializedCmdData.ContainsKey("cmd"))
                        {
                            commandValue = deserializedCmdData["cmd"].Value<string>();
                        }
                        try
                        {
                            if (deserializedCmdData.ContainsKey("command_id") || deserializedCmdData.ContainsKey("id"))
                            {
                                var commandIdValue = "";
                                var previousCommandId = _configurationService.ReadMqttCommandIdFromFile().Result;
                                if (deserializedCmdData.ContainsKey("command_id"))
                                {
                                    commandIdValue = deserializedCmdData["command_id"].Value<string>();
                                }
                                if (deserializedCmdData.ContainsKey("id"))
                                {
                                    commandIdValue = deserializedCmdData["id"].Value<string>();
                                }
                                if (!string.IsNullOrEmpty(commandIdValue))
                                {
                                    _ = await _configurationService.WriteMqttCommandIdToFile(commandIdValue);
                                    if (!string.IsNullOrEmpty(previousCommandId))
                                        if (previousCommandId.Trim().Equals(commandIdValue.Trim()))
                                        {
                                            commandStatus = "error";

                                            if (!string.IsNullOrEmpty(_standaloneConfigDTO
                                                    .mqttManagementResponseTopic))
                                                if (_standaloneConfigDTO.mqttManagementResponseTopic.Contains(
                                                        "{{deviceId}}"))
                                                    _standaloneConfigDTO.mqttManagementResponseTopic =
                                                        _standaloneConfigDTO.mqttManagementResponseTopic.Replace(
                                                            "{{deviceId}}", _standaloneConfigDTO.readerName);


                                            if (deserializedCmdData.ContainsKey("response"))
                                            {
                                                deserializedCmdData["response"] = commandStatus;
                                            }
                                            else
                                            {
                                                var commandResponse = new JProperty("response", commandStatus);
                                                deserializedCmdData.Add(commandResponse);
                                            }

                                            var payloadCommandStatus = new Dictionary<string, string>
                                            {
                                                {
                                                    "detail",
                                                    $"command_id {commandIdValue} already processed."
                                                }
                                            };
                                            if (deserializedCmdData.ContainsKey("message"))
                                            {
                                                deserializedCmdData["message"] =
                                                    JObject.FromObject(payloadCommandStatus);
                                            }
                                            else
                                            {
                                                var commandResponsePayload = new JProperty("message",
                                                    JObject.FromObject(payloadCommandStatus));
                                                deserializedCmdData.Add(commandResponsePayload);
                                            }

                                            var serializedData = JsonConvert.SerializeObject(deserializedCmdData);
                                            result = serializedData;
                                            if (shouldPublishToMqtt)
                                            {
                                                var qos = 0;
                                                var retain = false;
                                                var mqttQualityOfServiceLevel = MqttQualityOfServiceLevel.AtMostOnce;
                                                try
                                                {
                                                    _ = int.TryParse(_standaloneConfigDTO.mqttManagementResponseQoS,
                                                        out qos);
                                                    _ = bool.TryParse(
                                                        _standaloneConfigDTO.mqttManagementResponseRetainMessages,
                                                        out retain);

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

                                                var mqttCommandResponseTopic =
                                                    $"{_standaloneConfigDTO.mqttManagementResponseTopic}";
                                                await _mqttService.PublishAsync(mqttCommandResponseTopic,
                                                    serializedData,
                                                    _iotDeviceInterfaceClient.MacAddress,
                                                    mqttQualityOfServiceLevel,
                                                    retain);


                                            }

                                            return result;
                                        }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Unexpected error");
                        }


                        if ("start".Equals(commandValue))
                        {
                            try
                            {
                                //StartPresetAsync();
                                //_ = StartTasksAsync();
                                //SaveStartCommandToDb();
                                _configurationService.SaveStartPresetCommandToDb();
                            }
                            catch (Exception e)
                            {
                                commandStatus = "error";
                                _logger.LogError(e, "Unexpected error");
                            }

                            if (deserializedCmdData.ContainsKey("response"))
                            {
                                deserializedCmdData["response"] = commandStatus;
                            }
                            else
                            {
                                var commandResponse = new JProperty("response", commandStatus);
                                deserializedCmdData.Add(commandResponse);
                            }

                            var serializedData = JsonConvert.SerializeObject(deserializedCmdData);
                            result = serializedData;


                            if (!string.IsNullOrEmpty(_standaloneConfigDTO.mqttManagementResponseTopic))
                                if (_standaloneConfigDTO.mqttManagementResponseTopic.Contains("{{deviceId}}"))
                                    _standaloneConfigDTO.mqttManagementResponseTopic =
                                        _standaloneConfigDTO.mqttManagementResponseTopic.Replace("{{deviceId}}",
                                            _standaloneConfigDTO.readerName);

                            if (shouldPublishToMqtt)
                            {
                                var qos = 0;
                                var retain = false;
                                var mqttQualityOfServiceLevel = MqttQualityOfServiceLevel.AtMostOnce;
                                try
                                {
                                    _ = int.TryParse(_standaloneConfigDTO.mqttManagementResponseQoS, out qos);
                                    _ = bool.TryParse(_standaloneConfigDTO.mqttManagementResponseRetainMessages,
                                        out retain);

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

                                var mqttCommandResponseTopic = $"{_standaloneConfigDTO.mqttManagementResponseTopic}";
                                await _mqttService.PublishAsync(mqttCommandResponseTopic,
                                serializedData,
                                _iotDeviceInterfaceClient.MacAddress,
                                mqttQualityOfServiceLevel,
                                retain);



                            }

                        }
                        else if ("stop".Equals(commandValue))
                        {
                            try
                            {
                                //StopPresetAsync();
                                //_ = StopTasksAsync();
                                //SaveStopCommandToDb();
                                _configurationService.SaveStopPresetCommandToDb();
                            }
                            catch (Exception e)
                            {
                                commandStatus = "error";
                                _logger.LogError(e, "Unexpected error");
                            }

                            if (deserializedCmdData.ContainsKey("response"))
                            {
                                deserializedCmdData["response"] = commandStatus;
                            }
                            else
                            {
                                var commandResponse = new JProperty("response", commandStatus);
                                deserializedCmdData.Add(commandResponse);
                            }

                            var serializedData = JsonConvert.SerializeObject(deserializedCmdData);

                            result = serializedData;


                            if (!string.IsNullOrEmpty(_standaloneConfigDTO.mqttManagementResponseTopic))
                                if (_standaloneConfigDTO.mqttManagementResponseTopic.Contains("{{deviceId}}"))
                                    _standaloneConfigDTO.mqttManagementResponseTopic =
                                        _standaloneConfigDTO.mqttManagementResponseTopic.Replace("{{deviceId}}",
                                            _standaloneConfigDTO.readerName);

                            if (shouldPublishToMqtt)
                            {
                                var qos = 0;
                                var retain = false;
                                var mqttQualityOfServiceLevel = MqttQualityOfServiceLevel.AtMostOnce;
                                try
                                {
                                    _ = int.TryParse(_standaloneConfigDTO.mqttManagementResponseQoS, out qos);
                                    _ = bool.TryParse(_standaloneConfigDTO.mqttManagementResponseRetainMessages,
                                        out retain);

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

                                var mqttCommandResponseTopic = $"{_standaloneConfigDTO.mqttManagementResponseTopic}";
                                await _mqttService.PublishAsync(mqttCommandResponseTopic,
                                    serializedData,
                                    _iotDeviceInterfaceClient.MacAddress,
                                    mqttQualityOfServiceLevel,
                                    retain);



                            }


                        }
                        else if ("status".Equals(commandValue))
                        {
                            try
                            {
                                ProcessAppStatus();
                            }
                            catch (Exception e)
                            {
                                commandStatus = "error";
                                _logger.LogError(e, "Unexpected error");
                            }

                            if (deserializedCmdData.ContainsKey("response"))
                            {
                                deserializedCmdData["response"] = commandStatus;
                            }
                            else
                            {
                                var commandResponse = new JProperty("response", commandStatus);
                                deserializedCmdData.Add(commandResponse);
                            }

                            var serializedData = JsonConvert.SerializeObject(deserializedCmdData);

                            result = serializedData;

                            if (!string.IsNullOrEmpty(_standaloneConfigDTO.mqttManagementResponseTopic))
                                if (_standaloneConfigDTO.mqttManagementResponseTopic.Contains("{{deviceId}}"))
                                    _standaloneConfigDTO.mqttManagementResponseTopic =
                                        _standaloneConfigDTO.mqttManagementResponseTopic.Replace("{{deviceId}}",
                                            _standaloneConfigDTO.readerName);

                            if (shouldPublishToMqtt)
                            {
                                var qos = 0;
                                var retain = false;
                                var mqttQualityOfServiceLevel = MqttQualityOfServiceLevel.AtMostOnce;
                                try
                                {
                                    _ = int.TryParse(_standaloneConfigDTO.mqttManagementResponseQoS, out qos);
                                    _ = bool.TryParse(_standaloneConfigDTO.mqttManagementResponseRetainMessages,
                                        out retain);

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

                                var mqttCommandResponseTopic = $"{_standaloneConfigDTO.mqttManagementResponseTopic}";
                                await _mqttService.PublishAsync(mqttCommandResponseTopic,
                                serializedData,
                                _iotDeviceInterfaceClient.MacAddress,
                                mqttQualityOfServiceLevel,
                                retain);



                            }


                        }
                        else if ("status-detailed".Equals(commandValue))
                        {
                            try
                            {
                                ProcessAppStatusDetailed();
                            }
                            catch (Exception e)
                            {
                                commandStatus = "error";
                                _logger.LogError(e, "Unexpected error");
                            }

                            if (deserializedCmdData.ContainsKey("response"))
                            {
                                deserializedCmdData["response"] = commandStatus;
                            }
                            else
                            {
                                var commandResponse = new JProperty("response", commandStatus);
                                deserializedCmdData.Add(commandResponse);
                            }

                            var serializedData = JsonConvert.SerializeObject(deserializedCmdData);

                            result = serializedData;


                            if (!string.IsNullOrEmpty(_standaloneConfigDTO.mqttManagementResponseTopic))
                                if (_standaloneConfigDTO.mqttManagementResponseTopic.Contains("{{deviceId}}"))
                                    _standaloneConfigDTO.mqttManagementResponseTopic =
                                        _standaloneConfigDTO.mqttManagementResponseTopic.Replace("{{deviceId}}",
                                            _standaloneConfigDTO.readerName);

                            if (shouldPublishToMqtt)
                            {
                                var qos = 0;
                                var retain = false;
                                var mqttQualityOfServiceLevel = MqttQualityOfServiceLevel.AtMostOnce;
                                try
                                {
                                    _ = int.TryParse(_standaloneConfigDTO.mqttManagementResponseQoS, out qos);
                                    _ = bool.TryParse(_standaloneConfigDTO.mqttManagementResponseRetainMessages,
                                        out retain);

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

                                var mqttCommandResponseTopic = $"{_standaloneConfigDTO.mqttManagementResponseTopic}";
                                await _mqttService.PublishAsync(mqttCommandResponseTopic,
                                    serializedData,
                                    _iotDeviceInterfaceClient.MacAddress,
                                    mqttQualityOfServiceLevel,
                                    retain);



                            }


                        }
                        else if ("update-admin-password".Equals(commandValue))
                        {
                            try
                            {
                                commandStatus = ProcessAdminPasswordUpdate(managementCommandJson);
                                //if (!"success".Equals(updateAdminPwdResult)) commandStatus = "error";
                            }
                            catch (Exception e)
                            {
                                commandStatus = "error";
                                _logger.LogError(e, "Unexpected error");
                            }

                            if (deserializedCmdData.ContainsKey("response"))
                            {
                                deserializedCmdData["response"] = commandStatus;
                            }
                            else
                            {
                                var commandResponse = new JProperty("response", commandStatus);
                                deserializedCmdData.Add(commandResponse);
                            }
                            try
                            {

                                deserializedCmdData.Property("payload").Descendants()
                                    .OfType<JProperty>()
                                    .Where(attr => attr.Name.StartsWith("currentPassword"))
                                    .ToList()
                                    .ForEach(attr => attr.Remove());

                            }
                            catch (Exception)
                            {


                            }
                            try
                            {
                                deserializedCmdData.Property("payload").Descendants()
                                    .OfType<JProperty>()
                                    .Where(attr => attr.Name.StartsWith("newPassword"))
                                    .ToList()
                                    .ForEach(attr => attr.Remove());

                            }
                            catch (Exception)
                            {


                            }

                            var serializedData = JsonConvert.SerializeObject(deserializedCmdData);

                            result = serializedData;


                            if (!string.IsNullOrEmpty(_standaloneConfigDTO.mqttManagementResponseTopic))
                                if (_standaloneConfigDTO.mqttManagementResponseTopic.Contains("{{deviceId}}"))
                                    _standaloneConfigDTO.mqttManagementResponseTopic =
                                        _standaloneConfigDTO.mqttManagementResponseTopic.Replace("{{deviceId}}",
                                            _standaloneConfigDTO.readerName);

                            if (shouldPublishToMqtt)
                            {
                                var qos = 0;
                                var retain = false;
                                var mqttQualityOfServiceLevel = MqttQualityOfServiceLevel.AtMostOnce;
                                try
                                {
                                    _ = int.TryParse(_standaloneConfigDTO.mqttManagementResponseQoS, out qos);
                                    _ = bool.TryParse(_standaloneConfigDTO.mqttManagementResponseRetainMessages,
                                        out retain);

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

                                var mqttCommandResponseTopic = $"{_standaloneConfigDTO.mqttManagementResponseTopic}";
                                await _mqttService.PublishAsync(mqttCommandResponseTopic,
                                    serializedData,
                                    _iotDeviceInterfaceClient.MacAddress,
                                    mqttQualityOfServiceLevel,
                                    retain);


                            }


                        }
                        else if ("update-rshell-password".Equals(commandValue))
                        {
                            try
                            {
                                commandStatus = ProcessRShellPasswordUpdate(managementCommandJson);
                                //if (!"success".Equals(updateAdminPwdResult)) commandStatus = "error";
                            }
                            catch (Exception e)
                            {
                                commandStatus = "error";
                                _logger.LogError(e, "Unexpected error");
                            }

                            if (deserializedCmdData.ContainsKey("response"))
                            {
                                deserializedCmdData["response"] = commandStatus;
                            }
                            else
                            {
                                var commandResponse = new JProperty("response", commandStatus);
                                deserializedCmdData.Add(commandResponse);
                            }
                            try
                            {
                                deserializedCmdData.Property("payload").Descendants()
                                    .OfType<JProperty>()
                                    .Where(attr => attr.Name.StartsWith("currentPassword"))
                                    .ToList()
                                    .ForEach(attr => attr.Remove());
                            }
                            catch (Exception)
                            {


                            }
                            try
                            {
                                deserializedCmdData.Property("payload").Descendants()
                                    .OfType<JProperty>()
                                    .Where(attr => attr.Name.StartsWith("newPassword"))
                                    .ToList()
                                    .ForEach(attr => attr.Remove());
                            }
                            catch (Exception)
                            {


                            }

                            var serializedData = JsonConvert.SerializeObject(deserializedCmdData);

                            result = serializedData;

                            if (!string.IsNullOrEmpty(_standaloneConfigDTO.mqttManagementResponseTopic))
                                if (_standaloneConfigDTO.mqttManagementResponseTopic.Contains("{{deviceId}}"))
                                    _standaloneConfigDTO.mqttManagementResponseTopic =
                                        _standaloneConfigDTO.mqttManagementResponseTopic.Replace("{{deviceId}}",
                                            _standaloneConfigDTO.readerName);

                            if (shouldPublishToMqtt)
                            {
                                var qos = 0;
                                var retain = false;
                                var mqttQualityOfServiceLevel = MqttQualityOfServiceLevel.AtMostOnce;
                                try
                                {
                                    _ = int.TryParse(_standaloneConfigDTO.mqttManagementResponseQoS, out qos);
                                    _ = bool.TryParse(_standaloneConfigDTO.mqttManagementResponseRetainMessages,
                                        out retain);

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

                                var mqttCommandResponseTopic = $"{_standaloneConfigDTO.mqttManagementResponseTopic}";
                                await _mqttService.PublishAsync(mqttCommandResponseTopic,
                                serializedData,
                                _iotDeviceInterfaceClient.MacAddress,
                                mqttQualityOfServiceLevel,
                                retain);
                            }


                        }
                        else if ("reboot".Equals(commandValue))
                        {
                            var resultPayload = "";
                            try
                            {

                                resultPayload = await CallIotRestFulInterfaceAsync(managementCommandJson, "/system/reboot", "POST",
                                    $"{_standaloneConfigDTO.mqttManagementResponseTopic}", false);

                            }
                            catch (Exception e)
                            {
                                commandStatus = "error";
                                _logger.LogError(e, "Unexpected error");
                            }

                            if (deserializedCmdData.ContainsKey("payload") || deserializedCmdData.ContainsKey("fields"))
                            {
                                if (deserializedCmdData.ContainsKey("payload"))
                                {
                                    deserializedCmdData["payload"] = JObject.Parse(resultPayload);
                                }
                                else if (deserializedCmdData.ContainsKey("fields"))
                                {
                                    deserializedCmdData["fields"] = JObject.Parse(resultPayload);
                                }
                            }
                            else
                            {
                                var commandResponsePayload = new JProperty("payload", JObject.Parse(resultPayload));
                                deserializedCmdData.Add(commandResponsePayload);
                            }

                            if (deserializedCmdData.ContainsKey("response"))
                            {
                                deserializedCmdData["response"] = commandStatus;
                            }
                            else
                            {
                                var commandResponse = new JProperty("response", commandStatus);
                                deserializedCmdData.Add(commandResponse);
                            }

                            var serializedData = JsonConvert.SerializeObject(deserializedCmdData);

                            result = serializedData;

                            if (!string.IsNullOrEmpty(_standaloneConfigDTO.mqttManagementResponseTopic))
                                if (_standaloneConfigDTO.mqttManagementResponseTopic.Contains("{{deviceId}}"))
                                    _standaloneConfigDTO.mqttManagementResponseTopic =
                                        _standaloneConfigDTO.mqttManagementResponseTopic.Replace("{{deviceId}}",
                                            _standaloneConfigDTO.readerName);

                            if (shouldPublishToMqtt)
                            {
                                var qos = 0;
                                var retain = false;
                                var mqttQualityOfServiceLevel = MqttQualityOfServiceLevel.AtMostOnce;
                                try
                                {
                                    _ = int.TryParse(_standaloneConfigDTO.mqttManagementResponseQoS, out qos);
                                    _ = bool.TryParse(_standaloneConfigDTO.mqttManagementResponseRetainMessages,
                                        out retain);

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

                                var mqttCommandResponseTopic = $"{_standaloneConfigDTO.mqttManagementResponseTopic}";
                                await _mqttService.PublishAsync(mqttCommandResponseTopic,
                                    serializedData,
                                    _iotDeviceInterfaceClient.MacAddress,
                                    mqttQualityOfServiceLevel,
                                    retain);


                            }
                        }
                        else if ("mode".Equals(commandValue) || "setcfg".Equals(commandValue))
                        {
                            var modeCmdResult = "success";
                            try
                            {
                                if (deserializedCmdData.ContainsKey("payload") || deserializedCmdData.ContainsKey("fields"))
                                    try
                                    {
                                        modeCmdResult = ProcessMqttModeJsonCommand(managementCommandJson);
                                        if (!"success".Equals(modeCmdResult)) commandStatus = "error";
                                    }
                                    catch (Exception ex)
                                    {
                                        commandStatus = "error";
                                        _logger.LogError(ex, "Unexpected error");
                                    }
                            }
                            catch (Exception e)
                            {
                                commandStatus = "error";
                                _logger.LogError(e, "Unexpected error");
                            }

                            if (deserializedCmdData.ContainsKey("response"))
                            {
                                deserializedCmdData["response"] = commandStatus;
                            }
                            else
                            {
                                var commandResponse = new JProperty("response", commandStatus);
                                deserializedCmdData.Add(commandResponse);
                            }

                            if (deserializedCmdData.ContainsKey("message"))
                            {
                                deserializedCmdData["message"] = modeCmdResult;
                            }
                            else
                            {
                                var commandResponse = new JProperty("message", modeCmdResult);
                                deserializedCmdData.Add(commandResponse);
                            }

                            var serializedData = JsonConvert.SerializeObject(deserializedCmdData);

                            if (!string.IsNullOrEmpty(_standaloneConfigDTO.mqttManagementResponseTopic))
                                if (_standaloneConfigDTO.mqttManagementResponseTopic.Contains("{{deviceId}}"))
                                    _standaloneConfigDTO.mqttManagementResponseTopic =
                                        _standaloneConfigDTO.mqttManagementResponseTopic.Replace("{{deviceId}}",
                                            _standaloneConfigDTO.readerName);

                            var qos = 0;
                            var retain = false;
                            var mqttQualityOfServiceLevel = MqttQualityOfServiceLevel.AtMostOnce;
                            try
                            {
                                _ = int.TryParse(_standaloneConfigDTO.mqttManagementResponseQoS, out qos);
                                _ = bool.TryParse(_standaloneConfigDTO.mqttManagementResponseRetainMessages,
                                    out retain);

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

                            var mqttCommandResponseTopic = $"{_standaloneConfigDTO.mqttManagementResponseTopic}";
                            await _mqttService.PublishAsync(mqttCommandResponseTopic,
                                serializedData,
                                _iotDeviceInterfaceClient.MacAddress,
                                mqttQualityOfServiceLevel,
                                retain);
                        }
                        else if ("retrieve-settings".Equals(commandValue))
                        {
                            var resultPayload = "";
                            try
                            {
                                resultPayload = JsonConvert.SerializeObject(_standaloneConfigDTO);
                            }
                            catch (Exception e)
                            {
                                commandStatus = "error";
                                _logger.LogError(e, "Unexpected error");
                            }

                            if (deserializedCmdData.ContainsKey("payload") || deserializedCmdData.ContainsKey("fields"))
                            {
                                if (deserializedCmdData.ContainsKey("payload"))
                                {
                                    deserializedCmdData["payload"] = JObject.Parse(resultPayload);
                                }
                                else if (deserializedCmdData.ContainsKey("fields"))
                                {
                                    deserializedCmdData["fields"] = JObject.Parse(resultPayload);
                                }
                            }
                            else
                            {
                                var commandResponsePayload = new JProperty("payload", JObject.Parse(resultPayload));
                                deserializedCmdData.Add(commandResponsePayload);
                            }


                            if (deserializedCmdData.ContainsKey("response"))
                            {
                                deserializedCmdData["response"] = commandStatus;
                            }
                            else
                            {
                                var commandResponse = new JProperty("response", commandStatus);
                                deserializedCmdData.Add(commandResponse);
                            }

                            var serializedData = JsonConvert.SerializeObject(deserializedCmdData);

                            result = serializedData;

                            if (!string.IsNullOrEmpty(_standaloneConfigDTO.mqttManagementResponseTopic))
                                if (_standaloneConfigDTO.mqttManagementResponseTopic.Contains("{{deviceId}}"))
                                    _standaloneConfigDTO.mqttManagementResponseTopic =
                                        _standaloneConfigDTO.mqttManagementResponseTopic.Replace("{{deviceId}}",
                                            _standaloneConfigDTO.readerName);

                            if (shouldPublishToMqtt)
                            {
                                var qos = 0;
                                var retain = false;
                                var mqttQualityOfServiceLevel = MqttQualityOfServiceLevel.AtMostOnce;
                                try
                                {
                                    _ = int.TryParse(_standaloneConfigDTO.mqttManagementResponseQoS, out qos);
                                    _ = bool.TryParse(_standaloneConfigDTO.mqttManagementResponseRetainMessages,
                                        out retain);

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

                                var mqttCommandResponseTopic = $"{_standaloneConfigDTO.mqttManagementResponseTopic}";
                                await _mqttService.PublishAsync(mqttCommandResponseTopic,
                                    serializedData,
                                    _iotDeviceInterfaceClient.MacAddress,
                                    mqttQualityOfServiceLevel,
                                    retain);



                            }
                        }
                        else if ("apply-settings".Equals(commandValue))
                        {
                            if (deserializedCmdData.ContainsKey("payload") || deserializedCmdData.ContainsKey("fields"))
                            {
                                if (deserializedCmdData.ContainsKey("payload"))
                                {
                                    var commandPayloadJObject =
                                    deserializedCmdData["payload"].Value<StandaloneConfigDTO>();
                                    if (commandPayloadJObject != null)
                                        try
                                        {
                                            _configurationService.SaveConfigDtoToDb(commandPayloadJObject);
                                        }
                                        catch (Exception e)
                                        {
                                            commandStatus = "error";
                                            _logger.LogError(e, "Unexpected error");
                                        }
                                }
                                else if (deserializedCmdData.ContainsKey("fields"))
                                {
                                    var commandPayloadJObject =
                                    deserializedCmdData["fields"].Value<StandaloneConfigDTO>();
                                    if (commandPayloadJObject != null)
                                        try
                                        {
                                            _configurationService.SaveConfigDtoToDb(commandPayloadJObject);
                                        }
                                        catch (Exception e)
                                        {
                                            commandStatus = "error";
                                            _logger.LogError(e, "Unexpected error");
                                        }
                                }
                            }

                            if (deserializedCmdData.ContainsKey("response"))
                            {
                                deserializedCmdData["response"] = commandStatus;
                            }
                            else
                            {
                                var commandResponse = new JProperty("response", commandStatus);
                                deserializedCmdData.Add(commandResponse);
                            }

                            var serializedData = JsonConvert.SerializeObject(deserializedCmdData);

                            result = serializedData;

                            if (!string.IsNullOrEmpty(_standaloneConfigDTO.mqttManagementResponseTopic))
                                if (_standaloneConfigDTO.mqttManagementResponseTopic.Contains("{{deviceId}}"))
                                    _standaloneConfigDTO.mqttManagementResponseTopic =
                                        _standaloneConfigDTO.mqttManagementResponseTopic.Replace("{{deviceId}}",
                                            _standaloneConfigDTO.readerName);

                            if (shouldPublishToMqtt)
                            {
                                var qos = 0;
                                var retain = false;
                                var mqttQualityOfServiceLevel = MqttQualityOfServiceLevel.AtMostOnce;
                                try
                                {
                                    _ = int.TryParse(_standaloneConfigDTO.mqttManagementResponseQoS, out qos);
                                    _ = bool.TryParse(_standaloneConfigDTO.mqttManagementResponseRetainMessages,
                                        out retain);

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

                                var mqttCommandResponseTopic = $"{_standaloneConfigDTO.mqttManagementResponseTopic}";
                                await _mqttService.PublishAsync(mqttCommandResponseTopic,
                                    serializedData,
                                    _iotDeviceInterfaceClient.MacAddress,
                                    mqttQualityOfServiceLevel,
                                    retain);



                            }
                        }
                        else if ("upgrade".Equals(commandValue))
                        {
                            try
                            {
                                if (deserializedCmdData.ContainsKey("payload") || deserializedCmdData.ContainsKey("fields"))
                                {
                                    JObject commandPayloadJObject = [];

                                    var remoteUrl = _standaloneConfigDTO.systemImageUpgradeUrl;

                                    var timeoutInMinutes = 3;
                                    var maxRetries = 1;
                                    var commandPayloadParams = new Dictionary<object, object>();

                                    if (deserializedCmdData.ContainsKey("payload"))
                                    {
                                        commandPayloadJObject = deserializedCmdData["payload"].Value<JObject>();
                                    }
                                    else if (deserializedCmdData.ContainsKey("fields"))
                                    {
                                        commandPayloadJObject = deserializedCmdData["fields"].Value<JObject>();
                                    }
                                    if (commandPayloadJObject != null &&
                                        commandPayloadJObject.ContainsKey("upgradeUrl"))
                                    {
                                        var commandPayloadValue =
                                            commandPayloadJObject["upgradeUrl"].Value<string>();
                                        if (!string.IsNullOrEmpty(commandPayloadValue))
                                        {
                                            remoteUrl = commandPayloadValue;
                                            _logger.LogInformation($"Requesting image upgrade from {remoteUrl}");
                                            commandPayloadParams.Add("remoteUrl", remoteUrl);
                                        }
                                    }
                                    else if (commandPayloadJObject != null &&
                                        commandPayloadJObject.ContainsKey("url"))
                                    {
                                        var commandPayloadValue =
                                            commandPayloadJObject["url"].Value<string>();
                                        if (!string.IsNullOrEmpty(commandPayloadValue))
                                        {
                                            remoteUrl = commandPayloadValue;
                                            _logger.LogInformation($"Requesting image upgrade from {remoteUrl}");
                                            commandPayloadParams.Add("remoteUrl", remoteUrl);
                                        }
                                    }

                                    if (commandPayloadJObject != null &&
                                        commandPayloadJObject.ContainsKey("timeoutInMinutes"))
                                    {
                                        var commandPayloadValue =
                                            commandPayloadJObject["timeoutInMinutes"].Value<int>();
                                        if (commandPayloadValue > 0)
                                        {
                                            timeoutInMinutes = commandPayloadValue;
                                            _logger.LogInformation($"Image upgrade timetout set to {timeoutInMinutes}");
                                            commandPayloadParams.Add("timeoutInMinutes", timeoutInMinutes);
                                        }
                                    }

                                    if (commandPayloadJObject != null &&
                                        commandPayloadJObject.ContainsKey("maxRetries"))
                                    {
                                        var commandPayloadValue =
                                            commandPayloadJObject["maxRetries"].Value<int>();
                                        if (commandPayloadValue > 0)
                                        {
                                            maxRetries = commandPayloadValue;
                                            _logger.LogInformation($"Image upgrade max retries set to {maxRetries}");
                                            commandPayloadParams.Add("maxRetries", maxRetries);
                                        }
                                    }

                                    try
                                    {
                                        var deserializedConfigData =
                                            JsonConvert.DeserializeObject<StandaloneConfigDTO>(managementCommandJson);

                                        if (deserializedConfigData != null)
                                            try
                                            {
                                                if (!string.IsNullOrEmpty(remoteUrl))
                                                {
                                                    var cmdPayload = JsonConvert.SerializeObject(commandPayloadParams);
                                                    _configurationService.SaveUpgradeCommandToDb(cmdPayload);
                                                }

                                            }
                                            catch (Exception e)
                                            {
                                                commandStatus = "error";
                                                _logger.LogError(e, "Unexpected error");
                                            }
                                    }
                                    catch (Exception ex)
                                    {
                                        commandStatus = "error";
                                        _logger.LogError(ex, "Unexpected error");
                                    }
                                }
                            }
                            catch (Exception e)
                            {
                                commandStatus = "error";
                                _logger.LogError(e, "Unexpected error");
                            }

                            if (deserializedCmdData.ContainsKey("response"))
                            {
                                deserializedCmdData["response"] = commandStatus;
                            }
                            else
                            {
                                var commandResponse = new JProperty("response", commandStatus);
                                deserializedCmdData.Add(commandResponse);
                            }

                            var serializedData = JsonConvert.SerializeObject(deserializedCmdData);

                            result = serializedData;

                            if (!string.IsNullOrEmpty(_standaloneConfigDTO.mqttManagementResponseTopic))
                                if (_standaloneConfigDTO.mqttManagementResponseTopic.Contains("{{deviceId}}"))
                                    _standaloneConfigDTO.mqttManagementResponseTopic =
                                        _standaloneConfigDTO.mqttManagementResponseTopic.Replace("{{deviceId}}",
                                            _standaloneConfigDTO.readerName);

                            if (shouldPublishToMqtt)
                            {
                                var qos = 0;
                                var retain = false;
                                var mqttQualityOfServiceLevel = MqttQualityOfServiceLevel.AtMostOnce;
                                try
                                {
                                    _ = int.TryParse(_standaloneConfigDTO.mqttManagementResponseQoS, out qos);
                                    _ = bool.TryParse(_standaloneConfigDTO.mqttManagementResponseRetainMessages,
                                        out retain);

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

                                var mqttCommandResponseTopic = $"{_standaloneConfigDTO.mqttManagementResponseTopic}";
                                await _mqttService.PublishAsync(mqttCommandResponseTopic,
                                    serializedData,
                                    _iotDeviceInterfaceClient.MacAddress,
                                    mqttQualityOfServiceLevel,
                                    retain);



                            }
                        }
                        else if ("impinj_iot_device_interface".Equals(commandValue))
                        {
                            var resultPayload = "";
                            try
                            {
                                if (deserializedCmdData.ContainsKey("payload") || deserializedCmdData.ContainsKey("fields"))
                                {
                                    var impinjIotApiEndpoint = "";
                                    var impinjIotApiVerb = "";
                                    var impinjIotApiRequest = "";
                                    JObject commandPayloadJObject = [];
                                    if (deserializedCmdData.ContainsKey("payload"))
                                    {
                                        commandPayloadJObject = deserializedCmdData["payload"].Value<JObject>();
                                    }
                                    if (deserializedCmdData.ContainsKey("fields"))
                                    {
                                        commandPayloadJObject = deserializedCmdData["fields"].Value<JObject>();
                                    }
                                    if (commandPayloadJObject != null &&
                                        commandPayloadJObject.ContainsKey("impinjIotApiEndpoint"))
                                    {
                                        impinjIotApiEndpoint = commandPayloadJObject["impinjIotApiEndpoint"]
                                            .Value<string>();
                                        if (!string.IsNullOrEmpty(impinjIotApiEndpoint))
                                            _logger.LogInformation($"impinjIotApiEndpoint {impinjIotApiEndpoint}");
                                    }

                                    if (commandPayloadJObject != null &&
                                        commandPayloadJObject.ContainsKey("impinjIotApiVerb"))
                                    {
                                        impinjIotApiVerb = commandPayloadJObject["impinjIotApiVerb"]
                                            .Value<string>();
                                        if (!string.IsNullOrEmpty(impinjIotApiVerb))
                                            _logger.LogInformation($"impinjIotApiVerb {impinjIotApiVerb}");
                                    }

                                    if (commandPayloadJObject != null &&
                                        commandPayloadJObject.ContainsKey("impinjIotApiRequest"))
                                    {
                                        var impinjIotApiRequestJObject =
                                            commandPayloadJObject["impinjIotApiRequest"].Value<JObject>();
                                        if (impinjIotApiRequestJObject != null)
                                        {
                                            impinjIotApiRequest =
                                                JsonConvert.SerializeObject(impinjIotApiRequestJObject);
                                            if (!string.IsNullOrEmpty(impinjIotApiRequest))
                                                _logger.LogInformation(
                                                    $"impinjIotApiRequest {impinjIotApiRequest}");
                                        }
                                    }

                                    try
                                    {

                                        resultPayload = await CallIotRestFulInterfaceAsync(impinjIotApiRequest,
                                            impinjIotApiEndpoint, impinjIotApiVerb,
                                            _standaloneConfigDTO.mqttManagementResponseTopic, false);

                                    }
                                    catch (Exception ex)
                                    {
                                        commandStatus = "error";
                                        _logger.LogError(ex, "Unexpected error");
                                    }
                                }
                            }
                            catch (Exception e)
                            {
                                commandStatus = "error";
                                _logger.LogError(e, "Unexpected error");
                            }

                            if (deserializedCmdData.ContainsKey("payload"))
                            {
                                deserializedCmdData["payload"] = JObject.Parse(resultPayload);
                            }
                            else if (deserializedCmdData.ContainsKey("fields"))
                            {
                                deserializedCmdData["fields"] = JObject.Parse(resultPayload);
                            }
                            else
                            {
                                var commandResponsePayload = new JProperty("payload", JObject.Parse(resultPayload));
                                deserializedCmdData.Add(commandResponsePayload);
                            }

                            if (deserializedCmdData.ContainsKey("response"))
                            {
                                deserializedCmdData["response"] = commandStatus;
                            }
                            else
                            {
                                var commandResponse = new JProperty("response", commandStatus);
                                deserializedCmdData.Add(commandResponse);
                            }

                            var serializedData = JsonConvert.SerializeObject(deserializedCmdData);

                            result = serializedData;

                            if (!string.IsNullOrEmpty(_standaloneConfigDTO.mqttManagementResponseTopic))
                                if (_standaloneConfigDTO.mqttManagementResponseTopic.Contains("{{deviceId}}"))
                                    _standaloneConfigDTO.mqttManagementResponseTopic =
                                        _standaloneConfigDTO.mqttManagementResponseTopic.Replace("{{deviceId}}",
                                            _standaloneConfigDTO.readerName);

                            if (shouldPublishToMqtt)
                            {
                                var qos = 0;
                                var retain = false;
                                var mqttQualityOfServiceLevel = MqttQualityOfServiceLevel.AtMostOnce;
                                try
                                {
                                    _ = int.TryParse(_standaloneConfigDTO.mqttManagementResponseQoS, out qos);
                                    _ = bool.TryParse(_standaloneConfigDTO.mqttManagementResponseRetainMessages,
                                        out retain);

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

                                var mqttCommandResponseTopic = $"{_standaloneConfigDTO.mqttManagementResponseTopic}";
                                await _mqttService.PublishAsync(mqttCommandResponseTopic,
                                    serializedData,
                                    _iotDeviceInterfaceClient.MacAddress,
                                    mqttQualityOfServiceLevel,
                                    retain);


                            }
                        }
                    }
            }
            else
            {
                if (string.Equals("1", _standaloneConfigDTO.enablePlugin.Trim(), StringComparison.OrdinalIgnoreCase)
                    && _plugins.Count > 0
                    && _plugins.ContainsKey(_standaloneConfigDTO.activePlugin))
                {
                    _logger.LogInformation(_plugins[_standaloneConfigDTO.activePlugin].GetName());
                    var cmdResult = _plugins[_standaloneConfigDTO.activePlugin].ProcessCommand(managementCommandJson);

                    if ("START".Equals(cmdResult))
                    {
                        _configurationService.SaveStartPresetCommandToDb();
                    }
                    else if ("STOP".Equals(cmdResult))
                    {
                        _configurationService.SaveStopPresetCommandToDb();
                    }
                }

            }
        }
        catch (Exception)
        {


        }

        return result;
#pragma warning restore CS8600, CS8601, CS8602, CS8603, CS8604, CS8625, CS8629 // Dereference of a possibly null reference.
    }
    private async void ProcessMqttCommandMessage(string clientId, string topic, string receivedMqttMessage)
    {
#pragma warning disable CS8600, CS8601, CS8602, CS8603, CS8604, CS8625, CS8629 // Dereference of a possibly null reference.
        try
        {
            _logger.LogInformation("[ProcessMqttCommandMessage] Cliet ID = " + clientId);
            _logger.LogInformation("[ProcessMqttCommandMessage] Topic = " + topic);
            var mqttMessage = "";
            if (!string.IsNullOrEmpty(receivedMqttMessage))
                mqttMessage = receivedMqttMessage.Replace("\n", string.Empty).Replace("\r", string.Empty)
                    .Replace(@"\", string.Empty);


            _logger.LogInformation("[ProcessMqttCommandMessage] Payload = " + mqttMessage);

            if (string.IsNullOrEmpty(DeviceIdWithDashes)) DeviceIdWithDashes = DeviceId;


            if (topic.Contains(_standaloneConfigDTO.mqttManagementCommandTopic)
                || topic.Contains(
                    _standaloneConfigDTO.mqttManagementCommandTopic.Replace("{{deviceId}}",
                        _standaloneConfigDTO.readerName)))
                try
                {
                    _ = await ProcessMqttManagementCommandJsonAsync(mqttMessage, true);


                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error");
                }
            else if (topic.Contains(_standaloneConfigDTO.mqttControlCommandTopic)
                     || topic.Contains(
                         _standaloneConfigDTO.mqttControlCommandTopic.Replace("{{deviceId}}",
                             _standaloneConfigDTO.readerName)))
                try
                {
                    _ = await ProcessMqttControlCommandJsonAsync(mqttMessage, true);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error");
                }

            if ((topic.Contains(DeviceId) || topic.Contains(_standaloneConfigDTO.readerName)) &&
                topic.Contains("cmd/settings/post"))
            {
                try
                {
                    var deserializedData = JsonConvert.DeserializeObject<StandaloneConfigDTO>(mqttMessage);
                    if (deserializedData != null)
                        try
                        {
                            _configurationService.SaveConfigDtoToDb(deserializedData);
                        }
                        catch (Exception)
                        {
                            //throw;
                        }
                    //_standaloneConfigDTO = deserializedData;
                    //var configHelper = new IniConfigHelper();
                    //try
                    //{
                    //    configHelper.SaveDtoToFile(_standaloneConfigDTO);
                    //}
                    //catch (Exception ex)
                    //{
                    //}
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error");
                }
            }
            else if ((topic.Contains(DeviceId) || topic.Contains(_standaloneConfigDTO.readerName)) &&
                     topic.Contains("cmd/settings/get"))
            {
                try
                {
                    var serializedData = JsonConvert.SerializeObject(_standaloneConfigDTO);
                    var mqttCommandResponseTopic = $"{_standaloneConfigDTO.mqttTagEventsTopic}/response";
                    await _mqttService.PublishAsync(mqttCommandResponseTopic, serializedData);



                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error" + ex.Message);
                }
            }
            else if ((topic.Contains(DeviceId) || topic.Contains(_standaloneConfigDTO.readerName)) &&
                     topic.Contains("cmd/getserial"))
            {
                try
                {
                    var serial = GetSerialAsync();
                    var serializedData = JsonConvert.SerializeObject(serial.Result);
                    var mqttCommandResponseTopic = $"{_standaloneConfigDTO.mqttTagEventsTopic}/response";
                    await _mqttService.PublishAsync(mqttCommandResponseTopic, serializedData);


                }
                catch (Exception ex)
                {
                    _logger.LogInformation("Unexpected error" + ex.Message, SeverityType.Error);
                }
            }
            else if ((topic.Contains(DeviceId) || topic.Contains(_standaloneConfigDTO.readerName)) &&
                     topic.Contains("cmd/getstatus"))
            {
                try
                {
                    var status = GetReaderStatusAsync();
                    var serializedData = JsonConvert.SerializeObject(status.Result);
                    var mqttCommandResponseTopic = $"{_standaloneConfigDTO.mqttTagEventsTopic}/response";
                    await _mqttService.PublishAsync(mqttCommandResponseTopic, serializedData);



                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error" + ex.Message);
                }
            }
            else if ((topic.Contains(DeviceId) || topic.Contains(_standaloneConfigDTO.readerName)) &&
                     topic.Contains("cmd/start-inventory"))
            {
                try
                {
                    _ = StartTasksAsync();
                    var mqttCommandResponseTopic = $"{_standaloneConfigDTO.mqttTagEventsTopic}/response";
                    await _mqttService.PublishAsync(mqttCommandResponseTopic, "OK");



                }
                catch (Exception ex)
                {
                    _logger.LogInformation("Unexpected error" + ex.Message, SeverityType.Error);
                }
            }
            else if ((topic.Contains(DeviceId) || topic.Contains(_standaloneConfigDTO.readerName)) &&
                     topic.Contains("cmd/stop-inventory"))
            {
                try
                {
                    _ = StopTasksAsync();
                    var mqttCommandResponseTopic = $"{_standaloneConfigDTO.mqttTagEventsTopic}/response";
                    await _mqttService.PublishAsync(mqttCommandResponseTopic, "OK");



                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error" + ex.Message);
                }
            }
            else if ((topic.Contains(DeviceId) || topic.Contains(_standaloneConfigDTO.readerName)) &&
                     topic.Contains("cmd/set-gpo"))
            {
                try
                {
                    var deserializedData = JsonConvert.DeserializeObject<GpoVm>(mqttMessage);
                    if (deserializedData != null && deserializedData.GpoConfigurations.Any())
                    {
                        _ = SetGpoPortsAsync(deserializedData);
                        var mqttCommandResponseTopic = $"{_standaloneConfigDTO.mqttTagEventsTopic}/response";
                        await _mqttService.PublishAsync(mqttCommandResponseTopic, "OK");



                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error" + ex.Message);
                }
            }
            else if ((topic.Contains(DeviceId) || topic.Contains(_standaloneConfigDTO.readerName)) &&
                     topic.Contains("api/v1"))
            {
                var mqttCommandResponseTopic = $"{_standaloneConfigDTO.mqttManagementResponseTopic}";

                try
                {
                    var apiStringPosition = topic.IndexOf("api/v1");
                    var endpointString = topic.Substring(apiStringPosition);

                    endpointString = endpointString.Replace("api/v1", "");
                    var method = "GET";

                    if (endpointString.StartsWith("/get")) endpointString = endpointString.Replace("/get", "");
                    if (endpointString.StartsWith("/post"))
                    {
                        endpointString = endpointString.Replace("/post", "");
                        method = "POST";
                    }
                    else if (endpointString.StartsWith("/put"))
                    {
                        endpointString = endpointString.Replace("/put", "");
                        method = "PUT";
                    }
                    else if (endpointString.StartsWith("/delete"))
                    {
                        endpointString = endpointString.Replace("/delete", "");
                        method = "DELETE";
                    }

                    _ = await CallIotRestFulInterfaceAsync(mqttMessage, endpointString, method, mqttCommandResponseTopic, true);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error" + ex.Message);
                    //_mqttCommandClient.EnqueueAsync(_standaloneConfigDTO.mqttTopic, ex.Message);
                    await _mqttService.PublishAsync(mqttCommandResponseTopic, ex.Message);



                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error" + ex.Message);
        }
#pragma warning restore CS8600, CS8601, CS8602, CS8603, CS8604, CS8625, CS8629 // Dereference of a possibly null reference.
    }

    public string ProcessMqttModeJsonCommand(string modeCommandPayload)
    {
#pragma warning disable CS8600, CS8601, CS8602, CS8603, CS8604, CS8625, CS8629 // Dereference of a possibly null reference.
        var commandResult = "success";
        try
        {
            var deserializedConfigData = JsonConvert.DeserializeObject<JObject>(modeCommandPayload);
            if (deserializedConfigData != null)
                try
                {
                    JObject commandPayloadJObject = [];
                    if (deserializedConfigData.ContainsKey("payload"))
                    {
                        commandPayloadJObject = deserializedConfigData["payload"].Value<JObject>();
                    }
                    else if (deserializedConfigData.ContainsKey("fields"))
                    {
                        commandPayloadJObject = deserializedConfigData["fields"].Value<JObject>();
                    }
                    if (commandPayloadJObject != null && commandPayloadJObject.ContainsKey("type"))
                    {
                        var antennaZone = "zone1";
                        var antennaZoneState = "enabled";
                        var antennaList = new List<int>();
                        var antennaZoneList = _standaloneConfigDTO.antennaZones.Split(",");
                        var antennaPortList = _standaloneConfigDTO.antennaPorts.Split(",");
                        antennaList.Add(1);
                        double transmitPower = 30;
                        var transmitPowerCdbm = 3000;
                        var rssiThreshold = -92;
                        double groupIntervalInMs = 400;


                        var newAntennaZoneConfigLine = _standaloneConfigDTO.antennaZones;
                        var newAntennaStatesConfigLine = _standaloneConfigDTO.antennaStates;
                        var newAntennaTransmitPowerCdbmConfigLine = _standaloneConfigDTO.transmitPower;
                        var newAntennaReceiveSensitivityConfigLine = _standaloneConfigDTO.receiveSensitivity;
                        var newMqttPuslishIntervalSec = _standaloneConfigDTO.mqttPuslishIntervalSec;
                        var newReaderModeConfigLine = _standaloneConfigDTO.readerMode;
                        var newSearchModeConfigLine = _standaloneConfigDTO.searchMode;
                        var newSessionConfigLine = _standaloneConfigDTO.session;
                        var newTagPopulationConfigLine = _standaloneConfigDTO.tagPopulation;
                        var newSoftwareFilterTagIdValueLine = _standaloneConfigDTO.softwareFilterTagIdValueOrPattern;
                        var newSoftwareFilterTagIdMatchLine = _standaloneConfigDTO.softwareFilterTagIdMatch;
                        var newSoftwareFilterTagIdOperationLine = _standaloneConfigDTO.softwareFilterTagIdOperation;
                        var newSoftwareFilterTagIdEnabledLine = _standaloneConfigDTO.softwareFilterTagIdEnabled;
                        var newSoftwareFilterIncludeEpcsHeaderListEnabledLine = _standaloneConfigDTO.softwareFilterIncludeEpcsHeaderListEnabled;
                        var newSoftwareFilterIncludeEpcsHeaderListLine = _standaloneConfigDTO.softwareFilterIncludeEpcsHeaderList;

                        var newStartTriggerType = _standaloneConfigDTO.startTriggerType;
                        var newStartTriggerGpiPort = _standaloneConfigDTO.startTriggerGpiPort;
                        var newStartTriggerGpiEvent = _standaloneConfigDTO.startTriggerGpiEvent;
                        var newStartTriggerOffset = _standaloneConfigDTO.startTriggerOffset;
                        var newStartTriggerPeriod = _standaloneConfigDTO.startTriggerPeriod;
                        var newStartTriggerUTCTimestamp = _standaloneConfigDTO.startTriggerUTCTimestamp;

                        var newStopTriggerType = _standaloneConfigDTO.stopTriggerType;
                        var newStopTriggerDuration = _standaloneConfigDTO.stopTriggerDuration;
                        var newStopTriggerGpiEvent = _standaloneConfigDTO.stopTriggerGpiEvent;
                        var newStopTriggerGpiPort = _standaloneConfigDTO.stopTriggerGpiPort;
                        var newStopTriggerTimeout = _standaloneConfigDTO.stopTriggerTimeout;


                        var commandType = commandPayloadJObject["type"].Value<string>();
                        if (!string.IsNullOrEmpty(commandType)
                            && (string.Equals("INVENTORY", commandType, StringComparison.OrdinalIgnoreCase)
                                || string.Equals("Inv", commandType, StringComparison.OrdinalIgnoreCase)))
                        {
                            if (commandPayloadJObject.ContainsKey("antennaZone"))
                            {
                                try
                                {
                                    antennaZone = commandPayloadJObject["antennaZone"].Value<string>();
                                }
                                catch (Exception ex)
                                {
                                    commandResult = "error setting the antenna zone.";
                                    _logger.LogError(ex,
                                        "ProcessMqttSetCfgJsonCommand (antennaZone): Unexpected error" + ex.Message);
                                    return commandResult;
                                }

                                if (commandPayloadJObject.ContainsKey("antennaZoneState"))
                                {
                                    try
                                    {
                                        antennaZoneState = commandPayloadJObject["antennaZoneState"].Value<string>();
                                    }
                                    catch (Exception ex)
                                    {
                                        commandResult = "error setting the antenna zone.";
                                        _logger.LogError(ex,
                                            "ProcessMqttSetCfgJsonCommand (antennaZoneState): Unexpected error" + ex.Message);
                                        return commandResult;
                                    }
                                }
                                antennaList.Clear();
                                for (int i = 0; i < antennaZoneList.Length; i++)
                                {
                                    if (antennaZoneList[i].Equals(antennaZone))
                                    {
                                        antennaList.Add(int.Parse(antennaPortList[i]));
                                    }
                                }
                            }


                            if (commandPayloadJObject.ContainsKey("antennas") || commandPayloadJObject.ContainsKey("ants"))
                                try
                                {
                                    JArray antennaListJArray = [];
                                    if (commandPayloadJObject.ContainsKey("antennas"))
                                    {
                                        antennaListJArray = commandPayloadJObject["antennas"].Value<JArray>(); // Value<List<int>>();
                                    }
                                    else if (commandPayloadJObject.ContainsKey("ants"))
                                    {
                                        antennaListJArray = commandPayloadJObject["ants"].Value<JArray>(); // Value<List<int>>();
                                    }

                                    if (antennaListJArray != null && antennaListJArray.Count > 0)
                                    {
                                        antennaList.Clear();
                                        antennaList = antennaListJArray.ToObject<List<int>>();
                                    }

                                }
                                catch (Exception ex)
                                {
                                    commandResult = "error setting active antennas.";
                                    _logger.LogError(ex,
                                        "ProcessMqttSetCfgJsonCommand (antennas): Unexpected error" + ex.Message);
                                    return commandResult;
                                }

                            if (commandPayloadJObject.ContainsKey("transmitPower"))
                                try
                                {
                                    transmitPower = commandPayloadJObject["transmitPower"].Value<double>();
                                    var calculatedTransmitPower = 100 * transmitPower;
                                    transmitPowerCdbm = Convert.ToInt32(calculatedTransmitPower);
                                }
                                catch (Exception ex)
                                {
                                    commandResult = "error setting transmit power.";
                                    _logger.LogError(ex,
                                        "ProcessMqttSetCfgJsonCommand (transmitPower): Unexpected error" + ex.Message);
                                    return commandResult;
                                }

                            if (commandPayloadJObject.ContainsKey("rssiFilter"))
                                try
                                {
                                    var rssiFilterJObject = commandPayloadJObject["rssiFilter"].ToObject<JObject>();
                                    if (rssiFilterJObject != null && rssiFilterJObject.ContainsKey("threshold"))
                                        rssiThreshold = rssiFilterJObject["threshold"].Value<int>();
                                }
                                catch (Exception ex)
                                {
                                    commandResult = "error setting rssi filter.";
                                    _logger.LogError(ex,
                                        "ProcessMqttSetCfgJsonCommand (rssiFilter): Unexpected error" + ex.Message);
                                    return commandResult;
                                }

                            var currentStates = _standaloneConfigDTO.antennaStates.Split(",");

                            try
                            {
                                if (antennaList != null && antennaList.Count > 0)
                                {
                                    var valueToSet = "1";
                                    //var disabledValue = "0";
                                    if ("disabled".Equals(antennaZoneState))
                                    {
                                        valueToSet = "0";
                                    }
                                    //for (var i = 0; i < currentStates.Length; i++) currentStates[i] = disabledValue;

                                    //foreach (var antennaPortNumber in antennaList)
                                    //    if (currentStates.Length >= antennaPortNumber)
                                    //        currentStates[antennaPortNumber - 1] = valueToSet;

                                    newAntennaStatesConfigLine = "";
                                    for (var i = 0; i < currentStates.Length; i++)
                                    {
                                        if (antennaList.Contains(i + 1))
                                        {
                                            newAntennaStatesConfigLine += valueToSet;
                                        }
                                        else
                                        {
                                            newAntennaStatesConfigLine += currentStates[i];
                                        }

                                        if (i < currentStates.Length - 1) newAntennaStatesConfigLine += ",";
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                commandResult = "error trying to enable antennas.";
                                _logger.LogError(ex,
                                    "ProcessMqttsetCfgJsonCommand (enable antennas): Unexpected error" + ex.Message);
                                return commandResult;
                            }

                            try
                            {
                                var currentAntennaPorts = _standaloneConfigDTO.antennaPorts.Split(",");
                                var previousTransmitPower = _standaloneConfigDTO.transmitPower.Split(",");
                                var previousReceiveSensitivity = _standaloneConfigDTO.receiveSensitivity.Split(",");
                                newAntennaTransmitPowerCdbmConfigLine = "";
                                newAntennaReceiveSensitivityConfigLine = "";
                                for (var i = 0; i < currentAntennaPorts.Length; i++)
                                {
                                    if (antennaList.Contains(int.Parse(currentAntennaPorts[i])))
                                    {
                                        newAntennaTransmitPowerCdbmConfigLine += transmitPowerCdbm;
                                        newAntennaReceiveSensitivityConfigLine += rssiThreshold;
                                    }
                                    else
                                    {
                                        newAntennaTransmitPowerCdbmConfigLine += previousTransmitPower[i];
                                        newAntennaReceiveSensitivityConfigLine += previousReceiveSensitivity[i];
                                    }

                                    if (i < currentAntennaPorts.Length - 1)
                                    {
                                        newAntennaTransmitPowerCdbmConfigLine += ",";
                                        newAntennaReceiveSensitivityConfigLine += ",";
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                commandResult = "error saving settings.";
                                _logger.LogError(ex,
                                    "ProcessMqttsetCfgJsonCommand (set tx power): Unexpected error" + ex.Message);
                                return commandResult;
                            }

                            if (commandPayloadJObject.ContainsKey("groupIntervalInMs"))
                                try
                                {
                                    var receivedGroupIntervalInMs =
                                        commandPayloadJObject["groupIntervalInMs"].Value<double>();
                                    if (receivedGroupIntervalInMs > 0)
                                    {
                                        groupIntervalInMs = receivedGroupIntervalInMs;
                                        var groupIntervalInSec = groupIntervalInMs / 1000;

                                        newMqttPuslishIntervalSec =
                                            groupIntervalInSec.ToString(CultureInfo.InvariantCulture);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    commandResult = "error setting group interval in ms.";
                                    _logger.LogError(ex,
                                        "ProcessMqttSetCfgJsonCommand (groupIntervalInMs): Unexpected error" +
                                        ex.Message);
                                    return commandResult;
                                }

                            if (commandPayloadJObject.ContainsKey("rfMode"))
                                try
                                {
                                    var rfMode = commandPayloadJObject["rfMode"].Value<string>();
                                    if (!string.IsNullOrEmpty(rfMode))
                                    {
                                        var rfModeTable =
                                            Utils.GetDefaultRfModeTableByRegion(_iotDeviceInterfaceClient
                                                .ReaderOperatingRegion);
                                        var rfModeIndex = Utils.GetRfModeValueByName(rfMode, rfModeTable);

                                        if (rfModeIndex == -1)
                                        {
                                            commandResult = $"error setting the RF Mode, mode {rfMode} not found.";
                                            _logger.LogError($"error setting the RF Mode, mode {rfMode} not found.");
                                            return commandResult;
                                        }

                                        newReaderModeConfigLine = $"{rfModeIndex}";
                                    }
                                }
                                catch (Exception ex)
                                {
                                    commandResult = "error setting group interval in ms.";
                                    _logger.LogError(ex,
                                        "ProcessMqttSetCfgJsonCommand (groupIntervalInMs): Unexpected error" +
                                        ex.Message);
                                    return commandResult;
                                }

                            if (commandPayloadJObject.ContainsKey("session"))
                                try
                                {
                                    var receivedSession = commandPayloadJObject["session"].Value<string>();
                                    int convertedSession = -1;

                                    _ = int.TryParse(receivedSession, out convertedSession);

                                    if (convertedSession >= 0 && convertedSession < 4)
                                    {
                                        newSearchModeConfigLine = receivedSession;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    commandResult = "error setting session.";
                                    _logger.LogError(ex,
                                        "ProcessMqttSetCfgJsonCommand (session): Unexpected error" +
                                        ex.Message);
                                    return commandResult;
                                }

                            if (commandPayloadJObject.ContainsKey("tagPopulation"))
                                try
                                {
                                    var receivedTagPopulation = commandPayloadJObject["tagPopulation"].Value<int>();

                                    if (receivedTagPopulation >= 0 && receivedTagPopulation < 65000)
                                    {
                                        newTagPopulationConfigLine = receivedTagPopulation.ToString();
                                    }
                                }
                                catch (Exception ex)
                                {
                                    commandResult = "error setting tag population.";
                                    _logger.LogError(ex,
                                        "ProcessMqttSetCfgJsonCommand (tagPopulation): Unexpected error" +
                                        ex.Message);
                                    return commandResult;
                                }


                            //newTagPopulationConfigLine

                            if (commandPayloadJObject.ContainsKey("searchMode"))
                                try
                                {
                                    var searchMode = commandPayloadJObject["searchMode"].Value<string>();

                                    if (string.Equals("reader-selected", searchMode, StringComparison.OrdinalIgnoreCase)
                                        || string.Equals("readerselected", searchMode, StringComparison.OrdinalIgnoreCase)
                                        || string.Equals("0", searchMode, StringComparison.OrdinalIgnoreCase))
                                    {
                                        newSearchModeConfigLine = "1";
                                    }
                                    else if (string.Equals("single-target", searchMode, StringComparison.OrdinalIgnoreCase)
                                        || string.Equals("singletarget", searchMode, StringComparison.OrdinalIgnoreCase)
                                        || string.Equals("1", searchMode, StringComparison.OrdinalIgnoreCase))
                                    {
                                        newSearchModeConfigLine = "1";
                                    }
                                    else if (string.Equals("dual-target", searchMode, StringComparison.OrdinalIgnoreCase)
                                        || string.Equals("dualtarget", searchMode, StringComparison.OrdinalIgnoreCase)
                                        || string.Equals("2", searchMode, StringComparison.OrdinalIgnoreCase))
                                    {
                                        newSearchModeConfigLine = "2";
                                    }
                                    else if (string.Equals("single-target-with-tagfocus", searchMode, StringComparison.OrdinalIgnoreCase)
                                        || string.Equals("singletargetwithtagfocus", searchMode, StringComparison.OrdinalIgnoreCase)
                                         || string.Equals("single-target-with-suppression", searchMode, StringComparison.OrdinalIgnoreCase)
                                         || string.Equals("singletargetwithsuppression", searchMode, StringComparison.OrdinalIgnoreCase)
                                         || string.Equals("tagfocus", searchMode, StringComparison.OrdinalIgnoreCase)
                                         || string.Equals("tag-focus", searchMode, StringComparison.OrdinalIgnoreCase)
                                        || string.Equals("3", searchMode, StringComparison.OrdinalIgnoreCase))
                                    {
                                        newSearchModeConfigLine = "3";
                                    }
                                    else if (string.Equals("single-target-b-to-a", searchMode, StringComparison.OrdinalIgnoreCase)
                                        || string.Equals("singletargetbtoa", searchMode, StringComparison.OrdinalIgnoreCase)
                                        || string.Equals("single-target-reset", searchMode, StringComparison.OrdinalIgnoreCase)
                                        || string.Equals("singletargetreset", searchMode, StringComparison.OrdinalIgnoreCase)
                                        || string.Equals("5", searchMode, StringComparison.OrdinalIgnoreCase))
                                    {
                                        newSearchModeConfigLine = "5";
                                    }
                                    else if (string.Equals("dual-target-with-b-to-a-select", searchMode, StringComparison.OrdinalIgnoreCase)
                                        || string.Equals("dualtargetwithbtoaselect", searchMode, StringComparison.OrdinalIgnoreCase)
                                        || string.Equals("dual-target-with-b-to-a", searchMode, StringComparison.OrdinalIgnoreCase)
                                        || string.Equals("dualtargetwithbtoa", searchMode, StringComparison.OrdinalIgnoreCase)
                                        || string.Equals("6", searchMode, StringComparison.OrdinalIgnoreCase))
                                    {
                                        newSearchModeConfigLine = "6";
                                    }


                                }
                                catch (Exception ex)
                                {
                                    commandResult = "error setting searchMode.";
                                    _logger.LogError(ex,
                                        "ProcessMqttSetCfgJsonCommand (searchMode): Unexpected error" +
                                        ex.Message);
                                    return commandResult;
                                }


                            if (commandPayloadJObject.ContainsKey("filter"))
                                try
                                {
                                    var softwareFilterJObject = commandPayloadJObject["filter"].Value<JObject>();
                                    if (softwareFilterJObject != null && softwareFilterJObject.ContainsKey("value"))
                                    {
                                        newSoftwareFilterTagIdValueLine = softwareFilterJObject["value"].Value<string>();

                                        if (softwareFilterJObject != null && softwareFilterJObject.ContainsKey("status"))
                                        {
                                            var currentSoftwareFilterTagIdStatus = softwareFilterJObject["status"].Value<string>();
                                            if (string.Equals("1", currentSoftwareFilterTagIdStatus, StringComparison.OrdinalIgnoreCase)
                                                || string.Equals("enabled", currentSoftwareFilterTagIdStatus, StringComparison.OrdinalIgnoreCase)
                                                || string.Equals("true", currentSoftwareFilterTagIdStatus, StringComparison.OrdinalIgnoreCase))
                                            {
                                                newSoftwareFilterTagIdEnabledLine = "1";
                                            }
                                            else
                                            {
                                                newSoftwareFilterTagIdEnabledLine = "0";
                                            }
                                        }

                                        if (softwareFilterJObject != null && softwareFilterJObject.ContainsKey("match"))
                                        {
                                            newSoftwareFilterTagIdMatchLine = softwareFilterJObject["match"].Value<string>();
                                        }

                                        if (softwareFilterJObject != null && softwareFilterJObject.ContainsKey("operation"))
                                        {
                                            newSoftwareFilterTagIdOperationLine = softwareFilterJObject["operation"].Value<string>();
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    commandResult = "error setting filter.";
                                    _logger.LogError(ex,
                                        "ProcessMqttSetCfgJsonCommand (filter): Unexpected error" +
                                        ex.Message);
                                    return commandResult;
                                }

                            if (commandPayloadJObject.ContainsKey("filterIncludeEpcHeaderList"))
                                try
                                {
                                    var softwareFilterJObject = commandPayloadJObject["filterIncludeEpcHeaderList"].Value<JObject>();
                                    if (softwareFilterJObject != null && softwareFilterJObject.ContainsKey("value"))
                                    {
                                        newSoftwareFilterIncludeEpcsHeaderListLine = softwareFilterJObject["value"].Value<string>();

                                        if (softwareFilterJObject != null && softwareFilterJObject.ContainsKey("status"))
                                        {
                                            var currentSoftwareFilterTagIdStatus = softwareFilterJObject["status"].Value<string>();
                                            if (string.Equals("1", currentSoftwareFilterTagIdStatus, StringComparison.OrdinalIgnoreCase)
                                                || string.Equals("enabled", currentSoftwareFilterTagIdStatus, StringComparison.OrdinalIgnoreCase)
                                                || string.Equals("true", currentSoftwareFilterTagIdStatus, StringComparison.OrdinalIgnoreCase))
                                            {
                                                newSoftwareFilterIncludeEpcsHeaderListEnabledLine = "1";
                                            }
                                            else
                                            {
                                                newSoftwareFilterIncludeEpcsHeaderListEnabledLine = "0";
                                            }
                                        }
                                    }
                                }
                                catch (Exception ex)


                                {
                                    commandResult = "error setting filter header list.";
                                    _logger.LogError(ex,
                                        "ProcessMqttSetCfgJsonCommand (filter epc header list): Unexpected error" +
                                        ex.Message);
                                    return commandResult;
                                }

                            if (commandPayloadJObject.ContainsKey("startTrigger"))
                                try
                                {
                                    var startTriggerJObject = commandPayloadJObject["startTrigger"].Value<JObject>();

                                    if (startTriggerJObject != null && startTriggerJObject.ContainsKey("startTriggerType"))
                                    {
                                        newStartTriggerType = startTriggerJObject["startTriggerType"].Value<string>();
                                    }
                                    if (startTriggerJObject != null && startTriggerJObject.ContainsKey("startTriggerGpiPort"))
                                    {
                                        newStartTriggerGpiPort = startTriggerJObject["startTriggerGpiPort"].Value<string>();
                                    }
                                    if (startTriggerJObject != null && startTriggerJObject.ContainsKey("startTriggerGpiEvent"))
                                    {
                                        newStartTriggerGpiEvent = startTriggerJObject["startTriggerGpiEvent"].Value<string>();
                                    }
                                    if (startTriggerJObject != null && startTriggerJObject.ContainsKey("startTriggerOffset"))
                                    {
                                        newStartTriggerOffset = startTriggerJObject["startTriggerOffset"].Value<string>();
                                    }
                                    if (startTriggerJObject != null && startTriggerJObject.ContainsKey("startTriggerPeriod"))
                                    {
                                        newStartTriggerPeriod = startTriggerJObject["startTriggerPeriod"].Value<string>();
                                    }
                                    if (startTriggerJObject != null && startTriggerJObject.ContainsKey("startTriggerUTCTimestamp"))
                                    {
                                        newStartTriggerUTCTimestamp = startTriggerJObject["startTriggerUTCTimestamp"].Value<string>();
                                    }
                                }
                                catch (Exception ex)


                                {
                                    commandResult = "error setting filter header list.";
                                    _logger.LogError(ex,
                                        "ProcessMqttSetCfgJsonCommand (filter epc header list): Unexpected error" +
                                        ex.Message);
                                    return commandResult;
                                }

                            if (commandPayloadJObject.ContainsKey("stopTrigger"))
                                try
                                {

                                    var stopTriggerJObject = commandPayloadJObject["stopTrigger"].Value<JObject>();

                                    if (stopTriggerJObject != null && stopTriggerJObject.ContainsKey("stopTriggerType"))
                                    {
                                        newStopTriggerType = stopTriggerJObject["stopTriggerType"].Value<string>();
                                    }
                                    if (stopTriggerJObject != null && stopTriggerJObject.ContainsKey("stopTriggerDuration"))
                                    {
                                        newStopTriggerDuration = stopTriggerJObject["stopTriggerDuration"].Value<string>();
                                    }
                                    if (stopTriggerJObject != null && stopTriggerJObject.ContainsKey("stopTriggerGpiEvent"))
                                    {
                                        newStopTriggerGpiEvent = stopTriggerJObject["stopTriggerGpiEvent"].Value<string>();
                                    }
                                    if (stopTriggerJObject != null && stopTriggerJObject.ContainsKey("stopTriggerGpiPort"))
                                    {
                                        newStopTriggerGpiPort = stopTriggerJObject["stopTriggerGpiPort"].Value<string>();
                                    }
                                    if (stopTriggerJObject != null && stopTriggerJObject.ContainsKey("stopTriggerTimeout"))
                                    {
                                        newStopTriggerTimeout = stopTriggerJObject["stopTriggerTimeout"].Value<string>();
                                    }



                                }
                                catch (Exception ex)


                                {
                                    commandResult = "error setting filter header list.";
                                    _logger.LogError(ex,
                                        "ProcessMqttSetCfgJsonCommand (filter epc header list): Unexpected error" +
                                        ex.Message);
                                    return commandResult;
                                }

                            _standaloneConfigDTO.transmitPower = newAntennaTransmitPowerCdbmConfigLine;
                            _standaloneConfigDTO.receiveSensitivity = newAntennaReceiveSensitivityConfigLine;
                            _standaloneConfigDTO.antennaStates = newAntennaStatesConfigLine;
                            _standaloneConfigDTO.mqttPuslishIntervalSec = newMqttPuslishIntervalSec;
                            _standaloneConfigDTO.readerMode = newReaderModeConfigLine;
                            _standaloneConfigDTO.session = newSessionConfigLine;
                            _standaloneConfigDTO.searchMode = newSearchModeConfigLine;
                            _standaloneConfigDTO.tagPopulation = newTagPopulationConfigLine;
                            _standaloneConfigDTO.softwareFilterTagIdValueOrPattern = newSoftwareFilterTagIdValueLine;
                            _standaloneConfigDTO.softwareFilterTagIdMatch = newSoftwareFilterTagIdMatchLine;
                            _standaloneConfigDTO.softwareFilterTagIdOperation = newSoftwareFilterTagIdOperationLine;
                            _standaloneConfigDTO.softwareFilterIncludeEpcsHeaderListEnabled = newSoftwareFilterIncludeEpcsHeaderListEnabledLine;
                            _standaloneConfigDTO.softwareFilterIncludeEpcsHeaderList = newSoftwareFilterIncludeEpcsHeaderListLine;


                            _standaloneConfigDTO.startTriggerType = newStartTriggerType;
                            _standaloneConfigDTO.startTriggerGpiPort = newStartTriggerGpiPort;
                            _standaloneConfigDTO.startTriggerGpiEvent = newStartTriggerGpiEvent;
                            _standaloneConfigDTO.startTriggerOffset = newStartTriggerOffset;
                            _standaloneConfigDTO.startTriggerPeriod = newStartTriggerPeriod;
                            _standaloneConfigDTO.startTriggerUTCTimestamp = newStartTriggerUTCTimestamp;

                            _standaloneConfigDTO.stopTriggerType = newStopTriggerType;
                            _standaloneConfigDTO.stopTriggerDuration = newStopTriggerDuration;
                            _standaloneConfigDTO.stopTriggerGpiEvent = newStopTriggerGpiEvent;
                            _standaloneConfigDTO.stopTriggerGpiPort = newStopTriggerGpiPort;
                            _standaloneConfigDTO.stopTriggerTimeout = newStopTriggerTimeout;




                            ConfigFileHelper.SaveFile(_standaloneConfigDTO);
                            _configurationService.SaveConfigDtoToDb(_standaloneConfigDTO);
                            _standaloneConfigDTO = _configurationService.LoadConfig();
                            //_logger.LogInformation($"Requesting image upgrade from {remoteUrl}");
                            try
                            {
                                StopPresetAsync().RunSynchronously();

                            }
                            catch (Exception)
                            {

                            }
                            if (_standaloneConfigDTO != null
                                && !string.IsNullOrEmpty(_standaloneConfigDTO.cleanupTagEventsListBatchOnReload)
                                && string.Equals("1", _standaloneConfigDTO.cleanupTagEventsListBatchOnReload, StringComparison.OrdinalIgnoreCase))
                            {
                                _smartReaderTagEventsListBatch.Clear();
                                _smartReaderTagEventsListBatchOnUpdate.Clear();
                            }

                            try
                            {
                                ApplySettingsAsync().RunSynchronously();
                            }
                            catch (Exception)
                            {

                            }
                        }
                    }
                    //SaveConfigDtoToDb(deserializedConfigData);
                }
                catch (Exception e)
                {
                    //commandStatus = "error";
                    commandResult = "unexpected error.";
                    _logger.LogError(e, "Unexpected error");
                    return commandResult;
                }
        }
        catch (Exception ex)
        {
            commandResult = "error";
            _logger.LogError(ex, "Unexpected error");
        }

        return commandResult;
#pragma warning restore CS8600, CS8601, CS8602, CS8603, CS8604, CS8625, CS8629 // Dereference of a possibly null reference.
    }

    private void UpgradeSystemImage(string imageRemoteUrl, long timeoutInMinutes = 3, long maxUpgradeRetries = 1)
    {
#pragma warning disable CS8600, CS8601, CS8602, CS8603, CS8604, CS8625, CS8629 // Dereference of a possibly null reference.
        try
        {
            _logger.LogInformation($"UpgradeSystemImage: requesting image upgrade: {imageRemoteUrl}");

            var rshell = new RShellUtil(_readerAddress, _readerUsername, _readerPassword);
            try
            {

                var stopwatchImageUpgrade = new Stopwatch();
                stopwatchImageUpgrade.Start();
                bool succeeded = false;
                int tryCounter = 0;
                while (!succeeded || (tryCounter < maxUpgradeRetries))
                {

                    try
                    {
                        if (succeeded)
                        {
                            _logger.LogInformation($"UpgradeSystemImage: operation done.");
                            break;
                        }

                        if (tryCounter > maxUpgradeRetries)
                        {
                            _logger.LogInformation($"UpgradeSystemImage: operation exceeded max tries.");
                            break;
                        }
                        var resultImageUpgrade = rshell.SendCommand("config image upgrade " + imageRemoteUrl);
                        File.WriteAllText("/customer/upgrading", "1");
                        File.WriteAllText("/customer/upgrade_config.sh", $"url_download=\"{imageRemoteUrl}\"");
                        _logger.LogInformation(resultImageUpgrade);

                        // exit the app
                        Environment.Exit(0);

                        tryCounter = tryCounter + 1;
                        _logger.LogInformation($"UpgradeSystemImage: trial # {tryCounter}");
                        stopwatchImageUpgrade.Restart();
                        while (stopwatchImageUpgrade.Elapsed.TotalMinutes < timeoutInMinutes)
                        {

                            try
                            {
                                var resultImageUpgradeProcessing = rshell.SendCommand("show image summary ");
                                if (resultImageUpgradeProcessing.Contains("Waiting for manual reboot"))
                                {
                                    succeeded = true;

                                    _logger.LogInformation($"UpgradeSystemImage: Upload done for image: {imageRemoteUrl}");
                                    _ = rshell.SendCommand("reboot");
                                    _logger.LogInformation($"UpgradeSystemImage: restarting reader.");
                                    break;
                                }
                                else
                                {
                                    Task.Delay(1000).Wait();
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "UpgradeSystemImage - Unexpected timeout error on image summary " + ex.Message);
                            }
                        }
                    }
                    catch (Exception ex)
                    {

                        _logger.LogError(ex, "UpgradeSystemImage - Unexpected error on image summary " + ex.Message);
                    }

                }


            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UpgradeSystemImage - Unexpected error on image download " + ex.Message);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "UpgradeSystemImage - Unexpected error " + ex.Message);
        }

#pragma warning restore CS8600, CS8601, CS8602, CS8603, CS8604, CS8625, CS8629 // Dereference of a possibly null reference.

    }

    //private void UpgradeSystemImage(string imageRemoteUrl)
    //{
    //    try
    //    {
    //        _logger.LogInformation($"UpgradeSystemImage: requesting image upgrade: {imageRemoteUrl}");
    //        var localImageFile = "/tmp/upgrade.upgx";
    //        try
    //        {
    //            if(File.Exists(localImageFile))
    //            {
    //                File.Delete(localImageFile);
    //            }
    //        }
    //        catch (Exception)
    //        {

    //        }

    //        var downloadTask = _httpUtil.DownloadFileAsync(imageRemoteUrl, localImageFile);
    //        downloadTask.Wait();
    //        if (downloadTask.IsCompleted)
    //        {
    //            if (File.Exists(localImageFile))
    //            {
    //                _logger.LogInformation(
    //                   $"UpgradeSystemImage: Uploading local image: {localImageFile}");
    //                var upgradeTask = _httpUtil.UploadFileAsync(_readerAddress, localImageFile, _readerUsername, _readerPassword);
    //                //var upgradeTask = _iotDeviceInterfaceClient.SystemImageUpgradePostAsync(localImageFile);
    //                upgradeTask.Wait();
    //                _logger.LogInformation(
    //                   $"UpgradeSystemImage: Upload done for image: {localImageFile}");
    //            }
    //            else
    //            {
    //                _logger.LogInformation(
    //                    $"UpgradeSystemImage: Unable to find file downloaded from: {imageRemoteUrl}");
    //            }
    //        }
    //        else
    //        {
    //            _logger.LogInformation(
    //                $"UpgradeSystemImage: Unable to check if the download was successful: {imageRemoteUrl}");
    //        }
    //    }
    //    catch (Exception ex)
    //    {
    //        _logger.LogError(ex, "UpgradeSystemImage - Unexpected error" + ex.Message);
    //        throw;
    //    }
    //}

    private async Task<string> CallIotRestFulInterfaceAsync(string bodyRequest, string endpointString, string method,
        string mqttCommandResponseTopic, bool publish)
    {
        var requestResult = "";
        var url = $"https://{_readerAddress}/api/v1" + endpointString;
        var fullUriData = new Uri(url);
        var host = fullUriData.Host;
        var baseUri = fullUriData.GetLeftPart(UriPartial.Authority);
        var rightActionPath = url.Replace(baseUri, "");

        ServicePointManager.ServerCertificateValidationCallback += (o, c, ch, er) => true;
        var httpClientHandler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback =
                (
                    message, cert, chain, errors) => true
        };
        HttpClient httpClient = new(httpClientHandler)
        {
            BaseAddress = new Uri(url)
        };
        var readerUsername = _readerUsername;
        var readerPassword = _readerPassword;
        var authenticationString = $"{readerUsername}:{readerPassword}";
        var base64EncodedAuthenticationString =
            Convert.ToBase64String(Encoding.ASCII.GetBytes(authenticationString));

        var request = new HttpRequestMessage();

        if ("GET".Equals(method))
            request = new HttpRequestMessage
            {
                Method = HttpMethod.Get,
                RequestUri = new Uri(url)
            };

        if ("POST".Equals(method))
            request = new HttpRequestMessage
            {
                Method = HttpMethod.Post,
                RequestUri = new Uri(url),
                Content = new StringContent(bodyRequest, Encoding.UTF8,
                    "application/json" /* or "application/json" in older versions */)
            };

        if ("PUT".Equals(method))
            request = new HttpRequestMessage
            {
                Method = HttpMethod.Put,
                RequestUri = new Uri(url),
                Content = new StringContent(bodyRequest, Encoding.UTF8,
                    "application/json" /* or "application/json" in older versions */)
            };

        if ("DELETE".Equals(method))
            request = new HttpRequestMessage
            {
                Method = HttpMethod.Delete,
                RequestUri = new Uri(url),
                Content = new StringContent(bodyRequest, Encoding.UTF8,
                    "application/json" /* or "application/json" in older versions */)
            };

        request.Headers.Authorization =
            new AuthenticationHeaderValue("Basic", base64EncodedAuthenticationString);
        //requestMessage.Content = content;
        request.Headers.Add("Accept", "application/json");
        //request.Headers.Add(configDto.externalApiVerificationHttpHeaderName, configDto.externalApiVerificationHttpHeaderValue); ;


        httpClient.DefaultRequestHeaders
            .Accept
            .Add(new MediaTypeWithQualityHeaderValue("application/json"));

        Log.Debug(url);

        //Console.WriteLine(jsonDocument);

        var response = httpClient.SendAsync(request);
        var content = response.Result.Content.ReadAsStringAsync();

        //Log.Debug(content);
        Console.WriteLine(content);

        if (response.Result.IsSuccessStatusCode &&
            !string.IsNullOrEmpty(response.Result.Content.ReadAsStringAsync().Result)
            && publish)
        {
            requestResult = response.Result.Content.ReadAsStringAsync().Result;
            Log.Debug(requestResult);
            //_mqttCommandClient.EnqueueAsync(_standaloneConfigDTO.mqttTopic, requestResult);
            await _mqttService.PublishAsync(mqttCommandResponseTopic, requestResult);
        }
        else
        {
            requestResult = response.Result.Content.ReadAsStringAsync().Result;

            if (!string.IsNullOrEmpty(requestResult) && publish)
            {
                await _mqttService.PublishAsync(mqttCommandResponseTopic,
                    response.Result.Content.ReadAsStringAsync().Result);
            }

        }

        return requestResult;
    }





    private async Task ProcessHttpPostTagEventDataAsync(SmartReaderTagEventData smartReaderTagEventData)
    {
        try
        {
            _ = await _httpUtil.PostTagEventToSmartReaderServerAsync(smartReaderTagEventData);
        }
        catch (Exception ex)
        {
            await ProcessGpoErrorPortAsync();
            _logger.LogError(ex, "Unexpected error on ProcessTagEventData " + ex.Message);
        }
    }


    private async Task ProcessTagInventoryEventAsync(TagInventoryEvent tagInventoryEvent)
    {
#pragma warning disable CS8600, CS8601, CS8602, CS8603, CS8604, CS8625, CS8629 // Dereference of a possibly null reference.
        try
        {
            // Validate input
            if (tagInventoryEvent == null)
            {
                _logger.LogError("Received null tag event");
                return;
            }

            // Create thread-safe collections wrapper
            // var tagCollections = new ThreadSafeCollections(
            //     _logger,
            //     _readEpcs,
            //     _currentSkus, 
            //     _currentSkuReadEpcs
            // );

            if (string.Equals("1", _standaloneConfigDTO.enablePlugin,
                                StringComparison.OrdinalIgnoreCase))
            {
                if (_plugins != null
                   && _plugins.Count > 0
                   && !_plugins.ContainsKey("LICENSE"))
                {
                    if (_standaloneConfigDTO != null
                    && !string.IsNullOrEmpty(_expectedLicense)
                    && !_expectedLicense.Equals(_standaloneConfigDTO.licenseKey.Trim()))
                    {
                        Console.WriteLine(_standaloneConfigDTO.licenseKey.Trim());
                        _logger.LogInformation("Invalid license key. ");
                        if (IsMqttEnabled())
                        {
                            var mqttManagementEvents = new Dictionary<string, object>
                            {
                                { "readerName", _standaloneConfigDTO.readerName },
                                { "mac", _iotDeviceInterfaceClient.MacAddress },
                                { "ip", _iotDeviceInterfaceClient.IpAddresses },
                                { "serial", _standaloneConfigDTO.readerSerial },
                                { "timestamp", Utils.CSharpMillisToJavaLongMicroseconds(DateTime.Now) },
                                { "smartreader-status", "invalid-license-key" }
                            };
                            await _mqttService.PublishMqttManagementEventAsync(mqttManagementEvents);
                        }
                        return;
                    }
                }
            }
            else
            {
                if (_standaloneConfigDTO != null
                    && !string.IsNullOrEmpty(_expectedLicense)
                    && !_expectedLicense.Equals(_standaloneConfigDTO.licenseKey.Trim()))
                {
                    Console.WriteLine(_standaloneConfigDTO.licenseKey.Trim());
                    _logger.LogInformation("Invalid license key. ");
                    if (IsMqttEnabled())
                    {
                        var mqttManagementEvents = new Dictionary<string, object>
                        {
                            { "readerName", _standaloneConfigDTO.readerName },
                            { "mac", _iotDeviceInterfaceClient.MacAddress },
                            { "ip", _iotDeviceInterfaceClient.IpAddresses },
                            { "serial", _standaloneConfigDTO.readerSerial },
                            { "timestamp", Utils.CSharpMillisToJavaLongMicroseconds(DateTime.Now) },
                            { "smartreader-status", "invalid-license-key" }
                        };
                        await _mqttService.PublishMqttManagementEventAsync(mqttManagementEvents);
                    }
                    return;
                }
            }




            var smartReaderTagReadEvent = new SmartReaderTagReadEvent
            {
                TagReads = []
            };
            var tagRead = new TagRead
            {
                FirstSeenTimestamp = Utils.CSharpMillisToJavaLongMicroseconds(DateTime.Now)
            };

            //if (tagInventoryEvent.LastSeenTime.HasValue)
            //    tagRead.FirstSeenTimestamp = Utils.CSharpMillisToJavaLongMicroseconds(tagInventoryEvent.LastSeenTime.Value);
            //else
            //    tagRead.FirstSeenTimestamp = Utils.CSharpMillisToJavaLongMicroseconds(DateTime.Now);


            smartReaderTagReadEvent.ReaderName = _standaloneConfigDTO.readerName;
            smartReaderTagReadEvent.Mac = _iotDeviceInterfaceClient.MacAddress;


            if (string.Equals("1", _standaloneConfigDTO.siteEnabled, StringComparison.OrdinalIgnoreCase))
                smartReaderTagReadEvent.Site = _standaloneConfigDTO.site;

            var epc = tagInventoryEvent.EpcHex;

            if (string.Equals("1", _standaloneConfigDTO.softwareFilterIncludeEpcsHeaderListEnabled,
                    StringComparison.InvariantCultureIgnoreCase))
            {
                var shouldProceed = FilterMatchingEpcforSoftwareFilter(epc).Result;
                if (!shouldProceed)
                {
                    _logger.LogInformation("Excluding EPC due filter: " + epc, SeverityType.Debug);
                    return;
                }
            }

            if (string.Equals("1", _standaloneConfigDTO.softwareFilterTagIdEnabled,
                    StringComparison.InvariantCultureIgnoreCase))
            {
                var shouldProceed = FilterMatchingEpcforSoftwareFilter(epc).Result;
                if (!shouldProceed)
                {
                    _logger.LogInformation("Excluding EPC due filter: " + epc, SeverityType.Debug);
                    return;
                }
            }





            if (string.Equals("1", _standaloneConfigDTO.parseSgtinEnabled, StringComparison.OrdinalIgnoreCase))
                try
                {
                    //_tdtEngine
                    if (epc.ToUpper().StartsWith("E3") || epc.ToUpper().StartsWith("E4"))
                    {
                        try
                        {
                            //E31234567890010000000001
                            //header:   E3
                            //SKU:      123456789001
                            //Location: 00
                            //Serial    00000001
                            var sku = epc.Substring(2, 12);
                            var serial = epc.Substring(16, 8);
                            tagRead.TagDataKeyName = "rchl_sku";
                            tagRead.TagDataKey = sku;
                            tagRead.TagDataSerial = serial;

                            if (string.Equals("1", _standaloneConfigDTO.enableValidation,
                                    StringComparison.OrdinalIgnoreCase))
                                if (!string.IsNullOrEmpty(sku))
                                {
                                    // Create an instance of ThreadSafeCollections to handle concurrent access
                                    var collections = new ThreadSafeCollections(
                                        _logger,
                                        _readEpcs,
                                        _currentSkus,
                                        _currentSkuReadEpcs
                                    );

                                    try
                                    {
                                        // Use the ValidationService to handle the validation and tracking
                                        // This ensures thread-safe operations and centralized validation logic
                                        var validationResult = await _validationService.ValidateAndTrackEpcAsync(epc, sku);

                                        if (!validationResult)
                                        {
                                            // The EPC was already processed, so we log it and continue
                                            _logger.LogInformation($"Skipping duplicate EPC: {epc} for SKU: {sku}");
                                        }
                                        else
                                        {
                                            // If we're tracking SKU counts separately, use the thread-safe collection
                                            // This provides an additional layer of safety for SKU counting
                                            if (!collections.UpdateSkuCount(sku))
                                            {
                                                _logger.LogWarning($"Failed to update count for SKU: {sku}");
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        // Log any errors that occur during validation or counting
                                        _logger.LogError(ex, $"Error processing EPC: {epc} for SKU: {sku}");
                                    }
                                }
                        }
                        catch (Exception)
                        {
                        }
                    }
                    else if (_tdtEngine != null)
                    {
                        var epcIdentifier = _tdtEngine.HexToBinary(epc);
                        var parameterList = @"tagLength=96";
                        var decodedEpc = _tdtEngine.Translate(epcIdentifier, parameterList, @"LEGACY");
                        if (string.Equals("1", _standaloneConfigDTO.parseSgtinIncludePureIdentity,
                                StringComparison.OrdinalIgnoreCase))
                            try
                            {
                                var decodedPureIdentidyEpc =
                                    _tdtEngine.Translate(epcIdentifier, parameterList, @"PURE_IDENTITY");
                                if (!string.IsNullOrEmpty(decodedPureIdentidyEpc))
                                    tagRead.TagDataPureIdentity = decodedPureIdentidyEpc;
                            }
                            catch (Exception)
                            {
                            }

                        if (!string.IsNullOrEmpty(decodedEpc))
                        {
                            var decodedEpcParts = decodedEpc.Split(";");
                            var epcKey = decodedEpcParts[0];
                            var epcSerial = "";
                            if (decodedEpcParts.Length == 2) epcSerial = decodedEpcParts[1];
                            var epcKeyParts = epcKey.Split("=");
                            if (string.Equals("1", _standaloneConfigDTO.parseSgtinIncludeKeyType,
                                    StringComparison.OrdinalIgnoreCase)) tagRead.TagDataKeyName = epcKeyParts[0];
                            tagRead.TagDataKey = epcKeyParts[1];


                            if (!string.IsNullOrEmpty(epcSerial))
                            {
                                var epcSerialParts = epcSerial.Split("=");
                                if (string.Equals("1", _standaloneConfigDTO.parseSgtinIncludeSerial,
                                        StringComparison.OrdinalIgnoreCase)) tagRead.TagDataSerial = epcSerialParts[1];
                            }

                            if (string.Equals("1", _standaloneConfigDTO.enableValidation,
                                    StringComparison.OrdinalIgnoreCase))
                                if (!string.IsNullOrEmpty(tagRead.TagDataKey))
                                {
                                    var validationResult = await _validationService.ValidateAndTrackEpcAsync(
                                        epc,
                                        tagRead.TagDataKey
                                    );

                                    if (!validationResult)
                                    {
                                        _logger.LogInformation($"Duplicate EPC detected: {epc}");
                                        // Optionally return here if you want to skip duplicate EPCs
                                    }
                                }
                        }
                    }
                    else
                    {
                        tagRead.TagDataKeyName = "unknown";
                        tagRead.TagDataKey = "unknown";
                        tagRead.TagDataSerial = "0";
                    }
                }
                catch (Exception parseEx)
                {
                    _logger.LogInformation("Error parsing Tag Data. " + parseEx.Message, SeverityType.Error);
                    tagRead.TagDataKeyName = "unknown";
                    tagRead.TagDataKey = "unknown";
                    tagRead.TagDataSerial = "0";
                }

            if (string.Equals("1", _standaloneConfigDTO.truncateEpc, StringComparison.OrdinalIgnoreCase))
                try
                {
                    var start = int.Parse(_standaloneConfigDTO.truncateStart);
                    var len = int.Parse(_standaloneConfigDTO.truncateLen);
                    epc = epc.Substring(start, len);
                }
                catch (Exception parseEx)
                {
                    _logger.LogError(parseEx, "Error truncating EPC. " + parseEx.Message);
                }

            if (!string.IsNullOrEmpty(_standaloneConfigDTO.dataPrefix)) epc = _standaloneConfigDTO.dataPrefix + epc;

            if (!string.IsNullOrEmpty(_standaloneConfigDTO.dataSuffix)) epc = epc + _standaloneConfigDTO.dataSuffix;
            tagRead.AntennaPort = tagInventoryEvent.AntennaPort;
            tagRead.Epc = epc;
            tagRead.IsHeartBeat = false;
            tagRead.AntennaZone = tagInventoryEvent.AntennaName;
            if (tagInventoryEvent.Frequency.HasValue)
                tagRead.Frequency = tagInventoryEvent.Frequency;
            if (tagInventoryEvent.PhaseAngle.HasValue)
                tagRead.RfPhase = tagInventoryEvent.PhaseAngle;
            //if (!string.IsNullOrEmpty(tagInventoryEvent.Pc))
            //    tagRead.Pc = tagInventoryEvent.Pc;
            if (tagInventoryEvent.TransmitPowerCdbm.HasValue)
                tagRead.TxPower = tagInventoryEvent.TransmitPowerCdbm / 100;
            if (tagInventoryEvent.PeakRssiCdbm.HasValue)
                tagRead.PeakRssi = tagInventoryEvent.PeakRssiCdbm / 100;
            //if (tagInventoryEvent.LastSeenTime.HasValue)
            //    smartReaderTagEventData.VistoUltimaVez = tagInventoryEvent.LastSeenTime.Value.ToString("o");
            tagRead.Tid = tagInventoryEvent.TidHex;

            if (IsGpiEnabled())
            {
                if (_gpiPortStates.ContainsKey(0))
                {
                    if (_gpiPortStates[0])
                        tagRead.Gpi1Status = "high";
                    else
                        tagRead.Gpi1Status = "low";
                }

                if (_gpiPortStates.ContainsKey(1))
                {
                    if (_gpiPortStates[1])
                        tagRead.Gpi2Status = "high";
                    else
                        tagRead.Gpi2Status = "low";
                }
            }

            //============================================================================================
            // PLUGIN BEGIN
            //============================================================================================
            if (_standaloneConfigDTO != null && !string.IsNullOrEmpty(_standaloneConfigDTO.enablePlugin)
                                             && string.Equals("1", _standaloneConfigDTO.enablePlugin.Trim(),
                                                 StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    //_logger.LogInformation("Processing data: " + epc, SeverityType.Debug);
                    if (_plugins != null
                        && _plugins.Count > 0
                        && !string.IsNullOrEmpty(_standaloneConfigDTO.activePlugin)
                        && _plugins.ContainsKey(_standaloneConfigDTO.activePlugin)
                        && _plugins[_standaloneConfigDTO.activePlugin].IsProcessingTagData())
                    {
                        _plugins[_standaloneConfigDTO.activePlugin].AddTagDataToQueue(tagRead);
                        return;
                    }
                }
                catch (Exception)
                {

                }

            }
            //============================================================================================
            //  PLUGIN END
            //============================================================================================

            var isPositioningEvent = false;
            var isNonPositioningEvent = false;
            var shouldProccessPositioningEvents = false;
            List<LastPositioningReferecenceEpc> lastPositioningReferecenceEpcs;

            if (_standaloneConfigDTO != null && !string.IsNullOrEmpty(_standaloneConfigDTO.positioningEpcsEnabled)
                                             && string.Equals("1", _standaloneConfigDTO.positioningEpcsEnabled,
                                                 StringComparison.OrdinalIgnoreCase))
            {
                shouldProccessPositioningEvents = true;
                isPositioningEvent = FilterMatchingAntennaAndEpcforPositioning(tagRead).Result;
                isNonPositioningEvent = FilterMatchingAntennaAndEpcforNonPositioning(tagRead).Result;

                if (!isPositioningEvent && !isNonPositioningEvent)
                {
                    _logger.LogInformation(
                        "ProcessTagInventoryEventAsync: Positioning filter is enabled. Unexpected event while processing positioning: EPC [" +
                        tagRead.Epc + "] Antenna [" + tagRead.AntennaPort + "], ignoring event.", SeverityType.Error);
                    return;
                }

                if (isPositioningEvent)
                    _ = SavePositioningDataAsync(tagRead, true);
                else if (isNonPositioningEvent) _ = SavePositioningDataAsync(tagRead, false);
            }

            string? jsonData = null;

            if (shouldProccessPositioningEvents)
            {
                var positioningEvents = GetLastPositioningDataAsync(tagRead.FirstSeenTimestamp.Value, true);
                lastPositioningReferecenceEpcs = positioningEvents.Result;

                var smartReaderTagReadPositioningEvent = new SmartReaderTagReadEvent
                {
                    ReaderName = _standaloneConfigDTO.readerName
                };


                smartReaderTagReadEvent.Mac = _iotDeviceInterfaceClient.MacAddress;

                if (string.Equals("1", _standaloneConfigDTO.siteEnabled, StringComparison.OrdinalIgnoreCase))
                    smartReaderTagReadPositioningEvent.Site = _standaloneConfigDTO.site;

                smartReaderTagReadPositioningEvent.TagReadsPositioning = [];

                var tagReadPositioning = GetPositioningDataToReportAsync(tagRead.Epc).Result;

                if (tagReadPositioning == null || string.IsNullOrEmpty(tagReadPositioning.Epc))
                {
                    _logger.LogInformation(
                        tagRead.Epc + " - Discarding EPC data due to time filtering (positioningReportIntervalInSec)",
                        SeverityType.Error);
                    return;
                }

                _logger.LogInformation(" Publishing data: " + tagReadPositioning.Epc, SeverityType.Debug);


                if (lastPositioningReferecenceEpcs.Any())
                {
                    tagReadPositioning.LastPositioningReferecenceEpcs = [.. lastPositioningReferecenceEpcs];
                }

                smartReaderTagReadPositioningEvent.TagReadsPositioning.Add(tagReadPositioning);
                jsonData = JsonConvert.SerializeObject(smartReaderTagReadPositioningEvent);
            }
            else
            {
                smartReaderTagReadEvent.TagReads.Add(tagRead);
                jsonData = JsonConvert.SerializeObject(smartReaderTagReadEvent);
            }


            var dataToPublish = JObject.Parse(jsonData);


            if (string.IsNullOrEmpty(_standaloneConfigDTO.includeAntennaPort)
                || string.Equals("0", _standaloneConfigDTO.includeAntennaPort, StringComparison.OrdinalIgnoreCase))
                try
                {

                    dataToPublish.Property("tag_reads").Descendants()
                        .OfType<JProperty>()
                        .Where(attr => attr.Name.StartsWith("antennaPort"))
                        .ToList()
                        .ForEach(attr => attr.Remove());

                    dataToPublish.Property("tag_reads").Descendants()
                        .OfType<JProperty>()
                        .Where(attr => attr.Name.StartsWith("txPower"))
                        .ToList()
                        .ForEach(attr => attr.Remove());

                }
                catch (Exception)
                {
                }


            if (string.IsNullOrEmpty(_standaloneConfigDTO.includeAntennaZone)
                || string.Equals("0", _standaloneConfigDTO.includeAntennaZone, StringComparison.OrdinalIgnoreCase))
                try
                {

                    dataToPublish.Property("tag_reads").Descendants()
                        .OfType<JProperty>()
                        .Where(attr => attr.Name.StartsWith("antennaZone"))
                        .ToList()
                        .ForEach(attr => attr.Remove());

                }
                catch (Exception)
                {
                }

            if (string.IsNullOrEmpty(_standaloneConfigDTO.includeFirstSeenTimestamp)
                || string.Equals("0", _standaloneConfigDTO.includeFirstSeenTimestamp,
                    StringComparison.OrdinalIgnoreCase))
                try
                {
                    if (string.Equals("0", _standaloneConfigDTO.tagPresenceTimeoutEnabled, StringComparison.OrdinalIgnoreCase))
                    {

                        dataToPublish.Property("tag_reads").Descendants()
                        .OfType<JProperty>()
                        .Where(attr => attr.Name.StartsWith("firstSeenTimestamp"))
                        .ToList()
                        .ForEach(attr => attr.Remove());

                    }

                }
                catch (Exception)
                {
                }

            if (string.IsNullOrEmpty(_standaloneConfigDTO.heartbeatEnabled)
                || string.Equals("0", _standaloneConfigDTO.heartbeatEnabled, StringComparison.OrdinalIgnoreCase))
                try
                {

                    dataToPublish.Property("tag_reads").Descendants()
                        .OfType<JProperty>()
                        .Where(attr => attr.Name.StartsWith("isHeartBeat"))
                        .ToList()
                        .ForEach(attr => attr.Remove());



                    dataToPublish.Property("tag_reads").Descendants()
                        .OfType<JProperty>()
                        .Where(attr => attr.Name.StartsWith("isInventoryStatus"))
                        .ToList()
                        .ForEach(attr => attr.Remove());

                }
                catch (Exception)
                {
                }


            if (!string.IsNullOrEmpty(_standaloneConfigDTO.customField1Enabled)
                && string.Equals("1", _standaloneConfigDTO.customField1Enabled, StringComparison.OrdinalIgnoreCase))
            {
                var newPropertyData = new JProperty(_standaloneConfigDTO.customField1Name,
                    _standaloneConfigDTO.customField1Value);
                dataToPublish.Add(newPropertyData);
            }

            if (!string.IsNullOrEmpty(_standaloneConfigDTO.customField2Enabled)
                && string.Equals("1", _standaloneConfigDTO.customField2Enabled, StringComparison.OrdinalIgnoreCase))
            {
                var newPropertyData = new JProperty(_standaloneConfigDTO.customField2Name,
                    _standaloneConfigDTO.customField2Value);
                dataToPublish.Add(newPropertyData);
            }

            if (!string.IsNullOrEmpty(_standaloneConfigDTO.customField3Enabled)
                && string.Equals("1", _standaloneConfigDTO.customField3Enabled, StringComparison.OrdinalIgnoreCase))
            {
                var newPropertyData = new JProperty(_standaloneConfigDTO.customField3Name,
                    _standaloneConfigDTO.customField3Value);
                dataToPublish.Add(newPropertyData);
            }

            if (!string.IsNullOrEmpty(_standaloneConfigDTO.customField4Enabled)
                && string.Equals("1", _standaloneConfigDTO.customField4Enabled, StringComparison.OrdinalIgnoreCase))
            {
                var newPropertyData = new JProperty(_standaloneConfigDTO.customField4Name,
                    _standaloneConfigDTO.customField4Value);
                dataToPublish.Add(newPropertyData);
            }

            if (_mqttPublishingConfiguration.BatchListEnabled
                || _mqttPublishingConfiguration.BatchListPublishingEnabled)
            {
                _ = await _batchProcessor.ProcessEventBatchAsync(tagRead, dataToPublish);
            }
            else
            {
                if (string.Equals("1", _standaloneConfigDTO.enableValidation,
                    StringComparison.OrdinalIgnoreCase)
                    || string.Equals("1", _standaloneConfigDTO.groupEventsOnInventoryStatus,
                        StringComparison.OrdinalIgnoreCase))
                {
                    _messageQueueTagSmartReaderTagEventGroupToValidate.Enqueue(dataToPublish);
                }
                else
                {
                    EnqueueToExternalPublishers(dataToPublish, true);
                }
            }

            // if (!string.IsNullOrEmpty(_standaloneConfigDTO.enableTagEventsListBatch)
            //             && string.Equals("1", _standaloneConfigDTO.enableTagEventsListBatch,
            //                 StringComparison.OrdinalIgnoreCase))
            // {
            //     try
            //     {
            //         if (_smartReaderTagEventsListBatch.TryGetValue(tagRead.Epc, out JObject retrievedValue))
            //         {

            //             try
            //             {
            //                 var existingEventOnCurrentAntenna = (JObject)(retrievedValue["tag_reads"].FirstOrDefault(q => (long)q["antennaPort"] == tagRead.AntennaPort));


            //                 if (string.Equals("1", _standaloneConfigDTO.filterTagEventsListBatchOnChangeBasedOnAntennaZone, StringComparison.OrdinalIgnoreCase))
            //                 {
            //                     var existingEventOnCurrentAntennaZone = (JObject)(retrievedValue["tag_reads"].FirstOrDefault(q => (string)q["epc"] == tagRead.Epc));
            //                     //var zoneNames = _standaloneConfigDTO.antennaZones.Split(",");
            //                     if (existingEventOnCurrentAntennaZone != null)
            //                     {
            //                         var currentAntennazone = existingEventOnCurrentAntennaZone["antennaZone"].Value<string>();
            //                         if (!string.IsNullOrEmpty(currentAntennazone)
            //                             && !tagRead.AntennaZone.Equals(currentAntennazone))
            //                         {
            //                             if (string.Equals("1", _standaloneConfigDTO.updateTagEventsListBatchOnChange, StringComparison.OrdinalIgnoreCase)
            //                             && !_smartReaderTagEventsListBatchOnUpdate.ContainsKey(tagRead.Epc))
            //                             {
            //                                 _smartReaderTagEventsListBatchOnUpdate.TryAdd(tagRead.Epc, dataToPublish);
            //                             }
            //                         }
            //                     }
            //                 }
            //                 else
            //                 {
            //                     // it has been read on a different antenna:
            //                     if (existingEventOnCurrentAntenna == null)
            //                     {
            //                         if (string.Equals("1", _standaloneConfigDTO.updateTagEventsListBatchOnChange, StringComparison.OrdinalIgnoreCase)
            //                             && !_smartReaderTagEventsListBatchOnUpdate.ContainsKey(tagRead.Epc))
            //                         {
            //                             _smartReaderTagEventsListBatchOnUpdate.TryAdd(tagRead.Epc, dataToPublish);
            //                         }
            //                     }
            //                 }
            //             }
            //             catch (Exception)
            //             {


            //             }
            //             _smartReaderTagEventsListBatch.TryUpdate(tagRead.Epc, dataToPublish, retrievedValue);
            //         }
            //         else
            //         {

            //             if (string.Equals("1", _standaloneConfigDTO.updateTagEventsListBatchOnChange, StringComparison.OrdinalIgnoreCase)
            //                 && !_smartReaderTagEventsListBatchOnUpdate.ContainsKey(tagRead.Epc))
            //             {
            //                 _smartReaderTagEventsListBatchOnUpdate.TryAdd(tagRead.Epc, dataToPublish);
            //             }
            //             else
            //             {
            //                 _smartReaderTagEventsListBatch.TryAdd(tagRead.Epc, dataToPublish);
            //             }

            //         }
            //     }
            //     catch (Exception)
            //     {


            //     }
            // }
            // else
            // {
            //     if (string.Equals("1", _standaloneConfigDTO.enableValidation, StringComparison.OrdinalIgnoreCase)
            //     || string.Equals("1", _standaloneConfigDTO.groupEventsOnInventoryStatus,
            //         StringComparison.OrdinalIgnoreCase))
            //         _messageQueueTagSmartReaderTagEventGroupToValidate.Enqueue(dataToPublish);
            //     else
            //         EnqueueToExternalPublishers(dataToPublish, true);
            // }


        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error on ProcessTagInventoryEventsync. " + ex.Message);
        }
#pragma warning restore CS8600, CS8601, CS8602, CS8603, CS8604, CS8625, CS8629 // Dereference of a possibly null reference.   
    }

    //    private void EnqueueToPublishers(JObject dataToPublish)
    //    {
    //        try
    //        {
    //#pragma warning disable CS8602 // Dereference of a possibly null reference.
    //            if (string.Equals("1", _standaloneConfigDTO.enableSummaryStream, StringComparison.OrdinalIgnoreCase))
    //                if (!string.Equals("1", _standaloneConfigDTO.groupEventsOnInventoryStatus,
    //                        StringComparison.OrdinalIgnoreCase))
    //                {
    //                    var defaultTagEvent =
    //                        dataToPublish.Property("tag_reads").ToList().FirstOrDefault().FirstOrDefault();
    //#pragma warning disable CS8602 // Dereference of a possibly null reference.
    //                    if (((JObject)defaultTagEvent).ContainsKey("tagDataKey"))
    //                    {
    //                        var eventTimestamp = Utils.CSharpMillisToJavaLongMicroseconds(DateTime.Now);
    //                        var localBarcode = "";
    //                        if (((JObject)defaultTagEvent).ContainsKey("barcode"))
    //                            localBarcode = ((JObject)defaultTagEvent)["barcode"].Value<string>();
    //                        var skuSummaryList = new Dictionary<string, SkuSummary>();
    //                        var tagDataKeyValue = ((JObject)defaultTagEvent)["tagDataKey"].Value<string>();
    //                        var skuSummary = new SkuSummary();
    //                        skuSummary.Sku = tagDataKeyValue;
    //                        skuSummary.Qty = 1;
    //                        skuSummary.Barcode = localBarcode;
    //                        skuSummary.EventTimestamp = eventTimestamp;
    //                        skuSummaryList.Add(tagDataKeyValue, skuSummary);
    //                        var skuArray = JArray.FromObject(skuSummaryList.Values);
    //                        AddJsonSkuSummaryToQueue(skuArray);
    //                    }
    //#pragma warning restore CS8602 // Dereference of a possibly null reference.
    //                }
    //#pragma warning restore CS8602 // Dereference of a possibly null reference.

    //            if (string.Equals("1", _standaloneConfigDTO.enableTagEventStream, StringComparison.OrdinalIgnoreCase))
    //                SaveJsonTagEventToDb(dataToPublish);

    //            if (string.Equals("1", _standaloneConfigDTO.httpPostEnabled, StringComparison.OrdinalIgnoreCase))
    //                try
    //                {
    //                    if (_messageQueueTagSmartReaderTagEventHttpPost.Count < 1000)
    //                        _messageQueueTagSmartReaderTagEventHttpPost.Enqueue(dataToPublish);
    //                }
    //                catch (Exception)
    //                {
    //                }
    //        }
    //        catch (Exception)
    //        {
    //        }
    //    }

    public void EnqueueToExternalPublishers(JObject dataToPublish, bool shouldValidate)
    {
#pragma warning disable CS8600, CS8601, CS8602, CS8603, CS8604, CS8625, CS8629 // Dereference of a possibly null reference.
        try
        {




            if (string.Equals("1", _standaloneConfigDTO.enableSummaryStream, StringComparison.OrdinalIgnoreCase)
                && shouldValidate)
                try
                {
                    if (_messageQueueTagSmartReaderTagEventGroupToValidate.Count < 1000)
                        _messageQueueTagSmartReaderTagEventGroupToValidate.Enqueue(dataToPublish);
                }
                catch (Exception)
                {
                }


            if (IsSocketServerEnabled())
                try
                {
                    if (_tcpSocketService.IsHealthy())
                    {
                        try
                        {
                            if (_tcpSocketService.IsSocketServerConnectedToClients())
                            {
                                const int MaxQueueSize = 1000;
                                if (_messageQueueTagSmartReaderTagEventSocketServer.Count < MaxQueueSize)
                                {
                                    _messageQueueTagSmartReaderTagEventSocketServer.Enqueue(dataToPublish);
                                    if (_messageQueueTagSmartReaderTagEventSocketServer.Count > (MaxQueueSize * 0.8))
                                    {
                                        _logger.LogWarning($"Socket queue filling up: {_messageQueueTagSmartReaderTagEventSocketServer.Count}/{MaxQueueSize}");
                                    }
                                }
                                else
                                {
                                    _logger.LogError($"Socket queue full ({MaxQueueSize}), dropping message");
                                    _logger.LogDebug($"Cleaning up Socket queue due to full Socket queue.");
                                    _messageQueueTagSmartReaderTagEventSocketServer.Clear();
                                }
                            }
                            else
                            {
                                _logger.LogDebug($"Cleaning up Socket queue due to lack of socket clients.");
                                _messageQueueTagSmartReaderTagEventSocketServer.Clear();

                            }
                        }
                        catch (Exception)
                        {
                            _logger.LogDebug($"Error enqueuing keepalive event. Cleaning up Socket queue.");
                            _messageQueueTagSmartReaderTagEventSocketServer.Clear();
                        }
                    }

                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to enqueue socket message");
                }

            if (IsUsbFlashDriveEnabled())
                try
                {
                    if (_messageQueueTagSmartReaderTagEventUsbDrive.Count < 1000)
                        _messageQueueTagSmartReaderTagEventUsbDrive.Enqueue(dataToPublish);
                }
                catch (Exception)
                {
                }


            if (IsUdpServerEnabled())
                try
                {
                    if (_messageQueueTagSmartReaderTagEventUdpServer.Count < 1000)
                        _messageQueueTagSmartReaderTagEventUdpServer.Enqueue(dataToPublish);
                }
                catch (Exception)
                {
                }

            if (IsHttpPostEnabled())
                try
                {
                    if (!string.IsNullOrEmpty(_standaloneConfigDTO.httpPostURL))
                    {
                        if (_messageQueueTagSmartReaderTagEventHttpPost.Count < 1000)
                            _messageQueueTagSmartReaderTagEventHttpPost.Enqueue(dataToPublish);
                    }

                }
                catch (Exception)
                {
                }

            if (IsMqttEnabled()
                && !string.Equals("127.0.0.1", _standaloneConfigDTO.mqttBrokerAddress, StringComparison.OrdinalIgnoreCase))
                try
                {
                    if (_mqttService.CheckConnection())
                    {
                        if (!string.IsNullOrEmpty(_standaloneConfigDTO.mqttTagEventsTopic))
                        {
                            if (_messageQueueTagSmartReaderTagEventMqtt.Count < 1000)
                                _messageQueueTagSmartReaderTagEventMqtt.Enqueue(dataToPublish);
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Unable to check if the MQTT connection is active, dropping message.");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error enqueuing message to MQTT.");
                }

            if (string.Equals("1", _standaloneConfigDTO.serialPort, StringComparison.OrdinalIgnoreCase))
                try
                {
                    var line = SmartReaderJobs.Utils.Utils.ExtractLineFromJsonObject(dataToPublish, _standaloneConfigDTO, _logger);
                    try
                    {
                        if (_serialTty != null)
                        {
                            if (!_serialTty.IsOpen) _serialTty.Open();

                            if (_serialTty.IsOpen)
                            {
                                if (!string.IsNullOrEmpty(line))
                                {
                                    Console.WriteLine(line);
                                    var epcMessage = Encoding.UTF8.GetBytes(line);

                                    _serialTty.Write(epcMessage, 0, epcMessage.Length);
                                    //_serial_tty.WriteLine(line);
                                }
                                else
                                {
                                    _logger.LogInformation("serial data is empty.");
                                }
                            }
                        }
                        else
                        {
                            _logger.LogInformation("_serial_tty is not set.");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex,
                            "ProcessTagInventoryEventAsync: error processing serial port " + ex.Message);
                    }
                }
                catch (Exception ex1)
                {
                    _logger.LogError(ex1, "ProcessTagInventoryEventAsync: error processing serial port " + ex1.Message);
                }

            if (IsOpcUaClientEnabled())
                try
                {
                    if (_messageQueueTagSmartReaderTagEventOpcUa.Count < 1000)
                        _messageQueueTagSmartReaderTagEventOpcUa.Enqueue(dataToPublish);
                }
                catch (Exception)
                {
                }
        }
        catch (Exception)
        {
        }
#pragma warning restore CS8600, CS8601, CS8602, CS8603, CS8604, CS8625, CS8629 // Dereference of a possibly null reference.
    }





#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
    public async Task<bool> FilterMatchingEpcforSoftwareFilter(string epc)
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
    {
        if (_standaloneConfigDTO != null
            && string.Equals("1", _standaloneConfigDTO.softwareFilterIncludeEpcsHeaderListEnabled,
                StringComparison.InvariantCultureIgnoreCase)
            && !string.IsNullOrEmpty(_standaloneConfigDTO.softwareFilterIncludeEpcsHeaderList))
        {
            var epcsHeaderList = _standaloneConfigDTO.softwareFilterIncludeEpcsHeaderList.Split(",");

            if (epcsHeaderList.Any(p => epc.ToUpper().StartsWith(p.ToUpper()))) return true;
        }

        return false;
    }

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
    public async Task<bool> FilterTagIdForSoftwareFilter(string epc)
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
    {
        if (_standaloneConfigDTO != null
            && string.Equals("1", _standaloneConfigDTO.softwareFilterTagIdEnabled,
                StringComparison.InvariantCultureIgnoreCase))
        {
            if (string.Equals("prefix", _standaloneConfigDTO.softwareFilterTagIdMatch,
                StringComparison.InvariantCultureIgnoreCase))
            {
                if (string.Equals("include", _standaloneConfigDTO.softwareFilterTagIdOperation,
                StringComparison.InvariantCultureIgnoreCase))
                {
                    if (epc.StartsWith(_standaloneConfigDTO.softwareFilterTagIdValueOrPattern))
                    {
                        return true;
                    }
                }
                else
                {
                    if (!epc.StartsWith(_standaloneConfigDTO.softwareFilterTagIdValueOrPattern))
                    {
                        return true;
                    }
                }
            }
            else if (string.Equals("suffix", _standaloneConfigDTO.softwareFilterTagIdMatch,
                StringComparison.InvariantCultureIgnoreCase))
            {
                if (string.Equals("include", _standaloneConfigDTO.softwareFilterTagIdOperation,
                StringComparison.InvariantCultureIgnoreCase))
                {
                    if (epc.EndsWith(_standaloneConfigDTO.softwareFilterTagIdValueOrPattern))
                    {
                        return true;
                    }
                }
                else
                {
                    if (!epc.EndsWith(_standaloneConfigDTO.softwareFilterTagIdValueOrPattern))
                    {
                        return true;
                    }
                }
            }
            else if (string.Equals("regex", _standaloneConfigDTO.softwareFilterTagIdMatch,
                StringComparison.InvariantCultureIgnoreCase))
            {

                bool regexMatches = Regex.IsMatch(epc, _standaloneConfigDTO.softwareFilterTagIdValueOrPattern);

                if (string.Equals("include", _standaloneConfigDTO.softwareFilterTagIdOperation,
                StringComparison.InvariantCultureIgnoreCase))
                {
                    if (regexMatches)
                    {
                        return true;
                    }
                }
                else
                {
                    if (!regexMatches)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
    public async Task<bool> FilterMatchingAntennaAndEpcforPositioning(TagRead tagRead)
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
    {
#pragma warning disable CS8600, CS8601, CS8602, CS8603, CS8604, CS8625, CS8629 // Dereference of a possibly null reference.
        if (!string.IsNullOrEmpty(_standaloneConfigDTO.positioningAntennaPorts) &&
            !string.IsNullOrEmpty(_standaloneConfigDTO.positioningEpcsHeaderList))
        {
            var postioningAntennas = new List<int>();
            var paramPositioningEpcsHeaderList = _standaloneConfigDTO.positioningEpcsHeaderList.Split(",");
            var paramPositioningAntennas = _standaloneConfigDTO.positioningAntennaPorts.Split(",");
            for (var i = 0; i < paramPositioningAntennas.Length; i++)
                try
                {
                    postioningAntennas.Add(int.Parse(paramPositioningAntennas[i]));
                }
                catch (Exception)
                {
                }

            if (postioningAntennas.Any()
                && postioningAntennas.Contains((int)tagRead.AntennaPort)
                && paramPositioningEpcsHeaderList.Any(p => tagRead.Epc.StartsWith(p)))
                return true;
        }


        return false;
#pragma warning restore CS8600, CS8601, CS8602, CS8603, CS8604, CS8625, CS8629 // Dereference of a possibly null reference.
    }

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
    public async Task<bool> FilterMatchingAntennaAndEpcforNonPositioning(TagRead tagRead)
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
    {
#pragma warning disable CS8600, CS8601, CS8602, CS8603, CS8604, CS8625, CS8629 // Dereference of a possibly null reference.
        if (!string.IsNullOrEmpty(_standaloneConfigDTO.positioningAntennaPorts) &&
            !string.IsNullOrEmpty(_standaloneConfigDTO.positioningEpcsHeaderList))
        {
            var postioningAntennas = new List<int>();
            var paramPositioningEpcsHeaderList = _standaloneConfigDTO.positioningEpcsHeaderList.Split(",");
            var paramPositioningAntennas = _standaloneConfigDTO.positioningAntennaPorts.Split(",");
            for (var i = 0; i < paramPositioningAntennas.Length; i++)
                try
                {
                    postioningAntennas.Add(int.Parse(paramPositioningAntennas[i]));
                }
                catch (Exception)
                {
                }

            if (postioningAntennas.Any()
                && !postioningAntennas.Contains((int)tagRead.AntennaPort)
                && !paramPositioningEpcsHeaderList.Any(p => tagRead.Epc.StartsWith(p)))
            {
                if (!string.IsNullOrEmpty(_standaloneConfigDTO.positioningEpcsFilter))
                {
                    var paramPositioningEpcsFilter = _standaloneConfigDTO.positioningEpcsFilter.Split(",");
                    if (paramPositioningEpcsFilter.Any(p => tagRead.Epc.StartsWith(p))) return true;
                }
                else
                {
                    return true;
                }
            }
        }
#pragma warning restore CS8600, CS8601, CS8602, CS8603, CS8604, CS8625, CS8629 // Dereference of a possibly null reference.

        return false;
    }

    public async Task SavePositioningDataAsync(TagRead tagRead, bool IsPositioningHeader)
    {
#pragma warning disable CS8600, CS8601, CS8602, CS8603, CS8604, CS8625, CS8629 // Dereference of a possibly null reference.
        try
        {
            using var scope = Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<RuntimeDb>();
            var postioningEpcsData = await dbContext.PostioningEpcs.FindAsync(tagRead.Epc);
            if (postioningEpcsData != null)
            {
                postioningEpcsData.PeakRssi = tagRead.PeakRssi;
                postioningEpcsData.AntennaPort = tagRead.AntennaPort.Value;
                postioningEpcsData.TxPower = tagRead.TxPower;
                postioningEpcsData.LastSeenTimestamp = tagRead.FirstSeenTimestamp;
                postioningEpcsData.AntennaZone = tagRead.AntennaZone;
                postioningEpcsData.LastSeenOn =
                    DateTimeOffset.FromUnixTimeMilliseconds(tagRead.FirstSeenTimestamp.Value);
                postioningEpcsData.RfChannel = tagRead.RfChannel;
                postioningEpcsData.RfPhase = tagRead.RfPhase;
                postioningEpcsData.IsPositioningHeader = IsPositioningHeader;

                _ = dbContext.PostioningEpcs.Update(postioningEpcsData);
                _ = await dbContext.SaveChangesAsync();
            }
            else
            {
                postioningEpcsData = new PostioningEpcs
                {
                    Epc = tagRead.Epc,
                    PeakRssi = tagRead.PeakRssi,
                    AntennaPort = tagRead.AntennaPort.Value,
                    TxPower = tagRead.TxPower,
                    FirstSeenTimestamp = tagRead.FirstSeenTimestamp,
                    LastSeenTimestamp = tagRead.FirstSeenTimestamp,
                    FirstSeenOn =
                    DateTimeOffset.FromUnixTimeMilliseconds(tagRead.FirstSeenTimestamp.Value),
                    LastSeenOn =
                    DateTimeOffset.FromUnixTimeMilliseconds(tagRead.FirstSeenTimestamp.Value),
                    AntennaZone = tagRead.AntennaZone,
                    RfChannel = tagRead.RfChannel,
                    RfPhase = tagRead.RfPhase,
                    IsPositioningHeader = IsPositioningHeader
                };

                _ = dbContext.PostioningEpcs.Add(postioningEpcsData);
                _ = await dbContext.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error on SavePositioningDataAsync. " + ex.Message);
        }
#pragma warning restore CS8600, CS8601, CS8602, CS8603, CS8604, CS8625, CS8629 // Dereference of a possibly null reference.
    }

    //public PostioningEpcs? GetPositioningEpc()
    //{
    //    return positioningEpc;
    //}

    public async Task<TagReadPositioning> GetPositioningDataToReportAsync(string epc)
    {
#pragma warning disable CS8600, CS8601, CS8602, CS8603, CS8604, CS8625, CS8629 // Dereference of a possibly null reference.
        var tagReadPositioning = new TagReadPositioning();
        var positioningReferecenceEpcs = new List<LastPositioningReferecenceEpc>();
        try
        {

            var reportInterval = int.Parse(_standaloneConfigDTO.positioningReportIntervalInSec);
            using var scope = Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<RuntimeDb>();
            var postioningEpcsList = dbContext.PostioningEpcs
                .Where(e => e.Epc != null
                            && e.Epc.Equals(epc)
                            && e.LastSeenTimestamp != null
                            && e.FirstSeenTimestamp != null
                            && (e.LastSeenOn == e.FirstSeenOn ||
                                e.LastSeenOn.Subtract(e.FirstSeenOn).TotalSeconds > reportInterval))
                .OrderBy(f => f.LastSeenTimestamp)
                .ToList();

            if (postioningEpcsList.Any())
            {
                var shouldPublish = false;

                var positioningEpc = postioningEpcsList.FirstOrDefault();

                if (positioningEpc.LastPublishedOn.HasValue)
                {
                    var totalSeconds = positioningEpc.LastSeenOn.UtcDateTime
                        .Subtract(positioningEpc.LastPublishedOn.Value.UtcDateTime).TotalSeconds;
                    if (totalSeconds > reportInterval)
                    {
                        shouldPublish = true;
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(positioningEpc.Epc))
                            _logger.LogInformation(
                                positioningEpc.Epc +
                                " - Discarding EPC data due to time filtering (positioningReportIntervalInSec), current diff [" +
                                totalSeconds + "] ", SeverityType.Error);
                    }
                }
                else
                {
                    shouldPublish = true;
                }


                tagReadPositioning.AntennaPort = positioningEpc.AntennaPort;
                tagReadPositioning.Epc = positioningEpc.Epc;
                tagReadPositioning.AntennaZone = positioningEpc.AntennaZone;
                tagReadPositioning.FirstSeenTimestamp = positioningEpc.LastSeenTimestamp;
                tagReadPositioning.PeakRssi = positioningEpc.PeakRssi;
                tagReadPositioning.RfPhase = positioningEpc.RfPhase;
                tagReadPositioning.RfChannel = positioningEpc.RfChannel;
                tagReadPositioning.IsHeartBeat = false;

                if (!shouldPublish) return null;
                try
                {
                    positioningEpc.LastPublishedOn = positioningEpc.LastSeenOn;
                    _ = dbContext.PostioningEpcs.Update(positioningEpc);
                    _ = await dbContext.SaveChangesAsync();
                }
                catch (Exception)
                {
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error on GetPositioningDataToReportAsync. " + ex.Message);
        }

        return tagReadPositioning;
#pragma warning restore CS8600, CS8601, CS8602, CS8603, CS8604, CS8625, CS8629 // Dereference of a possibly null reference.
    }

    public async void AddJsonSkuSummaryToQueue(JArray dto)
    {
        try
        {
            _logger.LogInformation("SaveJsonSkuSummaryToDb... ");
            await readLock.WaitAsync();
            if (_standaloneConfigDTO != null
                && !"127.0.0.1".Equals(_standaloneConfigDTO.mqttBrokerAddress))
            {

                using var scope = Services.CreateScope();
                var summaryQueueBackgroundService = scope.ServiceProvider.GetRequiredService<ISummaryQueueBackgroundService>();
                var JsonSkuSummary = JsonConvert.SerializeObject(dto);
                summaryQueueBackgroundService.AddData(JsonSkuSummary);
            }
            else
            {
                _ = ProcessMqttJsonJarrayAsync(dto);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error on SaveJsonTagEventToDb. " + ex.Message);
        }
        finally
        {
            _ = readLock.Release();
        }
    }

    //public async void SaveJsonSkuSummaryToDb(JArray dto)
    //{
    //    try
    //    {
    //        _logger.LogInformation("SaveJsonSkuSummaryToDb... ");
    //        await readLock.WaitAsync();
    //        using var scope = Services.CreateScope();
    //        var dbContext = scope.ServiceProvider.GetRequiredService<RuntimeDb>();
    //        var configModel = new SmartReaderSkuSummaryModel();
    //        configModel.Value = JsonConvert.SerializeObject(dto);
    //        dbContext.SmartReaderSkuSummaryModels.Add(configModel);
    //        await dbContext.SaveChangesAsync();
    //    }
    //    catch (Exception ex)
    //    {
    //        _logger.LogError(ex, "Unexpected error on SaveJsonTagEventToDb. " + ex.Message);
    //    }
    //    finally
    //    {
    //        readLock.Release();
    //    }
    //}









    public async Task<List<LastPositioningReferecenceEpc>> GetLastPositioningDataAsync(long currentEventTimestamp,
        bool lookupForPositioningHeaderOnly)
    {
#pragma warning disable CS8600, CS8601, CS8602, CS8603, CS8604, CS8625, CS8629 // Dereference of a possibly null reference.
        var positioningReferecenceEpcs = new List<LastPositioningReferecenceEpc>();
        try
        {
            using var scope = Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<RuntimeDb>();
            var postioningEpcsList = dbContext.PostioningEpcs
                .Where(e => e.Epc != null
                            && e.LastSeenTimestamp != null
                            && e.FirstSeenTimestamp != null
                            && e.LastSeenTimestamp > e.FirstSeenTimestamp
                            && e.IsPositioningHeader == lookupForPositioningHeaderOnly)
                .OrderBy(f => f.LastSeenTimestamp)
                .ToList();
            if (postioningEpcsList.Any())
            {

                var expiration = int.Parse(_standaloneConfigDTO.positioningExpirationInSec);

                for (var i = 0; i < postioningEpcsList.Count; i++)
                {
                    var positioningEpc = new LastPositioningReferecenceEpc
                    {
                        AntennaPort = postioningEpcsList[i].AntennaPort,
                        Epc = postioningEpcsList[i].Epc,
                        AntennaZone = postioningEpcsList[i].AntennaZone,
                        LastPositionEpcAntTimestamp = postioningEpcsList[i].LastSeenTimestamp,
                        PeakRssi = postioningEpcsList[i].PeakRssi
                    };
                    positioningReferecenceEpcs.Add(positioningEpc);

                    try
                    {
                        var dateTimeOffsetCurrentEventTimestamp =
                            DateTimeOffset.FromUnixTimeMilliseconds(currentEventTimestamp);
                        var dateTimeOffsetLastSeenTimestamp =
                            DateTimeOffset.FromUnixTimeMilliseconds(postioningEpcsList[i].LastSeenTimestamp.Value);
                        var timeDiff = dateTimeOffsetCurrentEventTimestamp.Subtract(dateTimeOffsetLastSeenTimestamp);
                        if (timeDiff.TotalSeconds > expiration)
                        {
                            _ = dbContext.PostioningEpcs.Remove(postioningEpcsList[i]);
                            _ = await dbContext.SaveChangesAsync();
                        }
                    }
                    catch (Exception)
                    {
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("Unexpected error on GetLastPositioningDataAsync. " + ex.Message);
        }

        return positioningReferecenceEpcs;
#pragma warning restore CS8600, CS8601, CS8602, CS8603, CS8604, CS8625, CS8629 // Dereference of a possibly null reference.
    }

    public async Task UpdatePositioningDataAsync()
    {
#pragma warning disable CS8600, CS8601, CS8602, CS8603, CS8604, CS8625, CS8629 // Dereference of a possibly null reference.
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RuntimeDb>();
        var postioningEpcsList = dbContext.PostioningEpcs
            .Where(e => e.Epc != null && e.LastSeenTimestamp != null
                                      && e.FirstSeenTimestamp != null && e.LastSeenTimestamp > e.FirstSeenTimestamp)
            .OrderBy(f => f.FirstSeenTimestamp)
            .ToList();
        if (postioningEpcsList.Any())
        {
            var expiration = int.Parse(_standaloneConfigDTO.positioningExpirationInSec);
            for (var i = 0; i < postioningEpcsList.Count; i++)
                if (Utils.GetDateTime((ulong)postioningEpcsList[i].FirstSeenTimestamp).AddSeconds(expiration) >
                    Utils.GetDateTime((ulong)postioningEpcsList[i].LastSeenTimestamp))
                {
                    _ = dbContext.PostioningEpcs.Remove(postioningEpcsList[i]);
                    _ = await dbContext.SaveChangesAsync();
                }
        }
#pragma warning restore CS8600, CS8601, CS8602, CS8603, CS8604, CS8625, CS8629 // Dereference of a possibly null reference.
    }

    public JArray GetIotInterfaceTagEventReport(JObject smartReaderTagEventData)
    {
#pragma warning disable CS8600, CS8601, CS8602, CS8603, CS8604, CS8625, CS8629 // Dereference of a possibly null reference.
        JArray iotTagEventReportObject = [];
        try
        {
            List<object> iotTagEventReport = [];

            string hostname = "";
            string eventType = "tagInventory";


            if (smartReaderTagEventData.ContainsKey("readerName"))
            {
                if (smartReaderTagEventData["readerName"].HasValues)
                {
                    hostname = smartReaderTagEventData["readerName"].Value<String>();
                }

            }

            if (smartReaderTagEventData.ContainsKey("readerName"))
            {

                foreach (JObject currentEventItem in smartReaderTagEventData["tag_reads"])
                {
                    DateTimeOffset dateTimeOffsetLastSeenTimestamp = DateTimeOffset.Now;
                    if (smartReaderTagEventData.ContainsKey("firstSeenTimestamp"))
                    {
                        long firstSeenTimestamp = currentEventItem["firstSeenTimestamp"].Value<long>() / 1000;
                        dateTimeOffsetLastSeenTimestamp = DateTimeOffset.FromUnixTimeMilliseconds(firstSeenTimestamp);
                    }

                    string tagEpc = currentEventItem["epc"].Value<string>();
                    int antennaPort = 0;
                    if (currentEventItem.ContainsKey("antennaPort"))
                    {
                        antennaPort = currentEventItem["antennaPort"].Value<int>();
                    }

                    string antennaZone = "";
                    if (currentEventItem.ContainsKey("antennaZone"))
                    {
                        antennaZone = currentEventItem["antennaZone"].Value<string>();
                    }

                    int peakRssi = 0;
                    if (currentEventItem.ContainsKey("peakRssi"))
                    {
                        peakRssi = currentEventItem["peakRssi"].Value<int>();
                    }

                    double txPower = 0.00;
                    if (currentEventItem.ContainsKey("peakRssi"))
                    {
                        txPower = currentEventItem["txPower"].Value<double>();
                    }
                    Dictionary<object, object> iotEvent = new()
                    {
                        { "timestamp", dateTimeOffsetLastSeenTimestamp.ToString("o") },
                        { "hostname", hostname },
                        { "eventType", eventType }
                    };

                    Dictionary<object, object> iotEventItem = new()
                    {
                        { "epcHex", tagEpc }
                    };
                    if (!string.IsNullOrEmpty(antennaZone))
                    {
                        iotEventItem.Add("antennaName", antennaZone);
                    }

                    if (antennaPort > 0)
                    {
                        iotEventItem.Add("antennaPort", antennaPort);
                    }

                    if (peakRssi != 0)
                    {
                        peakRssi = peakRssi * 100;
                        iotEventItem.Add("peakRssiCdbm", peakRssi);
                    }

                    if (txPower != 0)
                    {
                        txPower = txPower * 100;
                        iotEventItem.Add("transmitPowerCdbm", txPower);
                    }

                    iotEvent.Add("tagInventoryEvent", iotEventItem);
                    iotTagEventReport.Add(iotEvent);
                }

            }
            iotTagEventReportObject = JArray.FromObject(iotTagEventReport);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error on GetIotInterfaceTagEventReport. ");
        }

        return iotTagEventReportObject;
#pragma warning restore CS8600, CS8601, CS8602, CS8603, CS8604, CS8625, CS8629 // Dereference of a possibly null reference.
    }

    public bool IsOnPause()
    {
        var pauseRequest = Path.Combine("/customer", "pause-request.txt");
        return File.Exists(pauseRequest);
    }

    private void RemoveOldestEntries<TKey, TValue>(ConcurrentDictionary<TKey, TValue> dictionary, double removeCount)
    {
        var orderedEntries = dictionary.OrderBy(x =>
            (x.Value as ReadCountTimeoutEvent)?.EventTimestamp ?? 0).Take((int)removeCount);

        foreach (var entry in orderedEntries)
        {
            _ = dictionary.TryRemove(entry.Key, out _);
        }
    }

    private void RemoveOldestBatchEntries(ConcurrentDictionary<string, JObject> dictionary, double removeCount)
    {
        var orderedEntries = dictionary.OrderBy(x =>
        {
            try
            {
                if (x.Value["firstSeenTimestamp"] != null)
                {
                    return x.Value["firstSeenTimestamp"].Value<long>();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error accessing timestamp for batch entry");
            }
            return long.MaxValue;
        }).Take((int)removeCount);

        foreach (var entry in orderedEntries)
        {
            _ = dictionary.TryRemove(entry.Key, out _);
        }
    }
}


