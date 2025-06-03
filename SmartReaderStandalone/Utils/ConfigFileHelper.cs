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
using SmartReader.Infrastructure.ViewModel;
using System.Text.Json;

namespace SmartReaderStandalone.Utils;

public class ConfigFileHelper
{
    public static void SaveFile(StandaloneConfigDTO standaloneConfigDTO)
    {
        var fileName = "smartreader.json";
        var jsonString = JsonSerializer.Serialize(standaloneConfigDTO);


        //try
        //{
        //    File.WriteAllText(Path.Combine("/tmp", fileName), jsonString);
        //}
        //catch (Exception)
        //{
        //}

        //try
        //{
        //    File.WriteAllText(Path.Combine("/var", fileName), jsonString);
        //}
        //catch (Exception)
        //{
        //}

        //try
        //{
        //    File.WriteAllText(Path.Combine("/customer", fileName), jsonString);
        //}
        //catch (Exception)
        //{
        //}

        File.WriteAllText(Path.Combine("/customer/config", fileName), jsonString);
    }

    public static StandaloneConfigDTO? ReadFile()
    {
        StandaloneConfigDTO? standaloneConfigDTO = null;
        var fileName = @"/customer/config/smartreader.json";
        if (File.Exists(fileName))
        {
            var length = new FileInfo(fileName).Length;
            if (length > 0)
            {
                var fileContent = File.ReadAllText(fileName);
                standaloneConfigDTO = JsonSerializer.Deserialize<StandaloneConfigDTO>(fileContent);
            }
        }

        return standaloneConfigDTO;
    }

    public static StandaloneConfigDTO? GetSmartreaderDefaultConfigDTO()
    {
        StandaloneConfigDTO? standaloneConfigDTO = null;
        var fileName = @"/customer/config/smartreader-default.json";
        if (File.Exists(fileName))
        {
            var length = new FileInfo(fileName).Length;
            if (length > 0)
            {
                var fileContent = File.ReadAllText(fileName);
                standaloneConfigDTO = JsonSerializer.Deserialize<StandaloneConfigDTO>(fileContent);
            }
        }

        return standaloneConfigDTO;
    }
}