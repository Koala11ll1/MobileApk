namespace ZarobitokApp.Services;

/// <summary>Зберігає налаштування замка в тих самих SharedPreferences, що й решта даних апки.</summary>
public sealed class ShiftStoreLockStore : ILockStore
{
    public string? Get(string key) => ShiftStore.GetString(key);

    public void Set(string key, string? value) => ShiftStore.SetString(key, value);
}
