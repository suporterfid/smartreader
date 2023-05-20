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
using System.ComponentModel.DataAnnotations;

namespace SmartReader.Infrastructure.Entities;

public class SmartReaderAntennaConfig
{
    [Key] public int Id { get; set; }

    [Required] public int AntennaPort { get; set; }

    public bool AntennaState { get; set; }

    public string? AntennaZone { get; set; }

    public int TransmitPower { get; set; }

    public int ReceiveSensitivity { get; set; }

    public int ReaderMode { get; set; }

    public int Session { get; set; }

    public int TagPopulation { get; set; }

    public int SmartReaderConfigId { get; set; }

    public SmartReaderConfig SmartReaderConfig { get; set; }
}