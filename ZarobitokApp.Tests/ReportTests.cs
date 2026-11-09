using ZarobitokApp.Localization;
using ZarobitokApp.Models;
using ZarobitokApp.Services;

namespace ZarobitokApp.Tests;

public class ReportTests : IDisposable
{
    public ReportTests() => L.Use("uk");
    public void Dispose() => L.Use("uk");

    // Місцевий час, щоб тест не залежав від пояса контейнера.
    private static DateTime Local(int month, int day, int hour) => new DateTime(2026, month, day, hour, 0, 0, DateTimeKind.Local).ToUniversalTime();

    private static ShiftLogEntry Shift(int month, int day, int h1, int h2, decimal rate = 100m)
        => new(Local(month, day, h1), Local(month, day, h2), rate, rate * (h2 - h1));

    private static readonly ExtraItem Drink = new(Guid.NewGuid(), "Red Bull", 60m);

    [Fact]
    public void Includes_only_shifts_ending_inside_the_period_and_groups_by_day()
    {
        var log = new[] { Shift(3, 2, 9, 12), Shift(3, 2, 14, 18), Shift(3, 5, 9, 17), Shift(2, 28, 9, 17), Shift(4, 1, 9, 17) };
        var d = ReportBuilder.Build(log, new[] { Drink }, Array.Empty<ExtraTick>(), new DateTime(2026, 3, 1), new DateTime(2026, 3, 31), 0);

        Assert.Equal(3, d.ShiftCount);
        Assert.Equal(2, d.WorkDays);
        Assert.Equal(2, d.Days[0].Shifts.Count);
        Assert.Equal(TimeSpan.FromHours(15), d.Worked);
        Assert.Equal(1500m, d.Total);
        Assert.Equal(100m, d.PerHour);
    }

    [Fact]
    public void Period_bounds_are_inclusive_on_both_days()
    {
        var log = new[] { Shift(3, 1, 9, 17), Shift(3, 31, 9, 17) };
        var d = ReportBuilder.Build(log, Array.Empty<ExtraItem>(), Array.Empty<ExtraTick>(), new DateTime(2026, 3, 1), new DateTime(2026, 3, 31), 0);
        Assert.Equal(2, d.ShiftCount);
    }

    [Fact]
    public void Extras_are_summed_per_shift_and_per_type_using_snapshot_prices()
    {
        var s = Shift(3, 2, 9, 17);
        var other = new ExtraItem(Guid.NewGuid(), "Chips", 20m);
        var ticks = new[]
        {
            new ExtraTick(Guid.NewGuid(), Drink.Id, s.StartedAtUtc.AddHours(1), +2, UnitPrice: 50m),  // знімок: 50, не 60
            new ExtraTick(Guid.NewGuid(), Drink.Id, s.StartedAtUtc.AddHours(2), -1),                  // старий тік → поточна ціна 60
            new ExtraTick(Guid.NewGuid(), other.Id, s.StartedAtUtc.AddHours(3), +3),
            new ExtraTick(Guid.NewGuid(), Drink.Id, s.EndedAtUtc.AddDays(5), +9),                      // поза зміною
        };

        var d = ReportBuilder.Build(new[] { s }, new[] { Drink, other }, ticks, new DateTime(2026, 3, 1), new DateTime(2026, 3, 31), 0);

        Assert.Equal(100m, d.ExtrasPay);   // 2×50 − 1×60 + 3×20 = 100 − 60 + 60
        Assert.Equal(2, d.Extras.Count);
        var drink = d.Extras.Single(x => x.Label == "Red Bull");
        Assert.Equal(1, drink.Count);
        Assert.Equal(40m, drink.Amount);          // 100 − 60
        Assert.Equal(60m, d.Extras.Single(x => x.Label == "Chips").Amount);
    }

    [Fact]
    public void Tax_gives_a_net_amount()
    {
        var d = ReportBuilder.Build(new[] { Shift(3, 2, 9, 17) }, Array.Empty<ExtraItem>(), Array.Empty<ExtraTick>(), new DateTime(2026, 3, 1), new DateTime(2026, 3, 31), 18);
        Assert.Equal(800m, d.Total);
        Assert.Equal(656m, d.Net);
    }

    [Fact]
    public void Periods_cover_whole_months_and_years_including_leap_february()
    {
        Assert.Equal((new DateTime(2026, 3, 1), new DateTime(2026, 3, 31)), ReportPeriods.ThisMonth(new DateTime(2026, 3, 15)));
        Assert.Equal((new DateTime(2026, 2, 1), new DateTime(2026, 2, 28)), ReportPeriods.LastMonth(new DateTime(2026, 3, 31)));
        Assert.Equal((new DateTime(2024, 2, 1), new DateTime(2024, 2, 29)), ReportPeriods.LastMonth(new DateTime(2024, 3, 1)));
        Assert.Equal((new DateTime(2025, 12, 1), new DateTime(2025, 12, 31)), ReportPeriods.LastMonth(new DateTime(2026, 1, 20)));
        Assert.Equal((new DateTime(2026, 1, 1), new DateTime(2026, 12, 31)), ReportPeriods.ThisYear(new DateTime(2026, 6, 6)));
    }

    [Fact]
    public void Rows_contain_summary_table_totals_and_extras()
    {
        var s = Shift(3, 2, 9, 17);
        var ticks = new[] { new ExtraTick(Guid.NewGuid(), Drink.Id, s.StartedAtUtc.AddHours(1), +2) };
        var d = ReportBuilder.Build(new[] { s, Shift(3, 2, 18, 20) }, new[] { Drink }, ticks, new DateTime(2026, 3, 1), new DateTime(2026, 3, 31), 0);

        var rows = ReportRows.Build(d, "₴", use12Hour: false);
        var text = string.Join("\n", rows.Select(r => string.Join(" | ", r.Cells)));

        Assert.Equal(RowKind.Title, rows[0].Kind);
        Assert.Contains("Звіт про заробіток", text);
        Assert.Contains("01.03.2026 — 31.03.2026", text);
        Assert.Contains("1120.00 ₴", text);                 // 800 + 200 + 120 допзаробітку
        Assert.Contains("09:00–17:00", text);
        Assert.Contains("Разом за день", text);              // у дня дві зміни → підсумок дня
        Assert.Contains("Допзаробіток по видах", text);
        Assert.Contains("Red Bull | 2 | 120.00 ₴", text);
        Assert.Equal(RowKind.TableTotal, rows.Last(r => r.Kind == RowKind.TableTotal).Kind);
    }

    [Fact]
    public void Table_rows_have_matching_weights_and_cells()
    {
        var d = ReportBuilder.Build(new[] { Shift(3, 2, 9, 17) }, new[] { Drink }, Array.Empty<ExtraTick>(), new DateTime(2026, 3, 1), new DateTime(2026, 3, 31), 0);
        foreach (var r in ReportRows.Build(d, "₴", false).Where(r => r.Weights is not null))
            Assert.Equal(r.Weights!.Length, r.Cells.Length);
    }

    [Fact]
    public void Empty_period_says_so_instead_of_printing_empty_tables()
    {
        var d = ReportBuilder.Build(Array.Empty<ShiftLogEntry>(), Array.Empty<ExtraItem>(), Array.Empty<ExtraTick>(), new DateTime(2026, 3, 1), new DateTime(2026, 3, 31), 0);
        var rows = ReportRows.Build(d, "₴", false);

        Assert.Contains(rows, r => r.Kind == RowKind.Body && r.Cells[0] == "За цей період змін немає.");
        Assert.DoesNotContain(rows, r => r.Kind == RowKind.TableHeader);
    }

    [Fact]
    public void Report_text_follows_the_language()
    {
        L.Use("en");
        var d = ReportBuilder.Build(new[] { Shift(3, 2, 9, 17) }, Array.Empty<ExtraItem>(), Array.Empty<ExtraTick>(), new DateTime(2026, 3, 1), new DateTime(2026, 3, 31), 0);
        var text = string.Join("\n", ReportRows.Build(d, "$", true).Select(r => string.Join(" | ", r.Cells)));

        Assert.Contains("Earnings report", text);
        Assert.Contains("9:00 AM–5:00 PM", text);
        Assert.DoesNotContain("Звіт", text);
    }
}
