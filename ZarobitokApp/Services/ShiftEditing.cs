using ZarobitokApp.Localization;
using ZarobitokApp.Models;

namespace ZarobitokApp.Services;

/// <summary>Чиста логіка редагування журналу: перевірки й перенесення тіків. Без MAUI.</summary>
public static class ShiftEditing
{
    public static readonly TimeSpan MaxDuration = TimeSpan.FromHours(48);

    /// <summary>
    /// Текст помилки або null, якщо проміжок можна зберегти. <paramref name="others"/> —
    /// решта записів журналу (без того, що редагується), <paramref name="runningStartUtc"/> —
    /// початок активної зміни, якщо така є.
    /// </summary>
    public static string? Validate(
        IEnumerable<ShiftLogEntry> others, DateTime startUtc, DateTime endUtc,
        DateTime nowUtc, DateTime? runningStartUtc)
    {
        if (endUtc <= startUtc) return L.T("Кінець має бути після початку.");
        if (endUtc - startUtc > MaxDuration) return L.T("Зміна не може бути довшою за 48 годин.");
        if (endUtc > nowUtc) return L.T("Кінець зміни не може бути в майбутньому.");

        foreach (var o in others)
            if (startUtc < o.EndedAtUtc && endUtc > o.StartedAtUtc)
                return L.T("Зміна перетинається з іншою у журналі.");

        if (runningStartUtc is { } rs && endUtc > rs)
            return L.T("Зміна перетинається з поточною (активною).");

        return null;
    }

    /// <summary>
    /// Тіки зі старого проміжку лишаються з тією самою зміною: якщо проміжок
    /// звузили, тіки поза ним підтягуються до найближчого краю нового.
    /// </summary>
    public static List<ExtraTick> MoveTicks(
        IEnumerable<ExtraTick> ticks,
        DateTime oldStart, DateTime oldEnd, DateTime newStart, DateTime newEnd)
        => ticks.Select(t =>
            t.AtUtc >= oldStart && t.AtUtc <= oldEnd
                ? t with { AtUtc = t.AtUtc < newStart ? newStart : t.AtUtc > newEnd ? newEnd : t.AtUtc }
                : t).ToList();

    /// <summary>
    /// Перетворює введені місцеві час початку й кінця на UTC-проміжок. Дата береться
    /// з <paramref name="startDateLocal"/>; якщо кінець не пізніше за початок —
    /// зміна нічна, і кінець припадає на наступну добу.
    /// </summary>
    public static (DateTime StartUtc, DateTime EndUtc) ResolveRange(
        DateTime startDateLocal, TimeSpan startTime, TimeSpan endTime)
    {
        var start = DateTime.SpecifyKind(startDateLocal.Date + startTime, DateTimeKind.Local);
        var end = DateTime.SpecifyKind(startDateLocal.Date + endTime, DateTimeKind.Local);
        if (end <= start) end = end.AddDays(1);
        return (start.ToUniversalTime(), end.ToUniversalTime());
    }

    /// <summary>Перерва запису: збережена або (для старих записів) виведена з суми.</summary>
    public static int BreakOf(ShiftLogEntry e)
    {
        if (e.BreakMinutes is { } b) return Math.Max(0, b);
        if (e.RatePerHour <= 0) return 0;

        var paidSeconds = (double)(e.Earned / e.RatePerHour * 3600m);
        var breakMinutes = (e.Duration.TotalSeconds - paidSeconds) / 60;
        return (int)Math.Max(0, Math.Round(breakMinutes));
    }

    /// <summary>Ставки для перерахунку запису: його власна базова + поточний розклад.</summary>
    public static ShiftState RatesFor(ShiftState current, decimal baseRate) => new()
    {
        RatePerHour = baseRate,
        WeekendRate = current.WeekendRate,
        NightRate = current.NightRate,
        NightStartHour = current.NightStartHour,
        NightEndHour = current.NightEndHour
    };

    /// <summary>
    /// Найближчий момент нагадування (місцевий час): сьогодні о заданий час,
    /// якщо він ще попереду, інакше завтра.
    /// </summary>
    public static DateTime NextReminder(DateTime localNow, int minutesFromMidnight)
    {
        var today = localNow.Date.AddMinutes(minutesFromMidnight);
        return today > localNow ? today : today.AddDays(1);
    }
}
