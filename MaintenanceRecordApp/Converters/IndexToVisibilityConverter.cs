using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MaintenanceRecordApp.Converters;

public sealed class IndexToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var actual = value is int index ? index : -1;
        var expected = int.TryParse(parameter?.ToString(), out var parsed) ? parsed : -1;
        return actual == expected ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
