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

public class ExceptionType : Enumeration
{
    public static readonly ExceptionType
        SubscribeNotPermittedException = new(0, nameof(SubscribeNotPermittedException));

    public static readonly ExceptionType ImplementationException = new(1, nameof(ImplementationException));
    public static readonly ExceptionType NoSuchNameException = new(2, nameof(NoSuchNameException));
    public static readonly ExceptionType QueryTooLargeException = new(3, nameof(QueryTooLargeException));
    public static readonly ExceptionType QueryParameterException = new(4, nameof(QueryParameterException));
    public static readonly ExceptionType ValidationException = new(5, nameof(ValidationException));
    public static readonly ExceptionType SubscriptionControlsException = new(6, nameof(SubscriptionControlsException));
    public static readonly ExceptionType NoSuchSubscriptionException = new(7, nameof(NoSuchSubscriptionException));

    public static readonly ExceptionType
        DuplicateSubscriptionException = new(8, nameof(DuplicateSubscriptionException));

    public static readonly ExceptionType QueryTooComplexException = new(9, nameof(QueryTooComplexException));
    public static readonly ExceptionType InvalidURIException = new(10, nameof(InvalidURIException));

    public ExceptionType()
    {
    }

    public ExceptionType(short id, string displayName) : base(id, displayName)
    {
    }
}