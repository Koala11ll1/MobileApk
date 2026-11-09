using ZarobitokApp.Localization;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using ZarobitokApp.Services;

namespace ZarobitokApp.Pages;

public partial class SettingsPage : ContentPage
{
    private bool _loading;

    // Порядок збігається з пунктами списку в пікері.
    private static readonly string[] ThemeCodes = { ThemeManager.Dark, ThemeManager.Light, ThemeManager.System };

    private static readonly string[] LanguageCodes = { L.Ukrainian, L.EnglishCode, L.SystemCode };

    private static readonly int[] GraceMinutes = { 0, 1, 5 };

    private bool _lockBusy;

    private string _appliedTheme = ThemeManager.Dark;
    private string _appliedLanguage = L.Ukrainian;

    public SettingsPage()
    {
        InitializeComponent();
        ThemePicker.ItemsSource = new[] { L.T("Темна"), L.T("Світла"), L.T("Як у системи") };
        // Назви мов — рідною мовою, щоб їх можна було знайти, навіть якщо інтерфейс не зрозумілий.
        LanguagePicker.ItemsSource = new[] { "Українська", "English", L.T("Як у системи") };
        GracePicker.ItemsSource = new[] { L.T("Одразу"), L.T("Через 1 хвилину"), L.T("Через 5 хвилин") };
    }

    private int _shownVersion = -1;

    protected override void OnAppearing()
    {
        base.OnAppearing();
        RefreshAboutUi();   // збої могли з'явитись, поки екран був закритий
        if (_shownVersion == ShiftManager.Version) return;   // поля вже актуальні
        FillFields();
    }

    private void FillFields()
    {
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

        _appliedTheme = ThemeManager.Normalize(s.Theme);
        ThemePicker.SelectedIndex = Math.Max(0, Array.IndexOf(ThemeCodes, _appliedTheme));

        _appliedLanguage = L.Normalize(s.Language);
        LanguagePicker.SelectedIndex = Math.Max(0, Array.IndexOf(LanguageCodes, _appliedLanguage));

        HideWidgetSwitch.IsToggled = s.HideWidgetAmount;
        WeekSundaySwitch.IsToggled = s.WeekStartsOnSunday;
        Hour12Switch.IsToggled = s.Use12Hour;
        ReminderPicker.Format = s.Use12Hour ? "h:mm tt" : "HH:mm";

        _loading = false;
        _shownVersion = ShiftManager.Version;

        RefreshSecurityUi();
    }

    // ---- Про застосунок ----

    private const string ReleasesUrl = "https://github.com/Koala11ll1/MobileApk/releases";

    private void RefreshAboutUi()
    {
        VersionLabel.Text = $"{AppInfo.Current.VersionString} ({AppInfo.Current.BuildString})";

        var crashes = Crashes.Log.Entries.Count;
        CrashLabel.Text = crashes == 0 ? L.T("Збоїв не було") : L.F("Збоїв зафіксовано: {0}", crashes);
        SendCrashButton.IsVisible = crashes > 0;
        ClearCrashButton.IsVisible = crashes > 0;
    }

    private async void OnOpenReleasesClicked(object? sender, EventArgs e)
    {
        try { await Launcher.Default.OpenAsync(ReleasesUrl); }
        catch (Exception ex) { await DisplayAlert(L.T("Не вдалося відкрити посилання"), ex.Message, "OK"); }
    }

    private async void OnSendCrashesClicked(object? sender, EventArgs e)
    {
        var text = CrashLog.FormatReport(Crashes.Log.Entries);
        await Share.Default.RequestAsync(new ShareTextRequest { Title = L.T("Звіт про збої"), Text = text });
    }

    private async void OnClearCrashesClicked(object? sender, EventArgs e)
    {
        var ok = await DisplayAlert(L.T("Очистити звіти про збої?"), L.T("Журнал збоїв буде видалено."), L.T("Очистити"), L.T("Скасувати"));
        if (!ok) return;

        Crashes.Log.Clear();
        RefreshAboutUi();
    }

    // ---- Захист ----

    /// <summary>Показує/ховає елементи замка за поточним станом і виставляє перемикачі без побічних ефектів.</summary>
    private void RefreshSecurityUi()
    {
        var lockState = AppLock.Current;
        var wasLoading = _loading;
        _loading = true;

        PinSwitch.IsToggled = lockState.IsEnabled;
        ChangePinButton.IsVisible = lockState.IsEnabled;
        GraceBox.IsVisible = lockState.IsEnabled;
        GracePicker.SelectedIndex = Math.Max(0, Array.IndexOf(GraceMinutes, lockState.GraceMinutes));

        BiometricRow.IsVisible = lockState.IsEnabled && Biometric.IsAvailable;
        BiometricSwitch.IsToggled = lockState.BiometricEnabled;

        _loading = wasLoading;
        ScreenSecurity.Apply();
    }

    /// <summary>Відкриває екран PIN модально й чекає результат.</summary>
    private async Task<bool> RunLockAsync(LockMode mode)
    {
        var result = new TaskCompletionSource<bool>();
        await Navigation.PushModalAsync(new LockPage(mode, ok => result.TrySetResult(ok), asModal: true), true);
        return await result.Task;
    }

    private async void OnPinToggled(object? sender, ToggledEventArgs e)
    {
        if (_loading || _lockBusy) return;
        _lockBusy = true;

        try
        {
            if (e.Value)
            {
                await RunLockAsync(LockMode.Create);
            }
            else if (await RunLockAsync(LockMode.Verify))
            {
                AppLock.Current.Disable();
            }
        }
        finally
        {
            RefreshSecurityUi();   // якщо скасували — перемикач повертається у справжній стан
            _lockBusy = false;
        }
    }

    private async void OnChangePinClicked(object? sender, EventArgs e)
    {
        if (_lockBusy) return;
        _lockBusy = true;

        try
        {
            if (await RunLockAsync(LockMode.Verify)) await RunLockAsync(LockMode.Create);
        }
        finally
        {
            _lockBusy = false;
        }
    }

    private void OnGraceChanged(object? sender, EventArgs e)
    {
        if (_loading || GracePicker.SelectedIndex < 0) return;
        AppLock.Current.GraceMinutes = GraceMinutes[GracePicker.SelectedIndex];
    }

    private async void OnBiometricToggled(object? sender, ToggledEventArgs e)
    {
        if (_loading) return;

        // Вмикаємо лише після того, як користувач справді пройшов перевірку відбитком/обличчям.
        if (e.Value && !await Biometric.AuthenticateAsync(L.T("Підтвердіть відбиток або обличчя"), L.T("Скасувати")))
        {
            RefreshSecurityUi();
            return;
        }

        AppLock.Current.BiometricEnabled = e.Value;
        RefreshSecurityUi();
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

    private void OnAppearanceChanged(object? sender, EventArgs e) => Save();

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
            s.HideWidgetAmount = HideWidgetSwitch.IsToggled;

            if (ThemePicker.SelectedIndex >= 0) s.Theme = ThemeCodes[ThemePicker.SelectedIndex];
            if (LanguagePicker.SelectedIndex >= 0) s.Language = LanguageCodes[LanguagePicker.SelectedIndex];
        });

        // Тему застосовуємо лише коли вона справді змінилась — це перемальовує екрани.
        var saved = ShiftStore.Load();
        var theme = ThemeManager.Normalize(saved.Theme);
        if (theme != _appliedTheme)
        {
            _appliedTheme = theme;
            ThemeManager.Apply(theme);
        }

        // Мова: тексти беруться під час створення сторінок, тож перебудовуємо інтерфейс.
        var language = L.Normalize(saved.Language);
        if (language != _appliedLanguage)
        {
            _appliedLanguage = language;
            L.Use(language);
            AppTabs.Rebuild();
        }

        // UpdateSettings підняв версію даних, але поля вже відповідають збереженому.
        _shownVersion = ShiftManager.Version;
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
        await ShareTextFileAsync($"zarobitok-{DateTime.Today:yyyy-MM-dd}.csv", csv, L.T("Експорт змін"));
    }

    private async void OnBackupClicked(object? sender, EventArgs e)
    {
        var json = DataExport.ToBackupJson(
            ShiftStore.Load(), ShiftStore.LoadLog(), ShiftStore.LoadExtraItems(), ShiftStore.LoadExtraTicks());
        await ShareTextFileAsync($"zarobitok-backup-{DateTime.Today:yyyy-MM-dd}.json", json, L.T("Резервна копія"));
    }

    private async void OnRestoreClicked(object? sender, EventArgs e)
    {
        var file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = L.T("Файл резервної копії") });
        if (file is null) return;

        string text;
        await using (var stream = await file.OpenReadAsync())
        using (var reader = new StreamReader(stream, Encoding.UTF8))
            text = await reader.ReadToEndAsync();

        var data = DataExport.FromBackupJson(text);
        if (data is null)
        {
            await DisplayAlert(L.T("Не вдалося відновити"), L.T("Це не схоже на резервну копію «Заробітку» або файл пошкоджений."), "OK");
            return;
        }

        var ok = await DisplayAlert(
            L.T("Замінити всі дані?"),
            L.F("У копії: змін — {0}, типів допзаробітку — {1}. Усі поточні дані та налаштування буде замінено.",
                data.Log.Count, data.Items.Count),
            L.T("Замінити"), L.T("Скасувати"));
        if (!ok) return;

        ShiftManager.RestoreAll(data);
        FillFields();   // підтягнути відновлені значення в поля
        await DisplayAlert(L.T("Готово"), L.T("Дані відновлено."), "OK");
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
            await DisplayAlert(L.T("Не вдалося поділитись файлом"), ex.Message, "OK");
        }
    }
}
