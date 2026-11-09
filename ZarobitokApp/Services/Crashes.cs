namespace ZarobitokApp.Services;

/// <summary>Єдиний журнал збоїв застосунку (створюється за першим зверненням — навіть якщо збій стався при старті).</summary>
public static class Crashes
{
    private static CrashLog? _log;

    public static CrashLog Log => _log ??= new CrashLog(new ShiftStoreKeyValueStore());
}
