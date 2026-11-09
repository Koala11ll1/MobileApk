using System.Text;
using System.Text.Json;

namespace ZarobitokApp.Services;

/// <summary>Один збій застосунку. Без особистих даних: лише версія, пристрій і стек.</summary>
public sealed record CrashEntry(
    DateTime AtUtc,
    string AppVersion,
    string Device,
    string Os,
    string Stack,
    bool Seen = false);

/// <summary>
/// Журнал незловлених винятків: останні кілька збоїв, щоб їх можна було переглянути й надіслати
/// розробнику, а не лише побачити раз у діалозі. Без залежностей від MAUI.
/// </summary>
public sealed class CrashLog
{
    public const int MaxEntries = 10;
    public const int MaxStackLength = 6000;

    private const string Key = "crash_log_json";

    private readonly IKeyValueStore _store;

    public CrashLog(IKeyValueStore store) => _store = store;

    /// <summary>Від найновішого до найстарішого.</summary>
    public IReadOnlyList<CrashEntry> Entries
    {
        get
        {
            var raw = _store.Get(Key);
            if (string.IsNullOrWhiteSpace(raw)) return Array.Empty<CrashEntry>();

            try
            {
                return JsonSerializer.Deserialize<List<CrashEntry>>(raw) ?? new List<CrashEntry>();
            }
            catch (JsonException)
            {
                return Array.Empty<CrashEntry>();   // пошкоджений журнал не повинен ламати запуск
            }
        }
    }

    public void Add(CrashEntry entry)
    {
        var stack = entry.Stack.Length > MaxStackLength ? entry.Stack[..MaxStackLength] + "\n…" : entry.Stack;

        var list = Entries.ToList();
        list.Insert(0, entry with { Stack = stack, Seen = false });
        if (list.Count > MaxEntries) list.RemoveRange(MaxEntries, list.Count - MaxEntries);

        _store.Set(Key, JsonSerializer.Serialize(list));
    }

    /// <summary>Найновіший іще не показаний збій (і помічає всі як показані) — для діалогу при запуску.</summary>
    public CrashEntry? TakeUnseen()
    {
        var list = Entries.ToList();
        var unseen = list.FirstOrDefault(e => !e.Seen);
        if (unseen is null) return null;

        _store.Set(Key, JsonSerializer.Serialize(list.Select(e => e with { Seen = true }).ToList()));
        return unseen;
    }

    public void Clear() => _store.Set(Key, null);

    /// <summary>Текст для відправки. Підкреслює, що особистих даних (змін, сум) тут немає.</summary>
    public static string FormatReport(IEnumerable<CrashEntry> entries)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Заробіток — звіт про збої");
        sb.AppendLine("Звіт містить лише версію застосунку, модель пристрою та технічний стек. Ваших змін і сум тут немає.");

        var n = 0;
        foreach (var e in entries)
        {
            sb.AppendLine();
            sb.AppendLine($"=== #{++n}  {e.AtUtc:yyyy-MM-dd HH:mm:ss} UTC ===");
            sb.AppendLine($"Версія: {e.AppVersion}");
            sb.AppendLine($"Пристрій: {e.Device}");
            sb.AppendLine($"Android: {e.Os}");
            sb.AppendLine(e.Stack);
        }

        return sb.ToString();
    }
}
