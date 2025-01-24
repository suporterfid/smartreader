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
using Newtonsoft.Json.Linq;
using SmartReader.Infrastructure.ViewModel;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using static Org.BouncyCastle.Math.EC.ECCurve;

namespace SmartReaderJobs.Utils;

public class Utils
{
    #region string to IP Address

    /// <summary>
    ///     Utility that will convert a string that is a valid IP Address or
    ///     hostname into an IPAddress object.
    /// </summary>
    /// <param name="ipAddressOrHostname">
    ///     string containing either an IP Address or a hostname
    /// </param>
    /// <param name="returnIPAddress">An IP Address Object</param>
    /// <exception cref="ArgumentNullException">
    ///     ipAddressOrHostname is a null reference.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     The length of ipAddressOrHostname is greater than 126 characters.
    /// </exception>
    /// <exception cref="SocketException">
    ///     An error is encountered when resolving ipAddressOrHostname via DNS.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     ipAddressOrHostname is an invalid hostname.
    /// </exception>
    public static void stringToIpAddress(string ipAddressOrHostname, out IPAddress returnIPAddress)
    {
        returnIPAddress = null;
        // Check to see whether the hostname is an IP address by
        // attempting to convert the string to an IPAddress object.
        // If this fails, assume that it is a hostname and do a DNS
        // query to obtain an appropriate IPAddress object.
        try
        {
            returnIPAddress = IPAddress.Parse(ipAddressOrHostname);
        }
        catch (Exception)
        {
            try
            {
                // Do a DNS query for the hostname
                var ipAddressArr = Dns.GetHostEntry(ipAddressOrHostname).AddressList;

                foreach (var ip in ipAddressArr)
                    // Select the first IPv4 address returned
                    if (ip.AddressFamily.ToString() == "InterNetwork")
                    {
                        returnIPAddress = ip;
                        break;
                    }
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }
    }

    #endregion

    #region GetDateTime

    public static DateTime GetDateTime(ulong usecondsSinceEpoch)
    {
        var epoc = new DateTime(1970, 1, 1, 0, 0, 0);
        var ticks = (long)usecondsSinceEpoch * 10 + epoc.Ticks;
        return new DateTime(ticks, DateTimeKind.Utc);
    }

    #endregion

    public static long CSharpMillisToJavaLong(DateTime dateTime)
    {
        var Jan1st1970 = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var diff = (long)(dateTime.ToUniversalTime() - Jan1st1970.ToUniversalTime()).TotalMilliseconds;
        //return diff / 1000;
        return diff;
    }

    public static long CSharpMillisToJavaLongMicroseconds(DateTime dateTime)
    {
        var Jan1st1970 = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var diff = (long)(dateTime.ToUniversalTime() - Jan1st1970.ToUniversalTime()).TotalMilliseconds;
        return diff * 1000;
    }


    public static List<int> GetDefaultTxTable(string readerModel, bool isPoEPlus, string operatingRegion)
    {
        var txTable = new List<int>();
        int[] int_arr =
        {
            1000, 1025, 1050, 1075, 1100, 1125, 1150, 1175, 1200, 1225, 1250, 1275, 1300, 1325, 1350, 1375, 1400, 1425,
            1450, 1475, 1500, 1525, 1550, 1575, 1600, 1625, 1650, 1675, 1700, 1725, 1750, 1775, 1800, 1825, 1850, 1875,
            1900, 1925, 1950, 1975, 2000
        };
        txTable.AddRange(int_arr);
        //int_arr =
        //{
        //    1000, 1025, 1050, 1075, 1100, 1125, 1150, 1175, 1200, 1225, 1250, 1275, 1300, 1325, 1350, 1375, 1400, 1425,
        //    1450, 1475, 1500, 1525, 1550, 1575, 1600, 1625, 1650, 1675, 1700, 1725, 1750, 1775, 1800, 1825, 1850, 1875,
        //    1900, 1925, 1950, 1975, 2000, 2025, 2050, 2075, 2100, 2125, 2150, 2175, 2200, 2225, 2250, 2275, 2300, 2325,
        //    2350, 2375, 2400, 2425, 2450, 2475, 2500, 2525, 2550, 2575, 2600, 2625, 2650, 2675, 2700, 2725, 2750, 2775,
        //    2800, 2825, 2850, 2875, 2900, 2925, 2950, 2975, 3000, 3025, 3050, 3075, 3100, 3125, 3150, 3175, 3200, 3225,
        //    3250, 3275, 3300
        //};

        if (string.IsNullOrEmpty(readerModel) || "R700".Equals(readerModel))
        {

            for (int i = 2025; i < 3025; i = i + 25)
            {
                txTable.Add(i);
            }
            if (isPoEPlus)
            {
                if (!operatingRegion.Contains("India") && !operatingRegion.Contains("865"))
                {
                    for (int i = 2025; i < 3325; i = i + 25)
                    {
                        txTable.Add(i);
                    }
                }
                else
                {
                    for (int i = 2025; i < 3175; i = i + 25)
                    {
                        txTable.Add(i);
                    }
                }

            }
        }
        else
        {
            if (isPoEPlus && !operatingRegion.Contains("India") && !operatingRegion.Contains("865"))
            {
                for (int i = 2025; i < 3325; i = i + 25)
                {
                    txTable.Add(i);
                }
            }
        }

        return txTable;
    }

    public static List<int> GetDefaultRxTable()
    {
        var list = new List<int>();
        int[] int_arr =
        {
            -30, -31, -32, -33, -34, -35, -36, -37, -38, -39, -40, -41, -42, -43, -44, -45, -46, -47, -48, -49, -50,
            -51, -51, -52, -53, -54, -55, -56, -57, -58, -59, -60, -61, -62, -63, -64, -65, -66, -67, -68, -69, -70,
            -71, -72, -73, -74, -75, -76, -77, -78, -79, -80, -92
        };

        list.AddRange(int_arr);

        return list;
    }

    public static List<int> GetDefaultRfModeTable()
    {
        var list = new List<int>();
        int[] int_arr =
        {
            0, 1, 2, 3, 4, 5, 1002, 1003, 1004, 100, 120, 121, 122, 140, 141, 142, 180, 181, 184, 185, 1110, 1111, 1112
        };

        list.AddRange(int_arr);

        return list;
    }

    public static Dictionary<string, int> GetDefaultRfModeTableByRegion(string region)
    {
        var rfModeTable = new Dictionary<string, int>();
        rfModeTable.Add("MaxThroughput", 0);
        rfModeTable.Add("Hybrid", 1);
        rfModeTable.Add("DenseReaderM4", 2);
        rfModeTable.Add("DenseReaderM8", 3);
        if (!string.IsNullOrEmpty(region)
            && !region.ToUpper().Contains("ETSI")
            && !region.ToUpper().Contains("INDIA"))
            rfModeTable.Add("MaxMiller", 4);
        rfModeTable.Add("AutoSetDenseReaderDeepScan", 1002);
        rfModeTable.Add("AutoSetStaticFast", 1003);
        rfModeTable.Add("AutoSetStaticDRM", 1004);
        return rfModeTable;
    }

    public static int GetRfModeValueByName(string rfModeName, Dictionary<string, int> rfModeTable)
    {
        var rfModeValue = -1;

        foreach (var rfModeEntry in rfModeTable)
            // do something with entry.Value or entry.Key
            if (string.Equals(rfModeEntry.Key.Trim(), rfModeName.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                rfModeValue = rfModeEntry.Value;
                return rfModeValue;
            }

        return rfModeValue;
    }

    public static List<int> GetDefaultSearchModeTable()
    {
        var list = new List<int>();
        int[] int_arr = { 0, 1, 2, 3, 5, 6 };

        list.AddRange(int_arr);

        return list;
    }

    // write a deep clone method for a class



    //    public static T DeepClone<T>(T obj)
    //    {
    //        using (var ms = new MemoryStream())
    //        {

    //            var formatter = new BinaryFormatter();
    //#pragma warning disable SYSLIB0011 // Type or member is obsolete
    //            formatter.Serialize(ms, obj);
    //#pragma warning restore SYSLIB0011 // Type or member is obsolete
    //            ms.Position = 0;

    //#pragma warning disable SYSLIB0011 // Type or member is obsolete
    //            return (T) formatter.Deserialize(ms);
    //#pragma warning restore SYSLIB0011 // Type or member is obsolete
    //        }
    //    }

    #region Utilities for testing whether a string contains only hex or binary characters

    /// <summary>
    ///     Utility for verifying that a given string includes only Hexadecimal
    ///     characters.
    /// </summary>
    /// <param name="InputString">
    ///     A hexadecimal string to test in capitals or lower case, either with
    ///     the "0x" prefix, or without.
    /// </param>
    /// <returns>
    ///     true if the string contains only Hexadecimal characters.
    ///     false if any non-hex characters are detected.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown if a null or empty string is passed in.
    /// </exception>
    public static bool OnlyHexInString(string InputString)
    {
        var returnResult = false;
        var SourceString = new StringBuilder();

        if (false == string.IsNullOrEmpty(InputString))
        {
            // Append the input string to the SourceString work variable
            SourceString.Append(InputString);

            // Remove any prefix characters
            SourceString.Replace("0x", string.Empty);

            // Next, remove any space or dash delimiters
            SourceString.Replace(" ", string.Empty);
            SourceString.Replace("-", string.Empty);

            returnResult =
                Regex.IsMatch(SourceString.ToString(), @"\A\b[0-9a-fA-F]+\b\Z");
        }
        else
        {
            throw new ArgumentNullException();
        }

        return returnResult;
    }

    /// <summary>
    ///     Utility for verifying that a given string includes only binary
    ///     characters.
    /// </summary>
    /// <param name="InputString">
    ///     A string of characters to test.
    /// </param>
    /// <returns>
    ///     true if the string contains only decimal characters.
    ///     false if any non-decimal characters are detected.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown if a null or empty string is passed in.
    /// </exception>
    public static bool OnlyBinInString(string InputString)
    {
        var returnResult = false;
        var SourceString = new StringBuilder();

        if (false == string.IsNullOrEmpty(InputString))
        {
            SourceString.Append(InputString);

            // Next, remove any space or dash delimiters
            SourceString.Replace(" ", string.Empty);
            SourceString.Replace("-", string.Empty);

            returnResult =
                Regex.IsMatch(SourceString.ToString(), @"\A\b[01]+\b\Z");
        }
        else
        {
            throw new ArgumentNullException();
        }

        return returnResult;
    }

    #endregion

    #region test whether file is legitimate

    /// <summary>
    ///     Verifies that a given file path and filename string is both a valid
    ///     format, and that the file exists.
    /// </summary>
    /// <param name="filePathAndName">
    ///     A complete file path and filename to check.
    /// </param>
    /// <returns>
    ///     true if the file exists and is accessible.
    ///     false if the file path and filename are not valid, or the file does
    ///     not exist.
    /// </returns>
    public static bool isValidFile(string filePathAndName)
    {
        var isValid = false;

        try
        {
            isValid = !string.IsNullOrEmpty(filePathAndName) &&
                      Path.GetFullPath(filePathAndName).IndexOfAny(Path.GetInvalidPathChars()) < 0 &&
                      File.Exists(filePathAndName);

            return isValid;
        }
        catch
        {
            return isValid;
        }
    }

    public static string CreateMD5Hash(string input)
    {
        // Step 1, calculate MD5 hash from input
        var md5 = MD5.Create();
        var inputBytes = Encoding.ASCII.GetBytes(input);
        var hashBytes = md5.ComputeHash(inputBytes);

        // Step 2, convert byte array to hex string
        var sb = new StringBuilder();
        for (var i = 0; i < hashBytes.Length; i++) sb.Append(hashBytes[i].ToString("X2"));
        return sb.ToString();
    }

    public static string CalculateMD5(string input)
    {
        // Use input string to calculate MD5 hash
        using (var md5 = MD5.Create())
        {
            var inputBytes = Encoding.ASCII.GetBytes(input);
            var hashBytes = md5.ComputeHash(inputBytes);

            return Convert.ToHexString(hashBytes); // .NET 5 +

            // Convert the byte array to hexadecimal string prior to .NET 5
            // StringBuilder sb = new System.Text.StringBuilder();
            // for (int i = 0; i < hashBytes.Length; i++)
            // {
            //     sb.Append(hashBytes[i].ToString("X2"));
            // }
            // return sb.ToString();
        }
    }

    #endregion

    public static string ExtractLineFromJsonObject(
    JObject smartReaderTagEventData,
    StandaloneConfigDTO config,
    Microsoft.Extensions.Logging.ILogger logger)
    {
        if (smartReaderTagEventData == null)
            throw new ArgumentNullException(nameof(smartReaderTagEventData));

        if (config == null)
            throw new ArgumentNullException(nameof(config));

        var sb = new StringBuilder();
        string fieldDelim = GetFieldDelimiter(config.fieldDelim);
        string lineEnd = GetLineEnd(config.lineEnd);

        try
        {
            if (!smartReaderTagEventData.ContainsKey("tag_reads")) return string.Empty;

            foreach (var tagRead in smartReaderTagEventData["tag_reads"]?.ToList() ?? new List<JToken>())
            {
                AppendField(sb, tagRead["antennaPort"], config.includeAntennaPort, fieldDelim);
                AppendField(sb, tagRead["antennaZone"], config.includeAntennaZone, fieldDelim);
                AppendField(sb, tagRead["epc"], "1", fieldDelim); // Always include EPC if present
                AppendField(sb, tagRead["firstSeenTimestamp"], config.includeFirstSeenTimestamp, fieldDelim);
                AppendField(sb, tagRead["peakRssi"], config.includePeakRssi, fieldDelim);
                AppendField(sb, tagRead["tid"], config.includeTid, fieldDelim);
                AppendField(sb, tagRead["rfPhase"], config.includeRFPhaseAngle, fieldDelim);
                AppendField(sb, tagRead["rfDoppler"], config.includeRFDopplerFrequency, fieldDelim);
                AppendField(sb, tagRead["frequency"], config.includeRFChannelIndex, fieldDelim);

                if (string.Equals("1", config.includeGpiEvent, StringComparison.OrdinalIgnoreCase))
                {
                    AppendGpiStatus(sb, tagRead, fieldDelim);
                }

                AppendSgtinFields(config, sb, tagRead, fieldDelim);
            }

            AppendField(sb, smartReaderTagEventData["readerName"], "1", fieldDelim);
            AppendField(sb, smartReaderTagEventData["site"], "1", fieldDelim);
            AppendCustomFields(sb, smartReaderTagEventData, config, fieldDelim);
            AppendBarcode(sb, smartReaderTagEventData, config, fieldDelim);

            sb.Append(lineEnd);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error processing JSON object for line extraction.");
            throw;
        }

        return sb.ToString().Replace(fieldDelim + lineEnd, lineEnd);
    }

    private static string GetFieldDelimiter(string fieldDelimConfig)
    {
        return fieldDelimConfig switch
        {
            "1" => ",",
            "2" => " ",
            "3" => "\t",
            "4" => ";",
            _ => ""
        };
    }

    private static void AppendField(StringBuilder sb, JToken field, string condition, string fieldDelim)
    {
        if (string.Equals("1", condition, StringComparison.OrdinalIgnoreCase))
        {
            sb.Append(field?.ToString() ?? string.Empty);
            sb.Append(fieldDelim);
        }
    }

    private static void AppendGpiStatus(StringBuilder sb, JToken tagRead, string fieldDelim)
    {
        for (int i = 1; i <= 4; i++)
        {
            var gpiStatus = tagRead?[$"gpi{i}Status"]?.ToString();
            sb.Append(string.IsNullOrEmpty(gpiStatus) ? "0" : gpiStatus);
            sb.Append(fieldDelim);
        }
    }

    private static void AppendSgtinFields(StandaloneConfigDTO standaloneConfigDTO, StringBuilder sb, JToken tagRead, string fieldDelim)
    {
        if (string.Equals("1", standaloneConfigDTO?.parseSgtinEnabled, StringComparison.OrdinalIgnoreCase))
        {
            AppendField(sb, tagRead["tagDataKeyName"], standaloneConfigDTO?.parseSgtinIncludeKeyType, fieldDelim);
            AppendField(sb, tagRead["tagDataKey"], "1", fieldDelim); // Always include tagDataKey
            AppendField(sb, tagRead["tagDataSerial"], standaloneConfigDTO?.parseSgtinIncludeSerial, fieldDelim);
            AppendField(sb, tagRead["tagDataPureIdentity"], standaloneConfigDTO?.parseSgtinIncludePureIdentity, fieldDelim);
        }
    }

    private static void AppendCustomFields(StringBuilder sb, JObject data, StandaloneConfigDTO config, string fieldDelim)
    {
        for (int i = 1; i <= 4; i++)
        {
            var customFieldEnabled = (string)data[$"customField{i}Enabled"];
            var customFieldName = (string)data[$"customField{i}Name"];
            if (string.Equals("1", customFieldEnabled, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(customFieldName))
            {
                AppendField(sb, data[customFieldName], "1", fieldDelim);
            }
        }
    }

    private static void AppendBarcode(StringBuilder sb, JObject data, StandaloneConfigDTO config, string fieldDelim)
    {
        var barcode = data["barcode"]?.ToString() ?? data.SelectToken("tag_reads[0].barcode")?.ToString();
        if (!string.IsNullOrEmpty(barcode) && (
            string.Equals("1", config?.enableBarcodeSerial, StringComparison.OrdinalIgnoreCase) ||
            string.Equals("1", config?.enableBarcodeHid, StringComparison.OrdinalIgnoreCase) ||
            string.Equals("1", config?.enableBarcodeTcp, StringComparison.OrdinalIgnoreCase)))
        {
            sb.Append(barcode);
            sb.Append(fieldDelim);
        }
    }

    private static string GetLineEnd(string lineEndConfig)
    {
        return lineEndConfig switch
        {
            "1" => "\n",
            "2" => "\r\n",
            "3" => "\r",
            _ => ""
        };
    }
}