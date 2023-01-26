using SmartReaderStandalone.ViewModel.Read.Epcis.Enums;

namespace SmartReaderStandalone.ViewModel.Read.Epcis;

public class Epc
{
    public string Id { get; set; }
    public EpcType Type { get; set; }
    public bool IsQuantity { get; set; }
    public float? Quantity { get; set; }
    public string UnitOfMeasure { get; set; }
}