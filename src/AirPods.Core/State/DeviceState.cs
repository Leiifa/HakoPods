using AirPods.Core.Advertisements;

namespace AirPods.Core.State;

/// <summary>
/// What the product knows about one pair right now. Immutable: every update produces a new
/// instance, so callers can compare snapshots and the UI can diff them.
///
/// <see cref="IsPresent"/> is false when the data is stale — in that case every measurement is
/// <c>null</c> or false, because a stale number is worse than no number. The model name and the
/// last-seen timestamp survive, so the UI can still say "AirPods · not nearby".
/// </summary>
public sealed record DeviceState
{
    public static readonly DeviceState Empty = new();

    public string? ModelName { get; init; }

    public bool IsPresent { get; init; }

    public DateTimeOffset? LastSeenAt { get; init; }

    public int? LeftBatteryPercent { get; init; }

    public int? RightBatteryPercent { get; init; }

    public int? CaseBatteryPercent { get; init; }

    public bool LeftCharging { get; init; }

    public bool RightCharging { get; init; }

    public bool CaseCharging { get; init; }

    public bool LeftInEar { get; init; }

    public bool RightInEar { get; init; }

    /// <summary>Lid state, only meaningful while the pods are in the case.</summary>
    public bool LidOpen { get; init; }

    public bool AnyInEar => LeftInEar || RightInEar;

    /// <summary>True when at least one component reports a readable level.</summary>
    public bool HasAnyBattery =>
        LeftBatteryPercent is not null || RightBatteryPercent is not null || CaseBatteryPercent is not null;
}
