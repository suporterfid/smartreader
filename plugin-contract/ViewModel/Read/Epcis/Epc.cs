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
using SmartReaderStandalone.ViewModel.Read.Epcis.Enums;

namespace SmartReaderStandalone.ViewModel.Read.Epcis;

public class Epc
{
    public string? Id { get; set; }
    public EpcType? Type { get; set; }
    public bool? IsQuantity { get; set; }
    public float? Quantity { get; set; }
    public string? UnitOfMeasure { get; set; }
}