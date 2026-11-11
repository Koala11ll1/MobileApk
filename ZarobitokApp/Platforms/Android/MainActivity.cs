using ZarobitokApp.Localization;
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
        L.Use(ShiftStore.Load().Language);
        var crash = Crashes.Log.TakeUnseen();
        if (crash is not null)
        {
            var dialog = new AlertDialog.Builder(this);
            dialog.SetTitle(L.T("Останній збій застосунку"));
            dialog.SetMessage(Summary(crash));
            dialog.SetCancelable(false);
            dialog.SetPositiveButton(L.T("Надіслати звіт"), (_, _) => SendReport(crash));
            dialog.SetNegativeButton(L.T("Закрити"), (_, _) => { });
            dialog.Show();
        }

        base.OnCreate(savedInstanceState);
        HandleShortcut(Intent);
    }

    // У діалозі — коротко: час і перші рядки стеку; повний текст іде у звіті.
    private static string Summary(CrashEntry e)
    {
        var lines = e.Stack.Split('\n').Take(4);
        return $"{e.AtUtc.ToLocalTime():yyyy-MM-dd HH:mm}  ·  {e.AppVersion}\n\n{string.Join('\n', lines)}";
    }

    private void SendReport(CrashEntry crash)
    {
        var send = new Intent(Intent.ActionSend);
        send.SetType("text/plain");
        send.PutExtra(Intent.ExtraSubject, "Заробіток — звіт про збій");
        send.PutExtra(Intent.ExtraText, CrashLog.FormatReport(new[] { crash }));
        StartActivity(Intent.CreateChooser(send, L.T("Надіслати звіт")));
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

        // Поки замок увімкнено — без скриншотів і без суми в списку недавніх.
        ScreenSecurity.Apply();

        // При старті тема виставляється ще до появи активності — панелі фарбуємо тут.
        SystemBars.Apply();

        // Автостоп міг спрацювати, поки апка була згорнута, а розклад — збитись
        // (ребут, force-stop): при поверненні синхронізуємо стан і розклад.
        WidgetScheduler.Sync(this);
        EarningsWidget.RefreshAll(this);
    }
}
