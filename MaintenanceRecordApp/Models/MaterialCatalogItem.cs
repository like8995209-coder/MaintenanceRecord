using CommunityToolkit.Mvvm.ComponentModel;

namespace MaintenanceRecordApp.Models;

public partial class MaterialCatalogItem : ObservableObject
{
    [ObservableProperty]
    private long _id;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _specification = string.Empty;

    [ObservableProperty]
    private string _unit = string.Empty;

    [ObservableProperty]
    private decimal? _price;

    public string SearchText =>
        $"{Name} {Specification} {Unit}".Trim();
}
