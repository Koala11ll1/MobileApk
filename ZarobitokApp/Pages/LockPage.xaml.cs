using ZarobitokApp.Localization;
using ZarobitokApp.Services;

namespace ZarobitokApp.Pages;

public enum LockMode
{
    /// <summary>Розблокувати застосунок (вхід або повернення з фону).</summary>
    Unlock,

    /// <summary>Підтвердити поточний PIN перед чутливою дією (вимкнути замок, змінити PIN).</summary>
    Verify,

    /// <summary>Задати новий PIN: ввести двічі.</summary>
    Create
}

public partial class LockPage : ContentPage
{
    private readonly LockMode _mode;
    private readonly bool _asModal;
    private readonly Action<bool>? _done;
    private readonly List<Border> _dots = new();
    private readonly List<Button> _keys = new();
    private readonly Button? _bioButton;

    private string _pin = string.Empty;
    private string? _firstEntry;       // Create: перший введений PIN, чекає повтору
    private IDispatcherTimer? _timer;
    private bool _bioTried;
    private bool _finished;

    /// <summary>Замок уже на екрані — повторно його не показуємо.</summary>
    public static bool IsShowing { get; private set; }

    private int MaxLength => _mode == LockMode.Create ? PinLock.MaxLength : AppLock.Current.PinLength;

    /// <param name="asModal">Замок відкрито модально поверх застосунку (а не як корінь вікна) — після успіху його треба закрити.</param>
    /// <param name="done">Результат: true — PIN вірний / створений, false — скасовано.</param>
    public LockPage(LockMode mode, Action<bool>? done = null, bool asModal = false)
    {
        InitializeComponent();
        _mode = mode;
        _done = done;
        _asModal = asModal;

        for (var i = 0; i < PinLock.MaxLength; i++)
        {
            var dot = new Border
            {
                WidthRequest = 16,
                HeightRequest = 16,
                StrokeThickness = 2,
                Stroke = ThemeManager.Get("TextMuted"),
                BackgroundColor = Colors.Transparent,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 }
            };
            _dots.Add(dot);
            DotsRow.Add(dot);
        }

        BuildKeypad(out _bioButton);
        ShowStep();
        ThemeManager.Changed += ApplyColors;

        if (mode == LockMode.Create)
        {
            ActionButton.IsVisible = true;
            ActionButton.Text = L.T("Далі");
        }
        else if (mode == LockMode.Verify)
        {
            ActionButton.IsVisible = true;
            ActionButton.Text = L.T("Скасувати");
            ActionButton.BackgroundColor = ThemeManager.Get("Surface");
            ActionButton.TextColor = ThemeManager.Get("TextSecondary");
        }
    }

    private void BuildKeypad(out Button? bio)
    {
        bio = null;
        var layout = new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "bio", "0", "del" };

        for (var i = 0; i < layout.Length; i++)
        {
            var key = layout[i];
            var button = new Button
            {
                WidthRequest = 76,
                HeightRequest = 76,
                CornerRadius = 38,
                Padding = 0,
                FontSize = 26,
                BackgroundColor = ThemeManager.Get("Surface"),
                TextColor = ThemeManager.Get("TextPrimary"),
                Text = key switch { "del" => "⌫", "bio" => "👆", _ => key }
            };

            if (key == "bio")
            {
                // Біометрія — лише для входу й лише якщо ввімкнена та доступна.
                var useBio = _mode == LockMode.Unlock && AppLock.Current.BiometricEnabled && Biometric.IsAvailable;
                button.IsVisible = useBio;
                button.Clicked += async (_, _) => await TryBiometricAsync();
                bio = button;
            }
            else if (key == "del")
            {
                button.Clicked += (_, _) => { if (_pin.Length > 0) { _pin = _pin[..^1]; UpdateDots(); } };
            }
            else
            {
                button.Clicked += async (_, _) => await OnDigitAsync(key);
            }

            _keys.Add(button);
            Keypad.Add(button, i % 3, i / 3);
        }
    }

    private void ApplyColors()
    {
        if (_finished) return;
        BackgroundColor = ThemeManager.Get("Bg");
        foreach (var k in _keys)
        {
            k.BackgroundColor = ThemeManager.Get("Surface");
            k.TextColor = ThemeManager.Get("TextPrimary");
        }
        UpdateDots();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        IsShowing = true;
        StartLockoutTimerIfNeeded();

        if (_mode == LockMode.Unlock && !_bioTried && _bioButton is { IsVisible: true })
        {
            _bioTried = true;
            await TryBiometricAsync();
        }
    }

    protected override void OnDisappearing()
    {
        _timer?.Stop();
        base.OnDisappearing();
    }

    // Фізична кнопка "назад" не повинна обходити замок.
    protected override bool OnBackButtonPressed()
    {
        if (_mode != LockMode.Unlock) _ = FinishAsync(false);
        return true;
    }

    private void ShowStep()
    {
        TitleLabel.Text = _mode switch
        {
            LockMode.Unlock => L.T("Введіть PIN"),
            LockMode.Verify => L.T("Підтвердіть PIN"),
            _ => _firstEntry is null ? L.T("Придумайте PIN (4–6 цифр)") : L.T("Повторіть PIN")
        };

        UpdateDots();
    }

    private void UpdateDots()
    {
        var max = MaxLength;
        for (var i = 0; i < _dots.Count; i++)
        {
            _dots[i].IsVisible = i < max;
            _dots[i].BackgroundColor = i < _pin.Length ? ThemeManager.Get("Accent") : Colors.Transparent;
            _dots[i].Stroke = ThemeManager.Get(i < _pin.Length ? "Accent" : "TextMuted");
        }
    }

    private async Task OnDigitAsync(string digit)
    {
        if (_finished || AppLock.Current.LockedFor(DateTime.UtcNow) > TimeSpan.Zero) return;
        if (_pin.Length >= MaxLength) return;

        MessageLabel.Text = string.Empty;
        _pin += digit;
        UpdateDots();

        // Для входу й підтвердження PIN вводиться рівно стільки цифр, скільки задано — далі автоматично.
        if (_mode != LockMode.Create && _pin.Length == MaxLength) await SubmitAsync();
    }

    private async void OnActionClicked(object? sender, EventArgs e)
    {
        if (_mode == LockMode.Create) await SubmitAsync();
        else await FinishAsync(false);   // Verify: "Скасувати"
    }

    private async Task SubmitAsync()
    {
        if (_mode == LockMode.Create)
        {
            if (!PinLock.IsValidFormat(_pin))
            {
                MessageLabel.Text = L.T("PIN має містити від 4 до 6 цифр.");
                return;
            }

            if (_firstEntry is null)
            {
                _firstEntry = _pin;
                _pin = string.Empty;
                ShowStep();
                return;
            }

            if (_firstEntry != _pin)
            {
                _firstEntry = null;
                _pin = string.Empty;
                MessageLabel.Text = L.T("PIN-коди не збігаються. Спробуйте ще раз.");
                ShowStep();
                return;
            }

            AppLock.Current.SetPin(_pin);
            await FinishAsync(true);
            return;
        }

        var result = AppLock.Current.TryVerify(_pin, DateTime.UtcNow);
        _pin = string.Empty;
        UpdateDots();

        if (result.Ok)
        {
            await FinishAsync(true);
            return;
        }

        MessageLabel.Text = L.T("Невірний PIN");
        StartLockoutTimerIfNeeded();
    }

    private async Task TryBiometricAsync()
    {
        if (_finished || AppLock.Current.LockedFor(DateTime.UtcNow) > TimeSpan.Zero) return;

        if (await Biometric.AuthenticateAsync(L.T("Розблокувати «Заробіток»"), L.T("Скасувати")))
            await FinishAsync(true);
    }

    private async Task FinishAsync(bool ok)
    {
        if (_finished) return;
        _finished = true;
        IsShowing = false;
        _timer?.Stop();
        ThemeManager.Changed -= ApplyColors;

        if (ok && _mode == LockMode.Unlock) AppLock.Current.MarkUnlocked();
        if (_asModal) await Navigation.PopModalAsync(_mode != LockMode.Unlock);

        _done?.Invoke(ok);
    }

    // ---- Затримка після невдалих спроб ----

    private void StartLockoutTimerIfNeeded()
    {
        if (_finished || AppLock.Current.LockedFor(DateTime.UtcNow) <= TimeSpan.Zero)
        {
            SetKeypadEnabled(true);
            return;
        }

        SetKeypadEnabled(false);
        ShowLockoutMessage();

        _timer ??= Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.Tick -= OnLockoutTick;
        _timer.Tick += OnLockoutTick;
        _timer.Start();
    }

    private void OnLockoutTick(object? sender, EventArgs e)
    {
        if (AppLock.Current.LockedFor(DateTime.UtcNow) <= TimeSpan.Zero)
        {
            _timer?.Stop();
            MessageLabel.Text = string.Empty;
            SetKeypadEnabled(true);
            return;
        }

        ShowLockoutMessage();
    }

    private void ShowLockoutMessage()
    {
        var seconds = (int)Math.Ceiling(AppLock.Current.LockedFor(DateTime.UtcNow).TotalSeconds);
        MessageLabel.Text = L.F("Забагато спроб. Спробуйте через {0} с", seconds);
    }

    private void SetKeypadEnabled(bool enabled)
    {
        foreach (var k in _keys) k.IsEnabled = enabled;
    }
}
