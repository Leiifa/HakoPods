namespace HakoPods.Core.Advertisements;

/// <summary>
/// Decodes Apple's BLE proximity-pairing payload (manufacturer data after the 0x004C
/// company ID). Pure, allocation-light and hardware-free: every branch is testable with a
/// captured byte array.
///
/// Layout (docs/RESEARCH.md §5.2, verified on AirPods Pro 2026-10-31):
///   0 type=0x07 · 1 length · 2 prefix · 3–4 product id (LE) · 5 status · 6 battery
///   7 charging+case · 8 lid · 9 colour · 10 connection · 11–26 encrypted
/// </summary>
public static class ProximityPairingParser
{
    public const byte MessageType = 0x07;

    public const int MinimumLength = 11;

    // Status byte flags.
    private const byte InEarSecondary = 0x08;
    private const byte InEarPrimary = 0x02;
    private const byte PrimaryPodLeft = 0x20;

    // Charging byte flags.
    private const byte CaseChargingBit = 0x02;
    private const byte RightChargingBit = 0x04;
    private const byte LeftChargingBit = 0x08;

    private const byte LidOpenBit = 0x08;

    /// <summary>
    /// Decodes one payload. Returns <c>null</c> when the buffer is too short or is not a
    /// proximity-pairing message — callers treat that as "not for us", not as an error.
    /// </summary>
    public static ProximityPairingAdvertisement? Parse(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < MinimumLength || payload[0] != MessageType)
        {
            return null;
        }

        var productId = (ushort)(payload[3] | (payload[4] << 8));
        var status = payload[5];
        var primaryIsLeft = (status & PrimaryPodLeft) != 0;

        var battery = payload[6];
        var leftNibble = battery & 0x0F;
        var rightNibble = (battery >> 4) & 0x0F;

        var charging = payload[7];
        var caseNibble = (charging >> 4) & 0x0F;
        var leftCharging = (charging & LeftChargingBit) != 0;
        var rightCharging = (charging & RightChargingBit) != 0;
        var caseCharging = (charging & CaseChargingBit) != 0;

        var inEarPrimary = (status & InEarPrimary) != 0;
        var inEarSecondary = (status & InEarSecondary) != 0;

        if (!primaryIsLeft)
        {
            // The payload mirrors left/right when the right pod is primary.
            (leftNibble, rightNibble) = (rightNibble, leftNibble);
            (leftCharging, rightCharging) = (rightCharging, leftCharging);
        }

        var lid = payload[8];

        return new ProximityPairingAdvertisement
        {
            ProductId = productId,
            ModelName = AirPodsModels.GetName(productId),
            LeftBatteryPercent = ToPercent(leftNibble),
            RightBatteryPercent = ToPercent(rightNibble),
            CaseBatteryPercent = ToPercent(caseNibble),
            LeftCharging = leftCharging,
            RightCharging = rightCharging,
            CaseCharging = caseCharging,
            LeftInEar = primaryIsLeft ? inEarPrimary : inEarSecondary,
            RightInEar = primaryIsLeft ? inEarSecondary : inEarPrimary,
            LidOpen = (lid & LidOpenBit) == 0,
            PrimaryPodIsLeft = primaryIsLeft,
            ConnectionState = payload[10],
            ColorCode = payload[9],
            StatusByte = status,
        };
    }

    /// <summary>10 % steps; anything above 10 is "unknown", never a percentage.</summary>
    private static int? ToPercent(int nibble) => nibble <= 10 ? nibble * 10 : null;
}
