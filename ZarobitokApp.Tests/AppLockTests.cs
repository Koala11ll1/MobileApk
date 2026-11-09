using ZarobitokApp.Services;

namespace ZarobitokApp.Tests;

public class PinLockTests
{
    [Theory]
    [InlineData("1234", true)]
    [InlineData("123456", true)]
    [InlineData("123", false)]
    [InlineData("1234567", false)]
    [InlineData("12a4", false)]
    [InlineData("١٢٣٤", false)]   // арабо-індійські цифри не приймаємо — лише ASCII
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Format_is_4_to_6_ascii_digits(string? pin, bool valid)
        => Assert.Equal(valid, PinLock.IsValidFormat(pin));

    [Fact]
    public void Hash_verifies_the_right_pin_only_and_is_salted()
    {
        var (salt, hash) = PinLock.Create("4321");
        Assert.True(PinLock.Verify("4321", salt, hash));
        Assert.False(PinLock.Verify("4322", salt, hash));

        var (salt2, hash2) = PinLock.Create("4321");
        Assert.NotEqual(salt, salt2);
        Assert.NotEqual(hash, hash2);   // та сама PIN, інша сіль → інший хеш
    }

    [Fact]
    public void Corrupt_stored_data_never_unlocks()
        => Assert.False(PinLock.Verify("1234", "not-base64!!", "also-not"));

    [Theory]
    [InlineData(0, 0)]
    [InlineData(4, 0)]
    [InlineData(5, 30)]
    [InlineData(6, 60)]
    [InlineData(7, 120)]
    [InlineData(10, 900)]
    [InlineData(50, 900)]   // стеля 15 хв
    public void Lockout_grows_after_five_free_attempts_and_is_capped(int failed, int seconds)
        => Assert.Equal(TimeSpan.FromSeconds(seconds), PinLock.LockoutAfter(failed));
}

public class AppLockStateTests
{
    private static readonly DateTime T0 = new(2026, 1, 5, 12, 0, 0, DateTimeKind.Utc);

    private static AppLockState Fresh() => new(new MemoryKeyValueStore());

    [Fact]
    public void Not_enabled_until_a_pin_is_set_and_disable_removes_it()
    {
        var s = Fresh();
        Assert.False(s.IsEnabled);

        s.SetPin("1234");
        Assert.True(s.IsEnabled);
        Assert.Equal(4, s.PinLength);

        s.Disable();
        Assert.False(s.IsEnabled);
    }

    [Fact]
    public void Invalid_pin_is_refused()
        => Assert.Throws<ArgumentException>(() => Fresh().SetPin("12"));

    [Fact]
    public void Wrong_pins_lock_out_after_five_tries_and_the_right_pin_is_refused_while_waiting()
    {
        var s = Fresh();
        s.SetPin("1234");

        for (var i = 0; i < 4; i++)
        {
            var r = s.TryVerify("0000", T0);
            Assert.False(r.Ok);
            Assert.Equal(TimeSpan.Zero, r.WaitFor);
        }

        var fifth = s.TryVerify("0000", T0);
        Assert.False(fifth.Ok);
        Assert.Equal(TimeSpan.FromSeconds(30), fifth.WaitFor);

        // Поки триває очікування, навіть правильний PIN не приймається.
        var during = s.TryVerify("1234", T0.AddSeconds(10));
        Assert.False(during.Ok);
        Assert.Equal(TimeSpan.FromSeconds(20), during.WaitFor);

        // Після очікування правильний PIN працює й скидає лічильник.
        Assert.True(s.TryVerify("1234", T0.AddSeconds(31)).Ok);
        Assert.Equal(TimeSpan.Zero, s.LockedFor(T0.AddSeconds(31)));
        Assert.False(s.TryVerify("0000", T0.AddSeconds(32)).Ok);   // знову 1-а спроба — без затримки
        Assert.Equal(TimeSpan.Zero, s.LockedFor(T0.AddSeconds(32)));
    }

    [Fact]
    public void Failed_attempts_and_lockout_survive_a_restart()
    {
        var store = new MemoryKeyValueStore();
        var a = new AppLockState(store);
        a.SetPin("1234");
        for (var i = 0; i < 5; i++) a.TryVerify("0000", T0);

        var afterRestart = new AppLockState(store);   // нова сесія, те саме сховище
        Assert.True(afterRestart.LockedFor(T0.AddSeconds(5)) > TimeSpan.Zero);
        Assert.False(afterRestart.IsUnlocked);
    }

    [Fact]
    public void Relocks_only_after_the_grace_period_in_background()
    {
        var s = Fresh();
        s.SetPin("1234");       // сесія розблокована
        s.GraceMinutes = 1;

        s.MarkBackgrounded(T0);
        Assert.False(s.ShouldRelock(T0.AddSeconds(30)));
        Assert.True(s.IsUnlocked);

        s.MarkBackgrounded(T0);
        Assert.True(s.ShouldRelock(T0.AddMinutes(2)));
        Assert.False(s.IsUnlocked);

        // Уже заблоковано — повторно не просимо.
        s.MarkBackgrounded(T0);
        Assert.False(s.ShouldRelock(T0.AddMinutes(5)));
    }

    [Fact]
    public void Grace_zero_relocks_on_every_return_from_background()
    {
        var s = Fresh();
        s.SetPin("1234");
        s.GraceMinutes = 0;
        s.MarkBackgrounded(T0);
        Assert.True(s.ShouldRelock(T0));
    }

    [Fact]
    public void A_pause_that_was_never_a_stop_does_not_relock()
    {
        var s = Fresh();
        s.SetPin("1234");
        Assert.False(s.ShouldRelock(T0.AddHours(1)));   // MarkBackgrounded не викликали
    }

    [Fact]
    public void No_lock_when_disabled_regardless_of_time_in_background()
    {
        var s = Fresh();
        s.MarkBackgrounded(T0);
        Assert.False(s.ShouldRelock(T0.AddDays(1)));
    }

    [Fact]
    public void Settings_default_to_one_minute_grace_and_no_biometric()
    {
        var s = Fresh();
        Assert.Equal(1, s.GraceMinutes);
        Assert.False(s.BiometricEnabled);

        s.SetPin("1234");
        s.BiometricEnabled = true;
        Assert.True(s.BiometricEnabled);
        s.Disable();
        Assert.False(s.BiometricEnabled);   // вимкнення замка скидає і біометрію
    }
}
