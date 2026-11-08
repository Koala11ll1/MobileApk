namespace ZarobitokApp.Services;

/// <summary>Запит дозволу на сповіщення (потрібен лише на Android 13+).</summary>
public static class NotificationPermission
{
    public static Task EnsureAsync()
    {
#if ANDROID
        if (!OperatingSystem.IsAndroidVersionAtLeast(33)) return Task.CompletedTask;

        var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
        const string permission = "android.permission.POST_NOTIFICATIONS";

        if (activity is not null
            && activity.CheckSelfPermission(permission) != Android.Content.PM.Permission.Granted)
        {
            activity.RequestPermissions(new[] { permission }, 4201);
        }
#endif
        return Task.CompletedTask;
    }
}
