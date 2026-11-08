using Android.App;
using Android.Content;
using ZarobitokApp.Services;

namespace ZarobitokApp.Platforms.Android;

/// <summary>
/// Виставляє два одноразові сповіщення: "зміну завершено" (на момент кінця плану)
/// і щоденне нагадування. Будильники неточні (SetAndAllowWhileIdle) — для точних
/// треба окремий дозвіл, а хвилинна похибка тут не важлива. Нагадування
/// перевиставляється щоразу після спрацювання, а не через повторюваний будильник.
/// </summary>
public static class NotificationScheduler
{
    private const int EndRequest = 7101;
    private const int ReminderRequest = 7102;

    public static void Sync(Context context)
    {
        var am = (AlarmManager?)context.GetSystemService(Context.AlarmService);
        if (am is null) return;

        var s = ShiftStore.Load();

        var end = Pending(context, AppActionReceiver.ActionShiftEnd, EndRequest);
        if (s.IsRunning && s.NotifyShiftEnd && s.PlannedHours > 0)
        {
            var delay = EarningsCalculator.PlannedEnd(s) - DateTime.UtcNow + TimeSpan.FromSeconds(2);
            if (delay < TimeSpan.FromSeconds(1)) delay = TimeSpan.FromSeconds(1);
            Schedule(am, delay, end);
        }
        else
        {
            am.Cancel(end);
        }

        var reminder = Pending(context, AppActionReceiver.ActionReminder, ReminderRequest);
        if (s.ReminderEnabled)
        {
            var now = DateTime.Now;
            Schedule(am, ShiftEditing.NextReminder(now, s.ReminderMinutes) - now, reminder);
        }
        else
        {
            am.Cancel(reminder);
        }
    }

    private static void Schedule(AlarmManager am, TimeSpan delay, PendingIntent pending)
        => am.SetAndAllowWhileIdle(
            AlarmType.RtcWakeup,
            DateTimeOffset.UtcNow.Add(delay).ToUnixTimeMilliseconds(),
            pending);

    private static PendingIntent Pending(Context context, string action, int requestCode)
    {
        var intent = new Intent(context, typeof(AppActionReceiver));
        intent.SetAction(action);

        return PendingIntent.GetBroadcast(
            context, requestCode, intent,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;
    }
}
