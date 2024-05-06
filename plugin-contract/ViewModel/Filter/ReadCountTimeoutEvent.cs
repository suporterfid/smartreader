#region copyright
//****************************************************************************************************
// Copyright ©2023 Impinj, Inc.All rights reserved.              
//                                    
// You may use and modify this code under the terms of the Impinj Software Tools License & Disclaimer. 
// Visit https://support.impinj.com/hc/en-us/articles/360000468370-Software-Tools-License-Disclaimer   
// for full license details, or contact Impinj, Inc.at support@impinj.com for a copy of the license.   
//
//****************************************************************************************************
#endregion
namespace SmartReaderStandalone.ViewModel.Filter;

public class ReadCountTimeoutEvent
{
    public string? Epc { get; set; }

    public int Antenna { get; set; }

    public int Timeout { get; set; }

    public int Count { get; set; }

    public long EventTimestamp { get; set; }
}