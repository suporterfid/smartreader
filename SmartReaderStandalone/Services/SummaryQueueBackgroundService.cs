namespace SmartReaderStandalone.Services
{
    public class SummaryQueueBackgroundService : BackgroundService, IServiceProviderIsService, ISummaryQueueBackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IConfiguration _configuration;

        //private int _activeConnections;
        private static bool _isRunning;

        public SummaryQueueBackgroundService(IServiceProvider serviceProvider, IConfiguration configuration)
        {
            _serviceProvider = serviceProvider;
            _configuration = configuration;
            //_activeConnections = 0;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                if (_isRunning)
                {
                    if (SharedSummaryQueue.Queue.Count > 0)
                    {
                        SharedSummaryQueue.DataAvailable = true;
                    }
                    else
                    {
                        SharedSummaryQueue.DataAvailable = false;
                    }

                }
                else
                {
                    SharedSummaryQueue.DataAvailable = false;
                }

                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
        }

        public bool HasDataAvailable()
        {
            return SharedSummaryQueue.DataAvailable;
        }

        public void AddData(string jsonData)
        {
            if (_isRunning)
            {
                // Enqueue the JSON data item
                SharedSummaryQueue.Queue.Enqueue(jsonData);
                if (SharedSummaryQueue.Queue.Count > 0)
                {
                    SharedSummaryQueue.DataAvailable = true;
                }
                else
                {
                    SharedSummaryQueue.DataAvailable = false;
                }

            }
            else
            {
                SharedSummaryQueue.DataAvailable = false;
            }
        }

        public string GetData()
        {
            string jsonData = null;

            if (SharedSummaryQueue.Queue.Count > 0)
            {
                // Enqueue the JSON data item
                SharedSummaryQueue.Queue.TryDequeue(out jsonData);
            }
            return jsonData;
        }

        public void StartQueue()
        {
            _isRunning = true;
            SharedSummaryQueue.Queue.Clear();
        }

        public void StopQueue()
        {
            _isRunning = false;
            SharedSummaryQueue.Queue.Clear();
        }

        //public void IncrementConnections()
        //{
        //    Interlocked.Increment(ref _activeConnections);
        //}

        //public void DecrementConnections()
        //{
        //    if (_activeConnections > 1)
        //    {
        //        Interlocked.Decrement(ref _activeConnections);
        //    }

        //}

        public bool IsService(Type serviceType)
        {
            return true;
        }
    }
}
