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
namespace SmartReaderStandalone.ViewModel.Read.Epcis;

public class TimeZoneOffset
{
    public static TimeZoneOffset Default = new();

    public string Representation
    {
        get => ComputeRepresentation(Value);
        set => Value = ComputeValue(value);
    }

    public short Value { get; set; }

    private static string ComputeRepresentation(int value)
    {
        var sign = value >= 0 ? "+" : "-";
        var hours = (Math.Abs(value) / 60).ToString("D2");
        var minutes = Math.Abs(value % 60).ToString("D2");

        return string.Format("{0}{1}:{2}", sign, hours, minutes);
    }

    private static short ComputeValue(string value)
    {
        var sign = value[0] == '-' ? -1 : +1;
        var parts = value.Split(':');

        return (short)(sign * ((Math.Abs(int.Parse(parts[0])) * 60) + int.Parse(parts[1])));
    }
}