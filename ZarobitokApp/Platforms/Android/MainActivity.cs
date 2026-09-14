using Android.App;
using Android.Content.PM;
using Android.OS;
using ZarobitokApp.Services;

namespace ZarobitokApp.Platforms.Android;

[Activity(
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation
        | ConfigChanges.UiMode | ConfigChanges.ScreenLayout
        | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnResume()
    {
        base.OnResume();

        // Користувач міг стартувати зміну тапом по віджету —
        // при поверненні в апку синхронізуємо стан і розклад.
        WidgetScheduler.Sync(this);
        EarningsWidget.RefreshAll(this);
    }
}
