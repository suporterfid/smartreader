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
using Newtonsoft.Json;
using System.Collections.ObjectModel;
using System.Net;
using System.Net.Http.Headers;
using System.Net.NetworkInformation;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using NetworkInterface = Impinj.Atlas.NetworkInterface;

namespace SmartReader.IotDeviceInterface;

internal class R700IotReader : IR700IotReader
{
    private readonly IotDeviceInterfaceEventProcessor _r700IotEventProcessor;

    internal R700IotReader(
        string hostname,
        string nickname = "",
        bool useHttpAlways = false,
        bool useBasicAuthAlways = false,
        string uname = "root",
        string pwd = "impinj",
        int hostPort = 0,
        string proxy = "",
        int proxyPort = 8080)
    {
        if (string.IsNullOrWhiteSpace(hostname))
            throw new ArgumentNullException(nameof(hostname));
        _r700IotEventProcessor =
            new IotDeviceInterfaceEventProcessor(hostname, useHttpAlways, useBasicAuthAlways, uname, pwd, hostPort, proxy, proxyPort);
        Hostname = hostname;
        Nickname = nickname;
    }

    public List<int> RxSensitivitiesInDbm { get; private set; }

    public string Hostname { get; }

    public string Nickname { get; set; }

    public string DisplayName => !string.IsNullOrWhiteSpace(Nickname) ? Nickname : Hostname;

    public string UniqueId { get; private set; }

    public string MacAddress { get; private set; }

    public uint ModelNumber { get; }

    public string ProductModel { get; private set; }

    public List<double> TxPowersInDbm { get; private set; }

    public double MinPowerStepDbm { get; } = 0.25;

    public string ReaderOperatingRegion { get; private set; }

    public bool IsAntennaHubEnabled { get; private set; }

    public bool IsNetworkConnected { get; private set; }

    public List<string> IpAddresses { get; private set; }


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
                    MacAddress = selectedIface.HardwareAddress;
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
            foreach (var netInterface in iFaces)
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
        if (antennaHubStatus != null && antennaHubStatus == AntennaHubInfoStatus.Enabled) IsAntennaHubEnabled = true;

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

    public event EventHandler<TagInventoryEvent> TagInventoryEvent;

    public event EventHandler<IotDeviceInterfaceException> StreamingErrorEvent;

    public event EventHandler<Impinj.Atlas.GpiTransitionEvent> GpiTransitionEvent;

    public event EventHandler<DiagnosticEvent> DiagnosticEvent;

    public event EventHandler<InventoryStatusEvent> InventoryStatusEvent;


    public Task SendCustomInventoryPresetAsync(string presetId, string jsonInventoryRequest)
    {
        Task task = null;
        try
        {
            return task =
                _r700IotEventProcessor.UpdateReaderInventoryPresetAsync(presetId, jsonInventoryRequest);
        }
        catch (AtlasException ex) when (ex.StatusCode == 403 || ex.StatusCode == 204)
        {
        }

        return task;
    }

    public Task PostTransientInventoryPresetAsync(string jsonInventoryRequest)
    {
        Task task = null;
        try
        {
            _r700IotEventProcessor.TagInventoryEvent += R700IotEventProcessorOnTagInventoryEvent;
            _r700IotEventProcessor.StreamingErrorEvent += R700IotEventProcessorOnStreamingErrorEvent;
            return task =
                _r700IotEventProcessor.PostTransientInventoryPresetAsync(jsonInventoryRequest);
        }
        catch (AtlasException ex) when (ex.StatusCode == 403 || ex.StatusCode == 204)
        {
            try
            {
                _r700IotEventProcessor.TagInventoryEvent -= R700IotEventProcessorOnTagInventoryEvent;
                _r700IotEventProcessor.StreamingErrorEvent -= R700IotEventProcessorOnStreamingErrorEvent;
            }
            catch (Exception)
            {
            }
        }

        return task;
    }

    public Task PostTransientInventoryStopPresetsAsync()
    {
        Task task = null;
        try
        {
            try
            {
                _r700IotEventProcessor.TagInventoryEvent -= R700IotEventProcessorOnTagInventoryEvent;
                _r700IotEventProcessor.StreamingErrorEvent -= R700IotEventProcessorOnStreamingErrorEvent;
            }
            catch (Exception)
            {
            }

            return task =
                _r700IotEventProcessor.PostStopInventoryPresetAsync();
        }
        catch (AtlasException ex) when (ex.StatusCode == 403 || ex.StatusCode == 204)
        {
            try
            {
                _r700IotEventProcessor.TagInventoryEvent -= R700IotEventProcessorOnTagInventoryEvent;
                _r700IotEventProcessor.StreamingErrorEvent -= R700IotEventProcessorOnStreamingErrorEvent;
            }
            catch (Exception)
            {
            }
        }

        return task;
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

    public Task UpdateReaderGpoAsync(GpoConfigurations gpoConfiguration)
    {
        return _r700IotEventProcessor.UpdateReaderGpoAsync(gpoConfiguration);
    }

    public Task SystemImageUpgradePostAsync(string file)
    {
        return _r700IotEventProcessor.SystemImageUpgradePostAsync(file);
    }

    private void R700IotEventProcessorOnStreamingErrorEvent(object sender, IotDeviceInterfaceException e)
    {
        OnStreamingErrorEvent(e);
    }

    private void R700IotEventProcessorOnTagInventoryEvent(object sender, TagInventoryEvent e)
    {
        OnTagInventoryEvent(e);
    }

    private void R700IotEventProcessorOnGpiTransitionEvent(object sender, Impinj.Atlas.GpiTransitionEvent e)
    {
        OnGpiTransitionEvent(e);
    }

    private void R700IotEventProcessorOnInventoryStatusEvent(object sender, InventoryStatusEvent e)
    {
        OnInventoryStatusEvent(e);
    }

    private void R700IotEventProcessorOnDiagnosticEvent(object sender, DiagnosticEvent e)
    {
        OnDiagnosticEvent(e);
    }

    protected virtual void OnTagInventoryEvent(TagInventoryEvent e)
    {
        var tagInventoryEvent = TagInventoryEvent;
        if (tagInventoryEvent == null)
            return;
        tagInventoryEvent(this, e);
    }

    protected virtual void OnStreamingErrorEvent(IotDeviceInterfaceException e)
    {
        var streamingErrorEvent = StreamingErrorEvent;
        if (streamingErrorEvent == null)
            return;
        streamingErrorEvent(this, e);
    }

    private void OnGpiTransitionEvent(Impinj.Atlas.GpiTransitionEvent e)
    {
        var gpiTransitionEvent = GpiTransitionEvent;
        if (gpiTransitionEvent == null)
            return;
        gpiTransitionEvent(this, e);
    }

    private void OnDiagnosticEvent(DiagnosticEvent e)
    {
        var diagnosticEvent = DiagnosticEvent;
        if (diagnosticEvent == null)
            return;
        diagnosticEvent(this, e);
    }

    private void OnInventoryStatusEvent(InventoryStatusEvent e)
    {
        var inventoryStatusEvent = InventoryStatusEvent;
        if (inventoryStatusEvent == null)
            return;
        inventoryStatusEvent(this, e);
    }

    void IDisposable.Dispose()
    {
        // Suppress finalization.
        GC.SuppressFinalize(this);
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
        private CancellationTokenSource _cancelSource;
        private Stream _responseStream;
        private Task _streamingTask;

        internal IotDeviceInterfaceEventProcessor(
            string hostname,
            bool useHttpAlways,
            bool useBasicAuthAlways,
            string uname,
            string pwd,
            int hostPort = 0,
            string proxy = "",
            int proxyPort = 8080)
        {
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


                ServerCertificateCustomValidationCallback =
                    (Func<HttpRequestMessage, X509Certificate2, X509Chain, SslPolicyErrors, bool>)((message, cert,
                        chain, errors) => true)
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
                    ServerCertificateCustomValidationCallback =
                    (Func<HttpRequestMessage, X509Certificate2, X509Chain, SslPolicyErrors, bool>)((message, cert,
                        chain, errors) => true)
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
        }

        public Task<ReaderStatus> GetStatusAsync()
        {
            var readerStatus = new ReaderStatus();
            var systemInfo = _iotDeviceInterfaceClient.SystemAsync().Result;

            var currentInterface = _iotDeviceInterfaceClient.SystemRfidInterfaceGetAsync().Result;
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
            return _iotDeviceInterfaceClientSecure.SystemAsync();
        }

        public Task<AntennaHubInfo> GetSystemAntennaHubInfoAsync()
        {
            bool? pending = null;
            return _iotDeviceInterfaceClientSecure.SystemAntennaHubAsync(pending);
        }

        public Task<RegionInfo> GetSystemRegionInfoAsync()
        {
            var systemRegionCancellationToken = new CancellationToken();
            bool? pending = null;
            return _iotDeviceInterfaceClientSecure.SystemRegionGetAsync(systemRegionCancellationToken);
        }

        public Task<PowerConfiguration> GetSystemPowerAsync()
        {
            var systemPowerCancellationToken = new CancellationToken();
            bool? pending = null;
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

            _cancelSource.Cancel();
            try
            {
                if (_responseStream != null) _responseStream.Dispose();

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
                    new IotDeviceInterfaceException(string.Format("Unexpected error - connection error ", _hostname),
                        ex));
            }
        }


        private async Task StreamingAsync()
        {
            try
            {
                using (var responseMessage = await _httpClient.GetAsync(new Uri("data/stream", UriKind.Relative),
                           HttpCompletionOption.ResponseHeadersRead))
                {
                    if (responseMessage.IsSuccessStatusCode)
                    {
                        _responseStream = await responseMessage.Content.ReadAsStreamAsync();
                        using (var streamReader = new StreamReader(_responseStream))
                        {
                            while (!_cancelSource.IsCancellationRequested)
                                if (!streamReader.EndOfStream)
                                {
                                    var str = await streamReader.ReadLineAsync();
                                    if (!string.IsNullOrWhiteSpace(str))
                                    {
                                        ReaderEvent readerEvent = null;
                                        if (!str.Contains("GpiTransitionEvent"))
                                            readerEvent = JsonConvert.DeserializeObject<ReaderEvent>(str);
                                        if (readerEvent != null && readerEvent.TagInventoryEvent != null)
                                            OnTagInventoryEvent(readerEvent.TagInventoryEvent);
                                        if (readerEvent != null && readerEvent.InventoryStatusEvent != null)
                                        {
                                            if (str.Contains("running"))
                                                readerEvent.InventoryStatusEvent.Status =
                                                    InventoryStatusEventStatus.Running;
                                            else if (str.Contains("idle"))
                                                readerEvent.InventoryStatusEvent.Status =
                                                    InventoryStatusEventStatus.Idle;
                                            OnInventoryStatusEvent(readerEvent.InventoryStatusEvent);
                                        }
                                    }
                                }
                                else
                                {
                                    break;
                                }
                        }
                    }
                    else
                    {
                        var error = JsonConvert.DeserializeObject<ErrorResponse>(
                            await responseMessage.Content.ReadAsStringAsync());
                        var headersDictionary = responseMessage.Headers.ToDictionary(
                            (Func<KeyValuePair<string, IEnumerable<string>>, string>)(h => h.Key),
                            (Func<KeyValuePair<string, IEnumerable<string>>, IEnumerable<string>>)(h => h.Value));
                        var response = await responseMessage.Content.ReadAsStringAsync();
                        throw new AtlasException(
                            string.Format("Unexpected error ", error.Message, error.InvalidPropertyId),
                            (int)responseMessage.StatusCode, response, headersDictionary, null);
                    }
                }
            }
            catch (TaskCanceledException)
            {
            }
            catch (IOException ex)
            {
                if (ex.InnerException != null && ex.InnerException.GetType() == typeof(SocketException))
                    return;
                throw;
            }
            catch (Exception ex)
            {
                OnStreamingErrorEvent(
                    new IotDeviceInterfaceException(string.Format("Unexpected error ", _hostname), ex));
            }
            finally
            {
                _cancelSource.Dispose();
            }
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
            Task task = null;

            try
            {
                task = _iotDeviceInterfaceClient.MqttPutAsync(mqttConfiguration);
            }
            catch (AtlasException ex) when (ex.StatusCode == 403)
            {
                Console.WriteLine("Unexpected error: " + ex.Message);
                throw;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Unexpected error: " + ex.Message);
            }

            return task;
        }

        public Task<GpoConfigurations> DeviceGposGetAsync()
        {
            return _iotDeviceInterfaceClient.DeviceGposGetAsync();
        }

        public Task UpdateReaderGpoAsync(GpoConfigurations gpoConfiguration)
        {
            Task task = null;

            try
            {
                task = _iotDeviceInterfaceClient.DeviceGposPutAsync(gpoConfiguration);
            }
            catch (AtlasException ex) when (ex.StatusCode == 403)
            {
                Console.WriteLine("Unexpected error: " + ex.Message);
                throw;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Unexpected error: " + ex.Message);
            }

            return task;
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
            Task task = null;

            try
            {
                task = _iotDeviceInterfaceClient.SystemRfidInterfacePutAsync(rfidInterface);
            }
            catch (AtlasException ex) when (ex.StatusCode == 403)
            {
                Console.WriteLine("Unexpected error: " + ex.Message);
                throw;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Unexpected error: " + ex.Message);
            }

            return task;
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
            Task task = null;
            try
            {
                task = _iotDeviceInterfaceClient.ProfilesInventoryPresetsPutAsync(presetId, inventoryRequest);
            }
            catch (AtlasException ex) when (ex.StatusCode == 403 || ex.StatusCode == 204)
            {
            }

            return task;
        }

        public Task UpdateReaderInventoryPresetAsync(
            string presetId,
            InventoryRequest updatedInventoryRequest)
        {
            Task task = null;
            try
            {
                task = _iotDeviceInterfaceClient.ProfilesInventoryPresetsPutAsync(presetId, updatedInventoryRequest);
            }
            catch (AtlasException ex) when (ex.StatusCode == 403 || ex.StatusCode == 204)
            {
            }

            return task;
        }

        public Task UpdateReaderInventoryPresetAsync(
            string presetId,
            string json)
        {
            var endpoint = "profiles/inventory/presets/";
            var completeUri = _httpClient.BaseAddress + endpoint + presetId;
            Task task = null;
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
                task = _httpClient.PutAsync(completeUri, stringContent);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Unexpected error: " + ex.Message);
                throw;
            }

            return task;
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
            Task task = null;
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
                task = _httpClient.PostAsync(completeUri, stringContent);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Unexpected error: " + ex.Message);
                throw;
            }

            return task;
        }

        public Task PostStopInventoryPresetAsync(
            string json = "")
        {
            if (_cancelSource == null || _cancelSource.IsCancellationRequested)
                return null;

            Task task = null;
            _cancelSource.Cancel();
            try
            {
                _responseStream.Dispose();
                _iotDeviceInterfaceClient.ProfilesStopAsync();
                task = _streamingTask;
            }
            catch (TaskCanceledException)
            {
            }
            catch (Exception ex)
            {
                OnStreamingErrorEvent(
                    new IotDeviceInterfaceException(string.Format("Unexpected error - connection error ", _hostname),
                        ex));
            }

            return task;
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

        public event EventHandler<TagInventoryEvent> TagInventoryEvent;

        private void OnTagInventoryEvent(TagInventoryEvent e)
        {
            var tagInventoryEvent = TagInventoryEvent;
            if (tagInventoryEvent == null)
                return;
            tagInventoryEvent(this, e);
        }

        public event EventHandler<IotDeviceInterfaceException> StreamingErrorEvent;

        private void OnStreamingErrorEvent(IotDeviceInterfaceException e)
        {
            var streamingErrorEvent = StreamingErrorEvent;
            if (streamingErrorEvent == null)
                return;
            streamingErrorEvent(this, e);
        }

        public event EventHandler<Impinj.Atlas.GpiTransitionEvent> GpiTransitionEvent;

        private void OnGpiTransitionEvent(Impinj.Atlas.GpiTransitionEvent e)
        {
            var gpiTransitionEvent = GpiTransitionEvent;
            if (gpiTransitionEvent == null)
                return;
            gpiTransitionEvent(this, e);
        }

        public event EventHandler<DiagnosticEvent> DiagnosticEvent;

        private void OnDiagnosticEvent(DiagnosticEvent e)
        {
            var diagnosticEvent = DiagnosticEvent;
            if (diagnosticEvent == null)
                return;
            diagnosticEvent(this, e);
        }

        public event EventHandler<InventoryStatusEvent> InventoryStatusEvent;

        private void OnInventoryStatusEvent(InventoryStatusEvent e)
        {
            var inventoryStatusEvent = InventoryStatusEvent;
            if (inventoryStatusEvent == null)
                return;
            inventoryStatusEvent(this, e);
        }
    }
}