using Android.App;
using Android.Content;
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

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    private void RecordCrash(Exception? ex)
    {
        if (ex is null) return;

        try
        {
            var prefs = ApplicationContext?.GetSharedPreferences(ShiftStore.PrefsName, FileCreationMode.Private);
            using var editor = prefs?.Edit();
            editor?.PutString(ShiftStore.CrashKey, ex.ToString());
            editor?.Commit();
        }
        catch
        {
            // Процес вже помирає — це найкраще, що можемо тут зробити.
        }
    }
}
