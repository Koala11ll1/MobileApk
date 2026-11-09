using ZarobitokApp.Services;

namespace ZarobitokApp.Pages;

public partial class StatsPage : ContentPage
{
    private const double ChartBarMaxHeight = 80;

    private static readonly string[] MonthNames =
    {
        "Січень", "Лютий", "Березень", "Квітень", "Травень", "Червень",
        "Липень", "Серпень", "Вересень", "Жовтень", "Листопад", "Грудень"
    };

    private static readonly string[] DayNames = { "Нд", "Пн", "Вт", "Ср", "Чт", "Пт", "Сб" };

    private bool _visible;
    private int _shownVersion = -1;
    private DateTime _shownDay;

    public StatsPage()
    {
        InitializeComponent();
        ShiftManager.Changed += () => MainThread.BeginInvokeOnMainThread(() => { if (_visible) Refresh(); });

        // Заздалегідь, поки користувач на першому екрані, а не під час першого свайпу.
        UiTiming.RunLater(TimeSpan.FromSeconds(3), () => { if (IsStale()) Refresh(); });
    }

    private bool IsStale() => _shownVersion != ShiftManager.Version || _shownDay != DateTime.Today;

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _visible = true;

        if (!IsStale()) return;
        UiTiming.RunLater(UiTiming.AfterSwipe, () => { if (_visible && IsStale()) Refresh(); });
    }

    protected override void OnDisappearing()
    {
        _visible = false;
        base.OnDisappearing();
    }

    private void Refresh()
    {
        _shownVersion = ShiftManager.Version;
        _shownDay = DateTime.Today;

        var s = ShiftStore.Load();
        var log = ShiftStore.LoadLog();
        var items = ShiftStore.LoadExtraItems();
        var ticks = ShiftStore.LoadExtraTicks();

        var today = DateTime.Today;
        var prev = today.AddMonths(-1);

        var month = StatsCalculator.ForMonth(log, items, ticks, today.Year, today.Month);
        var previous = StatsCalculator.ForMonth(log, items, ticks, prev.Year, prev.Month);

        MonthTitleLabel.Text = $"{MonthNames[today.Month - 1].ToUpperInvariant()} {today.Year}";
        MonthTotalLabel.Text = EarningsCalculator.Format(month.Total, s.Currency);

        MonthNetLabel.IsVisible = s.TaxPercent > 0;
        MonthNetLabel.Text = $"після податку {s.TaxPercent:0.##}%: " +
                             EarningsCalculator.Format(StatsCalculator.Net(month.Total, s.TaxPercent), s.Currency);

        if (s.MonthlyGoal > 0)
        {
            GoalProgress.IsVisible = true;
            GoalProgress.Progress = Math.Clamp((double)(month.Total / s.MonthlyGoal), 0, 1);
            var left = s.MonthlyGoal - month.Total;
            GoalLabel.Text = left > 0
                ? $"Ціль {EarningsCalculator.Format(s.MonthlyGoal, s.Currency)} — лишилось {EarningsCalculator.Format(left, s.Currency)} ({month.Total / s.MonthlyGoal:P0})"
                : $"Ціль {EarningsCalculator.Format(s.MonthlyGoal, s.Currency)} виконано! ({month.Total / s.MonthlyGoal:P0})";
        }
        else
        {
            GoalProgress.IsVisible = false;
            GoalLabel.Text = "Ціль на місяць можна задати в налаштуваннях";
        }

        if (previous.Total > 0)
        {
            var diff = month.Total - previous.Total;
            var sign = diff >= 0 ? "+" : "−";
            CompareLabel.Text =
                $"Минулий місяць: {EarningsCalculator.Format(previous.Total, s.Currency)} " +
                $"({sign}{EarningsCalculator.Format(Math.Abs(diff), s.Currency)}, {sign}{Math.Abs(diff / previous.Total):P0})";
        }
        else
        {
            CompareLabel.Text = "Минулого місяця даних немає";
        }

        PerHourLabel.Text = month.Shifts > 0 ? EarningsCalculator.Format(month.PerHour, s.Currency) : "—";
        PerShiftLabel.Text = month.Shifts > 0 ? EarningsCalculator.Format(month.PerShift, s.Currency) : "—";
        PerDayLabel.Text = month.ShiftDays > 0
            ? EarningsCalculator.Format(month.Total / month.ShiftDays, s.Currency)
            : "—";
        WorkedLabel.Text = EarningsCalculator.FormatDuration(month.Worked);
        CountsLabel.Text = $"{month.Shifts} / {month.ShiftDays}";

        RenderChart(StatsCalculator.LastDays(log, items, ticks, today, 7), today);
    }

    private void RenderChart(List<(DateTime Day, decimal Total)> days, DateTime today)
    {
        ChartGrid.Children.Clear();
        var max = days.Max(d => d.Total);

        for (var i = 0; i < days.Count; i++)
        {
            var (day, total) = days[i];
            var height = max > 0 ? Math.Max(2, (double)(total / max) * ChartBarMaxHeight) : 2;

            var column = new VerticalStackLayout { Spacing = 4, VerticalOptions = LayoutOptions.End };
            column.Add(new Label
            {
                Text = total > 0 ? total.ToString("0") : "",
                FontSize = 10,
                TextColor = Color.FromArgb("#9CA3AF"),
                HorizontalTextAlignment = TextAlignment.Center
            });
            column.Add(new BoxView
            {
                HeightRequest = height,
                CornerRadius = 4,
                Color = Color.FromArgb(total > 0 ? "#4ADE80" : "#1C1C26"),
                HorizontalOptions = LayoutOptions.Fill
            });
            column.Add(new Label
            {
                Text = DayNames[(int)day.DayOfWeek],
                FontSize = 11,
                FontAttributes = day == today ? FontAttributes.Bold : FontAttributes.None,
                TextColor = day == today ? Colors.White : Color.FromArgb("#6B7280"),
                HorizontalTextAlignment = TextAlignment.Center
            });

            ChartGrid.Add(column, i, 0);
        }
    }
}
