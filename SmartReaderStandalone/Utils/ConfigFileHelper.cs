using System.Text.Json;
using SmartReader.Infrastructure.ViewModel;

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
        StandaloneConfigDTO standaloneConfigDTO = null;
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
}