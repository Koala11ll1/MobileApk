using ZarobitokApp.Models;

#if ANDROID
using Android.Content;
using ZarobitokApp.Platforms.Android;
using Application = Android.App.Application;
#endif

namespace ZarobitokApp.Services;

/// <summary>
/// Фасад над ShiftStore: змінює стан і одразу штовхає віджет
/// та фоновий сервіс, щоб UI і домашній екран не розходились.
/// </summary>
public static class ShiftManager
{
    public static ShiftState Current => ShiftStore.Load();

    public static void StartShift()
    {
        var s = ShiftStore.Load();
        if (s.IsRunning) return;

        // Новий день — обнуляємо накопичене «раніше сьогодні».
        if (s.TodayStamp.Date != DateTime.Today)
        {
            s.TodayStamp = DateTime.Today;
            s.EarnedEarlierToday = 0m;
        }

        s.StartedAtUtc = DateTime.UtcNow;
        ShiftStore.Save(s);

        SyncSchedule();
        PushWidget();
    }

    public static void StopShift()
    {
        var s = ShiftStore.Load();
        if (!s.IsRunning) return;

        var now = DateTime.UtcNow;
        var earned = EarningsCalculator.EarnedThisShift(s, now);

        ShiftStore.AppendLog(new ShiftLogEntry(
            s.StartedAtUtc!.Value, now, s.RatePerHour, earned));

        s.EarnedEarlierToday = EarningsCalculator.EarnedToday(s, now);
        s.TodayStamp = DateTime.Today;
        s.StartedAtUtc = null;
        ShiftStore.Save(s);

        SyncSchedule();
        PushWidget();
    }

    public static void UpdateSettings(Action<ShiftState> mutate)
    {
        var s = ShiftStore.Load();
        mutate(s);
        ShiftStore.Save(s);
        PushWidget();
    }

    /// <summary>Примусово перемалювати всі екземпляри віджета.</summary>
    public static void PushWidget()
    {
#if ANDROID
        EarningsWidget.RefreshAll(Application.Context);
#endif
    }

    /// <summary>
    /// Вмикає або гасить хвилинний будильник оновлення суми
    /// залежно від того, чи йде зміна.
    /// </summary>
    private static void SyncSchedule()
    {
#if ANDROID
        WidgetScheduler.Sync(Application.Context);
#endif
    }
}
