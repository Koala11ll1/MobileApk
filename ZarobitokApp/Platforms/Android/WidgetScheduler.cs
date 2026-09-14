using Android.App;
using Android.Content;
using Android.OS;
using ZarobitokApp.Services;

namespace ZarobitokApp.Platforms.Android;

/// <summary>
/// Оновлює ЛИШЕ суму у віджеті. Час тікає сам через Chronometer,
/// тому тут не потрібні ні сервіс, ні щосекундні пробудження.
///
/// Свідомо використовуємо ElapsedRealtime (БЕЗ Wakeup): якщо екран
/// вимкнено — віджета ніхто не бачить, і будити пристрій немає сенсу.
/// Звідси майже нульова витрата батареї і байдужість до Samsung Device Care.
/// </summary>
public static class WidgetScheduler
{
    /// <summary>
    /// 60 с, бо система все одно піднімає коротші інтервали
    /// SetRepeating до хвилини (API 19+). Не боремося з платформою.
    /// </summary>
    private const long IntervalMs = 60_000;

    private const int RequestCode = 7001;

    public static void Start(Context context)
    {
        var am = (AlarmManager?)context.GetSystemService(Context.AlarmService);
        if (am is null) return;

        var pending = BuildPendingIntent(context);

        am.SetRepeating(
            AlarmType.ElapsedRealtime,          // не будить пристрій
            SystemClock.ElapsedRealtime() + IntervalMs,
            IntervalMs,
            pending);
    }

    public static void Stop(Context context)
    {
        var am = (AlarmManager?)context.GetSystemService(Context.AlarmService);
        am?.Cancel(BuildPendingIntent(context));
    }

    /// <summary>Перезапуск: викликати після зміни ставки чи старту/стопу.</summary>
    public static void Sync(Context context)
    {
        if (ShiftStore.Load().IsRunning) Start(context);
        else Stop(context);
    }

    private static PendingIntent BuildPendingIntent(Context context)
    {
        var intent = new Intent(context, typeof(EarningsWidget));
        intent.SetAction(EarningsWidget.ActionTick);

        return PendingIntent.GetBroadcast(
            context, RequestCode, intent,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;
    }
}
