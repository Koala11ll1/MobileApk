using System.Security.Cryptography;

namespace ZarobitokApp.Services;

/// <summary>
/// PIN-код: хеш із сіллю (PBKDF2) і затримка після невдалих спроб. Без залежностей від MAUI.
/// Це захист від стороннього погляду в телефон, а не стійке шифрування даних: 4–6 цифр
/// мають малий простір, тому головне тут — затримка між спробами й заборона скриншотів.
/// </summary>
public static class PinLock
{
    public const int MinLength = 4;
    public const int MaxLength = 6;

    private const int Iterations = 60_000;
    private const int FreeAttempts = 5;

    public static bool IsValidFormat(string? pin)
        => pin is { Length: >= MinLength and <= MaxLength } && pin.All(char.IsAsciiDigit);

    public static (string Salt, string Hash) Create(string pin)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        return (Convert.ToBase64String(salt), Convert.ToBase64String(Derive(pin, salt)));
    }

    public static bool Verify(string pin, string saltBase64, string hashBase64)
    {
        try
        {
            var expected = Convert.FromBase64String(hashBase64);
            var actual = Derive(pin, Convert.FromBase64String(saltBase64));
            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch (FormatException)
        {
            return false;   // пошкоджені збережені дані — вважаємо PIN невірним
        }
    }

    /// <summary>Скільки чекати після <paramref name="failedAttempts"/> невдалих спроб підряд: 5 вільних, далі 30 с, 1 хв, 2 хв… до 15 хв.</summary>
    public static TimeSpan LockoutAfter(int failedAttempts)
    {
        if (failedAttempts < FreeAttempts) return TimeSpan.Zero;

        var steps = Math.Min(failedAttempts - FreeAttempts, 5);
        return TimeSpan.FromSeconds(Math.Min(30 * Math.Pow(2, steps), 15 * 60));
    }

    private static byte[] Derive(string pin, byte[] salt)
        => Rfc2898DeriveBytes.Pbkdf2(pin, salt, Iterations, HashAlgorithmName.SHA256, 32);
}
