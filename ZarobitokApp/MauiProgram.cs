using ZarobitokApp.Services;
using Microsoft.Extensions.Logging;

namespace ZarobitokApp;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        // Замок застосунку читає свої налаштування з тих самих SharedPreferences, що й решта даних.
        AppLock.Current = new AppLockState(new ShiftStoreKeyValueStore());

        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
