using ZarobitokApp.Models;
using ZarobitokApp.Services;

namespace ZarobitokApp.Pages;

public sealed class DayShiftRow
{
    public required string TimeText { get; init; }
    public required string DetailText { get; init; }
    public required string AmountText { get; init; }
}

public sealed class ExtraBreakdownRow
{
    public required string LabelText { get; init; }
    public required string AmountText { get; init; }
}

public partial class DayDetailPage : ContentPage
{
    public DayDetailPage(DateTime day, string currency, List<ShiftLogEntry> dayEntries, List<ExtraEarning> dayExtras)
    {
        InitializeComponent();
        Title = DayTitle(day);
        Render(currency, dayEntries, dayExtras);
    }

    private void Render(string currency, List<ShiftLogEntry> dayEntries, List<ExtraEarning> dayExtras)
    {
        var totalEarned = dayEntries.Sum(e => e.Earned);
        var totalExtras = dayExtras.Sum(x => x.Amount);
        var baseEarned = totalEarned - totalExtras;
        var totalHours = dayEntries.Aggregate(TimeSpan.Zero, (sum, e) => sum + e.Duration);

        TotalLabel.Text = EarningsCalculator.Format(totalEarned, currency);
        HoursLabel.Text = EarningsCalculator.FormatDuration(totalHours);
        ShiftCountLabel.Text = dayEntries.Count.ToString();
        ExtrasTotalLabel.Text = EarningsCalculator.Format(totalExtras, currency);

        var hours = (decimal)totalHours.TotalHours;
        AvgRateLabel.Text = hours > 0 ? EarningsCalculator.Format(baseEarned / hours, currency) : "—";
        AvgEffectiveLabel.Text = hours > 0 ? EarningsCalculator.Format(totalEarned / hours, currency) : "—";

        ShiftsView.ItemsSource = dayEntries
            .OrderByDescending(e => e.StartedAtUtc)
            .Select(e => new DayShiftRow
            {
                TimeText = $"{e.StartedAtUtc.ToLocalTime():HH:mm} — {e.EndedAtUtc.ToLocalTime():HH:mm}",
                DetailText = $"{EarningsCalculator.FormatDuration(e.Duration)} · {e.RatePerHour:0.##} {currency}/год",
                AmountText = EarningsCalculator.Format(e.Earned, currency)
            })
            .ToList();

        ExtrasBreakdownView.ItemsSource = dayExtras
            .GroupBy(x => x.Label)
            .OrderByDescending(g => g.Sum(x => x.Amount))
            .Select(g => new ExtraBreakdownRow
            {
                LabelText = g.Count() > 1 ? $"{g.Key} ×{g.Count()}" : g.Key,
                AmountText = EarningsCalculator.Format(g.Sum(x => x.Amount), currency)
            })
            .ToList();
    }

    private async void OnCloseClicked(object? sender, EventArgs e)
        => await Navigation.PopModalAsync();

    private static string DayTitle(DateTime day)
    {
        if (day == DateTime.Today) return "Сьогодні";
        if (day == DateTime.Today.AddDays(-1)) return "Вчора";
        return day.ToString("dd.MM.yyyy");
    }
}
