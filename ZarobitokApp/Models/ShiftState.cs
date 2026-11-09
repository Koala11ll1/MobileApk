using System.Text.Json.Serialization;

namespace ZarobitokApp.Models;

/// <summary>
/// Стан поточної зміни. Зберігаємо ЧАС СТАРТУ, а не накопичену суму —
/// тоді після перезавантаження телефона або вбивства процесу
/// сума перерахується правильно.
/// </summary>
public sealed class ShiftState
{
    /// <summary>Ставка за годину (у валюті користувача).</summary>
    public decimal RatePerHour { get; set; } = 100m;

    /// <summary>Символ валюти для відображення.</summary>
    public string Currency { get; set; } = "₴";

    /// <summary>UTC-час початку зміни. Null — зміна не запущена.</summary>
    public DateTime? StartedAtUtc { get; set; }

    /// <summary>Неоплачувана перерва в хвилинах (віднімається від часу зміни).</summary>
    public int UnpaidBreakMinutes { get; set; }

    /// <summary>
    /// Планова тривалість зміни в годинах. Використовується і для
    /// прогрес-бару, і як момент автостопу — по досягненню цього часу
    /// зміна завершується сама.
    /// </summary>
    public double PlannedHours { get; set; } = 8;

    // ---- Ставки за розкладом ----

    /// <summary>Ставка у вихідні (сб/нд за місцевим часом). Null — така сама, як звичайна.</summary>
    public decimal? WeekendRate { get; set; }

    /// <summary>Нічна ставка. Null — не використовується. Перекриває вихідну в нічні години.</summary>
    public decimal? NightRate { get; set; }

    /// <summary>Година початку ночі (0..23, місцевий час).</summary>
    public int NightStartHour { get; set; } = 22;

    /// <summary>Година кінця ночі (0..23). Менша за початок — ніч переходить через північ.</summary>
    public int NightEndHour { get; set; } = 6;

    // ---- Цілі й податки ----

    /// <summary>Місячна ціль заробітку. 0 — без цілі.</summary>
    public decimal MonthlyGoal { get; set; }

    /// <summary>Відсоток податку/відрахувань для показу «чистої» суми.</summary>
    public decimal TaxPercent { get; set; }

    // ---- Сповіщення ----

    /// <summary>Сповістити, коли планова тривалість вичерпана і зміна завершилась.</summary>
    public bool NotifyShiftEnd { get; set; }

    /// <summary>Щоденне нагадування почати зміну.</summary>
    public bool ReminderEnabled { get; set; }

    /// <summary>Час нагадування у хвилинах від півночі (місцевий час).</summary>
    public int ReminderMinutes { get; set; } = 9 * 60;

    // ---- Формати ----

    public bool WeekStartsOnSunday { get; set; }

    public bool Use12Hour { get; set; }

    /// <summary>Тема: "dark" (за замовчуванням), "light" або "system".</summary>
    public string Theme { get; set; } = "dark";

    /// <summary>Мова інтерфейсу: "uk" (за замовчуванням), "en" або "system".</summary>
    public string Language { get; set; } = "uk";

    [JsonIgnore]
    public bool IsRunning => StartedAtUtc.HasValue;

    [JsonIgnore]
    public bool HasRateSchedule => WeekendRate.HasValue || NightRate.HasValue;
}

/// <summary>
/// Запис у журналі завершених змін. BreakMinutes — перерва, яку було віднято
/// (null у старих записів; тоді її можна вивести з Earned).
/// </summary>
public sealed record ShiftLogEntry(
    DateTime StartedAtUtc,
    DateTime EndedAtUtc,
    decimal RatePerHour,
    decimal Earned,
    int? BreakMinutes = null)
{
    public TimeSpan Duration => EndedAtUtc - StartedAtUtc;
}

/// <summary>
/// Тип допзаробітку (наприклад "Продав ред бул") — визначається один раз
/// і живе постійно, незалежно від змін. Archived — прихований з вибору, але
/// лишається в каталозі, щоб історія не втратила назву і ціну.
/// Лічильник по ньому рахується
/// окремо для кожної зміни чи дня, з тіків (див. ExtraTick).
/// </summary>
public sealed record ExtraItem(
    Guid Id,
    string Label,
    decimal UnitPrice,
    bool Archived = false);

/// <summary>
/// Одна дія +1 або -1 по типу допзаробітку. Приналежність до зміни чи дня
/// визначається порівнянням AtUtc з часовим проміжком — так само, як і решта
/// математики в апці, без явного зв'язку в даних. Тому один і той самий тип
/// можна "тикати" як під час поточної зміни, так і заднім числом, редагуючи
/// вже завершену зміну в журналі.
/// UnitPrice — знімок ціни на момент дії: зміна ціни типу не чіпає минулі
/// суми. Null лише у старих записів — для них береться поточна ціна типу.
/// </summary>
public sealed record ExtraTick(
    Guid Id,
    Guid ItemId,
    DateTime AtUtc,
    int Delta,
    decimal? UnitPrice = null);
