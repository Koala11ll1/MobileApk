using System.Globalization;
using ZarobitokApp.Models;
using ZarobitokApp.Services;

namespace ZarobitokApp.Pages;

public sealed class ExtraRow
{
    public required Guid Id { get; init; }
    public required string Label { get; init; }
    public required string TimeText { get; init; }
    public required string AmountText { get; init; }
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

        // Секундного цокання тут не треба — досить оновлюватись, коли
        // хтось інший (наприклад, стоп зміни з віджета) міг змінити стан.
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

        FormCard.IsVisible = s.IsRunning;
        NoShiftLabel.IsVisible = !s.IsRunning;

        var shiftExtras = s.IsRunning
            ? ShiftStore.LoadExtras()
                .Where(x => x.AtUtc >= s.StartedAtUtc!.Value)
                .OrderByDescending(x => x.AtUtc)
                .ToList()
            : new List<ExtraEarning>();

        ShiftExtrasTotalLabel.Text = EarningsCalculator.Format(
            shiftExtras.Sum(x => x.Amount), s.Currency);

        ExtrasView.ItemsSource = shiftExtras.Select(x => new ExtraRow
        {
            Id = x.Id,
            Label = x.Label,
            TimeText = x.AtUtc.ToLocalTime().ToString("HH:mm"),
            AmountText = EarningsCalculator.Format(x.Amount, s.Currency)
        }).ToList();
    }

    private void OnAddClicked(object? sender, EventArgs e)
    {
        var label = LabelEntry.Text?.Trim();
        if (string.IsNullOrWhiteSpace(label)) return;

        if (!decimal.TryParse(
                (AmountEntry.Text ?? string.Empty).Replace(',', '.'),
                NumberStyles.Any, CultureInfo.InvariantCulture, out var amount)
            || amount <= 0)
        {
            return;
        }

        ShiftManager.AddExtra(label, amount);

        LabelEntry.Text = string.Empty;
        AmountEntry.Text = string.Empty;
        Refresh();
    }

    private async void OnDeleteClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: ExtraRow row }) return;

        var confirmed = await DisplayAlert(
            "Видалити допзаробіток?",
            $"{row.Label} · {row.AmountText}",
            "Видалити",
            "Скасувати");

        if (!confirmed) return;

        ShiftManager.RemoveExtra(row.Id);
        Refresh();
    }
}
