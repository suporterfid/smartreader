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
using System.Runtime.Serialization;

namespace SmartReader.IotDeviceInterface;

[Serializable]
public class IotDeviceInterfaceException : Exception
{
    public IotDeviceInterfaceException()
    {
    }

    public IotDeviceInterfaceException(string message)
        : base(message)
    {
    }

    public IotDeviceInterfaceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    protected IotDeviceInterfaceException(SerializationInfo info, StreamingContext context)
        : base(info, context)
    {
    }
}