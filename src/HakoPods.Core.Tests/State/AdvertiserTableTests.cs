using HakoPods.Core.State;
using Xunit;

namespace HakoPods.Core.Tests.State;

public class AdvertiserTableTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

    private static DeviceState State(bool leftInEar = false, bool rightInEar = false, int? left = 90) => new()
    {
        ModelName = "AirPods Pro",
        IsPresent = true,
        LastSeenAt = Now,
        LeftBatteryPercent = left,
        RightBatteryPercent = 80,
        LeftInEar = leftInEar,
        RightInEar = rightInEar,
    };

    [Fact]
    public void Prefers_the_advertiser_whose_ear_bits_keep_repeating()
    {
        var table = new AdvertiserTable();

        // A: one packet claiming both buds, then silent.
        table.Observe(1, State(leftInEar: true, rightInEar: true), -40, Now);
        // B: three packets agreeing that only the right bud is worn.
        table.Observe(2, State(rightInEar: true), -60, Now);
        table.Observe(2, State(rightInEar: true), -60, Now);
        table.Observe(2, State(rightInEar: true), -60, Now);

        var best = table.Best(Now, TimeSpan.FromSeconds(30));

        Assert.NotNull(best);
        Assert.False(best!.LeftInEar);
        Assert.True(best.RightInEar);
    }

    [Fact]
    public void Falls_back_to_signal_strength_when_stability_is_equal()
    {
        var table = new AdvertiserTable();
        table.Observe(1, State(left: 50), -70, Now);
        table.Observe(2, State(left: 90), -45, Now);

        var best = table.Best(Now, TimeSpan.FromSeconds(30));

        Assert.Equal(90, best!.LeftBatteryPercent);
    }

    [Fact]
    public void Ignores_an_advertiser_that_has_gone_quiet()
    {
        var table = new AdvertiserTable();
        table.Observe(1, State(left: 11), -40, Now - TimeSpan.FromSeconds(120));
        table.Observe(2, State(left: 77), -70, Now);

        var best = table.Best(Now, TimeSpan.FromSeconds(30));

        Assert.Equal(77, best!.LeftBatteryPercent);
    }

    [Fact]
    public void A_changed_ear_state_resets_stability()
    {
        var table = new AdvertiserTable();
        table.Observe(1, State(rightInEar: true), -50, Now);
        table.Observe(1, State(rightInEar: true), -50, Now);
        table.Observe(1, State(leftInEar: true), -40, Now); // flipped: no longer consistent

        table.Observe(2, State(rightInEar: true), -60, Now);
        table.Observe(2, State(rightInEar: true), -60, Now);

        var best = table.Best(Now, TimeSpan.FromSeconds(30));

        Assert.NotNull(best);
        Assert.False(best!.LeftInEar);
        Assert.True(best.RightInEar);
    }
}
