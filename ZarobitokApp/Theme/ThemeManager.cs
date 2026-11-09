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

    private static readonly (string Key, string Dark, string Light)[] Palette =
    {
        ("Bg",            "#0B0B12", "#F3F4F8"),   // фон екрана
        ("Card",          "#12121A", "#FFFFFF"),   // картки
        ("Surface",       "#1C1C26", "#E7E9F0"),   // поля вводу, другорядні кнопки
        ("Hairline",      "#22FFFFFF", "#1F000000"), // тонкі рамки й роздільники
        ("TextPrimary",   "#FFFFFF", "#111827"),
        ("TextSecondary", "#9CA3AF", "#4B5563"),
        ("TextMuted",     "#6B7280", "#6B7280"),
        ("TextFaint",     "#4B5563", "#9CA3AF"),
        ("Accent",        "#4ADE80", "#15803D"),   // у світлій темі темніший, щоб читався на білому
        ("AccentOn",      "#06210F", "#FFFFFF"),   // текст на кнопці акценту
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
        app.Resources["HairlineBrush"] = new SolidColorBrush(Color.FromArgb(dark ? "#22FFFFFF" : "#1F000000"));

        Changed?.Invoke();
    }

    /// <summary>Поточне значення кольору теми — для місць, де колір задається в коді.</summary>
    public static Color Get(string key)
        => Application.Current?.Resources.TryGetValue(key, out var v) == true && v is Color c ? c : Colors.Magenta;
}
