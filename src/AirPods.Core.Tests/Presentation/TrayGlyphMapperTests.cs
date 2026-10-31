using AirPods.Core.Presentation;
using AirPods.Core.State;
using Xunit;

namespace AirPods.Core.Tests.Presentation;

public class TrayGlyphMapperTests
{
    private static DeviceState Present(int? left = null, int? right = null, int? casePercent = null) => new()
    {
        ModelName = "AirPods Pro",
        IsPresent = true,
        LeftBatteryPercent = left,
        RightBatteryPercent = right,
        CaseBatteryPercent = casePercent,
        LastSeenAt = DateTimeOffset.Now,
    };

    [Fact]
    public void NotPresent_IsDisconnected_EvenWhenStaleNumbersSurvive()
    {
        var state = Present() with { IsPresent = false, LeftBatteryPercent = 90 };

        Assert.Equal(TrayGlyph.Disconnected, TrayGlyphMapper.From(state));
    }

    [Fact]
    public void PresentWithNoReadableLevel_IsUnknown_NotFull()
    {
        Assert.Equal(TrayGlyph.Unknown, TrayGlyphMapper.From(Present()));
    }

    [Fact]
    public void LowBud_OutranksChargingCase()
    {
        var state = Present(left: 5, right: 90, casePercent: 80) with { CaseCharging = true };

        Assert.Equal(TrayGlyph.Low, TrayGlyphMapper.From(state));
    }

    [Theory]
    [InlineData(10)]
    [InlineData(9)]
    [InlineData(0)]
    public void AtOrBelowTenPercent_IsLow(int level)
    {
        Assert.Equal(TrayGlyph.Low, TrayGlyphMapper.From(Present(left: level, right: 80)));
    }

    [Fact]
    public void ChargingWithHealthyLevels_IsCharging()
    {
        var state = Present(left: 40, right: 45, casePercent: 30) with { LeftCharging = true };

        Assert.Equal(TrayGlyph.Charging, TrayGlyphMapper.From(state));
    }

    [Fact]
    public void HealthyAndNotCharging_IsFull()
    {
        Assert.Equal(TrayGlyph.Full, TrayGlyphMapper.From(Present(left: 80, right: 79, casePercent: 55)));
    }

    [Fact]
    public void Describe_UsesEmDashForAnUnknownBud()
    {
        var text = TrayGlyphMapper.Describe(Present(left: 80, casePercent: 55));

        Assert.Contains("L 80 %", text);
        Assert.DoesNotContain("R", text);
        Assert.Contains("Case 55 %", text);
    }

    [Fact]
    public void Describe_SaysNotNearby_WithoutInventingNumbers()
    {
        var text = TrayGlyphMapper.Describe(Present() with { IsPresent = false });

        Assert.Contains("not nearby", text);
        Assert.DoesNotContain("%", text);
    }
}
