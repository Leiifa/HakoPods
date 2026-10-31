namespace HakoPods.Core;

/// <summary>
/// Identity of the product, kept in one place so the UI, logs and diagnostics agree.
/// Real behaviour arrives with APW-102 onwards; this exists to give the skeleton one
/// testable seam.
/// </summary>
public static class AppInfo
{
    public const string ProductName = "HakoPods";

    public static Version Version { get; } = new(0, 1, 0);
}
