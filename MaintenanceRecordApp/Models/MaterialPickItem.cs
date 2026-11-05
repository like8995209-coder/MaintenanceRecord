using CommunityToolkit.Mvvm.ComponentModel;

namespace MaintenanceRecordApp.Models;

public partial class MaterialPickItem : ObservableObject
{
    public required MaterialCatalogItem Material { get; init; }

    [ObservableProperty]
    private bool _isSelected;

    public string Name => Material.Name;

    public string Specification => Material.Specification;

    public string Unit => Material.Unit;

    public decimal? Price => Material.Price;

    public string DisplayText
    {
        get
        {
            var specification = string.IsNullOrWhiteSpace(Material.Specification)
                ? string.Empty
                : $"  {Material.Specification}";
            var price = Material.Price.HasValue
                ? $"  ¥{Material.Price.Value:0.00}"
                : string.Empty;
            return $"{Material.Name}{specification}{price}";
        }
    }
}
