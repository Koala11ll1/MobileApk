using ZarobitokApp.Models;
using ZarobitokApp.Services;

namespace ZarobitokApp.Pages;

/// <summary>Рядок журналу. Index — позиція у збереженому списку, за нею і видаляємо.</summary>
public sealed class ShiftRow
{
    public required int Index { get; init; }
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
    }

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
            .Select((entry, index) => (entry, index))
            .GroupBy(x => x.entry.EndedAtUtc.ToLocalTime().Date)
            .OrderByDescending(g => g.Key)
            .Select(g => new DayGroup(
                DayText(g.Key),
                EarningsCalculator.Format(g.Sum(x => Total(x.entry)), s.Currency),
                g.OrderByDescending(x => x.entry.StartedAtUtc)
                 .Select(x => new ShiftRow
                 {
                     Index = x.index,
                     Day = g.Key,
                     TimeText = $"{x.entry.StartedAtUtc.ToLocalTime():HH:mm} — {x.entry.EndedAtUtc.ToLocalTime():HH:mm}",
                     DetailText = $"{EarningsCalculator.FormatDuration(x.entry.Duration)} · " +
                                  $"{x.entry.RatePerHour:0.##} {s.Currency}/год",
                     AmountText = EarningsCalculator.Format(Total(x.entry), s.Currency)
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

        CalendarView.ItemsSource = BuildCalendarCells(_visibleMonth, shiftDays);
    }

    private static List<CalendarDayCell> BuildCalendarCells(DateTime month, HashSet<DateTime> shiftDays)
    {
        var today = DateTime.Today;
        var daysInMonth = DateTime.DaysInMonth(month.Year, month.Month);

        // Тиждень починається з понеділка: DayOfWeek у .NET рахує з неділі (0),
        // цей зсув переводить понеділок у 0.
        var leadingBlanks = ((int)month.DayOfWeek + 6) % 7;

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

        await Navigation.PushModalAsync(new DayDetailPage(day, s.Currency, dayEntries, items, ticks));
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

        ShiftStore.RemoveLogAt(row.Index);
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
