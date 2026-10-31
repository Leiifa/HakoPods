namespace AirPods.Core.Advertisements;

/// <summary>
/// One decoded proximity-pairing advertisement.
///
/// Battery percentages are 10 % steps and <c>null</c> means "not readable right now"
/// (the payload uses nibble 0xF, and a case value above 10). Never clamp those to 0 or 100.
/// Left/right values are already resolved for the primary-pod flip.
/// </summary>
public sealed record ProximityPairingAdvertisement
{
    public ushort ProductId { get; init; }

    public string ModelName { get; init; } = AirPodsModels.GetName(0);

    public int? LeftBatteryPercent { get; init; }

    public int? RightBatteryPercent { get; init; }

    public int? CaseBatteryPercent { get; init; }

    public bool LeftCharging { get; init; }

    public bool RightCharging { get; init; }

    public bool CaseCharging { get; init; }

    public bool LeftInEar { get; init; }

    public bool RightInEar { get; init; }

    /// <summary>True when the lid is open. Only meaningful while the pods are in the case.</summary>
    public bool LidOpen { get; init; }

    /// <summary>Which pod is broadcasting as primary; the payload mirrors L/R when it is right.</summary>
    public bool PrimaryPodIsLeft { get; init; }

    /// <summary>0x00 disconnected, 0x04 idle, 0x05 media playing (observed values).</summary>
    public byte ConnectionState { get; init; }

    public byte ColorCode { get; init; }

    public byte StatusByte { get; init; }
}
