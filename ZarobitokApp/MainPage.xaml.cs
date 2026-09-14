using System.Globalization;
using ZarobitokApp.Models;
using ZarobitokApp.Services;

namespace ZarobitokApp;

public partial class MainPage : ContentPage
{
    private IDispatcherTimer? _timer;
    private bool _loading;

    public MainPage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        var crash = ShiftStore.TakeLastCrash();
        if (crash is not null)
            _ = DisplayAlert("Останній збій застосунку", crash, "OK");

        LoadSettingsIntoUi();
        LoadHistory();

        // Всередині апки обмежень немає — тікаємо щосекунди.
        _timer = Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();

        Refresh();
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
        var now = DateTime.UtcNow;

        AmountLabel.Text = EarningsCalculator.Format(
            EarningsCalculator.EarnedToday(s, now), s.Currency);

        PerSecondLabel.Text =
            $"+{EarningsCalculator.PerSecond(s):0.0000} {s.Currency}/сек";

        if (s.IsRunning)
        {
            var elapsed = EarningsCalculator.PaidElapsed(s, now);
            ElapsedLabel.Text = $"Зміна триває {EarningsCalculator.FormatDuration(elapsed)}";
            ShiftProgress.Progress = EarningsCalculator.Progress(s, now);

            ToggleButton.Text = "Завершити зміну";
            ToggleButton.BackgroundColor = Color.FromArgb("#F87171");
            ToggleButton.TextColor = Color.FromArgb("#2A0A0A");
        }
        else
        {
            ElapsedLabel.Text = "Зміна не активна";
            ShiftProgress.Progress = 0;

            ToggleButton.Text = "Почати зміну";
            ToggleButton.BackgroundColor = Color.FromArgb("#4ADE80");
            ToggleButton.TextColor = Color.FromArgb("#06210F");
        }
    }

    private void OnToggleClicked(object? sender, EventArgs e)
    {
        var s = ShiftStore.Load();

        if (s.IsRunning) ShiftManager.StopShift();
        else ShiftManager.StartShift();

        LoadHistory();
        Refresh();
    }

    private void LoadSettingsIntoUi()
    {
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

        Refresh();
    }

    private void LoadHistory()
    {
        var s = ShiftStore.Load();

        HistoryView.ItemsSource = ShiftStore.LoadLog()
            .Select(x => new
            {
                DateText = x.StartedAtUtc.ToLocalTime().ToString("dd.MM, HH:mm"),
                DetailText = $"{EarningsCalculator.FormatDuration(x.Duration)} · " +
                             $"{x.RatePerHour:0.##} {s.Currency}/год",
                AmountText = EarningsCalculator.Format(x.Earned, s.Currency)
            })
            .ToList();
    }

    // Кома і крапка як роздільник — обидві приймаються.
    private static bool TryDecimal(string? text, out decimal value) =>
        decimal.TryParse((text ?? string.Empty).Replace(',', '.'),
            NumberStyles.Any, CultureInfo.InvariantCulture, out value);

    private static bool TryDouble(string? text, out double value) =>
        double.TryParse((text ?? string.Empty).Replace(',', '.'),
            NumberStyles.Any, CultureInfo.InvariantCulture, out value);
}
