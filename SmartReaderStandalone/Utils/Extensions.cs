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