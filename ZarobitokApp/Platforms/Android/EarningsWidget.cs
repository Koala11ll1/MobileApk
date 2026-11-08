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
    EarningsWidget.ActionTick
})]
[MetaData("android.appwidget.provider", Resource = "@xml/widget_info")]
public class EarningsWidget : AppWidgetProvider
{
    public const string ActionTick = "com.zarobitok.widget.TICK";

    private const int ExtraRequestCode = 7002;

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
        // ActionUserPresent сюди з маніфесту не доходить (Android 8+ не доставляє
        // його оголошеним приймачам) — його ловить UnlockReceiver із MainApplication.
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
        // Тут же й перевіряємо автостоп по плану — це охоплює і хвилинний
        // будильник, і розблокування екрана, і відкриття апки (OnResume),
        // і ребут (BootReceiver), бо всі вони зрештою ведуть сюди.
        ShiftManager.CheckAutoStop();

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

        var items = ShiftStore.LoadExtraItems();
        var ticks = ShiftStore.LoadExtraTicks();

        views.SetTextViewText(Resource.Id.widget_amount,
            EarningsCalculator.Format(
                EarningsCalculator.EarnedToday(state, ShiftStore.LoadLog(), items, ticks, now),
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

        BindQuickExtra(context, views, state, items, ticks);

        manager.UpdateAppWidget(widgetId, views);
    }

    /// <summary>
    /// Кнопка "+1" для останнього використаного (а якщо ще нічого — першого) типу
    /// допзаробітку. Видима лише під час зміни. Натискання йде у закритий приймач
    /// апки, а не в публічний приймач віджета.
    /// </summary>
    private static void BindQuickExtra(
        Context context, RemoteViews views, Models.ShiftState state,
        List<Models.ExtraItem> items, List<Models.ExtraTick> ticks)
    {
        var active = items.Where(i => !i.Archived).ToList();
        var lastUsed = ticks.Select(t => t.ItemId).FirstOrDefault(id => active.Any(i => i.Id == id));
        var item = active.FirstOrDefault(i => i.Id == lastUsed) ?? active.FirstOrDefault();

        if (!state.IsRunning || item is null)
        {
            views.SetViewVisibility(Resource.Id.widget_extra, ViewStates.Gone);
            return;
        }

        var intent = new Intent(context, typeof(AppActionReceiver));
        intent.SetAction(AppActionReceiver.ActionExtraPlus);
        intent.PutExtra(AppActionReceiver.ExtraItemId, item.Id.ToString());

        var pending = PendingIntent.GetBroadcast(
            context, ExtraRequestCode, intent,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

        views.SetTextViewText(Resource.Id.widget_extra, $"+1 {item.Label}");
        views.SetOnClickPendingIntent(Resource.Id.widget_extra, pending);
        views.SetViewVisibility(Resource.Id.widget_extra, ViewStates.Visible);
    }
}
