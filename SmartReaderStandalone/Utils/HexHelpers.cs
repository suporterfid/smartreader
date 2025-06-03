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
namespace SmartReader.Infrastructure.Utils;

public static class HexHelpers
{
    public static IEnumerable<ushort> HexToUshortEnumerable(string hex)
    {
        var num = hex.Length % 4;
        if (num != 0)
            hex = hex.PadLeft(4 - num + hex.Length, '0');
        var numberChars = hex.Length;
        for (var i = 0; i < numberChars; i += 4)
            yield return Convert.ToUInt16(hex.Substring(i, 4), 16);
    }
}