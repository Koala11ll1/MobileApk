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
/// Одноразовий допзаробіток (продаж тощо), додається лише під час
/// активної зміни — його AtUtc завжди потрапляє в проміжок [StartedAtUtc, ...]
/// тієї зміни, тому окремо прив'язувати його до зміни не треба: приналежність
/// визначається порівнянням часових міток, як і решта математики в апці.
/// </summary>
public sealed record ExtraEarning(
    Guid Id,
    DateTime AtUtc,
    string Label,
    decimal Amount);
