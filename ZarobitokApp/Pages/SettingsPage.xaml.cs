using System.Globalization;
using ZarobitokApp.Services;

namespace ZarobitokApp.Pages;

public partial class SettingsPage : ContentPage
{
    private bool _loading;

    public SettingsPage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        _loading = true;
        var s = ShiftStore.Load();

        RateEntry.Text = s.RatePerHour.ToString(CultureInfo.InvariantCulture);
        CurrencyEntry.Text = s.Currency;
        PlannedEntry.Text = s.PlannedHours.ToString(CultureInfo.InvariantCulture);
        BreakEntry.Text = s.UnpaidBreakMinutes.ToString(CultureInfo.InvariantCulture);
        OvertimeEntry.Text = s.OvertimeMultiplier.ToString(CultureInfo.InvariantCulture);

        _loading = false;
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        if (_loading) return;

        ShiftManager.UpdateSettings(s =>
        {
            if (TryDecimal(RateEntry.Text, out var rate) && rate >= 0)
                s.RatePerHour = rate;

            if (!string.IsNullOrWhiteSpace(CurrencyEntry.Text))
                s.Currency = CurrencyEntry.Text.Trim();

            if (TryDouble(PlannedEntry.Text, out var planned) && planned > 0)
                s.PlannedHours = planned;

            if (int.TryParse(BreakEntry.Text, out var brk) && brk >= 0)
                s.UnpaidBreakMinutes = brk;

            if (TryDecimal(OvertimeEntry.Text, out var mult) && mult >= 1)
                s.OvertimeMultiplier = mult;
        });
    }

    // Кома і крапка як роздільник — обидві приймаються.
    private static bool TryDecimal(string? text, out decimal value) =>
        decimal.TryParse((text ?? string.Empty).Replace(',', '.'),
            NumberStyles.Any, CultureInfo.InvariantCulture, out value);

    private static bool TryDouble(string? text, out double value) =>
        double.TryParse((text ?? string.Empty).Replace(',', '.'),
            NumberStyles.Any, CultureInfo.InvariantCulture, out value);
}
