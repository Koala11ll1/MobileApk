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
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        // Показуємо причину минулого збою ДО base.OnCreate() — тобто до
        // того, як узагалі стартує MAUI-конвеєр (Window/Page). Якщо апка
        // валиться десь усередині нього, MainPage.OnAppearing ніколи не
        // встигає спрацювати, а цей рядок — встигає.
        var crash = ShiftStore.TakeLastCrash();
        if (crash is not null)
        {
            new AlertDialog.Builder(this)
                .SetTitle("Останній збій застосунку")
                .SetMessage(crash)
                .SetCancelable(false)
                .SetPositiveButton("OK", (s, e) => { })
                .Show();
        }

        base.OnCreate(savedInstanceState);
    }

    protected override void OnResume()
    {
        base.OnResume();

        // Користувач міг стартувати зміну тапом по віджету —
        // при поверненні в апку синхронізуємо стан і розклад.
        WidgetScheduler.Sync(this);
        EarningsWidget.RefreshAll(this);
    }
}
