namespace ZarobitokApp.Services;

/// <summary>Зберігає в тих самих SharedPreferences, що й решта даних апки.</summary>
public sealed class ShiftStoreKeyValueStore : IKeyValueStore
{
    public string? Get(string key) => ShiftStore.GetString(key);

    public void Set(string key, string? value) => ShiftStore.SetString(key, value);
}
