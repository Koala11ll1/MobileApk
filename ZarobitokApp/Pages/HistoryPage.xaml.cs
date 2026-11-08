using ZarobitokApp.Models;
using ZarobitokApp.Services;

namespace ZarobitokApp.Pages;

/// <summary>Рядок журналу. Entry — сам запис, за ним і видаляємо.</summary>
public sealed class ShiftRow
{
    public required ShiftLogEntry Entry { get; init; }
    public required DateTime Day { get; init; }
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

/// <summary>Одна клітинка місячної сітки. Date == null — порожня клітинка-заповнювач.</summary>
public sealed class CalendarDayCell
{
    public DateTime? Date { get; init; }
    public string DayText { get; init; } = "";
    public Color BackgroundColor { get; init; } = Colors.Transparent;
    public Color TextColor { get; init; } = Colors.Transparent;
    public FontAttributes FontAttributes { get; init; } = FontAttributes.None;
}

public partial class HistoryPage : ContentPage
{
    private static readonly string[] MonthNames =
    {
        "Січень", "Лютий", "Березень", "Квітень", "Травень", "Червень",
        "Липень", "Серпень", "Вересень", "Жовтень", "Листопад", "Грудень"
    };

    private bool _showCalendar;
    private DateTime _visibleMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);

    public HistoryPage()
    {
        InitializeComponent();
        SetMode(showCalendar: false);

        // Зміна могла прийти ззовні (автостоп, ярлик, правка в деталях дня).
        ShiftManager.Changed += () => Dispatcher.Dispatch(Reload);
    }

    private void Reload()
    {
        LoadHistory();
        if (_showCalendar) RenderCalendar();
    }

    private static string Time(DateTime utc, ShiftState s)
        => EarningsCalculator.FormatTime(utc.ToLocalTime(), s.Use12Hour);

    protected override void OnAppearing()
    {
        base.OnAppearing();
        LoadHistory();
        if (_showCalendar) RenderCalendar();
    }

    private void LoadHistory()
    {
        var s = ShiftStore.Load();
        var log = ShiftStore.LoadLog();
        var items = ShiftStore.LoadExtraItems();
        var ticks = ShiftStore.LoadExtraTicks();

        // Сума за зміну = час-заробіток (Earned) + допзаробіток по її
        // діапазону, порахований наживо з тіків — це і дає можливість
        // редагувати кількість тіків для вже завершеної зміни заднім числом.
        decimal Total(ShiftLogEntry e)
            => e.Earned + EarningsCalculator.TicksTotal(items, ticks, e.StartedAtUtc, e.EndedAtUtc);

        // Групуємо за днем ЗАВЕРШЕННЯ зміни — так само, як EarnedToday
        // рахує "сьогодні". Інакше зміна через північ показувалась би
        // під іншим днем тут, ніж на вкладці «Зміна» й у віджеті.
        HistoryView.ItemsSource = log
            .GroupBy(e => e.EndedAtUtc.ToLocalTime().Date)
            .OrderByDescending(g => g.Key)
            .Select(g => new DayGroup(
                DayText(g.Key),
                EarningsCalculator.Format(g.Sum(e => Total(e)), s.Currency),
                g.OrderByDescending(e => e.StartedAtUtc)
                 .Select(e => new ShiftRow
                 {
                     Entry = e,
                     Day = g.Key,
                     TimeText = $"{Time(e.StartedAtUtc, s)} — {Time(e.EndedAtUtc, s)}",
                     DetailText = $"{EarningsCalculator.FormatDuration(e.Duration)} · " +
                                  $"{e.RatePerHour:0.##} {s.Currency}/год",
                     AmountText = EarningsCalculator.Format(Total(e), s.Currency)
                 })))
            .ToList();
    }

    private void RenderCalendar()
    {
        var s = ShiftStore.Load();
        var log = ShiftStore.LoadLog();
        var items = ShiftStore.LoadExtraItems();
        var ticks = ShiftStore.LoadExtraTicks();

        decimal Total(ShiftLogEntry e)
            => e.Earned + EarningsCalculator.TicksTotal(items, ticks, e.StartedAtUtc, e.EndedAtUtc);

        var monthEntries = log
            .Where(e =>
            {
                var d = e.EndedAtUtc.ToLocalTime().Date;
                return d.Year == _visibleMonth.Year && d.Month == _visibleMonth.Month;
            })
            .ToList();

        var shiftDays = monthEntries.Select(e => e.EndedAtUtc.ToLocalTime().Date).ToHashSet();
        var monthTotal = monthEntries.Sum(Total);

        MonthLabel.Text = $"{MonthNames[_visibleMonth.Month - 1]} {_visibleMonth.Year}";
        MonthTotalLabel.Text = $"Заробіток за місяць: {EarningsCalculator.Format(monthTotal, s.Currency)}";
        if (s.TaxPercent > 0)
            MonthTotalLabel.Text += $"\nпісля податку: {EarningsCalculator.Format(StatsCalculator.Net(monthTotal, s.TaxPercent), s.Currency)}";

        SetWeekdayHeaders(s.WeekStartsOnSunday);
        CalendarView.ItemsSource = BuildCalendarCells(_visibleMonth, shiftDays, s.WeekStartsOnSunday);
    }

    private static List<CalendarDayCell> BuildCalendarCells(DateTime month, HashSet<DateTime> shiftDays, bool sundayFirst)
    {
        var today = DateTime.Today;
        var daysInMonth = DateTime.DaysInMonth(month.Year, month.Month);

        // DayOfWeek у .NET рахує з неділі (0). Для тижня з понеділка зсуваємо
        // так, щоб понеділок став 0; для тижня з неділі беремо як є.
        var leadingBlanks = sundayFirst ? (int)month.DayOfWeek : ((int)month.DayOfWeek + 6) % 7;

        var cells = new List<CalendarDayCell>();
        for (var i = 0; i < leadingBlanks; i++)
            cells.Add(new CalendarDayCell());

        for (var day = 1; day <= daysInMonth; day++)
        {
            var date = new DateTime(month.Year, month.Month, day);
            var hasShift = shiftDays.Contains(date);
            var isToday = date == today;

            cells.Add(new CalendarDayCell
            {
                Date = date,
                DayText = day.ToString(),
                BackgroundColor = hasShift ? Color.FromArgb("#1C1C26") : Colors.Transparent,
                TextColor = hasShift
                    ? Color.FromArgb("#4ADE80")
                    : isToday ? Colors.White : Color.FromArgb("#4B5563"),
                FontAttributes = hasShift || isToday ? FontAttributes.Bold : FontAttributes.None
            });
        }

        var trailingBlanks = (7 - cells.Count % 7) % 7;
        for (var i = 0; i < trailingBlanks; i++)
            cells.Add(new CalendarDayCell());

        return cells;
    }

    private void SetWeekdayHeaders(bool sundayFirst)
    {
        string[] names = sundayFirst
            ? new[] { "Нд", "Пн", "Вт", "Ср", "Чт", "Пт", "Сб" }
            : new[] { "Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Нд" };
        var labels = new[] { Wd0, Wd1, Wd2, Wd3, Wd4, Wd5, Wd6 };
        for (var i = 0; i < 7; i++) labels[i].Text = names[i];
    }

    private async void OnAddShiftClicked(object? sender, EventArgs e)
    {
        var dateText = await DisplayPromptAsync(
            "Додати зміну", "Дата початку (дд.мм.рррр)", "Далі", "Скасувати",
            initialValue: DateTime.Today.ToString("dd.MM.yyyy"));
        if (dateText is null) return;

        var startText = await DisplayPromptAsync(
            "Початок зміни", "Година:хвилина (наприклад 09:00)", "Далі", "Скасувати");
        if (startText is null) return;

        var endText = await DisplayPromptAsync(
            "Кінець зміни", "Година:хвилина. Якщо раніше за початок — це наступна доба.", "Додати", "Скасувати");
        if (endText is null) return;

        if (!DateTime.TryParseExact(dateText.Trim(), "dd.MM.yyyy",
                System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var date)
            || !Input.TryTimeOfDay(startText, out var startTod)
            || !Input.TryTimeOfDay(endText, out var endTod))
        {
            await DisplayAlert("Невірні дані", "Дата має бути дд.мм.рррр, а час — ГГ:ХХ.", "OK");
            return;
        }

        var (startUtc, endUtc) = ShiftEditing.ResolveRange(date, startTod, endTod);
        var error = ShiftManager.AddManualShift(startUtc, endUtc);
        if (error is not null)
            await DisplayAlert("Не додано", error, "OK");
    }

    private void OnListModeClicked(object? sender, EventArgs e) => SetMode(showCalendar: false);

    private void OnCalendarModeClicked(object? sender, EventArgs e) => SetMode(showCalendar: true);

    private void SetMode(bool showCalendar)
    {
        _showCalendar = showCalendar;

        HistoryView.IsVisible = !showCalendar;
        HintLabel.IsVisible = !showCalendar;
        CalendarContainer.IsVisible = showCalendar;

        ListModeButton.BackgroundColor = Color.FromArgb(showCalendar ? "#1C1C26" : "#4ADE80");
        ListModeButton.TextColor = Color.FromArgb(showCalendar ? "#9CA3AF" : "#06210F");
        CalendarModeButton.BackgroundColor = Color.FromArgb(showCalendar ? "#4ADE80" : "#1C1C26");
        CalendarModeButton.TextColor = Color.FromArgb(showCalendar ? "#06210F" : "#9CA3AF");

        if (showCalendar) RenderCalendar();
    }

    private void OnPrevMonthClicked(object? sender, EventArgs e)
    {
        _visibleMonth = _visibleMonth.AddMonths(-1);
        RenderCalendar();
    }

    private void OnNextMonthClicked(object? sender, EventArgs e)
    {
        _visibleMonth = _visibleMonth.AddMonths(1);
        RenderCalendar();
    }

    private async void OnCalendarDayTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not CalendarDayCell { Date: { } day }) return;

        var hasShift = ShiftStore.LoadLog().Any(x => x.EndedAtUtc.ToLocalTime().Date == day);
        if (!hasShift) return;

        await OpenDayDetail(day);
    }

    private async void OnRowTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not ShiftRow row) return;
        await OpenDayDetail(row.Day);
    }

    private async Task OpenDayDetail(DateTime day)
    {
        var s = ShiftStore.Load();

        var dayEntries = ShiftStore.LoadLog()
            .Where(x => x.EndedAtUtc.ToLocalTime().Date == day)
            .ToList();
        var items = ShiftStore.LoadExtraItems();
        var ticks = ShiftStore.LoadExtraTicks();

        await Navigation.PushModalAsync(new DayDetailPage(day, s.Currency, s.Use12Hour, dayEntries, items, ticks));
    }

    private async void OnDeleteClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: ShiftRow row }) return;

        var confirmed = await DisplayAlert(
            "Видалити зміну?",
            $"{row.TimeText} · {row.AmountText}",
            "Видалити",
            "Скасувати");

        if (!confirmed) return;

        ShiftStore.RemoveLog(row.Entry);
        ShiftManager.PushWidget();
        LoadHistory();
    }

    private static string DayText(DateTime day)
    {
        if (day == DateTime.Today) return "Сьогодні";
        if (day == DateTime.Today.AddDays(-1)) return "Вчора";
        return day.ToString("dd.MM.yyyy");
    }
}
