using Android.App;
using Android.Content;
using ZarobitokApp.Services;

namespace ZarobitokApp.Platforms.Android;

/// <summary>
/// Після ребуту всі будильники стерті, але час старту зміни збережено.
/// Відновлюємо розклад — сума не губиться, бо рахується від часу старту.
/// </summary>
[BroadcastReceiver(Name = "com.zarobitok.app.BootReceiver", Exported = true, Enabled = true, Permission = "android.permission.RECEIVE_BOOT_COMPLETED")]
[IntentFilter(new[]
{
    Intent.ActionBootCompleted,
    "android.intent.action.QUICKBOOT_POWERON",
    Intent.ActionMyPackageReplaced
})]
public class BootReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null) return;

        WidgetScheduler.Sync(context);
        EarningsWidget.RefreshAll(context);
    }
}
