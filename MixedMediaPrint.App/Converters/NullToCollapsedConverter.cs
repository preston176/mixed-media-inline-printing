using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MixedMediaPrint.App.Converters;

/// <summary>Null/empty string -> Collapsed, anything else -> Visible. Used for optional warning/error text blocks that should take up no space when there's nothing to say.</summary>
public sealed class NullToCollapsedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
