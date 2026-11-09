using ZarobitokApp.Localization;
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
    /// <summary>
    /// Дані змінились (старт/стоп, правка журналу, тік, відновлення з копії).
    /// Підписуються екрани, що тримають кеш, щоб оновитись, коли зміна
    /// прийшла не з них — наприклад, з ярлика чи автостопу.
    /// </summary>
    public static event Action? Changed;

    /// <summary>
    /// Лічильник змін даних. Екрани запам'ятовують, з якою версією вони останній раз
    /// перемалювались, і при появі пропускають роботу, якщо нічого не змінилось.
    /// </summary>
    public static int Version { get; private set; }

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

    /// <summary>
    /// Якщо зміна триває довше за план — завершує її автоматично.
    /// Повертає true, якщо саме зараз зупинила (щоб викликач міг
    /// оновити свій кеш журналу). Викликається і з таймера в апці
    /// (щосекунди, поки відкрита вкладка «Зміна»), і з оновлення
    /// віджета (раз на хвилину, плюс при розблокуванні й відкритті
    /// апки) — так спрацьовує і коли апка згорнута.
    /// </summary>
    public static bool CheckAutoStop()
    {
        var s = ShiftStore.Load();
        if (!s.IsRunning || s.PlannedHours <= 0) return false;

        if (EarningsCalculator.PaidElapsed(s, DateTime.UtcNow).TotalHours < s.PlannedHours)
            return false;

        // Кінець — момент, коли план вичерпано, а не "зараз": якщо телефон
        // спав, перевірка могла спрацювати години після плану.
        StopShift(EarningsCalculator.PlannedEnd(s));
        return true;
    }

    public static void StopShift(DateTime? endedAtUtc = null)
    {
        var s = ShiftStore.Load();
        if (!s.IsRunning) return;

        var now = endedAtUtc ?? DateTime.UtcNow;

        // Earned — лише час-заробіток, БЕЗ допзаробітку. Допзаробіток
        // завжди рахується наживо з тіків по діапазону [StartedAtUtc, EndedAtUtc]
        // цього запису — так лишається можливість редагувати кількість тіків
        // для вже завершеної зміни заднім числом (з журналу).
        var earned = EarningsCalculator.EarnedThisShift(s, now);

        ShiftStore.AppendLog(new ShiftLogEntry(
            s.StartedAtUtc!.Value, now, s.RatePerHour, earned, s.UnpaidBreakMinutes));

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

    /// <summary>
    /// Змінити назву й/або ціну типу. Спершу фіксуємо стару ціну в тіках, що
    /// її ще не мають, — тоді минулі суми не переписуються новою ціною.
    /// </summary>
    public static void UpdateExtraItem(Guid id, string label, decimal unitPrice)
    {
        var items = ShiftStore.LoadExtraItems();
        var i = items.FindIndex(x => x.Id == id);
        if (i < 0) return;

        FreezeTickPrices(id, items[i].UnitPrice);
        items[i] = items[i] with { Label = label, UnitPrice = unitPrice };
        ShiftStore.SaveExtraItems(items);
        PushWidget();
    }

    /// <summary>
    /// «Видалити» тип: ховаємо з вибору, але лишаємо в каталозі —
    /// історичні тіки зберігають назву й ціну.
    /// </summary>
    public static void ArchiveExtraItem(Guid id) => SetArchived(id, true, null);

    /// <summary>Повернути прихований тип (опційно з новою ціною).</summary>
    public static void RestoreExtraItem(Guid id, decimal? newPrice = null) => SetArchived(id, false, newPrice);

    private static void SetArchived(Guid id, bool archived, decimal? newPrice)
    {
        var items = ShiftStore.LoadExtraItems();
        var i = items.FindIndex(x => x.Id == id);
        if (i < 0) return;

        if (newPrice is { } p && p != items[i].UnitPrice) FreezeTickPrices(id, items[i].UnitPrice);
        items[i] = items[i] with { Archived = archived, UnitPrice = newPrice ?? items[i].UnitPrice };
        ShiftStore.SaveExtraItems(items);
        PushWidget();
    }

    private static void FreezeTickPrices(Guid itemId, decimal price)
    {
        var ticks = ShiftStore.LoadExtraTicks();
        if (!ticks.Any(t => t.ItemId == itemId && t.UnitPrice is null)) return;

        ShiftStore.SaveExtraTicks(ticks
            .Select(t => t.ItemId == itemId && t.UnitPrice is null ? t with { UnitPrice = price } : t)
            .ToList());
    }

    /// <summary>+1/-1 по типу допзаробітку в ПОТОЧНІЙ зміні.</summary>
    public static void Tick(Guid itemId, int delta)
    {
        var s = ShiftStore.Load();
        if (!s.IsRunning) return;

        AddTick(itemId, delta, DateTime.UtcNow);
    }

    /// <summary>+1/-1 по типу допзаробітку заднім числом, для вже завершеної зміни.</summary>
    public static void TickAt(Guid itemId, int delta, DateTime atUtc) => AddTick(itemId, delta, atUtc);

    private static void AddTick(Guid itemId, int delta, DateTime atUtc)
    {
        var price = ShiftStore.LoadExtraItems().FirstOrDefault(i => i.Id == itemId)?.UnitPrice;
        ShiftStore.AddExtraTick(new ExtraTick(Guid.NewGuid(), itemId, atUtc, delta, price));
        PushWidget();
    }

    // ---- Редагування журналу ----

    /// <summary>Змінити початок/кінець запису. Повертає текст помилки або null.</summary>
    public static string? EditLogEntry(ShiftLogEntry old, DateTime startUtc, DateTime endUtc)
    {
        var s = ShiftStore.Load();
        var log = ShiftStore.LoadLog();
        var i = log.FindIndex(e => e.StartedAtUtc == old.StartedAtUtc && e.EndedAtUtc == old.EndedAtUtc);
        if (i < 0) return L.T("Запис не знайдено — можливо, його вже видалено.");

        var others = log.Where((_, idx) => idx != i);
        var error = ShiftEditing.Validate(others, startUtc, endUtc, DateTime.UtcNow, s.StartedAtUtc);
        if (error is not null) return error;

        var breakMinutes = ShiftEditing.BreakOf(old);
        var earned = EarningsCalculator.EarnedForRange(
            ShiftEditing.RatesFor(s, old.RatePerHour), startUtc, endUtc, breakMinutes);

        log[i] = old with { StartedAtUtc = startUtc, EndedAtUtc = endUtc, Earned = earned, BreakMinutes = breakMinutes };
        ShiftStore.SaveLog(log.OrderByDescending(e => e.EndedAtUtc).ToList());
        ShiftStore.SaveExtraTicks(ShiftEditing.MoveTicks(
            ShiftStore.LoadExtraTicks(), old.StartedAtUtc, old.EndedAtUtc, startUtc, endUtc));

        PushWidget();
        return null;
    }

    /// <summary>Додати завершену зміну вручну (за поточною ставкою й перервою). Повертає помилку або null.</summary>
    public static string? AddManualShift(DateTime startUtc, DateTime endUtc)
    {
        var s = ShiftStore.Load();
        var log = ShiftStore.LoadLog();

        var error = ShiftEditing.Validate(log, startUtc, endUtc, DateTime.UtcNow, s.StartedAtUtc);
        if (error is not null) return error;

        var earned = EarningsCalculator.EarnedForRange(s, startUtc, endUtc, s.UnpaidBreakMinutes);
        log.Add(new ShiftLogEntry(startUtc, endUtc, s.RatePerHour, earned, s.UnpaidBreakMinutes));
        ShiftStore.SaveLog(log.OrderByDescending(e => e.EndedAtUtc).ToList());

        PushWidget();
        return null;
    }

    // ---- Резервна копія ----

    public static void RestoreAll(BackupData data)
    {
        ShiftStore.Save(data.State);
        ShiftStore.SaveLog(data.Log);
        ShiftStore.SaveExtraItems(data.Items);
        ShiftStore.SaveExtraTicks(data.Ticks);

        SyncSchedule();
        PushWidget();
    }

    public static void UpdateSettings(Action<ShiftState> mutate)
    {
        var s = ShiftStore.Load();
        mutate(s);
        ShiftStore.Save(s);

        // Планові години чи перерва змінили момент автостопу, а час нагадувань —
        // моменти сповіщень, тож розклад переставляємо.
        SyncSchedule();
        PushWidget();
    }

    /// <summary>Примусово перемалювати всі екземпляри віджета.</summary>
    public static void PushWidget()
    {
        Version++;
        Changed?.Invoke();
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
