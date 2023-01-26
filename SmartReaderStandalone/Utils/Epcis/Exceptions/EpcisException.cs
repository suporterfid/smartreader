﻿namespace SmartReader.Infrastructure.Utils.Epcis.Exceptions;

public class EpcisException : Exception
{
    public static readonly EpcisException Default = new(ExceptionType.ImplementationException, string.Empty,
        ExceptionSeverity.Error);

    public EpcisException(ExceptionType exceptionType, string message, ExceptionSeverity severity = null) :
        base(message)
    {
        ExceptionType = exceptionType;
        Severity = severity ?? ExceptionSeverity.Error;
    }

    public ExceptionType ExceptionType { get; }
    public ExceptionSeverity Severity { get; }
}