using HakoPods.Core.Advertisements;
using Xunit;

namespace HakoPods.Core.Tests.Advertisements;

/// <summary>
/// Model coverage and the two decode traps that cost real bugs: mirrored nibbles when the right
/// pod is primary, and values that mean "unknown" rather than zero.
/// </summary>
public class ProximityPairingModelTests
{
    public static TheoryData<ushort, string> KnownModels => new()
    {
        { 0x2002, "AirPods (1st gen)" },
        { 0x200F, "AirPods (2nd gen)" },
        { 0x2013, "AirPods (3rd gen)" },
        { 0x2019, "AirPods 4" },
        { 0x201B, "AirPods 4 (ANC)" },
        { 0x200E, "AirPods Pro" },
        { 0x2014, "AirPods Pro (2nd gen)" },
        { 0x2024, "AirPods Pro (2nd gen, USB-C)" },
        { 0x2027, "AirPods Pro 3" },
        { 0x200A, "AirPods Max" },
        { 0x201F, "AirPods Max (USB-C)" },
    };

    [Theory]
    [MemberData(nameof(KnownModels))]
    public void Every_documented_model_decodes_to_its_name(ushort productId, string expected)
    {
        var advert = Parse(Packet(status: 0x01, battery: 0x88, productId: productId));

        Assert.NotNull(advert);
        Assert.Equal(productId, advert!.ProductId);
        Assert.Equal(expected, advert.ModelName);
        Assert.True(AirPodsModels.IsKnown(productId));
    }

    [Fact]
    public void Unknown_model_still_names_itself_instead_of_throwing()
    {
        var advert = Parse(Packet(status: 0x01, battery: 0x88, productId: 0x2999));

        Assert.NotNull(advert);
        Assert.Equal("Unknown (0x2999)", advert!.ModelName);
        Assert.False(AirPodsModels.IsKnown(0x2999));
    }

    [Fact]
    public void The_primary_pod_mirrors_the_nibbles()
    {
        // Status bit 0x20 set = left primary, so the low nibble is the left bud.
        var leftPrimary = Parse(Packet(status: 0x20, battery: 0x37));

        // Same bytes, right primary: the nibbles swap.
        var rightPrimary = Parse(Packet(status: 0x00, battery: 0x37));

        Assert.NotNull(leftPrimary);
        Assert.NotNull(rightPrimary);

        Assert.Equal(70, leftPrimary!.LeftBatteryPercent);
        Assert.Equal(30, leftPrimary.RightBatteryPercent);

        Assert.Equal(30, rightPrimary!.LeftBatteryPercent);
        Assert.Equal(70, rightPrimary.RightBatteryPercent);
    }

    [Fact]
    public void Unknown_battery_nibbles_become_null_not_zero()
    {
        // 0xF means "not reported"; a left bud at 0 % is a different, real value.
        var leftUnknown = Parse(Packet(status: 0x20, battery: 0x0F));
        var rightUnknown = Parse(Packet(status: 0x20, battery: 0xF0));
        var bothUnknown = Parse(Packet(status: 0x20, battery: 0xFF));

        Assert.Null(leftUnknown!.LeftBatteryPercent);
        Assert.Null(rightUnknown!.RightBatteryPercent);
        Assert.Null(bothUnknown!.LeftBatteryPercent);
        Assert.Null(bothUnknown.RightBatteryPercent);
    }

    [Theory]
    [InlineData(0x0B)] // 11 after the charging flag is masked out
    [InlineData(0x0F)] // 11 as well
    public void Case_levels_above_ten_become_null(byte highNibble)
    {
        var advert = Parse(Packet(status: 0x20, battery: 0x88, charging: (byte)(highNibble << 4)));

        Assert.NotNull(advert);
        Assert.Null(advert!.CaseBatteryPercent);
    }

    [Fact]
    public void The_charging_flag_does_not_disturb_the_case_level()
    {
        // 0xD9 = 9 with the 0x40 flag set: charging, still 90 %.
        var advert = Parse(Packet(status: 0x20, battery: 0x88, charging: 0xD9));

        Assert.NotNull(advert);
        Assert.True(advert!.CaseCharging);
        Assert.Equal(90, advert.CaseBatteryPercent);
    }

    [Theory]
    [InlineData(0x00, false, false)]  // no ear bits at all
    [InlineData(0x02, false, true)]   // measured: right worn, right primary
    [InlineData(0x22, true, false)]   // measured: left worn, left primary
    [InlineData(0x0B, true, true)]    // measured: both worn
    public void Ear_bits_follow_the_primary_pod(byte status, bool leftEar, bool rightEar)
    {
        var advert = Parse(Packet(status: status, battery: 0x88));

        Assert.NotNull(advert);
        Assert.Equal(leftEar, advert!.LeftInEar);
        Assert.Equal(rightEar, advert.RightInEar);
    }

    private static ProximityPairingAdvertisement? Parse(byte[] payload) =>
        ProximityPairingParser.Parse(payload);

    private static byte[] Packet(
        byte status,
        byte battery,
        byte charging = 0x00,
        byte lid = 0x01,
        byte connection = 0x04,
        ushort productId = 0x200E)
    {
        var buffer = new byte[27];
        buffer[0] = 0x07;
        buffer[1] = 0x19;
        buffer[2] = 0x01;
        buffer[3] = (byte)(productId & 0xFF);
        buffer[4] = (byte)(productId >> 8);
        buffer[5] = status;
        buffer[6] = battery;
        buffer[7] = charging;
        buffer[8] = lid;
        buffer[9] = connection;
        return buffer;
    }
}
