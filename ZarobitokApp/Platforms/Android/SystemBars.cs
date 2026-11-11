using AndroidX.Core.View;
using AColor = Android.Graphics.Color;

namespace ZarobitokApp.Platforms.Android;

/// <summary>
/// Статус-бар і навігаційна панель у кольорах теми. На Android 15+ застосунок завжди
/// малюється «від краю до краю», тож колір під панелями дає фон вікна (DecorView),
/// а на старіших версіях — ще й колір самих панелей. Іконки панелей — темні у світлій темі.
/// </summary>
public static class SystemBars
{
    public static void Apply()
    {
        var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
        var window = activity?.Window;
        if (window is null) return;

        var dark = ThemeManager.IsDark;
        var bg = ToAndroid(ThemeManager.Get("Bg"));
        var bar = ToAndroid(ThemeManager.Get("Card"));   // під нижніми вкладками — колір їхньої панелі

        window.DecorView.SetBackgroundColor(bg);

        if (!OperatingSystem.IsAndroidVersionAtLeast(35))
        {
#pragma warning disable CA1422 // на Android 15+ ці методи нічого не роблять — тому й умова вище
            window.SetStatusBarColor(bg);
            window.SetNavigationBarColor(bar);
#pragma warning restore CA1422
        }

        var insets = WindowCompat.GetInsetsController(window, window.DecorView);
        insets.AppearanceLightStatusBars = !dark;
        insets.AppearanceLightNavigationBars = !dark;
    }

    private static AColor ToAndroid(Microsoft.Maui.Graphics.Color c)
        => AColor.Argb((int)(c.Alpha * 255), (int)(c.Red * 255), (int)(c.Green * 255), (int)(c.Blue * 255));
}
