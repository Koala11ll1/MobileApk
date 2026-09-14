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

public sealed class ExtraEditRow
{
    public required Guid Id { get; init; }
    public required string Label { get; init; }
    public required string PriceText { get; init; }
    public required int Count { get; init; }
    public required bool CanDecrement { get; init; }
}

public partial class DayDetailPage : ContentPage
{
    private readonly string _currency;
    private readonly List<ShiftLogEntry> _dayEntries;
    private List<ExtraItem> _items;
    private List<ExtraTick> _ticks;

    // Редагування дозволяємо, лише якщо за день рівно одна зміна — інакше
    // неоднозначно, до якої з кількох зміну віднести нову правку.
    private readonly (DateTime from, DateTime to)? _editableRange;

    public DayDetailPage(
        DateTime day, string currency, List<ShiftLogEntry> dayEntries,
        List<ExtraItem> items, List<ExtraTick> ticks)
    {
        InitializeComponent();
        Title = DayTitle(day);

        _currency = currency;
        _dayEntries = dayEntries;
        _items = items;
        _ticks = ticks;
        _editableRange = dayEntries.Count == 1
            ? (dayEntries[0].StartedAtUtc, dayEntries[0].EndedAtUtc)
            : null;

        Render();
    }

    private void Render()
    {
        decimal ExtrasFor(ShiftLogEntry e) => EarningsCalculator.TicksTotal(_items, _ticks, e.StartedAtUtc, e.EndedAtUtc);

        var totalExtras = _dayEntries.Sum(ExtrasFor);
        var baseEarned = _dayEntries.Sum(e => e.Earned);
        var totalEarned = baseEarned + totalExtras;
        var totalHours = _dayEntries.Aggregate(TimeSpan.Zero, (sum, e) => sum + e.Duration);

        TotalLabel.Text = EarningsCalculator.Format(totalEarned, _currency);
        HoursLabel.Text = EarningsCalculator.FormatDuration(totalHours);
        ShiftCountLabel.Text = _dayEntries.Count.ToString();
        ExtrasTotalLabel.Text = EarningsCalculator.Format(totalExtras, _currency);

        var hours = (decimal)totalHours.TotalHours;
        AvgRateLabel.Text = hours > 0 ? EarningsCalculator.Format(baseEarned / hours, _currency) : "—";
        AvgEffectiveLabel.Text = hours > 0 ? EarningsCalculator.Format(totalEarned / hours, _currency) : "—";

        ShiftsView.ItemsSource = _dayEntries
            .OrderByDescending(e => e.StartedAtUtc)
            .Select(e => new DayShiftRow
            {
                TimeText = $"{e.StartedAtUtc.ToLocalTime():HH:mm} — {e.EndedAtUtc.ToLocalTime():HH:mm}",
                DetailText = $"{EarningsCalculator.FormatDuration(e.Duration)} · {e.RatePerHour:0.##} {_currency}/год",
                AmountText = EarningsCalculator.Format(e.Earned + ExtrasFor(e), _currency)
            })
            .ToList();

        if (_editableRange is { } range)
        {
            MultiShiftHintLabel.IsVisible = false;
            ExtrasBreakdownView.IsVisible = false;
            EditableExtrasView.IsVisible = true;

            EditableExtrasView.ItemsSource = _items.Select(item =>
            {
                var count = Math.Max(0, _ticks
                    .Where(t => t.ItemId == item.Id && t.AtUtc >= range.from && t.AtUtc <= range.to)
                    .Sum(t => t.Delta));

                return new ExtraEditRow
                {
                    Id = item.Id,
                    Label = item.Label,
                    PriceText = $"{item.UnitPrice:0.##} {_currency}/шт",
                    Count = count,
                    CanDecrement = count > 0
                };
            }).ToList();
        }
        else
        {
            MultiShiftHintLabel.IsVisible = _dayEntries.Count > 1;
            EditableExtrasView.IsVisible = false;
            ExtrasBreakdownView.IsVisible = true;

            ExtrasBreakdownView.ItemsSource = _ticks
                .Where(t => _dayEntries.Any(e => t.AtUtc >= e.StartedAtUtc && t.AtUtc <= e.EndedAtUtc))
                .GroupBy(t => t.ItemId)
                .Select(g => (item: _items.FirstOrDefault(i => i.Id == g.Key), count: g.Sum(t => t.Delta)))
                .Where(x => x.item is not null && x.count != 0)
                .Select(x => new ExtraBreakdownRow
                {
                    LabelText = Math.Abs(x.count) > 1 ? $"{x.item!.Label} ×{x.count}" : x.item!.Label,
                    AmountText = EarningsCalculator.Format(x.count * x.item!.UnitPrice, _currency)
                })
                .ToList();
        }
    }

    private void OnPlusClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: ExtraEditRow row } || _editableRange is not { } range) return;

        ShiftManager.TickAt(row.Id, +1, range.to);
        _ticks = ShiftStore.LoadExtraTicks();
        Render();
    }

    private void OnMinusClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: ExtraEditRow row } || _editableRange is not { } range) return;
        if (row.Count <= 0) return;

        ShiftManager.TickAt(row.Id, -1, range.to);
        _ticks = ShiftStore.LoadExtraTicks();
        Render();
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
