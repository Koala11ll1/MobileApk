using ZarobitokApp.Localization;
using Android.App;
using Android.Content;
using Android.OS;

namespace ZarobitokApp.Platforms.Android;

public static class NotificationHelper
{
    private const string ChannelId = "zarobitok_events";

    public static void Show(Context context, int id, string title, string text)
    {
        var manager = (NotificationManager?)context.GetSystemService(Context.NotificationService);
        if (manager is null) return;

        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
        {
            manager.CreateNotificationChannel(
                new NotificationChannel(ChannelId, L.T("Нагадування"), NotificationImportance.Default));
        }

        var open = PendingIntent.GetActivity(
            context, 0,
            new Intent(context, typeof(MainActivity)).SetFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop),
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

        var builder = Build.VERSION.SdkInt >= BuildVersionCodes.O
            ? new Notification.Builder(context, ChannelId)
            : new Notification.Builder(context);

        builder
            .SetSmallIcon(global::Android.Resource.Drawable.IcDialogInfo)
            .SetContentTitle(title)
            .SetContentText(text)
            .SetContentIntent(open)
            .SetAutoCancel(true);

        // Без дозволу POST_NOTIFICATIONS (Android 13+) система просто не покаже сповіщення.
        manager.Notify(id, builder.Build());
    }
}
