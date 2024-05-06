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

public class EpcisEvent
{
    public DateTime CaptureTime { get; set; }
    public DateTime EventTime { get; set; }
    public TimeZoneOffset EventTimeZoneOffset { get; set; } = TimeZoneOffset.Default;
    public EventType? Type { get; set; }
    public EventAction? Action { get; set; }
    public string? EventId { get; set; }
    public string? ReadPoint { get; set; }
    public string? BusinessLocation { get; set; }
    public string? BusinessStep { get; set; }
    public string? Disposition { get; set; }
    public string? TransformationId { get; set; }
    public DateTime? CorrectiveDeclarationTime { get; set; }
    public string? CorrectiveReason { get; set; }
    public List<string> CorrectiveEventIds { get; set; } = new();
    public List<Epc> Epcs { get; set; } = new();
    public List<BusinessTransaction> BusinessTransactions { get; set; } = new();
    public List<SourceDestination> SourceDestinationList { get; set; } = new();
    public List<CustomField> CustomFields { get; set; } = new();
}