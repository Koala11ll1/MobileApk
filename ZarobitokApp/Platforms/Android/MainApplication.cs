using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using ZarobitokApp.Services;

namespace ZarobitokApp.Platforms.Android;

[Application]
public class MainApplication : MauiApplication
{
    public MainApplication(IntPtr handle, JniHandleOwnership ownership)
        : base(handle, ownership)
    {
        // Записуємо причину збою СИНХРОННО (Commit, не Apply) — процес
        // ось-ось помре, Apply() може не встигнути скинутись на диск.
        AndroidEnvironment.UnhandledExceptionRaiser += (_, e) => RecordCrash(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => RecordCrash(e.ExceptionObject as Exception);
    }

    public override void OnCreate()
    {
        base.OnCreate();

        // ACTION_USER_PRESENT не доставляється приймачам із маніфесту (Android 8+),
        // тільки зареєстрованим у коді — доки живий процес апки.
        RegisterReceiver(new UnlockReceiver(), new IntentFilter(Intent.ActionUserPresent));
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    private void RecordCrash(Exception? ex)
    {
        if (ex is null) return;

        try
        {
            // Запис синхронний (Commit): процес ось-ось помре, Apply() міг би не встигнути.
            Crashes.Log.Add(new CrashEntry(
                DateTime.UtcNow,
                AppVersionText(),
                $"{Build.Manufacturer} {Build.Model}",
                $"{Build.VERSION.Release} (API {(int)Build.VERSION.SdkInt})",
                ex.ToString()));
        }
        catch
        {
            // Процес вже помирає — це найкраще, що можемо тут зробити.
        }
    }

    private string AppVersionText()
    {
        try
        {
            var info = PackageManager?.GetPackageInfo(PackageName!, 0);
            return $"{info?.VersionName} ({info?.LongVersionCode})";
        }
        catch
        {
            return "?";
        }
    }

    /// <summary>Розблокування екрана → одразу освіжити віджет (і перевірити автостоп).</summary>
    public sealed class UnlockReceiver : BroadcastReceiver
    {
        public override void OnReceive(Context? context, Intent? intent)
        {
            if (context is not null) EarningsWidget.RefreshAll(context);
        }
    }
}
