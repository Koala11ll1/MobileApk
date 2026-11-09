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
    }

    private void ApplyColors()
    {
        if (_generation != Generation) return;

        BackgroundColor = ThemeManager.Get("Bg");
        BarBackgroundColor = ThemeManager.Get("Card");
        SelectedTabColor = ThemeManager.Get("Accent");
        UnselectedTabColor = ThemeManager.Get("TextMuted");
    }

    /// <summary>Створює інтерфейс заново (потрібно після зміни мови) і лишає користувача в налаштуваннях.</summary>
    public static void Rebuild()
    {
        Generation++;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            var window = Application.Current?.Windows.FirstOrDefault();
            if (window is null) return;

            var tabs = new AppTabs();
            window.Page = tabs;
            window.Title = L.T("Заробіток");
            tabs.CurrentPage = tabs.Children[^1];
        });
    }
}
