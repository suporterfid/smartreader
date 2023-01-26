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