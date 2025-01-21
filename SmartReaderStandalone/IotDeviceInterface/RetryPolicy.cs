using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Polly;
using Serilog;


namespace SmartReaderStandalone.IotDeviceInterface
{
    public static class RetryPolicy
    {
        private static readonly IAsyncPolicy RetryablePolicy = Policy
            .Handle<DbUpdateException>()
            .Or<SqlException>()
            .Or<DbUpdateConcurrencyException>()
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: retryAttempt =>
                    TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)), // exponential backoff
                onRetry: (exception, timeSpan, retryCount, context) =>
                {
                    // Inject ILogger via constructor if this is in a class
                    // For now, using static logger for demonstration
                    Log.Error(
                        exception,
                        "Error during database operation (Attempt {RetryCount}). Waiting {TimeSpan} before retrying.",
                        retryCount,
                        timeSpan);
                });

        public static Task ExecuteAsync(Func<Task> action)
        {
            return RetryablePolicy.ExecuteAsync(action);
        }

        public static Task<T> ExecuteAsync<T>(Func<Task<T>> action)
        {
            return RetryablePolicy.ExecuteAsync(action);
        }
    }
}
