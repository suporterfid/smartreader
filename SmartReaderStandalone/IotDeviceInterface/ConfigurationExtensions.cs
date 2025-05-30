using System.Text.Json;

namespace SmartReaderStandalone.IotDeviceInterface
{
    /// <summary>
    /// Extension methods for configuration handling.
    /// </summary>
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
            var config = JsonSerializer.Deserialize<ReaderConfiguration>(json);

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
        /// Saves the configuration to a JSON file.
        /// </summary>
        public static void SaveToJson(this ReaderConfiguration config, string filePath)
        {
            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(filePath, json);
        }
    }
}
