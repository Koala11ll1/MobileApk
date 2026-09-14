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

public partial class HistoryPage : ContentPage
{
    public HistoryPage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        LoadHistory();
    }

    private void LoadHistory()
    {
        var s = ShiftStore.Load();
        var log = ShiftStore.LoadLog();

        // Групуємо за днем ЗАВЕРШЕННЯ зміни — так само, як EarnedToday
        // рахує "сьогодні". Інакше зміна через північ показувалась би
        // під іншим днем тут, ніж на вкладці «Зміна» й у віджеті.
        HistoryView.ItemsSource = log
            .Select((entry, index) => (entry, index))
            .GroupBy(x => x.entry.EndedAtUtc.ToLocalTime().Date)
            .OrderByDescending(g => g.Key)
            .Select(g => new DayGroup(
                DayText(g.Key),
                EarningsCalculator.Format(g.Sum(x => x.entry.Earned), s.Currency),
                g.OrderByDescending(x => x.entry.StartedAtUtc)
                 .Select(x => new ShiftRow
                 {
                     Index = x.index,
                     Day = g.Key,
                     TimeText = $"{x.entry.StartedAtUtc.ToLocalTime():HH:mm} — {x.entry.EndedAtUtc.ToLocalTime():HH:mm}",
                     DetailText = $"{EarningsCalculator.FormatDuration(x.entry.Duration)} · " +
                                  $"{x.entry.RatePerHour:0.##} {s.Currency}/год",
                     AmountText = EarningsCalculator.Format(x.entry.Earned, s.Currency)
                 })))
            .ToList();
    }

    private async void OnRowTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not ShiftRow row) return;

        var s = ShiftStore.Load();
        var day = row.Day;

        var dayEntries = ShiftStore.LoadLog()
            .Where(x => x.EndedAtUtc.ToLocalTime().Date == day)
            .ToList();
        var dayExtras = ShiftStore.LoadExtras()
            .Where(x => x.AtUtc.ToLocalTime().Date == day)
            .ToList();

        await Navigation.PushModalAsync(new DayDetailPage(day, s.Currency, dayEntries, dayExtras));
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
