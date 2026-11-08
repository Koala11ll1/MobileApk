using System.Globalization;

namespace ZarobitokApp.Services;

/// <summary>Розбір чисел із полів вводу: кома і крапка однаково приймаються як роздільник.</summary>
public static class Input
{
    public static bool TryDecimal(string? text, out decimal value) =>
        decimal.TryParse((text ?? string.Empty).Trim().Replace(',', '.'),
            NumberStyles.Number, CultureInfo.InvariantCulture, out value);

    public static bool TryDouble(string? text, out double value) =>
        double.TryParse((text ?? string.Empty).Trim().Replace(',', '.'),
            NumberStyles.Number, CultureInfo.InvariantCulture, out value);

    /// <summary>Час "HH:mm" (також "H:mm" і "H.mm") у годинах/хвилинах від півночі.</summary>
    public static bool TryTimeOfDay(string? text, out TimeSpan value)
    {
        value = default;
        var t = (text ?? string.Empty).Trim().Replace('.', ':');
        if (!TimeSpan.TryParseExact(t, new[] { @"h\:mm", @"hh\:mm" }, CultureInfo.InvariantCulture, out var ts)
            || ts < TimeSpan.Zero || ts >= TimeSpan.FromDays(1))
            return false;
        value = ts;
        return true;
    }
}
