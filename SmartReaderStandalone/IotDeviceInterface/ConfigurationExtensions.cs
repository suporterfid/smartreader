using System.Text.Json;
using System.Text.Json.Serialization;
using SmartReaderStandalone.IotDeviceInterface;

namespace SmartReaderStandalone.IotDeviceInterface
{
    public static class ConfigurationExtensions
    {
        /// <summary>
        /// Loads reader configuration from a JSON file.
        /// </summary>
        public static ReaderConfiguration LoadFromJson(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("Configuration file not found", filePath);

            try
            {
                var configBuilder = new ConfigurationBuilder()
                    .AddJsonFile(filePath, optional: false, reloadOnChange: false);

                var configuration = configBuilder.Build();
                return LoadFromConfiguration(configuration, "ReaderConfiguration");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to load configuration from JSON file '{filePath}': {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Loads reader configuration from the appsettings.json section.
        /// </summary>
        public static ReaderConfiguration LoadFromConfiguration(IConfiguration configuration, string sectionName = "ReaderConfiguration")
        {
            var section = configuration.GetSection(sectionName);
            if (!section.Exists())
                throw new InvalidOperationException($"Configuration section '{sectionName}' not found");

            var config = new ReaderConfiguration();

            try
            {
                section.Bind(config);

                // Garantir que todas as propriedades necessárias estão inicializadas
                config = EnsurePropertiesInitialized(config);

                // Validate the loaded configuration
                var validationResult = config.Validate();
                if (!validationResult.IsValid)
                {
                    var errorDetails = string.Join("\n  - ", validationResult.Errors);
                    throw new InvalidOperationException(
                        $"Invalid configuration found {validationResult.Errors.Count} errors:\n  - {errorDetails}");
                }

                return config;
            }
            catch (Exception ex) when (!(ex is InvalidOperationException))
            {
                throw new InvalidOperationException($"Failed to bind configuration from section '{sectionName}': {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Saves reader configuration to a JSON file.
        /// </summary>
        public static void SaveToJson(this ReaderConfiguration config, string filePath)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            Dictionary<string, JsonElement> root;

            // Carrega o JSON existente, se houver
            if (File.Exists(filePath))
            {
                var existingJson = File.ReadAllText(filePath);
                root = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(existingJson) ?? new();
            }
            else
            {
                root = new Dictionary<string, JsonElement>();
            }

            // Serializa a configuração do ReaderConfiguration
            var readerJson = JsonSerializer.SerializeToElement(config, options);

            // Atualiza a seção "ReaderConfiguration"
            root["ReaderConfiguration"] = readerJson;

            // Serializa o dicionário completo de volta para o arquivo
            var finalJson = JsonSerializer.Serialize(root, options);
            File.WriteAllText(filePath, finalJson);
        }


        /// <summary>
        /// Ensures all required properties are properly initialized
        /// </summary>
        private static ReaderConfiguration EnsurePropertiesInitialized(ReaderConfiguration config)
        {
            // Inicializar Network se for nulo
            if (config.Network == null)
            {
                config.Network = NetworkSettings.CreateDefault();
            }
            else
            {
                // Garantir que propriedades required do Network estão inicializadas
                EnsureNetworkPropertiesInitialized(config.Network);
            }

            // Inicializar Security se for nulo
            if (config.Security == null)
            {
                config.Security = SecuritySettings.CreateDefault();
            }
            else
            {
                // Garantir que propriedades required do Security estão inicializadas
                EnsureSecurityPropertiesInitialized(config.Security);
            }

            // Inicializar Streaming se for nulo
            if (config.Streaming == null)
            {
                config.Streaming = StreamingSettings.CreateDefault();
            }

            // Inicializar RetryPolicy se for nulo
            if (config.RetryPolicy == null)
            {
                config.RetryPolicy = StreamRetryPolicy.CreateDefault();
            }
            else
            {
                // Garantir que RetryableStatusCodes está inicializada
                if (config.RetryPolicy.RetryableStatusCodes == null || config.RetryPolicy.RetryableStatusCodes.Count == 0)
                {
                    config.RetryPolicy.RetryableStatusCodes = new List<int> { 408, 429, 500, 502, 503, 504 };
                }
            }

            // Inicializar Gpo se for nulo
            if (config.Gpo == null)
            {
                config.Gpo = GpoSettings.CreateDefault();
            }
            else
            {
                // Garantir que DefaultConfigurations está inicializada
                if (config.Gpo.DefaultConfigurations == null)
                {
                    config.Gpo.DefaultConfigurations = new List<ExtendedGpoConfiguration>();
                }
            }

            return config;
        }

        /// <summary>
        /// Ensures Network properties are properly initialized
        /// </summary>
        private static void EnsureNetworkPropertiesInitialized(NetworkSettings network)
        {
            // Garantir que Hostname não é nulo
            if (string.IsNullOrEmpty(network.Hostname))
            {
                network.Hostname = "localhost";
            }

            // Garantir que Proxy está inicializado
            if (network.Proxy == null)
            {
                network.Proxy = new ProxySettings();
            }
            else
            {
                // Garantir que BypassList está inicializada
                if (network.Proxy.BypassList == null)
                {
                    network.Proxy.BypassList = new List<string> { "localhost", "127.0.0.1", "169.254.1.1" };
                }

                // Garantir que propriedades string não são nulas
                if (network.Proxy.Address == null)
                {
                    network.Proxy.Address = string.Empty;
                }
                if (network.Proxy.Username == null)
                {
                    network.Proxy.Username = string.Empty;
                }
                if (network.Proxy.Password == null)
                {
                    network.Proxy.Password = string.Empty;
                }
            }
        }

        /// <summary>
        /// Ensures Security properties are properly initialized
        /// </summary>
        private static void EnsureSecurityPropertiesInitialized(SecuritySettings security)
        {
            // Garantir que propriedades string não são nulas
            if (security.Username == null)
            {
                security.Username = "root";
            }
            if (security.Password == null)
            {
                security.Password = "impinj";
            }
        }

        /// <summary>
        /// Creates a ReaderConfiguration from JSON string (for testing)
        /// </summary>
        public static ReaderConfiguration FromJsonString(string json)
        {
            var tempFile = Path.GetTempFileName();
            try
            {
                File.WriteAllText(tempFile, json);
                return LoadFromJson(tempFile);
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
        }

        /// <summary>
        /// Validates a JSON configuration without loading it
        /// </summary>
        public static ValidationResult ValidateJsonConfiguration(string filePath)
        {
            try
            {
                var config = LoadFromJson(filePath);
                return new ValidationResult(new List<string>());
            }
            catch (InvalidOperationException ex)
            {
                if (ex.Message.StartsWith("Invalid configuration"))
                {
                    // Extrair os erros da mensagem
                    var errorsPart = ex.Message.Substring(ex.Message.IndexOf("errors:") + 7);
                    var errors = errorsPart.Split(new[] { "\n  - " }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(e => e.Trim())
                        .Where(e => !string.IsNullOrEmpty(e))
                        .ToList();

                    return new ValidationResult(errors);
                }
                return new ValidationResult(new List<string> { ex.Message });
            }
            catch (Exception ex)
            {
                return new ValidationResult(new List<string> { $"Configuration loading failed: {ex.Message}" });
            }
        }
    }
}