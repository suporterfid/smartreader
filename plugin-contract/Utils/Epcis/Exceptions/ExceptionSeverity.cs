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
namespace SmartReader.Infrastructure.Utils.Epcis.Exceptions;

public class ExceptionSeverity : Enumeration
{
    public static readonly ExceptionSeverity Error = new(4, "ERROR");
    public static readonly ExceptionSeverity Severe = new(5, "SEVERE");

    public ExceptionSeverity()
    {
    }

    public ExceptionSeverity(short id, string displayName) : base(id, displayName)
    {
    }
}