using ZarobitokApp.Models;

namespace ZarobitokApp.Services;

/// <summary>
/// Уся математика зібрана тут і не залежить від Android/MAUI —
/// цей клас можна покрити юніт-тестами.
/// </summary>
public static class EarningsCalculator
{
    /// <summary>Оплачуваний час зміни на момент nowUtc (перерва вже віднята).</summary>
    public static TimeSpan PaidElapsed(ShiftState s, DateTime nowUtc)
    {
        if (s.StartedAtUtc is null) return TimeSpan.Zero;

        var raw = nowUtc - s.StartedAtUtc.Value;
        var paid = raw - TimeSpan.FromMinutes(s.UnpaidBreakMinutes);
        return paid < TimeSpan.Zero ? TimeSpan.Zero : paid;
    }

    /// <summary>Скільки набігло за поточну зміну.</summary>
    public static decimal EarnedThisShift(ShiftState s, DateTime nowUtc)
    {
        var paid = PaidElapsed(s, nowUtc);
        if (paid <= TimeSpan.Zero) return 0m;

        var perSecond = s.RatePerHour / 3600m;
        var plannedSeconds = (decimal)(s.PlannedHours * 3600);
        var totalSeconds = (decimal)paid.TotalSeconds;

        if (totalSeconds <= plannedSeconds || s.OvertimeMultiplier == 1.0m)
            return totalSeconds * perSecond;

        var overtimeSeconds = totalSeconds - plannedSeconds;
        return plannedSeconds * perSecond
             + overtimeSeconds * perSecond * s.OvertimeMultiplier;
    }

    /// <summary>
    /// Сума тіків допзаробітку в проміжку [fromUtc, toUtc], за поточними
    /// цінами типів. Ціна типу не має власної історії — якщо колись
    /// знадобиться редагувати ціну заднім числом, це вплине на всі тіки.
    /// </summary>
    public static decimal TicksTotal(
        IEnumerable<ExtraItem> items, IEnumerable<ExtraTick> ticks, DateTime fromUtc, DateTime toUtc)
    {
        var priceById = items.ToDictionary(i => i.Id, i => i.UnitPrice);
        return ticks
            .Where(t => t.AtUtc >= fromUtc && t.AtUtc <= toUtc)
            .Sum(t => t.Delta * priceById.GetValueOrDefault(t.ItemId, 0m));
    }

    /// <summary>Скільки набігло за поточну зміну разом із допзаробітками.</summary>
    public static decimal EarnedThisShiftWithExtras(
        ShiftState s, IEnumerable<ExtraItem> items, IEnumerable<ExtraTick> ticks, DateTime nowUtc)
    {
        if (s.StartedAtUtc is null) return 0m;
        return EarnedThisShift(s, nowUtc) + TicksTotal(items, ticks, s.StartedAtUtc.Value, nowUtc);
    }

    /// <summary>
    /// Загальна сума за сьогодні: завершені сьогодні зміни (час-заробіток +
    /// їхні тіки, порахувані по діапазону кожної зміни) + поточна.
    /// Рахується з журналу, а не з окремого лічильника — інакше та сама
    /// сума жила б у двох місцях і розходилась на зміні через північ.
    /// </summary>
    public static decimal EarnedToday(
        ShiftState s,
        IEnumerable<ShiftLogEntry> log,
        IEnumerable<ExtraItem> items,
        IEnumerable<ExtraTick> ticks,
        DateTime nowUtc)
    {
        var today = DateTime.Today;
        var earlier = log
            .Where(e => e.EndedAtUtc.ToLocalTime().Date == today)
            .Sum(e => e.Earned + TicksTotal(items, ticks, e.StartedAtUtc, e.EndedAtUtc));

        return earlier + EarnedThisShiftWithExtras(s, items, ticks, nowUtc);
    }

    /// <summary>Скільки капає за секунду — для підпису у віджеті.</summary>
    public static decimal PerSecond(ShiftState s) => s.RatePerHour / 3600m;

    /// <summary>Прогрес зміни 0..1 відносно планових годин.</summary>
    public static double Progress(ShiftState s, DateTime nowUtc)
    {
        if (s.PlannedHours <= 0) return 0;
        var p = PaidElapsed(s, nowUtc).TotalHours / s.PlannedHours;
        return Math.Clamp(p, 0, 1);
    }

    /// <summary>Форматування суми: 1234.56 ₴</summary>
    public static string Format(decimal amount, string currency)
        => $"{amount:0.00} {currency}";

    /// <summary>Форматування тривалості: 7:32:05</summary>
    public static string FormatDuration(TimeSpan t)
        => $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}";
}
