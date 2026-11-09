using ZarobitokApp.Localization;
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
        => new(new AppTabs())
        {
            Title = L.T("Заробіток")
        };
}
