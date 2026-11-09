using ZarobitokApp.Services;

namespace ZarobitokApp;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();

        ThemeManager.Apply(ShiftStore.Load().Theme);

        // Режим "як у системи": тема телефона змінилась, поки апка відкрита.
        RequestedThemeChanged += (_, _) => ThemeManager.Refresh();
    }

    protected override Window CreateWindow(IActivationState? activationState)
        => new(new AppTabs())
        {
            Title = "Заробіток"
        };
}
