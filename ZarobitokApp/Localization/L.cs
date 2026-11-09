using System.Globalization;

namespace ZarobitokApp.Localization;

/// <summary>
/// Переклад інтерфейсу. Ключ — сам український текст: так тексти в коді лишаються читабельними,
/// а якщо перекладу немає, показується оригінал (нічого не зникає). Без залежностей від MAUI.
/// </summary>
public static class L
{
    public const string Ukrainian = "uk";
    public const string EnglishCode = "en";
    public const string SystemCode = "system";

    public static bool English { get; private set; }

    private static readonly string[] UkMonths =
    {
        "Січень", "Лютий", "Березень", "Квітень", "Травень", "Червень",
        "Липень", "Серпень", "Вересень", "Жовтень", "Листопад", "Грудень"
    };

    private static readonly string[] EnMonths =
    {
        "January", "February", "March", "April", "May", "June",
        "July", "August", "September", "October", "November", "December"
    };

    // Індекс = (int)DayOfWeek, тобто неділя — 0.
    private static readonly string[] UkDays = { "Нд", "Пн", "Вт", "Ср", "Чт", "Пт", "Сб" };
    private static readonly string[] EnDays = { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };

    /// <summary>Мова за кодом з налаштувань: "uk" (за замовчуванням), "en" або "system" (за мовою телефона).</summary>
    public static void Use(string? code)
    {
        English = Normalize(code) switch
        {
            EnglishCode => true,
            SystemCode => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName != "uk",
            _ => false
        };
    }

    public static string Normalize(string? code)
        => code is EnglishCode or SystemCode ? code : Ukrainian;

    public static string T(string uk)
        => English && En.Map.TryGetValue(uk, out var en) ? en : uk;

    /// <summary>Переклад рядка-шаблону з позиційними {0}, {1}… і підстановка значень.</summary>
    public static string F(string ukFormat, params object?[] args)
        => string.Format(CultureInfo.CurrentCulture, T(ukFormat), args);

    public static string[] Months => English ? EnMonths : UkMonths;

    /// <summary>Назви днів тижня для шапки календаря: з понеділка або з неділі.</summary>
    public static string[] Weekdays(bool sundayFirst)
    {
        var d = English ? EnDays : UkDays;
        return sundayFirst
            ? d
            : new[] { d[1], d[2], d[3], d[4], d[5], d[6], d[0] };
    }

    /// <summary>Назва дня тижня за (int)DayOfWeek.</summary>
    public static string DayName(DayOfWeek day) => (English ? EnDays : UkDays)[(int)day];
}
