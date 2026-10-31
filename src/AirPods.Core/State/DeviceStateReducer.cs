using AirPods.Core.Bluetooth;

namespace AirPods.Core.State;

/// <summary>
/// Pure state transitions: sample in, new state out. No clock of its own, no UI, no I/O —
/// callers pass "now", which makes every staleness rule testable to the millisecond.
/// </summary>
public static class DeviceStateReducer
{
    /// <summary>How long a pair may stay silent before its numbers stop being shown.</summary>
    public static readonly TimeSpan DefaultStaleness = TimeSpan.FromSeconds(60);

    public static DeviceState Apply(
        DeviceState previous,
        ProximityPairingSample sample,
        TimeSpan? staleness = null)
    {
        // A late sample that arrives after the state expired still counts: the device is here now.
        var advert = sample.Advertisement;

        return previous with
        {
            ModelName = advert.ModelName,
            IsPresent = true,
            LastSeenAt = sample.ReceivedAt,
            LeftBatteryPercent = advert.LeftBatteryPercent,
            RightBatteryPercent = advert.RightBatteryPercent,
            CaseBatteryPercent = advert.CaseBatteryPercent,
            LeftCharging = advert.LeftCharging,
            RightCharging = advert.RightCharging,
            CaseCharging = advert.CaseCharging,
            LeftInEar = advert.LeftInEar,
            RightInEar = advert.RightInEar,
            LidOpen = advert.LidOpen,
        };
    }

    /// <summary>
    /// Ages the state. Past the staleness window the measurements are dropped rather than
    /// frozen — "unknown" is honest, a two-minute-old battery reading is not.
    /// </summary>
    public static DeviceState Expire(DeviceState state, DateTimeOffset now, TimeSpan? staleness = null)
    {
        if (!state.IsPresent || state.LastSeenAt is null)
        {
            return state;
        }

        var window = staleness ?? DefaultStaleness;
        if (now - state.LastSeenAt.Value <= window)
        {
            return state;
        }

        return state with
        {
            IsPresent = false,
            LeftBatteryPercent = null,
            RightBatteryPercent = null,
            CaseBatteryPercent = null,
            LeftCharging = false,
            RightCharging = false,
            CaseCharging = false,
            LeftInEar = false,
            RightInEar = false,
            LidOpen = false,
        };
    }
}
