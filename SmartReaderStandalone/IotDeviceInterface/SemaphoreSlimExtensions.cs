namespace SmartReaderStandalone.IotDeviceInterface
{
    public static class SemaphoreSlimExtensions
    {
        public static async Task<IDisposable> WaitAsyncWithTimeout(
            this SemaphoreSlim semaphore,
            TimeSpan timeout)
        {
            if (!await semaphore.WaitAsync(timeout))
            {
                throw new TimeoutException("Failed to acquire lock within the specified timeout.");
            }

            return new SemaphoreSlimReleaser(semaphore);
        }

        private class SemaphoreSlimReleaser : IDisposable
        {
            private readonly SemaphoreSlim _semaphore;
            private bool _disposed;

            public SemaphoreSlimReleaser(SemaphoreSlim semaphore)
            {
                _semaphore = semaphore;
            }

            public void Dispose()
            {
                if (!_disposed)
                {
                    _semaphore.Release();
                    _disposed = true;
                }
            }
        }
    }
}
