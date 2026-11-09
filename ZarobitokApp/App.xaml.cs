using ZarobitokApp.Localization;
using ZarobitokApp.Pages;
using ZarobitokApp.Services;

namespace ZarobitokApp;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();

        var settings = ShiftStore.Load();
        L.Use(settings.Language);
        ThemeManager.Apply(settings.Theme);

        // Режим "як у системи": тема телефона змінилась, поки апка відкрита.
        RequestedThemeChanged += (_, _) => ThemeManager.Refresh();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var tabs = new AppTabs();

        // Із замком застосунок створюється, але лишається невидимим, поки не введено правильний PIN;
        // сам екран PIN — модальний поверх нього. Підміна кореневої сторінки на запущеному вікні в MAUI
        // на Android не відмальовується, тож корінь не міняємо ніколи.
        if (AppLock.Current.IsEnabled && !AppLock.Current.IsUnlocked)
        {
            tabs.IsVisible = false;

            var shown = false;
            tabs.Loaded += async (_, _) =>
            {
                if (shown) return;
                shown = true;

                await tabs.Navigation.PushModalAsync(
                    new LockPage(LockMode.Unlock, ok => { if (ok) tabs.IsVisible = true; }, asModal: true), false);
            };
        }

        var window = new Window(tabs) { Title = L.T("Заробіток") };

        // Stopped — апка справді згорнута (а не просто діалог зверху): запам'ятовуємо час.
        window.Stopped += (_, _) => AppLock.Current.MarkBackgrounded(DateTime.UtcNow);
        window.Resumed += (_, _) => LockIfNeeded(window);

        return window;
    }

    /// <summary>Повернулись у апку після довгої паузи — поверх неї виринає екран PIN.</summary>
    private static void LockIfNeeded(Window window)
    {
        if (!AppLock.Current.ShouldRelock(DateTime.UtcNow) || LockPage.IsShowing) return;

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (window.Page is { } page)
                await page.Navigation.PushModalAsync(new LockPage(LockMode.Unlock, asModal: true), false);
        });
    }
}
