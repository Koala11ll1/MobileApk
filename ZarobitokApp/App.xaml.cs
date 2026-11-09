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
        // Із замком користувач бачить лише екран PIN: застосунок із даними не створюється,
        // поки не введено правильний PIN.
        Page root = AppLock.Current.IsEnabled && !AppLock.Current.IsUnlocked
            ? new LockPage(LockMode.Unlock, ok =>
            {
                if (ok && Windows.FirstOrDefault() is { } w) w.Page = new AppTabs();
            })
            : new AppTabs();

        var window = new Window(root) { Title = L.T("Заробіток") };

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
