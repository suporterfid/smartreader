using SmartReaderStandalone.ViewModel.Read.Epcis.Enums;

namespace SmartReaderStandalone.ViewModel.Read.Epcis;

public class CustomField
{
    public FieldType Type { get; set; }
    public string Name { get; set; }
    public string Namespace { get; set; }
    public string TextValue { get; set; }
    public double? NumericValue { get; set; }
    public DateTime? DateValue { get; set; }
    public List<CustomField> Children { get; set; } = new();
}