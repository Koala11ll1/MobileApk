using ZarobitokApp.Services;

namespace ZarobitokApp.Pages;

public sealed class ExtraItemRow
{
    public required Guid Id { get; init; }
    public required string Label { get; init; }
    public required decimal Price { get; init; }
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
    }

    protected override void OnDisappearing()
    {
        _timer?.Stop();
        _timer = null;
        base.OnDisappearing();
    }

    /// <summary>
    /// Поки зміна не активна, тут нічого не тікає (лічильники завжди 0),
    /// тож фоновий таймер тільки б марно ганяв JSON і перебудову списку —
    /// саме це заважало плавному свайпу між вкладками. Вмикаємо його лише
    /// на час активної зміни, щоб зловити її можливий автостоп по плану.
    /// </summary>
    private void SyncTicking(bool running)
    {
        if (running)
        {
            if (_timer is not null) return;

            _timer = Dispatcher.CreateTimer();
            _timer.Interval = TimeSpan.FromSeconds(3);
            _timer.Tick += (_, _) => Refresh();
            _timer.Start();
        }
        else
        {
            _timer?.Stop();
            _timer = null;
        }
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

        ItemsView.ItemsSource = items.Where(i => !i.Archived).Select(item =>
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
                Price = item.UnitPrice,
                PriceText = $"{item.UnitPrice:0.##} {s.Currency}/шт",
                Count = count,
                CanIncrement = canEdit,
                CanDecrement = canEdit && count > 0
            };
        }).ToList();

        var total = canEdit ? EarningsCalculator.TicksTotal(items, ticks, from, now) : 0m;
        ShiftExtrasTotalLabel.Text = EarningsCalculator.Format(total, s.Currency);

        SyncTicking(canEdit);
    }

    private async void OnAddClicked(object? sender, EventArgs e)
    {
        var label = LabelEntry.Text?.Trim();
        if (string.IsNullOrWhiteSpace(label)) return;

        if (!Input.TryDecimal(PriceEntry.Text, out var price) || price <= 0) return;

        var existing = ShiftStore.LoadExtraItems()
            .FirstOrDefault(i => string.Equals(i.Label, label, StringComparison.OrdinalIgnoreCase));

        if (existing is { Archived: false })
        {
            // Ціну активного типу тут не міняємо — для цього є меню «⋯» біля нього.
            await DisplayAlert("Вже є",
                $"«{existing.Label}» уже в списку за {existing.UnitPrice:0.##}. Щоб змінити ціну, натисни «⋯» біля нього.", "OK");
            return;
        }

        if (existing is not null)
            ShiftManager.RestoreExtraItem(existing.Id, price);   // був прибраний — повертаємо з новою ціною
        else
            ShiftManager.AddOrGetExtraItem(label, price);

        LabelEntry.Text = string.Empty;
        PriceEntry.Text = string.Empty;
        Refresh();
    }

    private async void OnMenuClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: ExtraItemRow row }) return;

        const string changePrice = "Змінити ціну";
        const string rename = "Перейменувати";
        const string remove = "Прибрати зі списку";

        var action = await DisplayActionSheet(row.Label, "Скасувати", null, changePrice, rename, remove);

        switch (action)
        {
            case changePrice:
            {
                var text = await DisplayPromptAsync(
                    row.Label, "Нова ціна за одиницю. Минулі підрахунки не зміняться.",
                    "Зберегти", "Скасувати", keyboard: Keyboard.Numeric, initialValue: row.Price.ToString("0.##"));
                if (!Input.TryDecimal(text, out var price) || price <= 0) return;
                ShiftManager.UpdateExtraItem(row.Id, row.Label, price);
                break;
            }
            case rename:
            {
                var name = (await DisplayPromptAsync(
                    "Нова назва", row.Label, "Зберегти", "Скасувати", initialValue: row.Label))?.Trim();
                if (string.IsNullOrWhiteSpace(name)) return;

                var clash = ShiftStore.LoadExtraItems().Any(i => i.Id != row.Id
                    && string.Equals(i.Label, name, StringComparison.OrdinalIgnoreCase));
                if (clash)
                {
                    await DisplayAlert("Назва зайнята", $"Тип «{name}» уже існує.", "OK");
                    return;
                }
                ShiftManager.UpdateExtraItem(row.Id, name, row.Price);
                break;
            }
            case remove:
            {
                var ok = await DisplayAlert("Прибрати зі списку?",
                    $"«{row.Label}» зникне зі списку, але минулі підрахунки в журналі збережуться.",
                    "Прибрати", "Скасувати");
                if (!ok) return;
                ShiftManager.ArchiveExtraItem(row.Id);
                break;
            }
            default:
                return;
        }

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
