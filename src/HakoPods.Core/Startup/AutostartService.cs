using Microsoft.Win32;

namespace HakoPods.Core.Startup;

/// <summary>
/// Start-with-Windows via the per-user Run key. Per-user on purpose: the app needs no admin
/// rights anywhere else, and a machine-wide entry would.
/// </summary>
public static class AutostartService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "HakoPods";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
        return key?.GetValue(ValueName) is string value && value.Length > 0;
    }

    /// <param name="executablePath">Absolute path to the exe; quoted so spaces survive.</param>
    public static void Set(bool enabled, string executablePath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true)
            ?? throw new InvalidOperationException("Cannot open the Run key.");

        if (enabled)
        {
            key.SetValue(ValueName, $"\"{executablePath}\"", RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    /// <summary>Keeps the registry honest when the app is moved or deleted.</summary>
    public static void SyncWith(bool shouldStart, string executablePath)
    {
        var registered = IsEnabled();
        if (shouldStart && !registered)
        {
            Set(true, executablePath);
        }
        else if (!shouldStart && registered)
        {
            Set(false, executablePath);
        }
    }
}
