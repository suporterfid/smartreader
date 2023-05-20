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
using SmartReader.Infrastructure.Utils.Epcis;

namespace SmartReaderStandalone.ViewModel.Read.Epcis.Enums;

public class SourceDestinationType : Enumeration
{
    public static readonly SourceDestinationType Source = new(0, "source");
    public static readonly SourceDestinationType Destination = new(1, "destination");

    public SourceDestinationType()
    {
    }

    public SourceDestinationType(short id, string displayName) : base(id, displayName)
    {
    }
}