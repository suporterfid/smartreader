using System.Collections.Concurrent;

namespace SmartReaderStandalone.Services
{
    public static class SharedSummaryQueue
    {
        public static ConcurrentQueue<string> Queue { get; } = new ConcurrentQueue<string>();
        public static bool DataAvailable { get; set; }
    }
}
