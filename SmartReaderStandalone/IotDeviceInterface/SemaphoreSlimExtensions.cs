#region copyright
//****************************************************************************************************
// Copyright ©2025 Impinj, Inc.All rights reserved.              
//                                    
// You may use and modify this code under the terms of the Impinj Software Tools License & Disclaimer. 
// Visit https://support.impinj.com/hc/en-us/articles/360000468370-Software-Tools-License-Disclaimer   
// for full license details, or contact Impinj, Inc.at support@impinj.com for a copy of the license.   
//
//****************************************************************************************************
#endregion
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
                    _ = _semaphore.Release();
                    _disposed = true;
                }
            }
        }
    }
}
