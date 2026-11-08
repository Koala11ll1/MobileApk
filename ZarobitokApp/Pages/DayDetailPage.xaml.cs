using ZarobitokApp.Models;
using ZarobitokApp.Services;

namespace ZarobitokApp.Pages;

public sealed class DayShiftRow
{
    public required ShiftLogEntry Entry { get; init; }
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
    private readonly DateTime _day;
    private readonly string _currency;
    private readonly bool _use12Hour;
    private List<ShiftLogEntry> _dayEntries;
    private List<ExtraItem> _items;
    private List<ExtraTick> _ticks;

    // Редагування кількості дозволяємо, лише якщо за день рівно одна зміна — інакше
    // неоднозначно, до якої з кількох зміну віднести нову правку.
    private (DateTime from, DateTime to)? _editableRange;

    public DayDetailPage(
        DateTime day, string currency, bool use12Hour, List<ShiftLogEntry> dayEntries,
        List<ExtraItem> items, List<ExtraTick> ticks)
    {
        InitializeComponent();
        Title = DayTitle(day);

        _day = day;
        _currency = currency;
        _use12Hour = use12Hour;
        _dayEntries = dayEntries;
        _items = items;
        _ticks = ticks;

        Render();
    }

    private string Time(DateTime utc) => EarningsCalculator.FormatTime(utc.ToLocalTime(), _use12Hour);

    private void Render()
    {
        _editableRange = _dayEntries.Count == 1
            ? (_dayEntries[0].StartedAtUtc, _dayEntries[0].EndedAtUtc)
            : null;

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
                Entry = e,
                TimeText = $"{Time(e.StartedAtUtc)} — {Time(e.EndedAtUtc)}",
                DetailText = $"{EarningsCalculator.FormatDuration(e.Duration)} · {e.RatePerHour:0.##} {_currency}/год",
                AmountText = EarningsCalculator.Format(e.Earned + ExtrasFor(e), _currency)
            })
            .ToList();

        if (_editableRange is { } range)
        {
            MultiShiftHintLabel.IsVisible = false;
            ExtrasBreakdownView.IsVisible = false;
            EditableExtrasView.IsVisible = true;

            EditableExtrasView.ItemsSource = _items
                .Select(item =>
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
                })
                // Прибрані типи показуємо лише якщо в цій зміні вони були.
                .Where(r => r.Count > 0 || !_items.First(i => i.Id == r.Id).Archived)
                .ToList();
        }
        else
        {
            MultiShiftHintLabel.IsVisible = _dayEntries.Count > 1;
            EditableExtrasView.IsVisible = false;
            ExtrasBreakdownView.IsVisible = true;

            var priceById = _items.ToDictionary(i => i.Id, i => i.UnitPrice);

            ExtrasBreakdownView.ItemsSource = _ticks
                .Where(t => _dayEntries.Any(e => t.AtUtc >= e.StartedAtUtc && t.AtUtc <= e.EndedAtUtc))
                .GroupBy(t => t.ItemId)
                .Select(g => (
                    label: _items.FirstOrDefault(i => i.Id == g.Key)?.Label ?? "Невідомий тип",
                    count: g.Sum(t => t.Delta),
                    amount: g.Sum(t => EarningsCalculator.TickValue(t, priceById))))
                .Where(x => x.count != 0)
                .Select(x => new ExtraBreakdownRow
                {
                    LabelText = Math.Abs(x.count) > 1 ? $"{x.label} ×{x.count}" : x.label,
                    AmountText = EarningsCalculator.Format(x.amount, _currency)
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

    private async void OnEditShiftClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: DayShiftRow row }) return;
        var entry = row.Entry;

        var startText = await DisplayPromptAsync(
            "Початок зміни", "Година:хвилина (наприклад 09:30)", "Далі", "Скасувати",
            keyboard: Keyboard.Text, initialValue: entry.StartedAtUtc.ToLocalTime().ToString("HH:mm"));
        if (startText is null) return;

        var endText = await DisplayPromptAsync(
            "Кінець зміни", "Година:хвилина. Якщо раніше за початок — це наступна доба.", "Зберегти", "Скасувати",
            keyboard: Keyboard.Text, initialValue: entry.EndedAtUtc.ToLocalTime().ToString("HH:mm"));
        if (endText is null) return;

        if (!Input.TryTimeOfDay(startText, out var startTod) || !Input.TryTimeOfDay(endText, out var endTod))
        {
            await DisplayAlert("Невірний час", "Введи час у форматі ГГ:ХХ, наприклад 09:30.", "OK");
            return;
        }

        var (startUtc, endUtc) = ShiftEditing.ResolveRange(entry.StartedAtUtc.ToLocalTime(), startTod, endTod);
        var error = ShiftManager.EditLogEntry(entry, startUtc, endUtc);
        if (error is not null)
        {
            await DisplayAlert("Не збережено", error, "OK");
            return;
        }

        // Кінець міг переїхати на інший день — тоді цей екран більше не актуальний.
        _dayEntries = ShiftStore.LoadLog().Where(x => x.EndedAtUtc.ToLocalTime().Date == _day).ToList();
        _items = ShiftStore.LoadExtraItems();
        _ticks = ShiftStore.LoadExtraTicks();

        if (_dayEntries.Count == 0) await Navigation.PopModalAsync();
        else Render();
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
