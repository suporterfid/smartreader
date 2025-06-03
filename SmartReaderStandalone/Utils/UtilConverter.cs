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

public static class UtilConverter
{
    public static string ConvertBoolToNumericString(bool value)
    {
        int i;
        try
        {
            i = Convert.ToInt32(value);
        }
        catch (Exception)
        {
            i = 0;
        }

        return i.ToString();
    }

    public static bool ConvertfromNumericStringToBool(string value)
    {
        return value != "0";
    }

    public static int ConvertfromNumericStringToInt(string value)
    {
        int returnValue;
        _ = int.TryParse(value, out returnValue);
        return returnValue;
    }

    public static long ConvertfromNumericStringToLong(string value)
    {
        long returnValue;
        _ = long.TryParse(value, out returnValue);
        return returnValue;
    }
}