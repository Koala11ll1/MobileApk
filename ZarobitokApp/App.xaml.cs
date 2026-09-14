namespace ZarobitokApp;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
        UserAppTheme = AppTheme.Dark;
    }

    protected override Window CreateWindow(IActivationState? activationState)
        => new(new AppTabs())
        {
            Title = "Заробіток"
        };
}
