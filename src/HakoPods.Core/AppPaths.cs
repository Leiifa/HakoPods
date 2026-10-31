namespace HakoPods.Core;

/// <summary>Single place that knows where the app is allowed to write.</summary>
public static class AppPaths
{
    public const string FolderName = "AirPodsWindows";

    public static string Root => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        FolderName);

    public static string SettingsFile => Path.Combine(Root, "settings.json");

    public static string LogDirectory => Path.Combine(Root, "logs");

    public static string DiagnosticsDirectory => Path.Combine(Root, "diagnostics");
}
