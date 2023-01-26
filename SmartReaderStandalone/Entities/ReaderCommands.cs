using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SmartReaderStandalone.Entities;

public class ReaderCommands
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public string Id { get; set; }

    public string Value { get; set; }

    public DateTime Timestamp { get; set; }
}