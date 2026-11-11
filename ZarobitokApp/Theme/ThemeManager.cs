namespace ZarobitokApp;

/// <summary>
/// Тема оформлення: темна (за замовчуванням), світла або як у системи.
/// Усі кольори екранів — це DynamicResource із App.xaml; тут ми підмінюємо їхні значення,
/// тож зміна теми застосовується одразу, без перезапуску і без перебудови сторінок.
/// </summary>
public static class ThemeManager
{
    public const string Dark = "dark";
    public const string Light = "light";
    public const string System = "system";

    // Темна тема — справжній чорний фон: на AMOLED (Galaxy S24 Ultra тощо) чорні пікселі вимкнені,
    // це економить батарею й дає глибокий контраст. Усі пари текст/фон тримають ≥ 4.5:1 (WCAG AA),
    // окрім TextFaint — лише для декоративного (порожні стани, дні поза місяцем).
    private static readonly (string Key, string Dark, string Light)[] Palette =
    {
        ("Bg",            "#000000", "#F4F5F7"),   // фон екрана
        ("Card",          "#141418", "#FFFFFF"),   // картки
        ("Surface",       "#202027", "#ECEEF2"),   // поля вводу, другорядні кнопки
        ("Hairline",      "#1FFFFFFF", "#1A000000"), // тонкі рамки й роздільники
        ("TextPrimary",   "#F5F5F7", "#0F1115"),
        ("TextSecondary", "#B3B8C2", "#454B55"),
        ("TextMuted",     "#8F96A3", "#5F6672"),
        ("TextFaint",     "#5C626D", "#9AA0AA"),
        ("Accent",        "#4ADE80", "#15803D"),   // у світлій темі темніший, щоб читався на білому
        ("AccentOn",      "#04210E", "#FFFFFF"),   // текст на кнопці акценту
        ("Danger",        "#F87171", "#DC2626"),
        ("DangerOn",      "#2A0A0A", "#FFFFFF"),
    };


    /// <summary>Спрацьовує після кожної зміни кольорів — екрани з кольорами, заданими в коді, перемальовуються.</summary>
    public static event Action? Changed;

    public static bool IsDark => Application.Current?.RequestedTheme != AppTheme.Light;

    public static string Normalize(string? mode)
        => mode is Light or System ? mode : Dark;

    public static void Apply(string? mode)
    {
        var app = Application.Current;
        if (app is null) return;

        app.UserAppTheme = Normalize(mode) switch
        {
            Light => AppTheme.Light,
            System => AppTheme.Unspecified,
            _ => AppTheme.Dark
        };

        Refresh();
    }

    /// <summary>Перечитати активну тему й виставити кольори (викликається й коли змінилась системна тема).</summary>
    public static void Refresh()
    {
        var app = Application.Current;
        if (app is null) return;

        var dark = IsDark;
        foreach (var (key, d, l) in Palette)
            app.Resources[key] = Color.FromArgb(dark ? d : l);

        // Рамка (Stroke) приймає Brush, а не Color.
        app.Resources["HairlineBrush"] = new SolidColorBrush(Color.FromArgb(dark ? "#1FFFFFFF" : "#1A000000"));

#if ANDROID
        // Статус-бар і навігаційна панель — у колір фону, іконки на них — контрастні до теми.
        ZarobitokApp.Platforms.Android.SystemBars.Apply();
#endif

        Changed?.Invoke();
    }

    /// <summary>Поточне значення кольору теми — для місць, де колір задається в коді.</summary>
    public static Color Get(string key)
        => Application.Current?.Resources.TryGetValue(key, out var v) == true && v is Color c ? c : Colors.Magenta;
}
