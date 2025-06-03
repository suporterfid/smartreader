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
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SmartReaderStandalone.Entities;

public class InventoryStatus
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public string? Id { get; set; }

    public string? CurrentStatus { get; set; }

    public string? CycleId { get; set; }

    public int? TotalCount { get; set; }

    public DateTimeOffset? StartedOn { get; set; }

    public DateTimeOffset? StoppedOn { get; set; }
}