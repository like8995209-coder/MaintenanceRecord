using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MaintenanceRecordApp.Models;

public partial class RecordMaterial : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _specification = string.Empty;

    [ObservableProperty]
    private string _unit = string.Empty;

    [ObservableProperty]
    private decimal _quantity = 1;

    [ObservableProperty]
    private decimal? _unitPrice;

    [ObservableProperty]
    private string _note = string.Empty;

    [JsonIgnore]
    public decimal Subtotal => Quantity * (UnitPrice ?? 0);

    [JsonIgnore]
    public string DisplayText
    {
        get
        {
            var specification = string.IsNullOrWhiteSpace(Specification) ? string.Empty : $" / {Specification}";
            var price = UnitPrice.HasValue ? $"    单价 ¥{UnitPrice.Value:0.00}" : "    未设单价";
            return $"{Name}{specification}{price}";
        }
    }

    partial void OnQuantityChanged(decimal value)
    {
        OnPropertyChanged(nameof(Subtotal));
    }

    partial void OnUnitPriceChanged(decimal? value)
    {
        OnPropertyChanged(nameof(Subtotal));
        OnPropertyChanged(nameof(DisplayText));
    }

    partial void OnNameChanged(string value)
    {
        OnPropertyChanged(nameof(DisplayText));
    }

    partial void OnSpecificationChanged(string value)
    {
        OnPropertyChanged(nameof(DisplayText));
    }
}
