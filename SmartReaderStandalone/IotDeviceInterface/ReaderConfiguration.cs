using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmartReaderStandalone.IotDeviceInterface
{
    /// <summary>
    /// Defines the contract for RFID reader configuration settings.
    /// </summary>
    public interface IReaderConfiguration
    {
        /// <summary>
        /// Gets the network-related settings for the reader.
        /// </summary>
        NetworkSettings Network { get; }

        /// <summary>
        /// Gets the security-related settings for the reader.
        /// </summary>
        SecuritySettings Security { get; }

        /// <summary>
        /// Gets the streaming-related settings for the reader.
        /// </summary>
        StreamingSettings Streaming { get; }

        /// <summary>
        /// Gets the retry policy settings for operations.
        /// </summary>
        StreamRetryPolicy RetryPolicy { get; }

        /// <summary>
        /// Validates the configuration settings.
        /// </summary>
        /// <returns>True if the configuration is valid; otherwise, false.</returns>
        ValidationResult Validate();
    }

    /// <summary>
    /// Represents the complete configuration for an RFID reader.
    /// </summary>
    public class ReaderConfiguration : IReaderConfiguration
    {
        private readonly ILogger<ReaderConfiguration> _logger;

        /// <summary>
        /// Gets or sets the GPO configuration settings.
        /// </summary>
        public GpoSettings Gpo { get; set; }

        /// <summary>
        /// Initializes a new instance of the ReaderConfiguration class.
        /// </summary>
        /// <param name="logger">Logger for configuration-related events.</param>
        public ReaderConfiguration(ILogger<ReaderConfiguration>? logger = null)
        {
            _logger = logger;

            // Initialize with default settings
            Network = NetworkSettings.CreateDefault();
            Security = SecuritySettings.CreateDefault();
            Streaming = StreamingSettings.CreateDefault();
            RetryPolicy = StreamRetryPolicy.CreateDefault();
            Gpo = GpoSettings.CreateDefault();
        }

        /// <summary>
        /// Gets or sets the network configuration settings.
        /// </summary>
        public NetworkSettings Network { get; set; }

        /// <summary>
        /// Gets or sets the security configuration settings.
        /// </summary>
        public SecuritySettings Security { get; set; }

        /// <summary>
        /// Gets or sets the streaming configuration settings.
        /// </summary>
        public StreamingSettings Streaming { get; set; }

        /// <summary>
        /// Gets or sets the retry policy settings.
        /// </summary>
        public StreamRetryPolicy RetryPolicy { get; set; }

        /// <summary>
        /// Creates a new configuration instance with default settings.
        /// </summary>
        public static ReaderConfiguration CreateDefault()
        {
            return new ReaderConfiguration
            {
                Network = NetworkSettings.CreateDefault(),
                Security = SecuritySettings.CreateDefault(),
                Streaming = StreamingSettings.CreateDefault(),
                RetryPolicy = StreamRetryPolicy.CreateDefault(),
                Gpo = GpoSettings.CreateDefault() 
            };
        }

        /// <summary>
        /// Validates all configuration settings.
        /// </summary>
        public ValidationResult Validate()
        {
            var validationErrors = new List<string>();

            // Validate each configuration section
            validationErrors.AddRange(Network.Validate());
            validationErrors.AddRange(Security.Validate());
            validationErrors.AddRange(Streaming.Validate());
            validationErrors.AddRange(RetryPolicy.Validate());
            validationErrors.AddRange(Gpo.Validate());

            if (validationErrors.Any())
            {
                _logger?.LogWarning("Configuration validation failed with {ErrorCount} errors",
                    validationErrors.Count);
            }

            return new ValidationResult(validationErrors);
        }
    }

    /// <summary>
    /// Represents network-related configuration settings.
    /// </summary>
    public class NetworkSettings
    {
        /// <summary>
        /// Gets or sets the hostname or IP address of the reader.
        /// </summary>
        public required string Hostname { get; set; }

        /// <summary>
        /// Gets or sets the port number for the connection.
        /// </summary>
        public int Port { get; set; }

        /// <summary>
        /// Gets or sets the connection timeout in seconds.
        /// </summary>
        public int ConnectionTimeoutSeconds { get; set; }

        /// <summary>
        /// Gets or sets whether to use HTTP instead of HTTPS.
        /// </summary>
        public bool UseHttps { get; set; }

        /// <summary>
        /// Gets or sets proxy server settings if required.
        /// </summary>
        public required ProxySettings Proxy { get; set; }

        public static NetworkSettings CreateDefault()
        {
            return new NetworkSettings
            {
                Hostname = "localhost",
                Port = 443,
                ConnectionTimeoutSeconds = 30,
                UseHttps = false,
                Proxy = new ProxySettings()
            };
        }

        internal IEnumerable<string> Validate()
        {
            if (string.IsNullOrWhiteSpace(Hostname))
                yield return "Hostname cannot be empty";

            if (Port <= 0 || Port > 65535)
                yield return "Port must be between 1 and 65535";

            if (ConnectionTimeoutSeconds <= 0)
                yield return "Connection timeout must be greater than 0 seconds";

            if (Proxy?.Enabled == true)
            {
                foreach (var error in Proxy.Validate())
                {
                    yield return error;
                }
            }
        }
    }

    /// <summary>
    /// Represents security-related configuration settings.
    /// </summary>
    public class SecuritySettings
    {
        /// <summary>
        /// Gets or sets whether to use basic authentication.
        /// </summary>
        public bool UseBasicAuth { get; set; }

        /// <summary>
        /// Gets or sets the username for authentication.
        /// </summary>
        public required string Username { get; set; }

        /// <summary>
        /// Gets or sets the password for authentication.
        /// </summary>
        public required string Password { get; set; }

        /// <summary>
        /// Gets or sets whether to validate SSL certificates.
        /// </summary>
        public bool ValidateCertificates { get; set; }

        public static SecuritySettings CreateDefault()
        {
            return new SecuritySettings
            {
                UseBasicAuth = true,
                Username = "root",
                Password = "impinj",
                ValidateCertificates = false
            };
        }

        internal IEnumerable<string> Validate()
        {
            if (UseBasicAuth)
            {
                if (string.IsNullOrWhiteSpace(Username))
                    yield return "Username is required when using basic authentication";

                if (string.IsNullOrWhiteSpace(Password))
                    yield return "Password is required when using basic authentication";
            }
        }
    }

    /// <summary>
    /// Represents proxy server configuration settings for network communication.
    /// </summary>
    public class ProxySettings
    {
        /// <summary>
        /// Gets or sets whether proxy server should be used for communication.
        /// When disabled, all other proxy settings are ignored.
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// Gets or sets the proxy server address (hostname or IP).
        /// Only used when Enabled is true.
        /// </summary>
        public string Address { get; set; }

        /// <summary>
        /// Gets or sets the proxy server port number.
        /// Must be between 1 and 65535 when proxy is enabled.
        /// </summary>
        public int Port { get; set; }

        /// <summary>
        /// Gets or sets whether authentication is required for the proxy.
        /// </summary>
        public bool RequiresAuthentication { get; set; }

        /// <summary>
        /// Gets or sets the username for proxy authentication.
        /// Only used when RequiresAuthentication is true.
        /// </summary>
        public string Username { get; set; }

        /// <summary>
        /// Gets or sets the password for proxy authentication.
        /// Only used when RequiresAuthentication is true.
        /// </summary>
        public string Password { get; set; }

        /// <summary>
        /// Gets or sets whether local addresses should bypass the proxy.
        /// </summary>
        public bool BypassOnLocal { get; set; }

        /// <summary>
        /// Gets or sets a list of addresses that should bypass the proxy.
        /// Each entry can be an IP address, hostname, or domain.
        /// </summary>
        public List<string> BypassList { get; set; }

        /// <summary>
        /// Gets or sets the timeout for proxy connection attempts in seconds.
        /// </summary>
        public int ConnectionTimeoutSeconds { get; set; }

        /// <summary>
        /// Creates a new instance of ProxySettings with default values.
        /// </summary>
        public ProxySettings()
        {
            // Initialize with default values
            BypassList = [];
            SetDefaults();
        }

        /// <summary>
        /// Creates a default proxy configuration suitable for most environments.
        /// </summary>
        public static ProxySettings CreateDefault()
        {
            return new ProxySettings
            {
                Enabled = false,
                Port = 8080,
                BypassOnLocal = true,
                ConnectionTimeoutSeconds = 30,
                BypassList =
            [
                "localhost",
                "127.0.0.1",
                "169.254.1.1"
            ]
            };
        }

        /// <summary>
        /// Resets all settings to their default values.
        /// </summary>
        public void SetDefaults()
        {
            Enabled = false;
            Address = string.Empty;
            Port = 8080;
            RequiresAuthentication = false;
            Username = string.Empty;
            Password = string.Empty;
            BypassOnLocal = true;
            ConnectionTimeoutSeconds = 30;
            BypassList.Clear();
            BypassList.AddRange(new[] { "localhost", "127.0.0.1", "169.254.1.1" });
        }

        /// <summary>
        /// Validates the proxy settings configuration.
        /// </summary>
        /// <returns>A collection of validation error messages.</returns>
        internal IEnumerable<string> Validate()
        {
            // Only validate other settings if proxy is enabled
            if (!Enabled)
            {
                yield break;
            }

            // Validate proxy address
            if (string.IsNullOrWhiteSpace(Address))
            {
                yield return "Proxy address cannot be empty when proxy is enabled";
            }
            else if (!IsValidHostnameOrIp(Address))
            {
                yield return "Invalid proxy address format";
            }

            // Validate port number
            if (Port <= 0 || Port > 65535)
            {
                yield return "Proxy port must be between 1 and 65535";
            }

            // Validate authentication settings
            if (RequiresAuthentication)
            {
                if (string.IsNullOrWhiteSpace(Username))
                {
                    yield return "Proxy username is required when authentication is enabled";
                }

                if (string.IsNullOrWhiteSpace(Password))
                {
                    yield return "Proxy password is required when authentication is enabled";
                }
            }

            // Validate timeout
            if (ConnectionTimeoutSeconds <= 0)
            {
                yield return "Proxy connection timeout must be greater than 0 seconds";
            }

            // Validate bypass list entries
            if (BypassList != null)
            {
                foreach (var entry in BypassList)
                {
                    if (!IsValidBypassEntry(entry))
                    {
                        yield return $"Invalid bypass list entry: {entry}";
                    }
                }
            }
        }

        /// <summary>
        /// Creates a System.Net.WebProxy instance from these settings.
        /// </summary>
        /// <returns>A configured WebProxy instance if enabled, null otherwise.</returns>
        public WebProxy? CreateWebProxy()
        {
            if (!Enabled || string.IsNullOrWhiteSpace(Address))
            {
                return null;
            }

            var proxyAddress = Port != 80 ? $"{Address}:{Port}" : Address;
            var proxy = new WebProxy
            {
                Address = new Uri($"http://{proxyAddress}"),
                BypassProxyOnLocal = BypassOnLocal,
                UseDefaultCredentials = !RequiresAuthentication
            };

            // Add bypass list entries
            if (BypassList?.Count > 0)
            {
                proxy.BypassList = BypassList.ToArray();
            }

            // Configure proxy authentication if required
            if (RequiresAuthentication)
            {
                proxy.Credentials = new NetworkCredential(Username, Password);
            }

            return proxy;
        }

        /// <summary>
        /// Validates a hostname or IP address format.
        /// </summary>
        private bool IsValidHostnameOrIp(string address)
        {
            // Check if it's a valid IP address
            if (IPAddress.TryParse(address, out _))
            {
                return true;
            }

            // Check if it's a valid hostname
            // This is a simplified validation - production code might want more thorough validation
            return address.Length <= 255 &&
                   !address.StartsWith("-") &&
                   !address.EndsWith("-") &&
                   address.All(c => char.IsLetterOrDigit(c) || c == '.' || c == '-');
        }

        /// <summary>
        /// Validates a proxy bypass list entry.
        /// </summary>
        private bool IsValidBypassEntry(string entry)
        {
            if (string.IsNullOrWhiteSpace(entry))
            {
                return false;
            }

            // Allow IP addresses
            if (IPAddress.TryParse(entry, out _))
            {
                return true;
            }

            // Allow wildcards and domain names
            // This is a simplified validation - production code might want more thorough validation
            return entry.All(c => char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '*');
        }

        /// <summary>
        /// Creates a deep copy of the proxy settings.
        /// </summary>
        public ProxySettings Clone()
        {
            return new ProxySettings
            {
                Enabled = this.Enabled,
                Address = this.Address,
                Port = this.Port,
                RequiresAuthentication = this.RequiresAuthentication,
                Username = this.Username,
                Password = this.Password,
                BypassOnLocal = this.BypassOnLocal,
                ConnectionTimeoutSeconds = this.ConnectionTimeoutSeconds,
                BypassList = this.BypassList?.ToList() ?? []
            };
        }
    }

    /// <summary>
    /// GPO-specific configuration settings
    /// </summary>
    public class GpoSettings
    {
        /// <summary>
        /// Default pulse duration in milliseconds when not specified
        /// </summary>
        public int DefaultPulseDurationMs { get; set; } = 1000;

        /// <summary>
        /// Maximum allowed pulse duration in milliseconds
        /// </summary>
        public int MaxPulseDurationMs { get; set; } = 60000;

        /// <summary>
        /// Whether to allow network control mode
        /// </summary>
        public bool AllowNetworkControl { get; set; } = true;

        /// <summary>
        /// Default GPO configurations to apply on startup
        /// </summary>
        public List<ExtendedGpoConfiguration> DefaultConfigurations { get; set; } = new();

        /// <summary>
        /// Creates default GPO settings
        /// </summary>
        public static GpoSettings CreateDefault()
        {
            return new GpoSettings
            {
                DefaultPulseDurationMs = 1000,
                MaxPulseDurationMs = 60000,
                AllowNetworkControl = true,
                DefaultConfigurations = new List<ExtendedGpoConfiguration>
            {
                // Default all GPOs to reader control
                new ExtendedGpoConfiguration { Gpo = 1, Control = GpoControlMode.Reader },
                new ExtendedGpoConfiguration { Gpo = 2, Control = GpoControlMode.Reader },
                new ExtendedGpoConfiguration { Gpo = 3, Control = GpoControlMode.Reader },
                new ExtendedGpoConfiguration { Gpo = 4, Control = GpoControlMode.Reader }
            }
            };
        }

        /// <summary>
        /// Validates the GPO settings
        /// </summary>
        internal IEnumerable<string> Validate()
        {
            if (DefaultPulseDurationMs <= 0)
                yield return "Default pulse duration must be greater than 0";

            if (MaxPulseDurationMs < DefaultPulseDurationMs)
                yield return "Maximum pulse duration must be greater than or equal to default duration";

            if (DefaultConfigurations?.Any() == true)
            {
                var request = new ExtendedGpoConfigurationRequest
                {
                    GpoConfigurations = DefaultConfigurations
                };

                var validationResult = request.Validate();
                foreach (var error in validationResult.Errors)
                {
                    yield return $"Default configuration error: {error}";
                }
            }
        }
    }
}
