using Android.App;
using Android.Appwidget;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;
using ZarobitokApp.Services;

namespace ZarobitokApp.Platforms.Android;

[BroadcastReceiver(
    Name = "com.zarobitok.app.EarningsWidget",
    Label = "Заробіток",
    Exported = true,
    Enabled = true)]
[IntentFilter(new[]
{
    AppWidgetManager.ActionAppwidgetUpdate,
    EarningsWidget.ActionTick,
    Intent.ActionUserPresent   // розблокування екрана — освіжаємо суму одразу
})]
[MetaData("android.appwidget.provider", Resource = "@xml/widget_info")]
public class EarningsWidget : AppWidgetProvider
{
    public const string ActionTick = "com.zarobitok.widget.TICK";

    public override void OnUpdate(Context context, AppWidgetManager manager, int[] appWidgetIds)
    {
        foreach (var id in appWidgetIds)
            Render(context, manager, id);
    }

    public override void OnReceive(Context context, Intent intent)
    {
        base.OnReceive(context, intent);

        // Віджет лише показує дані: керування зміною живе в апці,
        // щоб випадковий тап по домашньому екрану не завершив зміну.
        if (intent.Action is ActionTick or Intent.ActionUserPresent)
            RefreshAll(context);
    }

    public override void OnEnabled(Context context)
    {
        base.OnEnabled(context);
        WidgetScheduler.Sync(context);
    }

    public override void OnDisabled(Context context)
    {
        // Останній екземпляр віджета прибрали — гасимо будильник.
        WidgetScheduler.Stop(context);
        base.OnDisabled(context);
    }

    /// <summary>Перемалювати всі екземпляри віджета.</summary>
    public static void RefreshAll(Context context)
    {
        var manager = AppWidgetManager.GetInstance(context);
        if (manager is null) return;

        var component = new ComponentName(context, "com.zarobitok.app.EarningsWidget");
        var ids = manager.GetAppWidgetIds(component);
        if (ids is null) return;

        foreach (var id in ids)
            Render(context, manager, id);
    }

    private static void Render(Context context, AppWidgetManager manager, int widgetId)
    {
        var state = ShiftStore.Load();
        var now = DateTime.UtcNow;

        var views = new RemoteViews(context.PackageName, Resource.Layout.widget_earnings);

        views.SetTextViewText(Resource.Id.widget_amount,
            EarningsCalculator.Format(
                EarningsCalculator.EarnedToday(state, ShiftStore.LoadLog(), ShiftStore.LoadExtras(), now),
                state.Currency));

        if (state.IsRunning)
        {
            // Chronometer рахує від "base" у шкалі ElapsedRealtime.
            // Зсуваємо base назад на вже відпрацьований оплачуваний час —
            // далі лаунчер тікає сам, без жодного нашого коду.
            var paidMs = (long)EarningsCalculator.PaidElapsed(state, now).TotalMilliseconds;
            views.SetChronometer(
                Resource.Id.widget_chrono,
                SystemClock.ElapsedRealtime() - paidMs,
                "%s",
                started: true);

            views.SetViewVisibility(Resource.Id.widget_chrono, ViewStates.Visible);
            views.SetViewVisibility(Resource.Id.widget_idle, ViewStates.Gone);
        }
        else
        {
            // started: false зупиняє тікання і фіксує показання.
            views.SetChronometer(Resource.Id.widget_chrono, SystemClock.ElapsedRealtime(), "%s", false);

            views.SetViewVisibility(Resource.Id.widget_chrono, ViewStates.Gone);
            views.SetViewVisibility(Resource.Id.widget_idle, ViewStates.Visible);
        }

        manager.UpdateAppWidget(widgetId, views);
    }
}
