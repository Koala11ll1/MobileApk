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

    /// <summary>Планова тривалість зміни в годинах — для прогрес-бару.</summary>
    public double PlannedHours { get; set; } = 8;

    /// <summary>Коефіцієнт понаднормових після планових годин (1.0 = без доплати).</summary>
    public decimal OvertimeMultiplier { get; set; } = 1.0m;

    [JsonIgnore]
    public bool IsRunning => StartedAtUtc.HasValue;
}

/// <summary>Запис у журналі завершених змін.</summary>
public sealed record ShiftLogEntry(
    DateTime StartedAtUtc,
    DateTime EndedAtUtc,
    decimal RatePerHour,
    decimal Earned)
{
    public TimeSpan Duration => EndedAtUtc - StartedAtUtc;
}

/// <summary>
/// Тип допзаробітку (наприклад "Продав ред бул") — визначається один раз
/// і живе постійно, незалежно від змін. Лічильник по ньому рахується
/// окремо для кожної зміни чи дня, з тіків (див. ExtraTick).
/// </summary>
public sealed record ExtraItem(
    Guid Id,
    string Label,
    decimal UnitPrice);

/// <summary>
/// Одна дія +1 або -1 по типу допзаробітку. Приналежність до зміни чи дня
/// визначається порівнянням AtUtc з часовим проміжком — так само, як і решта
/// математики в апці, без явного зв'язку в даних. Тому один і той самий тип
/// можна "тикати" як під час поточної зміни, так і заднім числом, редагуючи
/// вже завершену зміну в журналі.
/// </summary>
public sealed record ExtraTick(
    Guid Id,
    Guid ItemId,
    DateTime AtUtc,
    int Delta);
