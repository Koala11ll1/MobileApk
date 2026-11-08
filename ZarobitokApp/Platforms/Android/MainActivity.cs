using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using ZarobitokApp.Services;

namespace ZarobitokApp.Platforms.Android;

[Activity(
    Name = "com.zarobitok.app.MainActivity",   // ярлики (shortcuts.xml) посилаються на це ім'я
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation
        | ConfigChanges.UiMode | ConfigChanges.ScreenLayout
        | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
[MetaData("android.app.shortcuts", Resource = "@xml/shortcuts")]
public class MainActivity : MauiAppCompatActivity
{
    public const string ActionStartShift = "com.zarobitok.action.START_SHIFT";
    public const string ActionStopShift = "com.zarobitok.action.STOP_SHIFT";

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        // Показуємо причину минулого збою ДО base.OnCreate() — тобто до
        // того, як узагалі стартує MAUI-конвеєр (Window/Page). Якщо апка
        // валиться десь усередині нього, MainPage.OnAppearing ніколи не
        // встигає спрацювати, а цей рядок — встигає.
        var crash = ShiftStore.TakeLastCrash();
        if (crash is not null)
        {
            var dialog = new AlertDialog.Builder(this);
            dialog.SetTitle("Останній збій застосунку");
            dialog.SetMessage(crash);
            dialog.SetCancelable(false);
            dialog.SetPositiveButton("OK", (_, _) => { });
            dialog.Show();
        }

        base.OnCreate(savedInstanceState);
        HandleShortcut(Intent);
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        HandleShortcut(intent);
    }

    /// <summary>
    /// Ярлики на іконці: довге натискання → «Почати зміну» / «Завершити».
    /// Дію скидаємо після виконання, щоб відновлення активності її не повторило.
    /// </summary>
    private static void HandleShortcut(Intent? intent)
    {
        switch (intent?.Action)
        {
            case ActionStartShift:
                ShiftManager.StartShift();
                break;
            case ActionStopShift:
                ShiftManager.StopShift();
                break;
            default:
                return;
        }

        intent.SetAction(Intent.ActionMain);
    }

    protected override void OnResume()
    {
        base.OnResume();

        // Автостоп міг спрацювати, поки апка була згорнута, а розклад — збитись
        // (ребут, force-stop): при поверненні синхронізуємо стан і розклад.
        WidgetScheduler.Sync(this);
        EarningsWidget.RefreshAll(this);
    }
}
