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

        // Earned — лише час-заробіток, БЕЗ допзаробітку. Допзаробіток
        // завжди рахується наживо з тіків по діапазону [StartedAtUtc, EndedAtUtc]
        // цього запису — так лишається можливість редагувати кількість тіків
        // для вже завершеної зміни заднім числом (з журналу).
        var earned = EarningsCalculator.EarnedThisShift(s, now);

        ShiftStore.AppendLog(new ShiftLogEntry(
            s.StartedAtUtc!.Value, now, s.RatePerHour, earned));

        s.StartedAtUtc = null;
        ShiftStore.Save(s);

        SyncSchedule();
        PushWidget();
    }

    /// <summary>Створити новий тип допзаробітку, або повернути вже існуючий з такою назвою.</summary>
    public static ExtraItem AddOrGetExtraItem(string label, decimal unitPrice)
    {
        var items = ShiftStore.LoadExtraItems();
        var existing = items.FirstOrDefault(i => string.Equals(i.Label, label, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) return existing;

        var item = new ExtraItem(Guid.NewGuid(), label, unitPrice);
        items.Insert(0, item);
        ShiftStore.SaveExtraItems(items);
        return item;
    }

    /// <summary>+1/-1 по типу допзаробітку в ПОТОЧНІЙ зміні.</summary>
    public static void Tick(Guid itemId, int delta)
    {
        var s = ShiftStore.Load();
        if (!s.IsRunning) return;

        ShiftStore.AddExtraTick(new ExtraTick(Guid.NewGuid(), itemId, DateTime.UtcNow, delta));
        PushWidget();
    }

    /// <summary>+1/-1 по типу допзаробітку заднім числом, для вже завершеної зміни.</summary>
    public static void TickAt(Guid itemId, int delta, DateTime atUtc)
    {
        ShiftStore.AddExtraTick(new ExtraTick(Guid.NewGuid(), itemId, atUtc, delta));
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
