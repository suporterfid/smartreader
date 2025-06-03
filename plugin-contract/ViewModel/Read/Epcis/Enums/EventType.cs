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

public class EventType : Enumeration
{
    public static readonly EventType Object = new(0, "ObjectEvent");
    public static readonly EventType Aggregation = new(1, "AggregationEvent");
    public static readonly EventType Transaction = new(2, "TransactionEvent");
    public static readonly EventType Transformation = new(3, "TransformationEvent");
    public static readonly EventType Quantity = new(4, "QuantityEvent");

    public EventType()
    {
    }

    public EventType(short id, string displayName) : base(id, displayName)
    {
    }
}