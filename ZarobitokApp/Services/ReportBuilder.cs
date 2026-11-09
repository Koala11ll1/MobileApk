using ZarobitokApp.Localization;
using ZarobitokApp.Models;

namespace ZarobitokApp.Services;

public sealed record ReportShift(
    DateTime StartLocal, DateTime EndLocal, TimeSpan Duration, decimal Rate, decimal TimePay, decimal Extras)
{
    public decimal Total => TimePay + Extras;
}

public sealed record ReportDay(DateTime Day, IReadOnlyList<ReportShift> Shifts)
{
    public TimeSpan Worked => Shifts.Aggregate(TimeSpan.Zero, (s, x) => s + x.Duration);
    public decimal TimePay => Shifts.Sum(x => x.TimePay);
    public decimal Extras => Shifts.Sum(x => x.Extras);
    public decimal Total => TimePay + Extras;
}

public sealed record ReportExtra(string Label, int Count, decimal Amount);

public sealed record ReportData(
    DateTime From, DateTime To,
    IReadOnlyList<ReportDay> Days,
    IReadOnlyList<ReportExtra> Extras,
    decimal TaxPercent)
{
    public int ShiftCount => Days.Sum(d => d.Shifts.Count);
    public int WorkDays => Days.Count;
    public TimeSpan Worked => Days.Aggregate(TimeSpan.Zero, (s, d) => s + d.Worked);
    public decimal TimePay => Days.Sum(d => d.TimePay);
    public decimal ExtrasPay => Days.Sum(d => d.Extras);
    public decimal Total => TimePay + ExtrasPay;
    public decimal Net => StatsCalculator.Net(Total, TaxPercent);
    public decimal PerHour => Worked.TotalHours > 0 ? Total / (decimal)Worked.TotalHours : 0m;
}

/// <summary>Збирає дані звіту за період. Зміна належить дню її завершення (як усюди в апці). Без залежностей від MAUI.</summary>
public static class ReportBuilder
{
    /// <param name="fromDate">Перший день періоду (місцева дата, включно).</param>
    /// <param name="toDate">Останній день періоду (місцева дата, включно).</param>
    public static ReportData Build(
        IEnumerable<ShiftLogEntry> log, IEnumerable<ExtraItem> items, IEnumerable<ExtraTick> ticks,
        DateTime fromDate, DateTime toDate, decimal taxPercent)
    {
        var from = fromDate.Date;
        var to = toDate.Date;

        var itemList = items.ToList();
        var tickList = ticks.OrderBy(t => t.AtUtc).ToList();
        var names = itemList.ToDictionary(i => i.Id, i => i.Label);
        var prices = itemList.ToDictionary(i => i.Id, i => i.UnitPrice);

        var extrasByType = new Dictionary<Guid, (int Count, decimal Amount)>();

        var days = log
            .Where(e => e.EndedAtUtc.ToLocalTime().Date >= from && e.EndedAtUtc.ToLocalTime().Date <= to)
            .OrderBy(e => e.StartedAtUtc)
            .GroupBy(e => e.EndedAtUtc.ToLocalTime().Date)
            .OrderBy(g => g.Key)
            .Select(g => new ReportDay(g.Key, g.Select(e =>
            {
                var extras = 0m;
                foreach (var t in tickList.Where(t => t.AtUtc >= e.StartedAtUtc && t.AtUtc <= e.EndedAtUtc))
                {
                    var value = EarningsCalculator.TickValue(t, prices);
                    extras += value;

                    var (count, amount) = extrasByType.GetValueOrDefault(t.ItemId);
                    extrasByType[t.ItemId] = (count + t.Delta, amount + value);
                }

                return new ReportShift(
                    e.StartedAtUtc.ToLocalTime(), e.EndedAtUtc.ToLocalTime(), e.Duration, e.RatePerHour, e.Earned, extras);
            }).ToList()))
            .ToList();

        var extraRows = extrasByType
            .Where(kv => kv.Value.Count != 0 || kv.Value.Amount != 0)
            .Select(kv => new ReportExtra(names.GetValueOrDefault(kv.Key) ?? L.T("Невідомий тип"), kv.Value.Count, kv.Value.Amount))
            .OrderByDescending(x => x.Amount)
            .ToList();

        return new ReportData(from, to, days, extraRows, taxPercent);
    }
}

/// <summary>Готові періоди для звіту.</summary>
public static class ReportPeriods
{
    public static (DateTime From, DateTime To) Month(int year, int month)
        => (new DateTime(year, month, 1), new DateTime(year, month, DateTime.DaysInMonth(year, month)));

    public static (DateTime From, DateTime To) ThisMonth(DateTime today) => Month(today.Year, today.Month);

    public static (DateTime From, DateTime To) LastMonth(DateTime today)
    {
        var prev = today.Date.AddDays(1 - today.Day).AddMonths(-1);   // 1-ше число минулого місяця
        return Month(prev.Year, prev.Month);
    }

    public static (DateTime From, DateTime To) ThisYear(DateTime today)
        => (new DateTime(today.Year, 1, 1), new DateTime(today.Year, 12, 31));
}
