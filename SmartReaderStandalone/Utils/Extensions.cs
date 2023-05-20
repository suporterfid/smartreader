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
using System.Runtime.Serialization;

namespace SmartReader.Infrastructure.Utils;

public static class Extensions
{
    public static string GetEnumValue(this Enum value)
    {
        var fi = value.GetType().GetField(value.ToString());

        var attributes =
            (EnumMemberAttribute[]) fi.GetCustomAttributes(typeof(EnumMemberAttribute), false);

        if (attributes != null && attributes.Length > 0)
            return attributes[0].Value;
        return value.ToString();
    }
}