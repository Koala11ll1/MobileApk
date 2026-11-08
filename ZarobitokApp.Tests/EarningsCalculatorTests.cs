using ZarobitokApp.Models;
using ZarobitokApp.Services;

namespace ZarobitokApp.Tests;

public class EarningsCalculatorTests
{
    private static readonly DateTime Start = new(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc);

    private static ShiftState Running(decimal rate = 100m, int breakMin = 0, double planned = 8) => new()
    {
        RatePerHour = rate,
        UnpaidBreakMinutes = breakMin,
        PlannedHours = planned,
        StartedAtUtc = Start
    };

    [Fact]
    public void EarnedThisShift_is_rate_times_paid_hours()
        => Assert.Equal(200m, EarningsCalculator.EarnedThisShift(Running(), Start.AddHours(2)));

    [Fact]
    public void Break_is_subtracted_and_never_goes_negative()
    {
        var s = Running(breakMin: 30);
        Assert.Equal(TimeSpan.FromMinutes(30), EarningsCalculator.PaidElapsed(s, Start.AddHours(1)));
        Assert.Equal(TimeSpan.Zero, EarningsCalculator.PaidElapsed(s, Start.AddMinutes(10)));
        Assert.Equal(0m, EarningsCalculator.EarnedThisShift(s, Start.AddMinutes(10)));
    }

    [Fact]
    public void Idle_shift_earns_nothing()
        => Assert.Equal(0m, EarningsCalculator.EarnedThisShift(new ShiftState(), Start));

    [Fact]
    public void PlannedEnd_is_start_plus_break_plus_planned_hours()
        => Assert.Equal(Start.AddMinutes(30).AddHours(8), EarningsCalculator.PlannedEnd(Running(breakMin: 30)));

    [Fact]
    public void Paid_time_at_PlannedEnd_equals_planned_hours()
    {
        var s = Running(breakMin: 45, planned: 7.5);
        Assert.Equal(TimeSpan.FromHours(7.5), EarningsCalculator.PaidElapsed(s, EarningsCalculator.PlannedEnd(s)));
    }

    [Fact]
    public void Ticks_are_summed_inside_the_range_inclusive_and_use_current_prices()
    {
        var item = new ExtraItem(Guid.NewGuid(), "Red Bull", 60m);
        var ticks = new[]
        {
            new ExtraTick(Guid.NewGuid(), item.Id, Start, +1),                    // on the lower bound
            new ExtraTick(Guid.NewGuid(), item.Id, Start.AddHours(1), +1),
            new ExtraTick(Guid.NewGuid(), item.Id, Start.AddHours(2), -1),        // on the upper bound
            new ExtraTick(Guid.NewGuid(), item.Id, Start.AddHours(3), +5),        // outside
            new ExtraTick(Guid.NewGuid(), item.Id, Start.AddHours(-1), +5),       // outside
        };

        Assert.Equal(60m, EarningsCalculator.TicksTotal(new[] { item }, ticks, Start, Start.AddHours(2)));
    }

    [Fact]
    public void Ticks_of_an_unknown_item_count_as_zero()
    {
        var ticks = new[] { new ExtraTick(Guid.NewGuid(), Guid.NewGuid(), Start, +3) };
        Assert.Equal(0m, EarningsCalculator.TicksTotal(Array.Empty<ExtraItem>(), ticks, Start, Start.AddHours(1)));
    }

    [Fact]
    public void Progress_is_clamped_to_0_1_and_safe_for_zero_plan()
    {
        Assert.Equal(0.25, EarningsCalculator.Progress(Running(), Start.AddHours(2)), 6);
        Assert.Equal(1.0, EarningsCalculator.Progress(Running(), Start.AddHours(20)));
        Assert.Equal(0.0, EarningsCalculator.Progress(Running(planned: 0), Start.AddHours(1)));
    }

    [Fact]
    public void FormatDuration_does_not_wrap_past_24_hours()
        => Assert.Equal("30:05:09", EarningsCalculator.FormatDuration(new TimeSpan(30, 5, 9)));
}
