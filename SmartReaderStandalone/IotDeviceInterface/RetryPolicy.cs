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
