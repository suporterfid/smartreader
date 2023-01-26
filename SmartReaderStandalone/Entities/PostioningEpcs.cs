using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SmartReaderStandalone.Entities;

public class PostioningEpcs
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public string Epc { get; set; }

    public long AntennaPort { get; set; }

    public string AntennaZone { get; set; }

    public double? PeakRssi { get; set; }

    public double? TxPower { get; set; }

    public long? FirstSeenTimestamp { get; set; }

    public long? LastSeenTimestamp { get; set; }

    public double? RfPhase { get; set; }

    public long? RfDoppler { get; set; }

    public long? RfChannel { get; set; }

    public bool IsPositioningHeader { get; set; }

    public DateTimeOffset FirstSeenOn { get; set; }

    public DateTimeOffset LastSeenOn { get; set; }

    public DateTimeOffset? LastPublishedOn { get; set; }
}