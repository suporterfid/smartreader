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
using Newtonsoft.Json.Linq;
using plugin_contract.ViewModel.Gpi;
using plugin_contract.ViewModel.Stream;
using SmartReaderStandalone.IotDeviceInterface;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.NetworkInformation;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using HealthStatus = SmartReaderStandalone.IotDeviceInterface.HealthStatus;
using NetworkInterface = Impinj.Atlas.NetworkInterface;

namespace SmartReader.IotDeviceInterface;

public class R700IotReader : IR700IotReader
{
    private readonly IReaderConfiguration _configuration;

    private readonly IEventManager _eventManager;

    private readonly IHealthMonitor _healthMonitor;

    private readonly SemaphoreSlim _operationSemaphore = new(1, 1);

    private readonly IotDeviceInterfaceEventProcessor _r700IotEventProcessor;

    private readonly ILogger<R700IotReader> _logger;

    private readonly ILoggerFactory _loggerFactory;

    private bool _disposed;

    internal R700IotReader(
        string hostname,
        IReaderConfiguration configuration,
        ILogger<R700IotReader>? eventProcessorLogger = null,
        ILoggerFactory? loggerFactory = null,
        IHealthMonitor? healthMonitor = null
        )
    {
        _logger = eventProcessorLogger ?? throw new ArgumentNullException(nameof(eventProcessorLogger));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));

        if (string.IsNullOrWhiteSpace(hostname))
            throw new ArgumentNullException(nameof(hostname));

        _healthMonitor = healthMonitor ?? new HealthMonitor(_logger);
        _operationSemaphore = new SemaphoreSlim(1, 1);
        _eventManager = new EventManager(_logger);

        _configuration = configuration;

        _r700IotEventProcessor = new IotDeviceInterfaceEventProcessor(
                hostname,
                configuration,
                eventProcessorLogger: _loggerFactory.CreateLogger<R700IotReader>(),
                _loggerFactory.CreateLogger<IotDeviceInterfaceEventProcessor>(),
                _loggerFactory,
                () => IsNetworkConnected, // Pass delegate
                this
            );

        Hostname = hostname;
        Nickname = "nickname";

        // Subscribe to events through the event manager
        InitializeEventSubscriptions();
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

    private void InitializeEventSubscriptions()
    {
        // Unsubscribe from events first to avoid multiple subscriptions
        _r700IotEventProcessor.TagInventoryEvent -= R700IotEventProcessorOnTagInventoryEvent;
        _r700IotEventProcessor.StreamingErrorEvent -= R700IotEventProcessorOnStreamingErrorEvent;
        _r700IotEventProcessor.GpiTransitionEvent -= R700IotEventProcessorOnGpiTransitionEvent;
        _r700IotEventProcessor.InventoryStatusEvent -= R700IotEventProcessorOnInventoryStatusEvent;
        _r700IotEventProcessor.DiagnosticEvent -= R700IotEventProcessorOnDiagnosticEvent;

        // Subscribe to events
        _r700IotEventProcessor.TagInventoryEvent += R700IotEventProcessorOnTagInventoryEvent;
        _r700IotEventProcessor.StreamingErrorEvent += R700IotEventProcessorOnStreamingErrorEvent;
        _r700IotEventProcessor.GpiTransitionEvent += R700IotEventProcessorOnGpiTransitionEvent;
        _r700IotEventProcessor.InventoryStatusEvent += R700IotEventProcessorOnInventoryStatusEvent;
        _r700IotEventProcessor.DiagnosticEvent += R700IotEventProcessorOnDiagnosticEvent;
    }

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

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _operationSemaphore.Dispose();
                _eventManager.Dispose();
                _r700IotEventProcessor.Dispose();
                _healthMonitor.Dispose();
            }
            _disposed = true;
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(R700IotReader));
        }
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
                IpAddresses = [];
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

    /// <summary>
    /// Updates the reader GPO configuration with advanced options
    /// </summary>
    /// <param name="extendedGpoConfiguration">Extended  GPO configuration</param>
    /// <returns>Task representing the asynchronous operation</returns>
    public Task UpdateReaderGpoAsync(ExtendedGpoConfigurationRequest extendedGpoConfiguration)
    {
        return _r700IotEventProcessor.UpdateInternalProcessorReaderGpoAsync(extendedGpoConfiguration);
    }

    /// <summary>
    /// Updates the reader GPO configuration with extended options
    /// </summary>
    /// <param name="extendedConfiguration">Extended GPO configuration with advanced modes</param>
    /// <returns>Task representing the asynchronous operation</returns>
    public async Task UpdateReaderGpoExtendedAsync(ExtendedGpoConfigurationRequest extendedConfiguration)
    {
        try
        {
            // Validate the configuration
            var validationResult = extendedConfiguration.Validate();
            if (!validationResult.IsValid)
            {
                throw new ArgumentException(
                    $"Invalid GPO configuration: {string.Join(", ", validationResult.Errors)}");
            }

            // Log the configuration for debugging
            _logger.LogInformation("Updating GPO configuration with extended options: {@Config}",
                extendedConfiguration);

            // Send to reader
            await _r700IotEventProcessor.UpdateInternalProcessorReaderGpoAsync(extendedConfiguration);

            // Record health metric for successful GPO update
            var metric = new HealthMetric.Builder(MetricType.Hardware, "GpoConfigurationUpdate")
                .WithSeverity(MetricSeverity.Information)
                .WithDescription("Successfully updated GPO configuration")
                .WithSource("R700IotReader")
                .AddMetadata("ConfiguredPorts", extendedConfiguration.GpoConfigurations.Count.ToString())
                .Build();

            await _healthMonitor.RecordMetricAsync(metric);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update extended GPO configuration");

            // Record health metric for failed GPO update
            var metric = new HealthMetric.Builder(MetricType.Hardware, "GpoConfigurationUpdate")
                .WithSeverity(MetricSeverity.Error)
                .WithDescription($"Failed to update GPO configuration: {ex.Message}")
                .WithSource("R700IotReader")
                .Build();

            await _healthMonitor.RecordMetricAsync(metric);

            throw;
        }
    }

    /// <summary>
    /// Gets the current GPO configuration with extended information
    /// </summary>
    /// <returns>Extended GPO configuration</returns>
    public async Task<ExtendedGpoConfigurationRequest> GetGpoExtendedAsync()
    {
        try
        {
            var advancedGposConfig = await _r700IotEventProcessor.DeviceGposGetAsync();

            var extendedConfig = new ExtendedGpoConfigurationRequest
            {
                GpoConfigurations = new List<ExtendedGpoConfiguration>()
            };

            if (advancedGposConfig?.Count != 0)
            {
                foreach (var gpo in advancedGposConfig)
                {
                    extendedConfig.GpoConfigurations.Add(gpo);
                }
            }

            return extendedConfig;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get extended GPO configuration");
            throw;
        }
    }

    public async Task StartAsync(string presetId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        try
        {
            await _operationSemaphore.WaitAsync(cancellationToken);

            InitializeEventSubscriptions();

            // Start monitoring health
            //_ = Task.Run(() => _healthMonitor.StartMonitoring(cancellationToken), cancellationToken)
            //    .ContinueWith(task =>
            //    {
            //        if (task.IsFaulted)
            //        {
            //            // Handle or log the exception
            //            var exception = task.Exception;
            //            // Log the exception or take other appropriate actions
            //            _logger.LogError(exception, "Error starting the monitoring health task.");
            //        }
            //    }, TaskScheduler.Default);

            // Start the event processor
            _ = Task.Run(() => _r700IotEventProcessor.StartStreamingAsync(presetId), cancellationToken)
                .ContinueWith(task =>
                {
                    if (task.IsFaulted)
                    {
                        // Handle or log the exception
                        var exception = task.Exception;
                        // Log the exception or take other appropriate actions
                        _logger.LogError(exception, "Error starting the event processor task.");
                    }
                }, TaskScheduler.Default);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start reader operations");
            throw;
        }
        finally
        {
            _ = _operationSemaphore.Release();
        }
    }


    public Task StartPresetAsync(string presetId)
    {
        return _r700IotEventProcessor.StartPresetAsync(presetId);
    }

    public Task StopPresetAsync()
    {
        return _r700IotEventProcessor.StopPresetAsync();
    }

    public async Task StopAsync()
    {
        try
        {
            await _operationSemaphore.WaitAsync();
            await _r700IotEventProcessor.StopStreamingAsync();

            // Unsubscribe from events
            _r700IotEventProcessor.TagInventoryEvent -= R700IotEventProcessorOnTagInventoryEvent;
            _r700IotEventProcessor.StreamingErrorEvent -= R700IotEventProcessorOnStreamingErrorEvent;
            _r700IotEventProcessor.GpiTransitionEvent -= R700IotEventProcessorOnGpiTransitionEvent;
            _r700IotEventProcessor.InventoryStatusEvent -= R700IotEventProcessorOnInventoryStatusEvent;
            _r700IotEventProcessor.DiagnosticEvent -= R700IotEventProcessorOnDiagnosticEvent;
        }
        finally
        {
            _ = _operationSemaphore.Release();
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

    public Task<List<ExtendedGpoConfiguration>> DeviceGposGetAsync()
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

    public Task UpdateHttpStreamConfigAsync(HttpStreamConfig streamConfiguration)
    {
        return _r700IotEventProcessor.UpdateHttpStreamConfigAsync(streamConfiguration);
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



    private void R700IotEventProcessorOnGpiTransitionEvent(object? sender, GpiTransitionVm e)
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

    internal interface IEventManager
    {
        void Dispose();
        void Subscribe<T>(string eventName, EventHandler<T> handler);
        void Unsubscribe<T>(string eventName, EventHandler<T> handler);
    }

    internal class EventManager : IDisposable, IEventManager
    {
        private readonly ConcurrentDictionary<string, Delegate> _subscriptions;
        private readonly ILogger _logger;
        private bool _disposed;

        public EventManager(ILogger logger)
        {
            _logger = logger;
            _subscriptions = new ConcurrentDictionary<string, Delegate>();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(R700IotReader));
            }
        }

        public void Subscribe<T>(string eventName, EventHandler<T> handler)
        {
            ThrowIfDisposed();

            _ = _subscriptions.AddOrUpdate(
                eventName,
                handler,
                (_, existing) => Delegate.Combine(existing, handler));

            _logger.LogDebug("Subscribed to event: {EventName}", eventName);
        }

        public void Unsubscribe<T>(string eventName, EventHandler<T> handler)
        {
            ThrowIfDisposed();

            if (_subscriptions.TryGetValue(eventName, out var existing))
            {
                var updated = Delegate.Remove(existing, handler);
                if (updated == null)
                {
                    _ = _subscriptions.TryRemove(eventName, out _);
                }
                else
                {
                    _ = _subscriptions.TryUpdate(eventName, updated, existing);
                }
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _subscriptions.Clear();
                }
                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }
    }

    internal interface IHealthMonitor
    {
        Task RecordMetricAsync(HealthMetric metric, CancellationToken cancellationToken = default);
        Task StartMonitoring(CancellationToken cancellationToken = default);
        void Dispose();
    }

    internal class HealthMonitor : IHealthMonitor
    {
        private readonly ILogger _logger;
        private readonly Channel<HealthMetric> _metricsChannel;
        private readonly CancellationTokenSource _monitoringCts;
        private Task _monitoringTask;
        private bool _disposed;
        private readonly HealthCheck _healthCheck;

        public HealthMonitor(ILogger logger)
        {
            _logger = logger;
            _metricsChannel = Channel.CreateUnbounded<HealthMetric>(
                new UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = false
                });
            _monitoringCts = new CancellationTokenSource();
            _healthCheck = new HealthCheck();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(HealthMonitor));
            }
        }

        /// <summary>
        /// Starts the health monitoring process, tracking system metrics and health status.
        /// </summary>
        public Task StartMonitoring(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            // Initialize health check monitoring state
            _healthCheck.MarkStreamStart();

            _monitoringTask = Task.Run(async () =>
            {
                try
                {
                    // Register cancellation callback
                    await using var registration = cancellationToken.Register(
                        () => _monitoringCts.Cancel());

                    // Process metrics as they arrive
                    await foreach (var metric in _metricsChannel.Reader
                        .ReadAllAsync(_monitoringCts.Token))
                    {
                        try
                        {
                            // Process each metric and update health status
                            ProcessHealthMetric(metric);

                            // Get current health metrics after processing
                            var healthMetrics = _healthCheck.GetHealthMetrics();

                            // Log health status changes
                            if (healthMetrics.Status >= HealthStatus.Degraded)
                            {
                                _logger.LogWarning(
                                    "System health degraded - Status: {Status}, Uptime: {Uptime}, " +
                                    "Messages/min: {MessageRate}, Recent Errors: {ErrorCount}",
                                    healthMetrics.Status,
                                    healthMetrics.Uptime,
                                    healthMetrics.MessagesPerMinute,
                                    healthMetrics.RecentErrors?.Length ?? 0);

                                // Record the degraded state as an error
                                _healthCheck.RecordError(
                                    new Exception($"Health status degraded to {healthMetrics.Status}"),
                                    ErrorSeverity.Warning);
                            }
                        }
                        catch (Exception ex)
                        {
                            // Record processing errors but continue monitoring
                            _healthCheck.RecordError(ex, ErrorSeverity.Error);
                            _logger.LogError(ex, "Error processing health metric");
                        }
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // Log the expected cancellation
                    _logger.LogInformation("Health monitoring cancelled normally");

                    // Record monitoring stoppage in health check
                    _healthCheck.RecordError(
                        new Exception("Health monitoring cancelled"),
                        ErrorSeverity.Info);
                }
                catch (Exception ex)
                {
                    // Record unexpected monitoring failures
                    _healthCheck.RecordError(ex, ErrorSeverity.Critical);
                    _logger.LogError(ex, "Health monitoring failed unexpectedly");
                }
                finally
                {
                    // Ensure we mark any degraded state when monitoring stops
                    var finalMetrics = _healthCheck.GetHealthMetrics();
                    if (finalMetrics.Status != HealthStatus.Healthy)
                    {
                        _logger.LogWarning(
                            "Health monitoring stopped with non-healthy status: {Status}",
                            finalMetrics.Status);
                    }
                }
            }, cancellationToken);
            return _monitoringTask;
        }

        public async Task RecordMetricAsync(
            HealthMetric metric,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            await _metricsChannel.Writer.WriteAsync(metric, cancellationToken);
        }

        /// <summary>
        /// Processes health metrics, evaluates system state, and records the results
        /// using the existing HealthCheck system.
        /// </summary>
        private void ProcessHealthMetric(HealthMetric metric)
        {
            if (metric == null)
            {
                _logger.LogWarning("Received null health metric");
                return;
            }

            try
            {
                _logger.LogDebug("Processing health metric: {MetricType} - {MetricName} from {Source}",
                    metric.Type, metric.Name, metric.Source);

                // Record the metric receipt as a message for general health tracking
                _healthCheck.RecordMessageReceived();

                // Process based on metric type
                switch (metric.Type)
                {
                    case MetricType.Connection:
                        ProcessConnectionMetric(metric);
                        break;

                    case MetricType.Performance:
                    case MetricType.TagReads:
                        // These metrics serve as heartbeats for the system
                        _healthCheck.RecordHeartbeat();
                        ProcessPerformanceMetric(metric);
                        break;

                    case MetricType.Memory:
                        ProcessMemoryMetric(metric);
                        break;

                    case MetricType.Errors:
                        ProcessErrorMetric(metric);
                        break;
                }

                // Get current health status after processing
                var healthMetrics = _healthCheck.GetHealthMetrics();

                // Log significant health state changes
                if (healthMetrics.Status >= SmartReaderStandalone.IotDeviceInterface.HealthStatus.Degraded)
                {
                    _logger.LogWarning(
                        "System health degraded: Status={Status}, TimeSinceLastHeartbeat={Heartbeat}, MessagesPerMinute={MessageRate}",
                        healthMetrics.Status,
                        healthMetrics.TimeSinceLastHeartbeat,
                        healthMetrics.MessagesPerMinute);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing health metric: {MetricType} - {MetricName}",
                    metric.Type, metric.Name);
                _healthCheck.RecordError(ex, ErrorSeverity.Error);
            }
        }

        private void ProcessConnectionMetric(HealthMetric metric)
        {
            // Connection metrics directly affect health state
            bool isConnected = metric.Value > 0;

            if (!isConnected)
            {
                _healthCheck.RecordError(
                    new Exception(metric.Description),
                    ErrorSeverity.Critical);
            }
            else
            {
                _healthCheck.RecordHeartbeat();
            }
        }

        private void ProcessPerformanceMetric(HealthMetric metric)
        {
            // For performance metrics, we evaluate against thresholds
            if (metric.Value.HasValue && metric.Value.Value <= 0)
            {
                _healthCheck.RecordError(
                    new Exception($"Performance degradation: {metric.Description}"),
                    ErrorSeverity.Warning);
            }
        }

        private void ProcessMemoryMetric(HealthMetric metric)
        {
            if (!metric.Value.HasValue) return;

            // Memory usage thresholds from the HealthMetric factory
            if (metric.Value.Value > 90)
            {
                _healthCheck.RecordError(
                    new Exception($"Critical memory usage: {metric.Value.Value}%"),
                    ErrorSeverity.Critical);
            }
            else if (metric.Value.Value > 80)
            {
                _healthCheck.RecordError(
                    new Exception($"High memory usage: {metric.Value.Value}%"),
                    ErrorSeverity.Warning);
            }
        }

        private void ProcessErrorMetric(HealthMetric metric)
        {
            // Map MetricSeverity to ErrorSeverity
            var severity = metric.Severity switch
            {
                MetricSeverity.Critical => ErrorSeverity.Critical,
                MetricSeverity.Error => ErrorSeverity.Error,
                MetricSeverity.Warning => ErrorSeverity.Warning,
                _ => ErrorSeverity.Info
            };

            _healthCheck.RecordError(
                new Exception(metric.Description),
                severity);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _monitoringCts.Cancel();
                    _monitoringCts.Dispose();
                    _metricsChannel.Writer.Complete();

                    if (_monitoringTask != null)
                    {
                        // Wait for monitoring to complete with timeout
                        _ = Task.WaitAny(_monitoringTask, Task.Delay(TimeSpan.FromSeconds(5)));
                    }
                }
                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }

        //Task IHealthMonitor.StartMonitoring(CancellationToken cancellationToken)
        //{
        //    throw new NotImplementedException();
        //}
    }



    private sealed class IotDeviceInterfaceEventProcessor
    {
        private readonly SemaphoreSlim _streamingSemaphore = new(1, 1);
        private volatile bool _isStreaming;
        private readonly string _hostname;
        private readonly HttpClient _httpClient;
        private readonly HttpClient _httpClientSecure;
        private readonly IAtlasClient _iotDeviceInterfaceClient;
        private readonly IAtlasClient _iotDeviceInterfaceClientSecure;
        private readonly bool _useBasicAuthAlways;
        private readonly bool _useHttpsAlways;
        private CancellationTokenSource? _cancelSource;
        private Stream? _responseStream;
        private Task? _streamingTask;
        private readonly ILogger<IotDeviceInterfaceEventProcessor> _logger;
        private readonly HealthCheck _healthCheck = new();
        private readonly object _innerEventLock = new();


        private readonly Func<bool> _isNetworkConnectedDelegate;

        private readonly R700IotReader _parent;

        private IReaderConfiguration _configuration;

        internal IotDeviceInterfaceEventProcessor(
            string hostname,
            IReaderConfiguration configuration,
            ILogger<R700IotReader>? eventProcessorLogger = null,
            ILogger<IotDeviceInterfaceEventProcessor>? logger = null,
            ILoggerFactory? loggerFactory = null,
            Func<bool>? isNetworkConnectedDelegate = null,
            R700IotReader? parent = null)
        {
            

            _parent = parent ?? throw new ArgumentNullException(nameof(parent));

            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _isNetworkConnectedDelegate = isNetworkConnectedDelegate ?? throw new ArgumentNullException(nameof(isNetworkConnectedDelegate));

            _configuration = configuration;

            _hostname = Regex.Replace(hostname, "^https*\\://", "");
            var num = (uint)_configuration.Network.Port > 0U ? _configuration.Network.Port : 0;
            _useHttpsAlways = _configuration.Network.UseHttps;
            _useBasicAuthAlways = _configuration.Security.UseBasicAuth;
            var baseUrl1 = num != 0
                ? string.Format("http://{0}:{1}/api/v1", hostname, num)
                : "http://" + hostname + "/api/v1";
            var baseUrl2 = "https://" + hostname + "/api/v1";
            var bytes = Encoding.ASCII.GetBytes(_configuration.Security.Username + ":" + _configuration.Security.Password);
            var timeSpan = new TimeSpan(0, 0, 0, 3, 0);
            ServicePointManager.ServerCertificateValidationCallback +=
                (sender, certificate, chain, sslPolicyErrors) => true;


            var httpClientHandler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (HttpRequestMessage message, X509Certificate2? cert,
                    X509Chain? chain, SslPolicyErrors errors) => true
            };


            if (_configuration.Network.Proxy != null && !string.IsNullOrEmpty(_configuration.Network.Proxy.Address))
            {
                WebProxy webProxy = new(_configuration.Network.Proxy.Address)
                {
                    Credentials = CredentialCache.DefaultNetworkCredentials,
                    BypassProxyOnLocal = true
                };
                _ = webProxy.BypassList.Append("169.254.1.1");
                _ = webProxy.BypassList.Append(hostname);


                httpClientHandler = new HttpClientHandler
                {
                    Proxy = webProxy,
                    UseProxy = true,
                    ServerCertificateCustomValidationCallback = (HttpRequestMessage message, X509Certificate2? cert,
    X509Chain? chain, SslPolicyErrors errors) => true
                };
            }

            if (_useHttpsAlways)
            {


                _httpClient = new HttpClient(httpClientHandler)
                {
                    BaseAddress = new Uri(baseUrl2 + "/"),
                    //Timeout = timeSpan
                    Timeout = Timeout.InfiniteTimeSpan
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

            if (_useBasicAuthAlways)
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

        //private void ResetCancellationToken()
        //{
        //    _cancelSource?.Cancel();
        //    _cancelSource?.Dispose();
        //    _cancelSource = new CancellationTokenSource();
        //}
        public Task<ReaderStatus> GetStatusAsync()
        {
            var readerStatus = new ReaderStatus();
            var systemInfo = _iotDeviceInterfaceClient.SystemAsync().Result;

            var currentInterface = new RfidInterface();
            try
            {
                currentInterface = _iotDeviceInterfaceClient.SystemRfidInterfaceGetAsync().Result;
            }
            catch (Exception)
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

        private async Task StopExistingStreamIfAnyAsync()
        {
            if (_cancelSource != null)
            {
                _cancelSource.Cancel();

                try
                {
                    // Dispose of the response stream
                    if (_responseStream != null)
                    {
                        await _responseStream.DisposeAsync();
                        _responseStream = null;
                    }

                    // Stop the inventory preset
                    await _iotDeviceInterfaceClient.ProfilesStopAsync();

                    // Wait for the streaming task to complete
                    if (_streamingTask != null)
                    {
                        await _streamingTask;
                        _streamingTask = null;
                    }
                }
                catch (TaskCanceledException)
                {
                    // Expected during cancellation
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error stopping existing stream");
                    OnStreamingErrorEvent(new IotDeviceInterfaceException(
                        $"Error stopping stream for {_hostname}", ex));
                }
                finally
                {
                    // Dispose of the cancellation token
                    _cancelSource.Dispose();
                    _cancelSource = null;
                }
            }
        }

        private async Task StreamingAsync()
        {
            try
            {
                while (!_cancelSource?.IsCancellationRequested ?? false)
                {
                    using var responseMessage = await _httpClient.GetAsync(
                        new Uri("data/stream", UriKind.Relative),
                        HttpCompletionOption.ResponseHeadersRead);

                    if (!responseMessage.IsSuccessStatusCode)
                    {
                        HandleHttpError(responseMessage);
                        break;
                    }

                    _responseStream = await responseMessage.Content.ReadAsStreamAsync();
                    using var streamReader = new StreamReader(_responseStream);

                    await ProcessStream(streamReader);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Streaming operation failed");
                OnStreamingErrorEvent(new IotDeviceInterfaceException(
                    "Streaming operation failed", ex));
            }
            finally
            {
                _isStreaming = false;
                _responseStream?.Dispose();
                _responseStream = null;
            }
        }

        internal async Task StartStreamingAsync(string presetId)
        {
            await _streamingSemaphore.WaitAsync();
            try
            {
                if (_isStreaming)
                {
                    _logger.LogWarning("Streaming is already in progress.");
                    return;
                }

                // Ensure any existing stream is stopped.
                await StopExistingStreamIfAnyAsync();

                // Reset cancellation token and mark streaming as active.
                _cancelSource = new CancellationTokenSource();
                _isStreaming = true;
                _streamingTask = Task.Run(() => StreamingAsync(), _cancelSource.Token);

                // Try to start the inventory preset.
                try
                {
                    await _iotDeviceInterfaceClient.ProfilesInventoryPresetsStartAsync(presetId);
                }
                catch (AtlasException ex) when (ex.StatusCode == 409)
                {
                    // The preset is already running – log as informational and continue.
                    _logger.LogInformation("Preset is already running. Skipping preset start.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start streaming");
                await StopExistingStreamIfAnyAsync();
                throw;
            }
            finally
            {
                _ = _streamingSemaphore.Release();
            }
        }



        internal async Task StopStreamingAsync()
        {
            try
            {
                await _streamingSemaphore.WaitAsync();

                // Stop the existing stream
                await StopExistingStreamIfAnyAsync();

                // Reset the streaming state
                _isStreaming = false;
            }
            finally
            {
                _ = _streamingSemaphore.Release();
            }
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


        private async Task ProcessStream(StreamReader streamReader, TimeSpan timeout = default)
        {
            // If no timeout specified, default to 10 seconds
            timeout = timeout == default ? TimeSpan.FromSeconds(10) : timeout;

            while (!_cancelSource.IsCancellationRequested)
            {
                try
                {
                    // Create a task to read the next line with timeout
                    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(_cancelSource.Token);
                    timeoutCts.CancelAfter(timeout);

                    // Check stream state before attempting to read
                    if (streamReader.BaseStream?.CanRead != true)
                    {
                        throw new InvalidOperationException("IoT Interface Processor: Stream is not readable or has been disposed.");
                    }

                    if (streamReader.EndOfStream)
                    {
                        throw new IOException("IoT Interface Processor: End of stream reached unexpectedly.");
                    }

                    // Read with timeout using the combined cancellation token
                    var readTask = streamReader.ReadLineAsync();
                    var str = await ExecuteWithTimeout(readTask, timeoutCts.Token)
                        .ConfigureAwait(false);

                    if (string.IsNullOrWhiteSpace(str))
                    {
                        _logger.LogDebug("IoT Interface Processor: Received an empty or whitespace-only line (stream keepalive).");
                        continue;
                    }

                    _logger.LogDebug("IoT Interface Processor: Processing received line: {Line}", str);

                    // Process the data with timeout as well
                    var processTask = ProcessStreamedData(str);
                    await ExecuteWithTimeout(processTask, timeoutCts.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException ocEx) when (!_cancelSource.Token.IsCancellationRequested)
                {
                    // This catch block handles timeouts specifically
                    _logger.LogError("IoT Interface Processor: Operation timed out. {Message}", ocEx.Message);
                    await HandleTimeout().ConfigureAwait(false);
                }
                catch (IOException ioEx)
                {
                    _logger.LogError("IoT Interface Processor: I/O error while reading the stream. {Message}", ioEx.Message);
                    throw; // Rethrow as this indicates a critical connection issue
                }
                catch (JsonException jsonEx)
                {
                    _logger.LogWarning("IoT Interface Processor: Failed to deserialize a line from the stream. {Message}", jsonEx.Message);
                    // Continue processing as this is a non-critical error
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "IoT Interface Processor: Unexpected error while processing the stream.");
                    // Consider implementing a retry policy here
                    await HandleUnexpectedError(ex).ConfigureAwait(false);
                }
            }
        }

        // Helper method to execute a task with timeout
        private static async Task<T> ExecuteWithTimeout<T>(Task<T> task, CancellationToken cancellationToken)
        {
            try
            {
                return await task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("Operation timed out.");
            }
        }

        private static async Task ExecuteWithTimeout(Task task, CancellationToken cancellationToken)
        {
            try
            {
                await task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("Operation timed out.");
            }
        }

        private async Task HandleTimeout()
        {
            // Implement timeout recovery logic
            _logger.LogInformation("IoT Interface Processor: Attempting to recover from timeout...");

            try
            {
                // Add a small delay before retrying to prevent tight loops
                await Task.Delay(TimeSpan.FromSeconds(1), _cancelSource.Token)
                    .ConfigureAwait(false);

                await StopPresetAsync();

                await Task.Delay(TimeSpan.FromSeconds(1), _cancelSource.Token)
                    .ConfigureAwait(false);

                await StartPresetAsync("SmartReader");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "HandleTimeout: Unexpected error while processing the stream.");
            }

        }

        private async Task HandleUnexpectedError(Exception ex)
        {
            // Implement error recovery logic
            _logger.LogInformation("IoT Interface Processor: Attempting to recover from unexpected error...");

            try
            {
                // Add a small delay before retrying to prevent tight loops
                await Task.Delay(TimeSpan.FromSeconds(1), _cancelSource.Token)
                    .ConfigureAwait(false);

                await StopPresetAsync();

                await Task.Delay(TimeSpan.FromSeconds(1), _cancelSource.Token)
                    .ConfigureAwait(false);

                await StartPresetAsync("SmartReader");
            }
            catch (Exception exInternal)
            {
                _logger.LogError(exInternal, "HandleTimeout: Unexpected error while processing the stream.");
            }
        }

        private Task ProcessStreamedData(string str)
        {
            try
            {
                ReaderEvent? readerEvent = null;

                // Determine the type of event
                if (str.Contains("gpiTransitionEvent"))
                {
                    _logger.LogDebug($"IoT Interface Processor: GpiTransitionEvent: \n {str}");
                    //readerEvent = Newtonsoft.Json.JsonConvert.DeserializeObject<ReaderEvent>(str);
                    var gpiTransitionVm = Newtonsoft.Json.JsonConvert.DeserializeObject<GpiTransitionVm>(str);
                    _logger.LogDebug(gpiTransitionVm.ToString());
                    OnGpiTransitionEvent(gpiTransitionVm);
                    return Task.CompletedTask;
                }

                if (!str.Contains("gpiTransitionEvent"))
                {
                    _logger.LogDebug($"IoT Interface Processor: ReaderEvent: \n {str}");
                    readerEvent = Newtonsoft.Json.JsonConvert.DeserializeObject<ReaderEvent>(str);
                }

                if (readerEvent == null)
                {
                    _logger.LogWarning("IoT Interface Processor: Unrecognized or null event: {Line}", str);
                    return Task.CompletedTask;
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
            return Task.CompletedTask;
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


        //private void CleanupStream()
        //{
        //    if (_responseStream != null)
        //    {
        //        _responseStream.Dispose();
        //        _responseStream = null;
        //    }
        //}

        //private void CleanupAfterStreaming()
        //{
        //    if (_cancelSource != null && _cancelSource.IsCancellationRequested)
        //    {
        //        _cancelSource.Dispose();
        //    }
        //}

        //private void HandleStreamingError(Exception ex)
        //{
        //    _logger.LogWarning("Unable to read streaming: {Message}", ex.Message);
        //    OnStreamingErrorEvent(new IotDeviceInterfaceException("Streaming error detected", ex));
        //}

        //public bool IsHealthy()
        //{
        //    return _isNetworkConnectedDelegate() && (_responseStream != null) && !_cancelSource.IsCancellationRequested;
        //}

        internal bool IsHealthy()
        {
            return IsStreamingActive() &&
                   HasRecentHeartbeat() &&
                   !IsCancellationRequested();
        }

        private bool IsStreamingActive() => _isStreaming;

        private bool HasRecentHeartbeat() =>
            _healthCheck.LastHeartbeat > DateTime.UtcNow.AddSeconds(-30);

        private bool IsCancellationRequested() =>
            _cancelSource?.IsCancellationRequested ?? false;

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

        public async Task<List<ExtendedGpoConfiguration>> DeviceGposGetAsync()
        {
            var endpoint = "device/gpos";
            var completeUri = _httpClient.BaseAddress + endpoint;

            try
            {
                _logger.LogDebug("Getting GPO configurations from {Uri}", completeUri);

                using var response = await _httpClient.GetAsync(completeUri);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Failed to get GPO configurations. Status: {StatusCode}, Response: {Response}",
                        response.StatusCode, errorContent);
                    throw new Exception($"Failed to get GPO configurations: {response.StatusCode} - {errorContent}");
                }

                var jsonContent = await response.Content.ReadAsStringAsync();
                _logger.LogDebug("Received GPO configurations: {Response}", jsonContent);

                // Parse the JSON response
                var jsonObject = JObject.Parse(jsonContent);

                // Create the list of ExtendedGpoConfiguration
                var extendedGpoConfigurations = new List<ExtendedGpoConfiguration>();

                // Parse the gpoConfigurations array
                if (jsonObject["gpoConfigurations"] is JArray gpoArray)
                {
                    foreach (var gpoItem in gpoArray)
                    {
                        var extendedConfig = new ExtendedGpoConfiguration
                        {
                            Gpo = gpoItem["gpo"]?.Value<int>() ?? 0
                        };

                        // Handle the control field
                        var control = gpoItem["control"]?.Value<string>();

                        // Map control values to GpoControlMode
                        switch (control?.ToLower())
                        {
                            case "static":
                                extendedConfig.Control = GpoControlMode.Static;
                                // Handle state for static control
                                var state = gpoItem["state"]?.Value<string>()?.ToLower();
                                extendedConfig.State = state == "high"
                                    ? GpoState.High
                                    : GpoState.Low;
                                break;

                            case "reading-tags":
                                extendedConfig.Control = GpoControlMode.ReadingTags;
                                extendedConfig.State = null;
                                break;

                            case "pulsed":
                                extendedConfig.Control = GpoControlMode.Pulsed;
                                // Extract pulse duration if available
                                if (gpoItem["pulseDuration"] != null)
                                {
                                    extendedConfig.PulseDurationMilliseconds = gpoItem["pulseDuration"]?.Value<int>();
                                }
                                break;

                            case "network":
                                extendedConfig.Control = GpoControlMode.Network;
                                extendedConfig.State = null;
                                break;

                            case "running":
                                extendedConfig.Control = GpoControlMode.Running;
                                extendedConfig.State = null;
                                break;

                            default:
                                // Default to static if unknown
                                extendedConfig.Control = GpoControlMode.Static;
                                extendedConfig.State = GpoState.Low;
                                _logger.LogWarning("Unknown GPO control type: {Control} for GPO {Gpo}", control, extendedConfig.Gpo);
                                break;
                        }

                        extendedGpoConfigurations.Add(extendedConfig);
                    }
                }

                _logger.LogInformation("Successfully retrieved extended GPO configurations for {Count} ports",
                    extendedGpoConfigurations.Count);

                return extendedGpoConfigurations;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP request error while getting GPO configurations");
                throw new Exception("Failed to communicate with the reader", ex);
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogError(ex, "Request timeout while getting GPO configurations");
                throw new Exception("Request timed out", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while getting GPO configurations");
                throw;
            }
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
                request_.Method = new HttpMethod("PUT");
                request_.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                return _httpClient.PutAsync(completeUri, stringContent);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Unexpected error: " + ex.Message);
                return Task.CompletedTask;
            }
        }

        public async Task UpdateInternalProcessorReaderGpoAsync(ExtendedGpoConfigurationRequest extendedConfiguration)
        {
            try
            {
                var endpoint = "device/gpos";
                var completeUri = _httpClient.BaseAddress + endpoint;

                var payload = new
                {
                    gpoConfigurations = extendedConfiguration.GpoConfigurations.Select(gpo =>
                    {
                        var controlValue = gpo.Control == GpoControlMode.ReadingTags
                            ? "reading-tags"
                            : CamelCase(gpo.Control.ToString());

                        var dict = new Dictionary<string, object?>
                        {
                            { "gpo", gpo.Gpo },
                            { "control", controlValue }
                        };

                        if (gpo.State.HasValue)
                            dict.Add("state", CamelCase(gpo.State.ToString()));

                        if (gpo.PulseDurationMilliseconds.HasValue)
                            dict.Add("pulseDurationMilliseconds", gpo.PulseDurationMilliseconds);

                        return dict;
                    }).ToList()
                };

                var options = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                    WriteIndented = false
                };

                string json = JsonSerializer.Serialize(payload, options);
                var requestContent = new StringContent(json, Encoding.UTF8, "application/json");

                _logger.LogDebug("Sending GPO configuration update request to {Uri} with payload: {Payload}", completeUri, json);

                using var response = await _httpClient.PutAsync(completeUri, requestContent);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Failed to update GPO configuration. Status: {StatusCode}, Response: {Response}",
                        response.StatusCode, errorContent);

                    if (response.StatusCode == HttpStatusCode.Forbidden)
                    {
                        throw new Exception($"Access denied when updating GPO configuration: {response.StatusCode} - {errorContent}");
                    }

                    throw new Exception($"Failed to update GPO configuration: {response.StatusCode} - {errorContent}");
                }

                _logger.LogInformation("Successfully updated GPO configuration");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error updating GPO configuration: {Message}", ex.Message);
                throw;
            }
        }

        private static string CamelCase(string value)
        {
            if (string.IsNullOrEmpty(value) || char.IsLower(value[0]))
                return value;

            return char.ToLowerInvariant(value[0]) + value.Substring(1);
        }




        public Task UpdateHttpStreamConfigAsync(HttpStreamConfig streamConfiguration)
        {
            var endpoint = "http-stream";
            var completeUri = _httpClient.BaseAddress + endpoint;

            try
            {
                string json = JsonSerializer.Serialize(streamConfiguration);
                var request_ = new HttpRequestMessage();
                var stringContent = new StringContent(json);
                stringContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");
                request_.Method = new HttpMethod("PUT");
                request_.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                return _httpClient.PutAsync(completeUri, stringContent);
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

        public async Task<HttpResponseMessage> PostTransientInventoryPresetAsync(string json)
        {
            // Acquire the semaphore for exclusive streaming control.
            await _streamingSemaphore.WaitAsync();
            try
            {
                // Stop any existing streaming to prevent overlapping streams.
                await StopExistingStreamIfAnyAsync();

                // Reset cancellation token and mark streaming as active.
                _cancelSource = new CancellationTokenSource();
                _isStreaming = true;

                // Start the streaming task in a thread-safe manner.
                _streamingTask = Task.Run(() => StreamingAsync(), _cancelSource.Token);

                // Construct the POST request for starting the transient inventory preset.
                var endpoint = "profiles/inventory/start";
                var completeUri = _httpClient.BaseAddress + endpoint;
                var requestContent = new StringContent(json, Encoding.UTF8, "application/json");

                // Execute the POST request. This HTTP call is now performed after the streaming task has been safely started.
                return await _httpClient.PostAsync(completeUri, requestContent);
            }
            catch (AtlasException ex) when (ex.StatusCode == 403 || ex.StatusCode == 204)
            {
                try
                {
                    // Unsubscribe from events in a thread-safe way if necessary.
                    lock (_innerEventLock)
                    {
                        _parent.TagInventoryEvent -= _parent.R700IotEventProcessorOnTagInventoryEvent;
                        _parent.StreamingErrorEvent -= _parent.R700IotEventProcessorOnStreamingErrorEvent;
                        _parent.GpiTransitionEvent -= _parent.R700IotEventProcessorOnGpiTransitionEvent;
                        _parent.InventoryStatusEvent -= _parent.R700IotEventProcessorOnInventoryStatusEvent;
                        _parent.DiagnosticEvent -= _parent.R700IotEventProcessorOnDiagnosticEvent;
                    }
                }
                catch (Exception)
                {
                    // Optionally log or handle unsubscription errors.
                }
                return new HttpResponseMessage(HttpStatusCode.OK);
            }
            finally
            {
                _ = _streamingSemaphore.Release();
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
                    _ = _iotDeviceInterfaceClient.ProfilesStopAsync();
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

        public async Task DeleteReaderInventoryPresetAsync(string presetId)
        {
            try
            {
                await _iotDeviceInterfaceClient.ProfilesInventoryPresetsDeleteAsync(presetId);
            }
            catch (AtlasException ex) when (ex.StatusCode == 409)
            {
                _logger.LogDebug("Preset deletion skipped because the preset is already running.");
            }
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



    private class StreamingConfiguration
    {
        public int MaxRetryAttempts { get; set; } = 3;
        public TimeSpan InitialRetryDelay { get; set; } = TimeSpan.FromSeconds(1);
        public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(30);
        public TimeSpan StreamTimeout { get; set; } = TimeSpan.FromSeconds(30);
    }

}
