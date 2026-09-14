using System.ComponentModel;
using System.Globalization;
using ZarobitokApp.Models;
using ZarobitokApp.Services;

namespace ZarobitokApp;

/// <summary>Рядок журналу — одна завершена зміна.</summary>
public sealed class ShiftRow
{
    public required string TimeText { get; init; }
    public required string DetailText { get; init; }
    public required string AmountText { get; init; }
}

/// <summary>Один день журналу з підсумком за нього.</summary>
public sealed class DayGroup : List<ShiftRow>
{
    public DayGroup(string dayText, string totalText, IEnumerable<ShiftRow> rows) : base(rows)
    {
        DayText = dayText;
        TotalText = totalText;
    }

    public string DayText { get; }
    public string TotalText { get; }
}

public partial class MainPage : ContentPage
{
    private IDispatcherTimer? _timer;
    private bool _loading;

    // Журнал кешуємо: Refresh() бігає щосекунди, і розбирати JSON
    // на кожен тик заради суми за день — марна робота.
    private List<ShiftLogEntry> _log = new();

    public MainPage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        LoadHistory();
        LoadSettingsIntoUi();

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
            EarningsCalculator.EarnedToday(s, _log, now), s.Currency);

        PerSecondLabel.Text =
            $"+{EarningsCalculator.PerSecond(s):0.0000} {s.Currency}/сек";

        if (s.IsRunning)
        {
            var elapsed = EarningsCalculator.PaidElapsed(s, now);
            ElapsedLabel.Text = $"Зміна триває {EarningsCalculator.FormatDuration(elapsed)}";
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
        var s = ShiftStore.Load();

        if (s.IsRunning) ShiftManager.StopShift();
        else ShiftManager.StartShiftAt(ToUtcStart(StartTimePicker.Time));

        LoadHistory();
        LoadSettingsIntoUi();
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

    private void LoadSettingsIntoUi()
    {
        _loading = true;
        var s = ShiftStore.Load();

        RateEntry.Text = s.RatePerHour.ToString(CultureInfo.InvariantCulture);
        CurrencyEntry.Text = s.Currency;
        PlannedEntry.Text = s.PlannedHours.ToString(CultureInfo.InvariantCulture);
        BreakEntry.Text = s.UnpaidBreakMinutes.ToString(CultureInfo.InvariantCulture);
        OvertimeEntry.Text = s.OvertimeMultiplier.ToString(CultureInfo.InvariantCulture);

        StartTimePicker.Time = s.IsRunning
            ? s.StartedAtUtc!.Value.ToLocalTime().TimeOfDay
            : DateTime.Now.TimeOfDay;

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
        _log = ShiftStore.LoadLog();

        HistoryView.ItemsSource = _log
            .GroupBy(x => x.StartedAtUtc.ToLocalTime().Date)
            .OrderByDescending(g => g.Key)
            .Select(g => new DayGroup(
                DayText(g.Key),
                EarningsCalculator.Format(g.Sum(x => x.Earned), s.Currency),
                g.OrderByDescending(x => x.StartedAtUtc)
                 .Select(x => new ShiftRow
                 {
                     TimeText = $"{x.StartedAtUtc.ToLocalTime():HH:mm} — {x.EndedAtUtc.ToLocalTime():HH:mm}",
                     DetailText = $"{EarningsCalculator.FormatDuration(x.Duration)} · " +
                                  $"{x.RatePerHour:0.##} {s.Currency}/год",
                     AmountText = EarningsCalculator.Format(x.Earned, s.Currency)
                 })))
            .ToList();
    }

    private static string DayText(DateTime day)
    {
        if (day == DateTime.Today) return "Сьогодні";
        if (day == DateTime.Today.AddDays(-1)) return "Вчора";
        return day.ToString("dd.MM.yyyy");
    }

    // Кома і крапка як роздільник — обидві приймаються.
    private static bool TryDecimal(string? text, out decimal value) =>
        decimal.TryParse((text ?? string.Empty).Replace(',', '.'),
            NumberStyles.Any, CultureInfo.InvariantCulture, out value);

    private static bool TryDouble(string? text, out double value) =>
        double.TryParse((text ?? string.Empty).Replace(',', '.'),
            NumberStyles.Any, CultureInfo.InvariantCulture, out value);
}
