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
using System.Security.Cryptography;
using System.Text;

namespace ConsoleAppHash // Note: actual namespace depends on the project name.
{
    internal class Program
    {
        static void Main(string[] args)
        {

            string serial = "37021220460"; // BAE693A1D4F2E004B41E5F6C0CE27428

            Console.WriteLine(CreateMD5Hash("sM@RTrEADER2022-" + serial));
            _ = Console.ReadLine();
        }


        public static string CreateMD5Hash(string input)
        {
            // Step 1, calculate MD5 hash from input
            MD5 md5 = System.Security.Cryptography.MD5.Create();
            byte[] inputBytes = System.Text.Encoding.ASCII.GetBytes(input);
            byte[] hashBytes = md5.ComputeHash(inputBytes);

            // Step 2, convert byte array to hex string
            StringBuilder sb = new();
            for (int i = 0; i < hashBytes.Length; i++)
            {
                _ = sb.Append(hashBytes[i].ToString("X2"));
            }
            return sb.ToString();
        }
    }
}






