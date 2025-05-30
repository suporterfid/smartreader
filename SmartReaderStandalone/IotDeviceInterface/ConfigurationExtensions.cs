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

            var json = File.ReadAllText(filePath);

            // Configurar JsonSerializerOptions para lidar com construtores parametrizados
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                // Permitir campos adicionais não mapeados
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip
            };

            var config = JsonSerializer.Deserialize<ReaderConfiguration>(json, options);

            if (config == null)
                throw new InvalidOperationException("Failed to deserialize configuration");

            // Validate the loaded configuration
            var validationResult = config.Validate();
            if (!validationResult.IsValid)
            {
                throw new InvalidOperationException(
                    $"Invalid configuration: {string.Join(", ", validationResult.Errors)}");
            }

            return config;
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
            section.Bind(config);

            // Validate the loaded configuration
            var validationResult = config.Validate();
            if (!validationResult.IsValid)
            {
                throw new InvalidOperationException(
                    $"Invalid configuration: {string.Join(", ", validationResult.Errors)}");
            }

            return config;
        }

        /// <summary>
        /// Saves reader configuration to a JSON file.
        /// </summary>
        public static void SaveToJson(this ReaderConfiguration config, string filePath)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var json = JsonSerializer.Serialize(config, options);
            File.WriteAllText(filePath, json);
        }
    }
}