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
using SmartReader.Infrastructure.Utils.Epcis;

namespace SmartReaderStandalone.ViewModel.Read.Epcis.Enums;

public class EpcType : Enumeration
{
    public static readonly EpcType List = new(0, "list");
    public static readonly EpcType ParentId = new(1, "parentID");
    public static readonly EpcType InputQuantity = new(2, "inputQuantity");
    public static readonly EpcType OutputQuantity = new(3, "outputQuantity");
    public static readonly EpcType InputEpc = new(4, "inputEPC");
    public static readonly EpcType OutputEpc = new(5, "outputEPC");
    public static readonly EpcType Quantity = new(6, "quantity");
    public static readonly EpcType ChildEpc = new(7, "childEPC");
    public static readonly EpcType ChildQuantity = new(8, "childQuantity");

    public EpcType()
    {
    }

    public EpcType(short id, string displayName) : base(id, displayName)
    {
    }
}