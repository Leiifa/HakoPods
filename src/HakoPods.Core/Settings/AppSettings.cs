namespace HakoPods.Core.Settings;

/// <summary>
/// User preferences. Versioned so a future release can migrate instead of guessing; unknown
/// JSON members are ignored, missing members fall back to these defaults.
/// </summary>
public sealed record AppSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>Paired device the user chose, stored by Bluetooth address. Identifying an
    /// advertisement by address is impossible, so this is only used to connect and to label.</summary>
    public string? PreferredDeviceAddress { get; init; }

    // On by default: automatic connection is the reason this app exists. Users can still
    // switch it off, but nobody should have to find a checkbox to get the headline feature.
    public bool AutoConnectOnLidOpen { get; init; } = true;

    public bool SwitchDefaultAudioEndpoint { get; init; } = true;

    /// <summary>Endpoint id learned when the user picked the AirPods as output.</summary>
    public string? PreferredAudioEndpointId { get; init; }

    /// <summary>Where audio was playing before the AirPods arrived; restored on disconnect.</summary>
    public string? PreviousAudioEndpointId { get; init; }

    public bool DisableHandsFreeProfile { get; init; }


    public bool StartWithWindows { get; init; }

    /// <summary>Signal strength above which a pair counts as "near" (measured 2026-10-31).</summary>
    public int NearThresholdDbm { get; init; } = -72;
}
