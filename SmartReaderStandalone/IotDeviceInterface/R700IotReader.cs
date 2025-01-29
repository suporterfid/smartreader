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
using Microsoft.Extensions.Logging;
using plugin_contract.ViewModel.Gpi;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.NetworkInformation;
using System.Net.Security;
using System.Net.Sockets;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using NetworkInterface = Impinj.Atlas.NetworkInterface;

namespace SmartReader.IotDeviceInterface;

public class R700IotReader : IR700IotReader
{
    private readonly IotDeviceInterfaceEventProcessor _r700IotEventProcessor;

    private readonly ILogger<R700IotReader> _logger;

    private readonly ILoggerFactory _loggerFactory;

    internal R700IotReader(
        string hostname,
        string nickname = "",
        bool useHttpAlways = false,
        bool useBasicAuthAlways = false,
        string uname = "root",
        string pwd = "impinj",
        int hostPort = 0,
        string? proxy = "",
        int proxyPort = 8080,
        ILogger<R700IotReader>? eventProcessorLogger = null,
        ILoggerFactory loggerFactory = null
        )
    {
        _logger = (eventProcessorLogger ?? throw new ArgumentNullException(nameof(eventProcessorLogger)));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));

        if (string.IsNullOrWhiteSpace(hostname))
            throw new ArgumentNullException(nameof(hostname));

        _r700IotEventProcessor = new IotDeviceInterfaceEventProcessor(
                hostname,
                useHttpAlways,
                useBasicAuthAlways,
                uname,
                pwd,
                hostPort,
                proxy ?? "",
                proxyPort,
                _loggerFactory.CreateLogger<IotDeviceInterfaceEventProcessor>(),
                _loggerFactory,
                () => IsNetworkConnected // Pass delegate
            );

        Hostname = hostname;
        Nickname = nickname;
    }

    public List<int> RxSensitivitiesInDbm { get; set; }
    public string UniqueId { get; set; }
    public string MacAddress { get; set; }
    public string ProductModel { get; set; }
    public List<double> TxPowersInDbm { get; set; }
    public string ReaderOperatingRegion { get; set; }
    public List<string> IpAddresses { get; set; }

    public string Hostname { get; }

    public string Nickname { get; set; }

    public string DisplayName => !string.IsNullOrWhiteSpace(Nickname) ? Nickname : Hostname;

    public uint ModelNumber { get; }

    public double MinPowerStepDbm { get; } = 0.25;

    public bool IsAntennaHubEnabled { get; private set; }

    public bool IsNetworkConnected { get; private set; }

    public void Dispose()
    {
        if (_r700IotEventProcessor != null)
        {
            // Dispose the event processor since it contains HTTP clients and streams
            _r700IotEventProcessor.Dispose();

            // Unsubscribe from events to prevent memory leaks
            _r700IotEventProcessor.TagInventoryEvent -= R700IotEventProcessorOnTagInventoryEvent;
            _r700IotEventProcessor.StreamingErrorEvent -= R700IotEventProcessorOnStreamingErrorEvent;
            _r700IotEventProcessor.GpiTransitionEvent -= R700IotEventProcessorOnGpiTransitionEvent;
            _r700IotEventProcessor.InventoryStatusEvent -= R700IotEventProcessorOnInventoryStatusEvent;
            _r700IotEventProcessor.DiagnosticEvent -= R700IotEventProcessorOnDiagnosticEvent;
        }


        GC.SuppressFinalize(this);
    }

    public async Task<ReaderStatus> GetStatusAsync()
    {
        var statusAsync = await _r700IotEventProcessor.GetStatusAsync();
        UniqueId = statusAsync.SerialNumber;
        try
        {

            var iFaces = await _r700IotEventProcessor.GetReaderSystemNetworkInterfacesAsync();
            if (iFaces != null && iFaces.Any())
            {
                // where  i.InterfaceType == NetworkInterfaceInterfaceType.Eth && i.InterfaceName.Equals("eth0")
                var selectedIfaces = iFaces.Where(i => i.Enabled == true).ToList();
                if (selectedIfaces != null && selectedIfaces.Any())
                {
                    var selectedIface = selectedIfaces.FirstOrDefault();
                    MacAddress = selectedIface?.HardwareAddress ?? string.Empty;
                }
            }

            if (IpAddresses == null)
            {
                IpAddresses = new List<string>();
            }
            else
            {
                IpAddresses.Clear();
            }

            IsNetworkConnected = false;
            foreach (var netInterface in iFaces ?? Enumerable.Empty<NetworkInterface>())
            {
                if (netInterface != null
                    && netInterface.NetworkAddress != null
                    && netInterface.NetworkAddress.Count > 0)
                {
                    if (netInterface.Status == NetworkInterfaceStatus.Connected)
                    {
                        try
                        {
                            using (Ping ping = new())
                            {
                                foreach (var networkAddress in netInterface.NetworkAddress)
                                {
                                    string hostName = networkAddress.Gateway;
                                    if (!string.IsNullOrEmpty(hostName))
                                    {
                                        PingReply reply = await ping.SendPingAsync(hostName, 1000);
                                        //Console.WriteLine($"Ping status for ({hostName}): {reply.Status}");
                                        if (reply is { Status: IPStatus.Success })
                                        {
                                            IsNetworkConnected = true;
                                        }
                                    }
                                }

                            }
                        }
                        catch (Exception)
                        {
                        }

                    }
                    foreach (var networkAddress in netInterface.NetworkAddress)
                    {
                        IpAddresses.Add(networkAddress.Address);
                    }

                }
            }
        }
        catch (Exception)
        {
            IsNetworkConnected = true;
        }

        return statusAsync;
    }

    public async Task<AntennaHubInfo> GetSystemAntennaHubInfoAsync()
    {
        IsAntennaHubEnabled = false;
        var antennaHubInfoAsync = await _r700IotEventProcessor.GetSystemAntennaHubInfoAsync();
        var antennaHubStatus = antennaHubInfoAsync.Status;
        //if (antennaHubStatus != null && antennaHubStatus == AntennaHubInfoStatus.Enabled) IsAntennaHubEnabled = true;
        IsAntennaHubEnabled = antennaHubStatus == AntennaHubInfoStatus.Enabled;

        return antennaHubInfoAsync;
    }

    public async Task<RegionInfo> GetSystemRegionInfoAsync()
    {
        var regionInfoAsync = await _r700IotEventProcessor.GetSystemRegionInfoAsync();
        ReaderOperatingRegion = regionInfoAsync.OperatingRegion;
        //var regionInfo = regionInfoAsync.OperatingRegion;
        return regionInfoAsync;
    }

    public async Task<PowerConfiguration> GetSystemPowerAsync()
    {
        var powerInfoAsync = await _r700IotEventProcessor.GetSystemPowerAsync();

        //var regionInfo = regionInfoAsync.OperatingRegion;
        return powerInfoAsync;
    }


    public async Task<SystemInfo> GetSystemInfoAsync()
    {
        var systemInfoAsync = await _r700IotEventProcessor.GetSystemInfoAsync();
        ProductModel = systemInfoAsync.ProductModel;
        return systemInfoAsync;
    }

    public Task StartAsync(string presetId)
    {
        _r700IotEventProcessor.TagInventoryEvent += R700IotEventProcessorOnTagInventoryEvent;
        _r700IotEventProcessor.StreamingErrorEvent += R700IotEventProcessorOnStreamingErrorEvent;
        _r700IotEventProcessor.GpiTransitionEvent += R700IotEventProcessorOnGpiTransitionEvent;
        _r700IotEventProcessor.InventoryStatusEvent += R700IotEventProcessorOnInventoryStatusEvent;
        _r700IotEventProcessor.DiagnosticEvent += R700IotEventProcessorOnDiagnosticEvent;

        return _r700IotEventProcessor.StartAsync(presetId);
    }

    public Task StartPresetAsync(string presetId)
    {
        return _r700IotEventProcessor.StartPresetAsync(presetId);
    }

    //private void R700IotEventProcessorOnTagInventoryEventConfiguration(object sender, Impinj.Atlas.TagInventoryEventConfiguration e) => this.OnTagInventoryEventConfiguration(e);

    public Task StopPresetAsync()
    {
        return _r700IotEventProcessor.StopPresetAsync();
    }

    public async Task StopAsync()
    {
        var r700IotReader = this;
        try
        {
            await r700IotReader._r700IotEventProcessor.StopAsync();
        }
        catch (ObjectDisposedException)
        {
        }

        try
        {
            r700IotReader._r700IotEventProcessor.TagInventoryEvent -=
                r700IotReader.R700IotEventProcessorOnTagInventoryEvent;
            r700IotReader._r700IotEventProcessor.StreamingErrorEvent -=
                r700IotReader.R700IotEventProcessorOnStreamingErrorEvent;

            _r700IotEventProcessor.GpiTransitionEvent -= R700IotEventProcessorOnGpiTransitionEvent;
            _r700IotEventProcessor.InventoryStatusEvent -= R700IotEventProcessorOnInventoryStatusEvent;
            _r700IotEventProcessor.DiagnosticEvent -= R700IotEventProcessorOnDiagnosticEvent;
        }
        catch (Exception)
        {
        }
    }

    public Task<ObservableCollection<string>> GetReaderInventoryPresetListAsync()
    {
        return _r700IotEventProcessor.GetReaderInventoryPresetListAsync();
    }

    public Task<InventoryRequest> GetReaderInventoryPresetAsync(string presetId)
    {
        return _r700IotEventProcessor.GetReaderInventoryPresetAsync(presetId);
    }

    public Task SaveInventoryPresetAsync(string presetId, InventoryRequest inventoryRequest)
    {
        return _r700IotEventProcessor.SaveInventoryPresetAsync(presetId, inventoryRequest);
    }

    public Task UpdateInventoryPresetAsync(
        string presetId,
        InventoryRequest updatedInventoryRequest)
    {
        return _r700IotEventProcessor.UpdateReaderInventoryPresetAsync(presetId, updatedInventoryRequest);
    }


    public Task DeleteInventoryPresetAsync(string presetId)
    {
        return _r700IotEventProcessor.DeleteReaderInventoryPresetAsync(presetId);
    }

    public Task<object> GetReaderInventorySchemaAsync()
    {
        return _r700IotEventProcessor.GetReaderInventorySchemaAsync();
    }

    public Task<ObservableCollection<string>> GetSupportedReaderProfilesAsync()
    {
        return _r700IotEventProcessor.GetReaderProfilesAsync();
    }

    public event EventHandler<TagInventoryEvent>? TagInventoryEvent;

    public event EventHandler<IotDeviceInterfaceException>? StreamingErrorEvent;

    public event EventHandler<GpiTransitionVm>? GpiTransitionEvent;

    public event EventHandler<DiagnosticEvent>? DiagnosticEvent;

    public event EventHandler<InventoryStatusEvent>? InventoryStatusEvent;

    private readonly object _eventLock = new();


    public Task SendCustomInventoryPresetAsync(string presetId, string jsonInventoryRequest)
    {
        try
        {
            return _r700IotEventProcessor.UpdateReaderInventoryPresetAsync(presetId, jsonInventoryRequest);
        }
        catch (AtlasException ex) when (ex.StatusCode == 403 || ex.StatusCode == 204)
        {
            return Task.CompletedTask;
        }


    }

    public Task PostTransientInventoryPresetAsync(string jsonInventoryRequest)
    {
        try
        {
            _r700IotEventProcessor.TagInventoryEvent += R700IotEventProcessorOnTagInventoryEvent;
            _r700IotEventProcessor.StreamingErrorEvent += R700IotEventProcessorOnStreamingErrorEvent;
            return _r700IotEventProcessor.PostTransientInventoryPresetAsync(jsonInventoryRequest);
        }
        catch (AtlasException ex) when (ex.StatusCode == 403 || ex.StatusCode == 204)
        {
            try
            {
                lock (_eventLock)
                {
                    _r700IotEventProcessor.TagInventoryEvent -= R700IotEventProcessorOnTagInventoryEvent;
                    _r700IotEventProcessor.StreamingErrorEvent -= R700IotEventProcessorOnStreamingErrorEvent;
                    _r700IotEventProcessor.GpiTransitionEvent -= R700IotEventProcessorOnGpiTransitionEvent;
                    _r700IotEventProcessor.InventoryStatusEvent -= R700IotEventProcessorOnInventoryStatusEvent;
                    _r700IotEventProcessor.DiagnosticEvent -= R700IotEventProcessorOnDiagnosticEvent;
                }
            }
            catch (Exception)
            {
            }
            return Task.CompletedTask;
        }
    }

    public Task PostTransientInventoryStopPresetsAsync()
    {
        try
        {
            try
            {
                lock (_eventLock)
                {
                    _r700IotEventProcessor.TagInventoryEvent -= R700IotEventProcessorOnTagInventoryEvent;
                    _r700IotEventProcessor.StreamingErrorEvent -= R700IotEventProcessorOnStreamingErrorEvent;
                    _r700IotEventProcessor.GpiTransitionEvent -= R700IotEventProcessorOnGpiTransitionEvent;
                    _r700IotEventProcessor.InventoryStatusEvent -= R700IotEventProcessorOnInventoryStatusEvent;
                    _r700IotEventProcessor.DiagnosticEvent -= R700IotEventProcessorOnDiagnosticEvent;
                }
            }
            catch (Exception)
            {
            }

            return _r700IotEventProcessor.PostStopInventoryPresetAsync();
        }
        catch (AtlasException ex) when (ex.StatusCode == 403 || ex.StatusCode == 204)
        {
            try
            {
                //_r700IotEventProcessor.TagInventoryEvent -= R700IotEventProcessorOnTagInventoryEvent;
                //_r700IotEventProcessor.StreamingErrorEvent -= R700IotEventProcessorOnStreamingErrorEvent;
                lock (_eventLock)
                {
                    _r700IotEventProcessor.TagInventoryEvent -= R700IotEventProcessorOnTagInventoryEvent;
                    _r700IotEventProcessor.StreamingErrorEvent -= R700IotEventProcessorOnStreamingErrorEvent;
                    _r700IotEventProcessor.GpiTransitionEvent -= R700IotEventProcessorOnGpiTransitionEvent;
                    _r700IotEventProcessor.InventoryStatusEvent -= R700IotEventProcessorOnInventoryStatusEvent;
                    _r700IotEventProcessor.DiagnosticEvent -= R700IotEventProcessorOnDiagnosticEvent;
                }
            }
            catch (Exception)
            {
            }
            return Task.CompletedTask;
        }
    }

    public Task<TimeInfo> DeviceReaderTimeGetAsync()
    {
        return _r700IotEventProcessor.DeviceReaderTimeGetAsync();
    }

    public Task<ReaderStatus> DeviceReaderStatusGetAsync()
    {
        return _r700IotEventProcessor.DeviceReaderStatusGetAsync();
    }

    public Task<RfidInterface> DeviceReaderRfidInterfaceGetAsync()
    {
        return _r700IotEventProcessor.DeviceReaderRfidInterfaceGetAsync();
    }

    public Task UpdateReaderRfidInterface(RfidInterface rfidInterface)
    {
        return _r700IotEventProcessor.UpdateReaderRfidInterface(rfidInterface);
    }

    public Task<MqttConfiguration> GetReaderMqttAsync()
    {
        return _r700IotEventProcessor.GetReaderMqttAsync();
    }

    public Task UpdateReaderMqttAsync(MqttConfiguration mqttConfiguration)
    {
        return _r700IotEventProcessor.UpdateReaderMqttAsync(mqttConfiguration);
    }

    public Task<GpoConfigurations> DeviceGposGetAsync()
    {
        return _r700IotEventProcessor.DeviceGposGetAsync();
    }

    public Task<object> DeviceGetInventoryPresetsSchemaAsync()
    {
        return _r700IotEventProcessor.DeviceGetInventoryPresetsSchemaAsync();
    }


    public Task UpdateReaderGpiAsync(GpiConfigRoot gpiConfiguration)
    {
        return _r700IotEventProcessor.UpdateReaderGpiAsync(gpiConfiguration);
    }

    public Task UpdateReaderGpoAsync(GpoConfigurations gpoConfiguration)
    {
        return _r700IotEventProcessor.UpdateReaderGpoAsync(gpoConfiguration);
    }

    public Task SystemImageUpgradePostAsync(string file)
    {
        return _r700IotEventProcessor.SystemImageUpgradePostAsync(file);
    }

    private void R700IotEventProcessorOnStreamingErrorEvent(object? sender, IotDeviceInterfaceException e)
    {
        OnStreamingErrorEvent(e);
    }

    private void R700IotEventProcessorOnTagInventoryEvent(object? sender, TagInventoryEvent e)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            _logger.LogDebug("TagInventoryEvent received: {@Event}", e);

            if (e == null)
            {
                _logger.LogWarning("Received null TagInventoryEvent.");
                return;
            }

            if (string.IsNullOrWhiteSpace(e.Epc))
            {
                _logger.LogWarning("TagInventoryEvent received with invalid TagId: {@Event}", e);
                return;
            }

            // Invoke the event handler
            OnTagInventoryEvent(e);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while processing TagInventoryEvent: {Message}", ex.Message);
        }
        finally
        {
            stopwatch.Stop();
            _logger.LogDebug("Processed TagInventoryEvent in {ElapsedMilliseconds} ms", stopwatch.ElapsedMilliseconds);
        }
    }



    private void R700IotEventProcessorOnGpiTransitionEvent(object?sender, GpiTransitionVm e)
    {
        OnGpiTransitionEvent(e);
    }

    private void R700IotEventProcessorOnInventoryStatusEvent(object? sender, InventoryStatusEvent e)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            _logger.LogDebug("InventoryStatusEvent received: {@Event}", e);

            // Validate the event
            if (e == null)
            {
                _logger.LogWarning("Received null InventoryStatusEvent.");
                return;
            }

            if (string.IsNullOrWhiteSpace(e.Status.ToString()))
            {
                _logger.LogWarning("InventoryStatusEvent received with invalid status: {@Event}", e);
                return;
            }

            // Process the event
            OnInventoryStatusEvent(e);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while processing InventoryStatusEvent: {Message}", ex.Message);
        }
        finally
        {
            stopwatch.Stop();
            _logger.LogDebug("Processed InventoryStatusEvent in {ElapsedMilliseconds} ms", stopwatch.ElapsedMilliseconds);
        }
    }


    private void R700IotEventProcessorOnDiagnosticEvent(object? sender, DiagnosticEvent e)
    {
        if (e == null)
        {
            _logger.LogWarning("R700IotEventProcessorOnDiagnosticEvent was invoked with a null DiagnosticEvent.");
            return;
        }

        try
        {
            _logger.LogDebug("Processing DiagnosticEvent: {@Event}", e);

            // Invoke the OnDiagnosticEvent handler
            OnDiagnosticEvent(e);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while processing the DiagnosticEvent: {Message}", ex.Message);
        }
    }


    protected virtual void OnTagInventoryEvent(TagInventoryEvent e)
    {
        var handler = TagInventoryEvent; // Copy to avoid race conditions
        if (handler != null)
        {
            handler(this, e);
        }
        else
        {
            _logger.LogWarning("No subscribers for TagInventoryEvent.");
        }
    }

    protected virtual void OnStreamingErrorEvent(IotDeviceInterfaceException e)
    {
        if (e == null)
        {
            _logger.LogWarning("OnStreamingErrorEvent invoked with a null exception.");
            return;
        }

        try
        {
            var handler = StreamingErrorEvent; // Copy delegate to avoid race conditions
            if (handler != null)
            {
                _logger.LogDebug("Raising StreamingErrorEvent: {@Exception}", e);
                handler(this, e);
            }
            else
            {
                _logger.LogWarning("No subscribers for StreamingErrorEvent.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while invoking the StreamingErrorEvent handler: {Message}", ex.Message);
        }
    }


    private void OnGpiTransitionEvent(GpiTransitionVm e)
    {
        if (e == null)
        {
            _logger.LogWarning("OnGpiTransitionEvent was invoked with a null event.");
            return;
        }

        try
        {
            var handler = GpiTransitionEvent; // Copy delegate to avoid race conditions
            if (handler != null)
            {
                _logger.LogDebug("Raising GpiTransitionEvent: {@Event}", e);
                handler(this, e);
            }
            else
            {
                _logger.LogWarning("No subscribers for GpiTransitionEvent.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while invoking the GpiTransitionEvent handler: {Message}", ex.Message);
        }
    }


    private void OnDiagnosticEvent(DiagnosticEvent e)
    {
        if (e == null)
        {
            _logger.LogWarning("OnDiagnosticEvent was invoked with a null event.");
            return;
        }

        try
        {
            var handler = DiagnosticEvent; // Copy delegate to avoid race conditions
            if (handler != null)
            {
                _logger.LogDebug("Raising DiagnosticEvent: {@Event}", e);
                handler(this, e);
            }
            else
            {
                _logger.LogWarning("No subscribers for DiagnosticEvent.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while invoking the DiagnosticEvent handler: {Message}", ex.Message);
        }
    }

    

    private void OnInventoryStatusEvent(InventoryStatusEvent e)
    {
        if (e == null)
        {
            _logger.LogWarning("OnInventoryStatusEvent was invoked with a null event.");
            return;
        }

        try
        {
            // Log that the event is being processed
            _logger.LogDebug("Processing InventoryStatusEvent: {@Event}", e);

            // Thread-safe event invocation
            var handler = InventoryStatusEvent; // Copy delegate to avoid race conditions
            if (handler != null)
            {
                _logger.LogDebug("Raising InventoryStatusEvent to subscribers.");
                handler(this, e);
            }
            else
            {
                _logger.LogWarning("No subscribers for InventoryStatusEvent.");
            }
        }
        catch (Exception ex)
        {
            // Handle and log any exceptions from the subscribers
            _logger.LogError(ex, "An error occurred while invoking InventoryStatusEvent: {Message}", ex.Message);
        }
        finally
        {
            // Log completion of event processing
            _logger.LogDebug("Completed processing InventoryStatusEvent.");
        }
    }



    private sealed class IotDeviceInterfaceEventProcessor
    {
        private readonly string _hostname;
        private readonly HttpClient _httpClient;
        private readonly HttpClient _httpClientSecure;
        private readonly IAtlasClient _iotDeviceInterfaceClient;
        private readonly IAtlasClient _iotDeviceInterfaceClientSecure;
        private readonly bool _useBasicAuthAlways;
        private readonly bool _useHttpAlways;
        private CancellationTokenSource? _cancelSource;
        private Stream? _responseStream;
        private Task? _streamingTask;
        private readonly ILogger<IotDeviceInterfaceEventProcessor> _logger;

        private readonly Func<bool> _isNetworkConnectedDelegate;

        internal IotDeviceInterfaceEventProcessor(
            string hostname,
            bool useHttpAlways,
            bool useBasicAuthAlways,
            string uname,
            string pwd,
            int hostPort = 0,
            string proxy = "",
            int proxyPort = 8080,
            ILogger<IotDeviceInterfaceEventProcessor> logger = null,
            ILoggerFactory loggerFactory = null,
            Func<bool> isNetworkConnectedDelegate = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _isNetworkConnectedDelegate = isNetworkConnectedDelegate ?? throw new ArgumentNullException(nameof(isNetworkConnectedDelegate));


            _hostname = Regex.Replace(hostname, "^https*\\://", "");
            var num = (uint)hostPort > 0U ? 1 : 0;
            _useHttpAlways = useHttpAlways;
            _useBasicAuthAlways = useBasicAuthAlways;
            var baseUrl1 = num != 0
                ? string.Format("http://{0}:{1}/api/v1", hostname, hostPort)
                : "http://" + hostname + "/api/v1";
            var baseUrl2 = "https://" + hostname + "/api/v1";
            var bytes = Encoding.ASCII.GetBytes(uname + ":" + pwd);
            var timeSpan = new TimeSpan(0, 0, 0, 3, 0);
            ServicePointManager.ServerCertificateValidationCallback +=
                (sender, certificate, chain, sslPolicyErrors) => true;


            var httpClientHandler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (HttpRequestMessage message, X509Certificate2? cert,
                    X509Chain? chain, SslPolicyErrors errors) => true
            };


            if (!string.IsNullOrEmpty(proxy))
            {
                WebProxy webProxy = new WebProxy(proxy);
                webProxy.Credentials = CredentialCache.DefaultNetworkCredentials;
                webProxy.BypassProxyOnLocal = true;
                webProxy.BypassList.Append("169.254.1.1");
                webProxy.BypassList.Append(hostname);


                httpClientHandler = new HttpClientHandler
                {
                    Proxy = webProxy,
                    UseProxy = true,
                    ServerCertificateCustomValidationCallback = (HttpRequestMessage message, X509Certificate2? cert,
    X509Chain? chain, SslPolicyErrors errors) => true
                };
            }

            if (useHttpAlways)
            {


                _httpClient = new HttpClient(httpClientHandler)
                {
                    BaseAddress = new Uri(baseUrl2 + "/"),
                    Timeout = timeSpan
                };
                _iotDeviceInterfaceClient = new AtlasClient(baseUrl2, _httpClient);

            }
            else
            {
                _httpClient = new HttpClient(httpClientHandler)
                {
                    BaseAddress = new Uri(baseUrl1 + "/"),
                    Timeout = timeSpan
                };
                _iotDeviceInterfaceClient = new AtlasClient(baseUrl1, _httpClient);
            }

            if (useBasicAuthAlways)
                _httpClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Basic", Convert.ToBase64String(bytes));
            _httpClientSecure = new HttpClient(httpClientHandler)
            {
                BaseAddress = new Uri(baseUrl2 + "/")
            };
            ServicePointManager.ServerCertificateValidationCallback +=
                (sender, certificate, chain, sslPolicyErrors) => true;
            _httpClientSecure.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Basic", Convert.ToBase64String(bytes));
            _iotDeviceInterfaceClientSecure = new AtlasClient(baseUrl2 ?? "", _httpClientSecure);
            _logger = logger;
        }

        private async Task<T> ExecuteWithTimeout<T>(Task<T> task, int timeoutMs)
        {
            if (await Task.WhenAny(task, Task.Delay(timeoutMs)) == task)
            {
                return await task;
            }
            throw new TimeoutException("Operation timed out");
        }

        private void ResetCancellationToken()
        {
            _cancelSource?.Cancel();
            _cancelSource?.Dispose();
            _cancelSource = new CancellationTokenSource();
        }
        public Task<ReaderStatus> GetStatusAsync()
        {
            var readerStatus = new ReaderStatus();
            var systemInfo = _iotDeviceInterfaceClient.SystemAsync().Result;

            var currentInterface = new RfidInterface();
            try
            {
                currentInterface = _iotDeviceInterfaceClient.SystemRfidInterfaceGetAsync().Result;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Error while trying to query the current interface mode.");
            }
            
            if (currentInterface.RfidInterface1 == RfidInterface1.Rest)
            {
                readerStatus = _iotDeviceInterfaceClient.StatusAsync().Result;
            }
            else
            {
                var llrpStatus = _iotDeviceInterfaceClient.SystemRfidLlrpAsync().Result;
                readerStatus.Status = new ReaderStatusStatus();
                if (llrpStatus.LlrpRfidStatus == LlrpStatusLlrpRfidStatus.Active)
                {
                    readerStatus.Status = ReaderStatusStatus.Running;
                }
                else
                {
                    readerStatus.Status = ReaderStatusStatus.Idle;
                }

                readerStatus.SerialNumber = systemInfo.SerialNumber;
                readerStatus.Time = DateTime.Now;

            }
            //_iotDeviceInterfaceClient.SystemRfidLlrpAsync();

            return Task.FromResult(readerStatus);
        }

        public Task<SystemInfo> GetSystemInfoAsync()
        {
            //return _iotDeviceInterfaceClientSecure.SystemAsync();
            try
            {
                return Task.FromResult(ExecuteWithTimeout(_iotDeviceInterfaceClientSecure.SystemAsync(), 5000)
                       .GetAwaiter().GetResult());
            }
            catch (TimeoutException ex)
            {
                _logger.LogError($"Timeout while getting system info: {ex.Message}");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error while getting system info: {ex.Message}");
                throw;
            }
        }

        public Task<AntennaHubInfo> GetSystemAntennaHubInfoAsync()
        {
            bool? pending = null;
            return _iotDeviceInterfaceClientSecure.SystemAntennaHubAsync(pending);
        }

        public Task<RegionInfo> GetSystemRegionInfoAsync()
        {
            var systemRegionCancellationToken = new CancellationToken();
            return _iotDeviceInterfaceClientSecure.SystemRegionGetAsync(systemRegionCancellationToken);
        }

        public Task<PowerConfiguration> GetSystemPowerAsync()
        {
            var systemPowerCancellationToken = new CancellationToken();
            //bool? pending = null;
            return _iotDeviceInterfaceClientSecure.SystemPowerGetAsync(systemPowerCancellationToken);
        }

        public async Task StartAsync(string presetId)
        {
            var iotDeviceInterfaceEventProcessor = this;
            await iotDeviceInterfaceEventProcessor._iotDeviceInterfaceClient.ProfilesStopAsync();
            iotDeviceInterfaceEventProcessor._cancelSource = new CancellationTokenSource();
            iotDeviceInterfaceEventProcessor._streamingTask = Task.Run(iotDeviceInterfaceEventProcessor.StreamingAsync);
            await iotDeviceInterfaceEventProcessor._iotDeviceInterfaceClient
                .ProfilesInventoryPresetsStartAsync(presetId);
        }


        public async Task StartPresetAsync(string presetId)
        {
            try
            {
                await _iotDeviceInterfaceClient.ProfilesInventoryPresetsStartAsync(presetId);
            }
            catch (Exception)
            {
            }
        }

        public async Task StopPresetAsync()
        {
            try
            {
                await _iotDeviceInterfaceClient.ProfilesStopAsync();
            }
            catch (Exception)
            {
            }
        }

        public async Task StopAsync()
        {
            if (_cancelSource == null || _cancelSource.IsCancellationRequested)
            {
                var iotDeviceInterfaceEventProcessor = this;
                iotDeviceInterfaceEventProcessor._cancelSource = new CancellationTokenSource();
            }

            _cancelSource?.Cancel();
            try
            {
                if (_responseStream != null) _responseStream?.Dispose();

                await _iotDeviceInterfaceClient.ProfilesStopAsync();
                try
                {
                    if (_streamingTask != null) await _streamingTask;
                }
                catch (Exception)
                {
                }
            }
            catch (TaskCanceledException)
            {
            }
            catch (Exception ex)
            {
                OnStreamingErrorEvent(
                    new IotDeviceInterfaceException($"Unexpected error - connection error {_hostname}",
    ex));
            }
        }

        private const int MaxRetryAttempts = 3;

        private async Task StreamingAsync()
        {
            ResetCancellationToken();

            while (!_cancelSource?.IsCancellationRequested ?? false)
            {
                int retryCount = 0;
                bool shouldRestart = true;

                while (shouldRestart && !_cancelSource.IsCancellationRequested)
                {
                    try
                    {
                        // Establish the stream
                        using (var responseMessage = await _httpClient.GetAsync(
                            new Uri("data/stream", UriKind.Relative),
                            HttpCompletionOption.ResponseHeadersRead))
                        {
                            if (!responseMessage.IsSuccessStatusCode)
                            {
                                HandleHttpError(responseMessage);
                                break;
                            }

                            _responseStream = await responseMessage.Content.ReadAsStreamAsync();

                            // Process the stream
                            using (var streamReader = new StreamReader(_responseStream))
                            {
                                await ProcessStream(streamReader);
                            }

                            shouldRestart = false; // Successfully completed
                        }
                    }
                    catch (TaskCanceledException)
                    {
                        _logger.LogDebug("Streaming operation was canceled.");
                        shouldRestart = false;
                    }
                    catch (Exception ex)
                    {
                        if (!_cancelSource.IsCancellationRequested && retryCount < MaxRetryAttempts)
                        {
                            retryCount++;
                            await RetryAfterDelay(retryCount, ex);
                        }
                        else
                        {
                            HandleStreamingError(ex);
                            shouldRestart = false;
                        }
                    }
                    finally
                    {
                        CleanupStream();
                    }
                }
            }

            CleanupAfterStreaming();
        }

        private async Task ProcessStream(StreamReader streamReader)
        {
            while (!_cancelSource.IsCancellationRequested)
            {
                try
                {
                    if (streamReader.EndOfStream)
                    {
                        throw new IOException("IoT Interface Processor: End of stream reached unexpectedly.");
                    }

                    var str = await streamReader.ReadLineAsync();
                    if (string.IsNullOrWhiteSpace(str))
                    {
                        _logger.LogWarning("IoT Interface Processor: Received an empty or whitespace-only line.");
                        continue;
                    }

                    _logger.LogDebug("IoT Interface Processor: Processing received line: {Line}", str);

                    await ProcessStreamedData(str);
                }
                catch (IOException ioEx)
                {
                    _logger.LogWarning("IoT Interface Processor: I/O error while reading the stream.");
                    throw;
                }
                catch (JsonException jsonEx)
                {
                    _logger.LogWarning("IoT Interface Processor: Failed to deserialize a line from the stream.");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("IoT Interface Processor: Unexpected error while processing the stream.");
                    //throw;
                }
            }
        }

        private async Task ProcessStreamedData(string str)
        {
            try
            {
                ReaderEvent? readerEvent = null;

                // Determine the type of event
                if (str.Contains("gpiTransitionEvent"))
                {
                    _logger.LogInformation($"IoT Interface Processor: GpiTransitionEvent: \n {str}");
                    //readerEvent = Newtonsoft.Json.JsonConvert.DeserializeObject<ReaderEvent>(str);
                    var gpiTransitionVm = Newtonsoft.Json.JsonConvert.DeserializeObject<GpiTransitionVm>(str);
                    _logger.LogInformation(gpiTransitionVm.ToString());
                    OnGpiTransitionEvent(gpiTransitionVm);
                    return;
                }

                if (!str.Contains("gpiTransitionEvent"))
                {
                    _logger.LogInformation($"IoT Interface Processor: ReaderEvent: \n {str}");
                    readerEvent = Newtonsoft.Json.JsonConvert.DeserializeObject<ReaderEvent>(str);
                }

                if (readerEvent == null)
                {
                    _logger.LogWarning("IoT Interface Processor: Unrecognized or null event: {Line}", str);
                    return;
                }

                // Handle specific events
                if (readerEvent.TagInventoryEvent != null)
                {
                    _logger.LogDebug("IoT Interface Processor - TagInventoryEvent detected: {@TagInventoryEvent}", readerEvent.TagInventoryEvent);
                    OnTagInventoryEvent(readerEvent.TagInventoryEvent);
                }

                if (readerEvent.InventoryStatusEvent != null)
                {
                    UpdateInventoryStatus(readerEvent, str);
                    OnInventoryStatusEvent(readerEvent.InventoryStatusEvent);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "IoT Interface Processor - Error processing streamed data: {Line}", str);
            }
        }

        private void UpdateInventoryStatus(ReaderEvent readerEvent, string str)
        {
            if (str.Contains("running"))
            {
                readerEvent.InventoryStatusEvent.Status = InventoryStatusEventStatus.Running;
            }
            else if (str.Contains("idle"))
            {
                readerEvent.InventoryStatusEvent.Status = InventoryStatusEventStatus.Idle;
            }
        }

        private async Task RetryAfterDelay(int retryCount, Exception ex)
        {
            int delayMs = Math.Min(1000 * (int)Math.Pow(2, retryCount - 1), 30000);
            _logger.LogWarning("IoT Interface Processor - Retrying operation after failure (Attempt: {RetryCount}, Delay: {DelayMs} ms): {ErrorMessage}",
                retryCount, delayMs, ex.Message);

            try
            {
                await Task.Delay(delayMs, _cancelSource.Token);
            }
            catch (TaskCanceledException)
            {
                _logger.LogDebug("IoT Interface Processor - Retry attempt {RetryCount} was canceled.", retryCount);
            }
        }


        //private async Task RetryAfterDelay(int retryCount, Exception ex)
        //{
        //    // Calculate exponential backoff with a maximum delay
        //    const int BaseRetryDelayMs = 1000; // Base delay in milliseconds
        //    const int MaxRetryDelayMs = 30000; // Maximum delay in milliseconds

        //    int delayMs = Math.Min(BaseRetryDelayMs * (int)Math.Pow(2, retryCount - 1), MaxRetryDelayMs);

        //    // Log retry details
        //    _logger.LogWarning(
        //        "Retrying operation after failure. Attempt: {RetryCount}, Delay: {DelayMs} ms, Error: {ErrorMessage}",
        //        retryCount, delayMs, ex.Message);

        //    // Wait for the calculated delay
        //    try
        //    {
        //        await Task.Delay(delayMs, _cancelSource.Token);
        //    }
        //    catch (TaskCanceledException)
        //    {
        //        // Log if the retry was interrupted by cancellation
        //        _logger.LogDebug("Retry attempt {RetryCount} was canceled.", retryCount);
        //        throw;
        //    }
        //}


        private void HandleHttpError(HttpResponseMessage responseMessage)
        {
            if (responseMessage == null)
            {
                _logger.LogError("IoT Interface Processor - Received a null HttpResponseMessage.");
                return;
            }

            try
            {
                // Log basic HTTP response details
                _logger.LogError(
                    "HTTP error occurred. Status Code: {StatusCode}, Reason: {ReasonPhrase}, Request URI: {RequestUri}",
                    responseMessage.StatusCode,
                    responseMessage.ReasonPhrase,
                    responseMessage.RequestMessage?.RequestUri);

                // Attempt to read the response content for additional details
                var content = responseMessage.Content?.ReadAsStringAsync().Result;
                if (!string.IsNullOrWhiteSpace(content))
                {
                    _logger.LogError("Response Content: {Content}", content);
                }

                // Trigger a custom error event if applicable
                OnStreamingErrorEvent(new IotDeviceInterfaceException(
                    $"HTTP error: {responseMessage.StatusCode} - {responseMessage.ReasonPhrase}",
                    null));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "IoT Interface Processor - An unexpected error occurred while handling the HTTP response.");
            }
        }


        private void CleanupStream()
        {
            if (_responseStream != null)
            {
                _responseStream.Dispose();
                _responseStream = null;
            }
        }

        private void CleanupAfterStreaming()
        {
            if (_cancelSource != null && _cancelSource.IsCancellationRequested)
            {
                _cancelSource.Dispose();
            }
        }

        private void HandleStreamingError(Exception ex)
        {
            _logger.LogWarning("Unable to read streaming: {Message}", ex.Message);
            OnStreamingErrorEvent(new IotDeviceInterfaceException("Streaming error detected", ex));
        }

        public bool IsHealthy()
        {
            return _isNetworkConnectedDelegate() && (_responseStream != null) && !_cancelSource.IsCancellationRequested;
        }

        public void Dispose()
        {
            _cancelSource?.Cancel();
            _cancelSource?.Dispose();
            _responseStream?.Dispose();
            _httpClient?.Dispose();
            _httpClientSecure?.Dispose();

            

            GC.SuppressFinalize(this);
        }

        public Task<IpConfiguration> GetReaderSystemNetworkInterfaceByIdAsync(int interfaceId, NetworkProtocol2 proto)
        {
            return _iotDeviceInterfaceClient.SystemNetworkInterfacesConfigurationGetAsync(interfaceId, proto);
        }

        public Task<ObservableCollection<NetworkInterface>> GetReaderSystemNetworkInterfacesAsync()
        {
            return _iotDeviceInterfaceClient.SystemNetworkInterfacesGetAsync();
        }

        public Task<MqttConfiguration> GetReaderMqttAsync()
        {
            return _iotDeviceInterfaceClient.MqttGetAsync();
        }

        public Task UpdateReaderMqttAsync(MqttConfiguration mqttConfiguration)
        {

            try
            {
                return _iotDeviceInterfaceClient.MqttPutAsync(mqttConfiguration);
            }
            catch (AtlasException ex) when (ex.StatusCode == 403)
            {
                Console.WriteLine("Unexpected error: " + ex.Message);
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Unexpected error: " + ex.Message);
                return Task.CompletedTask;
            }

        }

        public Task<GpoConfigurations> DeviceGposGetAsync()
        {
            return _iotDeviceInterfaceClient.DeviceGposGetAsync();
        }

        public Task UpdateReaderGpiAsync(GpiConfigRoot gpiConfiguration)
        {

            var endpoint = "device/gpis ";
            var completeUri = _httpClient.BaseAddress + endpoint;
            try
            {
                string json = JsonSerializer.Serialize(gpiConfiguration);
                var request_ = new HttpRequestMessage();
                var stringContent = new StringContent(json);
                stringContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");
                //request_.Content = (HttpContent)stringContent;
                request_.Method = new HttpMethod("PUT");
                request_.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                //request_.RequestUri = new Uri(completeUri, UriKind.RelativeOrAbsolute);
                //task = (Task)_httpClient.SendAsync(request_, HttpCompletionOption.ResponseHeadersRead);
                return _httpClient.PutAsync(completeUri, stringContent);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Unexpected error: " + ex.Message);
                return Task.CompletedTask;
            }
        }

        public Task UpdateReaderGpoAsync(GpoConfigurations gpoConfiguration)
        {

            try
            {
                return _iotDeviceInterfaceClient.DeviceGposPutAsync(gpoConfiguration);
            }
            catch (AtlasException ex) when (ex.StatusCode == 403)
            {
                Console.WriteLine("Unexpected error: " + ex.Message);
                throw;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Unexpected error: " + ex.Message);
                return Task.CompletedTask;
            }
        }

        public Task<TimeInfo> DeviceReaderTimeGetAsync()
        {
            return _iotDeviceInterfaceClient.SystemTimeGetAsync();
        }

        public Task<ReaderStatus> DeviceReaderStatusGetAsync()
        {
            return _iotDeviceInterfaceClient.StatusAsync();
        }

        public Task<object> DeviceGetInventoryPresetsSchemaAsync()
        {
            return _iotDeviceInterfaceClient.ProfilesInventoryPresetsSchemaAsync();
        }


        public Task<RfidInterface> DeviceReaderRfidInterfaceGetAsync()
        {
            return _iotDeviceInterfaceClient.SystemRfidInterfaceGetAsync();
        }

        public Task UpdateReaderRfidInterface(RfidInterface rfidInterface)
        {

            try
            {
                return _iotDeviceInterfaceClient.SystemRfidInterfacePutAsync(rfidInterface);
            }
            catch (AtlasException ex) when (ex.StatusCode == 403)
            {
                Console.WriteLine("Unexpected error: " + ex.Message);
                throw;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Unexpected error: " + ex.Message);
                return Task.CompletedTask;
            }
        }

        public Task<ObservableCollection<string>> GetReaderProfilesAsync()
        {
            return _iotDeviceInterfaceClient.ProfilesAsync();
        }

        public Task<ObservableCollection<string>> GetReaderInventoryPresetListAsync()
        {
            return _iotDeviceInterfaceClient.ProfilesInventoryPresetsGetAsync();
        }

        public Task<InventoryRequest> GetReaderInventoryPresetAsync(
            string presetId)
        {
            return _iotDeviceInterfaceClient.ProfilesInventoryPresetsGetAsync(presetId);
        }

        public Task SaveInventoryPresetAsync(string presetId, InventoryRequest inventoryRequest)
        {
            try
            {
                return _iotDeviceInterfaceClient.ProfilesInventoryPresetsPutAsync(presetId, inventoryRequest);
            }
            catch (AtlasException ex) when (ex.StatusCode == 403 || ex.StatusCode == 204)
            {
                return Task.CompletedTask;
            }
        }

        public Task UpdateReaderInventoryPresetAsync(
            string presetId,
            InventoryRequest updatedInventoryRequest)
        {
            try
            {
                return _iotDeviceInterfaceClient.ProfilesInventoryPresetsPutAsync(presetId, updatedInventoryRequest);
            }
            catch (AtlasException ex) when (ex.StatusCode == 403 || ex.StatusCode == 204)
            {
                return Task.CompletedTask;
            }

            
        }

        public Task UpdateReaderInventoryPresetAsync(
            string presetId,
            string json)
        {
            var endpoint = "profiles/inventory/presets/";
            var completeUri = _httpClient.BaseAddress + endpoint + presetId;
            try
            {
                var request_ = new HttpRequestMessage();
                var stringContent = new StringContent(json);
                stringContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");
                //request_.Content = (HttpContent)stringContent;
                request_.Method = new HttpMethod("PUT");
                request_.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                //request_.RequestUri = new Uri(completeUri, UriKind.RelativeOrAbsolute);
                //task = (Task)_httpClient.SendAsync(request_, HttpCompletionOption.ResponseHeadersRead);
                return _httpClient.PutAsync(completeUri, stringContent);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Unexpected error: " + ex.Message);
                return Task.CompletedTask;
            }

        }

        public Task PostTransientInventoryPresetAsync(
            string json)
        {
            var iotDeviceInterfaceEventProcessor = this;
            iotDeviceInterfaceEventProcessor._iotDeviceInterfaceClient.ProfilesStopAsync();
            iotDeviceInterfaceEventProcessor._cancelSource = new CancellationTokenSource();
            iotDeviceInterfaceEventProcessor._streamingTask =
                Task.Run(iotDeviceInterfaceEventProcessor.StreamingAsync);

            var endpoint = "profiles/inventory/start";
            var completeUri = _httpClient.BaseAddress + endpoint;
            try
            {
                var request_ = new HttpRequestMessage();
                var stringContent = new StringContent(json);
                stringContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");
                //request_.Content = (HttpContent)stringContent;
                request_.Method = new HttpMethod("POST");
                request_.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                //request_.RequestUri = new Uri(completeUri, UriKind.RelativeOrAbsolute);
                //task = (Task)_httpClient.SendAsync(request_, HttpCompletionOption.ResponseHeadersRead);
                return _httpClient.PostAsync(completeUri, stringContent);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Unexpected error: " + ex.Message);
                return Task.CompletedTask;
            }
        }

        public Task PostStopInventoryPresetAsync(
            string json = "")
        {
            if (_cancelSource == null || _cancelSource.IsCancellationRequested)
                return Task.CompletedTask;

            if (_cancelSource != null)
            {
                _cancelSource.Cancel();
                try
                {
                    _responseStream?.Dispose();
                    _iotDeviceInterfaceClient.ProfilesStopAsync();
                    return _streamingTask ?? Task.CompletedTask;
                
                }
                catch (TaskCanceledException)
                {
                }
                catch (Exception ex)
                {
                    OnStreamingErrorEvent(
                        new IotDeviceInterfaceException($"Unexpected error - connection error {_hostname}",
    ex));
                }
            }
            return Task.CompletedTask;
        }

        public Task DeleteReaderInventoryPresetAsync(string presetId)
        {
            return _iotDeviceInterfaceClient.ProfilesInventoryPresetsDeleteAsync(presetId);
        }

        public Task<object> GetReaderInventorySchemaAsync()
        {
            return _iotDeviceInterfaceClient.ProfilesInventoryPresetsSchemaAsync();
        }

        public Task SystemImageUpgradePostAsync(string file)
        {
            using (var fs = File.Open(file, FileMode.Open))
            {
                var atlasFileParameter = new FileParameter(fs);


                return _iotDeviceInterfaceClient.SystemImageUpgradePostAsync(atlasFileParameter);
            }
        }

        internal event EventHandler<TagInventoryEvent>? TagInventoryEvent;

        private void OnTagInventoryEvent(TagInventoryEvent e)
        {
            if (e == null)
            {
                _logger.LogWarning("OnTagInventoryEvent invoked with a null event.");
                return;
            }

            try
            {
                // Thread-safe delegate invocation
                var handler = TagInventoryEvent;
                if (handler != null)
                {
                    _logger.LogDebug("Raising TagInventoryEvent: {@Event}", e);
                    handler(this, e);
                }
                else
                {
                    _logger.LogDebug("No subscribers for TagInventoryEvent.");
                }
            }
            catch (Exception ex)
            {
                // Handle and log exceptions
                _logger.LogError(ex, "An error occurred while invoking TagInventoryEvent: {Message}", ex.Message);
            }
            finally
            {
                // Log completion of the event
                _logger.LogDebug("Completed processing TagInventoryEvent.");
            }
        }


        internal event EventHandler<IotDeviceInterfaceException>? StreamingErrorEvent;

        private void OnStreamingErrorEvent(IotDeviceInterfaceException e)
        {
            if (e == null)
            {
                _logger.LogWarning("OnStreamingErrorEvent was invoked with a null exception.");
                return;
            }

            try
            {
                // Thread-safe event invocation
                var handler = StreamingErrorEvent; // Copy the delegate to a local variable
                if (handler != null)
                {
                    _logger.LogDebug("Raising StreamingErrorEvent: {@Exception}", e);
                    handler(this, e);
                }
                else
                {
                    _logger.LogDebug("No subscribers for StreamingErrorEvent.");
                }
            }
            catch (Exception ex)
            {
                // Log exceptions from the event invocation
                _logger.LogError(ex, "An error occurred while invoking StreamingErrorEvent: {Message}", ex.Message);
            }
            finally
            {
                // Log completion of the event
                _logger.LogDebug("Completed processing StreamingErrorEvent.");
            }
        }


        internal event EventHandler<GpiTransitionVm>? GpiTransitionEvent;

        private void OnGpiTransitionEvent(GpiTransitionVm e)
        {
            if (e == null)
            {
                _logger.LogWarning("OnGpiTransitionEvent was invoked with a null event.");
                return;
            }

            try
            {
                // Thread-safe delegate invocation
                var handler = GpiTransitionEvent; // Copy delegate to avoid race conditions
                if (handler != null)
                {
                    _logger.LogDebug("Raising GpiTransitionEvent: {@Event}", e);
                    handler(this, e);
                }
                else
                {
                    _logger.LogWarning("No subscribers for GpiTransitionEvent.");
                }
            }
            catch (Exception ex)
            {
                // Handle and log exceptions from subscribers
                _logger.LogError(ex, "An error occurred while invoking GpiTransitionEvent: {Message}", ex.Message);
            }
            finally
            {
                // Log that processing is complete
                _logger.LogDebug("Completed processing GpiTransitionEvent.");
            }
        }


        internal event EventHandler<DiagnosticEvent>? DiagnosticEvent;

        private void OnDiagnosticEvent(DiagnosticEvent e)
        {
            if (e == null)
            {
                _logger.LogWarning("OnDiagnosticEvent was invoked with a null event.");
                return;
            }

            try
            {
                // Thread-safe delegate invocation
                var handler = DiagnosticEvent; // Copy the delegate to avoid race conditions
                if (handler != null)
                {
                    _logger.LogDebug("Raising DiagnosticEvent: {@Event}", e);
                    handler(this, e);
                }
                else
                {
                    _logger.LogWarning("No subscribers for DiagnosticEvent.");
                }
            }
            catch (Exception ex)
            {
                // Handle and log exceptions thrown by subscribers
                _logger.LogError(ex, "An error occurred while invoking DiagnosticEvent: {Message}", ex.Message);
            }
            finally
            {
                // Log that event processing is complete
                _logger.LogDebug("Completed processing DiagnosticEvent.");
            }
        }


        internal event EventHandler<InventoryStatusEvent>? InventoryStatusEvent;

        private void OnInventoryStatusEvent(InventoryStatusEvent e)
        {
            if (e == null)
            {
                _logger.LogWarning("OnInventoryStatusEvent was invoked with a null event.");
                return;
            }

            try
            {
                // Thread-safe delegate invocation
                var handler = InventoryStatusEvent; // Copy the delegate to avoid race conditions
                if (handler != null)
                {
                    _logger.LogDebug("Raising InventoryStatusEvent: {@Event}", e);
                    handler(this, e);
                }
                else
                {
                    _logger.LogWarning("No subscribers for InventoryStatusEvent.");
                }
            }
            catch (Exception ex)
            {
                // Handle and log exceptions thrown by subscribers
                _logger.LogError(ex, "An error occurred while invoking InventoryStatusEvent: {Message}", ex.Message);
            }
            finally
            {
                // Log that processing is complete
                _logger.LogDebug("Completed processing InventoryStatusEvent.");
            }
        }

    }
}
