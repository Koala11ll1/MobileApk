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

    /// <summary>Момент, коли оплачуваний час зміни досягає запланованих годин.</summary>
    public static DateTime PlannedEnd(ShiftState s)
        => s.StartedAtUtc!.Value
           + TimeSpan.FromMinutes(s.UnpaidBreakMinutes)
           + TimeSpan.FromHours(s.PlannedHours);

    /// <summary>Скільки набігло за поточну зміну (з урахуванням розкладу ставок).</summary>
    public static decimal EarnedThisShift(ShiftState s, DateTime nowUtc)
    {
        if (s.StartedAtUtc is null) return 0m;

        // Перерва віднімається з початку зміни: оплачуваний час іде від старт + перерва.
        return RateSchedule.Earned(s, PaidStart(s), nowUtc);
    }

    /// <summary>
    /// Скільки варта зміна з довільним початком/кінцем — для ручного додавання
    /// і редагування запису в журналі. Ставки беруться з <paramref name="rates"/>.
    /// </summary>
    public static decimal EarnedForRange(ShiftState rates, DateTime startUtc, DateTime endUtc, int breakMinutes)
        => RateSchedule.Earned(rates, startUtc + TimeSpan.FromMinutes(Math.Max(0, breakMinutes)), endUtc);

    /// <summary>Початок оплачуваного часу: старт + неоплачувана перерва.</summary>
    public static DateTime PaidStart(ShiftState s)
        => s.StartedAtUtc!.Value + TimeSpan.FromMinutes(s.UnpaidBreakMinutes);

    /// <summary>Вартість одного тіка: знімок ціни, а для старих записів — поточна ціна типу.</summary>
    public static decimal TickValue(ExtraTick t, IReadOnlyDictionary<Guid, decimal> priceById)
        => t.Delta * (t.UnitPrice ?? priceById.GetValueOrDefault(t.ItemId, 0m));

    /// <summary>
    /// Сума тіків допзаробітку в проміжку [fromUtc, toUtc]. Ціна береться зі знімка
    /// в самому тіку, тож зміна ціни типу не переписує минуле.
    /// </summary>
    public static decimal TicksTotal(
        IEnumerable<ExtraItem> items, IEnumerable<ExtraTick> ticks, DateTime fromUtc, DateTime toUtc)
    {
        var priceById = items.ToDictionary(i => i.Id, i => i.UnitPrice);
        return ticks
            .Where(t => t.AtUtc >= fromUtc && t.AtUtc <= toUtc)
            .Sum(t => TickValue(t, priceById));
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

    /// <summary>Скільки капає за секунду зараз (з урахуванням розкладу ставок).</summary>
    public static decimal PerSecond(ShiftState s, DateTime nowUtc) => RateSchedule.RateAt(s, nowUtc) / 3600m;

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

    /// <summary>Час доби для показу: 14:05 або 2:05 PM.</summary>
    public static string FormatTime(DateTime local, bool use12Hour)
        => local.ToString(use12Hour ? "h:mm tt" : "HH:mm", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Форматування тривалості: 7:32:05</summary>
    public static string FormatDuration(TimeSpan t)
        => $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}";
}
