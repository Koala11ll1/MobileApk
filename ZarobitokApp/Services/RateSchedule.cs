using ZarobitokApp.Models;

namespace ZarobitokApp.Services;

/// <summary>
/// Ставка залежно від часу доби й дня тижня (за місцевим часом).
/// Пріоритет: нічна → вихідна → звичайна. Без налаштованого розкладу
/// це просто ставка × час.
/// </summary>
public static class RateSchedule
{
    public static decimal RateAt(ShiftState s, DateTime utc, TimeZoneInfo? tz = null)
        => RateAtLocal(s, ToLocal(utc, tz));

    /// <summary>Скільки зароблено за оплачуваний проміжок [fromUtc, toUtc).</summary>
    public static decimal Earned(ShiftState s, DateTime fromUtc, DateTime toUtc, TimeZoneInfo? tz = null)
    {
        if (toUtc <= fromUtc) return 0m;

        if (!s.HasRateSchedule)
            return s.RatePerHour * (decimal)(toUtc - fromUtc).TotalSeconds / 3600m;

        // Ставка стала в межах відрізка між "межами": північ, початок і кінець ночі.
        var total = 0m;
        var cursor = fromUtc;
        while (cursor < toUtc)
        {
            var local = ToLocal(cursor, tz);
            var next = cursor + UntilNextBoundary(s, local);
            if (next > toUtc) next = toUtc;

            total += RateAtLocal(s, local) * (decimal)(next - cursor).TotalSeconds / 3600m;
            cursor = next;
        }
        return total;
    }

    private static decimal RateAtLocal(ShiftState s, DateTime local)
    {
        if (s.NightRate is { } night && IsNight(s, local)) return night;

        var weekend = local.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
        if (weekend && s.WeekendRate is { } w) return w;

        return s.RatePerHour;
    }

    private static bool IsNight(ShiftState s, DateTime local)
    {
        var h = local.Hour;
        return s.NightStartHour <= s.NightEndHour
            ? h >= s.NightStartHour && h < s.NightEndHour
            : h >= s.NightStartHour || h < s.NightEndHour;
    }

    // Рахуємо зсув у годинах місцевого годинника, а не перетворюємо назад у UTC:
    // так перехід на літній час не може кинути виняток, лише зсуне межу на годину.
    private static TimeSpan UntilNextBoundary(ShiftState s, DateTime local)
    {
        var best = local.Date.AddDays(1) - local;

        foreach (var hour in new[] { s.NightStartHour, s.NightEndHour })
        {
            var boundary = local.Date.AddHours(hour);
            if (boundary <= local) boundary = boundary.AddDays(1);
            var delta = boundary - local;
            if (delta < best) best = delta;
        }

        return best > TimeSpan.Zero ? best : TimeSpan.FromSeconds(1);
    }

    private static DateTime ToLocal(DateTime utc, TimeZoneInfo? tz)
    {
        var u = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        return tz is null ? u.ToLocalTime() : TimeZoneInfo.ConvertTimeFromUtc(u, tz);
    }
}
