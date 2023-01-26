namespace SmartReader.Infrastructure.Utils;

public static class UtilConverter
{
    public static string ConvertBoolToNumericString(bool value)
    {
        var i = 0;
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
        var returnValue = 0;
        int.TryParse(value, out returnValue);
        return returnValue;
    }

    public static long ConvertfromNumericStringToLong(string value)
    {
        long returnValue = 0;
        long.TryParse(value, out returnValue);
        return returnValue;
    }
}