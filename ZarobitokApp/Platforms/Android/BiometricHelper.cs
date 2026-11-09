using Android.App;
using Android.Content;
using Android.Hardware.Biometrics;
using Android.OS;

namespace ZarobitokApp.Platforms.Android;

public static class BiometricHelper
{
    // У біндингах .NET немає констант BiometricManager.Authenticators — беремо значення з Android SDK.
    private const int BiometricWeak = 0x00FF;   // BiometricManager.Authenticators.BIOMETRIC_WEAK
    private const int Success = 0;              // BiometricManager.BIOMETRIC_SUCCESS

    public static bool IsAvailable()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(30)) return false;

        var manager = (BiometricManager?)global::Android.App.Application.Context.GetSystemService(Context.BiometricService);
        return manager is not null
            && manager.CanAuthenticate(BiometricWeak) == Success;
    }

    public static Task<bool> AuthenticateAsync(string title, string cancelText)
    {
        var tcs = new TaskCompletionSource<bool>();
        var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;

        if (activity is null || !OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            tcs.SetResult(false);
            return tcs.Task;
        }

        var executor = activity.MainExecutor!;
        var prompt = new BiometricPrompt.Builder(activity)
            .SetTitle(title)!
            .SetAllowedAuthenticators(BiometricWeak)!
            .SetNegativeButton(cancelText, executor, new NegativeClick(tcs))!
            .Build();

        prompt.Authenticate(new CancellationSignal(), executor, new Callback(tcs));
        return tcs.Task;
    }
}

internal sealed class Callback : BiometricPrompt.AuthenticationCallback
{
    private readonly TaskCompletionSource<bool> _tcs;

    public Callback(TaskCompletionSource<bool> tcs) => _tcs = tcs;

    public override void OnAuthenticationSucceeded(BiometricPrompt.AuthenticationResult? result)
        => _tcs.TrySetResult(true);

    public override void OnAuthenticationError(BiometricErrorCode errorCode, Java.Lang.ICharSequence? errString)
        => _tcs.TrySetResult(false);
}

internal sealed class NegativeClick : Java.Lang.Object, IDialogInterfaceOnClickListener
{
    private readonly TaskCompletionSource<bool> _tcs;

    public NegativeClick(TaskCompletionSource<bool> tcs) => _tcs = tcs;

    public void OnClick(IDialogInterface? dialog, int which) => _tcs.TrySetResult(false);
}
