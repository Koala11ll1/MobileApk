namespace ZarobitokApp.Services;

/// <summary>Просте сховище ключ-значення (замок, журнал збоїв). На Android це SharedPreferences, у тестах — пам'ять.</summary>
public interface IKeyValueStore
{
    string? Get(string key);
    void Set(string key, string? value);   // null — видалити
}

public sealed class MemoryKeyValueStore : IKeyValueStore
{
    private readonly Dictionary<string, string> _data = new();

    public string? Get(string key) => _data.GetValueOrDefault(key);

    public void Set(string key, string? value)
    {
        if (value is null) _data.Remove(key);
        else _data[key] = value;
    }
}

public readonly record struct VerifyResult(bool Ok, TimeSpan WaitFor);

/// <summary>
/// Замок застосунку: PIN, затримка після невдалих спроб і стан "розблоковано" у межах сесії.
/// PIN-дані навмисно зберігаються окремо від налаштувань і резервної копії: копія не повинна
/// ні відкривати чужий доступ, ні повертати старий PIN.
/// </summary>
public sealed class AppLockState
{
    private const string KeySalt = "lock_pin_salt";
    private const string KeyHash = "lock_pin_hash";
    private const string KeyLength = "lock_pin_len";
    private const string KeyFailed = "lock_failed";
    private const string KeyUntil = "lock_until_ticks";
    private const string KeyBiometric = "lock_biometric";
    private const string KeyGrace = "lock_grace_min";

    private readonly IKeyValueStore _store;
    private DateTime? _backgroundedAtUtc;

    public AppLockState(IKeyValueStore store) => _store = store;

    public bool IsEnabled => !string.IsNullOrEmpty(_store.Get(KeyHash));

    /// <summary>Розблоковано в поточній сесії (пам'ять; після перезапуску знову потрібен PIN).</summary>
    public bool IsUnlocked { get; private set; }

    public int PinLength => int.TryParse(_store.Get(KeyLength), out var n) && n is >= PinLock.MinLength and <= PinLock.MaxLength
        ? n : PinLock.MinLength;

    public bool BiometricEnabled
    {
        get => _store.Get(KeyBiometric) == "1";
        set => _store.Set(KeyBiometric, value ? "1" : null);
    }

    /// <summary>Через скільки хвилин у фоні блокувати знову. 0 — щоразу, коли апка була згорнута.</summary>
    public int GraceMinutes
    {
        get => int.TryParse(_store.Get(KeyGrace), out var n) && n >= 0 ? n : 1;
        set => _store.Set(KeyGrace, Math.Max(0, value).ToString());
    }

    public void SetPin(string pin)
    {
        if (!PinLock.IsValidFormat(pin)) throw new ArgumentException("PIN must be 4–6 digits.", nameof(pin));

        var (salt, hash) = PinLock.Create(pin);
        _store.Set(KeySalt, salt);
        _store.Set(KeyHash, hash);
        _store.Set(KeyLength, pin.Length.ToString());
        ResetFailures();
        IsUnlocked = true;   // користувач щойно його ввів
    }

    public void Disable()
    {
        foreach (var k in new[] { KeySalt, KeyHash, KeyLength, KeyFailed, KeyUntil, KeyBiometric })
            _store.Set(k, null);
        IsUnlocked = true;
    }

    /// <summary>Скільки ще чекати до наступної спроби (нуль — можна пробувати).</summary>
    public TimeSpan LockedFor(DateTime nowUtc)
    {
        if (!long.TryParse(_store.Get(KeyUntil), out var ticks)) return TimeSpan.Zero;

        var left = new DateTime(ticks, DateTimeKind.Utc) - nowUtc;
        return left > TimeSpan.Zero ? left : TimeSpan.Zero;
    }

    public VerifyResult TryVerify(string pin, DateTime nowUtc)
    {
        var wait = LockedFor(nowUtc);
        if (wait > TimeSpan.Zero) return new VerifyResult(false, wait);

        var salt = _store.Get(KeySalt);
        var hash = _store.Get(KeyHash);
        if (salt is null || hash is null) return new VerifyResult(true, TimeSpan.Zero);   // замка нема

        if (PinLock.Verify(pin, salt, hash))
        {
            ResetFailures();
            return new VerifyResult(true, TimeSpan.Zero);
        }

        var failed = (int.TryParse(_store.Get(KeyFailed), out var f) ? f : 0) + 1;
        _store.Set(KeyFailed, failed.ToString());

        var delay = PinLock.LockoutAfter(failed);
        if (delay > TimeSpan.Zero) _store.Set(KeyUntil, (nowUtc + delay).Ticks.ToString());
        return new VerifyResult(false, delay);
    }

    public void MarkUnlocked() => IsUnlocked = true;

    public void MarkBackgrounded(DateTime nowUtc) => _backgroundedAtUtc ??= nowUtc;

    /// <summary>
    /// Викликається, коли апка повертається на передній план. Якщо вона пробула в фоні довше
    /// за "затримку блокування" — скидає стан і каже, що треба показати замок.
    /// </summary>
    public bool ShouldRelock(DateTime nowUtc)
    {
        var since = _backgroundedAtUtc;
        _backgroundedAtUtc = null;

        if (!IsEnabled || !IsUnlocked || since is null) return false;
        if (nowUtc - since.Value < TimeSpan.FromMinutes(GraceMinutes)) return false;

        IsUnlocked = false;
        return true;
    }

    private void ResetFailures()
    {
        _store.Set(KeyFailed, null);
        _store.Set(KeyUntil, null);
    }
}

public static class AppLock
{
    /// <summary>Підміняється при старті реальним сховищем (див. MauiProgram).</summary>
    public static AppLockState Current { get; set; } = new(new MemoryKeyValueStore());
}
