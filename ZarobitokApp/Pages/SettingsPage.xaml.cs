using System.ComponentModel;
using System.Globalization;
using System.Text;
using ZarobitokApp.Services;

namespace ZarobitokApp.Pages;

public partial class SettingsPage : ContentPage
{
    private bool _loading;

    public SettingsPage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        _loading = true;
        var s = ShiftStore.Load();
        var inv = CultureInfo.InvariantCulture;

        RateEntry.Text = s.RatePerHour.ToString(inv);
        CurrencyEntry.Text = s.Currency;
        PlannedEntry.Text = s.PlannedHours.ToString(inv);
        BreakEntry.Text = s.UnpaidBreakMinutes.ToString(inv);

        WeekendRateEntry.Text = s.WeekendRate?.ToString(inv) ?? string.Empty;
        NightRateEntry.Text = s.NightRate?.ToString(inv) ?? string.Empty;
        NightStartEntry.Text = s.NightStartHour.ToString(inv);
        NightEndEntry.Text = s.NightEndHour.ToString(inv);

        GoalEntry.Text = s.MonthlyGoal > 0 ? s.MonthlyGoal.ToString(inv) : string.Empty;
        TaxEntry.Text = s.TaxPercent > 0 ? s.TaxPercent.ToString(inv) : string.Empty;

        NotifyEndSwitch.IsToggled = s.NotifyShiftEnd;
        ReminderSwitch.IsToggled = s.ReminderEnabled;
        ReminderPicker.Time = TimeSpan.FromMinutes(s.ReminderMinutes);

        WeekSundaySwitch.IsToggled = s.WeekStartsOnSunday;
        Hour12Switch.IsToggled = s.Use12Hour;
        ReminderPicker.Format = s.Use12Hour ? "h:mm tt" : "HH:mm";

        _loading = false;
    }

    private void OnSettingsChanged(object? sender, EventArgs e) => Save();

    private async void OnToggled(object? sender, ToggledEventArgs e)
    {
        if (_loading) return;

        // Для сповіщень на Android 13+ потрібен дозвіл — просимо в момент вмикання.
        if (e.Value && (sender == NotifyEndSwitch || sender == ReminderSwitch))
            await NotificationPermission.EnsureAsync();

        Save();
        ReminderPicker.Format = Hour12Switch.IsToggled ? "h:mm tt" : "HH:mm";
    }

    private void OnReminderTimeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == TimePicker.TimeProperty.PropertyName) Save();
    }

    private void Save()
    {
        if (_loading) return;

        // Порожнє поле для необов'язкових значень означає "вимкнено"; нерозбірливий
        // текст ігноруємо, щоб одруківка не затирала налаштування.
        ShiftManager.UpdateSettings(s =>
        {
            if (Input.TryDecimal(RateEntry.Text, out var rate) && rate is >= 0 and <= Limits.MaxRate)
                s.RatePerHour = rate;

            if (!string.IsNullOrWhiteSpace(CurrencyEntry.Text))
                s.Currency = CurrencyEntry.Text.Trim();

            if (Input.TryDouble(PlannedEntry.Text, out var planned) && planned is > 0 and <= Limits.MaxPlannedHours)
                s.PlannedHours = planned;

            if (int.TryParse(BreakEntry.Text, out var brk) && brk is >= 0 and <= Limits.MaxBreakMinutes)
                s.UnpaidBreakMinutes = brk;

            ApplyOptionalRate(WeekendRateEntry.Text, v => s.WeekendRate = v);
            ApplyOptionalRate(NightRateEntry.Text, v => s.NightRate = v);

            if (int.TryParse(NightStartEntry.Text, out var ns) && ns is >= 0 and <= 23) s.NightStartHour = ns;
            if (int.TryParse(NightEndEntry.Text, out var ne) && ne is >= 0 and <= 23) s.NightEndHour = ne;

            if (string.IsNullOrWhiteSpace(GoalEntry.Text)) s.MonthlyGoal = 0;
            else if (Input.TryDecimal(GoalEntry.Text, out var goal) && goal is >= 0 and <= Limits.MaxRate * 100)
                s.MonthlyGoal = goal;

            if (string.IsNullOrWhiteSpace(TaxEntry.Text)) s.TaxPercent = 0;
            else if (Input.TryDecimal(TaxEntry.Text, out var tax) && tax is >= 0 and <= 100)
                s.TaxPercent = tax;

            s.NotifyShiftEnd = NotifyEndSwitch.IsToggled;
            s.ReminderEnabled = ReminderSwitch.IsToggled;
            s.ReminderMinutes = (int)(ReminderPicker.Time ?? TimeSpan.Zero).TotalMinutes;
            s.WeekStartsOnSunday = WeekSundaySwitch.IsToggled;
            s.Use12Hour = Hour12Switch.IsToggled;
        });
    }

    private static void ApplyOptionalRate(string? text, Action<decimal?> set)
    {
        if (string.IsNullOrWhiteSpace(text)) set(null);
        else if (Input.TryDecimal(text, out var v) && v is >= 0 and <= Limits.MaxRate) set(v);
    }

    // ---- Дані: експорт і резервна копія ----

    private async void OnExportCsvClicked(object? sender, EventArgs e)
    {
        var s = ShiftStore.Load();
        var csv = DataExport.ToCsv(ShiftStore.LoadLog(), ShiftStore.LoadExtraItems(), ShiftStore.LoadExtraTicks(), s.Currency);
        await ShareTextFileAsync($"zarobitok-{DateTime.Today:yyyy-MM-dd}.csv", csv, "Експорт змін");
    }

    private async void OnBackupClicked(object? sender, EventArgs e)
    {
        var json = DataExport.ToBackupJson(
            ShiftStore.Load(), ShiftStore.LoadLog(), ShiftStore.LoadExtraItems(), ShiftStore.LoadExtraTicks());
        await ShareTextFileAsync($"zarobitok-backup-{DateTime.Today:yyyy-MM-dd}.json", json, "Резервна копія");
    }

    private async void OnRestoreClicked(object? sender, EventArgs e)
    {
        var file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Файл резервної копії" });
        if (file is null) return;

        string text;
        await using (var stream = await file.OpenReadAsync())
        using (var reader = new StreamReader(stream, Encoding.UTF8))
            text = await reader.ReadToEndAsync();

        var data = DataExport.FromBackupJson(text);
        if (data is null)
        {
            await DisplayAlert("Не вдалося відновити", "Це не схоже на резервну копію «Заробітку» або файл пошкоджений.", "OK");
            return;
        }

        var ok = await DisplayAlert(
            "Замінити всі дані?",
            $"У копії: змін — {data.Log.Count}, типів допзаробітку — {data.Items.Count}. " +
            "Усі поточні дані та налаштування буде замінено.",
            "Замінити", "Скасувати");
        if (!ok) return;

        ShiftManager.RestoreAll(data);
        OnAppearing();   // підтягнути відновлені значення в поля
        await DisplayAlert("Готово", "Дані відновлено.", "OK");
    }

    private async Task ShareTextFileAsync(string fileName, string content, string title)
    {
        try
        {
            var path = Path.Combine(FileSystem.CacheDirectory, fileName);
            // BOM потрібен, щоб Excel правильно прочитав кирилицю в CSV.
            await File.WriteAllTextAsync(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            await Share.Default.RequestAsync(new ShareFileRequest(title, new ShareFile(path)));
        }
        catch (Exception ex)
        {
            await DisplayAlert("Не вдалося поділитись файлом", ex.Message, "OK");
        }
    }
}
