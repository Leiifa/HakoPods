using AirPods.Core.Advertisements;
using AirPods.Core.Bluetooth;
using AirPods.Core.State;
using Xunit;

namespace AirPods.Core.Tests.State;

public class DeviceStateReducerTests
{
    private const string LidOpenPacket = "0719010E2001448F010005B731B49FAEC4F527095EE9A21509499B";
    private const string LidClosedPacket = "0719010E205544B05A000420933F41C57FF91AEAE822A26A058D42";
    private const string RightBudInEarPacket = "0719010E2003448F0100054A842B687A868AD875D3E7EBBA5B0763";

    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;

    [Fact]
    public void Applies_the_first_sample()
    {
        var state = DeviceStateReducer.Apply(DeviceState.Empty, Sample(LidOpenPacket, T0));

        Assert.True(state.IsPresent);
        Assert.Equal("AirPods Pro", state.ModelName);
        Assert.Equal(40, state.LeftBatteryPercent);
        Assert.Equal(40, state.RightBatteryPercent);
        Assert.Equal(80, state.CaseBatteryPercent);
        Assert.True(state.LidOpen);
        Assert.True(state.CaseCharging);
        Assert.True(state.HasAnyBattery);
        Assert.Equal(T0, state.LastSeenAt);
    }

    [Fact]
    public void Closing_the_lid_updates_the_state()
    {
        var open = DeviceStateReducer.Apply(DeviceState.Empty, Sample(LidOpenPacket, T0));
        var closed = DeviceStateReducer.Apply(open, Sample(LidClosedPacket, T0.AddSeconds(5)));

        Assert.True(open.LidOpen);
        Assert.False(closed.LidOpen);
        // Case 0xB in this packet means "not readable", so it must not be clamped to 110 %.
        Assert.Null(closed.CaseBatteryPercent);
    }

    [Fact]
    public void Removing_a_bud_clears_its_ear_flag()
    {
        var inEar = DeviceStateReducer.Apply(DeviceState.Empty, Sample(RightBudInEarPacket, T0));
        var removed = DeviceStateReducer.Apply(inEar, Sample(LidOpenPacket, T0.AddSeconds(5)));

        Assert.True(inEar.RightInEar);
        Assert.True(inEar.AnyInEar);

        Assert.False(removed.RightInEar);
        Assert.False(removed.LeftInEar);
        Assert.False(removed.AnyInEar);
    }

    [Fact]
    public void Reports_case_charging_from_the_packet()
    {
        var charging = DeviceStateReducer.Apply(DeviceState.Empty, Sample(LidOpenPacket, T0));
        // Synthetic idle packet: charging byte 0x00 clears every charging bit.
        var idle = DeviceStateReducer.Apply(
            charging,
            Sample(Packet(status: 0x01, battery: 0x44, charging: 0x00), T0.AddSeconds(5)));

        Assert.True(charging.CaseCharging);
        Assert.False(idle.CaseCharging);
        Assert.False(idle.LeftCharging);
        Assert.False(idle.RightCharging);
    }

    [Fact]
    public void Orientation_flip_keeps_left_and_right_on_the_right_sides()
    {
        // Synthetic: status 0x01 → right pod primary → the payload's nibbles are mirrored.
        var packet = Packet(status: 0x01, battery: 0x3A);

        var state = DeviceStateReducer.Apply(DeviceState.Empty, Sample(packet, T0));

        Assert.Equal(30, state.LeftBatteryPercent);
        Assert.Equal(100, state.RightBatteryPercent);
    }

    [Fact]
    public void Expires_into_unknown_after_the_staleness_window()
    {
        var state = DeviceStateReducer.Apply(DeviceState.Empty, Sample(LidOpenPacket, T0));

        var expired = DeviceStateReducer.Expire(state, T0.AddSeconds(61));

        Assert.False(expired.IsPresent);
        Assert.Null(expired.LeftBatteryPercent);
        Assert.Null(expired.RightBatteryPercent);
        Assert.Null(expired.CaseBatteryPercent);
        Assert.False(expired.LidOpen);
        Assert.False(expired.HasAnyBattery);
        // The identity and the timestamp survive so the UI can explain itself.
        Assert.Equal("AirPods Pro", expired.ModelName);
        Assert.Equal(T0, expired.LastSeenAt);
    }

    [Fact]
    public void Stays_present_inside_the_staleness_window()
    {
        var state = DeviceStateReducer.Apply(DeviceState.Empty, Sample(LidOpenPacket, T0));

        var still = DeviceStateReducer.Expire(state, T0.AddSeconds(59));

        Assert.True(still.IsPresent);
        Assert.Equal(40, still.LeftBatteryPercent);
        Assert.Same(state, still);
    }

    [Fact]
    public void Expiring_an_empty_state_changes_nothing()
    {
        var expired = DeviceStateReducer.Expire(DeviceState.Empty, T0.AddHours(1));

        Assert.Same(DeviceState.Empty, expired);
        Assert.False(expired.IsPresent);
    }

    private static ProximityPairingSample Sample(string hex, DateTimeOffset at, short rssi = -50) =>
        Sample(Convert.FromHexString(hex), at, rssi);

    private static ProximityPairingSample Sample(byte[] payload, DateTimeOffset at, short rssi = -50)
    {
        var advert = ProximityPairingParser.Parse(payload);
        Assert.NotNull(advert);
        return new ProximityPairingSample(advert, 0x6BE0048EE8FD, rssi, at);
    }

    private static byte[] Packet(byte status, byte battery, byte charging = 0x00, byte lid = 0x01)
    {
        var bytes = new byte[27];
        bytes[0] = 0x07;
        bytes[1] = 0x19;
        bytes[2] = 0x01;
        bytes[3] = 0x0E;
        bytes[4] = 0x20;
        bytes[5] = status;
        bytes[6] = battery;
        bytes[7] = charging;
        bytes[8] = lid;
        return bytes;
    }
}
