using System.Globalization;
using ZarobitokApp.Services;

namespace ZarobitokApp.Pages;

public sealed class ExtraItemRow
{
    public required Guid Id { get; init; }
    public required string Label { get; init; }
    public required string PriceText { get; init; }
    public required int Count { get; init; }
    public required bool CanIncrement { get; init; }
    public required bool CanDecrement { get; init; }
}

public partial class ExtrasPage : ContentPage
{
    private IDispatcherTimer? _timer;

    public ExtrasPage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        Refresh();

        _timer = Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(2);
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
    }

    protected override void OnDisappearing()
    {
        _timer?.Stop();
        _timer = null;
        base.OnDisappearing();
    }

    private void Refresh()
    {
        var s = ShiftStore.Load();
        var items = ShiftStore.LoadExtraItems();
        var ticks = ShiftStore.LoadExtraTicks();

        NoShiftLabel.IsVisible = !s.IsRunning;

        var canEdit = s.IsRunning;
        var from = canEdit ? s.StartedAtUtc!.Value : DateTime.MinValue;
        var now = DateTime.UtcNow;

        ItemsView.ItemsSource = items.Select(item =>
        {
            var count = canEdit
                ? Math.Max(0, ticks
                    .Where(t => t.ItemId == item.Id && t.AtUtc >= from && t.AtUtc <= now)
                    .Sum(t => t.Delta))
                : 0;

            return new ExtraItemRow
            {
                Id = item.Id,
                Label = item.Label,
                PriceText = $"{item.UnitPrice:0.##} {s.Currency}/шт",
                Count = count,
                CanIncrement = canEdit,
                CanDecrement = canEdit && count > 0
            };
        }).ToList();

        var total = canEdit ? EarningsCalculator.TicksTotal(items, ticks, from, now) : 0m;
        ShiftExtrasTotalLabel.Text = EarningsCalculator.Format(total, s.Currency);
    }

    private void OnAddClicked(object? sender, EventArgs e)
    {
        var label = LabelEntry.Text?.Trim();
        if (string.IsNullOrWhiteSpace(label)) return;

        if (!decimal.TryParse(
                (PriceEntry.Text ?? string.Empty).Replace(',', '.'),
                NumberStyles.Any, CultureInfo.InvariantCulture, out var price)
            || price <= 0)
        {
            return;
        }

        ShiftManager.AddOrGetExtraItem(label, price);

        LabelEntry.Text = string.Empty;
        PriceEntry.Text = string.Empty;
        Refresh();
    }

    private void OnPlusClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: ExtraItemRow row }) return;
        ShiftManager.Tick(row.Id, +1);
        Refresh();
    }

    private void OnMinusClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: ExtraItemRow row } || row.Count <= 0) return;
        ShiftManager.Tick(row.Id, -1);
        Refresh();
    }
}
