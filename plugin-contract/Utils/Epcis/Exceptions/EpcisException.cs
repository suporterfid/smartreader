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
namespace SmartReader.Infrastructure.Utils.Epcis.Exceptions;

public class EpcisException : Exception
{
    public static readonly EpcisException Default = new(ExceptionType.ImplementationException, string.Empty,
        ExceptionSeverity.Error);

    public EpcisException(ExceptionType exceptionType, string message, ExceptionSeverity? severity = null) :
        base(message)
    {
        ExceptionType = exceptionType;
        Severity = severity ?? ExceptionSeverity.Error;
    }

    public ExceptionType ExceptionType { get; }
    public ExceptionSeverity Severity { get; }
}