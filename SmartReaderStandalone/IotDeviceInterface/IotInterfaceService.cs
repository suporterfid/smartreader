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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO.Ports;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Reflection;
using System.Runtime;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Timers;
using Impinj.Atlas;
using Impinj.Utils.DebugLogger;
using McMaster.NETCore.Plugins;
using MQTTnet;
using MQTTnet.Client.Options;
using MQTTnet.Extensions.ManagedClient;
using MQTTnet.Protocol;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;
using Serilog.Events;
using SimpleTcp;
using SmartReader.Infrastructure.Database;
using SmartReader.Infrastructure.ViewModel;
using SmartReaderJobs.Utils;
using SmartReaderJobs.ViewModel.Events;
using SmartReaderStandalone.Entities;
using SmartReaderStandalone.Utils;
using SmartReaderStandalone.ViewModel.Filter;
using SmartReaderStandalone.ViewModel.Gpo;
using SmartReaderStandalone.ViewModel.Read;
using SmartReaderStandalone.ViewModel.Read.Sku.Summary;
using SmartReaderStandalone.ViewModel.Status;
using TagDataTranslation;
using DataReceivedEventArgs = SimpleTcp.DataReceivedEventArgs;
using ReaderStatus = SmartReaderStandalone.Entities.ReaderStatus;
using Timer = System.Timers.Timer;
using SmartReaderStandalone.Plugins;
using SmartReaderStandalone.Services;
using System;
using Renci.SshNet;
using System.Net.NetworkInformation;

namespace SmartReader.IotDeviceInterface;

public class IotInterfaceService : BackgroundService, IServiceProviderIsService
{
    private static string _readerAddress;

    private static readonly string _bearerToken;

    private static readonly object _timerTagPublisherHttpLock = new();

    private static readonly object _timerTagPublisherOpcUaLock = new();

    private static int _positioningExpirationInSec = 3;

    private static IR700IotReader _iotDeviceInterfaceClient;

    private static string _readerUsername = "root";
    private static string _readerPassword = "impinj";

    private static string _proxyAddress = "";

    private static int _proxyPort = 8080;


    private static bool _isStarted;

    private static StandaloneConfigDTO _standaloneConfigDTO;

    private static readonly SystemInfo _readerSystemInfo;

    private static SimpleTcpServer _socketServer;

    private static SimpleTcpServer _socketCommandServer;

    private static SimpleTcpClient _socketBarcodeClient;

    //private static UdpEndpoint _udpServer;

    private static UDPSocket _udpSocketServer;

    //private static UdpClientUtil _udpClientUtil;

    private static IManagedMqttClient _mqttClient;

    //private static IMqttClient _mqttClient;

    private static ManagedMqttClientOptions _mqttClientOptions;

    //private static IMqttClientOptions _mqttClientOptions;

    //private static IManagedMqttClient _mqttCommandClient;

    private static ManagedMqttClientOptions _mqttCommandClientOptions;

    private static string _expectedLicense;

    private static TagRead _lastTagRead;

    private static readonly TDTEngine _tdtEngine = new();

    private static readonly ConcurrentDictionary<string, DateTimeOffset> _knowTagsForSoftwareFilterWindowSec = new();

    public static string LastValidBarcode = "";

    public static string R700UsbDrive = @"/run/mount/external/";

    public static string R700UsbDrivePath = @"/run/mount/external/";

    private readonly IConfiguration _configuration;

    public readonly ConcurrentDictionary<string, int> _currentSkus = new();

    private readonly List<string> _currentSkuReadEpcs = new();

    private readonly CancellationTokenSource _gpiCts;

    public static readonly ConcurrentDictionary<int, bool> _gpiPortStates = new();

    private static bool? previousGpi1 = null;

    private static bool gpi1HasChanged = false;

    private static bool? previousGpi2 = null;

    private static bool gpi2HasChanged = false;

    private readonly PeriodicTimer _gpiTimer;

    private readonly Task _gpiTimerTask;

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

    private readonly ConcurrentQueue<JObject> _messageQueueTagSmartReaderTagEventUdpServer = new();

    private readonly ConcurrentQueue<JObject> _messageQueueTagSmartReaderTagEventUsbDrive = new();

    private static List<string> _oldReadEpcList = new();

    private readonly List<int> _positioningAntennaPorts = new();

    private readonly List<string> _positioningEpcsHeaderList = new();

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

    private readonly Timer _timerSummaryStreamPublisher;

    private readonly Timer _timerTagPublisherUdpServer;

    private readonly Timer _timerTagPublisherUsbDrive;

    private readonly CancellationTokenSource CancellationTokenSource = new();

    private readonly SemaphoreSlim periodicJobTaskLock = new(1, 1);
    private readonly SemaphoreSlim readLock = new(1, 1);

    private Stopwatch _httpTimerStopwatch;

    //private static string? currentBarcode;
    //private static SmartReaderSetupData _mqttClientSmartReaderSetupDataSmartReaderSetupData;

    private ConcurrentDictionary<string, long> _readEpcs = new();

    private SerialPort _serialTty;

    protected object _timerPeriodicTasksJobManagerLock = new();

    //private readonly RuntimeDb _db;

    private string DeviceId;

    private string DeviceIdMqtt;

    private string DeviceIdWithDashes;

    protected Timer PeriodicTasksTimerInventoryData = new();

    private string ExternalApiToken;

    public static Dictionary<string, IPlugin> _plugins = new();


    public IotInterfaceService(IServiceProvider services, IConfiguration configuration,
        ILogger<IotInterfaceService> logger, IHttpClientFactory httpClientFactory)
    {
        _configuration = configuration;
        _logger = logger;
        Services = services;
        HttpClientFactory = httpClientFactory;

        var filePath = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);

        var serverUrl = _configuration.GetValue<string>("ServerInfo:Url");
        var serverToken = _configuration.GetValue<string>("ServerInfo:AuthToken");

        _readerAddress = _configuration.GetValue<string>("ReaderInfo:Address") ?? "127.0.0.1";
        string buildConfiguration = "Release"; // Default to Debug if unable to determine
#if DEBUG
        buildConfiguration = "Debug";
#endif
        // Get the value based on the build configuration
        if ("Debug".Equals(buildConfiguration))
        {
            _readerAddress = _configuration.GetValue<string>("ReaderInfo:DebugAddress") ?? _readerAddress;
        }
        _readerUsername = _configuration.GetValue<string>("RShell:UserName") ?? "root";
        _readerPassword = _configuration.GetValue<string>("RShell:Password") ?? "impinj";

        _httpUtil = new HttpUtil(HttpClientFactory, serverUrl, serverToken);

        _timerTagFilterLists = new Timer(100);

        _timerKeepalive = new Timer(100);

        _timerStopTriggerDuration = new Timer(100);

        _timerTagPublisherHttp = new Timer(100);

        _timerTagPublisherSocket = new Timer(100);

        _timerSummaryStreamPublisher = new Timer(100);

        _timerTagPublisherUsbDrive = new Timer(100);

        _timerTagPublisherUdpServer = new Timer(100);

        _timerTagPublisherRetry = new Timer(100);

        _timerTagPublisherMqtt = new Timer(100);

        _timerTagPublisherOpcUa = new Timer(100);

        try
        {
            _gpiCts = new CancellationTokenSource();
            _gpiTimer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
            _gpiTimerTask = HandleGpiTimerAsync(_gpiTimer, _gpiCts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HandleGpiTimerAsync init - " + ex.Message);

        }
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

    private async Task HandleGpiTimerAsync(PeriodicTimer timer, CancellationToken cancel = default)
    {
        try
        {
            if (_standaloneConfigDTO == null)
            {
                _standaloneConfigDTO = ConfigFileHelper.ReadFile();
            }
            if (string.Equals("1", _standaloneConfigDTO.includeGpiEvent,
                                             StringComparison.OrdinalIgnoreCase))
            {
                while (await timer.WaitForNextTickAsync(cancel)) await Task.Run(() => QueryGpiStatus(cancel), cancel);
            }
            else
            {
                await Task.Delay(100);
            }

        }
        catch (Exception ex)
        {
            Console.WriteLine("QueryGpiStatus - " + ex.Message);
        }
    }

    private async Task QueryGpiStatus(CancellationToken cancel = default)
    {
        try
        {


            var gpi1File = @"/dev/gpio/ext-gpi-0/value";
            var gpi2File = @"/dev/gpio/ext-gpi-1/value";
            var fileContent1 = "";
            var fileContent2 = "";
            if (File.Exists(gpi1File))
                fileContent1 = File.ReadAllText(gpi1File);
            if (File.Exists(gpi2File))
                fileContent2 = File.ReadAllText(gpi2File);

            if (!_gpiPortStates.ContainsKey(0)) _gpiPortStates.TryAdd(0, false);
            if (!_gpiPortStates.ContainsKey(1)) _gpiPortStates.TryAdd(1, false);

            if (!string.IsNullOrEmpty(fileContent1) && fileContent1.Contains("1"))
            {
                _gpiPortStates[0] = true;
                if (previousGpi1.HasValue)
                {
                    if (_gpiPortStates[0] != previousGpi1.Value)
                    {
                        gpi1HasChanged = true;
                    }
                    else
                    {
                        gpi1HasChanged = false;
                    }
                }
                else
                {
                    gpi1HasChanged = true;
                }

                previousGpi1 = _gpiPortStates[0];
            }
            else
            {
                _gpiPortStates[0] = false;
                if (previousGpi1.HasValue)
                {
                    if (_gpiPortStates[0] != previousGpi1.Value)
                    {
                        gpi1HasChanged = true;
                    }
                    else
                    {
                        gpi1HasChanged = false;
                    }
                }
                else
                {
                    gpi1HasChanged = true;
                }
                previousGpi1 = _gpiPortStates[0];
            }

            if (!string.IsNullOrEmpty(fileContent2) && fileContent2.Contains("1"))
            {
                _gpiPortStates[1] = true;
                if (previousGpi2.HasValue)
                {
                    if (_gpiPortStates[1] != previousGpi2.Value)
                    {
                        gpi2HasChanged = true;
                    }
                    else
                    {
                        gpi2HasChanged = false;
                    }
                }
                else
                {
                    gpi2HasChanged = true;
                }

                previousGpi2 = _gpiPortStates[1];
            }
            else
            {
                _gpiPortStates[1] = false;
                if (previousGpi2.HasValue)
                {
                    if (_gpiPortStates[1] != previousGpi2.Value)
                    {
                        gpi2HasChanged = true;
                    }
                    else
                    {
                        gpi2HasChanged = false;
                    }
                }
                else
                {
                    gpi2HasChanged = true;
                }
                previousGpi2 = _gpiPortStates[1];
            }



            //if (File.Exists(gpi1File))
            //{
            //    if (!_gpiPortStates.ContainsKey(0)) _gpiPortStates.TryAdd(0, false);
            //    // read file content into a string
            //    var fileContent = File.ReadAllText(gpi1File);

            //    // compare TextBox content with file content
            //    if (fileContent.Contains("1"))
            //    {
            //        _gpiPortStates[0] = true;
            //        if (previousGpi1.HasValue)
            //        {
            //            if (_gpiPortStates[0] != previousGpi1.Value)
            //            {
            //                gpi1HasChanged = true;
            //            }
            //            else
            //            {
            //                gpi1HasChanged = false;
            //            }
            //        }
            //        else
            //        {
            //            gpi1HasChanged = true;
            //        }

            //        previousGpi1 = _gpiPortStates[0];
            //    }
            //    else
            //    {
            //        _gpiPortStates[0] = false;
            //        if (previousGpi1.HasValue)
            //        {
            //            if (_gpiPortStates[0] != previousGpi1.Value)
            //            {
            //                gpi1HasChanged = true;
            //            }
            //            else
            //            {
            //                gpi1HasChanged = false;
            //            }
            //        }
            //        else
            //        {
            //            gpi1HasChanged = true;
            //        }

            //        //gpi1HasChanged = true;                    

            //        previousGpi1 = _gpiPortStates[0];
            //    }

            //}

            //if (File.Exists(gpi2File))
            //{
            //    if (!_gpiPortStates.ContainsKey(1)) _gpiPortStates.TryAdd(1, false);
            //    // read file content into a string
            //    var fileContent = File.ReadAllText(gpi2File);

            //    // compare TextBox content with file content
            //    //if (fileContent.Contains("1"))
            //    //    _gpiPortStates[1] = true;
            //    //else
            //    //    _gpiPortStates[1] = false;

            //    if (fileContent.Contains("1"))
            //    {
            //        _gpiPortStates[1] = true;
            //        if (previousGpi2.HasValue)
            //        {
            //            if (_gpiPortStates[1] != previousGpi2.Value)
            //            {
            //                gpi2HasChanged = true;
            //            }
            //            else
            //            {
            //                gpi2HasChanged = false;
            //            }
            //        }
            //        else
            //        {
            //            gpi2HasChanged = true;
            //        }

            //        previousGpi2 = _gpiPortStates[0];
            //    }
            //    else
            //    {
            //        _gpiPortStates[1] = false;
            //        if (previousGpi2.HasValue)
            //        {
            //            if (_gpiPortStates[1] != previousGpi2.Value)
            //            {
            //                gpi2HasChanged = true;
            //            }
            //            else
            //            {
            //                gpi2HasChanged = false;
            //            }
            //        }
            //        else
            //        {
            //            gpi2HasChanged = true;
            //        }
            //        //gpi1HasChanged = true;

            //        previousGpi2 = _gpiPortStates[1];
            //    }
            //}

            try
            {
                if (gpi1HasChanged || gpi2HasChanged)
                {
                    //fileContent1
                    _logger.LogInformation($"GPI status: gpi1: {fileContent1}, gpi2: {fileContent2}");
                    //_logger.LogInformation($"GPI status: gpi1HasChanged: {gpi1HasChanged}, gpi2HasChanged: {gpi2HasChanged}");
                    ProcessGpiStatus();
                }

            }
            catch (Exception)
            {


            }

        }
        catch (Exception ex)
        {
            Console.WriteLine("QueryGpiStatus - " + ex.Message);
        }
    }

    private void StartUdpServer()
    {
        if (_standaloneConfigDTO != null && !string.IsNullOrEmpty(_standaloneConfigDTO.udpServer)
                                         && string.Equals("1", _standaloneConfigDTO.udpServer,
                                             StringComparison.OrdinalIgnoreCase))
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
        if (_standaloneConfigDTO != null && !string.IsNullOrEmpty(_standaloneConfigDTO.udpServer)
                                         && string.Equals("1", _standaloneConfigDTO.udpServer,
                                             StringComparison.OrdinalIgnoreCase))
            try
            {
                if (_udpSocketServer != null) _udpSocketServer.Close();
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

                                File.CreateSymbolicLink(webDir, R700UsbDrive);
                                _logger.LogInformation("Symbolic link created: " + webDir);
                                R700UsbDrivePath = webDir;

                                _ = ProcessGpoErrorPortRecoveryAsync();
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
        if (_standaloneConfigDTO != null && !string.IsNullOrEmpty(_standaloneConfigDTO.enableBarcodeTcp)
                                         && string.Equals("1", _standaloneConfigDTO.enableBarcodeTcp,
                                             StringComparison.OrdinalIgnoreCase))
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

                    _ = ProcessGpoErrorPortRecoveryAsync();
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

                _ = ProcessGpoErrorPortRecoveryAsync();
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
            var byteData = e.Data;
            var messageData = Encoding.UTF8.GetString(byteData, 0, byteData.Length);
            messageData = messageData.Replace("\n", "").Replace("\r", "");
            messageData = messageData.Trim();
            var receivedBarcode = ReceiveBarcode(messageData);
            if ("".Equals(receivedBarcode))
                //Console.WriteLine("Events_DataReceived - empty data received [" + messageData + "] ");
                _logger.LogInformation("Events_DataReceived - empty data received [" + messageData + "]   ");
                _ = ProcessGpoErrorPortRecoveryAsync();
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
                if (string.Equals("4", _standaloneConfigDTO.startTriggerType, StringComparison.OrdinalIgnoreCase))
                    _ = StartPresetAsync();
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
                                    Dictionary<string, SkuSummary> skuSummaryList = new Dictionary<string, SkuSummary>();
                                    var itemsFromExternalApi = _plugins[_standaloneConfigDTO.activePlugin].ExternalApiSearch(LastValidBarcode, skuSummaryList, epcArray);
                                    if (itemsFromExternalApi.Any())
                                    {
                                        foreach (KeyValuePair<string, int> entry in itemsFromExternalApi)
                                        {
                                            _expectedItems.TryAdd(entry.Key, entry.Value);
                                        }
                                    }
                                    return messageData;
                                }
                                else if (!string.IsNullOrEmpty(_standaloneConfigDTO.externalApiVerificationAuthLoginUrl))
                                {
                                    try
                                    {
                                        _plugins[_standaloneConfigDTO.activePlugin].ExternalApiLogin();
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

    private void StartTcpSocketServer()
    {
        if (_standaloneConfigDTO != null && !string.IsNullOrEmpty(_standaloneConfigDTO.socketServer)
                                         && string.Equals("1", _standaloneConfigDTO.socketServer,
                                             StringComparison.OrdinalIgnoreCase))
            try
            {
                if (_socketServer == null)
                {
                    // _logger.LogInformation("Creating tcp socket server. " + _standaloneConfigDTO.socketPort, SeverityType.Debug);
                    //_logger.LogInformation("Creating tcp socket server. [" + _standaloneConfigDTO.socketPort + "] ");
                    _logger.LogInformation("Creating tcp socket server. [" + _standaloneConfigDTO.socketPort + "] ");
                    _socketServer = new SimpleTcpServer("0.0.0.0:" + _standaloneConfigDTO.socketPort);
                    // set events
                    _socketServer.Events.ClientConnected += ClientConnected;
                    _socketServer.Events.ClientDisconnected += ClientDisconnected;
                    _socketServer.Events.DataReceived += DataReceived;
                }

                if (_socketServer != null && !_socketServer.IsListening)
                {
                    //_logger.LogInformation("Starting tcp socket server. " + _standaloneConfigDTO.socketPort, SeverityType.Debug);
                    _logger.LogInformation("Starting tcp socket server. [" + _standaloneConfigDTO.socketPort + "] ");
                    _socketServer.Start();
                    _ = ProcessGpoErrorPortRecoveryAsync();
                }
            }
            catch (Exception ex)
            {
                //_logger.LogInformation("Error starting tcp socket server. " + ex.Message, SeverityType.Error);
                _logger.LogError(ex, "Error starting tcp socket server. [" + _standaloneConfigDTO.socketPort + "] ");
                _ = ProcessGpoErrorPortAsync();
            }
    }

    private void StopTcpSocketServer()
    {
        try
        {
            if (_socketServer != null)
            {
                _socketServer.Events.ClientConnected -= ClientConnected;
                _socketServer.Events.ClientDisconnected -= ClientDisconnected;
                _socketServer.Events.DataReceived -= DataReceived;
                _socketServer.Stop();
            }
        }
        catch (Exception ex)
        {
            //_logger.LogInformation("Error stoping tcp socket server. " + ex.Message, SeverityType.Error);
            _logger.LogError(ex, "Error stoping tcp socket server. [" + _standaloneConfigDTO.socketPort + "] ");
        }
    }

    private void StartTcpSocketCommandServer()
    {
        if (_standaloneConfigDTO != null && !string.IsNullOrEmpty(_standaloneConfigDTO.socketCommandServer)
                                         && string.Equals("1", _standaloneConfigDTO.socketCommandServer,
                                             StringComparison.OrdinalIgnoreCase))
            try
            {
                if (_socketServer == null)
                {
                    _logger.LogInformation(
                        "Creating tcp socket cmd server. " + _standaloneConfigDTO.socketCommandServerPort,
                        SeverityType.Debug);
                    _socketCommandServer =
                        new SimpleTcpServer("0.0.0.0:" + _standaloneConfigDTO.socketCommandServerPort);
                    // set events
                    _socketCommandServer.Events.ClientConnected += ClientConnectedSocketCommand;
                    _socketCommandServer.Events.ClientDisconnected += ClientDisconnectedSocketCommand;
                    _socketCommandServer.Events.DataReceived += DataReceivedSocketCommand;
                }

                if (_socketServer != null && !_socketServer.IsListening)
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
                _socketCommandServer.Events.ClientConnected -= ClientConnectedSocketCommand;
                _socketCommandServer.Events.ClientDisconnected -= ClientDisconnectedSocketCommand;
                _socketCommandServer.Events.DataReceived -= DataReceivedSocketCommand;
                _socketCommandServer.Stop();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stoping tcp socket server. " + ex.Message);
        }
    }

    private static void LoadConfig()
    {
        try
        {
            //var configHelper = new IniConfigHelper();
            //_standaloneConfigDTO = configHelper.LoadDtoFromFile();
            var fileName = @"/customer/config/smartreader.json";
            if (File.Exists(fileName))
            {
                var length = new FileInfo(fileName).Length;
                if (length > 0)
                {
                    _standaloneConfigDTO = ConfigFileHelper.ReadFile();
                    Console.WriteLine("Config loaded. " + _standaloneConfigDTO.antennaPorts);
                }
            }
        }
        catch (Exception)
        {
        }
    }

    private void MqttLog(string threshold, string message)
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
                    _mqttClient.PublishAsync("smartreader/log/" + DeviceIdMqtt, payload,
                        MqttQualityOfServiceLevel.AtLeastOnce));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MqttLog: Unexpected error. " + ex.Message);
        }
    }

    private void ClientConnected(object sender, ConnectionEventArgs e)
    {
        _logger.LogInformation("[" + e.IpPort + "] client connected", SeverityType.Debug);
    }

    private void ClientDisconnected(object sender, ConnectionEventArgs e)
    {
        _logger.LogInformation("[" + e.IpPort + "] client disconnected: " + e.Reason, SeverityType.Debug);
    }

    private void DataReceived(object sender, DataReceivedEventArgs e)
    {
        _logger.LogInformation("[" + e.IpPort + "]: " + Encoding.UTF8.GetString(e.Data), SeverityType.Debug);
    }

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
                                    SaveConfigDtoToDb(_standaloneConfigDTO);
                                    shouldRestart = true;
                                }
                                else if (parsedItemParts[0].StartsWith("antennaStates"))
                                {
                                    _standaloneConfigDTO.antennaStates = parsedItemParts[1];
                                    SaveConfigDtoToDb(_standaloneConfigDTO);
                                    shouldRestart = true;
                                }
                                else if (parsedItemParts[0].StartsWith("transmitPower"))
                                {
                                    _standaloneConfigDTO.transmitPower = parsedItemParts[1];
                                    SaveConfigDtoToDb(_standaloneConfigDTO);
                                    shouldRestart = true;
                                }
                                else if (parsedItemParts[0].StartsWith("receiveSensitivity"))
                                {
                                    _standaloneConfigDTO.receiveSensitivity = parsedItemParts[1];
                                    SaveConfigDtoToDb(_standaloneConfigDTO);
                                    shouldRestart = true;
                                }
                                else if (parsedItemParts[0].StartsWith("readerMode"))
                                {
                                    _standaloneConfigDTO.readerMode = parsedItemParts[1];
                                    SaveConfigDtoToDb(_standaloneConfigDTO);
                                    shouldRestart = true;
                                }
                                else if (parsedItemParts[0].StartsWith("searchMode"))
                                {
                                    _standaloneConfigDTO.searchMode = parsedItemParts[1];
                                    SaveConfigDtoToDb(_standaloneConfigDTO);
                                    shouldRestart = true;
                                }
                                else if (parsedItemParts[0].StartsWith("session"))
                                {
                                    _standaloneConfigDTO.session = parsedItemParts[1];
                                    SaveConfigDtoToDb(_standaloneConfigDTO);
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
            _logger.LogError(ex, "MqttLog: Unexpected error. " + ex.Message);
        }
    }

    //void EndpointDetected(object sender, EndpointMetadata md)
    //{
    //    _logger.LogInformation("UDP Endpoint detected: " + md.Ip + ":" + md.Port);
    //    try
    //    {
    //        if(!_udpClients.ContainsKey(md.Ip))
    //        {
    //            _udpClients.TryAdd(md.Ip, md.Port);
    //        }

    //    }
    //    catch (Exception)
    //    {

    //    }
    //}

    //void DatagramReceived(object sender, Datagram dg)
    //{
    //    _logger.LogInformation("Datagram Received [" + dg.Ip + ":" + dg.Port + "]: " + Encoding.UTF8.GetString(dg.Data));
    //}

    public async Task<List<SmartreaderSerialNumberDto>> GetSerialAsync()
    {
        var returnData = new List<SmartreaderSerialNumberDto>();
        try
        {
            if (_iotDeviceInterfaceClient == null)
                _iotDeviceInterfaceClient =
                    new R700IotReader(_readerAddress, "", true, true, _readerUsername, _readerPassword, 0, _proxyAddress, _proxyPort);

            var info = await _iotDeviceInterfaceClient.GetSystemInfoAsync();


            var serialData = new SmartreaderSerialNumberDto();
            serialData.SerialNumber = info.SerialNumber;
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

    public async Task<List<SmartreaderRunningStatusDto>> GetReaderStatusAsync()
    {
        var returnData = new List<SmartreaderRunningStatusDto>();
        if (_iotDeviceInterfaceClient == null)
            _iotDeviceInterfaceClient =
                new R700IotReader(_readerAddress, "", true, true, _readerUsername, _readerPassword, 0, _proxyAddress, _proxyPort);

        var readerStatus = await _iotDeviceInterfaceClient.GetStatusAsync();
        if(!_iotDeviceInterfaceClient.IsNetworkConnected)
        {
            Task.Run(() => ProcessGpoErrorPortAsync());
            Task.Run(() => ProcessGpoErrorNetworkPortAsync(true));
        }
        else
        {
            Task.Run(() => ProcessGpoErrorPortRecoveryAsync());
            Task.Run(() => ProcessGpoErrorNetworkPortAsync(false));
        }
        var readerStatusData = new SmartreaderRunningStatusDto();


        if ("IDLE".Equals(readerStatus.Status.Value.ToString().ToUpper()))
            readerStatusData.Status = "STOPPED";
        else
            readerStatusData.Status = "STARTED";

        returnData.Add(readerStatusData);

        return returnData;
    }

    public async Task StartPresetAsync()
    {
        if (_iotDeviceInterfaceClient == null)
            _iotDeviceInterfaceClient =
                new R700IotReader(_readerAddress, "", true, true, _readerUsername, _readerPassword, 0, _proxyAddress, _proxyPort);

        try
        {
            _oldReadEpcList.Clear();
        }
        catch (Exception)
        {
        }

        try
        {
            _iotDeviceInterfaceClient.TagInventoryEvent -= OnTagInventoryEvent;
            _iotDeviceInterfaceClient.GpiTransitionEvent -= OnGpiTransitionEvent;
            await _iotDeviceInterfaceClient.StopAsync();
        }
        catch (Exception)
        {
        }

        try
        {
            _iotDeviceInterfaceClient.TagInventoryEvent += OnTagInventoryEvent;
            _iotDeviceInterfaceClient.GpiTransitionEvent += OnGpiTransitionEvent;
        }
        catch (Exception)
        {
        }

        _isStarted = true;

        try
        {
            await ApplySettingsAsync();
            _ = ProcessGpoErrorPortRecoveryAsync();
        }
        catch (Exception ex)
        {
            _logger.LogInformation("Error applying settings. " + ex.Message);
            await ProcessGpoErrorPortAsync();
        }

        //try
        //{
        //    await _iotDeviceInterfaceClient.StartPresetAsync("SmartReader");
        //}
        //catch (Exception)
        //{
        //}

        try
        {
            await _iotDeviceInterfaceClient.StartAsync("SmartReader");
        }
        catch (Exception)
        {
            if (_standaloneConfigDTO.isEnabled == "1" && _isStarted) SaveStartCommandToDb();
        }

        try
        {
            if (_standaloneConfigDTO != null
                && string.Equals("1", _standaloneConfigDTO.enableSummaryStream, StringComparison.OrdinalIgnoreCase))
            {
                using var scope = Services.CreateScope();
                var summaryQueueBackgroundService = scope.ServiceProvider.GetRequiredService<ISummaryQueueBackgroundService>();
                summaryQueueBackgroundService.StartQueue();
                _ = ProcessGpoErrorPortRecoveryAsync();
            }
        }
        catch (Exception)
        {
            await ProcessGpoErrorPortAsync();
        }
    }

    public async Task StopPresetAsync()
    {
        if (_iotDeviceInterfaceClient == null)
            _iotDeviceInterfaceClient =
                new R700IotReader(_readerAddress, "", true, true, _readerUsername, _readerPassword, 0, _proxyAddress, _proxyPort);
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

    public async Task StartTasksAsync()
    {

        try
        {
            _oldReadEpcList.Clear();
        }
        catch (Exception)
        {
        }

        if (_iotDeviceInterfaceClient == null)
            _iotDeviceInterfaceClient =
                new R700IotReader(_readerAddress, "", true, true, _readerUsername, _readerPassword, 0, _proxyAddress, _proxyPort);
        if (_isStarted)
            try
            {
                _iotDeviceInterfaceClient.TagInventoryEvent -= OnTagInventoryEvent;
                _iotDeviceInterfaceClient.GpiTransitionEvent -= OnGpiTransitionEvent;
                await _iotDeviceInterfaceClient.StopAsync();
            }
            catch (Exception)
            {
            }


        //try
        //{
        //    for (int i = 0; i < _gpiPortStates.Keys.Count; i++)
        //    {
        //        try
        //        {
        //            _gpiPortStates[i] = false;
        //        }
        //        catch (Exception)
        //        {

        //        }
        //    }
        //}
        //catch (Exception)
        //{


        //}


        try
        {
            if (_standaloneConfigDTO != null && !string.IsNullOrEmpty(_standaloneConfigDTO.isEnabled))
                try
                {
                    _standaloneConfigDTO.isEnabled = "1";
                    SaveConfigDtoToDb(_standaloneConfigDTO);
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

        _isStarted = true;

        try
        {
            await ApplySettingsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogInformation(ex, "Error applying settings. " + ex.Message);
        }

        _iotDeviceInterfaceClient.TagInventoryEvent += OnTagInventoryEvent;
        _iotDeviceInterfaceClient.InventoryStatusEvent += OnInventoryStatusEvent;
        _iotDeviceInterfaceClient.GpiTransitionEvent += OnGpiTransitionEvent;
        try
        {
            await _iotDeviceInterfaceClient.StartAsync("SmartReader");
            //await _iotDeviceInterfaceClient.StartPresetAsync("SmartReader");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting SmartReader preset, scheduling start command to background worker. " + ex.Message);
            if (_standaloneConfigDTO.isEnabled == "1" && _isStarted) SaveStartCommandToDb();
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
            StartTcpSocketServer();
        }
        catch (Exception ex)
        {

            _logger.LogError(ex, "Error starting tcp socket server. " + ex.Message);
        }

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
            var mqttManagementEvents = new Dictionary<string, object>();
            mqttManagementEvents.Add("smartreader-status", "started");
            PublishMqttManagementEvent(mqttManagementEvents);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing mqtt app status. " + ex.Message);

        }


    }

    public async Task StopTasksAsync()
    {
        _isStarted = false;
        if (_iotDeviceInterfaceClient == null)
            _iotDeviceInterfaceClient =
                new R700IotReader(_readerAddress, "", true, true, _readerUsername, _readerPassword, 0, _proxyAddress, _proxyPort);

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
            _logger.LogError(ex, "Error cleaning-up previous preset. " + ex.Message);
        }

        //try
        //{
        //    if (_standaloneConfigDTO != null && !string.IsNullOrEmpty(_standaloneConfigDTO.isEnabled))
        //    {
        //        _standaloneConfigDTO.isEnabled = "0";
        //        SaveConfigDtoToDb(_standaloneConfigDTO);
        //        //var configHelper = new IniConfigHelper();
        //        //configHelper.SaveDtoToFile(_standaloneConfigDTO);

        //    }
        //}
        //catch (Exception ex)
        //{
        //    _logger.LogInformation("Error saving state to config file. " + ex.Message, SeverityType.Error);

        //}

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
        StopTcpBarcodeClient();
        StopTcpSocketServer();
        StopUdpServer();
        StopTcpSocketCommandServer();

        var mqttManagementEvents = new Dictionary<string, object>();
        mqttManagementEvents.Add("smartreader-status", "stopped");
        PublishMqttManagementEvent(mqttManagementEvents);
    }

    public async Task ApplySettingsAsync()
    {
        if (_iotDeviceInterfaceClient == null)
            _iotDeviceInterfaceClient =
                new R700IotReader(_readerAddress, "", true, true, _readerUsername, _readerPassword, 0, _proxyAddress, _proxyPort);
        try
        {
            //var configHelper = new IniConfigHelper();
            //_standaloneConfigDTO = configHelper.LoadDtoFromFile();
            if (_standaloneConfigDTO == null) _standaloneConfigDTO = ConfigFileHelper.ReadFile();


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

            var mqttManagementEvents = new Dictionary<string, object>();
            mqttManagementEvents.Add("smartreader-status", "settings-applied");
            PublishMqttManagementEvent(mqttManagementEvents);

            await _iotDeviceInterfaceClient.GetReaderInventoryPresetListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error applying settings (ApplySettingsAsync).");
        }
    }
    public async Task ProcessGpoBlinkAnyTagGpoPortAsync()
    {
        try
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
        }
        catch (Exception)
        {

        }
    }
    public async Task ProcessGpoBlinkNewTagStatusGpoPortAsync(int timeInSec)
    {
        try
        {
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
        }
        catch (Exception)
        {
        }
    }

    public async Task SetGpoPortValidationAsync(bool ValidationState)
    {
        try
        {
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
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving state to config file. " + ex.Message);
        }
    }

    public async Task SetGpoPortAsync(int port, bool state)
    {
        _isStarted = false;
        if (_iotDeviceInterfaceClient == null)
            _iotDeviceInterfaceClient =
                new R700IotReader(_readerAddress, "", true, true, _readerUsername, _readerPassword, 0, _proxyAddress, _proxyPort);


        try
        {
            var gpoConfigurations = new GpoConfigurations();
            var gpoConfigList = new ObservableCollection<GpoConfiguration>();
            gpoConfigurations.GpoConfigurations1 = gpoConfigList;
            var gpoConfiguration = new GpoConfiguration();
            gpoConfiguration.Gpo = port;
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
        _isStarted = false;
        if (_iotDeviceInterfaceClient == null)
            _iotDeviceInterfaceClient =
                new R700IotReader(_readerAddress, "", true, true, _readerUsername, _readerPassword, 0, _proxyAddress, _proxyPort);


        try
        {
            var gpoConfigurations = new GpoConfigurations();
            var gpoConfigList = new ObservableCollection<GpoConfiguration>();
            gpoConfigurations.GpoConfigurations1 = gpoConfigList;

            foreach (var gpoVmConfig in gpos.GpoConfigurations)
            {
                var gpoConfiguration = new GpoConfiguration();
                gpoConfiguration.Gpo = gpoVmConfig.Gpo;
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

    public async Task ProcessGpoAnyTagPortAsync(bool setPortOn)
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

    public async Task ProcessGpoErrorPortAsync()
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

    public async Task ProcessGpoErrorNetworkPortAsync(bool status)
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

    public async Task ProcessGpoErrorPortRecoveryAsync()
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

    public async Task<ObservableCollection<string>> GetPresetListAsync()
    {
        if (_iotDeviceInterfaceClient == null)
            _iotDeviceInterfaceClient =
                new R700IotReader(_readerAddress, "", true, true, _readerUsername, _readerPassword, 0, _proxyAddress, _proxyPort);

        var presets = await _iotDeviceInterfaceClient.GetReaderInventoryPresetListAsync();
        return presets;
    }

    private async void ProcessGpiStatus()
    {

        if (string.Equals("1", _standaloneConfigDTO.mqttEnabled, StringComparison.OrdinalIgnoreCase))
        {
            Dictionary<string, object> gpiStatusEvent = new Dictionary<string, object>();
            Dictionary<object, object> gpiConfigurations1 = new Dictionary<object, object>();
            Dictionary<object, object> gpiConfigurations2 = new Dictionary<object, object>();

            if (_iotDeviceInterfaceClient == null)
                _iotDeviceInterfaceClient =
                    new R700IotReader(_readerAddress, "", true, true, _readerUsername, _readerPassword, 0, _proxyAddress, _proxyPort);

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


                //_messageQueueTagSmartReaderTagEventMqtt.Enqueue(dataToPublish);
                var mqttCommandResponseTopic = $"{mqttManagementEventsTopic}";
                var serializedData = JsonConvert.SerializeObject(dataToPublish);
                _ = _mqttClient.PublishAsync(mqttCommandResponseTopic, serializedData, mqttQualityOfServiceLevel, retain);
            }
            catch (Exception)
            {
            }
        }

    }

    private async void ProcessAppStatus()
    {
        Dictionary<string, string> statusEvent = new Dictionary<string, string>();

        statusEvent.Add("eventType", "status");

        statusEvent.Add("component", "smartreader");
        statusEvent.Add("readerName", _standaloneConfigDTO.readerName);
        statusEvent.Add("serialNumber", $"{_iotDeviceInterfaceClient.UniqueId}");
        statusEvent.Add("timestamp", DateTime.Now.ToUniversalTime().ToString("o"));
        //statusEvent.Add("timestamp", $"{currentReaderStatus.Time.Value.ToUniversalTime().ToString("o")}");
        //statusEvent.Add("displayName", _iotDeviceInterfaceClient.DisplayName);
        //statusEvent.Add("hostname", _iotDeviceInterfaceClient.Hostname);
        statusEvent.Add("macAddress", _iotDeviceInterfaceClient.MacAddress);
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
                double.TryParse(txPower[i], NumberStyles.Float, CultureInfo.InvariantCulture, out txPowerValuecDbm);
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
            if (currentReaderStatus != null)
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



        if (string.Equals("1", _standaloneConfigDTO.includeGpiEvent, StringComparison.OrdinalIgnoreCase))
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


                //_messageQueueTagSmartReaderTagEventMqtt.Enqueue(dataToPublish);
                var mqttCommandResponseTopic = $"{mqttManagementEventsTopic}";
                var serializedData = JsonConvert.SerializeObject(dataToPublish);
                _mqttClient.PublishAsync(mqttCommandResponseTopic, serializedData, mqttQualityOfServiceLevel, retain);
                _ = ProcessGpoErrorPortRecoveryAsync();
            }
            catch (Exception)
            {
                _ = ProcessGpoErrorPortAsync();
            }
        }

    }

    private async void ProcessAppStatusDetailed()
    {
        Dictionary<string, string> statusEvent = new Dictionary<string, string>();

        statusEvent.Add("eventType", "status");

        statusEvent.Add("component", "smartreader");
        statusEvent.Add("readerName", _standaloneConfigDTO.readerName);
        //statusEvent.Add("serialNumber", $"{_iotDeviceInterfaceClient.UniqueId}");
        statusEvent.Add("timestamp", DateTime.Now.ToUniversalTime().ToString("o"));
        //statusEvent.Add("timestamp", $"{currentReaderStatus.Time.Value.ToUniversalTime().ToString("o")}");
        //statusEvent.Add("displayName", _iotDeviceInterfaceClient.DisplayName);
        //statusEvent.Add("hostname", _iotDeviceInterfaceClient.Hostname);
        statusEvent.Add("macAddress", _iotDeviceInterfaceClient.MacAddress);
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
                double.TryParse(txPower[i], NumberStyles.Float, CultureInfo.InvariantCulture, out txPowerValuecDbm);
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
            if (currentReaderStatus != null)
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



        if (string.Equals("1", _standaloneConfigDTO.includeGpiEvent, StringComparison.OrdinalIgnoreCase))
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


                //_messageQueueTagSmartReaderTagEventMqtt.Enqueue(dataToPublish);
                var mqttCommandResponseTopic = $"{mqttManagementEventsTopic}";
                var serializedData = JsonConvert.SerializeObject(dataToPublish);
                _mqttClient.PublishAsync(mqttCommandResponseTopic, serializedData, mqttQualityOfServiceLevel, retain);
                _ = ProcessGpoErrorPortRecoveryAsync();
            }
            catch (Exception)
            {
                await ProcessGpoErrorPortAsync();
            }
        }

    }

    private async void ProcessKeepalive()
    {
        if (string.Equals("0", _standaloneConfigDTO.heartbeatEnabled, StringComparison.OrdinalIgnoreCase)) return;

        var smartReaderTagReadEvent = new SmartReaderTagReadEvent();
        smartReaderTagReadEvent.TagReads = new List<TagRead>();
        var tagRead = new TagRead();

        tagRead.FirstSeenTimestamp = Utils.CSharpMillisToJavaLongMicroseconds(DateTime.Now);

        smartReaderTagReadEvent.ReaderName = _standaloneConfigDTO.readerName;

        smartReaderTagReadEvent.Mac = _iotDeviceInterfaceClient.MacAddress;

        if (string.Equals("1", _standaloneConfigDTO.siteEnabled, StringComparison.OrdinalIgnoreCase))
            smartReaderTagReadEvent.Site = _standaloneConfigDTO.site;

        tagRead.Epc = "*****";
        tagRead.IsHeartBeat = true;
        if (string.Equals("1", _standaloneConfigDTO.includeGpiEvent, StringComparison.OrdinalIgnoreCase))
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

        smartReaderTagReadEvent.TagReads.Add(tagRead);

        var jsonData = JsonConvert.SerializeObject(smartReaderTagReadEvent);
        var dataToPublish = JObject.Parse(jsonData);

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

        if (string.Equals("1", _standaloneConfigDTO.httpPostEnabled, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrEmpty(_standaloneConfigDTO.httpPostURL))
            try
            {
                _messageQueueTagSmartReaderTagEventHttpPost.Enqueue(dataToPublish);
            }
            catch (Exception)
            {
            }

        if (string.Equals("1", _standaloneConfigDTO.socketServer, StringComparison.OrdinalIgnoreCase))
            try
            {
                _messageQueueTagSmartReaderTagEventSocketServer.Enqueue(dataToPublish);
            }
            catch (Exception)
            {
            }

        if (string.Equals("1", _standaloneConfigDTO.usbFlashDrive, StringComparison.OrdinalIgnoreCase))
            try
            {
                _messageQueueTagSmartReaderTagEventUsbDrive.Enqueue(dataToPublish);
            }
            catch (Exception)
            {

            }


        if (string.Equals("1", _standaloneConfigDTO.mqttEnabled, StringComparison.OrdinalIgnoreCase))
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


                //_messageQueueTagSmartReaderTagEventMqtt.Enqueue(dataToPublish);
                var mqttCommandResponseTopic = $"{mqttManagementEventsTopic}";
                var serializedData = JsonConvert.SerializeObject(dataToPublish);
                _mqttClient.PublishAsync(mqttCommandResponseTopic, serializedData, mqttQualityOfServiceLevel, retain);
                _ = ProcessGpoErrorPortRecoveryAsync();
            }
            catch (Exception)
            {
                await ProcessGpoErrorPortAsync();
            }
    }

    private async void OnInventoryStatusEvent(object sender, InventoryStatusEvent eventStatus)
    {
        try
        {
            var mqttManagementEvents = new Dictionary<string, object>();
            if (eventStatus.Status == InventoryStatusEventStatus.Idle)
                mqttManagementEvents.Add("smartreader-status", "inventory-idle");
            else
                mqttManagementEvents.Add("smartreader-status", "inventory-running");
            PublishMqttManagementEvent(mqttManagementEvents);
        }
        catch (Exception)
        {
        }

        try
        {
            if (string.Equals("1", _standaloneConfigDTO.mqttEnabled, StringComparison.OrdinalIgnoreCase))
                try
                {
                    if (!string.IsNullOrEmpty(_standaloneConfigDTO.mqttManagementEventsTopic))
                        if (_standaloneConfigDTO.mqttManagementEventsTopic.Contains("{{deviceId}}"))
                            _standaloneConfigDTO.mqttManagementEventsTopic =
                                _standaloneConfigDTO.mqttManagementEventsTopic.Replace("{{deviceId}}",
                                    _standaloneConfigDTO.readerName);
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

                    var mqttManagementEventsTopic = $"{_standaloneConfigDTO.mqttManagementEventsTopic}";


                    var serializedData = JsonConvert.SerializeObject(eventStatus);
                    _mqttClient.PublishAsync(mqttManagementEventsTopic, serializedData,
                        mqttQualityOfServiceLevel, retain);
                }
                catch (Exception)
                {
                }

            //_logger.LogInformation("Inventory status : " + eventStatus.Status, SeverityType.Debug);
            if (eventStatus.Status == InventoryStatusEventStatus.Running)
            {
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

                if (!string.IsNullOrEmpty(_standaloneConfigDTO.stopTriggerDuration)
                    && !string.Equals("0", _standaloneConfigDTO.stopTriggerDuration,
                        StringComparison.OrdinalIgnoreCase))
                {
                    long stopTiggerDuration = 100;
                    long.TryParse(_standaloneConfigDTO.stopTriggerDuration, out stopTiggerDuration);
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
            }
            else if (eventStatus.Status == InventoryStatusEventStatus.Idle)
            {

                var shouldAcceptEventStatus = false;
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
                    //if (_messageQueueTagSmartReaderTagEventGroupToValidate.Count > 0)
                    //{
                    //var existingItems = _messageQueueTagSmartReaderTagEventBarcodeGroup.ToArray();
                    //foreach (var item in existingItems)
                    //{
                    //    _messageQueueTagSmartReaderTagEventHttpPost.Enqueue(item);
                    //}
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
                if (string.Equals("3", _standaloneConfigDTO.startTriggerType, StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        StartPresetAsync();
                    }
                    catch (Exception)
                    {


                    }
                }
                //}
                //else if (string.Equals("1", _standaloneConfigDTO.enableBarcodeTcp, StringComparison.OrdinalIgnoreCase)
                //           && _messageQueueBarcode.Count > 0 && _messageQueueTagSmartReaderTagEventGroupToValidate.Count == 0)
                //{
                //    Console.WriteLine("=====================================================================");
                //    Console.WriteLine("Inventory Iddle event detected, trying to dequeue pending barcode...");                            
                //    var dequeuedBarcode = ProcessBarcodeQueue();
                //    Console.WriteLine("=============> dequeuedBarcode ["+ dequeuedBarcode + "]");
                //    Console.WriteLine("=====================================================================");
                //}
                //else
                //{
                //    string barcode = "";
                //    _messageQueueBarcode.TryDequeue(out barcode);
                //}
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
            }
        }
        catch (Exception exOnInventoryStatusEvent)
        {
            _logger.LogError(exOnInventoryStatusEvent,
                "Unexpected error on OnInventoryStatusEvent " + exOnInventoryStatusEvent.Message);
        }


    }

    private string ProcessBarcodeQueue()
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
                    _messageQueueBarcode.TryDequeue(out localBarcode);

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

    private async void OnGpiTransitionEvent(object sender, Impinj.Atlas.GpiTransitionEvent gpiEvent)
    {
        try
        {
            var currentGpiStatus = false;
            _logger.LogInformation("Gpi Transition : " + gpiEvent.Gpi + " - " + gpiEvent.Transition,
                SeverityType.Debug);
            if (gpiEvent.Transition == GpiTransitionEventTransition.HighToLow)
                currentGpiStatus = false;
            else if (gpiEvent.Transition == GpiTransitionEventTransition.LowToHigh) currentGpiStatus = true;
            if (_gpiPortStates.ContainsKey(gpiEvent.Gpi.Value))
                _gpiPortStates[gpiEvent.Gpi.Value] = currentGpiStatus;
            else
                _gpiPortStates.TryAdd(gpiEvent.Gpi.Value, currentGpiStatus);
        }
        catch (Exception exOnGpiStatusEvent)
        {
            _logger.LogError(exOnGpiStatusEvent,
                "Unexpected error on OnGpiTransitionEvent " + exOnGpiStatusEvent.Message);
        }
    }


    private async void OnTagInventoryEvent(object sender, TagInventoryEvent tagEvent)
    {
        try
        {
            var shouldProcess = false;
            _logger.LogInformation("EPC Hex: {0} Antenna : {1} LastSeenTime {2}", tagEvent.EpcHex, tagEvent.AntennaPort,
                tagEvent.LastSeenTime);

            Task.Run(() => ProcessGpoBlinkAnyTagGpoPortAsync());
            

            if (string.Equals("1", _standaloneConfigDTO.tagPresenceTimeoutEnabled,
                            StringComparison.OrdinalIgnoreCase))
            {
                if (_smartReaderTagEventsListBatch.ContainsKey(tagEvent.EpcHex))
                {
                    try
                    {
                        var updatedCurrentEventTimestamp = Utils.CSharpMillisToJavaLong(DateTime.Now) * 1000;
                        _smartReaderTagEventsListBatch[tagEvent.EpcHex]["firstSeenTimestamp"] = updatedCurrentEventTimestamp;
                    }
                    catch (KeyNotFoundException)
                    {
                        // epc already expired
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "error updating last seen time for the tag presence timeout.");
                    }

                }
            }
            if (!string.IsNullOrEmpty(_standaloneConfigDTO.stopTriggerDuration))
            {
                long stopTiggerDuration = 100;
                long.TryParse(_standaloneConfigDTO.stopTriggerDuration, out stopTiggerDuration);
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
                    long timeoutInSec = 0;
                    var seenCountThreshold = 1;
                    var currentEventTimestamp = Utils.CSharpMillisToJavaLong(DateTime.Now);
                    if (!string.Equals("0", _standaloneConfigDTO.softwareFilterReadCountTimeoutIntervalInSec,
                            StringComparison.OrdinalIgnoreCase))
                        long.TryParse(_standaloneConfigDTO.softwareFilterReadCountTimeoutIntervalInSec,
                            out timeoutInSec);

                    if (!string.Equals("0", _standaloneConfigDTO.softwareFilterReadCountTimeoutSeenCount,
                            StringComparison.OrdinalIgnoreCase))
                        int.TryParse(_standaloneConfigDTO.softwareFilterReadCountTimeoutSeenCount,
                            out seenCountThreshold);

                    if (!_softwareFilterReadCountTimeoutDictionary.ContainsKey(tagEvent.EpcHex))
                    {
                        var readCountTimeoutEvent = new ReadCountTimeoutEvent();
                        readCountTimeoutEvent.Epc = tagEvent.EpcHex;
                        readCountTimeoutEvent.EventTimestamp = currentEventTimestamp;
                        if (tagEvent.AntennaPort.HasValue) readCountTimeoutEvent.Antenna = tagEvent.AntennaPort.Value;
                        _softwareFilterReadCountTimeoutDictionary.TryAdd(tagEvent.EpcHex, readCountTimeoutEvent);
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
                catch (Exception)
                {
                }

            if (_readEpcs == null) _readEpcs = new ConcurrentDictionary<string, long>();


            if (!string.Equals("0", _standaloneConfigDTO.softwareFilterEnabled, StringComparison.OrdinalIgnoreCase))
            {
                var currentEventTimestamp = Utils.CSharpMillisToJavaLong(DateTime.Now);
                if (!_readEpcs.ContainsKey(tagEvent.EpcHex))
                {
                    _readEpcs.TryAdd(tagEvent.EpcHex, currentEventTimestamp);
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
                        shouldProcess = true;
                        _readEpcs[tagEvent.EpcHex] = currentEventTimestamp;
                    }
                    else
                    {
                        shouldProcess = false;
                        _logger.LogInformation(
                            "Ignoring tag event due to softwareFilterWindowSec on OnTagInventoryEvent ",
                            SeverityType.Debug);
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
                    _lastTagRead.Epc = tagEvent.EpcHex;
                    _lastTagRead.AntennaPort = tagEvent.AntennaPort;
                    //var currentEventTimestamp = Utils.CSharpMillisToJavaLong(DateTime.Now);
                    if (tagEvent.LastSeenTime.HasValue)
                        _lastTagRead.FirstSeenTimestamp = tagEvent.LastSeenTime.Value.ToFileTimeUtc();
                    shouldProcess = true;
                }
                else
                {
                    if (_lastTagRead.Epc == tagEvent.EpcHex && _lastTagRead.AntennaPort == tagEvent.AntennaPort)
                    {
                        if (tagEvent.LastSeenTime.HasValue)
                            if (_lastTagRead.FirstSeenTimestamp != tagEvent.LastSeenTime.Value.ToFileTimeUtc())
                                shouldProcess = true;
                    }
                    else
                    {
                        shouldProcess = true;
                    }

                    _lastTagRead.Epc = tagEvent.EpcHex;
                    _lastTagRead.AntennaPort = tagEvent.AntennaPort;
                    if (tagEvent.LastSeenTime.HasValue)
                        _lastTagRead.FirstSeenTimestamp = tagEvent.LastSeenTime.Value.ToFileTimeUtc();
                }
            }

            if (string.Equals("0", _standaloneConfigDTO.softwareFilterEnabled, StringComparison.OrdinalIgnoreCase)
                && string.Equals("0", _standaloneConfigDTO.softwareFilterReadCountTimeoutEnabled,
                    StringComparison.OrdinalIgnoreCase)
                && !shouldProcess)
                shouldProcess = true;

            

            //_messageQueueTagInventoryEvent.Enqueue(tagEvent);
            if (shouldProcess)
            {
                
                //if (string.Equals("1", _standaloneConfigDTO.softwareFilterEnabled, StringComparison.OrdinalIgnoreCase))
                //{
                //    try
                //    {
                //        int intervalInSec = 1;
                //        if (!string.IsNullOrEmpty(_standaloneConfigDTO.softwareFilterWindowSec))
                //        {
                //            int.TryParse(_standaloneConfigDTO.softwareFilterWindowSec, out intervalInSec);

                //            var currentEventTimestamp = Utils.CSharpMillisToJavaLong(DateTime.Now);
                //            var seenTime = DateTimeOffset.FromUnixTimeMilliseconds(currentEventTimestamp);
                //            //var seenTime = DateTimeOffset.FromUnixTimeMilliseconds(tagEvent.LastSeenTime.Value.ToFileTimeUtc());
                //            if (_knowTagsForSoftwareFilterWindowSec.ContainsKey(tagEvent.EpcHex))
                //            {
                //                if (seenTime.Subtract(_knowTagsForSoftwareFilterWindowSec[tagEvent.Epc]).TotalSeconds < intervalInSec)
                //                {
                //                    MqttLog("INFO", "Ignoring EPC  [" + tagEvent.EpcHex + "] due to Software filter by interval [" + intervalInSec + "].  ");
                //                    _logger.LogInformation("Ignoring EPC  [" + tagEvent.EpcHex + "] due to Software filter by interval [" + intervalInSec + "].  ", SeverityType.Debug);
                //                    return;
                //                }
                //                else
                //                {
                //                    _knowTagsForSoftwareFilterWindowSec[tagEvent.EpcHex] = seenTime;
                //                }
                //            }
                //            else
                //            {
                //                _knowTagsForSoftwareFilterWindowSec.TryAdd(tagEvent.EpcHex, seenTime);
                //            }
                //        }

                //    }
                //    catch (Exception exSoftwareFilter)
                //    {
                //        MqttLog("ERROR", "Error processing software filter. " + exSoftwareFilter.Message);
                //    }
                //}
                if (!_oldReadEpcList.Contains(tagEvent.EpcHex))
                {
                    _oldReadEpcList.Add(tagEvent.EpcHex);
                    try
                    {
                        //await Task.Run(async () => { await BlinkTagStatusGpoPortAsync(1); });
                        Task.Run(() => ProcessGpoBlinkNewTagStatusGpoPortAsync(1));

                    }
                    catch (Exception)
                    {
                    }

                }
                


                //else
                //{
                //    try
                //    {
                //        await ProcessGpoNoNewTagPortAsync(false, 500);
                //    }
                //    catch (Exception)
                //    {
                //    }
                //}

                await ProcessTagInventoryEventAsync(tagEvent);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error on OnTagInventoryEvent " + ex.Message);
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
                        Task.Run(() => ProcessHttpJsonPostTagEventDataAsync(smartReaderTagReadEvent));
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

    private async void ProcessValidationTagQueue()
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
                var skuSummary = new SkuSummary();
                skuSummary.Sku = "00000000000000";
                skuSummary.Qty = 0;
                if (!string.IsNullOrEmpty(barcode))
                    skuSummary.Barcode = barcode;
                else
                    skuSummary.Barcode = "";
                skuSummary.EventTimestamp = eventTimestamp;
                skuSummaryList.Add(skuSummary.Sku, skuSummary);
                _logger.LogInformation("skuSummaryList.count [" + skuSummaryList.Count + "] barcode [" + barcode +
                                       "] on empty event.");
                if (!string.IsNullOrEmpty(_standaloneConfigDTO.enableSummaryStream)
                    && string.Equals("1", _standaloneConfigDTO.enableSummaryStream, StringComparison.OrdinalIgnoreCase))
                {
                    var skuArray = JArray.FromObject(skuSummaryList.Values);
                    AddJsonSkuSummaryToQueue(skuArray);
                }

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
                                        skuSummaryList[tagDataKeyValue].Epcs = new List<string>();
                                    if (!string.IsNullOrEmpty(epcValue) &&
                                        !skuSummaryList[tagDataKeyValue].Epcs.Contains(epcValue))
                                        skuSummaryList[tagDataKeyValue].Epcs.Add(epcValue);
                                }
                                else
                                {
                                    var skuSummary = new SkuSummary();
                                    skuSummary.Sku = tagDataKeyValue;
                                    skuSummary.Qty = 1;
                                    if (!string.IsNullOrEmpty(barcode))
                                        skuSummary.Barcode = barcode;
                                    else
                                        skuSummary.Barcode = "";
                                    if (skuSummary.Epcs == null) skuSummary.Epcs = new List<string>();
                                    if (!string.IsNullOrEmpty(epcValue) && !skuSummary.Epcs.Contains(epcValue))
                                        skuSummary.Epcs.Add(epcValue);
                                    skuSummary.EventTimestamp = eventTimestamp;
                                    if (smartReaderTagReadEventAggregated.ContainsKey("site"))
                                    {
                                        var site = smartReaderTagReadEventAggregated["site"].Value<string>();
                                        if (skuSummary.AdditionalData == null)
                                            skuSummary.AdditionalData = new Dictionary<string, string>();
                                        if (!skuSummary.AdditionalData.ContainsKey("site"))
                                            skuSummary.AdditionalData.Add("site", site);
                                    }

                                    if (smartReaderTagReadEventAggregated.ContainsKey("status"))
                                    {
                                        var customField = smartReaderTagReadEventAggregated["status"].Value<string>();
                                        if (!string.IsNullOrEmpty(customField))
                                        {
                                            if (skuSummary.AdditionalData == null)
                                                skuSummary.AdditionalData = new Dictionary<string, string>();
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
                                                skuSummary.AdditionalData = new Dictionary<string, string>();
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
                                                skuSummary.AdditionalData = new Dictionary<string, string>();
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
                                                skuSummary.AdditionalData = new Dictionary<string, string>();
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

                            if (_standaloneConfigDTO != null && !string.IsNullOrEmpty(_standaloneConfigDTO
                                                                 .enableBarcodeSerial)
                                                             && string.Equals("1",
                                                                 _standaloneConfigDTO.enableBarcodeSerial,
                                                                 StringComparison.OrdinalIgnoreCase)
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
                            else if (_standaloneConfigDTO != null && !string.IsNullOrEmpty(_standaloneConfigDTO
                                                                      .enableBarcodeSerial)
                                                                  && string.Equals("0",
                                                                      _standaloneConfigDTO.enableBarcodeSerial,
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

                            try
                            {
                                bool isValidated = false;



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
                                            skusToValidate.TryAdd(skuEntry.Key, unchecked((int)skuEntry.Value.Qty.Value));
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
                                        _plugins[_standaloneConfigDTO.activePlugin].ExternalApiPublish(barcode, skuSummaryList, epcArray);
                                        //return;
                                    }
                                }
                                catch (Exception)
                                {

                                }

                            }

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

    private async void OnRunPeriodicTagFilterListsEvent(object sender, ElapsedEventArgs e)
    {
        _timerTagFilterLists.Enabled = false;
        _timerTagFilterLists.Stop();

        try
        {
            if (!string.IsNullOrEmpty(_standaloneConfigDTO.softwareFilterEnabled)
                && !"0".Equals(_standaloneConfigDTO.softwareFilterEnabled))
                try
                {
                    if (_readEpcs.Count > 1000)
                    {
                        var count = _readEpcs.Count - 1000;
                        if (count > 0)
                        {
                            // remove that number of items from the start of the list
                            long eventTimestampToRemove = 0;
                            foreach (var k in _readEpcs.Keys.Take(count))
                                _readEpcs.TryRemove(k, out eventTimestampToRemove);
                        }
                    }

                    var oldTimestamp = DateTime.Now.AddHours(-1);
                    var oldTimestampToCheck = Utils.CSharpMillisToJavaLong(oldTimestamp);
                    foreach (var kvp in _readEpcs.Where(x => x.Value < oldTimestampToCheck).ToList())
                    {
                        var val = kvp.Value;
                        _readEpcs.TryRemove(kvp.Key, out val);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error on remove _readEpcs" + ex.Message);
                }

            //if (!string.IsNullOrEmpty(_standaloneConfigDTO.softwareFilterEnabled)
            //    && !"0".Equals(_standaloneConfigDTO.softwareFilterEnabled))
            //{
            //    try
            //    {
            //        var oldTimestamp = DateTime.Now.AddHours(-1);
            //        var oldTimestampToCheck = Utils.CSharpMillisToJavaLong(oldTimestamp);
            //        DateTimeOffset oldDateTimeOffset = DateTimeOffset.FromUnixTimeMilliseconds(oldTimestampToCheck);
            //        foreach (var kvp in _knowTagsForSoftwareFilterWindowSec.Where(x => x.Value > oldDateTimeOffset).ToList())
            //        {
            //            var val = kvp.Value;
            //            _knowTagsForSoftwareFilterWindowSec.TryRemove(kvp.Key, out val);
            //        }
            //    }
            //    catch (Exception)
            //    {

            //    }
            //}

            if (!string.IsNullOrEmpty(_standaloneConfigDTO.softwareFilterReadCountTimeoutEnabled)
                && !"0".Equals(_standaloneConfigDTO.softwareFilterReadCountTimeoutEnabled))
                try
                {
                    if (_softwareFilterReadCountTimeoutDictionary.Count > 1000)
                    {
                        var count = _softwareFilterReadCountTimeoutDictionary.Count - 1000;
                        if (count > 0)
                        {
                            // remove that number of items from the start of the list
                            ReadCountTimeoutEvent? eventTimestampToRemove;
                            foreach (var k in _softwareFilterReadCountTimeoutDictionary.Keys.Take(count))
                                _softwareFilterReadCountTimeoutDictionary.TryRemove(k, out eventTimestampToRemove);
                        }
                    }

                    var currentTimestamp = DateTime.Now;
                    var currentTimestampToCheck = Utils.CSharpMillisToJavaLong(currentTimestamp);
                    var dateTimeOffsetCurrentEventTimestamp =
                        DateTimeOffset.FromUnixTimeMilliseconds(currentTimestampToCheck);
                    foreach (var kvp in _softwareFilterReadCountTimeoutDictionary)
                    {
                        var dateTimeOffsetLastSeenTimestamp =
                            DateTimeOffset.FromUnixTimeMilliseconds(kvp.Value.EventTimestamp);
                        var timeDiff = dateTimeOffsetCurrentEventTimestamp.Subtract(dateTimeOffsetLastSeenTimestamp);
                        if (timeDiff.TotalHours > 1)
                        {
                            var val = kvp.Value;
                            _softwareFilterReadCountTimeoutDictionary.TryRemove(kvp.Key, out val);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Unexpected error on remove _softwareFilterReadCountTimeoutDictionary" + ex.Message);
                }

            if (string.Equals("1", _standaloneConfigDTO.tagPresenceTimeoutEnabled, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var currentEventTimestamp = Utils.CSharpMillisToJavaLong(DateTime.Now);

                    double expirationInSec = 2;
                    double expirationInMillis = 2000;
                    double.TryParse(_standaloneConfigDTO.tagPresenceTimeoutInSec, NumberStyles.Float, CultureInfo.InvariantCulture, out expirationInSec);
                    expirationInMillis = expirationInSec * 1000;

                    var dateTimeOffsetCurrentEventTimestamp = DateTimeOffset.FromUnixTimeMilliseconds(currentEventTimestamp);
                    //var dateTimeOffsetLastSeenTimestamp = DateTimeOffset.FromUnixTimeMilliseconds(_readEpcs[tagEvent.EpcHex]);

                    var secondsToAdd = expirationInSec * -1;
                    var expiredEventTimestamp = Utils.CSharpMillisToJavaLong(DateTime.Now.AddSeconds(secondsToAdd));

                    //var expiredEvents = _smartReaderTagEventsListBatch.Values.Where(t => t["tag_reads"]["firstSeenTimestamp"].Where(q => (long)q["firstSeenTimestamp"] <= expiredEventTimestamp));
                    var currentEvents = _smartReaderTagEventsListBatch.Values;
                    //var expiredEvents = _smartReaderTagEventsListBatch.Values.Where(t => (long)t["tag_reads"]["firstSeenTimestamp"] <= expiredEventTimestamp);
                    //var existingEventOnCurrentAntenna = (JObject)(retrievedValue["tag_reads"].FirstOrDefault(q => (long)q["antennaPort"] == tagRead.AntennaPort));

                    foreach (JObject currentEvent in currentEvents)
                    {
                        foreach (JObject currentEventItem in currentEvent["tag_reads"])
                        {
                            long firstSeenTimestamp = currentEventItem["firstSeenTimestamp"].Value<long>() / 1000;
                            string expiredEpc = currentEventItem["epc"].Value<string>();
                            var dateTimeOffsetLastSeenTimestamp = DateTimeOffset.FromUnixTimeMilliseconds(firstSeenTimestamp);

                            var timeDiff = dateTimeOffsetCurrentEventTimestamp.Subtract(dateTimeOffsetLastSeenTimestamp);
                            if (timeDiff.TotalSeconds > expirationInSec)
                            {

                                JObject eventToRemove = null;
                                if (_smartReaderTagEventsListBatch.TryGetValue(expiredEpc, out eventToRemove))
                                {
                                    if (eventToRemove != null)
                                    {
                                        if (_smartReaderTagEventsListBatch.ContainsKey(expiredEpc))
                                        {

                                            if (_smartReaderTagEventsListBatch.TryRemove(expiredEpc, out eventToRemove))
                                            {
                                                _logger.LogInformation($"Expired EPC detected {timeDiff.TotalSeconds}: {expiredEpc} - firstSeenTimestamp: {dateTimeOffsetLastSeenTimestamp.ToString("o")}, current timestamp: {dateTimeOffsetCurrentEventTimestamp.ToString("o")} current timeout set {expirationInSec}");
                                            }
                                        }

                                        if (!_smartReaderTagEventsAbsence.ContainsKey(expiredEpc))
                                        {
                                            _logger.LogInformation($"On-Change event requested for expired EPC: {expiredEpc}");
                                            _smartReaderTagEventsAbsence.TryAdd(expiredEpc, eventToRemove);

                                        }

                                    }
                                }
                            }
                        }
                    }

                    if (string.Equals("1", _standaloneConfigDTO.tagPresenceTimeoutEnabled,
                            StringComparison.OrdinalIgnoreCase) && _smartReaderTagEventsAbsence.Any())
                    {
                        JObject smartReaderTagReadEvent;
                        JObject smartReaderTagReadEventAggregated = null;
                        var smartReaderTagReadEventsArray = new JArray();

                        foreach (var tagEpcToRemove in _smartReaderTagEventsAbsence.Keys)
                        {
                            JObject expiredTagEvent = null;

                            if (_smartReaderTagEventsListBatch.ContainsKey(tagEpcToRemove))
                            {
                                _smartReaderTagEventsListBatch.TryRemove(tagEpcToRemove, out expiredTagEvent);
                            }

                        }

                        _smartReaderTagEventsAbsence.Clear();

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
                                                "Unexpected error on OnRunPeriodicTagPublisherMqttTasksEvent " + ex.Message);
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(ex, "Unexpected error on OnRunPeriodicTagPublisherMqttTasksEvent " + ex.Message);
                                }
                            }
                            try
                            {
                                if (smartReaderTagReadEventAggregated != null && smartReaderTagReadEventsArray != null &&
                                    smartReaderTagReadEventsArray.Count > 0)
                                {
                                    smartReaderTagReadEventAggregated["tag_reads"] = smartReaderTagReadEventsArray;

                                    await ProcessMqttJsonTagEventDataAsync(smartReaderTagReadEventAggregated);
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Unexpected error on OnRunPeriodicTagPublisherMqttTasksEvent " + ex.Message);
                            }
                        }
                        else
                        {
                            // generate empty event...


                            var emptyTagData = new SmartReaderTagReadEvent();
                            emptyTagData.ReaderName = _standaloneConfigDTO.readerName;
                            emptyTagData.Mac = _iotDeviceInterfaceClient.MacAddress;
                            emptyTagData.TagReads = new List<TagRead>();
                            JObject emptyTagDataObject = JObject.FromObject(emptyTagData);
                            smartReaderTagReadEventsArray.Add(emptyTagDataObject);

                            try
                            {

                                await ProcessMqttJsonTagEventDataAsync(emptyTagDataObject);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Unexpected error on OnRunPeriodicTagPublisherMqttTasksEvent " + ex.Message);
                            }
                        }


                    }


                }
                catch (Exception)
                {


                }
            }
        }
        catch (Exception)
        {
        }


        _timerTagFilterLists.Enabled = true;
        _timerTagFilterLists.Start();
    }

    private async void OnRunPeriodicKeepaliveCheck(object sender, ElapsedEventArgs e)
    {
        //_timerKeepalive.Enabled = false;
        //_timerKeepalive.Stop();

        try
        {
            if (string.Equals("1", _standaloneConfigDTO.heartbeatEnabled, StringComparison.OrdinalIgnoreCase))
            {
                if (_stopwatchKeepalive.Elapsed.TotalMilliseconds > double.Parse(_standaloneConfigDTO.heartbeatPeriodSec) * 1000)
                {

                    //_stopwatchKeepalive.Stop();
                    //_stopwatchKeepalive.Reset();
                    _stopwatchKeepalive.Restart();
                    //ProcessKeepalive();
                    Task.Run(() => { ProcessKeepalive(); });
                }
            }

        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                    "Unexpected error running keepalive manager on OnRunPeriodicKeepaliveCheck. " + ex.Message);
        }

        //_timerKeepalive.Enabled = true;
        //_timerKeepalive.Start();
    }

    private async void OnRunPeriodicStopTriggerDurationEvent(object sender, ElapsedEventArgs e)
    {
        _timerStopTriggerDuration.Enabled = false;
        _timerStopTriggerDuration.Stop();

        try
        {
            if (!string.IsNullOrEmpty(_standaloneConfigDTO.stopTriggerDuration))
            {
                long stopTiggerDuration = 100;
                long.TryParse(_standaloneConfigDTO.stopTriggerDuration, out stopTiggerDuration);
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
                                    StopPresetAsync();
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
        }
        catch (Exception)
        {
        }


        _timerStopTriggerDuration.Enabled = true;
        _timerStopTriggerDuration.Start();
    }

    private async void OnRunPeriodicTagPublisherHttpTasksEvent(object sender, ElapsedEventArgs e)
    {
        _timerTagPublisherHttp.Enabled = false;
        _timerTagPublisherHttp.Stop();

        try
        {
            JObject smartReaderTagReadEvent;
            JObject smartReaderTagReadEventAggregated = null;
            var smartReaderTagReadEventsArray = new JArray();
            var httpPostTimer = Stopwatch.StartNew();
            var httpPostIntervalInSec = 1;
            if (!string.IsNullOrEmpty(_standaloneConfigDTO.httpPostIntervalSec))
            {
                int.TryParse(_standaloneConfigDTO.httpPostIntervalSec, out httpPostIntervalInSec);
                if (httpPostIntervalInSec < 1) httpPostIntervalInSec = 1;
            }

            if (!string.IsNullOrEmpty(_standaloneConfigDTO.httpPostEnabled)
                        && string.Equals("1", _standaloneConfigDTO.httpPostEnabled,
                            StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(_standaloneConfigDTO.enableTagEventsListBatch)
                        && string.Equals("1", _standaloneConfigDTO.enableTagEventsListBatch,
                            StringComparison.OrdinalIgnoreCase))
                {
                    while (httpPostTimer.Elapsed.Seconds < httpPostIntervalInSec)
                    {
                        await Task.Delay(10);
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
                                            "Unexpected error on OnRunPeriodicTagPublisherMqttTasksEvent " + ex.Message);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Unexpected error on OnRunPeriodicTagPublisherMqttTasksEvent " + ex.Message);
                            }
                        }
                    }

                }
                else
                {
                    while (httpPostTimer.Elapsed.Seconds < httpPostIntervalInSec)
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
    }

    private async void OnRunPeriodicTagPublisherSocketTasksEvent(object sender, ElapsedEventArgs e)
    {
        _timerTagPublisherSocket.Enabled = false;
        _timerTagPublisherSocket.Stop();


        try
        {
            JObject smartReaderTagReadEvent;
            var currentSocketQueueData = new ConcurrentQueue<JObject>(_messageQueueTagSmartReaderTagEventSocketServer);
            _messageQueueTagSmartReaderTagEventSocketServer.Clear();
            while (currentSocketQueueData.TryDequeue(out smartReaderTagReadEvent))
                try
                {
                    await ProcessSocketJsonTagEventDataAsync(smartReaderTagReadEvent);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error on OnRunPeriodicTagPublisherTasksEvent " + ex.Message);
                }
        }
        catch (Exception)
        {
        }


        _timerTagPublisherSocket.Enabled = true;
        _timerTagPublisherSocket.Start();
    }

    private async void OnRunPeriodicSummaryStreamPublisherTasksEvent(object sender, ElapsedEventArgs e)
    {
        _timerSummaryStreamPublisher.Enabled = false;
        _timerSummaryStreamPublisher.Stop();
        try
        {
            if (string.Equals("0", _standaloneConfigDTO.stopTriggerType, StringComparison.OrdinalIgnoreCase)
                && _messageQueueTagSmartReaderTagEventGroupToValidate.Count > 0)
            {
                ProcessValidationTagQueue();
            }
            else
            {
                await Task.Delay(100);
            }

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
            JObject smartReaderTagReadEvent;
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


    private async void OnRunPeriodicTagPublisherOpcUaTasksEvent(object sender, ElapsedEventArgs e)
    {
        if (Monitor.TryEnter(_timerTagPublisherOpcUaLock))
            try
            {
                JObject smartReaderTagReadEvent;
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
    }

    private async void OnRunPeriodicTagPublisherUdpTasksEvent(object sender, ElapsedEventArgs e)
    {
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
    }

    private async void OnRunPeriodicTagPublisherMqttTasksEvent(object sender, ElapsedEventArgs e)
    {
        _timerTagPublisherMqtt.Enabled = false;
        _timerTagPublisherMqtt.Stop();

        //_logger.LogInformation($"OnRunPeriodicTagPublisherMqttTasksEvent: >>>");

        JObject smartReaderTagReadEvent;
        JObject smartReaderTagReadEventAggregated = null;
        var smartReaderTagReadEventsArray = new JArray();

        double mqttPublishIntervalInSec = 1;
        double mqttPublishIntervalInMillis = 10;
        double mqttUpdateTagEventsListBatchOnChangeIntervalInSec = 1;
        double mqttUpdateTagEventsListBatchOnChangeIntervalInMillis = 10;
        bool isUpdateTimeForOnChangeEvent = false;


        if (!string.IsNullOrEmpty(_standaloneConfigDTO.mqttPuslishIntervalSec))
            double.TryParse(_standaloneConfigDTO.mqttPuslishIntervalSec, NumberStyles.Float,
                CultureInfo.InvariantCulture, out mqttPublishIntervalInSec);

        if (mqttPublishIntervalInSec == 0)
            mqttPublishIntervalInMillis = 5;
        else
            mqttPublishIntervalInMillis = mqttPublishIntervalInSec * 1000;

        if (!string.IsNullOrEmpty(_standaloneConfigDTO.updateTagEventsListBatchOnChangeIntervalInSec))
            double.TryParse(_standaloneConfigDTO.updateTagEventsListBatchOnChangeIntervalInSec, NumberStyles.Float,
                CultureInfo.InvariantCulture, out mqttUpdateTagEventsListBatchOnChangeIntervalInSec);

        if (mqttUpdateTagEventsListBatchOnChangeIntervalInSec == 0)
            mqttUpdateTagEventsListBatchOnChangeIntervalInMillis = 5;
        else
            mqttUpdateTagEventsListBatchOnChangeIntervalInMillis = mqttUpdateTagEventsListBatchOnChangeIntervalInSec * 1000;

        try
        {
            if (!string.IsNullOrEmpty(_standaloneConfigDTO.mqttEnabled)
                        && string.Equals("1", _standaloneConfigDTO.mqttEnabled,
                            StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(_standaloneConfigDTO.enableTagEventsListBatch)
                        && string.Equals("1", _standaloneConfigDTO.enableTagEventsListBatch,
                            StringComparison.OrdinalIgnoreCase))
                {

                    //_logger.LogInformation($"_smartReaderTagEventsListBatchOnUpdate: {_smartReaderTagEventsListBatchOnUpdate.Count}");
                    //_logger.LogInformation($"_smartReaderTagEventsListBatch: {_smartReaderTagEventsListBatch.Count}");
                    if (!string.IsNullOrEmpty(_standaloneConfigDTO.updateTagEventsListBatchOnChange)
                        && string.Equals("1", _standaloneConfigDTO.updateTagEventsListBatchOnChange)
                        && !_stopwatchStopTagEventsListBatchOnChange.IsRunning)
                    {
                        _stopwatchStopTagEventsListBatchOnChange.Start();
                    }


                    if (!string.IsNullOrEmpty(_standaloneConfigDTO.updateTagEventsListBatchOnChange)
                        && string.Equals("1", _standaloneConfigDTO.updateTagEventsListBatchOnChange,
                            StringComparison.OrdinalIgnoreCase)
                        && _smartReaderTagEventsListBatchOnUpdate.Count > 0)
                    {

                        //while (_stopwatchStopTagEventsListBatchOnChange.Elapsed.Seconds * 1000 < mqttUpdateTagEventsListBatchOnChangeIntervalInMillis)
                        //{
                        //    await Task.Delay(10);
                        //}
                        if (_stopwatchStopTagEventsListBatchOnChange.Elapsed.Seconds * 1000 >= mqttUpdateTagEventsListBatchOnChangeIntervalInMillis)
                        {
                            if (_stopwatchStopTagEventsListBatchOnChange.IsRunning)
                            {
                                _stopwatchStopTagEventsListBatchOnChange.Stop();
                                _stopwatchStopTagEventsListBatchOnChange.Reset();
                            }

                            if (_smartReaderTagEventsListBatchOnUpdate.Count > 0)
                            {
                                _logger.LogInformation($"OnRunPeriodicTagPublisherMqttTasksEvent: publishing new events due to OnChange {_smartReaderTagEventsListBatchOnUpdate.Count}");
                                foreach (var smartReaderTagReadEventBatchKV in _smartReaderTagEventsListBatchOnUpdate)
                                {
                                    if (!_smartReaderTagEventsListBatch.ContainsKey(smartReaderTagReadEventBatchKV.Key))
                                    {
                                        _smartReaderTagEventsListBatch.TryAdd(smartReaderTagReadEventBatchKV.Key, smartReaderTagReadEventBatchKV.Value);
                                    }
                                    else
                                    {
                                        _smartReaderTagEventsListBatch[smartReaderTagReadEventBatchKV.Key] = smartReaderTagReadEventBatchKV.Value;
                                    }

                                }

                                _smartReaderTagEventsListBatchOnUpdate.Clear();

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
                                                        "Unexpected error on OnRunPeriodicTagPublisherMqttTasksEvent " + ex.Message);
                                                }
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            _logger.LogError(ex, "Unexpected error on OnRunPeriodicTagPublisherMqttTasksEvent " + ex.Message);
                                        }
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        if (_smartReaderTagEventsListBatch.Count > 0
                            && _smartReaderTagEventsListBatchOnUpdate.Count == 0)
                        {
                            if (string.Equals("1", _standaloneConfigDTO.enableTagEventsListBatchPublishing))
                            {
                                if (!_mqttPublisherStopwatch.IsRunning)
                                {
                                    _mqttPublisherStopwatch.Start();
                                }
                                //while (mqttPublisherTimer.Elapsed.Seconds * 1000 < mqttPublishIntervalInMillis)
                                //{
                                //    await Task.Delay(10);
                                //}
                                if (_mqttPublisherStopwatch.Elapsed.Seconds * 1000 >= mqttPublishIntervalInMillis)
                                {
                                    _mqttPublisherStopwatch.Restart();
                                    _logger.LogInformation($"OnRunPeriodicTagPublisherMqttTasksEvent: publishing batch events list due to MqttPublishInterval {_smartReaderTagEventsListBatch.Count}");
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
                                                        "Unexpected error on OnRunPeriodicTagPublisherMqttTasksEvent " + ex.Message);
                                                }
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            _logger.LogError(ex, "Unexpected error on OnRunPeriodicTagPublisherMqttTasksEvent " + ex.Message);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                else
                {
                    if (!_mqttPublisherStopwatch.IsRunning)
                    {
                        _mqttPublisherStopwatch.Start();
                    }

                    while (_mqttPublisherStopwatch.Elapsed.Seconds * 1000 < mqttPublishIntervalInMillis)
                        while (_messageQueueTagSmartReaderTagEventMqtt.TryDequeue(out smartReaderTagReadEvent))
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
                                            "Unexpected error on OnRunPeriodicTagPublisherMqttTasksEvent " + ex.Message);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Unexpected error on OnRunPeriodicTagPublisherMqttTasksEvent " + ex.Message);
                            }

                    _mqttPublisherStopwatch.Restart();
                }
            }



            try
            {
                if (smartReaderTagReadEventAggregated != null && smartReaderTagReadEventsArray != null &&
                    smartReaderTagReadEventsArray.Count > 0)
                {
                    smartReaderTagReadEventAggregated["tag_reads"] = smartReaderTagReadEventsArray;

                    await ProcessMqttJsonTagEventDataAsync(smartReaderTagReadEventAggregated);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error on OnRunPeriodicTagPublisherMqttTasksEvent " + ex.Message);
            }

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error on OnRunPeriodicTagPublisherMqttTasksEvent " + ex.Message);
        }


        _timerTagPublisherMqtt.Enabled = true;
        _timerTagPublisherMqtt.Start();
    }

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

    private async Task ProcessMqttJsonTagEventDataAsync(JObject smartReaderTagEventData)
    {
        try
        {
            var jsonParam = JsonConvert.SerializeObject(smartReaderTagEventData);
            var mqttDataTopic = $"{_standaloneConfigDTO.mqttTagEventsTopic}";

            _mqttClient.PublishAsync(mqttDataTopic, jsonParam);
            _logger.LogInformation($"Data sent: {jsonParam}");
            _ = ProcessGpoErrorPortRecoveryAsync();
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
            var mqttDataTopic = $"{_standaloneConfigDTO.mqttTagEventsTopic}";

            _mqttClient.PublishAsync(mqttDataTopic, jsonParam);
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
                    sb.Append(antennaPort);
                    sb.Append(fieldDelim);
                }

                var antennaZone = (string)tagReads["antennaZone"];
                if (string.Equals("1", _standaloneConfigDTO.includeAntennaZone, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrEmpty(antennaZone))
                {
                    sb.Append(antennaZone);
                    sb.Append(fieldDelim);
                }

                var epc = (string)tagReads["epc"];
                if (!string.IsNullOrEmpty(epc))
                {
                    sb.Append(epc);
                    sb.Append(fieldDelim);
                }

                var firstSeenTimestamp = (string)tagReads["firstSeenTimestamp"];
                if (string.Equals("1", _standaloneConfigDTO.includeFirstSeenTimestamp,
                        StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(firstSeenTimestamp))
                {
                    sb.Append(firstSeenTimestamp);
                    sb.Append(fieldDelim);
                }

                var peakRssi = (string)tagReads["peakRssi"];
                if (string.Equals("1", _standaloneConfigDTO.includePeakRssi, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrEmpty(peakRssi))
                {
                    sb.Append(peakRssi);
                    sb.Append(fieldDelim);
                }

                var tid = (string)tagReads["tid"];
                if (string.Equals("1", _standaloneConfigDTO.includeTid, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrEmpty(tid))
                {
                    sb.Append(tid);
                    sb.Append(fieldDelim);
                }

                var rfPhase = (string)tagReads["rfPhase"];
                if (string.Equals("1", _standaloneConfigDTO.includeRFPhaseAngle, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrEmpty(rfPhase))
                {
                    sb.Append(rfPhase);
                    sb.Append(fieldDelim);
                }

                var rfDoppler = (string)tagReads["frequency"];
                if (string.Equals("1", _standaloneConfigDTO.includeRFDopplerFrequency,
                        StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(rfDoppler))
                {
                    sb.Append(rfDoppler);
                    sb.Append(fieldDelim);
                }

                var rfChannel = (string)tagReads["rfChannel"];
                if (string.Equals("1", _standaloneConfigDTO.includeRFChannelIndex,
                        StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(rfChannel))
                {
                    sb.Append(rfChannel);
                    sb.Append(fieldDelim);
                }


                if (string.Equals("1", _standaloneConfigDTO.includeGpiEvent, StringComparison.OrdinalIgnoreCase))
                {
                    var gpi1Status = (string)tagReads["gpi1Status"];
                    if (!string.IsNullOrEmpty(gpi1Status))
                    {
                        sb.Append(gpi1Status);
                        sb.Append(fieldDelim);
                    }
                    else
                    {
                        sb.Append("0");
                        sb.Append(fieldDelim);
                    }

                    var gpi2Status = (string)tagReads["gpi2Status"];
                    if (!string.IsNullOrEmpty(gpi2Status))
                    {
                        sb.Append(gpi2Status);
                        sb.Append(fieldDelim);
                    }
                    else
                    {
                        sb.Append("0");
                        sb.Append(fieldDelim);
                    }

                    var gpi3Status = (string)tagReads["gpi3Status"];
                    if (!string.IsNullOrEmpty(gpi3Status))
                    {
                        sb.Append(gpi3Status);
                        sb.Append(fieldDelim);
                    }
                    else
                    {
                        sb.Append("0");
                        sb.Append(fieldDelim);
                    }

                    var gpi4Status = (string)tagReads["gpi4Status"];
                    if (!string.IsNullOrEmpty(gpi4Status))
                    {
                        sb.Append(gpi4Status);
                        sb.Append(fieldDelim);
                    }
                    else
                    {
                        sb.Append("0");
                        sb.Append(fieldDelim);
                    }
                }

                var tagDataKeyName = (string)tagReads["tagDataKeyName"];
                if (string.Equals("1", _standaloneConfigDTO.parseSgtinEnabled, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrEmpty(tagDataKeyName))
                    if (string.Equals("1", _standaloneConfigDTO.parseSgtinIncludeKeyType,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        sb.Append(tagDataKeyName);
                        sb.Append(fieldDelim);
                    }

                var tagDataKey = (string)tagReads["tagDataKey"];
                if (string.Equals("1", _standaloneConfigDTO.parseSgtinEnabled, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrEmpty(tagDataKey))
                {
                    sb.Append(tagDataKey);
                    sb.Append(fieldDelim);
                }

                var tagDataSerial = (string)tagReads["tagDataSerial"];
                if (string.Equals("1", _standaloneConfigDTO.parseSgtinEnabled, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrEmpty(tagDataSerial))
                    if (string.Equals("1", _standaloneConfigDTO.parseSgtinIncludeSerial,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        sb.Append(tagDataSerial);
                        sb.Append(fieldDelim);
                    }

                var tagDataPureIdentity = (string)tagReads["tagDataPureIdentity"];
                if (string.Equals("1", _standaloneConfigDTO.parseSgtinEnabled, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrEmpty(tagDataSerial))
                    if (string.Equals("1", _standaloneConfigDTO.parseSgtinIncludePureIdentity,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        sb.Append(tagDataPureIdentity);
                        sb.Append(fieldDelim);
                    }

                var readerName = (string)smartReaderTagEventData["readerName"];
                if (!string.IsNullOrEmpty(_standaloneConfigDTO.readerName))
                {
                    sb.Append(readerName);
                    sb.Append(fieldDelim);
                }

                var site = (string)smartReaderTagEventData["site"];
                if (!string.IsNullOrEmpty(_standaloneConfigDTO.site))
                {
                    sb.Append(site);
                    sb.Append(fieldDelim);
                }

                if (string.Equals("1", _standaloneConfigDTO.customField1Enabled, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrEmpty(_standaloneConfigDTO.customField1Name))
                {
                    var customField1Value = (string)smartReaderTagEventData[_standaloneConfigDTO.customField1Name];
                    if (!string.IsNullOrEmpty(customField1Value))
                    {
                        sb.Append(customField1Value);
                        sb.Append(fieldDelim);
                    }
                }


                if (string.Equals("1", _standaloneConfigDTO.customField2Enabled, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrEmpty(_standaloneConfigDTO.customField2Name))
                {
                    var customField2Value = (string)smartReaderTagEventData[_standaloneConfigDTO.customField2Name];
                    if (!string.IsNullOrEmpty(customField2Value))
                    {
                        sb.Append(customField2Value);
                        sb.Append(fieldDelim);
                    }
                }

                if (string.Equals("1", _standaloneConfigDTO.customField3Enabled, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrEmpty(_standaloneConfigDTO.customField3Name))
                {
                    var customField3Value = (string)smartReaderTagEventData[_standaloneConfigDTO.customField3Name];
                    if (!string.IsNullOrEmpty(customField3Value))
                    {
                        sb.Append(customField3Value);
                        sb.Append(fieldDelim);
                    }
                }

                if (string.Equals("1", _standaloneConfigDTO.customField4Enabled, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrEmpty(_standaloneConfigDTO.customField4Name))
                {
                    var customField4Value = (string)smartReaderTagEventData[_standaloneConfigDTO.customField4Name];
                    if (!string.IsNullOrEmpty(customField4Value))
                    {
                        sb.Append(customField4Value);
                        sb.Append(fieldDelim);
                    }
                }

                sb.Append(lineEnd);
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
                    string udpRemoteIpAddress = null;


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
                        _ = ProcessGpoErrorPortRecoveryAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex,
                            "Unexpected error on ProcessUdpDataTagEventDataAsync for UDP " + udpClient.Key + " " +
                            ex.Message);
                        ProcessGpoErrorPortAsync();
                        //_messageQueueTagSmartReaderTagEventSocketServerRetry.Enqueue(smartReaderTagEventData);
                    }
                //_udpSocketServer.Send(line);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error on ProcessUdpDataTagEventDataAsync for UDP " + ex.Message);
                ProcessGpoErrorPortAsync();
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
            ProcessGpoErrorPortAsync();
            //_logger.LogInformation("Unexpected error on ProcessUdpDataTagEventDataAsync " + ex.Message, SeverityType.Error);
        }
    }

    private async Task ProcessHttpJsonPostTagEventDataAsync(JObject smartReaderTagEventData)
    {
        try
        {
            string username = null;
            string password = null;
            string httpAuthenticationHeader = null;
            string httpAuthenticationHeaderValue = null;
            string bearerToken = null;
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
            else
            {
                _ = ProcessGpoErrorPortRecoveryAsync();
            }

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
            ProcessGpoErrorPortAsync();
        }

        try
        {
            _httpTimerStopwatch.Restart();
        }
        catch (Exception ex)
        {
            Console.WriteLine("Unexpected error on ProcessTagEventData restarting _timerStopwatch" + ex.Message);
            _logger.LogError(ex, "Unexpected error on ProcessTagEventData restarting _timerStopwatch" + ex.Message);
            ProcessGpoErrorPortAsync();
        }
    }

    private async Task ProcessSocketJsonTagEventDataAsync(JObject smartReaderTagEventData)
    {
        try
        {
            if (smartReaderTagEventData == null)
                return;

            var line = ExtractLineFromJsonObject(smartReaderTagEventData);
            if (_socketServer != null)
            {
                var clients = _socketServer.GetClients();
                if (clients != null)
                    //string jsonParam = JsonConvert.SerializeObject(smartReaderTagEventData);
                    foreach (var client in clients)
                        try
                        {
                            _socketServer.Send(client, Encoding.UTF8.GetBytes(line));
                            _ = ProcessGpoErrorPortRecoveryAsync();
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex,
                                "Unexpected error on ProcessSocketJsonTagEventDataAsync " + ex.Message);
                            ProcessGpoErrorPortAsync();
                            //_messageQueueTagSmartReaderTagEventSocketServerRetry.Enqueue(smartReaderTagEventData);
                        }
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
            _logger.LogError(ex, "Unexpected error on ProcessSocketJsonTagEventDataAsync " + ex.Message);
            ProcessGpoErrorPortAsync();
        }
    }

    private async Task ProcessUsbDriveJsonTagEventDataAsync(JObject smartReaderTagEventData)
    {
        try
        {
            if (smartReaderTagEventData == null)
                return;

            var line = ExtractLineFromJsonObject(smartReaderTagEventData);
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
                    _ = ProcessGpoErrorPortRecoveryAsync();
                }
                else
                {
                    _logger.LogWarning("### Publisher USB Drive >>> path " + R700UsbDrivePath + " not found.");
                    ProcessGpoErrorPortAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error on ProcessSocketJsonTagEventDataAsync " + ex.Message);
                ProcessGpoErrorPortAsync();

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
            _logger.LogError(ex, "Unexpected error on ProcessSocketJsonTagEventDataAsync " + ex.Message,
                SeverityType.Error);
            ProcessGpoErrorPortAsync();
        }
    }

    private static string ExtractLineFromJsonObject(JObject smartReaderTagEventData)
    {
        var sb = new StringBuilder();
        var fieldDelim = ",";
        var lineEnd = "\r\n";
        if (string.Equals("0", _standaloneConfigDTO.fieldDelim, StringComparison.OrdinalIgnoreCase)) fieldDelim = "";
        if (string.Equals("1", _standaloneConfigDTO.fieldDelim, StringComparison.OrdinalIgnoreCase)) fieldDelim = ",";
        if (string.Equals("2", _standaloneConfigDTO.fieldDelim, StringComparison.OrdinalIgnoreCase)) fieldDelim = " ";
        if (string.Equals("3", _standaloneConfigDTO.fieldDelim, StringComparison.OrdinalIgnoreCase)) fieldDelim = "\t";
        if (string.Equals("4", _standaloneConfigDTO.fieldDelim, StringComparison.OrdinalIgnoreCase)) fieldDelim = ";";


        if (string.Equals("0", _standaloneConfigDTO.lineEnd, StringComparison.OrdinalIgnoreCase)) lineEnd = "";
        if (string.Equals("1", _standaloneConfigDTO.lineEnd, StringComparison.OrdinalIgnoreCase)) lineEnd = "\n";
        if (string.Equals("2", _standaloneConfigDTO.lineEnd, StringComparison.OrdinalIgnoreCase)) lineEnd = "\r\n";
        if (string.Equals("3", _standaloneConfigDTO.lineEnd, StringComparison.OrdinalIgnoreCase)) lineEnd = "\r";

        if (smartReaderTagEventData.ContainsKey("tag_reads"))
        {
            var receivedBarcode = "";

            //var tagReads = smartReaderTagEventData.GetValue("tag_reads").FirstOrDefault();
            foreach (var tagReads in smartReaderTagEventData.GetValue("tag_reads").ToList())
            {
                var antennaPort = (string)tagReads["antennaPort"];
                if (string.Equals("1", _standaloneConfigDTO.includeAntennaPort, StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrEmpty(antennaPort))
                    {
                        sb.Append(antennaPort);
                        sb.Append(fieldDelim);
                    }
                    else
                    {
                        sb.Append("");
                        sb.Append(fieldDelim);
                    }
                }


                var antennaZone = (string)tagReads["antennaZone"];
                if (string.Equals("1", _standaloneConfigDTO.includeAntennaZone, StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrEmpty(antennaZone))
                    {
                        sb.Append(antennaZone);
                        sb.Append(fieldDelim);
                    }
                    else
                    {
                        sb.Append("");
                        sb.Append(fieldDelim);
                    }
                }

                var epc = (string)tagReads["epc"];
                if (!string.IsNullOrEmpty(epc))
                {
                    sb.Append(epc);
                    sb.Append(fieldDelim);
                }

                var firstSeenTimestamp = (string)tagReads["firstSeenTimestamp"];
                if (string.Equals("1", _standaloneConfigDTO.includeFirstSeenTimestamp,
                        StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrEmpty(firstSeenTimestamp))
                    {
                        sb.Append(firstSeenTimestamp);
                        sb.Append(fieldDelim);
                    }
                    else
                    {
                        sb.Append("");
                        sb.Append(fieldDelim);
                    }
                }

                var peakRssi = (string)tagReads["peakRssi"];
                if (string.Equals("1", _standaloneConfigDTO.includePeakRssi, StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrEmpty(peakRssi))
                    {
                        sb.Append(peakRssi);
                        sb.Append(fieldDelim);
                    }
                    else
                    {
                        sb.Append("");
                        sb.Append(fieldDelim);
                    }
                }


                var tid = (string)tagReads["tid"];
                if (string.Equals("1", _standaloneConfigDTO.includeTid, StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrEmpty(tid))
                    {
                        sb.Append(tid);
                        sb.Append(fieldDelim);
                    }
                    else
                    {
                        sb.Append("");
                        sb.Append(fieldDelim);
                    }
                }


                var rfPhase = (string)tagReads["rfPhase"];
                if (string.Equals("1", _standaloneConfigDTO.includeRFPhaseAngle, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrEmpty(rfPhase))
                {
                    sb.Append(rfPhase);
                    sb.Append(fieldDelim);
                }

                var rfDoppler = (string)tagReads["rfDoppler"];
                if (string.Equals("1", _standaloneConfigDTO.includeRFDopplerFrequency,
                        StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(rfDoppler))
                {
                    sb.Append(rfDoppler);
                    sb.Append(fieldDelim);
                }

                var rfChannel = (string)tagReads["frequency"];
                if (string.Equals("1", _standaloneConfigDTO.includeRFChannelIndex,
                        StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(rfChannel))
                {
                    sb.Append(rfChannel);
                    sb.Append(fieldDelim);
                }


                if (string.Equals("1", _standaloneConfigDTO.includeGpiEvent, StringComparison.OrdinalIgnoreCase))
                {
                    var gpi1Status = (string)tagReads["gpi1Status"];
                    if (!string.IsNullOrEmpty(gpi1Status))
                    {
                        sb.Append(gpi1Status);
                        sb.Append(fieldDelim);
                    }
                    else
                    {
                        sb.Append("0");
                        sb.Append(fieldDelim);
                    }

                    var gpi2Status = (string)tagReads["gpi2Status"];
                    if (!string.IsNullOrEmpty(gpi2Status))
                    {
                        sb.Append(gpi2Status);
                        sb.Append(fieldDelim);
                    }
                    else
                    {
                        sb.Append("0");
                        sb.Append(fieldDelim);
                    }

                    var gpi3Status = (string)tagReads["gpi3Status"];
                    if (!string.IsNullOrEmpty(gpi3Status))
                    {
                        sb.Append(gpi3Status);
                        sb.Append(fieldDelim);
                    }
                    else
                    {
                        sb.Append("0");
                        sb.Append(fieldDelim);
                    }

                    var gpi4Status = (string)tagReads["gpi4Status"];
                    if (!string.IsNullOrEmpty(gpi4Status))
                    {
                        sb.Append(gpi4Status);
                        sb.Append(fieldDelim);
                    }
                    else
                    {
                        sb.Append("0");
                        sb.Append(fieldDelim);
                    }
                }

                var tagDataKeyName = (string)tagReads["tagDataKeyName"];
                if (string.Equals("1", _standaloneConfigDTO.parseSgtinEnabled, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrEmpty(tagDataKeyName))
                    if (string.Equals("1", _standaloneConfigDTO.parseSgtinIncludeKeyType,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        sb.Append(tagDataKeyName);
                        sb.Append(fieldDelim);
                    }

                var tagDataKey = (string)tagReads["tagDataKey"];
                if (string.Equals("1", _standaloneConfigDTO.parseSgtinEnabled, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrEmpty(tagDataKey))
                {
                    sb.Append(tagDataKey);
                    sb.Append(fieldDelim);
                }

                var tagDataSerial = (string)tagReads["tagDataSerial"];
                if (string.Equals("1", _standaloneConfigDTO.parseSgtinEnabled, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrEmpty(tagDataSerial))
                    if (string.Equals("1", _standaloneConfigDTO.parseSgtinIncludeSerial,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        sb.Append(tagDataSerial);
                        sb.Append(fieldDelim);
                    }

                var tagDataPureIdentity = (string)tagReads["tagDataPureIdentity"];
                if (string.Equals("1", _standaloneConfigDTO.parseSgtinEnabled, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrEmpty(tagDataPureIdentity))
                    if (string.Equals("1", _standaloneConfigDTO.parseSgtinIncludePureIdentity,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        sb.Append(tagDataPureIdentity);
                        sb.Append(fieldDelim);
                    }

                var readerName = (string)smartReaderTagEventData["readerName"];
                if (!string.IsNullOrEmpty(_standaloneConfigDTO.readerName))
                {
                    if (!string.IsNullOrEmpty(readerName))
                    {
                        sb.Append(readerName);
                        sb.Append(fieldDelim);
                    }
                    else
                    {
                        sb.Append("");
                        sb.Append(fieldDelim);
                    }
                }

                var site = (string)smartReaderTagEventData["site"];
                if (!string.IsNullOrEmpty(_standaloneConfigDTO.site))
                {
                    if (string.IsNullOrEmpty(site))
                    {
                        sb.Append(site);
                        sb.Append(fieldDelim);
                    }
                    else
                    {
                        sb.Append("");
                        sb.Append(fieldDelim);
                    }
                }

                if (string.Equals("1", _standaloneConfigDTO.customField1Enabled, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrEmpty(_standaloneConfigDTO.customField1Name))
                {
                    var customField1Value = (string)smartReaderTagEventData[_standaloneConfigDTO.customField1Name];
                    if (!string.IsNullOrEmpty(customField1Value))
                    {
                        sb.Append(customField1Value);
                        sb.Append(fieldDelim);
                    }
                }


                if (string.Equals("1", _standaloneConfigDTO.customField2Enabled, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrEmpty(_standaloneConfigDTO.customField2Name))
                {
                    var customField2Value = (string)smartReaderTagEventData[_standaloneConfigDTO.customField2Name];
                    if (!string.IsNullOrEmpty(customField2Value))
                    {
                        sb.Append(customField2Value);
                        sb.Append(fieldDelim);
                    }
                }

                if (string.Equals("1", _standaloneConfigDTO.customField3Enabled, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrEmpty(_standaloneConfigDTO.customField3Name))
                {
                    var customField3Value = (string)smartReaderTagEventData[_standaloneConfigDTO.customField3Name];
                    if (!string.IsNullOrEmpty(customField3Value))
                    {
                        sb.Append(customField3Value);
                        sb.Append(fieldDelim);
                    }
                }

                if (string.Equals("1", _standaloneConfigDTO.customField4Enabled, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrEmpty(_standaloneConfigDTO.customField4Name))
                {
                    var customField4Value = (string)smartReaderTagEventData[_standaloneConfigDTO.customField4Name];
                    if (!string.IsNullOrEmpty(customField4Value))
                    {
                        sb.Append(customField4Value);
                        sb.Append(fieldDelim);
                    }
                }

                try
                {
                    if (smartReaderTagEventData.ContainsKey("barcode"))
                        receivedBarcode = (string)smartReaderTagEventData["barcode"];
                    else if (((JObject)tagReads).ContainsKey("barcode"))
                        receivedBarcode = ((JObject)tagReads)["barcode"].Value<string>();
                    if ((string.Equals("1", _standaloneConfigDTO.enableBarcodeSerial,
                             StringComparison.OrdinalIgnoreCase)
                         || string.Equals("1", _standaloneConfigDTO.enableBarcodeTcp,
                             StringComparison.OrdinalIgnoreCase)))
                    {

                        if (!string.IsNullOrEmpty(receivedBarcode))
                        {
                            sb.Append(receivedBarcode);
                            sb.Append(fieldDelim);
                        }
                        else
                        {
                            sb.Append("");
                            sb.Append(fieldDelim);
                        }
                    }
                }
                catch (Exception)
                {
                }

                sb.Append(lineEnd);
            }
        }

        var line = sb.ToString();
        line = line.Replace(fieldDelim + lineEnd, lineEnd);
        return line;
    }

    private async void OnRunPeriodicTasksJobManagerEvent(object sender, ElapsedEventArgs e)
    {
        PeriodicTasksTimerInventoryData.Enabled = false;
        PeriodicTasksTimerInventoryData.Stop();
        //await periodicJobTaskLock.WaitAsync();
        //if (Monitor.TryEnter(_timerPeriodicTasksJobManagerLock))
        //{


        try
        {
            if (_standaloneConfigDTO != null
                && !string.IsNullOrEmpty(_standaloneConfigDTO.httpPostEnabled)
                && string.Equals("1", _standaloneConfigDTO.httpPostEnabled, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrEmpty(_standaloneConfigDTO.enablePlugin)
                && string.Equals("0", _standaloneConfigDTO.enablePlugin, StringComparison.OrdinalIgnoreCase))
                if (_messageQueueTagSmartReaderTagEventHttpPost.Count > 0 && _httpTimerStopwatch.Elapsed.Minutes > 10)
                {
                    _logger.LogInformation(
                        "OnRunPeriodicTasksJobManagerEvent - Exit due to POST timeout (600 seconds) ");

                    await Task.Delay(TimeSpan.FromSeconds(2));
                    _logger.LogInformation("Restarting process");
                    // Restart the application by spawning a new process with the same arguments
                    var process = Process.GetCurrentProcess();
                    process.StartInfo.WorkingDirectory = Directory.GetCurrentDirectory();
                    Process.Start(process.MainModule.FileName);
                    Environment.Exit(1);
                }
        }
        catch (Exception)
        {
        }

        try
        {
            if (_standaloneConfigDTO != null && !string.IsNullOrEmpty(_standaloneConfigDTO.httpAuthenticationType)
                                             && string.Equals("BEARER", _standaloneConfigDTO.httpAuthenticationType,
                                                 StringComparison.OrdinalIgnoreCase)
                                             && string.Equals("1",
                                                 _standaloneConfigDTO.httpAuthenticationTokenApiEnabled,
                                                 StringComparison.OrdinalIgnoreCase))
                if (_stopwatchBearerToken.IsRunning && _stopwatchBearerToken.Elapsed.Minutes > 30)
                {
                    _stopwatchBearerToken.Stop();
                    _stopwatchBearerToken.Reset();
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
                    }

                    _stopwatchBearerToken.Start();
                }
            //var hasLock = false;
            //try
            //{
            //    Monitor.TryEnter(_locker, ref hasLock);
            //    if (!hasLock)
            //    {
            //        return;
            //    }


            try
            {
                var licenseToSet = "";
                if (_plugins != null
                       && _plugins.Count > 0
                       && !_plugins.ContainsKey("LICENSE"))
                {
                    try
                    {
                        licenseToSet = GetLicenseFromDb().Result;
                    }
                    catch (Exception)
                    {
                    }
                }


                try
                {
                    //
                    var configModel = GetConfigDtoFromDb().Result;
                    //if (configModel != null && File.Exists("/customer/config/config-changed.txt"))
                    if (configModel != null)
                        //File.Delete("/customer/config/config-changed.txt");
                        //var configHelper = new IniConfigHelper();
                        //var currentDtoFile = configHelper.LoadDtoFromFile();
                        //Utils.CalculateMD5(configModel.Value);
                        if (configModel != null)
                        {
                            //var compareLogic = new CompareLogic();
                            //var newConfigDataToCompare = configHelper.CleanupUrlEncoding(configModel);
                            var newConfigDataToCompare = StandaloneConfigDTO.CleanupUrlEncoding(configModel);
                            if (newConfigDataToCompare != null)
                                //ComparisonResult result = compareLogic.Compare(newConfigDataToCompare, currentDtoFile);
                                //These will be different, write out the differences
                                //if (!result.AreEqual)
                                if (_standaloneConfigDTO != null &&
                                    !_standaloneConfigDTO.Equals(newConfigDataToCompare))
                                {
                                    _logger.LogInformation("Saving new config: ");
                                    _logger.LogInformation("Serial: " + configModel.readerSerial);
                                    _logger.LogInformation("Name: " + configModel.readerName);
                                    _logger.LogInformation("Antenna Ports: " + configModel.antennaPorts);
                                    _logger.LogInformation("TxPower: " + configModel.transmitPower);
                                    _logger.LogInformation("Reader Mode: " + configModel.readerMode);
                                    _logger.LogInformation("Session: " + configModel.session);
                                    if (!string.IsNullOrEmpty(newConfigDataToCompare.antennaPorts.Trim()))
                                    {
                                        //_standaloneConfigDTO = newConfigDataToCompare;
                                        if (!string.IsNullOrEmpty(licenseToSet)) configModel.licenseKey = licenseToSet;

                                        //SaveConfigDtoToDb(_standaloneConfigDTO);
                                        ConfigFileHelper.SaveFile(configModel);

                                        //configHelper.SaveDtoToFile(configModel);
                                        LoadConfig();
                                        //if (_standaloneConfigDTO != null)
                                        //{
                                        //    SaveConfigDtoToDb(_standaloneConfigDTO);
                                        //}
                                        //dbContext.ReaderConfigs.Remove(configModel);
                                        //await dbContext.SaveChangesAsync();
                                    }
                                }
                        }
                }
                catch (Exception ex)
                {
                    _logger.LogInformation(
                        "[OnRunPeriodicTasksJobManagerEvent] READER_CONFIG - Unexpected error. " + ex.Message,
                        SeverityType.Error);
                }

                using var scope = Services.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<RuntimeDb>();
                try
                {
                    try
                    {
                        var currentTime = await _iotDeviceInterfaceClient.DeviceReaderTimeGetAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogInformation(
                        "[OnRunPeriodicTasksJobManagerEvent] GET READER TIME - Unexpected error. ");
                        // exits the app
                        //await Task.Delay(TimeSpan.FromSeconds(2));
                        //_logger.LogInformation("Restarting process");
                        //// Restart the application by spawning a new process with the same arguments
                        //var process = Process.GetCurrentProcess();
                        //process.StartInfo.WorkingDirectory = Directory.GetCurrentDirectory();
                        //Process.Start(process.MainModule.FileName);
                        //Environment.Exit(1);
                        //Environment.Exit(0);
                    }


                    var statusData = await GetReaderStatusAsync();

                    var existingStatusData = await dbContext.ReaderStatus.FindAsync("READER_STATUS");
                    if (existingStatusData != null)
                    {
                        existingStatusData.Value = JsonConvert.SerializeObject(statusData);
                        dbContext.ReaderStatus.Update(existingStatusData);
                        await dbContext.SaveChangesAsync();
                    }
                    else
                    {
                        var statusDatadb = new ReaderStatus();
                        statusDatadb.Id = "READER_STATUS";
                        statusDatadb.Value = JsonConvert.SerializeObject(statusData);
                        dbContext.ReaderStatus.Add(statusDatadb);
                        await dbContext.SaveChangesAsync();
                    }

                    try
                    {
                        if (string.Equals("1", _standaloneConfigDTO.isEnabled, StringComparison.OrdinalIgnoreCase) &&
                            _isStarted)
                            try
                            {
                                var pauseRequest = Path.Combine("/customer", "pause-request.txt");
                                if (!File.Exists(pauseRequest))
                                    if (statusData.Any() && string.Equals("STOPPED", statusData.FirstOrDefault().Status,
                                            StringComparison.OrdinalIgnoreCase))
                                        SaveStartCommandToDb();
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Unexpected error. " + ex.Message);
                                //Console.WriteLine("Unexpected error. " + ex.Message);
                            }
                    }
                    catch (Exception exReaderStatus)
                    {
                        _logger.LogError(exReaderStatus,
                            "Unexpected error checking inventory status on OnRunPeriodicTasksJobManagerEvent " +
                            exReaderStatus.Message);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogInformation("Unexpected error. " + ex.Message, SeverityType.Error);
                    try
                    {
                        if (!string.Equals("127.0.0.1", _readerAddress, StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals("localhost", _readerAddress, StringComparison.OrdinalIgnoreCase))
                        {
                            if (ex.Message.Contains("Timeout"))
                            {
                                if (_readerAddress.EndsWith(".local"))
                                {
                                    _readerAddress = "169.254.1.1";
                                }
                                else
                                {
                                    // Get the current build configuration
                                    string buildConfiguration = "Release"; // Default to Debug if unable to determine
#if DEBUG
                                    buildConfiguration = "Debug";
#endif
                                    _readerAddress = _configuration.GetValue<string>("ReaderInfo:Address");
                                    // Get the value based on the build configuration
                                    if ("Debug".Equals(buildConfiguration))
                                    {
                                        _readerAddress = _configuration.GetValue<string>("ReaderInfo:DebugAddress") ?? _readerAddress;
                                    }
                                }

                                _iotDeviceInterfaceClient = new R700IotReader(_readerAddress, "", true, true,
                                    _readerUsername, _readerPassword, 0, _proxyAddress, _proxyPort);
                            }
                            else if (ex.Message.Contains("No route to host"))
                            {
                                // Get the current build configuration
                                string buildConfiguration = "Release"; // Default to Debug if unable to determine
#if DEBUG
                                buildConfiguration = "Debug";
#endif
                                _readerAddress = _configuration.GetValue<string>("ReaderInfo:Address");
                                // Get the value based on the build configuration
                                if ("Debug".Equals(buildConfiguration))
                                {
                                    _readerAddress = _configuration.GetValue<string>("ReaderInfo:DebugAddress") ?? _readerAddress;
                                }

                                _iotDeviceInterfaceClient = new R700IotReader(_readerAddress, "", true, true,
                                    _readerUsername, _readerPassword, 0, _proxyAddress, _proxyPort);
                            }
                        }
                        else if (ex.Message.Contains("Status: 404"))
                        {
                            // exits the app

                            await Task.Delay(TimeSpan.FromSeconds(2));
                            _logger.LogInformation("Restarting process");
                            // Restart the application by spawning a new process with the same arguments
                            var process = Process.GetCurrentProcess();
                            process.StartInfo.WorkingDirectory = Directory.GetCurrentDirectory();
                            Process.Start(process.MainModule.FileName);
                            Environment.Exit(1);
                        }
                    }
                    catch (Exception)
                    {
                    }
                }

                try
                {
                    // recupera comandos solicitados
                    var commands = dbContext.ReaderCommands
                        .OrderBy(f => f.Timestamp)
                        .ToList();

                    if (commands.Any())
                        for (var i = 0; i < commands.Count; i++)
                            try
                            {
                                if (string.Equals("START_INVENTORY", commands[i].Id,
                                        StringComparison.OrdinalIgnoreCase))
                                {
                                    try
                                    {
                                        var pauseRequest = Path.Combine("/customer", "pause-request.txt");
                                        if (File.Exists(pauseRequest)) File.Delete(pauseRequest);
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger.LogError(ex, "Unexpected error. " + ex.Message);
                                        //Console.WriteLine("Unexpected error. " + ex.Message);
                                    }

                                    await StartTasksAsync();
                                    dbContext.ReaderCommands.Remove(commands[i]);
                                    await dbContext.SaveChangesAsync();
                                }

                                if (string.Equals("STOP_INVENTORY", commands[i].Id, StringComparison.OrdinalIgnoreCase))
                                {
                                    if (!string.IsNullOrEmpty(_standaloneConfigDTO.enableTagEventsListBatch)
                                     && string.Equals("1", _standaloneConfigDTO.enableTagEventsListBatch,
                                     StringComparison.OrdinalIgnoreCase))
                                    {
                                        _logger.LogInformation("Cleaning up batch EPC list.");
                                        try
                                        {
                                            _smartReaderTagEventsListBatch.Clear();
                                        }
                                        catch (Exception)
                                        {

                                        }

                                    }
                                    try
                                    {
                                        var pauseRequest = Path.Combine("/customer", "pause-request.txt");
                                        File.WriteAllText(pauseRequest, "pause");
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger.LogError(ex, "Unexpected error. " + ex.Message);
                                        //Console.WriteLine("Unexpected error. " + ex.Message);
                                    }

                                    try
                                    {
                                        await StopPresetAsync();
                                    }
                                    catch (Exception)
                                    {
                                    }

                                    await StopTasksAsync();
                                    dbContext.ReaderCommands.Remove(commands[i]);
                                    await dbContext.SaveChangesAsync();
                                }

                                if (string.Equals("START_PRESET", commands[i].Id, StringComparison.OrdinalIgnoreCase))
                                {
                                    try
                                    {
                                        var pauseRequest = Path.Combine("/customer", "pause-request.txt");
                                        if (File.Exists(pauseRequest)) File.Delete(pauseRequest);
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger.LogError(ex, "Unexpected error. " + ex.Message);
                                        //Console.WriteLine("Unexpected error. " + ex.Message);
                                    }

                                    await StartPresetAsync();
                                    dbContext.ReaderCommands.Remove(commands[i]);
                                    await dbContext.SaveChangesAsync();
                                }

                                if (string.Equals("STOP_PRESET", commands[i].Id, StringComparison.OrdinalIgnoreCase))
                                {
                                    if (!string.IsNullOrEmpty(_standaloneConfigDTO.enableTagEventsListBatch)
                                     && string.Equals("1", _standaloneConfigDTO.enableTagEventsListBatch,
                                     StringComparison.OrdinalIgnoreCase))
                                    {
                                        _logger.LogInformation("Cleaning up batch EPC list.");
                                        try
                                        {
                                            _smartReaderTagEventsListBatch.Clear();
                                            _smartReaderTagEventsListBatchOnUpdate.Clear();
                                        }
                                        catch (Exception)
                                        {

                                        }

                                    }
                                    try
                                    {
                                        var pauseRequest = Path.Combine("/customer", "pause-request.txt");
                                        File.WriteAllText(pauseRequest, "pause");
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger.LogError(ex, "Unexpected error. " + ex.Message);
                                        //Console.WriteLine("Unexpected error. " + ex.Message);
                                    }

                                    await StopPresetAsync();
                                    dbContext.ReaderCommands.Remove(commands[i]);
                                    await dbContext.SaveChangesAsync();
                                }

                                if (string.Equals("MODE_COMMAND", commands[i].Id, StringComparison.OrdinalIgnoreCase))
                                {
                                    try
                                    {
                                        ProcessModeJsonCommand(commands[i].Value);
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger.LogError(ex, "Unexpected error. " + ex.Message);

                                    }
                                    await StopPresetAsync();
                                    dbContext.ReaderCommands.Remove(commands[i]);
                                    await dbContext.SaveChangesAsync();
                                }

                                if (commands[i].Id.StartsWith("SET_GPO_"))
                                {
                                    var gpoPortParts = commands[i].Id.Split("_");
                                    var gpoPort = int.Parse(gpoPortParts[2]);
                                    var gpoPortStatus = bool.Parse(commands[i].Value);
                                    await SetGpoPortAsync(gpoPort, gpoPortStatus);
                                    dbContext.ReaderCommands.Remove(commands[i]);
                                    await dbContext.SaveChangesAsync();
                                }

                                if (commands[i].Id.StartsWith("CLEAN_EPC_SOFTWARE_HISTORY_FILTERS"))
                                {
                                    try
                                    {
                                        _readEpcs.Clear();
                                        _softwareFilterReadCountTimeoutDictionary.Clear();
                                        _knowTagsForSoftwareFilterWindowSec.Clear();
                                    }
                                    catch (Exception)
                                    {
                                    }

                                    dbContext.ReaderCommands.Remove(commands[i]);
                                    await dbContext.SaveChangesAsync();
                                }

                                if (commands[i].Id.StartsWith("UPGRADE_SYSTEM_IMAGE"))
                                {
                                    try
                                    {
                                        string commandValue = commands[i].Value;
                                        dbContext.ReaderCommands.Remove(commands[i]);
                                        await dbContext.SaveChangesAsync();

                                        var cmdPayload = JsonConvert.DeserializeObject<Dictionary<object, object>>(commandValue);

                                        long maxRetries = 1;
                                        long timeoutInMinutes = 3;
                                        var remoteUrl = "";
                                        if (cmdPayload.ContainsKey("remoteUrl"))
                                        {
                                            remoteUrl = (string)cmdPayload["remoteUrl"];
                                        }

                                        if (cmdPayload.ContainsKey("timeoutInMinutes"))
                                        {
                                            timeoutInMinutes = (long)cmdPayload["timeoutInMinutes"];
                                        }

                                        if (cmdPayload.ContainsKey("maxRetries"))
                                        {
                                            maxRetries = (long)cmdPayload["maxRetries"];
                                        }
                                        UpgradeSystemImage(remoteUrl, timeoutInMinutes, maxRetries);
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger.LogWarning(ex, "Unexpected error parsing upgrade command. " + ex.Message);
                                    }


                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Unexpected error. " + ex.Message);
                            }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error. " + ex.Message);
                }

                try
                {
                    var serialData = await GetSerialAsync();
                    var serialFromReader = "";

                    try
                    {
                        if (serialData.Any()
                            && serialData.FirstOrDefault() != null
                            && !string.IsNullOrEmpty(serialData.FirstOrDefault().SerialNumber))
                            serialFromReader = serialData.FirstOrDefault().SerialNumber;
                        if (string.IsNullOrEmpty(DeviceId))
                            if (!string.IsNullOrEmpty(serialFromReader))
                                DeviceId = serialFromReader;

                        if (string.IsNullOrEmpty(DeviceId))
                            if (!string.IsNullOrEmpty(serialFromReader))
                                DeviceIdMqtt = serialFromReader + _standaloneConfigDTO.readerName;

                        if (!string.IsNullOrEmpty(serialFromReader) && !string.Equals(DeviceId, serialFromReader,
                                StringComparison.OrdinalIgnoreCase)) DeviceId = serialFromReader;

                        try
                        {
                            if (!string.IsNullOrEmpty(DeviceId))
                            {
                                if (_standaloneConfigDTO != null
                                    && (string.IsNullOrEmpty(_standaloneConfigDTO.readerSerial)
                                        || !string.Equals(DeviceId, _standaloneConfigDTO.readerSerial)))
                                {
                                    _standaloneConfigDTO.readerSerial = DeviceId;

                                    try
                                    {
                                        //var configHelper = new IniConfigHelper();
                                        //configHelper.SaveDtoToFile(_standaloneConfigDTO);
                                        SaveConfigDtoToDb(_standaloneConfigDTO);
                                        _logger.LogInformation("Config updated with serial. " +
                                                               _standaloneConfigDTO.readerSerial);
                                    }
                                    catch (Exception)
                                    {
                                    }
                                }

                                if (string.IsNullOrEmpty(_expectedLicense))
                                    _expectedLicense = Utils.CreateMD5Hash("sM@RTrEADER2022-" + DeviceId);
                            }
                        }
                        catch (Exception)
                        {
                        }
                    }
                    catch (Exception)
                    {
                    }


                    var serialDatadb = new ReaderStatus();
                    serialDatadb.Value = JsonConvert.SerializeObject(serialData);
                    var existingSerialData = await dbContext.ReaderStatus.FindAsync("READER_SERIAL");
                    if (existingSerialData != null)
                    {
                        existingSerialData.Value = JsonConvert.SerializeObject(serialData);
                        dbContext.ReaderStatus.Update(existingSerialData);
                        await dbContext.SaveChangesAsync();
                    }
                    else
                    {
                        serialDatadb.Id = "READER_SERIAL";
                        serialDatadb.Value = JsonConvert.SerializeObject(serialData);
                        dbContext.ReaderStatus.Add(serialDatadb);
                        await dbContext.SaveChangesAsync();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error. " + ex.Message);
                }


                Thread.Sleep(100);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error on OnRunPeriodicTasksJobManagerEvent. " + ex.Message);
            }
            //}
            //finally
            //{
            //    if (hasLock)
            //    {
            //        Monitor.Exit(_locker);
            //    }
            //}

            //    try
            //    {
            //        if (string.Equals("1", _standaloneConfigDTO.heartbeatEnabled, StringComparison.OrdinalIgnoreCase))
            //            if (_stopwatchKeepalive.Elapsed.Seconds > int.Parse(_standaloneConfigDTO.heartbeatPeriodSec))
            //            {
            //                ProcessKeepalive();
            //                _stopwatchKeepalive.Stop();
            //                _stopwatchKeepalive.Reset();
            //                _stopwatchKeepalive.Start();
            //            }
            //    }
            //    catch (Exception ex)
            //    {
            //        _logger.LogError(ex,
            //            "Unexpected error running keepalive manager on OnRunPeriodicTasksJobManagerEvent. " + ex.Message);
            //    }
        }
        catch (Exception)
        {
        }


        //finally
        //{
        //    //Monitor.Exit(_timerPeriodicTasksJobManagerLock);
        //    periodicJobTaskLock.Release();
        //}
        //}
        PeriodicTasksTimerInventoryData.Enabled = true;
        PeriodicTasksTimerInventoryData.Start();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            //_timer = new Timer(1000); // Set up the timer for 1 second
            //_timer.Elapsed += TimerElapsed;
            //_timer.AutoReset = false;
            //_timer.Start();

            //_timerTagInventoryEvent = new Timer(1000); // Set up the timer for 1 second
            //_timerTagInventoryEvent.Elapsed += TimerTagInventoryEventElapsed;
            //_timerTagInventoryEvent.AutoReset = false;
            //_timerTagInventoryEvent.Start();

            LoadConfig();

            try
            {
                if (_standaloneConfigDTO != null) SaveConfigDtoToDb(_standaloneConfigDTO);
                _proxyAddress = _standaloneConfigDTO.networkProxy;
                _proxyPort = int.Parse(_standaloneConfigDTO.networkProxyPort);
            }
            catch (Exception)
            {
            }

            _iotDeviceInterfaceClient =
                new R700IotReader(_readerAddress, "", true, true, _readerUsername, _readerPassword, 0, _proxyAddress, _proxyPort);
            try
            {
                _iotDeviceInterfaceClient.UpdateReaderRfidInterface(
                    RfidInterface.FromJson("{\"rfidInterface\":\"rest\"}"));
            }
            catch (Exception)
            {
            }


            try
            {
                if (_iotDeviceInterfaceClient == null)
                    _iotDeviceInterfaceClient =
                        new R700IotReader(_readerAddress, "", true, true, _readerUsername, _readerPassword, 0, _proxyAddress, _proxyPort);

                await _iotDeviceInterfaceClient.StopPresetAsync();

            }
            catch (Exception)
            {
            }

            try
            {
                if (_iotDeviceInterfaceClient == null)
                    _iotDeviceInterfaceClient =
                        new R700IotReader(_readerAddress, "", true, true, _readerUsername, _readerPassword, 0, _proxyAddress, _proxyPort);

                var _readerSystemInfo = await _iotDeviceInterfaceClient.GetSystemInfoAsync();

            }
            catch (Exception)
            {
            }

            try
            {
                _standaloneConfigDTO.readerSerial = _iotDeviceInterfaceClient.GetStatusAsync().Result.SerialNumber;

                if (_standaloneConfigDTO.readerName.Equals("impinj-xx-xx-xx"))
                {
                    _standaloneConfigDTO.readerName = "impinj" + _iotDeviceInterfaceClient.MacAddress.Replace("-", String.Empty).Replace(":", String.Empty);
                }

            }
            catch (Exception)
            {


            }

            try
            {
                if (_iotDeviceInterfaceClient == null)
                    _iotDeviceInterfaceClient =
                        new R700IotReader(_readerAddress, "", true, true, _readerUsername, _readerPassword, 0, _proxyAddress, _proxyPort);

                var _readerAntennaHubInfo = _iotDeviceInterfaceClient.GetSystemAntennaHubInfoAsync();
                if (_readerAntennaHubInfo != null && _readerAntennaHubInfo.Result != null)
                {
                    if (_readerAntennaHubInfo.Result.Status == AntennaHubInfoStatus.Enabled
                        && _standaloneConfigDTO != null
                        && _standaloneConfigDTO.antennaPorts.Split(",").Length < 32)
                    {
                        var newAntennaPortConfigLine = "";
                        var newAntennaStatesConfigLine = "";
                        var newAntennaZonesConfigLine = "";
                        var newTransmitPowerConfigLine = "";
                        var newReceiveSensitivityConfigLine = "";

                        for (var i = 1; i < 33; i++)
                        {
                            newAntennaPortConfigLine += "" + i;


                            if (i == 1)
                                newAntennaStatesConfigLine += "1";
                            else
                                newAntennaStatesConfigLine += "0";

                            newAntennaZonesConfigLine += "ANT" + i;

                            newTransmitPowerConfigLine += "3000";

                            newReceiveSensitivityConfigLine += "-92";

                            if (i < 32)
                            {
                                newAntennaPortConfigLine += ",";
                                newAntennaStatesConfigLine += ",";
                                newAntennaZonesConfigLine += ",";
                                newTransmitPowerConfigLine += ",";
                                newReceiveSensitivityConfigLine += ",";
                            }
                        }

                        _standaloneConfigDTO.antennaPorts = newAntennaPortConfigLine;
                        _standaloneConfigDTO.antennaStates = newAntennaStatesConfigLine;
                        _standaloneConfigDTO.antennaZones = newAntennaZonesConfigLine;
                        _standaloneConfigDTO.transmitPower = newTransmitPowerConfigLine;
                        _standaloneConfigDTO.receiveSensitivity = newReceiveSensitivityConfigLine;
                        ConfigFileHelper.SaveFile(_standaloneConfigDTO);
                        SaveConfigDtoToDb(_standaloneConfigDTO);
                        //await  Task.Delay(1000);
                    }
                    else
                    {
                        if (_readerAntennaHubInfo.Result.Status == AntennaHubInfoStatus.Disabled
                            && _standaloneConfigDTO != null
                            && _standaloneConfigDTO.antennaPorts.Split(",").Length > 4)
                        {
                            var newAntennaPortConfigLine = "";
                            var newAntennaStatesConfigLine = "";
                            var newAntennaZonesConfigLine = "";
                            var newTransmitPowerConfigLine = "";
                            var newReceiveSensitivityConfigLine = "";

                            for (var i = 1; i < 5; i++)
                            {
                                newAntennaPortConfigLine += "" + i;


                                if (i == 1)
                                    newAntennaStatesConfigLine += "1";
                                else
                                    newAntennaStatesConfigLine += "0";

                                newAntennaZonesConfigLine += "ANT" + i;

                                newTransmitPowerConfigLine += "3000";

                                newReceiveSensitivityConfigLine += "-92";

                                if (i < 4)
                                {
                                    newAntennaPortConfigLine += ",";
                                    newAntennaStatesConfigLine += ",";
                                    newAntennaZonesConfigLine += ",";
                                    newTransmitPowerConfigLine += ",";
                                    newReceiveSensitivityConfigLine += ",";
                                }
                            }

                            _standaloneConfigDTO.antennaPorts = newAntennaPortConfigLine;
                            _standaloneConfigDTO.antennaStates = newAntennaStatesConfigLine;
                            _standaloneConfigDTO.antennaZones = newAntennaZonesConfigLine;
                            _standaloneConfigDTO.transmitPower = newTransmitPowerConfigLine;
                            _standaloneConfigDTO.receiveSensitivity = newReceiveSensitivityConfigLine;
                            ConfigFileHelper.SaveFile(_standaloneConfigDTO);
                            SaveConfigDtoToDb(_standaloneConfigDTO);
                            //await Task.Delay(1000);
                        }
                    }
                }
            }
            catch (Exception)
            {
            }

            if (_standaloneConfigDTO != null)
            {
                if (!string.IsNullOrEmpty(_standaloneConfigDTO.mqttTagEventsTopic))
                    if (_standaloneConfigDTO.mqttTagEventsTopic.Contains("{{deviceId}}"))
                        _standaloneConfigDTO.mqttTagEventsTopic =
                            _standaloneConfigDTO.mqttTagEventsTopic.Replace("{{deviceId}}",
                                _standaloneConfigDTO.readerName);
                if (!string.IsNullOrEmpty(_standaloneConfigDTO.mqttManagementEventsTopic))
                    if (_standaloneConfigDTO.mqttManagementEventsTopic.Contains("{{deviceId}}"))
                        _standaloneConfigDTO.mqttManagementEventsTopic =
                            _standaloneConfigDTO.mqttManagementEventsTopic.Replace("{{deviceId}}",
                                _standaloneConfigDTO.readerName);
                if (!string.IsNullOrEmpty(_standaloneConfigDTO.mqttMetricEventsTopic))
                    if (_standaloneConfigDTO.mqttMetricEventsTopic.Contains("{{deviceId}}"))
                        _standaloneConfigDTO.mqttMetricEventsTopic =
                            _standaloneConfigDTO.mqttMetricEventsTopic.Replace("{{deviceId}}",
                                _standaloneConfigDTO.readerName);
                if (!string.IsNullOrEmpty(_standaloneConfigDTO.mqttManagementCommandTopic))
                    if (_standaloneConfigDTO.mqttManagementCommandTopic.Contains("{{deviceId}}"))
                        _standaloneConfigDTO.mqttManagementCommandTopic =
                            _standaloneConfigDTO.mqttManagementCommandTopic.Replace("{{deviceId}}",
                                _standaloneConfigDTO.readerName);
                if (!string.IsNullOrEmpty(_standaloneConfigDTO.mqttManagementResponseTopic))
                    if (_standaloneConfigDTO.mqttManagementResponseTopic.Contains("{{deviceId}}"))
                        _standaloneConfigDTO.mqttManagementResponseTopic =
                            _standaloneConfigDTO.mqttManagementResponseTopic.Replace("{{deviceId}}",
                                _standaloneConfigDTO.readerName);
                if (!string.IsNullOrEmpty(_standaloneConfigDTO.mqttControlCommandTopic))
                    if (_standaloneConfigDTO.mqttControlCommandTopic.Contains("{{deviceId}}"))
                        _standaloneConfigDTO.mqttControlCommandTopic =
                            _standaloneConfigDTO.mqttControlCommandTopic.Replace("{{deviceId}}",
                                _standaloneConfigDTO.readerName);
                if (!string.IsNullOrEmpty(_standaloneConfigDTO.mqttControlResponseTopic))
                    if (_standaloneConfigDTO.mqttControlResponseTopic.Contains("{{deviceId}}"))
                        _standaloneConfigDTO.mqttControlResponseTopic =
                            _standaloneConfigDTO.mqttControlResponseTopic.Replace("{{deviceId}}",
                                _standaloneConfigDTO.readerName);
                ConfigFileHelper.SaveFile(_standaloneConfigDTO);
                SaveConfigDtoToDb(_standaloneConfigDTO);
            }

            try
            {
                if (_iotDeviceInterfaceClient == null)
                    _iotDeviceInterfaceClient =
                        new R700IotReader(_readerAddress, "", true, true, _readerUsername, _readerPassword, 0, _proxyAddress, _proxyPort);

                var _readerRegionInfo = _iotDeviceInterfaceClient.GetSystemRegionInfoAsync().Result;
                var systemInfo = _iotDeviceInterfaceClient.GetSystemInfoAsync().Result;
                var systemPower = _iotDeviceInterfaceClient.GetSystemPowerAsync().Result;
                bool isPoePlus = false;
                if (systemPower.PowerSource.Equals(Impinj.Atlas.PowerSource.Poeplus))
                {
                    isPoePlus = true;
                }
                List<int> capabilityTxTable = Utils.GetDefaultTxTable(systemInfo.ProductModel, isPoePlus, _readerRegionInfo.OperatingRegion);
                if (_standaloneConfigDTO != null)
                {
                    var currentTxPowersArr = _standaloneConfigDTO.transmitPower.Split(",");
                    var txMax = capabilityTxTable.Max();
                    var newTxLine = "";
                    for (int i = 0; i < currentTxPowersArr.Length; i++)
                    {
                        try
                        {
                            if (int.Parse(currentTxPowersArr[i]) > txMax)
                            {
                                currentTxPowersArr[i] = txMax.ToString();
                            }
                            newTxLine += currentTxPowersArr[i];
                            if (i < (currentTxPowersArr.Length - 1))
                            {
                                newTxLine += ",";
                            }
                        }
                        catch (Exception)
                        {

                        }
                    }
                    _logger.LogInformation("Configuring transmit power: " + newTxLine);
                    _standaloneConfigDTO.transmitPower = newTxLine;
                    SaveConfigDtoToDb(_standaloneConfigDTO);
                    await Task.Delay(1000);
                }

                if (_readerRegionInfo != null && _readerRegionInfo != null
                                              && !string.IsNullOrEmpty(_readerRegionInfo.OperatingRegion))
                {
                    if (_standaloneConfigDTO != null)
                    {
                        _standaloneConfigDTO.operatingRegion = _readerRegionInfo.OperatingRegion.ToUpper();
                        SaveConfigDtoToDb(_standaloneConfigDTO);
                        await Task.Delay(1000);
                    }

                    if (_readerRegionInfo.OperatingRegion.ToUpper().Contains("ETSI"))
                        if (_standaloneConfigDTO != null
                            && !string.IsNullOrEmpty(_standaloneConfigDTO.readerMode)
                            && string.Equals("4", _standaloneConfigDTO.readerMode, StringComparison.OrdinalIgnoreCase))
                        {
                            _standaloneConfigDTO.readerMode = "1002";
                            ConfigFileHelper.SaveFile(_standaloneConfigDTO);
                            SaveConfigDtoToDb(_standaloneConfigDTO);
                            //await Task.Delay(1000);
                        }

                    if (!_readerRegionInfo.OperatingRegion.ToUpper().Contains("ETSI"))
                        if (_standaloneConfigDTO != null
                            && !string.IsNullOrEmpty(_standaloneConfigDTO.readerMode)
                            && string.Equals("5", _standaloneConfigDTO.readerMode, StringComparison.OrdinalIgnoreCase))
                        {
                            _standaloneConfigDTO.readerMode = "1002";
                            ConfigFileHelper.SaveFile(_standaloneConfigDTO);
                            SaveConfigDtoToDb(_standaloneConfigDTO);
                            //await Task.Delay(1000);
                        }

                    try
                    {
                        DeviceId = _iotDeviceInterfaceClient.GetStatusAsync().Result.SerialNumber;
                    }
                    catch (Exception)
                    {


                    }
                }
            }
            catch (Exception)
            {
            }


            if (_standaloneConfigDTO != null && !string.IsNullOrEmpty(_standaloneConfigDTO.httpAuthenticationType)
                                             && string.Equals("BEARER", _standaloneConfigDTO.httpAuthenticationType,
                                                 StringComparison.OrdinalIgnoreCase)
                                             && string.Equals("1",
                                                 _standaloneConfigDTO.httpAuthenticationTokenApiEnabled,
                                                 StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var updatedBearerToken = _httpUtil.GetBearerTokenUsingJsonBodyAsync(
                        _standaloneConfigDTO.httpAuthenticationTokenApiUrl,
                        _standaloneConfigDTO.httpAuthenticationTokenApiBody).Result;
                    if (!string.IsNullOrEmpty(updatedBearerToken) && updatedBearerToken.Length > 10)
                        _standaloneConfigDTO.httpAuthenticationTokenApiValue = updatedBearerToken;
                }
                catch (Exception)
                {
                }

                _stopwatchBearerToken.Start();
            }


            if (_standaloneConfigDTO != null && !string.IsNullOrEmpty(_standaloneConfigDTO.positioningEpcsEnabled)
                                             && string.Equals("1", _standaloneConfigDTO.positioningEpcsEnabled,
                                                 StringComparison.OrdinalIgnoreCase))
                if (!string.IsNullOrEmpty(_standaloneConfigDTO.positioningAntennaPorts))
                    try
                    {
                        var ports = _standaloneConfigDTO.positioningAntennaPorts.Split(",");
                        foreach (var port in ports)
                        {
                            var antennaPort = int.Parse(port.Trim());
                            _positioningAntennaPorts.Add(antennaPort);
                        }
                    }
                    catch (Exception)
                    {
                    }

            if (!string.IsNullOrEmpty(_standaloneConfigDTO.positioningEpcsHeaderList))
                try
                {
                    var headers = _standaloneConfigDTO.positioningEpcsHeaderList.Split(",");
                    foreach (var header in headers) _positioningEpcsHeaderList.Add(header);
                }
                catch (Exception)
                {
                }

            if (!string.IsNullOrEmpty(_standaloneConfigDTO.positioningExpirationInSec))
                try
                {
                    var newExpiration = 3;
                    if (int.TryParse(_standaloneConfigDTO.positioningExpirationInSec.Trim(), out newExpiration))
                        _positioningExpirationInSec = newExpiration;
                }
                catch (Exception)
                {
                }

            try
            {
                var gpoPorts = new GpoVm();
                gpoPorts.GpoConfigurations = new List<SmartReaderGpoConfiguration>();
                var gpoPort = new SmartReaderGpoConfiguration { Gpo = 1, State = false };
                gpoPorts.GpoConfigurations.Add(new SmartReaderGpoConfiguration { Gpo = 1, State = false });
                gpoPorts.GpoConfigurations.Add(new SmartReaderGpoConfiguration { Gpo = 2, State = false });
                gpoPorts.GpoConfigurations.Add(new SmartReaderGpoConfiguration { Gpo = 3, State = false });
                await SetGpoPortsAsync(gpoPorts);
            }
            catch (Exception exGpo)
            {

                _logger.LogError(exGpo, "Error setting gpo to off.");
            }

            _stopwatchPositioningExpiration.Start();


            _stopwatchKeepalive.Start();
            _httpTimerStopwatch = new Stopwatch();
            _httpTimerStopwatch.Start();

            if (_standaloneConfigDTO != null && !string.IsNullOrEmpty(_standaloneConfigDTO.isLogFileEnabled)
                                             && string.Equals("1", _standaloneConfigDTO.isLogFileEnabled,
                                                 StringComparison.OrdinalIgnoreCase))
            {
                // Enable and configure logging to file
                ImpinjDebugLogger.EnableLogFile("/customer/wwwroot/logs", "log-app.txt", 9000);
                // Use the human readable DateTime timestamp format
                ImpinjDebugLogger.UseDateTimeTimestamp = true;
                // Start the data logger
                ImpinjDebugLogger.Enabled = true;
                // Write first entries to Log file
                _logger.LogInformation("{0}++++++++++++++++{0}", SeverityType.Debug);
                _logger.LogInformation("Logging enabled. Starting application.");
            }
            else
            {
                // Enable and configure logging to file
                ImpinjDebugLogger.EnableLogFile("/customer/wwwroot/logs", "log-app.txt", 1000);
                ImpinjDebugLogger.EnableConsole();
                // Use the human readable DateTime timestamp format
                ImpinjDebugLogger.UseDateTimeTimestamp = true;
                // Start the data logger
                ImpinjDebugLogger.Enabled = true;
                // Write first entries to Log file
                _logger.LogInformation("{0}++++++++++++++++{0}", SeverityType.Debug);
                _logger.LogInformation("Logging enabled. Starting application.");
                ImpinjDebugLogger.DisableLogFile();
            }
        }
        catch (Exception)
        {
        }

        try
        {
            _gpiPortStates.TryAdd(0, false);
            _gpiPortStates.TryAdd(1, false);
            _gpiPortStates.TryAdd(2, false);
        }
        catch (Exception)
        {
        }

        StartBarcodeSerialPort();

        PeriodicTasksTimerInventoryData.Elapsed += OnRunPeriodicTasksJobManagerEvent;
        PeriodicTasksTimerInventoryData.Interval = 1000;
        PeriodicTasksTimerInventoryData.AutoReset = false;
        PeriodicTasksTimerInventoryData.Start();

        try
        {
            var readPointIpAddress = _configuration.GetValue<string>("ReaderInfo:Address");
            string buildConfiguration = "Release"; // Default to Debug if unable to determine
#if DEBUG
            buildConfiguration = "Debug";
#endif
            // Get the value based on the build configuration
            if ("Debug".Equals(buildConfiguration))
            {
                readPointIpAddress = _configuration.GetValue<string>("ReaderInfo:DebugAddress") ?? readPointIpAddress;
            }
            _logger.LogInformation("Using initial ip address to get serial: " + readPointIpAddress, SeverityType.Debug);

            if (_standaloneConfigDTO != null && !string.IsNullOrEmpty(_standaloneConfigDTO.mqttEnabled)
                                             && string.Equals("1", _standaloneConfigDTO.mqttEnabled,
                                                 StringComparison.OrdinalIgnoreCase))
            {
                //var serial = GetSerialAsync();
                //var serialNumber = serial.Result.FirstOrDefault().SerialNumber;
                var serialNumber = _standaloneConfigDTO.readerSerial;
                _logger.LogInformation("Serial number retrieved from file: " + serialNumber);
                //_logger.LogInformation("Serial number retrieved from file: " + serialNumber, SeverityType.Debug);
                if (string.IsNullOrEmpty(DeviceId)) DeviceId = serialNumber;
                if (string.IsNullOrEmpty(DeviceIdMqtt)) DeviceIdMqtt = serialNumber + _standaloneConfigDTO.readerName;
                _logger.LogInformation("Device: " + DeviceId);
                _logger.LogInformation("DeviceIdMqtt: " + DeviceIdMqtt);
                //_logger.LogInformation("Device: " + DeviceId, SeverityType.Debug);
                if (!string.IsNullOrEmpty(_standaloneConfigDTO.mqttTagEventsTopic))
                    _ = ConnectToMqttBrokerAsync(DeviceIdMqtt, _standaloneConfigDTO.mqttBrokerAddress,
                        int.Parse(_standaloneConfigDTO.mqttBrokerPort), _standaloneConfigDTO.mqttUsername,
                        _standaloneConfigDTO.mqttPassword, _standaloneConfigDTO.mqttTagEventsTopic,
                        int.Parse(_standaloneConfigDTO.mqttTagEventsQoS), _standaloneConfigDTO);

                //if (!string.IsNullOrEmpty(_standaloneConfigDTO.mqttManagementCommandTopic))
                //    _ = ConnectToMqttCommandBrokerAsync(DeviceIdMqtt, _standaloneConfigDTO.mqttBrokerAddress,
                //        int.Parse(_standaloneConfigDTO.mqttBrokerPort), _standaloneConfigDTO.mqttUsername,
                //        _standaloneConfigDTO.mqttPassword, _standaloneConfigDTO.mqttManagementCommandTopic,
                //        int.Parse(_standaloneConfigDTO.mqttManagementCommandQoS), _standaloneConfigDTO);
            }


            try
            {
                await StopTasksAsync();
            }
            catch (Exception)
            {
            }

            try
            {
                if (!IsOnPause()) await StartTasksAsync();
            }
            catch (Exception)
            {
            }

            try
            {
                SetupUsbFlashDrive();
            }
            catch (Exception ex)
            {

                _logger.LogError(ex, "SetupUsbFlashDrive: unexpected error.");
            }
            try
            {
                if (_standaloneConfigDTO != null
                    && string.Equals("1", _standaloneConfigDTO.enablePlugin, StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var pluginLoaders = new List<PluginLoader>();

                        // create plugin loaders
                        var pluginsDir = Path.Combine(AppContext.BaseDirectory, "plugins");
                        var files = Directory.GetFiles(pluginsDir);
                        if (files != null && files.Any())
                        {
                            foreach (var file in files)
                            {
                                if (file.EndsWith(".dll"))
                                {
                                    //var dirName = Path.GetFileName(file);
                                    //var pluginDll = Path.Combine(dirName, file + ".dll");
                                    var pluginDll = file;
                                    if (File.Exists(pluginDll))
                                    {
                                        var loader = PluginLoader.CreateFromAssemblyFile(
                                            pluginDll,
                                            sharedTypes: new[] { typeof(IPlugin) });
                                        pluginLoaders.Add(loader);
                                    }
                                }

                            }

                            // Create an instance of plugin types
                            foreach (var loader in pluginLoaders)
                            {

                                foreach (var pluginType in loader
                                    .LoadDefaultAssembly()
                                    .GetTypes()
                                    .Where(t => typeof(IPlugin).IsAssignableFrom(t) && !t.IsAbstract))
                                {
                                    try
                                    {
                                        // This assumes the implementation of IPlugin has a parameterless constructor
                                        var plugin = Activator.CreateInstance(pluginType) as IPlugin;

                                        //Console.WriteLine($"Created plugin instance '{plugin?.GetName()}'.");
                                        _logger.LogInformation($"Created plugin instance '{plugin?.GetName()}'.");
                                        _logger.LogInformation($"Details: '{plugin?.GetDescription()}'.");
                                        _logger.LogInformation($"DeviceId: '{DeviceId}'.");
                                        plugin.SetDeviceId(DeviceId);
                                        plugin.Init();
                                        _plugins.Add(plugin?.GetName(), plugin);
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger.LogError(ex, "Plugin Activator: unexpected error.");
                                    }
                                }
                            }
                        }

                    }
                    catch (Exception ex)
                    {

                        _logger.LogError(ex, "Plugin Activator: unexpected error.");
                    }

                }

            }
            catch (Exception ex)
            {

                _logger.LogError(ex, "Plugin Activator: unexpected error.");
            }

            _timerTagFilterLists.Elapsed += OnRunPeriodicTagFilterListsEvent;
            _timerTagFilterLists.Interval = 100;
            _timerTagFilterLists.AutoReset = false;
            _timerTagFilterLists.Start();

            _timerKeepalive.Elapsed += OnRunPeriodicKeepaliveCheck;
            _timerKeepalive.Interval = 100;
            _timerKeepalive.AutoReset = true;
            _timerKeepalive.Start();

            _timerStopTriggerDuration.Elapsed += OnRunPeriodicStopTriggerDurationEvent;
            _timerStopTriggerDuration.Interval = 100;
            _timerStopTriggerDuration.AutoReset = false;
            _timerStopTriggerDuration.Start();

            _timerTagPublisherHttp.Elapsed += OnRunPeriodicTagPublisherHttpTasksEvent;
            _timerTagPublisherHttp.Interval = 500;
            _timerTagPublisherHttp.AutoReset = false;
            _timerTagPublisherHttp.Start();


            _timerTagPublisherSocket.Elapsed += OnRunPeriodicTagPublisherSocketTasksEvent;
            _timerTagPublisherSocket.Interval = 10;
            _timerTagPublisherSocket.AutoReset = false;
            _timerTagPublisherSocket.Start();

            _timerSummaryStreamPublisher.Elapsed += OnRunPeriodicSummaryStreamPublisherTasksEvent;
            _timerSummaryStreamPublisher.Interval = 10;
            _timerSummaryStreamPublisher.AutoReset = false;
            _timerSummaryStreamPublisher.Start();



            _timerTagPublisherUsbDrive.Elapsed += OnRunPeriodicUsbDriveTasksEvent;
            _timerTagPublisherUsbDrive.Interval = 100;
            _timerTagPublisherUsbDrive.AutoReset = false;
            _timerTagPublisherUsbDrive.Start();

            _timerTagPublisherUdpServer.Elapsed += OnRunPeriodicTagPublisherUdpTasksEvent;
            _timerTagPublisherUdpServer.Interval = 500;
            _timerTagPublisherUdpServer.AutoReset = false;
            _timerTagPublisherUdpServer.Start();


            _timerTagPublisherRetry.Elapsed += OnRunPeriodicTagPublisherRetryTasksEvent;
            _timerTagPublisherRetry.Interval = 500;
            _timerTagPublisherRetry.AutoReset = false;
            _timerTagPublisherRetry.Start();

            _timerTagPublisherMqtt.Elapsed += OnRunPeriodicTagPublisherMqttTasksEvent;
            _timerTagPublisherMqtt.Interval = 10;
            _timerTagPublisherMqtt.AutoReset = false;
            _timerTagPublisherMqtt.Start();

            _timerTagPublisherOpcUa.Elapsed += OnRunPeriodicTagPublisherOpcUaTasksEvent;
            _timerTagPublisherOpcUa.Interval = 500;
            _timerTagPublisherOpcUa.AutoReset = false;
            _timerTagPublisherOpcUa.Start();

            _logger.LogInformation("App started. ");
            UpdateSystemImageFallbackFlag();

            while (!stoppingToken.IsCancellationRequested)
                //_logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
                await Task.Delay(10);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Run: unexpected error.");
            _logger.LogInformation("Run: unexpected error. " + ex.Message);

            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            _logger.LogInformation("Restarting process - ExecuteAsync");
            // Restart the application by spawning a new process with the same arguments
            var process = Process.GetCurrentProcess();
            process.StartInfo.WorkingDirectory = Directory.GetCurrentDirectory();
            Process.Start(process.MainModule.FileName);
            Environment.Exit(1);
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
                _serialTty = new SerialPort(serialPortName, baudrate);
                _serialTty.ReadTimeout = 1000;
                _serialTty.DiscardNull = true;

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

            sp.Read(bytes, 0, bytesToRead);
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

            sp.Read(bytes, 0, bytesToRead);
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
                        double latDecimal = latDegrees + latMinutes / 60;
                        if (latDir == "S")
                        {
                            latDecimal = -latDecimal;
                        }
                        // Convert the latitude from NMEA format to decimal degrees.



                        double lonDegrees = double.Parse(lon.Substring(0, 3));
                        double lonMinutes = double.Parse(lon.Substring(3));
                        double lonDecimal = lonDegrees + lonMinutes / 60;
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
    //            if (e.ApplicationMessage.Payload != null)
    //                _logger.LogInformation($"+ Payload = {Encoding.UTF8.GetString(e.ApplicationMessage.Payload)}");

    //            _logger.LogInformation($"+ QoS = {e.ApplicationMessage.QualityOfServiceLevel}");
    //            _logger.LogInformation($"+ Retain = {e.ApplicationMessage.Retain}");
    //            _logger.LogInformation($"+ ClientId = {e.ClientId}");

    //            var payload = "";
    //            if (e.ApplicationMessage.Payload != null)
    //                payload = Encoding.UTF8.GetString(e.ApplicationMessage.Payload);
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

    private List<MqttTopicFilter> BuildMqttTopicList(StandaloneConfigDTO smartReaderSetupData)
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

            return mqttTopicFilters;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BuildMqttTopicList: Unexpected error. " + ex.Message);
            throw;
        }
    }

    private void ProcessMqttCommandMessage(string clientId, string topic, string receivedMqttMessage)
    {
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


            //
            if (topic.Contains(_standaloneConfigDTO.mqttManagementCommandTopic)
                || topic.Contains(
                    _standaloneConfigDTO.mqttManagementCommandTopic.Replace("{{deviceId}}",
                        _standaloneConfigDTO.readerName)))
                try
                {
                    //var deserializedCmdData = JsonDocument.Parse(mqttMessage);
                    if (mqttMessage.StartsWith("{") && !mqttMessage.Contains("antennaFilter"))
                    {
                        var deserializedCmdData = JsonConvert.DeserializeObject<JObject>(mqttMessage);
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
                                        var previousCommandId = ReadMqttCommandIdFromFile().Result;
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
                                            WriteMqttCommandIdToFile(commandIdValue);
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

                                                    var payloadCommandStatus = new Dictionary<string, string>();
                                                    payloadCommandStatus.Add("detail",
                                                        $"command_id {commandIdValue} already processed.");
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

                                                    var qos = 0;
                                                    var retain = false;
                                                    var mqttQualityOfServiceLevel = MqttQualityOfServiceLevel.AtMostOnce;
                                                    try
                                                    {
                                                        int.TryParse(_standaloneConfigDTO.mqttManagementResponseQoS,
                                                            out qos);
                                                        bool.TryParse(
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
                                                    _mqttClient.PublishAsync(mqttCommandResponseTopic,
                                                        serializedData, mqttQualityOfServiceLevel, retain);
                                                    return;
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
                                        SaveStartPresetCommandToDb();
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
                                        int.TryParse(_standaloneConfigDTO.mqttManagementResponseQoS, out qos);
                                        bool.TryParse(_standaloneConfigDTO.mqttManagementResponseRetainMessages,
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
                                    _mqttClient.PublishAsync(mqttCommandResponseTopic, serializedData,
                                        mqttQualityOfServiceLevel, retain);
                                }
                                else if ("stop".Equals(commandValue))
                                {
                                    try
                                    {
                                        //StopPresetAsync();
                                        //_ = StopTasksAsync();
                                        //SaveStopCommandToDb();
                                        SaveStopPresetCommandToDb();
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
                                        int.TryParse(_standaloneConfigDTO.mqttManagementResponseQoS, out qos);
                                        bool.TryParse(_standaloneConfigDTO.mqttManagementResponseRetainMessages,
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
                                    _mqttClient.PublishAsync(mqttCommandResponseTopic, serializedData,
                                        mqttQualityOfServiceLevel, retain);


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
                                        int.TryParse(_standaloneConfigDTO.mqttManagementResponseQoS, out qos);
                                        bool.TryParse(_standaloneConfigDTO.mqttManagementResponseRetainMessages,
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
                                    _mqttClient.PublishAsync(mqttCommandResponseTopic, serializedData,
                                        mqttQualityOfServiceLevel, retain);


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
                                        int.TryParse(_standaloneConfigDTO.mqttManagementResponseQoS, out qos);
                                        bool.TryParse(_standaloneConfigDTO.mqttManagementResponseRetainMessages,
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
                                    _mqttClient.PublishAsync(mqttCommandResponseTopic, serializedData,
                                        mqttQualityOfServiceLevel, retain);


                                }
                                else if ("reboot".Equals(commandValue))
                                {
                                    var resultPayload = "";
                                    try
                                    {
                                        resultPayload = CallIotRestFulInterface(mqttMessage, "/system/reboot", "POST",
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

                                    //var mqttCommandResponseTopic = $"{_standaloneConfigDTO.mqttCommandTopic}/response";
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
                                        int.TryParse(_standaloneConfigDTO.mqttManagementResponseQoS, out qos);
                                        bool.TryParse(_standaloneConfigDTO.mqttManagementResponseRetainMessages,
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
                                    _mqttClient.PublishAsync(mqttCommandResponseTopic, serializedData,
                                        mqttQualityOfServiceLevel, retain);
                                }
                                else if ("mode".Equals(commandValue) || "setcfg".Equals(commandValue))
                                {
                                    var modeCmdResult = "success";
                                    try
                                    {
                                        if (deserializedCmdData.ContainsKey("payload") || deserializedCmdData.ContainsKey("fields"))
                                            try
                                            {
                                                modeCmdResult = ProcessModeJsonCommand(mqttMessage);
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
                                        int.TryParse(_standaloneConfigDTO.mqttManagementResponseQoS, out qos);
                                        bool.TryParse(_standaloneConfigDTO.mqttManagementResponseRetainMessages,
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
                                    _mqttClient.PublishAsync(mqttCommandResponseTopic, serializedData,
                                        mqttQualityOfServiceLevel, retain);
                                    //if ("success".Equals(commandStatus))
                                    //{
                                    //    // exits the app to reload the settings
                                    //    Task.Delay(3000);
                                    //    Environment.Exit(0);
                                    //}
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

                                    //var mqttCommandResponseTopic = $"{_standaloneConfigDTO.mqttCommandTopic}/response";
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
                                        int.TryParse(_standaloneConfigDTO.mqttManagementResponseQoS, out qos);
                                        bool.TryParse(_standaloneConfigDTO.mqttManagementResponseRetainMessages,
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
                                    _mqttClient.PublishAsync(mqttCommandResponseTopic, serializedData,
                                        mqttQualityOfServiceLevel, retain);
                                }
                                else if ("apply-settings".Equals(commandValue))
                                {
                                    var resultPayload = "";

                                    if (deserializedCmdData.ContainsKey("payload") || deserializedCmdData.ContainsKey("fields"))
                                    {
                                        if (deserializedCmdData.ContainsKey("payload"))
                                        {
                                            var commandPayloadJObject =
                                            deserializedCmdData["payload"].Value<StandaloneConfigDTO>();
                                            if (commandPayloadJObject != null)
                                                try
                                                {
                                                    SaveConfigDtoToDb(commandPayloadJObject);
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
                                                    SaveConfigDtoToDb(commandPayloadJObject);
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
                                        int.TryParse(_standaloneConfigDTO.mqttManagementResponseQoS, out qos);
                                        bool.TryParse(_standaloneConfigDTO.mqttManagementResponseRetainMessages,
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
                                    _mqttClient.PublishAsync(mqttCommandResponseTopic, serializedData,
                                        mqttQualityOfServiceLevel, retain);
                                }
                                else if ("upgrade".Equals(commandValue))
                                {
                                    try
                                    {
                                        if (deserializedCmdData.ContainsKey("payload") || deserializedCmdData.ContainsKey("fields"))
                                        {
                                            JObject commandPayloadJObject = new();
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
                                                    JsonConvert.DeserializeObject<StandaloneConfigDTO>(mqttMessage);

                                                if (deserializedConfigData != null)
                                                    try
                                                    {
                                                        if (!string.IsNullOrEmpty(remoteUrl))
                                                        {
                                                            var cmdPayload = JsonConvert.SerializeObject(commandPayloadParams);
                                                            SaveUpgradeCommandToDb(cmdPayload);
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
                                        int.TryParse(_standaloneConfigDTO.mqttManagementResponseQoS, out qos);
                                        bool.TryParse(_standaloneConfigDTO.mqttManagementResponseRetainMessages,
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
                                    _mqttClient.PublishAsync(mqttCommandResponseTopic, serializedData,
                                        mqttQualityOfServiceLevel, retain);
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
                                            JObject commandPayloadJObject = new();
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
                                                resultPayload = CallIotRestFulInterface(impinjIotApiRequest,
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
                                        int.TryParse(_standaloneConfigDTO.mqttManagementResponseQoS, out qos);
                                        bool.TryParse(_standaloneConfigDTO.mqttManagementResponseRetainMessages,
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
                                    _mqttClient.PublishAsync(mqttCommandResponseTopic, serializedData,
                                        mqttQualityOfServiceLevel, retain);
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
                            var cmdResult = _plugins[_standaloneConfigDTO.activePlugin].ProcessCommand(mqttMessage);

                            if ("START".Equals(cmdResult))
                            {
                                SaveStartPresetCommandToDb();
                            }
                            else if ("STOP".Equals(cmdResult))
                            {
                                SaveStopPresetCommandToDb();
                            }
                        }
                    }
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
                    //var deserializedCmdData = JsonDocument.Parse(mqttMessage);
                    var deserializedCmdData = JsonConvert.DeserializeObject<JObject>(mqttMessage);
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
                                var previousCommandId = ReadMqttCommandIdFromFile().Result;
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
                                    WriteMqttCommandIdToFile(commandIdValue);
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

                                            var payloadCommandStatus = new Dictionary<string, string>();
                                            payloadCommandStatus.Add("detail",
                                                $"id {commandIdValue} already processed.");
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

                                            var qos = 0;
                                            var retain = false;
                                            var mqttQualityOfServiceLevel = MqttQualityOfServiceLevel.AtMostOnce;
                                            try
                                            {
                                                int.TryParse(_standaloneConfigDTO.mqttControlResponseQoS, out qos);
                                                bool.TryParse(_standaloneConfigDTO.mqttControlResponseRetainMessages,
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
                                            _mqttClient.PublishAsync(mqttCommandResponseTopic, serializedData,
                                                mqttQualityOfServiceLevel, retain);
                                            return;
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
                                    SaveStartPresetCommandToDb();
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
                                    int.TryParse(_standaloneConfigDTO.mqttControlResponseQoS, out qos);
                                    bool.TryParse(_standaloneConfigDTO.mqttControlResponseRetainMessages, out retain);

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
                                _mqttClient.PublishAsync(mqttCommandResponseTopic, serializedData,
                                    mqttQualityOfServiceLevel, retain);
                            }
                            else if ("stop".Equals(commandValue))
                            {
                                try
                                {
                                    //StopPresetAsync();
                                    //_ = StopTasksAsync();
                                    //SaveStopCommandToDb();
                                    SaveStopPresetCommandToDb();
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
                                    int.TryParse(_standaloneConfigDTO.mqttControlResponseQoS, out qos);
                                    bool.TryParse(_standaloneConfigDTO.mqttControlResponseRetainMessages, out retain);

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
                                _mqttClient.PublishAsync(mqttCommandResponseTopic, serializedData,
                                    mqttQualityOfServiceLevel, retain);
                            }
                            else if ("reboot".Equals(commandValue))
                            {
                                var resultPayload = "";
                                try
                                {
                                    resultPayload = CallIotRestFulInterface(mqttMessage, "/system/reboot", "POST",
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

                                //var mqttCommandResponseTopic = $"{_standaloneConfigDTO.mqttCommandTopic}/response";
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
                                    int.TryParse(_standaloneConfigDTO.mqttControlResponseQoS, out qos);
                                    bool.TryParse(_standaloneConfigDTO.mqttControlResponseRetainMessages, out retain);

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
                                _mqttClient.PublishAsync(mqttCommandResponseTopic, serializedData,
                                    mqttQualityOfServiceLevel, retain);
                            }
                            else if ("mode".Equals(commandValue))
                            {
                                var modeCmdResult = "success";
                                try
                                {
                                    if (deserializedCmdData.ContainsKey("payload") || deserializedCmdData.ContainsKey("fields"))
                                        try
                                        {
                                            modeCmdResult = ProcessModeJsonCommand(mqttMessage);
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
                                    int.TryParse(_standaloneConfigDTO.mqttControlResponseQoS, out qos);
                                    bool.TryParse(_standaloneConfigDTO.mqttControlResponseRetainMessages, out retain);

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
                                _mqttClient.PublishAsync(mqttCommandResponseTopic, serializedData,
                                    mqttQualityOfServiceLevel, retain);
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
                                            JObject commandPayloadJObject = new();
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
                                                    gpoCmdResult = CallIotRestFulInterface(impinjIotApiRequest, "/device/gpos", "PUT", _standaloneConfigDTO.mqttControlResponseTopic, false);

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
                                    int.TryParse(_standaloneConfigDTO.mqttControlResponseQoS, out qos);
                                    bool.TryParse(_standaloneConfigDTO.mqttControlResponseRetainMessages, out retain);

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
                                _mqttClient.PublishAsync(mqttCommandResponseTopic, serializedData,
                                    mqttQualityOfServiceLevel, retain);
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
                                        gpoCmdResult = CallIotRestFulInterface("", "/device/gpos", "GET", _standaloneConfigDTO.mqttControlResponseTopic, false);

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
                                    int.TryParse(_standaloneConfigDTO.mqttControlResponseQoS, out qos);
                                    bool.TryParse(_standaloneConfigDTO.mqttControlResponseRetainMessages, out retain);

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
                                _mqttClient.PublishAsync(mqttCommandResponseTopic, serializedData,
                                    mqttQualityOfServiceLevel, retain);
                                //if ("success".Equals(commandStatus))
                                //{
                                //    // exits the app to reload the settings
                                //    Task.Delay(3000);
                                //    Environment.Exit(0);
                                //}
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
                                        JObject commandPayloadJObject = new();
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
                                            resultPayload = CallIotRestFulInterface(impinjIotApiRequest,
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
                                    int.TryParse(_standaloneConfigDTO.mqttControlResponseQoS, out qos);
                                    bool.TryParse(_standaloneConfigDTO.mqttControlResponseRetainMessages, out retain);

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
                                _mqttClient.PublishAsync(mqttCommandResponseTopic, serializedData,
                                    mqttQualityOfServiceLevel, retain);
                            }
                        }
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
                            SaveConfigDtoToDb(deserializedData);
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
                    _mqttClient.PublishAsync(mqttCommandResponseTopic, serializedData);
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
                    _mqttClient.PublishAsync(mqttCommandResponseTopic, serializedData);
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
                    _mqttClient.PublishAsync(mqttCommandResponseTopic, serializedData);
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
                    _mqttClient.PublishAsync(mqttCommandResponseTopic, "OK");
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
                    _mqttClient.PublishAsync(mqttCommandResponseTopic, "OK");
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
                        _mqttClient.PublishAsync(mqttCommandResponseTopic, "OK");
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

                    CallIotRestFulInterface(mqttMessage, endpointString, method, mqttCommandResponseTopic, true);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error" + ex.Message);
                    //_mqttCommandClient.PublishAsync(_standaloneConfigDTO.mqttTopic, ex.Message);
                    _mqttClient.PublishAsync(mqttCommandResponseTopic, ex.Message);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error" + ex.Message);
        }
    }

    private string ProcessModeJsonCommand(string modeCommandPayload)
    {
        var commandResult = "success";
        try
        {
            var deserializedConfigData = JsonConvert.DeserializeObject<JObject>(modeCommandPayload);
            if (deserializedConfigData != null)
                try
                {
                    JObject commandPayloadJObject = new();
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
                        var softwareFilterTagIdValue = "E280";
                        var softwareFilterTagIdMatch = "prefix";
                        var softwareFilterTagIdOperation = "include";
                        var softwareFilterTagIdEnabled = "0";
                        var softwareFilterIncludeEpcHeaderListEnabled = "0";
                        var softwareFilterIncludeEpcHeaderListValue = "E280,3031";


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
                                    JArray antennaListJArray = new();
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
                                    var disabledValue = "0";
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

                                    int.TryParse(receivedSession, out convertedSession);

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

                            ConfigFileHelper.SaveFile(_standaloneConfigDTO);
                            SaveConfigDtoToDb(_standaloneConfigDTO);
                            LoadConfig();
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
    }

    private void UpgradeSystemImage(string imageRemoteUrl, long timeoutInMinutes = 3, long maxUpgradeRetries = 1)
    {
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
                        File.WriteAllText("/tmp/upgrading", "1");
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
                                    rshell.SendCommand("reboot");
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

    private static string CallIotRestFulInterface(string bodyRequest, string endpointString, string method,
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
                (Func<HttpRequestMessage, X509Certificate2, X509Chain, SslPolicyErrors, bool>)((
                    message, cert, chain, errors) => true)
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
            //_mqttCommandClient.PublishAsync(_standaloneConfigDTO.mqttTopic, requestResult);
            _mqttClient.PublishAsync(mqttCommandResponseTopic, requestResult);
        }
        else
        {
            requestResult = response.Result.Content.ReadAsStringAsync().Result;

            if (!string.IsNullOrEmpty(requestResult) && publish)
                _mqttClient.PublishAsync(mqttCommandResponseTopic,
                    response.Result.Content.ReadAsStringAsync().Result);
        }

        return requestResult;
    }

    private async Task ConnectToMqttBrokerAsync(string mqttClientId, string mqttBrokerAddress, int mqttBrokerPort,
        string mqttUsername, string mqttPassword, string mqttTopic, int mqttQos,
        StandaloneConfigDTO smartReaderSetupData)
    {
        try
        {
            var lastWillMessage = BuildMqttLastWillMessage();
            int mqttKeepAlivePeriod = 30;
            int.TryParse(_standaloneConfigDTO.mqttBrokerKeepAlive, out mqttKeepAlivePeriod);

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
                        .WithWebSocketServer($"{_standaloneConfigDTO.mqttBrokerProtocol}://{mqttBrokerAddress}:{mqttBrokerPort}{mqttBrokerWebSocketPath}")
                        .WithWillMessage(lastWillMessage)
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
                        .WithWillMessage(lastWillMessage)
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
                    _mqttClientOptions = new ManagedMqttClientOptionsBuilder()
                    .WithClientOptions(new MqttClientOptionsBuilder()
                        //.WithCleanSession()
                        .WithKeepAlivePeriod(new TimeSpan(0, 0, 0, mqttKeepAlivePeriod))
                        //.WithCommunicationTimeout(TimeSpan.FromMilliseconds(60 * 1000))
                        .WithClientId(localClientId)
                        .WithWebSocketServer($"{_standaloneConfigDTO.mqttBrokerProtocol}://{mqttBrokerAddress}:{mqttBrokerPort}{mqttBrokerWebSocketPath}")
                        .WithCredentials(mqttUsername, mqttPassword)
                        .WithWillMessage(lastWillMessage)
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
                        .WithWillMessage(lastWillMessage)
                        .Build())
                    .Build();
                }
            }


            _mqttClient = new MqttFactory().CreateManagedMqttClient();
            //_mqttClient = new MqttFactory().CreateMqttClient();


            await _mqttClient.StartAsync(_mqttClientOptions);


            //_mqttClient.UseApplicationMessageReceivedHandler(e => { });

            _mqttClient.UseApplicationMessageReceivedHandler(e =>
            {
                _logger.LogInformation("### RECEIVED APPLICATION MESSAGE ###");
                _logger.LogInformation($"+ Topic = {e.ApplicationMessage.Topic}");
                if (e.ApplicationMessage.Payload != null)
                    _logger.LogInformation($"+ Payload = {Encoding.UTF8.GetString(e.ApplicationMessage.Payload)}");

                _logger.LogInformation($"+ QoS = {e.ApplicationMessage.QualityOfServiceLevel}");
                _logger.LogInformation($"+ Retain = {e.ApplicationMessage.Retain}");
                _logger.LogInformation($"+ ClientId = {e.ClientId}");

                var payload = "";
                if (e.ApplicationMessage.Payload != null)
                    payload = Encoding.UTF8.GetString(e.ApplicationMessage.Payload);
                try
                {
                    ProcessMqttCommandMessage(e.ClientId, e.ApplicationMessage.Topic, payload);
                }
                catch (Exception ex)
                {
                    _logger.LogError("ProcessMqttMessage: Unexpected error. " + ex.Message);
                }

                _logger.LogInformation(" ");
            });



            _ = _mqttClient.UseConnectedHandler(async e =>
            {
                Console.WriteLine("### CONNECTED WITH SERVER ###");
                if (!string.IsNullOrEmpty(_standaloneConfigDTO.mqttBrokerAddress))
                    _logger.LogInformation("### CONNECTED WITH SERVER ### " + _standaloneConfigDTO.mqttBrokerAddress,
                        SeverityType.Debug);
                var mqttManagementEvents = new Dictionary<string, object>();
                mqttManagementEvents.Add("smartreader-mqtt-status", "connected");
                mqttManagementEvents.Add("readerName", _standaloneConfigDTO.readerName);
                mqttManagementEvents.Add("mac", _iotDeviceInterfaceClient.MacAddress);
                mqttManagementEvents.Add("timestamp", Utils.CSharpMillisToJavaLongMicroseconds(DateTime.Now));
                PublishMqttManagementEvent(mqttManagementEvents);

                try
                {
                    var mqttTopicFilters = BuildMqttTopicList(smartReaderSetupData);


                    await _mqttClient.SubscribeAsync(mqttTopicFilters.ToArray());
                    _logger.LogInformation("### Subscribed to topics. ### ");
                    _ = ProcessGpoErrorPortRecoveryAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Subscribe: Unexpected error. " + ex.Message);
                }
                //if (!string.IsNullOrEmpty(_standaloneConfigDTO.mqttManagementCommandTopic))
                //    try
                //    {
                //        //TODO
                //    }
                //    catch (Exception ex)
                //    {
                //        _logger.LogError(ex, "UseConnectedHandler: Unexpected error. " + ex.Message);
                //    }
            });

            _ = _mqttClient.UseDisconnectedHandler(async e =>
            {
                Console.WriteLine("### DISCONNECTED FROM SERVER ###");



                if (!string.IsNullOrEmpty(_standaloneConfigDTO.mqttBrokerAddress))
                    _logger.LogInformation($"### DISCONNECTED FROM SERVER ### {_standaloneConfigDTO.mqttBrokerAddress}");

                try
                {
                    var diconnectDetails = $"Disconnection: ConnectResult {e.ConnectResult} \n";
                    diconnectDetails += $"Disconnection: Reason {e.Reason} \n";
                    diconnectDetails += $" ResultCode {e.ConnectResult.ResultCode} \n";
                    diconnectDetails += $" ClientWasConnected {e.ClientWasConnected} \n";

                    _logger.LogInformation($"### Details {diconnectDetails}");

                    await ProcessGpoErrorPortAsync();
                }
                catch (Exception)
                {

                }

            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error" + ex.Message);
        }
    }

    private MqttApplicationMessage BuildMqttLastWillMessage()
    {
        var mqttManagementEvents = new Dictionary<string, string>();
        mqttManagementEvents.Add("smartreader-mqtt-status", "disconnected");
        var jsonParam = JsonConvert.SerializeObject(mqttManagementEvents);

        var lastWillMessage = new MqttApplicationMessageBuilder()
            .WithTopic("/events/")
            .WithPayload(Encoding.ASCII.GetBytes(jsonParam))
            .Build();
        try
        {
            if (_standaloneConfigDTO != null)
            {
                var mqttManagementEventsTopic = _standaloneConfigDTO.mqttManagementEventsTopic;
                if (string.Equals("1", _standaloneConfigDTO.mqttEnabled, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrEmpty(_standaloneConfigDTO.mqttManagementEventsTopic))
                {
                    if (_standaloneConfigDTO.mqttManagementEventsTopic.Contains("{{deviceId}}"))
                        mqttManagementEventsTopic =
                            _standaloneConfigDTO.mqttControlResponseTopic.Replace("{{deviceId}}",
                                _standaloneConfigDTO.readerName);
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

                    lastWillMessage = new MqttApplicationMessageBuilder()
                        .WithTopic(mqttManagementEventsTopic)
                        .WithPayload(Encoding.ASCII.GetBytes(jsonParam))
                        .WithQualityOfServiceLevel(mqttQualityOfServiceLevel)
                        .WithRetainFlag(retain)
                        .Build();
                }
            }
        }
        catch (Exception)
        {
        }

        return lastWillMessage;
    }

    private void PublishMqttManagementEvent(Dictionary<string, object> mqttManagementEvents)
    {
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

                        var mqttCommandResponseTopic = $"{_standaloneConfigDTO.mqttManagementResponseTopic}";
                        _ = _mqttClient.PublishAsync(mqttManagementEventsTopic, jsonParam,
                            mqttQualityOfServiceLevel, retain);
                        _ = ProcessGpoErrorPortRecoveryAsync();
                    }
                    catch (Exception)
                    {
                        ProcessGpoErrorPortAsync();
                    }
                }
        }
    }

    private async Task ProcessHttpPostTagEventDataAsync(SmartReaderTagEventData smartReaderTagEventData)
    {
        try
        {
            _ = await _httpUtil.PostTagEventToSmartReaderServerAsync(smartReaderTagEventData);
        }
        catch (Exception ex)
        {
            ProcessGpoErrorPortAsync();
            _logger.LogError(ex, "Unexpected error on ProcessTagEventData " + ex.Message);
        }
    }


    private async Task ProcessTagInventoryEventAsync(TagInventoryEvent tagInventoryEvent)
    {
        try
        {
            if (tagInventoryEvent == null)
                return;

            var licenseToSet = "";
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
                        _logger.LogInformation("Invalid license key. ", SeverityType.Error);
                        var mqttManagementEvents = new Dictionary<string, object>();
                        mqttManagementEvents.Add("readerName", _standaloneConfigDTO.readerName);
                        mqttManagementEvents.Add("mac", _iotDeviceInterfaceClient.MacAddress);
                        mqttManagementEvents.Add("ip", _iotDeviceInterfaceClient.IpAddresses);
                        mqttManagementEvents.Add("serial", _standaloneConfigDTO.readerSerial);
                        mqttManagementEvents.Add("timestamp", Utils.CSharpMillisToJavaLongMicroseconds(DateTime.Now));
                        mqttManagementEvents.Add("smartreader-status", "invalid-license-key");
                        PublishMqttManagementEvent(mqttManagementEvents);
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
                    _logger.LogInformation("Invalid license key. ", SeverityType.Error);
                    var mqttManagementEvents = new Dictionary<string, object>();
                    mqttManagementEvents.Add("readerName", _standaloneConfigDTO.readerName);
                    mqttManagementEvents.Add("mac", _iotDeviceInterfaceClient.MacAddress);
                    mqttManagementEvents.Add("ip", _iotDeviceInterfaceClient.IpAddresses);
                    mqttManagementEvents.Add("serial", _standaloneConfigDTO.readerSerial);
                    mqttManagementEvents.Add("timestamp", Utils.CSharpMillisToJavaLongMicroseconds(DateTime.Now));
                    mqttManagementEvents.Add("smartreader-status", "invalid-license-key");
                    PublishMqttManagementEvent(mqttManagementEvents);
                    return;
                }
            }



            var smartReaderTagReadEvent = new SmartReaderTagReadEvent();


            smartReaderTagReadEvent.TagReads = new List<TagRead>();
            var tagRead = new TagRead();

            tagRead.FirstSeenTimestamp = Utils.CSharpMillisToJavaLongMicroseconds(DateTime.Now);

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
                                    if (!_currentSkuReadEpcs.Contains(epc))
                                    {
                                        _currentSkuReadEpcs.Add(epc);
                                        if (!_currentSkus.ContainsKey(sku))
                                            _currentSkus.TryAdd(sku, 1);
                                        else
                                            try
                                            {
                                                _currentSkus[sku] = _currentSkus[sku] + 1;
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
                                    if (!_currentSkuReadEpcs.Contains(epc))
                                    {
                                        _currentSkuReadEpcs.Add(epc);
                                        if (!_currentSkus.ContainsKey(tagRead.TagDataKey))
                                            _currentSkus.TryAdd(tagRead.TagDataKey, 1);
                                        else
                                            try
                                            {
                                                _currentSkus[tagRead.TagDataKey] = _currentSkus[tagRead.TagDataKey] + 1;
                                            }
                                            catch (Exception)
                                            {
                                            }
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

            if (string.Equals("1", _standaloneConfigDTO.includeGpiEvent, StringComparison.OrdinalIgnoreCase))
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
            // PLUGIN
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
            //  PLUGIN
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

            string jsonData = null;

            if (shouldProccessPositioningEvents)
            {
                var positioningEvents = GetLastPositioningDataAsync(tagRead.FirstSeenTimestamp.Value, true);
                lastPositioningReferecenceEpcs = positioningEvents.Result;

                var smartReaderTagReadPositioningEvent = new SmartReaderTagReadEvent();
                smartReaderTagReadPositioningEvent.ReaderName = _standaloneConfigDTO.readerName;

                smartReaderTagReadEvent.Mac = _iotDeviceInterfaceClient.MacAddress;

                if (string.Equals("1", _standaloneConfigDTO.siteEnabled, StringComparison.OrdinalIgnoreCase))
                    smartReaderTagReadPositioningEvent.Site = _standaloneConfigDTO.site;

                smartReaderTagReadPositioningEvent.TagReadsPositioning = new List<TagReadPositioning>();

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
                    tagReadPositioning.LastPositioningReferecenceEpcs = new List<LastPositioningReferecenceEpc>();
                    tagReadPositioning.LastPositioningReferecenceEpcs.AddRange(lastPositioningReferecenceEpcs);
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
                    dataToPublish.Property("tag_reads").Descendants()
                        .OfType<JProperty>()
                        .Where(attr => attr.Name.StartsWith("firstSeenTimestamp"))
                        .ToList()
                        .ForEach(attr => attr.Remove());
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

            if (!string.IsNullOrEmpty(_standaloneConfigDTO.enableTagEventsListBatch)
                        && string.Equals("1", _standaloneConfigDTO.enableTagEventsListBatch,
                            StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    if (_smartReaderTagEventsListBatch.TryGetValue(tagRead.Epc, out JObject retrievedValue))
                    {

                        try
                        {
                            var existingEventOnCurrentAntenna = (JObject)(retrievedValue["tag_reads"].FirstOrDefault(q => (long)q["antennaPort"] == tagRead.AntennaPort));


                            if (string.Equals("1", _standaloneConfigDTO.filterTagEventsListBatchOnChangeBasedOnAntennaZone, StringComparison.OrdinalIgnoreCase))
                            {
                                var existingEventOnCurrentAntennaZone = (JObject)(retrievedValue["tag_reads"].FirstOrDefault(q => (string)q["epc"] == tagRead.Epc));
                                //var zoneNames = _standaloneConfigDTO.antennaZones.Split(",");
                                if (existingEventOnCurrentAntennaZone != null)
                                {
                                    var currentAntennazone = existingEventOnCurrentAntennaZone["antennaZone"].Value<string>();
                                    if (!string.IsNullOrEmpty(currentAntennazone)
                                        && !tagRead.AntennaZone.Equals(currentAntennazone))
                                    {
                                        if (string.Equals("1", _standaloneConfigDTO.updateTagEventsListBatchOnChange, StringComparison.OrdinalIgnoreCase)
                                        && !_smartReaderTagEventsListBatchOnUpdate.ContainsKey(tagRead.Epc))
                                        {
                                            _smartReaderTagEventsListBatchOnUpdate.TryAdd(tagRead.Epc, dataToPublish);
                                        }
                                    }
                                }
                            }
                            else
                            {
                                // it has been read on a different antenna:
                                if (existingEventOnCurrentAntenna == null)
                                {
                                    if (string.Equals("1", _standaloneConfigDTO.updateTagEventsListBatchOnChange, StringComparison.OrdinalIgnoreCase)
                                        && !_smartReaderTagEventsListBatchOnUpdate.ContainsKey(tagRead.Epc))
                                    {
                                        _smartReaderTagEventsListBatchOnUpdate.TryAdd(tagRead.Epc, dataToPublish);
                                    }
                                }
                            }
                        }
                        catch (Exception)
                        {


                        }
                        _smartReaderTagEventsListBatch.TryUpdate(tagRead.Epc, dataToPublish, retrievedValue);
                    }
                    else
                    {

                        if (string.Equals("1", _standaloneConfigDTO.updateTagEventsListBatchOnChange, StringComparison.OrdinalIgnoreCase)
                            && !_smartReaderTagEventsListBatchOnUpdate.ContainsKey(tagRead.Epc))
                        {
                            _smartReaderTagEventsListBatchOnUpdate.TryAdd(tagRead.Epc, dataToPublish);
                        }
                        else
                        {
                            _smartReaderTagEventsListBatch.TryAdd(tagRead.Epc, dataToPublish);
                        }

                    }
                }
                catch (Exception)
                {


                }
            }
            else
            {
                if (string.Equals("1", _standaloneConfigDTO.enableValidation, StringComparison.OrdinalIgnoreCase)
                || string.Equals("1", _standaloneConfigDTO.groupEventsOnInventoryStatus,
                    StringComparison.OrdinalIgnoreCase))
                    _messageQueueTagSmartReaderTagEventGroupToValidate.Enqueue(dataToPublish);
                else
                    EnqueueToExternalPublishers(dataToPublish, true);
            }


        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error on ProcessTagInventoryEventsync. " + ex.Message);
        }
    }

    private void EnqueueToPublishers(JObject dataToPublish)
    {
        try
        {
            if (string.Equals("1", _standaloneConfigDTO.enableSummaryStream, StringComparison.OrdinalIgnoreCase))
                if (!string.Equals("1", _standaloneConfigDTO.groupEventsOnInventoryStatus,
                        StringComparison.OrdinalIgnoreCase))
                {
                    var defaultTagEvent =
                        dataToPublish.Property("tag_reads").ToList().FirstOrDefault().FirstOrDefault();
                    if (((JObject)defaultTagEvent).ContainsKey("tagDataKey"))
                    {
                        var eventTimestamp = Utils.CSharpMillisToJavaLongMicroseconds(DateTime.Now);
                        var localBarcode = "";
                        if (((JObject)defaultTagEvent).ContainsKey("barcode"))
                            localBarcode = ((JObject)defaultTagEvent)["barcode"].Value<string>();
                        var skuSummaryList = new Dictionary<string, SkuSummary>();
                        var tagDataKeyValue = ((JObject)defaultTagEvent)["tagDataKey"].Value<string>();
                        var skuSummary = new SkuSummary();
                        skuSummary.Sku = tagDataKeyValue;
                        skuSummary.Qty = 1;
                        skuSummary.Barcode = localBarcode;
                        skuSummary.EventTimestamp = eventTimestamp;
                        skuSummaryList.Add(tagDataKeyValue, skuSummary);
                        var skuArray = JArray.FromObject(skuSummaryList.Values);
                        AddJsonSkuSummaryToQueue(skuArray);
                    }
                }

            if (string.Equals("1", _standaloneConfigDTO.enableTagEventStream, StringComparison.OrdinalIgnoreCase))
                SaveJsonTagEventToDb(dataToPublish);

            if (string.Equals("1", _standaloneConfigDTO.httpPostEnabled, StringComparison.OrdinalIgnoreCase))
                try
                {
                    if (_messageQueueTagSmartReaderTagEventHttpPost.Count < 1000)
                        _messageQueueTagSmartReaderTagEventHttpPost.Enqueue(dataToPublish);
                }
                catch (Exception)
                {
                }
        }
        catch (Exception)
        {
        }
    }

    public void EnqueueToExternalPublishers(JObject dataToPublish, bool shouldValidate)
    {
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

            if (string.Equals("1", _standaloneConfigDTO.socketServer, StringComparison.OrdinalIgnoreCase))
                try
                {
                    if (_messageQueueTagSmartReaderTagEventSocketServer.Count < 1000)
                        _messageQueueTagSmartReaderTagEventSocketServer.Enqueue(dataToPublish);
                }
                catch (Exception)
                {
                }

            if (string.Equals("1", _standaloneConfigDTO.usbFlashDrive, StringComparison.OrdinalIgnoreCase))
                try
                {
                    if (_messageQueueTagSmartReaderTagEventUsbDrive.Count < 1000)
                        _messageQueueTagSmartReaderTagEventUsbDrive.Enqueue(dataToPublish);
                }
                catch (Exception)
                {
                }


            if (string.Equals("1", _standaloneConfigDTO.udpServer, StringComparison.OrdinalIgnoreCase))
                try
                {
                    if (_messageQueueTagSmartReaderTagEventUdpServer.Count < 1000)
                        _messageQueueTagSmartReaderTagEventUdpServer.Enqueue(dataToPublish);
                }
                catch (Exception)
                {
                }

            if (string.Equals("1", _standaloneConfigDTO.httpPostEnabled, StringComparison.OrdinalIgnoreCase))
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

            if (string.Equals("1", _standaloneConfigDTO.mqttEnabled, StringComparison.OrdinalIgnoreCase)
                && !string.Equals("127.0.0.1", _standaloneConfigDTO.mqttBrokerAddress, StringComparison.OrdinalIgnoreCase))
                try
                {
                    if (!string.IsNullOrEmpty(_standaloneConfigDTO.mqttTagEventsTopic))
                    {
                        if (_messageQueueTagSmartReaderTagEventMqtt.Count < 1000)
                            _messageQueueTagSmartReaderTagEventMqtt.Enqueue(dataToPublish);
                    }

                }
                catch (Exception)
                {
                }

            if (string.Equals("1", _standaloneConfigDTO.serialPort, StringComparison.OrdinalIgnoreCase))
                try
                {
                    var line = ExtractLineFromJsonObject(dataToPublish);
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

            if (string.Equals("1", _standaloneConfigDTO.enableOpcUaClient, StringComparison.OrdinalIgnoreCase))
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
    }

    public async Task<bool> WriteMqttCommandIdToFile(string commandId)
    {
        try
        {
            File.WriteAllText("/customer/config/mqtt-command-id", commandId);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Unexpected error on WriteMqttCommandIdToFile");
            //MqttLog("ERROR", "Unexpected error on WriteMqttCommandIdToFile " + ex.Message);
            return false;
        }
    }

    public async Task<string> ReadMqttCommandIdFromFile()
    {
        try
        {
            if (File.Exists("/customer/config/mqtt-command-id"))
            {
                var fileText = File.ReadAllText("/customer/config/mqtt-command-id");
                return fileText;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Unexpected error on ReadMqttCommandIdFromFile");
            //MqttLog("ERROR", "Unexpected error on ReadMqttCommandIdFromFile " + ex.Message);
        }

        return null;
    }

    public async Task<bool> FilterMatchingEpcforSoftwareFilter(string epc)
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

    public async Task<bool> FilterTagIdForSoftwareFilter(string epc)
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

    public async Task<bool> FilterMatchingAntennaAndEpcforPositioning(TagRead tagRead)
    {
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
    }

    public async Task<bool> FilterMatchingAntennaAndEpcforNonPositioning(TagRead tagRead)
    {
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

        return false;
    }

    public async Task SavePositioningDataAsync(TagRead tagRead, bool IsPositioningHeader)
    {
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

                dbContext.PostioningEpcs.Update(postioningEpcsData);
                await dbContext.SaveChangesAsync();
            }
            else
            {
                postioningEpcsData = new PostioningEpcs();
                postioningEpcsData.Epc = tagRead.Epc;
                postioningEpcsData.PeakRssi = tagRead.PeakRssi;
                postioningEpcsData.AntennaPort = tagRead.AntennaPort.Value;
                postioningEpcsData.TxPower = tagRead.TxPower;
                postioningEpcsData.FirstSeenTimestamp = tagRead.FirstSeenTimestamp;
                postioningEpcsData.LastSeenTimestamp = tagRead.FirstSeenTimestamp;
                postioningEpcsData.FirstSeenOn =
                    DateTimeOffset.FromUnixTimeMilliseconds(tagRead.FirstSeenTimestamp.Value);
                postioningEpcsData.LastSeenOn =
                    DateTimeOffset.FromUnixTimeMilliseconds(tagRead.FirstSeenTimestamp.Value);
                postioningEpcsData.AntennaZone = tagRead.AntennaZone;
                postioningEpcsData.RfChannel = tagRead.RfChannel;
                postioningEpcsData.RfPhase = tagRead.RfPhase;
                postioningEpcsData.IsPositioningHeader = IsPositioningHeader;

                dbContext.PostioningEpcs.Add(postioningEpcsData);
                await dbContext.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error on SavePositioningDataAsync. " + ex.Message);
        }
    }

    public async Task<TagReadPositioning> GetPositioningDataToReportAsync(string epc)
    {
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
                    dbContext.PostioningEpcs.Update(positioningEpc);
                    await dbContext.SaveChangesAsync();
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
    }

    public async Task<StandaloneConfigDTO> GetConfigDtoFromDb()
    {
        StandaloneConfigDTO model = null;
        try
        {
            using var scope = Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<RuntimeDb>();
            var configModel = dbContext.ReaderConfigs.FindAsync("READER_CONFIG").Result;
            if (configModel != null)
            {
                var savedConfigDTO = JsonConvert.DeserializeObject<StandaloneConfigDTO>(configModel.Value);
                if (savedConfigDTO != null) model = savedConfigDTO;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error on GetConfigDtoFromDb. " + ex.Message);
        }

        return model;
    }

    public async void SaveStartPresetCommandToDb()
    {
        try
        {
            _logger.LogInformation("Requesting Start Preset Command... ", SeverityType.Debug);
            await readLock.WaitAsync();
            using var scope = Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<RuntimeDb>();
            var commandModel = dbContext.ReaderCommands.FindAsync("START_PRESET").Result;
            if (commandModel == null)
            {
                commandModel = new ReaderCommands();
                commandModel.Id = "START_PRESET";
                commandModel.Value = "START";
                commandModel.Timestamp = DateTime.Now;
                dbContext.ReaderCommands.Add(commandModel);
            }
            else
            {
                commandModel.Value = "START";
                commandModel.Timestamp = DateTime.Now;
                dbContext.ReaderCommands.Update(commandModel);
            }

            await dbContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError("Unexpected error on SaveStartPresetCommandToDb. " + ex.Message);
        }
        finally
        {
            readLock.Release();
        }
    }

    public async void SaveStopPresetCommandToDb()
    {
        try
        {
            _logger.LogInformation("Requesting Stop Preset Command... ", SeverityType.Debug);
            await readLock.WaitAsync();
            using var scope = Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<RuntimeDb>();
            var commandModel = dbContext.ReaderCommands.FindAsync("STOP_PRESET").Result;
            if (commandModel == null)
            {
                commandModel = new ReaderCommands();
                commandModel.Id = "STOP_PRESET";
                commandModel.Value = "STOP";
                commandModel.Timestamp = DateTime.Now;
                dbContext.ReaderCommands.Add(commandModel);
            }
            else
            {
                commandModel.Value = "STOP";
                commandModel.Timestamp = DateTime.Now;
                dbContext.ReaderCommands.Update(commandModel);
            }

            await dbContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError("Unexpected error on SaveStopPresetCommandToDb. " + ex.Message);
        }
        finally
        {
            readLock.Release();
        }
    }

    public async void SaveStartCommandToDb()
    {
        try
        {
            _logger.LogInformation("Requesting Start Command... ", SeverityType.Debug);
            await readLock.WaitAsync();
            using var scope = Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<RuntimeDb>();
            var commandModel = dbContext.ReaderCommands.FindAsync("START_INVENTORY").Result;
            if (commandModel == null)
            {
                commandModel = new ReaderCommands();
                commandModel.Id = "START_INVENTORY";
                commandModel.Value = "START";
                commandModel.Timestamp = DateTime.Now;
                dbContext.ReaderCommands.Add(commandModel);
            }
            else
            {
                commandModel.Value = "START";
                commandModel.Timestamp = DateTime.Now;
                dbContext.ReaderCommands.Update(commandModel);
            }

            await dbContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError("Unexpected error on SaveStartCommandToDb. " + ex.Message);
        }
        finally
        {
            readLock.Release();
        }
    }

    public async void SaveStopCommandToDb()
    {
        try
        {
            _logger.LogInformation("Requesting Stop Command... ", SeverityType.Debug);
            await readLock.WaitAsync();
            using var scope = Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<RuntimeDb>();
            var commandModel = dbContext.ReaderCommands.FindAsync("STOP_INVENTORY").Result;
            if (commandModel == null)
            {
                commandModel = new ReaderCommands();
                commandModel.Id = "STOP_INVENTORY";
                commandModel.Value = "STOP";
                commandModel.Timestamp = DateTime.Now;
                dbContext.ReaderCommands.Add(commandModel);
            }
            else
            {
                commandModel.Value = "STOP";
                commandModel.Timestamp = DateTime.Now;
                dbContext.ReaderCommands.Update(commandModel);
            }

            await dbContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError("Unexpected error on SaveStopCommandToDb. " + ex.Message);
        }
        finally
        {
            readLock.Release();
        }
    }

    public async void SaveUpgradeCommandToDb(string remoteUrl)
    {
        try
        {
            _logger.LogInformation("Requesting Upgrade Command... ", SeverityType.Debug);
            await readLock.WaitAsync();
            using var scope = Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<RuntimeDb>();
            var commandModel = dbContext.ReaderCommands.FindAsync("UPGRADE_SYSTEM_IMAGE").Result;
            if (commandModel == null)
            {
                commandModel = new ReaderCommands();
                commandModel.Id = "UPGRADE_SYSTEM_IMAGE";
                commandModel.Value = remoteUrl;
                commandModel.Timestamp = DateTime.Now;
                dbContext.ReaderCommands.Add(commandModel);
            }
            else
            {
                commandModel.Value = remoteUrl;
                commandModel.Timestamp = DateTime.Now;
                dbContext.ReaderCommands.Update(commandModel);
            }

            await dbContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError("Unexpected error on SaveUpgradeCommandToDb. " + ex.Message);
        }
        finally
        {
            readLock.Release();
        }
    }

    public async void SaveInventoryStatusToDb(string status, int currentCount, string cycleId, bool isStopRequest)
    {
        try
        {
            await readLock.WaitAsync();
            using var scope = Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<RuntimeDb>();
            var dataModel = dbContext.InventoryStatus.FindAsync("INVENTORY_STATUS").Result;
            if (dataModel == null)
            {
                dataModel = new InventoryStatus();
                dataModel.Id = "INVENTORY_STATUS";
                dataModel.CurrentStatus = status;
                dataModel.TotalCount = currentCount;
                dataModel.CycleId = cycleId;
                dataModel.StartedOn = DateTimeOffset.Now;
                if (isStopRequest)
                    dataModel.StoppedOn = DateTimeOffset.Now;
                else
                    dataModel.StoppedOn = null;
                dbContext.InventoryStatus.Add(dataModel);
            }
            else
            {
                dataModel.CurrentStatus = status;
                dataModel.TotalCount = dataModel.TotalCount + currentCount;
                dataModel.CycleId = cycleId;
                dataModel.StartedOn = DateTime.Now;
                if (isStopRequest)
                    dataModel.StoppedOn = DateTimeOffset.Now;
                else
                    dataModel.StoppedOn = null;
                dbContext.InventoryStatus.Update(dataModel);
            }

            await dbContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error on SaveInventoryStatusToDb. " + ex.Message);
        }
        finally
        {
            readLock.Release();
        }
    }

    public async void SaveJsonTagEventToDb(JObject dto)
    {
        try
        {
            await readLock.WaitAsync();
            using var scope = Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<RuntimeDb>();
            var configModel = new SmartReaderTagReadModel();
            configModel.Value = JsonConvert.SerializeObject(dto);
            dbContext.SmartReaderTagReadModels.Add(configModel);
            await dbContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error on SaveJsonTagEventToDb. " + ex.Message);
        }
        finally
        {
            readLock.Release();
        }
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
            readLock.Release();
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

    public async void SaveConfigDtoToDb(StandaloneConfigDTO dto)
    {
        try
        {
            await readLock.WaitAsync();
            using var scope = Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<RuntimeDb>();
            var configModel = dbContext.ReaderConfigs.FindAsync("READER_CONFIG").Result;
            if (configModel == null)
            {
                configModel = new ReaderConfigs();
                configModel.Id = "READER_CONFIG";
                configModel.Value = JsonConvert.SerializeObject(dto);
                dbContext.ReaderConfigs.Add(configModel);
            }
            else
            {
                configModel.Value = JsonConvert.SerializeObject(dto);
                dbContext.ReaderConfigs.Update(configModel);
            }

            await dbContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error on SaveConfigDtoToDb. " + ex.Message);
        }
        finally
        {
            readLock.Release();
        }
    }

    public async Task<string> GetLicenseFromDb()
    {
        var license = "";
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RuntimeDb>();
        var configModel = dbContext.ReaderConfigs.FindAsync("READER_LICENSE").Result;
        if (configModel != null) license = configModel.Value;
        return license;
    }

    public async void SaveLicenseToDb(string license)
    {
        try
        {
            await readLock.WaitAsync();
            using var scope = Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<RuntimeDb>();
            var configModel = dbContext.ReaderConfigs.FindAsync("READER_LICENSE").Result;
            if (configModel == null)
            {
                configModel = new ReaderConfigs();
                configModel.Id = "READER_LICENSE";
                configModel.Value = license;
                dbContext.ReaderConfigs.Add(configModel);
            }
            else
            {
                configModel.Value = license;
                dbContext.ReaderConfigs.Update(configModel);
            }

            await dbContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError("Unexpected error on SaveLicenseToDb. " + ex.Message);
        }
        finally
        {
            readLock.Release();
        }
    }

    public async Task<List<LastPositioningReferecenceEpc>> GetLastPositioningDataAsync(long currentEventTimestamp,
        bool lookupForPositioningHeaderOnly)
    {
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
                    var positioningEpc = new LastPositioningReferecenceEpc();
                    positioningEpc.AntennaPort = postioningEpcsList[i].AntennaPort;
                    positioningEpc.Epc = postioningEpcsList[i].Epc;
                    positioningEpc.AntennaZone = postioningEpcsList[i].AntennaZone;
                    positioningEpc.LastPositionEpcAntTimestamp = postioningEpcsList[i].LastSeenTimestamp;
                    positioningEpc.PeakRssi = postioningEpcsList[i].PeakRssi;
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
                            dbContext.PostioningEpcs.Remove(postioningEpcsList[i]);
                            await dbContext.SaveChangesAsync();
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
    }

    public async Task UpdatePositioningDataAsync()
    {
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
                    dbContext.PostioningEpcs.Remove(postioningEpcsList[i]);
                    await dbContext.SaveChangesAsync();
                }
        }
    }

    public JArray GetIotInterfaceTagEventReport(JObject smartReaderTagEventData)
    {
        JArray iotTagEventReportObject = new JArray();
        try
        {
            List<object> iotTagEventReport = new List<object>();

            string hostname = "";
            string eventType = "tagInventory";


            if (smartReaderTagEventData.ContainsKey("readerName"))
            {
                hostname = smartReaderTagEventData["readerName"].Value<String>();
            }

            if (smartReaderTagEventData.ContainsKey("readerName"))
            {
                foreach (JObject currentEventItem in smartReaderTagEventData["tag_reads"])
                {
                    long firstSeenTimestamp = currentEventItem["firstSeenTimestamp"].Value<long>() / 1000;
                    var dateTimeOffsetLastSeenTimestamp = DateTimeOffset.FromUnixTimeMilliseconds(firstSeenTimestamp);

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
                    Dictionary<object, object> iotEvent = new Dictionary<object, object>();
                    iotEvent.Add("timestamp", dateTimeOffsetLastSeenTimestamp.ToString("o"));
                    iotEvent.Add("hostname", hostname);
                    iotEvent.Add("eventType", eventType);

                    Dictionary<object, object> iotEventItem = new Dictionary<object, object>();
                    iotEventItem.Add("epcHex", tagEpc);
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
    }

    public bool IsOnPause()
    {
        var pauseRequest = Path.Combine("/customer", "pause-request.txt");
        return File.Exists(pauseRequest);
    }
}