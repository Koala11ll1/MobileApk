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
    /// Загальна сума за сьогодні: завершені сьогодні зміни + поточна.
    /// Рахується з журналу, а не з окремого лічильника — інакше та сама
    /// сума жила б у двох місцях і розходилась на зміні через північ.
    /// </summary>
    public static decimal EarnedToday(ShiftState s, IEnumerable<ShiftLogEntry> log, DateTime nowUtc)
    {
        var today = DateTime.Today;
        var earlier = log
            .Where(e => e.EndedAtUtc.ToLocalTime().Date == today)
            .Sum(e => e.Earned);

        return earlier + EarnedThisShift(s, nowUtc);
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
