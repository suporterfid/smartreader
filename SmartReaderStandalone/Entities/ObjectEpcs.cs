using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SmartReaderStandalone.Entities;

public class ObjectEpcs
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public string Epc { get; set; }

    public int AntennaPort { get; set; }

    public string AntennaZone { get; set; }

    public double? PeakRssi { get; set; }

    public double? TxPower { get; set; }

    public long? FirstSeenTimestamp { get; set; }

    public long? LastSeenTimestamp { get; set; }
}