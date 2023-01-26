using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SmartReaderStandalone.Entities;

public class InventoryStatus
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public string Id { get; set; }

    public string CurrentStatus { get; set; }

    public string? CycleId { get; set; }

    public int? TotalCount { get; set; }

    public DateTimeOffset? StartedOn { get; set; }

    public DateTimeOffset? StoppedOn { get; set; }
}