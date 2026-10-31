using HakoPods.Core.Advertisements;
using Xunit;

namespace HakoPods.Core.Tests.Advertisements;

/// <summary>
/// Fixtures are real packets from docs/captures (2026-10-31) unless marked synthetic.
/// </summary>
public class ProximityPairingParserTests
{
    // Captured: lid open, both pods out, all charging bits set, case 80 %.
    private const string CapturedLidOpen =
        "0719010E2001448F010005B731B49FAEC4F527095EE9A21509499B";

    // Captured: both pods in case, lid closed.
    private const string CapturedLidClosed =
        "0719010E205544B05A000420933F41C57FF91AEAE822A26A058D42";

    // Captured: right bud battery unreadable (nibble 0xF), case 90 %, nothing charging.
    private const string CapturedUnknownBudBattery =
        "0719010E2050F4901100005FE7F8B0BC60C89152C8DC6FEEFC0466";

    // Captured: case battery unreadable (nibble 0xB), lid open, connection idle.
    private const string CapturedUnknownCaseBattery =
        "0719010E205444B0510004F0FDBA510C56CE51A8D42F6778CDC0DD";

    // Captured: left pod inserted mid-capture (status bit 1 set, right primary).
    private const string CapturedBudInEar =
        "0719010E2003448F0100054A842B687A868AD875D3E7EBBA5B0763";

    [Fact]
    public void Parses_a_captured_packet()
    {
        var advert = Parse(CapturedLidOpen);

        Assert.NotNull(advert);
        Assert.Equal(0x200E, advert.ProductId);
        Assert.Equal("AirPods Pro", advert.ModelName);
        Assert.Equal(40, advert.LeftBatteryPercent);
        Assert.Equal(40, advert.RightBatteryPercent);
        Assert.Equal(80, advert.CaseBatteryPercent);
        // Measured 2026-10-31: 0x8f carries no charging flag at all (bit 6 is the only one that
        // moves on power), and bud charging is unproven, so it must not be claimed.
        Assert.False(advert.LeftCharging);
        Assert.False(advert.RightCharging);
        Assert.False(advert.CaseCharging);
        Assert.True(advert.LidOpen);
        Assert.Equal(0x05, advert.ConnectionState);
    }

    [Fact]
    public void Detects_a_closed_lid()
    {
        var advert = Parse(CapturedLidClosed);

        Assert.NotNull(advert);
        Assert.False(advert.LidOpen);
        Assert.Equal(0x55, advert.StatusByte);
    }

    [Fact]
    public void Unreadable_bud_battery_is_null_not_zero()
    {
        // Status 0x50 has bit 5 clear → right pod primary, so the raw nibbles are mirrored
        // and the unreadable 0xF lands on the left bud after resolution.
        var advert = Parse(CapturedUnknownBudBattery);

        Assert.NotNull(advert);
        Assert.False(advert.PrimaryPodIsLeft);
        Assert.Null(advert.LeftBatteryPercent);
        Assert.Equal(40, advert.RightBatteryPercent);
        Assert.Equal(90, advert.CaseBatteryPercent);
        Assert.False(advert.LeftCharging);
        Assert.False(advert.CaseCharging);
    }

    [Fact]
    public void Unreadable_case_battery_is_null_not_clamped()
    {
        var advert = Parse(CapturedUnknownCaseBattery);

        Assert.NotNull(advert);
        Assert.Null(advert.CaseBatteryPercent);
        Assert.Equal(40, advert.LeftBatteryPercent);
        Assert.Equal(40, advert.RightBatteryPercent);
    }

    [Fact]
    public void Swaps_batteries_when_the_right_pod_is_primary()
    {
        // Synthetic: status 0x01 has bit 5 clear → right pod primary → nibbles mirrored.
        var advert = Parse(Packet(status: 0x01, battery: 0x3A));

        Assert.NotNull(advert);
        Assert.False(advert.PrimaryPodIsLeft);
        Assert.Equal(30, advert.LeftBatteryPercent);
        Assert.Equal(100, advert.RightBatteryPercent);
    }

    [Fact]
    public void Case_charging_comes_from_bit_six_of_the_flags_byte()
    {
        // Captured with the lid open; only the charger differed: 0x9a unplugged, 0xda plugged.
        var unplugged = Parse(Packet(status: 0x73, battery: 0x9a, charging: 0x9a));
        var plugged = Parse(Packet(status: 0x73, battery: 0x9a, charging: 0xda));

        Assert.NotNull(unplugged);
        Assert.NotNull(plugged);
        Assert.False(unplugged.CaseCharging);
        Assert.True(plugged.CaseCharging);

        // The flag sits inside the case-battery nibble, so the level must not change with it.
        Assert.Equal(90, unplugged.CaseBatteryPercent);
        Assert.Equal(90, plugged.CaseBatteryPercent);
    }

    [Fact]
    public void Bud_charging_is_never_claimed_from_unverified_bits()
    {
        var advert = Parse(Packet(status: 0x01, battery: 0x33, charging: 0x04));

        Assert.NotNull(advert);
        Assert.False(advert.LeftCharging);
        Assert.False(advert.RightCharging);
    }

    [Fact]
    public void Unknown_product_id_degrades_gracefully()
    {
        var advert = Parse(Packet(status: 0x01, battery: 0x33, productId: 0xABCD));

        Assert.NotNull(advert);
        Assert.Equal(0xABCD, advert.ProductId);
        Assert.Equal("Unknown (0xABCD)", advert.ModelName);
        Assert.False(AirPodsModels.IsKnown(0xABCD));
        Assert.True(AirPodsModels.IsKnown(0x200E));
    }

    [Fact]
    public void Rejects_a_payload_that_is_too_short()
    {
        Assert.Null(ProximityPairingParser.Parse(new byte[10]));
    }

    [Fact]
    public void Rejects_a_different_message_type()
    {
        var packet = Packet(status: 0x01, battery: 0x33);
        packet[0] = 0x06;

        Assert.Null(ProximityPairingParser.Parse(packet));
    }

    [Fact]
    public void Maps_in_ear_flags_when_the_right_pod_is_primary()
    {
        // Synthetic: bit 1 = in ear (primary), bit 5 clear = right primary.
        var advert = Parse(Packet(status: 0x02, battery: 0x33));

        Assert.NotNull(advert);
        Assert.True(advert.RightInEar);
        Assert.False(advert.LeftInEar);
    }

    [Fact]
    public void Maps_in_ear_flags_when_the_left_pod_is_primary()
    {
        // Synthetic: bit 5 set = left primary, bit 1 = in ear (primary).
        var advert = Parse(Packet(status: 0x22, battery: 0x33));

        Assert.NotNull(advert);
        Assert.True(advert.LeftInEar);
        Assert.False(advert.RightInEar);
    }

    [Fact]
    public void Captured_bud_insertion_reports_the_right_bud_in_ear()
    {
        var advert = Parse(CapturedBudInEar);

        Assert.NotNull(advert);
        Assert.False(advert.PrimaryPodIsLeft);
        Assert.True(advert.RightInEar);
        Assert.False(advert.LeftInEar);
    }

    private static ProximityPairingAdvertisement? Parse(string hex) =>
        ProximityPairingParser.Parse(Hex(hex));

    private static ProximityPairingAdvertisement? Parse(byte[] payload) =>
        ProximityPairingParser.Parse(payload);

    private static byte[] Hex(string hex) => Convert.FromHexString(hex);

    private static byte[] Packet(
        byte status,
        byte battery,
        byte charging = 0x00,
        byte lid = 0x01,
        byte connection = 0x04,
        ushort productId = 0x200E)
    {
        var bytes = new byte[27];
        bytes[0] = 0x07;
        bytes[1] = 0x19;
        bytes[2] = 0x01;
        bytes[3] = (byte)(productId & 0xFF);
        bytes[4] = (byte)(productId >> 8);
        bytes[5] = status;
        bytes[6] = battery;
        bytes[7] = charging;
        bytes[8] = lid;
        bytes[9] = 0x00;
        bytes[10] = connection;
        return bytes;
    }
}
