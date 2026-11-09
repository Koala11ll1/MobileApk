using ZarobitokApp.Pages;

namespace ZarobitokApp;

public class AppTabs : TabbedPage
{
    public AppTabs()
    {
        Title = "Заробіток";
        ApplyColors();
        ThemeManager.Changed += ApplyColors;

        Children.Add(new ShiftPage());
        Children.Add(new ExtrasPage());
        Children.Add(new HistoryPage());
        Children.Add(new StatsPage());
        Children.Add(new SettingsPage());
    }

    private void ApplyColors()
    {
        BackgroundColor = ThemeManager.Get("Bg");
        BarBackgroundColor = ThemeManager.Get("Card");
        SelectedTabColor = ThemeManager.Get("Accent");
        UnselectedTabColor = ThemeManager.Get("TextMuted");
    }
}
