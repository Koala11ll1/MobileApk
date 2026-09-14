using ZarobitokApp.Pages;

namespace ZarobitokApp;

public class AppTabs : TabbedPage
{
    public AppTabs()
    {
        Title = "Заробіток";
        BackgroundColor = Color.FromArgb("#0B0B12");
        BarBackgroundColor = Color.FromArgb("#12121A");
        SelectedTabColor = Color.FromArgb("#4ADE80");
        UnselectedTabColor = Color.FromArgb("#6B7280");

        Children.Add(new ShiftPage());
        Children.Add(new ExtrasPage());
        Children.Add(new HistoryPage());
        Children.Add(new SettingsPage());
    }
}
