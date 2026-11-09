using ZarobitokApp.Services;

namespace ZarobitokApp.Tests;

public class CrashLogTests
{
    private static readonly DateTime T0 = new(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc);

    private static CrashEntry Crash(int n, string stack = "System.Exception: boom")
        => new(T0.AddMinutes(n), "1.2.3 (45)", "Google Pixel 7", "14 (API 34)", stack);

    [Fact]
    public void Starts_empty_and_returns_newest_first()
    {
        var log = new CrashLog(new MemoryKeyValueStore());
        Assert.Empty(log.Entries);

        log.Add(Crash(1));
        log.Add(Crash(2));

        Assert.Equal(new[] { T0.AddMinutes(2), T0.AddMinutes(1) }, log.Entries.Select(e => e.AtUtc));
    }

    [Fact]
    public void Keeps_only_the_latest_ten()
    {
        var log = new CrashLog(new MemoryKeyValueStore());
        for (var i = 0; i < 15; i++) log.Add(Crash(i));

        Assert.Equal(CrashLog.MaxEntries, log.Entries.Count);
        Assert.Equal(T0.AddMinutes(14), log.Entries[0].AtUtc);
        Assert.Equal(T0.AddMinutes(5), log.Entries[^1].AtUtc);
    }

    [Fact]
    public void Very_long_stacks_are_truncated()
    {
        var log = new CrashLog(new MemoryKeyValueStore());
        log.Add(Crash(1, new string('x', 50_000)));
        Assert.InRange(log.Entries[0].Stack.Length, 1, CrashLog.MaxStackLength + 5);
    }

    [Fact]
    public void TakeUnseen_returns_the_newest_once_then_nothing()
    {
        var log = new CrashLog(new MemoryKeyValueStore());
        log.Add(Crash(1));
        log.Add(Crash(2));

        Assert.Equal(T0.AddMinutes(2), log.TakeUnseen()!.AtUtc);
        Assert.Null(log.TakeUnseen());              // діалог — один раз
        Assert.Equal(2, log.Entries.Count);         // але в журналі вони лишаються

        log.Add(Crash(3));
        Assert.Equal(T0.AddMinutes(3), log.TakeUnseen()!.AtUtc);   // новий збій знову показується
    }

    [Fact]
    public void Survives_a_new_instance_over_the_same_store()
    {
        var store = new MemoryKeyValueStore();
        new CrashLog(store).Add(Crash(1));
        Assert.Single(new CrashLog(store).Entries);
    }

    [Fact]
    public void Corrupt_stored_data_is_treated_as_empty()
    {
        var store = new MemoryKeyValueStore();
        store.Set("crash_log_json", "{not json");
        var log = new CrashLog(store);

        Assert.Empty(log.Entries);
        Assert.Null(log.TakeUnseen());
        log.Add(Crash(1));          // і журнал знову працює
        Assert.Single(log.Entries);
    }

    [Fact]
    public void Clear_empties_the_log()
    {
        var log = new CrashLog(new MemoryKeyValueStore());
        log.Add(Crash(1));
        log.Clear();
        Assert.Empty(log.Entries);
    }

    [Fact]
    public void Report_has_version_device_stack_and_a_privacy_note_but_no_user_data()
    {
        var text = CrashLog.FormatReport(new[] { Crash(1, "System.InvalidOperationException: bad") });

        Assert.Contains("1.2.3 (45)", text);
        Assert.Contains("Google Pixel 7", text);
        Assert.Contains("API 34", text);
        Assert.Contains("System.InvalidOperationException: bad", text);
        Assert.Contains("2026-03-01 10:01:00 UTC", text);
        Assert.Contains("Ваших змін і сум тут немає", text);
    }
}
