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
            Console.ReadLine();
        }


        public static string CreateMD5Hash(string input)
        {
            // Step 1, calculate MD5 hash from input
            MD5 md5 = System.Security.Cryptography.MD5.Create();
            byte[] inputBytes = System.Text.Encoding.ASCII.GetBytes(input);
            byte[] hashBytes = md5.ComputeHash(inputBytes);

            // Step 2, convert byte array to hex string
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < hashBytes.Length; i++)
            {
                sb.Append(hashBytes[i].ToString("X2"));
            }
            return sb.ToString();
        }
    }
}






