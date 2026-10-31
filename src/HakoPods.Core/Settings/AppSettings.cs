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

    public bool AutoConnectOnLidOpen { get; init; }

    public bool SwitchDefaultAudioEndpoint { get; init; } = true;

    public bool DisableHandsFreeProfile { get; init; }


    public bool StartWithWindows { get; init; }

    /// <summary>Signal strength above which a pair counts as "near" (measured 2026-10-31).</summary>
    public int NearThresholdDbm { get; init; } = -72;
}
