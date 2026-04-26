using System.Globalization;
using System.Windows.Data;

namespace PiggyBank.App.Converters;

/// <summary>
/// WPF's <c>DatePicker</c> and <c>System.Windows.Controls.Calendar</c> bind
/// to <see cref="DateTime"/>. The domain uses <see cref="DateOnly"/>. This
/// converter bridges them without leaking <c>DateTime</c> types into the
/// view model (which keeps the VM portable to Blazor later, where the
/// equivalent controls are <c>DateOnly</c>-native).
/// </summary>
public sealed class DateOnlyToDateTimeConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is DateOnly d) return d.ToDateTime(TimeOnly.MinValue);
        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is DateTime dt) return DateOnly.FromDateTime(dt);
        // Null in (user cleared the DatePicker) must map to null out so a
        // nullable DateOnly? property actually goes back to null. Previously
        // this returned default(DateOnly) == 0001-01-01, which silently
        // populated the property with a bogus date and made "clear" a no-op.
        return null;
    }
}
