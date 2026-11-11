using ZarobitokApp.Localization;
using ZarobitokApp.Pages;

namespace ZarobitokApp;

public class AppTabs : TabbedPage
{
    /// <summary>
    /// Номер "покоління" інтерфейсу. Після зміни мови вкладки створюються заново, а старі сторінки
    /// лишаються підписаними на статичні події; вони порівнюють свій номер із поточним і мовчать.
    /// </summary>
    public static int Generation { get; private set; }

    private readonly int _generation = Generation;

    public AppTabs()
    {
        Title = L.T("Заробіток");
        ApplyColors();
        ThemeManager.Changed += ApplyColors;

        // Вкладки внизу: на високому екрані (S24 Ultra, 6.8") верх недосяжний однією рукою.
        // Гортання вкладок пальцем лишається, як і було.
        var android = On<Microsoft.Maui.Controls.PlatformConfiguration.Android>();
        Microsoft.Maui.Controls.PlatformConfiguration.AndroidSpecific.TabbedPage.SetToolbarPlacement(
            android, Microsoft.Maui.Controls.PlatformConfiguration.AndroidSpecific.ToolbarPlacement.Bottom);
        Microsoft.Maui.Controls.PlatformConfiguration.AndroidSpecific.TabbedPage.SetIsSwipePagingEnabled(android, true);

        Children.Add(new ShiftPage { IconImageSource = "tab_shift.png" });
        Children.Add(new ExtrasPage { IconImageSource = "tab_extras.png" });
        Children.Add(new HistoryPage { IconImageSource = "tab_history.png" });
        Children.Add(new StatsPage { IconImageSource = "tab_stats.png" });
        Children.Add(new SettingsPage { IconImageSource = "tab_settings.png" });

        if (OpenSettingsOnLaunch)
        {
            OpenSettingsOnLaunch = false;
            CurrentPage = Children[^1];   // до підключення до вікна — це просто властивість, платформа не чіпається
        }
    }

    private void ApplyColors()
    {
        if (_generation != Generation) return;

        BackgroundColor = ThemeManager.Get("Bg");
        BarBackgroundColor = ThemeManager.Get("Card");
        SelectedTabColor = ThemeManager.Get("Accent");
        UnselectedTabColor = ThemeManager.Get("TextMuted");
    }

    /// <summary>Після перезапуску активності (зміна мови) лишаємо користувача в налаштуваннях.</summary>
    public static bool OpenSettingsOnLaunch { get; set; }

    /// <summary>
    /// Зміна мови: тексти беруться під час створення сторінок, тож потрібен новий екземпляр інтерфейсу.
    /// Підміна Window.Page на запущеному вікні в MAUI на Android не відмальовується, тому
    /// перезапускаємо саму активність — вікно й вкладки створюються заново з новою мовою.
    /// </summary>
    public static void Rebuild()
    {
        Generation++;           // старі сторінки лишаються підписаними на події — вони мовчать
        OpenSettingsOnLaunch = true;

#if ANDROID
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await Task.Delay(400);   // дати пікеру мови закрити своє діалогове вікно
            Microsoft.Maui.ApplicationModel.Platform.CurrentActivity?.Recreate();
        });
#endif
    }
}
