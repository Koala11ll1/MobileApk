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

    public static void StartShift() => StartShiftAt(DateTime.UtcNow);

    /// <summary>Почати зміну заднім числом — коли забув натиснути вчасно.</summary>
    public static void StartShiftAt(DateTime startedAtUtc)
    {
        var s = ShiftStore.Load();
        if (s.IsRunning) return;

        s.StartedAtUtc = startedAtUtc;
        ShiftStore.Save(s);

        SyncSchedule();
        PushWidget();
    }

    /// <summary>Пересунути час початку вже запущеної зміни.</summary>
    public static void SetShiftStart(DateTime startedAtUtc)
    {
        var s = ShiftStore.Load();
        if (!s.IsRunning) return;

        s.StartedAtUtc = startedAtUtc;
        ShiftStore.Save(s);

        PushWidget();
    }

    public static void StopShift()
    {
        var s = ShiftStore.Load();
        if (!s.IsRunning) return;

        var now = DateTime.UtcNow;
        var earned = EarningsCalculator.EarnedThisShiftWithExtras(s, ShiftStore.LoadExtras(), now);

        ShiftStore.AppendLog(new ShiftLogEntry(
            s.StartedAtUtc!.Value, now, s.RatePerHour, earned));

        s.StartedAtUtc = null;
        ShiftStore.Save(s);

        SyncSchedule();
        PushWidget();
    }

    /// <summary>Додати одноразовий допзаробіток до поточної зміни.</summary>
    public static void AddExtra(string label, decimal amount)
    {
        var s = ShiftStore.Load();
        if (!s.IsRunning) return;

        ShiftStore.AddExtra(new ExtraEarning(Guid.NewGuid(), DateTime.UtcNow, label, amount));
        PushWidget();
    }

    public static void RemoveExtra(Guid id)
    {
        ShiftStore.RemoveExtra(id);
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
