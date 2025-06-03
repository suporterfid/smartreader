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
using SmartReaderStandalone.ViewModel.Read;
using SmartReaderStandalone.ViewModel.Read.Sku.Summary;
using System.Collections.Concurrent;

namespace SmartReaderStandalone.Plugins
{
    /// <summary>
    /// This interface is an example of one way to define the interactions between the host and plugins.
    /// There is nothing special about the name "IPlugin"; it's used here to illustrate a concept.
    /// Look at https://github.com/natemcmaster/DotNetCorePlugins/tree/main/samples for additional examples
    /// of ways you could define the interaction between host and plugins.
    /// </summary>
    public interface IPlugin
    {
        string GetName();

        string GetDescription();

        void SetDeviceId(string Id);

        Task<bool> Init();

        string ProcessCommand(string command);

        void AddTagDataToQueue(TagRead tagRead);

        bool IsProcessingTagData();

        bool IsProcessingExternalValidation();

        bool ExternalApiLogin();

        ConcurrentDictionary<string, int> ExternalApiSearch(string barcode, Dictionary<string, SkuSummary> skuSummaryList, string[] epcs);

        bool ExternalApiPublish(string barcode, Dictionary<string, SkuSummary> skuSummaryList, string[] epcs);

        bool ValidateCurrentProductContent(ConcurrentDictionary<string, int> expectedContent, ConcurrentDictionary<string, int> currentContent);
    }
}
