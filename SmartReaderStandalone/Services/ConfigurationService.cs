using McMaster.NETCore.Plugins;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;
using SmartReader.Infrastructure.Database;
using SmartReader.Infrastructure.ViewModel;
using SmartReaderJobs.Utils;
using SmartReaderStandalone.Entities;
using SmartReaderStandalone.IotDeviceInterface;
using SmartReaderStandalone.Plugins;
using SmartReaderStandalone.Utils;
using SmartReaderStandalone.ViewModel.Status;
using System.Data;

namespace SmartReaderStandalone.Services
{
    public interface IConfigurationService
    {
        HttpUtil CreateHttpUtil(IHttpClientFactory httpClientFactory);
        Task<StandaloneConfigDTO> GetConfigDtoFromDb();
        Task<string> GetLicenseFromDb();
        string GetReaderAddress();
        string GetReaderPassword();
        string GetReaderUsername();
        string GetServerAuthToken();
        string GetServerUrl();
        void Initialize();
        Task<Dictionary<string, IPlugin>> InitializePluginsAsync(string deviceId, List<PluginLoader> pluginLoaders);
        StandaloneConfigDTO LoadConfig();
        List<PluginLoader> LoadPluginAssemblies(string pluginsDir);
        Task<string> ReadMqttCommandIdFromFile();
        void SaveConfigDtoToDb(StandaloneConfigDTO dto);
        void SaveInventoryStatusToDb(string status, int currentCount, string cycleId, bool isStopRequest);
        void SaveJsonTagEventToDb(JObject dto);
        void SaveLicenseToDb(string license);
        Task SaveSerialToDbAsync(string deviceSerial);
        void SaveStartCommandToDb();
        void SaveStartPresetCommandToDb();
        void SaveStopCommandToDb();
        void SaveStopPresetCommandToDb();
        void SaveUpgradeCommandToDb(string remoteUrl);
        Task UpdateLicenseAndConfigAsync(string deviceId, Dictionary<string, IPlugin> plugins);
        Task<bool> WriteMqttCommandIdToFile(string commandId);
    }

    public class ConfigurationService : IConfigurationService
    {
        private readonly IConfiguration _configuration;
        public IServiceProvider Services { get; }
        private readonly ILogger<ConfigurationService> _logger;
        private StandaloneConfigDTO _standaloneConfigDTO;
        private readonly SemaphoreSlim readLock = new(1, 1);

        public ConfigurationService(IServiceProvider services, IConfiguration configuration,
            ILogger<ConfigurationService> logger)
        {
            Services = services;
            _logger = logger;
            _configuration = new ConfigurationBuilder()
                .AddConfiguration(configuration) // Include existing configuration (e.g., from appsettings)
                .AddJsonFile("/customer/appsettings.json", optional: false, reloadOnChange: true)
                .AddJsonFile("/customer/customsettings.json", optional: false, reloadOnChange: true)
                .Build();
            _standaloneConfigDTO = LoadConfig();
        }

        public void Initialize()
        {
            // This method ensures initialization logic, if necessary
            _standaloneConfigDTO = LoadConfig();
        }

        public StandaloneConfigDTO LoadConfig()
        {
            StandaloneConfigDTO? standaloneConfigDTO = null;
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
                        standaloneConfigDTO = StandaloneConfigDTO.CleanupUrlEncoding(ConfigFileHelper.ReadFile());
                        Console.WriteLine("Config loaded. " + standaloneConfigDTO.antennaPorts);
                    }
                }
            }
            catch (Exception)
            {
            }

            return standaloneConfigDTO;
        }

        public string GetServerUrl()
        {
            return _configuration.GetValue<string>("ServerInfo:Url");
        }

        public string GetServerAuthToken()
        {
            return _configuration.GetValue<string>("ServerInfo:AuthToken");
        }

        public string GetReaderAddress()
        {
            string readerAddress = _configuration.GetValue<string>("ReaderInfo:Address") ?? "127.0.0.1";
            string buildConfiguration = "Release";

#if DEBUG
            buildConfiguration = "Debug";
#endif

            if ("Debug".Equals(buildConfiguration))
            {
                readerAddress = _configuration.GetValue<string>("ReaderInfo:DebugAddress") ?? readerAddress;
            }

            return readerAddress;
        }

        public string GetReaderUsername()
        {
            return _configuration.GetValue<string>("RShellAuth:UserName") ?? "root";
        }

        public string GetReaderPassword()
        {
            return _configuration.GetValue<string>("RShellAuth:Password") ?? "impinj";
        }

        public HttpUtil CreateHttpUtil(IHttpClientFactory httpClientFactory)
        {
            var serverUrl = GetServerUrl();
            var serverToken = GetServerAuthToken();
            return new HttpUtil(httpClientFactory, serverUrl, serverToken);
        }

        public async Task UpdateLicenseAndConfigAsync(string deviceId, Dictionary<string, IPlugin> plugins)
        {
            try
            {
                _logger.LogDebug("Starting UpdateLicenseAndConfigAsync...");

                // Update license
                await UpdateLicenseAsync(deviceId, plugins);

                // Update configuration
                UpdateConfiguration();
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "An unexpected error occurred in UpdateLicenseAndConfigAsync.");
            }
        }

        private async Task UpdateLicenseAsync(string deviceId, Dictionary<string, IPlugin> plugins)
        {
            try
            {
                if (plugins?.ContainsKey("LICENSE") == false)
                {
                    _logger.LogDebug("License key not found in plugins. Updating license...");

                    var expectedLicense = string.IsNullOrEmpty(deviceId)
                        ? throw new InvalidOperationException("DeviceId is null or empty. Cannot generate expected license.")
                        : SmartReaderJobs.Utils.Utils.CreateMD5Hash("sM@RTrEADER2022-" + deviceId);

                    var licenseToSet = await GetLicenseFromDb();
                    if (string.IsNullOrEmpty(licenseToSet))
                    {
                        _logger.LogWarning("No license found in database. Using the existing license from configuration.");
                        licenseToSet = _standaloneConfigDTO?.licenseKey ?? string.Empty;
                    }

                    SaveLicenseToDb(licenseToSet);
                    _logger.LogDebug("License updated successfully: {License}", licenseToSet);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while updating the license.");
            }
        }

        private void UpdateConfiguration()
        {
            try
            {
                var configModel = GetConfigDtoFromDb();
                if (configModel == null)
                {
                    _logger.LogWarning("No configuration found in the database.");
                    return;
                }

                if (!_standaloneConfigDTO.Equals(configModel))
                {
                    _logger.LogInformation("Configuration mismatch detected. Updating configuration...");

                    ConfigFileHelper.SaveFile(configModel);
                    _standaloneConfigDTO = LoadConfig();

                    SaveConfigDtoToDb(_standaloneConfigDTO);
                    _logger.LogInformation("Configuration updated successfully.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while updating the configuration.");
            }
        }

        // Add helper methods for database and configuration file operations
#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        public async Task<string> GetLicenseFromDb()
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
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
                    configModel = new ReaderConfigs
                    {
                        Id = "READER_LICENSE",
                        Value = license
                    };
                    _ = dbContext.ReaderConfigs.Add(configModel);
                }
                else
                {
                    configModel.Value = license;
                    _ = dbContext.ReaderConfigs.Update(configModel);
                }

                _ = await dbContext.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError("Unexpected error on SaveLicenseToDb. " + ex.Message);
            }
            finally
            {
                _ = readLock.Release();
            }
        }

        public StandaloneConfigDTO GetConfigDtoFromDb()
        {
#pragma warning disable CS8600, CS8601, CS8602, CS8603, CS8604, CS8625, CS8629 // Dereference of a possibly null reference.
            StandaloneConfigDTO? model = null;
            try
            {
                using var scope = Services.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<RuntimeDb>();
                var configModel = dbContext.ReaderConfigs.FindAsync("READER_CONFIG").Result;
                if (configModel != null)
                {

                    var savedConfigDTO = JsonConvert.DeserializeObject<StandaloneConfigDTO>(configModel.Value);
                    if (savedConfigDTO != null)
                    {
                        model = StandaloneConfigDTO.CleanupUrlEncoding(savedConfigDTO); ;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error on GetConfigDtoFromDb. " + ex.Message);
            }

            return model;
#pragma warning restore CS8600, CS8601, CS8602, CS8603, CS8604, CS8625, CS8629 // Dereference of a possibly null reference.
        }

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
                    configModel = new ReaderConfigs
                    {
                        Id = "READER_CONFIG",
                        Value = JsonConvert.SerializeObject(dto)
                    };
                    _ = dbContext.ReaderConfigs.Add(configModel);
                }
                else
                {
                    configModel.Value = JsonConvert.SerializeObject(dto);
                    _ = dbContext.ReaderConfigs.Update(configModel);
                }

                _ = await dbContext.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error on SaveConfigDtoToDb. " + ex.Message);
            }
            finally
            {
                _ = readLock.Release();
            }
        }

        /// <summary>
        /// Saves or updates a device serial number in the database using proper concurrency control.
        /// </summary>
        /// <param name="deviceSerial">The serial number of the device to be saved</param>
        /// <returns>Task representing the asynchronous operation</returns>
        /// <exception cref="ArgumentNullException">Thrown when deviceSerial is null or empty</exception>
        public async Task SaveSerialToDbAsync(string deviceSerial)
        {
            // Input validation is crucial for robustness
            if (string.IsNullOrWhiteSpace(deviceSerial))
            {
                throw new ArgumentNullException(nameof(deviceSerial));
            }

            try
            {
                // Using await with a disposable lock pattern for better resource management
                using var lockAcquisition = await readLock.WaitAsyncWithTimeout(TimeSpan.FromSeconds(30));

                // Creating a transactional scope to ensure database consistency
                using var scope = Services.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<RuntimeDb>();

                // Using proper async/await pattern without .Result to prevent deadlocks
                var configModel = await dbContext.ReaderStatus.FindAsync("READER_SERIAL");

                // Using object initialization for cleaner code
                var serialModelDto = new SmartreaderSerialNumberDto
                {
                    SerialNumber = deviceSerial
                };

                var serialModelDtoList = new List<SmartreaderSerialNumberDto> { serialModelDto };

                if (configModel == null)
                {
                    configModel = new ReaderStatus
                    {
                        Id = "READER_SERIAL",
                        Value = JsonConvert.SerializeObject(serialModelDtoList, new JsonSerializerSettings
                        {
                            NullValueHandling = NullValueHandling.Ignore
                        })
                    };
                    _ = await dbContext.ReaderStatus.AddAsync(configModel);
                }
                else
                {
                    configModel.Value = JsonConvert.SerializeObject(serialModelDtoList, new JsonSerializerSettings
                    {
                        NullValueHandling = NullValueHandling.Ignore
                    });
                    _ = dbContext.ReaderStatus.Update(configModel);
                }

                // Adding retry logic for transient database errors
                _ = await RetryPolicy.ExecuteAsync(async () => await dbContext.SaveChangesAsync());
            }
            catch (TimeoutException tex)
            {
                _logger.LogError(tex, "Timeout while attempting to acquire lock for saving device serial: {DeviceSerial}", deviceSerial);
                throw;
            }
            catch (DbUpdateConcurrencyException dcex)
            {
                _logger.LogError(dcex, "Concurrency conflict while saving device serial: {DeviceSerial}", deviceSerial);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while saving device serial: {DeviceSerial}. Error: {ErrorMessage}",
                    deviceSerial, ex.Message);
                throw;
            }
            finally
            {
                // The lock release is now handled by the disposable pattern
            }
        }

        public async void SaveStartPresetCommandToDb()
        {
            try
            {
                _logger.LogInformation("Requesting Start Preset Command... ");
                await readLock.WaitAsync();
                using var scope = Services.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<RuntimeDb>();
                var commandModel = dbContext.ReaderCommands.FindAsync("START_PRESET").Result;
                if (commandModel == null)
                {
                    commandModel = new ReaderCommands
                    {
                        Id = "START_PRESET",
                        Value = "START",
                        Timestamp = DateTime.Now
                    };
                    _ = dbContext.ReaderCommands.Add(commandModel);
                }
                else
                {
                    commandModel.Value = "START";
                    commandModel.Timestamp = DateTime.Now;
                    _ = dbContext.ReaderCommands.Update(commandModel);
                }

                _ = await dbContext.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError("Unexpected error on SaveStartPresetCommandToDb. " + ex.Message);
            }
            finally
            {
                _ = readLock.Release();
            }
        }

        public async void SaveStopPresetCommandToDb()
        {
            try
            {
                _logger.LogInformation("Requesting Stop Preset Command... ");
                await readLock.WaitAsync();
                using var scope = Services.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<RuntimeDb>();
                var commandModel = dbContext.ReaderCommands.FindAsync("STOP_PRESET").Result;
                if (commandModel == null)
                {
                    commandModel = new ReaderCommands
                    {
                        Id = "STOP_PRESET",
                        Value = "STOP",
                        Timestamp = DateTime.Now
                    };
                    _ = dbContext.ReaderCommands.Add(commandModel);
                }
                else
                {
                    commandModel.Value = "STOP";
                    commandModel.Timestamp = DateTime.Now;
                    _ = dbContext.ReaderCommands.Update(commandModel);
                }

                _ = await dbContext.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError("Unexpected error on SaveStopPresetCommandToDb. " + ex.Message);
            }
            finally
            {
                _ = readLock.Release();
            }
        }

        public async void SaveStartCommandToDb()
        {
            try
            {
                _logger.LogInformation("Requesting Start Command... ");
                await readLock.WaitAsync();
                using var scope = Services.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<RuntimeDb>();
                var commandModel = dbContext.ReaderCommands.FindAsync("START_INVENTORY").Result;
                if (commandModel == null)
                {
                    commandModel = new ReaderCommands
                    {
                        Id = "START_INVENTORY",
                        Value = "START",
                        Timestamp = DateTime.Now
                    };
                    _ = dbContext.ReaderCommands.Add(commandModel);
                }
                else
                {
                    commandModel.Value = "START";
                    commandModel.Timestamp = DateTime.Now;
                    _ = dbContext.ReaderCommands.Update(commandModel);
                }

                _ = await dbContext.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError("Unexpected error on SaveStartCommandToDb. " + ex.Message);
            }
            finally
            {
                _ = readLock.Release();
            }
        }

        public async void SaveStopCommandToDb()
        {
            try
            {
                _logger.LogInformation("Requesting Stop Command... ");
                await readLock.WaitAsync();
                using var scope = Services.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<RuntimeDb>();
                var commandModel = dbContext.ReaderCommands.FindAsync("STOP_INVENTORY").Result;
                if (commandModel == null)
                {
                    commandModel = new ReaderCommands
                    {
                        Id = "STOP_INVENTORY",
                        Value = "STOP",
                        Timestamp = DateTime.Now
                    };
                    _ = dbContext.ReaderCommands.Add(commandModel);
                }
                else
                {
                    commandModel.Value = "STOP";
                    commandModel.Timestamp = DateTime.Now;
                    _ = dbContext.ReaderCommands.Update(commandModel);
                }

                _ = await dbContext.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError("Unexpected error on SaveStopCommandToDb. " + ex.Message);
            }
            finally
            {
                _ = readLock.Release();
            }
        }

        public async void SaveUpgradeCommandToDb(string remoteUrl)
        {
            try
            {
                _logger.LogInformation("Requesting Upgrade Command... ");
                await readLock.WaitAsync();
                using var scope = Services.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<RuntimeDb>();
                var commandModel = dbContext.ReaderCommands.FindAsync("UPGRADE_SYSTEM_IMAGE").Result;
                if (commandModel == null)
                {
                    commandModel = new ReaderCommands
                    {
                        Id = "UPGRADE_SYSTEM_IMAGE",
                        Value = remoteUrl,
                        Timestamp = DateTime.Now
                    };
                    _ = dbContext.ReaderCommands.Add(commandModel);
                }
                else
                {
                    commandModel.Value = remoteUrl;
                    commandModel.Timestamp = DateTime.Now;
                    _ = dbContext.ReaderCommands.Update(commandModel);
                }

                _ = await dbContext.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError("Unexpected error on SaveUpgradeCommandToDb. " + ex.Message);
            }
            finally
            {
                _ = readLock.Release();
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
                    dataModel = new InventoryStatus
                    {
                        Id = "INVENTORY_STATUS",
                        CurrentStatus = status,
                        TotalCount = currentCount,
                        CycleId = cycleId,
                        StartedOn = DateTimeOffset.Now
                    };
                    if (isStopRequest)
                        dataModel.StoppedOn = DateTimeOffset.Now;
                    else
                        dataModel.StoppedOn = null;
                    _ = dbContext.InventoryStatus.Add(dataModel);
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
                    _ = dbContext.InventoryStatus.Update(dataModel);
                }

                _ = await dbContext.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error on SaveInventoryStatusToDb. " + ex.Message);
            }
            finally
            {
                _ = readLock.Release();
            }
        }

        public async void SaveJsonTagEventToDb(JObject dto)
        {
            try
            {
                await readLock.WaitAsync();
                using var scope = Services.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<RuntimeDb>();
                var configModel = new SmartReaderTagReadModel
                {
                    Value = JsonConvert.SerializeObject(dto)
                };
                _ = dbContext.SmartReaderTagReadModels.Add(configModel);
                _ = await dbContext.SaveChangesAsync();
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

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        public async Task<string> ReadMqttCommandIdFromFile()
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
        {
#pragma warning disable CS8600, CS8601, CS8602, CS8603, CS8604, CS8625, CS8629 // Dereference of a possibly null reference.
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
#pragma warning restore CS8600, CS8601, CS8602, CS8603, CS8604, CS8625, CS8629 // Dereference of a possibly null reference.
        }

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        public async Task<bool> WriteMqttCommandIdToFile(string commandId)
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
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



        //public void ConfigureSocketServer(SimpleTcpServer server,
        //    EventHandler<ConnectionEventArgs> ClientConnected,
        //    EventHandler<ConnectionEventArgs> ClientDisconnected,
        //    EventHandler<DataReceivedEventArgs> DataReceived,
        //    EventHandler<DataSentEventArgs> DataSent)
        //{
        //    if (server == null) throw new ArgumentNullException(nameof(server));

        //    server.Events.ClientConnected += ClientConnected!;
        //    server.Events.ClientDisconnected += ClientDisconnected!;
        //    server.Events.DataReceived += DataReceived!;
        //    server.Events.DataSent += DataSent!;

        //    server.Keepalive.EnableTcpKeepAlives = true;
        //    server.Keepalive.TcpKeepAliveInterval = 5;
        //    server.Keepalive.TcpKeepAliveTime = 10;
        //    server.Keepalive.TcpKeepAliveRetryCount = 5;
        //}

        public List<PluginLoader> LoadPluginAssemblies(string pluginsDir)
        {
            var pluginLoaders = new List<PluginLoader>();
            var files = Directory.GetFiles(pluginsDir);

            foreach (var file in files)
            {
                if (!file.EndsWith(".dll")) continue;

                if (File.Exists(file))
                {
                    var loader = PluginLoader.CreateFromAssemblyFile(
                        file,
                        sharedTypes: new[] { typeof(IPlugin) }
                    );
                    pluginLoaders.Add(loader);
                }
            }
            return pluginLoaders;
        }

        public async Task<Dictionary<string, IPlugin>> InitializePluginsAsync(string deviceId, List<PluginLoader> pluginLoaders)
        {
            var plugins = new Dictionary<string, IPlugin>();

            foreach (var loader in pluginLoaders)
            {
                try
                {
                    // Load all plugin types from the loader
                    var pluginTypes = loader
                        .LoadDefaultAssembly()
                        .GetTypes()
                        .Where(t => typeof(IPlugin).IsAssignableFrom(t) && !t.IsAbstract);

                    foreach (var pluginType in pluginTypes)
                    {
                        try
                        {
                            // Create an instance of the plugin
                            var plugin = Activator.CreateInstance(pluginType) as IPlugin;
                            if (plugin != null)
                            {
                                _logger.LogInformation($"Created plugin: {plugin.GetName()}");
                                plugin.SetDeviceId(deviceId);
                                _ = await plugin.Init();
                                plugins.Add(plugin.GetName(), plugin);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"Error initializing plugin of type {pluginType.FullName}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error loading plugins from loader {loader}");
                }
            }

            return plugins;
        }

        Task<StandaloneConfigDTO> IConfigurationService.GetConfigDtoFromDb()
        {
            throw new NotImplementedException();
        }
    }
}
