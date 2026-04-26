using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace PiggyBank.App.Converters;

/// <summary>
/// Converts a hex colour string like "#3B82F6" into a <see cref="Brush"/>.
/// Used by the profile picker list and any other place that binds a string
/// property to a Brush-typed UI property. Falls back to a transparent brush
/// for null / bad input so a bad colour doesn't kill the whole window.
/// </summary>
public sealed class HexToBrushConverter : IValueConverter
{
    private static readonly BrushConverter _inner = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string hex || string.IsNullOrWhiteSpace(hex))
            return Brushes.Transparent;

        try
        {
            return _inner.ConvertFromString(hex) as Brush ?? Brushes.Transparent;
        }
        catch
        {
            return Brushes.Transparent;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
