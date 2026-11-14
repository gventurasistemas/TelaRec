using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TelaRec.App.Wpf.Infrastructure;

/// <summary>Mostra o elemento quando o booleano é falso (ex.: estado vazio de listas).</summary>
public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility.Collapsed;
}
