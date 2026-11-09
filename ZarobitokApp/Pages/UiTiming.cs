namespace ZarobitokApp.Pages;

internal static class UiTiming
{
    /// <summary>
    /// Анімація свайпу між вкладками триває близько 300 мс. Важке оновлення екрана
    /// (розбір JSON, групування, будування списку) на UI-потоці саме в цей час рве
    /// анімацію, тож запускаємо його вже після неї.
    /// </summary>
    public static readonly TimeSpan AfterSwipe = TimeSpan.FromMilliseconds(350);

    public static void RunLater(TimeSpan delay, Action action)
        => _ = Task.Delay(delay).ContinueWith(
            _ => MainThread.BeginInvokeOnMainThread(action),
            TaskScheduler.Default);
}
