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
using SmartReader.Infrastructure.ViewModel;
using System.Collections.Concurrent;

public class ValidationService
{
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, int> _currentSkus;
    private readonly ConcurrentBag<string> _currentSkuReadEpcs;
    private readonly SemaphoreSlim _validationLock = new(1, 1);
    private volatile StandaloneConfigDTO? _config;

    public ValidationService(
        ILogger logger,
        ConcurrentDictionary<string, int> currentSkus,
        ConcurrentBag<string> currentSkuReadEpcs)
    {
        _logger = logger;
        _currentSkus = currentSkus;
        _currentSkuReadEpcs = currentSkuReadEpcs;
    }

    public void UpdateConfig(StandaloneConfigDTO config)
    {
        _config = config;
    }

    public bool IsValidationEnabled()
    {
        return _config != null &&
               string.Equals("1", _config.enableValidation, StringComparison.OrdinalIgnoreCase);
    }

    public async Task<bool> ValidateAndTrackEpcAsync(string epc, string tagDataKey)
    {
        if (!IsValidationEnabled())
            return true;

        try
        {
            await _validationLock.WaitAsync();
            try
            {
                if (!_currentSkuReadEpcs.Contains(epc))
                {
                    _currentSkuReadEpcs.Add(epc);
                    _ = _currentSkus.AddOrUpdate(
                        tagDataKey,
                        1,
                        (key, oldValue) => oldValue + 1
                    );
                    return true;
                }
                return false;
            }
            finally
            {
                _ = _validationLock.Release();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error validating EPC {epc}");
            return false;
        }
    }

    public Dictionary<string, int> GetCurrentValidationStatus()
    {
        if (!IsValidationEnabled())
            return [];

        try
        {
            return _currentSkus.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting validation status");
            return [];
        }
    }

    public async Task<ValidationResult> ValidateExpectedItemsAsync(
        Dictionary<string, int> expectedItems)
    {
        if (!IsValidationEnabled())
            return new ValidationResult { IsValid = true };

        try
        {
            await _validationLock.WaitAsync();
            try
            {
                var result = new ValidationResult
                {
                    IsValid = true,
                    Details = []
                };

                if (!expectedItems.Any())
                {
                    result.IsValid = false;
                    result.Details.Add("No expected items provided");
                    return result;
                }

                foreach (var expected in expectedItems)
                {
                    if (!_currentSkus.TryGetValue(expected.Key, out int actualCount))
                    {
                        result.IsValid = false;
                        result.Details.Add($"SKU {expected.Key} not found");
                        continue;
                    }

                    if (actualCount != expected.Value)
                    {
                        result.IsValid = false;
                        result.Details.Add($"SKU {expected.Key}: expected {expected.Value}, found {actualCount}");
                    }
                }

                // Check for unexpected additional SKUs
                var unexpectedSkus = _currentSkus.Keys.Except(expectedItems.Keys);
                foreach (var sku in unexpectedSkus)
                {
                    result.IsValid = false;
                    result.Details.Add($"Unexpected SKU found: {sku}");
                }

                return result;
            }
            finally
            {
                _ = _validationLock.Release();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating expected items");
            return new ValidationResult
            {
                IsValid = false,
                Details = ["Internal validation error"]
            };
        }
    }

    public void ClearValidation()
    {
        try
        {
            _currentSkus.Clear();
            while (_currentSkuReadEpcs.TryTake(out _)) { }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing validation state");
        }
    }
}

// 2. Supporting class for validation results
public class ValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Details { get; set; } = [];
}