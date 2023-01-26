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