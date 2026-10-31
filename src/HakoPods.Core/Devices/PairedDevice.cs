using System.Globalization;

namespace HakoPods.Core.Devices;

/// <summary>
/// A Bluetooth device the user has paired with Windows.
/// <para>
/// The address is stable across re-pairing, which is why it is what gets persisted. It is
/// <em>not</em> usable to identify advertisements — those arrive from rotating random addresses
/// (docs/research/IDENTITY.md).
/// </para>
/// </summary>
public sealed record PairedDevice(string Name, ulong Address)
{
    /// <summary>12 upper-case hex digits, no separators — the form stored in settings.</summary>
    public string AddressText => Address.ToString("X12", CultureInfo.InvariantCulture);

    public bool LooksLikeAirPods => Name.Contains("AirPods", StringComparison.OrdinalIgnoreCase);

    /// <summary>Parses the Windows AEP address form, e.g. <c>74:65:0C:77:CE:1F</c>.</summary>
    public static bool TryParseWindowsAddress(string? value, out ulong address)
    {
        address = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var hex = value.Replace(":", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Trim();

        return hex.Length is > 0 and <= 12
            && ulong.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out address);
    }
}
