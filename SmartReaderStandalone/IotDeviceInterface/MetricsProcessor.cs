using System.Threading.Channels;

namespace SmartReaderStandalone.IotDeviceInterface
{
    /// <summary>
    /// Processes health metrics in a background task.
    /// </summary>
    public class MetricsProcessor
    {
        private readonly Channel<HealthMetric> _metricsChannel;
        private readonly ILogger<MetricsProcessor> _logger;
        private readonly CancellationTokenSource _cancellationSource;
        private Task _processingTask;

        public MetricsProcessor(ILogger<MetricsProcessor> logger)
        {
            _logger = logger;
            _metricsChannel = Channel.CreateUnbounded<HealthMetric>(
                new UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = false
                });
            _cancellationSource = new CancellationTokenSource();
        }

        public void Start()
        {
            _processingTask = Task.Run(ProcessMetricsAsync);
        }

        public async Task StopAsync()
        {
            _cancellationSource.Cancel();
            await _processingTask;
        }

        public async Task SubmitMetricAsync(HealthMetric metric)
        {
            await _metricsChannel.Writer.WriteAsync(metric);
        }

        private async Task ProcessMetricsAsync()
        {
            try
            {
                await foreach (var metric in _metricsChannel.Reader.ReadAllAsync(_cancellationSource.Token))
                {
                    try
                    {
                        ProcessMetric(metric);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing metric: {MetricName}", metric.Name);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when stopping
            }
        }

        private void ProcessMetric(HealthMetric metric)
        {
            // Log the metric
            _logger.LogInformation(
                "Metric: {Type} {Name} = {Value}{Unit} ({Severity})",
                metric.Type,
                metric.Name,
                metric.Value,
                metric.Unit,
                metric.Severity);

            // Handle different metric types
            switch (metric.Type)
            {
                case MetricType.Connection when !HealthMetric.Analysis.IsHealthy(metric):
                    HandleConnectionIssue(metric);
                    break;

                case MetricType.Memory when metric.Severity >= MetricSeverity.Warning:
                    HandleMemoryWarning(metric);
                    break;

                    // Add other metric type handling as needed
            }
        }

        private void HandleConnectionIssue(HealthMetric metric)
        {
            // Implement connection issue handling
            _logger.LogWarning("Connection issue detected: {Description}", metric.Description);
        }

        private void HandleMemoryWarning(HealthMetric metric)
        {
            // Implement memory warning handling
            _logger.LogWarning("Memory warning: {Description}", metric.Description);
        }
    }
}
