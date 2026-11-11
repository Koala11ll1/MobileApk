using ZarobitokApp.Localization;
using ZarobitokApp.Services;

namespace ZarobitokApp.Pages;

public partial class StatsPage : ContentPage
{
    private const double ChartBarMaxHeight = 80;

    private bool _visible;
    private readonly int _generation = AppTabs.Generation;
    private int _shownVersion = -1;
    private DateTime _shownDay;

    public StatsPage()
    {
        InitializeComponent();
        ShiftManager.Changed += () => MainThread.BeginInvokeOnMainThread(() => { if (_visible && _generation == AppTabs.Generation) Refresh(); });

        // Заздалегідь, поки користувач на першому екрані, а не під час першого свайпу.
        UiTiming.RunLater(TimeSpan.FromSeconds(3), () => { if (_generation == AppTabs.Generation && IsStale()) Refresh(); });

        // Колір стовпчиків графіка задається в коді.
        ThemeManager.Changed += () =>
        {
            if (_generation != AppTabs.Generation) return;
            _shownVersion = -1;
            if (_visible) Refresh();
        };
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

        MonthTitleLabel.Text = $"{L.Months[today.Month - 1].ToUpperInvariant()} {today.Year}";
        MonthTotalLabel.Text = EarningsCalculator.Format(month.Total, s.Currency);

        MonthNetLabel.IsVisible = s.TaxPercent > 0;
        MonthNetLabel.Text = L.F("після податку {0:0.##}%: {1}",
            s.TaxPercent, EarningsCalculator.Format(StatsCalculator.Net(month.Total, s.TaxPercent), s.Currency));

        if (s.MonthlyGoal > 0)
        {
            GoalProgress.IsVisible = true;
            GoalProgress.Progress = Math.Clamp((double)(month.Total / s.MonthlyGoal), 0, 1);
            var left = s.MonthlyGoal - month.Total;
            GoalLabel.Text = left > 0
                ? L.F("Ціль {0} — лишилось {1} ({2:P0})",
                    EarningsCalculator.Format(s.MonthlyGoal, s.Currency), EarningsCalculator.Format(left, s.Currency), month.Total / s.MonthlyGoal)
                : L.F("Ціль {0} виконано! ({1:P0})",
                    EarningsCalculator.Format(s.MonthlyGoal, s.Currency), month.Total / s.MonthlyGoal);
        }
        else
        {
            GoalProgress.IsVisible = false;
            GoalLabel.Text = L.T("Ціль на місяць можна задати в налаштуваннях");
        }

        if (previous.Total > 0)
        {
            var diff = month.Total - previous.Total;
            var sign = diff >= 0 ? "+" : "−";
            CompareLabel.Text = L.F("Минулий місяць: {0} ({1}{2}, {1}{3:P0})",
                EarningsCalculator.Format(previous.Total, s.Currency), sign,
                EarningsCalculator.Format(Math.Abs(diff), s.Currency), Math.Abs(diff / previous.Total));
        }
        else
        {
            CompareLabel.Text = L.T("Минулого місяця даних немає");
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

    // ---- Звіт за період (PDF) ----

    private async void OnReportClicked(object? sender, EventArgs e)
    {
        var today = DateTime.Today;
        var thisMonth = L.T("Цей місяць");
        var lastMonth = L.T("Минулий місяць");
        var thisYear = L.T("Цей рік");
        var custom = L.T("Свій період…");

        var choice = await DisplayActionSheet(L.T("Звіт за період"), L.T("Скасувати"), null, thisMonth, lastMonth, thisYear, custom);
        if (choice is null || choice == L.T("Скасувати")) return;

        (DateTime From, DateTime To) period;
        if (choice == thisMonth) period = ReportPeriods.ThisMonth(today);
        else if (choice == lastMonth) period = ReportPeriods.LastMonth(today);
        else if (choice == thisYear) period = ReportPeriods.ThisYear(today);
        else if (choice == custom)
        {
            var asked = await AskCustomPeriodAsync(today);
            if (asked is null) return;
            period = asked.Value;
        }
        else return;

        try
        {
            var s = ShiftStore.Load();
            var data = ReportBuilder.Build(
                ShiftStore.LoadLog(), ShiftStore.LoadExtraItems(), ShiftStore.LoadExtraTicks(),
                period.From, period.To, s.TaxPercent);

            if (data.ShiftCount == 0)
            {
                await DisplayAlert(L.T("Немає даних"), L.T("За цей період змін немає."), "OK");
                return;
            }

            var rows = ReportRows.Build(data, s.Currency, s.Use12Hour);
            var path = Path.Combine(FileSystem.CacheDirectory, $"zarobitok-report-{period.From:yyyy-MM-dd}_{period.To:yyyy-MM-dd}.pdf");

            await Task.Run(() => ReportExporter.WritePdf(rows, path));
            await Share.Default.RequestAsync(new ShareFileRequest(L.T("Звіт про заробіток"), new ShareFile(path, "application/pdf")));
        }
        catch (Exception ex)
        {
            await DisplayAlert(L.T("Не вдалося створити звіт"), ex.Message, "OK");
        }
    }

    private async Task<(DateTime From, DateTime To)?> AskCustomPeriodAsync(DateTime today)
    {
        var fromText = await DisplayPromptAsync(
            L.T("Початок періоду"), L.T("Дата (дд.мм.рррр)"), L.T("Далі"), L.T("Скасувати"),
            initialValue: new DateTime(today.Year, today.Month, 1).ToString("dd.MM.yyyy"));
        if (fromText is null) return null;

        var toText = await DisplayPromptAsync(
            L.T("Кінець періоду"), L.T("Дата (дд.мм.рррр)"), L.T("Створити"), L.T("Скасувати"),
            initialValue: today.ToString("dd.MM.yyyy"));
        if (toText is null) return null;

        var ok = DateTime.TryParseExact(fromText.Trim(), "dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture,
                     System.Globalization.DateTimeStyles.None, out var from)
                 & DateTime.TryParseExact(toText.Trim(), "dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture,
                     System.Globalization.DateTimeStyles.None, out var to);

        if (!ok || to < from)
        {
            await DisplayAlert(L.T("Невірні дані"), L.T("Дата має бути дд.мм.рррр, а початок — не пізніше за кінець."), "OK");
            return null;
        }

        return (from, to);
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
                FontSize = 12,
                TextColor = ThemeManager.Get("TextSecondary"),
                HorizontalTextAlignment = TextAlignment.Center
            });
            column.Add(new BoxView
            {
                HeightRequest = height,
                CornerRadius = 4,
                Color = ThemeManager.Get(total > 0 ? "Accent" : "Surface"),
                HorizontalOptions = LayoutOptions.Fill
            });
            column.Add(new Label
            {
                Text = L.DayName(day.DayOfWeek),
                FontSize = 12,
                FontAttributes = day == today ? FontAttributes.Bold : FontAttributes.None,
                TextColor = ThemeManager.Get(day == today ? "TextPrimary" : "TextMuted"),
                HorizontalTextAlignment = TextAlignment.Center
            });

            ChartGrid.Add(column, i, 0);
        }
    }
}
