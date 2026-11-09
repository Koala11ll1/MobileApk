namespace ZarobitokApp.Services;

/// <summary>Відбиток / обличчя для розблокування. Лише Android 11+; інакше недоступно.</summary>
public static class Biometric
{
    public static bool IsAvailable
#if ANDROID
        => global::ZarobitokApp.Platforms.Android.BiometricHelper.IsAvailable();
#else
        => false;
#endif

    /// <summary>Показує системний запит. true — користувач підтвердив себе.</summary>
    public static Task<bool> AuthenticateAsync(string title, string cancelText)
#if ANDROID
        => global::ZarobitokApp.Platforms.Android.BiometricHelper.AuthenticateAsync(title, cancelText);
#else
        => Task.FromResult(false);
#endif
}
