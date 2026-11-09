using ZarobitokApp.Models;

namespace ZarobitokApp.Services;

public sealed record MonthStats(
    decimal Total,
    TimeSpan Worked,
    int Shifts,
    int ShiftDays,
    decimal PerHour,
    decimal PerShift);

/// <summary>Статистика з журналу. Зміна належить дню її ЗАВЕРШЕННЯ (як усюди в апці).</summary>
public static class StatsCalculator
{
    public static decimal ShiftTotal(ShiftLogEntry e, TicksIndex ticks)
        => e.Earned + ticks.Total(e.StartedAtUtc, e.EndedAtUtc);

    public static MonthStats ForMonth(
        IEnumerable<ShiftLogEntry> log, IEnumerable<ExtraItem> items, IEnumerable<ExtraTick> ticks,
        int year, int month)
    {
        var index = new TicksIndex(items, ticks);

        var entries = log
            .Where(e => EndLocal(e) is var d && d.Year == year && d.Month == month)
            .ToList();

        var total = entries.Sum(e => ShiftTotal(e, index));
        var worked = entries.Aggregate(TimeSpan.Zero, (sum, e) => sum + e.Duration);
        var days = entries.Select(e => EndLocal(e).Date).Distinct().Count();
        var hours = (decimal)worked.TotalHours;

        return new MonthStats(
            total,
            worked,
            entries.Count,
            days,
            hours > 0 ? total / hours : 0m,
            entries.Count > 0 ? total / entries.Count : 0m);
    }

    /// <summary>Підсумки за останні <paramref name="days"/> днів, включно з today, від старого до нового.</summary>
    public static List<(DateTime Day, decimal Total)> LastDays(
        IEnumerable<ShiftLogEntry> log, IEnumerable<ExtraItem> items, IEnumerable<ExtraTick> ticks,
        DateTime today, int days)
    {
        var index = new TicksIndex(items, ticks);

        // Групуємо журнал за днем один раз, а не фільтруємо весь журнал для кожного дня.
        var byDay = log
            .GroupBy(e => EndLocal(e).Date)
            .ToDictionary(g => g.Key, g => g.Sum(e => ShiftTotal(e, index)));

        return Enumerable.Range(0, days)
            .Select(i => today.Date.AddDays(i - (days - 1)))
            .Select(day => (day, byDay.GetValueOrDefault(day, 0m)))
            .ToList();
    }

    /// <summary>Сума після податку. Відсоток обмежений 0..100.</summary>
    public static decimal Net(decimal gross, decimal taxPercent)
        => gross * (1m - Math.Clamp(taxPercent, 0m, 100m) / 100m);

    private static DateTime EndLocal(ShiftLogEntry e) => e.EndedAtUtc.ToLocalTime();
}
