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
namespace SmartReaderStandalone.Helpers
{
    public static class DictionaryExtensions
    {
        public static Dictionary<string, object?> AlsoIf(this Dictionary<string, object?> dict, bool condition, Action<Dictionary<string, object?>> action)
        {
            if (condition)
                action(dict);

            return dict;
        }
    }
}
