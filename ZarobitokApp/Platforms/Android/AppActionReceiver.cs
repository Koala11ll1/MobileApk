using ZarobitokApp.Localization;
using Android.App;
using Android.Content;
using ZarobitokApp.Services;

namespace ZarobitokApp.Platforms.Android;

/// <summary>
/// Приймає внутрішні дії апки: кінець зміни, нагадування, "+1" з віджета.
/// Exported=false — ззовні (інші апки) надіслати сюди нічого не можна, тому
/// ці дії не можна підробити, на відміну від публічного приймача віджета.
/// </summary>
[BroadcastReceiver(Name = "com.zarobitok.app.AppActionReceiver", Exported = false, Enabled = true)]
public class AppActionReceiver : BroadcastReceiver
{
    public const string ActionShiftEnd = "com.zarobitok.action.SHIFT_END";
    public const string ActionReminder = "com.zarobitok.action.REMINDER";
    public const string ActionExtraPlus = "com.zarobitok.action.EXTRA_PLUS";
    public const string ExtraItemId = "item";

    private const int EndNotificationId = 9101;
    private const int ReminderNotificationId = 9102;

    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent is null) return;

        // Процес міг стартувати з нуля (будильник) — мову беремо зі збережених налаштувань.
        L.Use(ShiftStore.Load().Language);

        switch (intent.Action)
        {
            case ActionShiftEnd:
                // Автостоп завершує зміну рівно в момент кінця плану (див. CheckAutoStop).
                if (ShiftManager.CheckAutoStop())
                {
                    var s = ShiftStore.Load();
                    var today = EarningsCalculator.EarnedToday(
                        s, ShiftStore.LoadLog(), ShiftStore.LoadExtraItems(), ShiftStore.LoadExtraTicks(), DateTime.UtcNow);
                    NotificationHelper.Show(context, EndNotificationId, L.T("Зміну завершено"),
                        L.F("Сьогодні зароблено {0}", EarningsCalculator.Format(today, s.Currency)));
                }
                break;

            case ActionReminder:
                var st = ShiftStore.Load();
                if (st.ReminderEnabled && !st.IsRunning)
                    NotificationHelper.Show(context, ReminderNotificationId, L.T("Час почати зміну"),
                        L.T("Відкрий «Заробіток», щоб запустити лічильник"));

                NotificationScheduler.Sync(context);   // виставити наступне
                break;

            case ActionExtraPlus:
                if (Guid.TryParse(intent.GetStringExtra(ExtraItemId), out var id))
                    ShiftManager.Tick(id, +1);
                break;
        }
    }
}
