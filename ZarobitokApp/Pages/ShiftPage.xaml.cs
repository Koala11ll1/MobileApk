using System.ComponentModel;
using ZarobitokApp.Models;
using ZarobitokApp.Services;

namespace ZarobitokApp.Pages;

public partial class ShiftPage : ContentPage
{
    private IDispatcherTimer? _timer;
    private bool _loading;

    // Журнал і допзаробітки кешуємо: Refresh() бігає щосекунди, а розбирати
    // JSON на кожен тик заради підсумку за день — марна робота. Кеш
    // оновлюється при відкритті сторінки й одразу після старту/стопу зміни.
    private List<ShiftLogEntry> _log = new();
    private List<ExtraEarning> _extras = new();

    public ShiftPage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        _log = ShiftStore.LoadLog();
        _extras = ShiftStore.LoadExtras();
        LoadStartTimeIntoUi();

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
        var today = DateTime.Today;

        var thisShift = EarningsCalculator.EarnedThisShiftWithExtras(s, _extras, now);
        var paid = EarningsCalculator.PaidElapsed(s, now);

        AmountLabel.Text = EarningsCalculator.Format(
            EarningsCalculator.EarnedToday(s, _log, _extras, now), s.Currency);

        PerSecondLabel.Text =
            $"+{EarningsCalculator.PerSecond(s):0.0000} {s.Currency}/сек";

        ThisShiftLabel.Text = EarningsCalculator.Format(thisShift, s.Currency);
        PaidTimeLabel.Text = EarningsCalculator.FormatDuration(paid);

        var planned = TimeSpan.FromHours(s.PlannedHours);
        RemainingLabel.Text = !s.IsRunning
            ? "—"
            : paid >= planned
                ? "план виконано"
                : EarningsCalculator.FormatDuration(planned - paid);

        var todayEntries = _log.Where(e => e.EndedAtUtc.ToLocalTime().Date == today).ToList();
        TodayLabel.Text = EarningsCalculator.Format(
            EarningsCalculator.EarnedToday(s, _log, _extras, now), s.Currency);
        TodayCountLabel.Text = todayEntries.Count.ToString();

        var todayWorked = todayEntries.Aggregate(TimeSpan.Zero, (sum, e) => sum + e.Duration) + paid;
        TodayHoursLabel.Text = EarningsCalculator.FormatDuration(todayWorked);

        RateLabel.Text = $"{s.RatePerHour:0.##} {s.Currency}/год";

        if (s.IsRunning)
        {
            ElapsedLabel.Text = $"Зміна триває {EarningsCalculator.FormatDuration(paid)}";
            ShiftProgress.Progress = EarningsCalculator.Progress(s, now);

            StartHintLabel.Text = "Можна пересунути, якщо натиснув не вчасно";

            ToggleButton.Text = "Завершити зміну";
            ToggleButton.BackgroundColor = Color.FromArgb("#F87171");
            ToggleButton.TextColor = Color.FromArgb("#2A0A0A");
        }
        else
        {
            ElapsedLabel.Text = "Зміна не активна";
            ShiftProgress.Progress = 0;

            StartHintLabel.Text = "Зміна почнеться з цього часу";

            ToggleButton.Text = "Почати зміну";
            ToggleButton.BackgroundColor = Color.FromArgb("#4ADE80");
            ToggleButton.TextColor = Color.FromArgb("#06210F");
        }
    }

    private void OnToggleClicked(object? sender, EventArgs e)
    {
        if (ShiftStore.Load().IsRunning) ShiftManager.StopShift();
        else ShiftManager.StartShiftAt(ToUtcStart(StartTimePicker.Time));

        _log = ShiftStore.LoadLog();
        _extras = ShiftStore.LoadExtras();
        LoadStartTimeIntoUi();
        Refresh();
    }

    private void OnStartTimeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_loading || e.PropertyName != TimePicker.TimeProperty.PropertyName) return;

        // Поки зміна не запущена, пікер — просто заготовка на майбутній старт.
        if (!ShiftStore.Load().IsRunning) return;

        ShiftManager.SetShiftStart(ToUtcStart(StartTimePicker.Time));
        Refresh();
    }

    /// <summary>
    /// Пікер дає лише час доби. Якщо він сьогодні ще не настав —
    /// значить зміна почалась учора: так працює нічна зміна.
    /// </summary>
    private static DateTime ToUtcStart(TimeSpan? timeOfDay)
    {
        var local = DateTime.Today + (timeOfDay ?? DateTime.Now.TimeOfDay);
        if (local > DateTime.Now) local = local.AddDays(-1);
        return local.ToUniversalTime();
    }

    private void LoadStartTimeIntoUi()
    {
        _loading = true;
        var s = ShiftStore.Load();

        StartTimePicker.Time = s.IsRunning
            ? s.StartedAtUtc!.Value.ToLocalTime().TimeOfDay
            : DateTime.Now.TimeOfDay;

        _loading = false;
    }
}
