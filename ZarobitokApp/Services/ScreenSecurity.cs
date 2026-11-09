using ZarobitokApp.Services;

namespace ZarobitokApp.Services;

/// <summary>
/// Поки замок увімкнено, вікно позначається як захищене (FLAG_SECURE): суму не видно в списку
/// недавніх застосунків і не можна зробити скриншот.
/// </summary>
public static class ScreenSecurity
{
    public static void Apply()
    {
#if ANDROID
        var window = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity?.Window;
        if (window is null) return;

        if (AppLock.Current.IsEnabled) window.AddFlags(global::Android.Views.WindowManagerFlags.Secure);
        else window.ClearFlags(global::Android.Views.WindowManagerFlags.Secure);
#endif
    }
}
