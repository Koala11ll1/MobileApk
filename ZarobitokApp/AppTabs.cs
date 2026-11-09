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

        Children.Add(new ShiftPage());
        Children.Add(new ExtrasPage());
        Children.Add(new HistoryPage());
        Children.Add(new StatsPage());
        Children.Add(new SettingsPage());

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
