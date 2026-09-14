using System.Text.Json;
using ZarobitokApp.Models;

#if ANDROID
using Android.Content;
using Application = Android.App.Application;
#endif

namespace ZarobitokApp.Services;

/// <summary>
/// Єдине джерело правди для UI, віджета і фонового сервісу.
///
/// ВАЖЛИВО: віджет — це окремий BroadcastReceiver, який часто працює,
/// коли активності апки вже немає в пам'яті. Тому MAUI Preferences тут
/// не підходять напряму; звертаємось до Android SharedPreferences з
/// фіксованим іменем, доступним усім компонентам пакета.
/// </summary>
public static class ShiftStore
{
    public const string PrefsName = "zarobitok_shared_store";
    private const string KeyState = "shift_state_json";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false
    };

    private static readonly object Gate = new();

    public static ShiftState Load()
    {
        lock (Gate)
        {
            var raw = ReadRaw();
            if (string.IsNullOrWhiteSpace(raw)) return new ShiftState();

            try
            {
                return JsonSerializer.Deserialize<ShiftState>(raw, JsonOpts) ?? new ShiftState();
            }
            catch (JsonException)
            {
                // Пошкоджені дані краще скинути, ніж падати при старті віджета.
                return new ShiftState();
            }
        }
    }

    public static void Save(ShiftState state)
    {
        lock (Gate)
        {
            WriteRaw(JsonSerializer.Serialize(state, JsonOpts));
        }
    }

    // ---- Діагностика: останній незловлений виняток ----

    public const string CrashKey = "last_crash";

    /// <summary>Повертає текст останнього збою (якщо є) і одразу його очищує.</summary>
    public static string? TakeLastCrash()
    {
        var crash = ReadKey(CrashKey);
        if (string.IsNullOrEmpty(crash)) return null;

        WriteKey(CrashKey, string.Empty);
        return crash;
    }

    // ---- Журнал завершених змін ----

    private const string KeyLog = "shift_log_json";

    public static List<ShiftLogEntry> LoadLog()
    {
        var raw = ReadKey(KeyLog);
        if (string.IsNullOrWhiteSpace(raw)) return new List<ShiftLogEntry>();

        try
        {
            return JsonSerializer.Deserialize<List<ShiftLogEntry>>(raw, JsonOpts)
                   ?? new List<ShiftLogEntry>();
        }
        catch (JsonException)
        {
            return new List<ShiftLogEntry>();
        }
    }

    public static void AppendLog(ShiftLogEntry entry)
    {
        var log = LoadLog();
        log.Insert(0, entry);
        if (log.Count > 200) log.RemoveRange(200, log.Count - 200);
        WriteKey(KeyLog, JsonSerializer.Serialize(log, JsonOpts));
    }

    // ---- Платформозалежна частина ----

    private static string? ReadRaw() => ReadKey(KeyState);
    private static void WriteRaw(string value) => WriteKey(KeyState, value);

    private static string? ReadKey(string key)
    {
#if ANDROID
        var prefs = Application.Context.GetSharedPreferences(PrefsName, FileCreationMode.Private);
        return prefs?.GetString(key, null);
#else
        return Preferences.Default.Get<string?>(key, null);
#endif
    }

    private static void WriteKey(string key, string value)
    {
#if ANDROID
        var prefs = Application.Context.GetSharedPreferences(PrefsName, FileCreationMode.Private);
        using var editor = prefs!.Edit();
        editor!.PutString(key, value);

        // Commit, а не Apply: записів мало (старт/стоп/зміна налаштувань),
        // зате журнал змін гарантовано на диску, навіть якщо систему
        // зараз же вб'є процес.
        editor.Commit();
#else
        Preferences.Default.Set(key, value);
#endif
    }
}
