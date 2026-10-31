namespace AirPods.Core.Advertisements;

/// <summary>
/// Product IDs carried in Apple's proximity-pairing advertisement. Values are the
/// little-endian u16 at payload offsets 3–4; only 0x2024 and 0x2027 are verified on
/// hardware in the source project, so unknown IDs must degrade gracefully.
/// See docs/RESEARCH.md §5.3.
/// </summary>
public static class AirPodsModels
{
    private static readonly Dictionary<ushort, string> Names = new()
    {
        [0x2002] = "AirPods (1st gen)",
        [0x200F] = "AirPods (2nd gen)",
        [0x2013] = "AirPods (3rd gen)",
        [0x2019] = "AirPods 4",
        [0x201B] = "AirPods 4 (ANC)",
        [0x200E] = "AirPods Pro",
        [0x2014] = "AirPods Pro (2nd gen)",
        [0x2024] = "AirPods Pro (2nd gen, USB-C)",
        [0x2027] = "AirPods Pro 3",
        [0x200A] = "AirPods Max",
        [0x201F] = "AirPods Max (USB-C)",
    };

    public static bool IsKnown(ushort productId) => Names.ContainsKey(productId);

    /// <summary>Display name, or <c>Unknown (0xXXXX)</c> so the UI can still say something.</summary>
    public static string GetName(ushort productId) =>
        Names.TryGetValue(productId, out var name) ? name : $"Unknown (0x{productId:X4})";
}
