using ZarobitokApp.Localization;
using System.ComponentModel;
using ZarobitokApp.Models;
using ZarobitokApp.Services;

namespace ZarobitokApp.Pages;

public partial class ShiftPage : ContentPage
{
    private IDispatcherTimer? _timer;
    private bool _loading;

    // Журнал, типи допзаробітку й тіки кешуємо: Refresh() бігає щосекунди,
    // а розбирати JSON на кожен тик заради підсумку за день — марна робота.
    // Кеш оновлюється при відкритті сторінки й одразу після старту/стопу зміни.
    private List<ShiftLogEntry> _log = new();
    private List<ExtraItem> _items = new();
    private List<ExtraTick> _ticks = new();

    private bool _visible;
    private readonly int _generation = AppTabs.Generation;

    public ShiftPage()
    {
        InitializeComponent();

        // Зміну могли почати/завершити ззовні (ярлик, автостоп, правка журналу) —
        // кеш має це підхопити. Екран перемальовуємо лише коли він на виду,
        // щоб не оживляти таймер на прихованій вкладці.
        ShiftManager.Changed += () => MainThread.BeginInvokeOnMainThread(OnDataChanged);
    }

    private int _cacheVersion = -1;

    private void ReloadCaches()
    {
        _log = ShiftStore.LoadLog();
        _items = ShiftStore.LoadExtraItems();
        _ticks = ShiftStore.LoadExtraTicks();
        _cacheVersion = ShiftManager.Version;
    }

    private void OnDataChanged()
    {
        // Прихований екран нічого не перечитує — це б вкрало кадри в того, що зараз на виду.
        // Кеш застаріє за версією, і OnAppearing його оновить.
        if (!_visible || _generation != AppTabs.Generation) return;

        ReloadCaches();
        LoadStartTimeIntoUi();
        Refresh();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _visible = true;

        if (_cacheVersion != ShiftManager.Version) ReloadCaches();
        LoadStartTimeIntoUi();

        Refresh();
    }

    protected override void OnDisappearing()
    {
        _visible = false;
        _timer?.Stop();
        _timer = null;
        base.OnDisappearing();
    }

    /// <summary>
    /// Секундний тікер потрібен лише поки зміна триває — цифра оновлюється
    /// щосекунди тільки тоді. Коли зміна не активна, простій екран нічого
    /// не показує, що б мінялось раз на секунду, тож таймер просто гаситься:
    /// це прибирає фонову роботу (парсинг, LINQ) саме тоді, коли вона нікому
    /// не потрібна, а конкурує за UI-потік зі свайпом між вкладками.
    /// </summary>
    private void SyncTicking(bool running)
    {
        if (running)
        {
            if (_timer is not null) return;

            _timer = Dispatcher.CreateTimer();
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick += (_, _) =>
            {
                if (_generation != AppTabs.Generation) { _timer?.Stop(); _timer = null; return; }
                Refresh();
            };
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
        // Якщо план щойно вийшов — зміна автоматично зупиниться тут,
        // і журнал (з новим записом) та пікер треба перечитати заново,
        // інакше кеш лишиться застарілим до наступного відкриття сторінки.
        if (ShiftManager.CheckAutoStop())
        {
            _log = ShiftStore.LoadLog();
            LoadStartTimeIntoUi();
        }

        var s = ShiftStore.Load();
        var now = DateTime.UtcNow;
        var today = DateTime.Today;

        var thisShift = EarningsCalculator.EarnedThisShiftWithExtras(s, _items, _ticks, now);
        var paid = EarningsCalculator.PaidElapsed(s, now);

        // Рахуємо один раз і на AmountLabel, і на TodayLabel — це та сама
        // сума, а TicksTotal усередині неї небезкоштовна (LINQ + словник).
        var todayTotalText = EarningsCalculator.Format(
            EarningsCalculator.EarnedToday(s, _log, _items, _ticks, now), s.Currency);

        AmountLabel.Text = todayTotalText;

        PerSecondLabel.Text =
            $"+{EarningsCalculator.PerSecond(s, now):0.0000} {s.Currency}/{L.T("сек")}";

        ThisShiftLabel.Text = EarningsCalculator.Format(thisShift, s.Currency);
        PaidTimeLabel.Text = EarningsCalculator.FormatDuration(paid);

        // Поки зміна йде, paid < planned завжди: щойно вони зрівняються,
        // CheckAutoStop() вище вже встиг зупинити зміну на цьому ж тику.
        var planned = TimeSpan.FromHours(s.PlannedHours);
        RemainingLabel.Text = s.IsRunning
            ? EarningsCalculator.FormatDuration(planned - paid)
            : "—";

        var todayEntries = _log.Where(e => e.EndedAtUtc.ToLocalTime().Date == today).ToList();
        TodayLabel.Text = todayTotalText;
        TodayCountLabel.Text = todayEntries.Count.ToString();

        var todayWorked = todayEntries.Aggregate(TimeSpan.Zero, (sum, e) => sum + e.Duration) + paid;
        TodayHoursLabel.Text = EarningsCalculator.FormatDuration(todayWorked);

        RateLabel.Text = s.HasRateSchedule
            ? L.F("{0:0.##} {1}/год (зараз)", RateSchedule.RateAt(s, now), s.Currency)
            : $"{s.RatePerHour:0.##} {s.Currency}/{L.T("год")}";

        TodayNetRow.IsVisible = s.TaxPercent > 0;
        if (s.TaxPercent > 0)
            TodayNetLabel.Text = EarningsCalculator.Format(
                StatsCalculator.Net(EarningsCalculator.EarnedToday(s, _log, _items, _ticks, now), s.TaxPercent), s.Currency);

        if (s.IsRunning)
        {
            ElapsedLabel.Text = L.F("Зміна триває {0}", EarningsCalculator.FormatDuration(paid));
            ShiftProgress.Progress = EarningsCalculator.Progress(s, now);

            StartHintLabel.Text = L.T("Можна пересунути, якщо натиснув не вчасно");

            ToggleButton.Text = L.T("Завершити зміну");
            ToggleButton.BackgroundColor = ThemeManager.Get("Danger");
            ToggleButton.TextColor = ThemeManager.Get("DangerOn");
        }
        else
        {
            ElapsedLabel.Text = L.T("Зміна не активна");
            ShiftProgress.Progress = 0;

            StartHintLabel.Text = L.T("Зміна почнеться з цього часу");

            ToggleButton.Text = L.T("Почати зміну");
            ToggleButton.BackgroundColor = ThemeManager.Get("Accent");
            ToggleButton.TextColor = ThemeManager.Get("AccentOn");
        }

        SyncTicking(s.IsRunning);
    }

    private void OnToggleClicked(object? sender, EventArgs e)
    {
        if (ShiftStore.Load().IsRunning) ShiftManager.StopShift();
        else ShiftManager.StartShiftAt(ToUtcStart(StartTimePicker.Time));

        ReloadCaches();
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
        StartTimePicker.Format = s.Use12Hour ? "h:mm tt" : "HH:mm";

        StartTimePicker.Time = s.IsRunning
            ? s.StartedAtUtc!.Value.ToLocalTime().TimeOfDay
            : DateTime.Now.TimeOfDay;

        _loading = false;
    }
}
