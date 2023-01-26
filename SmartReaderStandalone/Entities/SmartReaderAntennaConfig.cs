using System.ComponentModel.DataAnnotations;

namespace SmartReader.Infrastructure.Entities;

public class SmartReaderAntennaConfig
{
    [Key] public int Id { get; set; }

    [Required] public int AntennaPort { get; set; }

    public bool AntennaState { get; set; }

    public string? AntennaZone { get; set; }

    public int TransmitPower { get; set; }

    public int ReceiveSensitivity { get; set; }

    public int ReaderMode { get; set; }

    public int Session { get; set; }

    public int TagPopulation { get; set; }

    public int SmartReaderConfigId { get; set; }

    public SmartReaderConfig SmartReaderConfig { get; set; }
}