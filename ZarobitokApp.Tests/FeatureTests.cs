using System.Text.Json;
using ZarobitokApp.Models;
using ZarobitokApp.Services;

namespace ZarobitokApp.Tests;

public class RateScheduleTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    // 2026-01-05 — понеділок; 2026-01-10 — субота.
    private static DateTime At(int day, int hour, int min = 0) => new(2026, 1, day, hour, min, 0, DateTimeKind.Utc);

    [Fact]
    public void Without_schedule_it_is_plain_rate_times_time()
        => Assert.Equal(300m, RateSchedule.Earned(new ShiftState { RatePerHour = 100m }, At(5, 9), At(5, 12), Utc));

    [Fact]
    public void Weekend_rate_applies_on_saturday_only()
    {
        var s = new ShiftState { RatePerHour = 100m, WeekendRate = 150m };
        Assert.Equal(100m, RateSchedule.RateAt(s, At(5, 12), Utc));
        Assert.Equal(150m, RateSchedule.RateAt(s, At(10, 12), Utc));
        Assert.Equal(150m, RateSchedule.Earned(s, At(10, 9), At(10, 10), Utc));
    }

    [Fact]
    public void Night_rate_overrides_weekend_and_spans_midnight()
    {
        var s = new ShiftState { RatePerHour = 100m, WeekendRate = 150m, NightRate = 200m, NightStartHour = 22, NightEndHour = 6 };
        Assert.Equal(200m, RateSchedule.RateAt(s, At(10, 23), Utc)); // сб, ніч
        Assert.Equal(200m, RateSchedule.RateAt(s, At(11, 3), Utc));  // нд, ніч
        Assert.Equal(150m, RateSchedule.RateAt(s, At(11, 12), Utc)); // нд, день
        Assert.Equal(100m, RateSchedule.RateAt(s, At(6, 12), Utc));  // вт, день
    }

    [Fact]
    public void Shift_crossing_a_rate_boundary_is_split_exactly()
    {
        // Пн 20:00 → вт 02:00: 2 год по 100 (20–22) + 4 год по 200 (22–02).
        var s = new ShiftState { RatePerHour = 100m, NightRate = 200m };
        Assert.Equal(1000m, RateSchedule.Earned(s, At(5, 20), At(6, 2), Utc));
    }

    [Fact]
    public void Weekday_to_weekend_boundary_is_split_at_midnight()
    {
        // Пт 22:00 → сб 02:00 без нічної: 2 год по 100 + 2 год по 150.
        var s = new ShiftState { RatePerHour = 100m, WeekendRate = 150m };
        Assert.Equal(500m, RateSchedule.Earned(s, At(9, 22), At(10, 2), Utc));
    }

    [Fact]
    public void Empty_or_reversed_range_earns_nothing()
    {
        var s = new ShiftState { RatePerHour = 100m, NightRate = 200m };
        Assert.Equal(0m, RateSchedule.Earned(s, At(5, 12), At(5, 12), Utc));
        Assert.Equal(0m, RateSchedule.Earned(s, At(5, 12), At(5, 10), Utc));
    }
}

public class ExtrasSnapshotTests
{
    private static readonly DateTime T = new(2026, 1, 5, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Tick_snapshot_price_wins_over_current_item_price()
    {
        var item = new ExtraItem(Guid.NewGuid(), "Red Bull", 100m); // ціну потім підняли до 100
        var ticks = new[]
        {
            new ExtraTick(Guid.NewGuid(), item.Id, T, +2, UnitPrice: 60m),   // старий знімок
            new ExtraTick(Guid.NewGuid(), item.Id, T, +1, UnitPrice: 100m),  // новий
        };
        Assert.Equal(220m, EarningsCalculator.TicksTotal(new[] { item }, ticks, T, T));
    }

    [Fact]
    public void Legacy_tick_without_snapshot_uses_current_price()
    {
        var item = new ExtraItem(Guid.NewGuid(), "Red Bull", 60m);
        var ticks = new[] { new ExtraTick(Guid.NewGuid(), item.Id, T, +3) };
        Assert.Equal(180m, EarningsCalculator.TicksTotal(new[] { item }, ticks, T, T));
    }

    [Fact]
    public void Snapshot_survives_even_if_the_item_is_missing_from_the_catalog()
    {
        var ticks = new[] { new ExtraTick(Guid.NewGuid(), Guid.NewGuid(), T, +2, UnitPrice: 50m) };
        Assert.Equal(100m, EarningsCalculator.TicksTotal(Array.Empty<ExtraItem>(), ticks, T, T));
    }

    [Fact]
    public void Old_json_without_the_new_fields_still_loads()
    {
        var id = Guid.NewGuid();
        var tick = JsonSerializer.Deserialize<ExtraTick>(
            $$"""{"Id":"{{Guid.NewGuid()}}","ItemId":"{{id}}","AtUtc":"2026-01-05T12:00:00Z","Delta":1}""");
        var item = JsonSerializer.Deserialize<ExtraItem>($$"""{"Id":"{{id}}","Label":"x","UnitPrice":5}""");
        var entry = JsonSerializer.Deserialize<ShiftLogEntry>(
            """{"StartedAtUtc":"2026-01-05T09:00:00Z","EndedAtUtc":"2026-01-05T17:00:00Z","RatePerHour":100,"Earned":800}""");
        var state = JsonSerializer.Deserialize<ShiftState>("""{"RatePerHour":120,"Currency":"$"}""");

        Assert.Null(tick!.UnitPrice);
        Assert.False(item!.Archived);
        Assert.Null(entry!.BreakMinutes);
        Assert.Equal(22, state!.NightStartHour);
        Assert.False(state.HasRateSchedule);
    }
}

public class StatsTests
{
    // Місцевий час, щоб тест не залежав від пояса контейнера.
    private static DateTime Local(int day, int hour) => new DateTime(2026, 3, day, hour, 0, 0, DateTimeKind.Local).ToUniversalTime();

    private static ShiftLogEntry Shift(int day, int h1, int h2, decimal rate = 100m)
        => new(Local(day, h1), Local(day, h2), rate, rate * (h2 - h1));

    [Fact]
    public void Month_stats_sum_hours_days_and_averages()
    {
        var log = new[] { Shift(2, 9, 17), Shift(2, 18, 20), Shift(5, 9, 13), new ShiftLogEntry(Local(1, 9).AddMonths(-1), Local(1, 17).AddMonths(-1), 100m, 800m) };
        var stats = StatsCalculator.ForMonth(log, Array.Empty<ExtraItem>(), Array.Empty<ExtraTick>(), 2026, 3);

        Assert.Equal(1400m, stats.Total);
        Assert.Equal(3, stats.Shifts);
        Assert.Equal(2, stats.ShiftDays);
        Assert.Equal(TimeSpan.FromHours(14), stats.Worked);
        Assert.Equal(100m, stats.PerHour);
        Assert.Equal(1400m / 3, stats.PerShift);
    }

    [Fact]
    public void Month_stats_of_an_empty_month_are_zero_not_NaN()
    {
        var stats = StatsCalculator.ForMonth(Array.Empty<ShiftLogEntry>(), Array.Empty<ExtraItem>(), Array.Empty<ExtraTick>(), 2026, 3);
        Assert.Equal(0m, stats.Total);
        Assert.Equal(0m, stats.PerHour);
        Assert.Equal(0m, stats.PerShift);
    }

    [Fact]
    public void Month_stats_include_extras()
    {
        var item = new ExtraItem(Guid.NewGuid(), "x", 10m);
        var shift = Shift(2, 9, 17);
        var ticks = new[] { new ExtraTick(Guid.NewGuid(), item.Id, shift.StartedAtUtc.AddHours(1), +5) };
        Assert.Equal(850m, StatsCalculator.ForMonth(new[] { shift }, new[] { item }, ticks, 2026, 3).Total);
    }

    [Fact]
    public void LastDays_returns_oldest_first_with_zero_for_empty_days()
    {
        var days = StatsCalculator.LastDays(new[] { Shift(4, 9, 17) }, Array.Empty<ExtraItem>(), Array.Empty<ExtraTick>(),
            new DateTime(2026, 3, 5), 3);

        Assert.Equal(new DateTime(2026, 3, 3), days[0].Day);
        Assert.Equal(new DateTime(2026, 3, 5), days[2].Day);
        Assert.Equal(new[] { 0m, 800m, 0m }, days.Select(d => d.Total).ToArray());
    }

    [Theory]
    [InlineData(1000, 18, 820)]
    [InlineData(1000, 0, 1000)]
    [InlineData(1000, 150, 0)]   // понад 100% обмежується
    [InlineData(1000, -5, 1000)] // від'ємний — ігнорується
    public void Net_applies_clamped_tax(int gross, int tax, int expected)
        => Assert.Equal(expected, StatsCalculator.Net(gross, tax));
}

public class DataExportTests
{
    private static (ShiftState, List<ShiftLogEntry>, List<ExtraItem>, List<ExtraTick>) Sample()
    {
        var item = new ExtraItem(Guid.NewGuid(), "Red Bull", 60m);
        var start = new DateTime(2026, 1, 5, 9, 0, 0, DateTimeKind.Utc);
        var entry = new ShiftLogEntry(start, start.AddHours(8), 100m, 800m, 30);
        var tick = new ExtraTick(Guid.NewGuid(), item.Id, start.AddHours(2), 2, 60m);
        return (new ShiftState { RatePerHour = 100m, Currency = "₴", NightRate = 150m }, new() { entry }, new() { item }, new() { tick });
    }

    [Fact]
    public void Backup_roundtrips_everything()
    {
        var (s, log, items, ticks) = Sample();
        var back = DataExport.FromBackupJson(DataExport.ToBackupJson(s, log, items, ticks));

        Assert.NotNull(back);
        Assert.Equal(150m, back!.State.NightRate);
        Assert.Equal(log, back.Log);
        Assert.Equal(items, back.Items);
        Assert.Equal(ticks, back.Ticks);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{"Version":99,"State":{},"Log":[],"Items":[],"Ticks":[]}""")]
    public void Bad_backup_files_are_rejected(string json)
        => Assert.Null(DataExport.FromBackupJson(json));

    [Fact]
    public void Backup_with_values_that_would_crash_the_app_is_rejected()
    {
        var (s, log, items, ticks) = Sample();
        s.PlannedHours = 1e9;
        Assert.Null(DataExport.FromBackupJson(DataExport.ToBackupJson(s, log, items, ticks)));
    }

    [Fact]
    public void Csv_has_header_and_one_invariant_formatted_row_per_shift()
    {
        var (_, log, items, ticks) = Sample();
        var lines = DataExport.ToCsv(log, items, ticks, "₴").Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(2, lines.Length);
        var cols = lines[1].TrimEnd('\r').Split(';');
        Assert.Equal(8, cols.Length);
        Assert.Equal("8.00", cols[3]);
        Assert.Equal("800.00", cols[5]);
        Assert.Equal("120.00", cols[6]);
        Assert.Equal("920.00", cols[7]);
    }

    [Fact]
    public void Csv_currency_cannot_break_columns()
    {
        var csv = DataExport.ToCsv(Array.Empty<ShiftLogEntry>(), Array.Empty<ExtraItem>(), Array.Empty<ExtraTick>(), "a;b\nc");
        Assert.Equal(8, csv.TrimEnd().Split(';').Length);
    }
}

public class ShiftEditingTests
{
    private static DateTime T(int h, int day = 5) => new(2026, 1, day, h, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Now = T(23, 20);

    private static ShiftLogEntry Entry(int h1, int h2) => new(T(h1), T(h2), 100m, 100m * (h2 - h1));

    [Fact]
    public void Valid_range_has_no_error()
        => Assert.Null(ShiftEditing.Validate(new[] { Entry(9, 12) }, T(13), T(17), Now, null));

    [Theory]
    [InlineData(10, 10)]
    [InlineData(10, 9)]
    public void End_must_be_after_start(int h1, int h2)
        => Assert.NotNull(ShiftEditing.Validate(Array.Empty<ShiftLogEntry>(), T(h1), T(h2), Now, null));

    [Fact]
    public void Future_end_and_over_48_hours_are_rejected()
    {
        Assert.NotNull(ShiftEditing.Validate(Array.Empty<ShiftLogEntry>(), T(9), Now.AddHours(1), Now, null));
        Assert.NotNull(ShiftEditing.Validate(Array.Empty<ShiftLogEntry>(), T(9, 1), T(11, 4), Now, null));
    }

    [Fact]
    public void Overlap_with_log_or_running_shift_is_rejected_but_touching_is_fine()
    {
        var log = new[] { Entry(9, 12) };
        Assert.NotNull(ShiftEditing.Validate(log, T(11), T(14), Now, null));
        Assert.Null(ShiftEditing.Validate(log, T(12), T(14), Now, null));
        Assert.NotNull(ShiftEditing.Validate(Array.Empty<ShiftLogEntry>(), T(14), T(20), Now, T(18)));
        Assert.Null(ShiftEditing.Validate(Array.Empty<ShiftLogEntry>(), T(14), T(18), Now, T(18)));
    }

    [Fact]
    public void Narrowing_a_shift_pulls_outside_ticks_to_the_new_edge_and_leaves_others_alone()
    {
        var id = Guid.NewGuid();
        var inside = new ExtraTick(Guid.NewGuid(), id, T(10), 1);
        var late = new ExtraTick(Guid.NewGuid(), id, T(16), 1);
        var other = new ExtraTick(Guid.NewGuid(), id, T(20), 1); // чужа зміна

        var moved = ShiftEditing.MoveTicks(new[] { inside, late, other }, T(9), T(17), T(9), T(14));

        Assert.Equal(T(10), moved[0].AtUtc);
        Assert.Equal(T(14), moved[1].AtUtc);
        Assert.Equal(T(20), moved[2].AtUtc);
    }

    [Fact]
    public void BreakOf_prefers_the_stored_value_and_derives_it_for_legacy_entries()
    {
        Assert.Equal(45, ShiftEditing.BreakOf(Entry(9, 17) with { BreakMinutes = 45 }));
        // 8 год, оплачено 7,5 год → перерва 30 хв
        Assert.Equal(30, ShiftEditing.BreakOf(new ShiftLogEntry(T(9), T(17), 100m, 750m)));
        Assert.Equal(0, ShiftEditing.BreakOf(new ShiftLogEntry(T(9), T(17), 0m, 0m)));
    }

    [Fact]
    public void Edited_shift_is_repriced_with_its_own_rate_and_break()
    {
        var rates = ShiftEditing.RatesFor(new ShiftState { RatePerHour = 999m, NightRate = 200m }, 100m);
        Assert.Equal(100m, rates.RatePerHour);
        Assert.Equal(200m, rates.NightRate);
        // 9:00–17:00 з перервою 60 хв → 7 год × 100
        Assert.Equal(700m, EarningsCalculator.EarnedForRange(rates, T(9), T(17), 60));
    }
}

public class ResolveRangeTests
{
    [Fact]
    public void Same_day_range_keeps_the_date()
    {
        var (s, e) = ShiftEditing.ResolveRange(new DateTime(2026, 1, 5), TimeSpan.FromHours(9), TimeSpan.FromHours(17));
        Assert.Equal(TimeSpan.FromHours(8), e - s);
    }

    [Fact]
    public void End_not_after_start_means_the_next_day()
    {
        var (s, e) = ShiftEditing.ResolveRange(new DateTime(2026, 1, 5), TimeSpan.FromHours(22), TimeSpan.FromHours(6));
        Assert.Equal(TimeSpan.FromHours(8), e - s);
        var (s2, e2) = ShiftEditing.ResolveRange(new DateTime(2026, 1, 5), TimeSpan.FromHours(9), TimeSpan.FromHours(9));
        Assert.Equal(TimeSpan.FromHours(24), e2 - s2);
    }
}

public class InputTests
{
    [Theory]
    [InlineData("12,5", 12.5)]
    [InlineData(" 12.5 ", 12.5)]
    [InlineData("100", 100)]
    public void Decimal_accepts_comma_and_dot(string text, double expected)
    {
        Assert.True(Input.TryDecimal(text, out var v));
        Assert.Equal((decimal)expected, v);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("1e5")]
    public void Decimal_rejects_garbage_and_exponents(string text)
        => Assert.False(Input.TryDecimal(text, out _));

    [Theory]
    [InlineData("09:30", 9, 30)]
    [InlineData("9:05", 9, 5)]
    [InlineData("9.05", 9, 5)]
    [InlineData("23:59", 23, 59)]
    public void Time_parses_common_forms(string text, int h, int m)
    {
        Assert.True(Input.TryTimeOfDay(text, out var t));
        Assert.Equal(new TimeSpan(h, m, 0), t);
    }

    [Theory]
    [InlineData("24:00")]
    [InlineData("9:60")]
    [InlineData("nine")]
    [InlineData("")]
    public void Time_rejects_invalid(string text)
        => Assert.False(Input.TryTimeOfDay(text, out _));
}

public class NextReminderTests
{
    [Fact]
    public void Later_today_when_the_time_has_not_come_yet()
        => Assert.Equal(new DateTime(2026, 1, 5, 9, 0, 0),
            ShiftEditing.NextReminder(new DateTime(2026, 1, 5, 7, 30, 0), 9 * 60));

    [Theory]
    [InlineData(9, 0)]    // рівно в цей момент — вже минуло, тож завтра
    [InlineData(18, 45)]
    public void Tomorrow_when_the_time_has_passed(int h, int m)
        => Assert.Equal(new DateTime(2026, 1, 6, 9, 0, 0),
            ShiftEditing.NextReminder(new DateTime(2026, 1, 5, h, m, 0), 9 * 60));
}

public class TicksIndexTests
{
    [Fact]
    public void Index_matches_the_straightforward_calculation_on_random_data()
    {
        var rnd = new Random(42);
        var items = Enumerable.Range(0, 5).Select(i => new ExtraItem(Guid.NewGuid(), $"i{i}", rnd.Next(1, 200))).ToList();
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var ticks = Enumerable.Range(0, 500).Select(_ =>
        {
            var item = items[rnd.Next(items.Count)];
            var price = rnd.Next(3) == 0 ? (decimal?)null : rnd.Next(1, 300);   // частина зі знімком, частина "стара"
            return new ExtraTick(Guid.NewGuid(), item.Id, t0.AddMinutes(rnd.Next(0, 60 * 24 * 30)), rnd.Next(-1, 3), price);
        })
        // тік із ціною невідомого типу та тік рівно на межі
        .Append(new ExtraTick(Guid.NewGuid(), Guid.NewGuid(), t0.AddHours(5), 2))
        .Append(new ExtraTick(Guid.NewGuid(), items[0].Id, t0.AddHours(10), 1, 7m))
        .ToList();

        var index = new TicksIndex(items, ticks);

        for (var i = 0; i < 200; i++)
        {
            var from = t0.AddMinutes(rnd.Next(0, 60 * 24 * 30));
            var to = from.AddMinutes(rnd.Next(0, 60 * 24 * 3));
            Assert.Equal(EarningsCalculator.TicksTotal(items, ticks, from, to), index.Total(from, to));
        }

        // межі включені з обох боків
        Assert.Equal(EarningsCalculator.TicksTotal(items, ticks, t0.AddHours(10), t0.AddHours(10)),
                     index.Total(t0.AddHours(10), t0.AddHours(10)));
    }

    [Fact]
    public void Empty_index_returns_zero()
        => Assert.Equal(0m, new TicksIndex(Array.Empty<ExtraItem>(), Array.Empty<ExtraTick>())
            .Total(DateTime.MinValue, DateTime.MaxValue));
}
