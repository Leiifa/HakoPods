using System.IO.Compression;

namespace AirPods.Core.Diagnostics;

/// <summary>
/// Builds a local zip with the logs, the settings file and a version stamp. Nothing is
/// uploaded: the user chooses what to do with the file (ADR-010).
/// </summary>
public static class DiagnosticsExporter
{
    public static string Export(
        string destinationZip,
        string settingsFile,
        string logDirectory,
        string appVersion)
    {
        var directory = Path.GetDirectoryName(destinationZip);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (File.Exists(destinationZip))
        {
            File.Delete(destinationZip);
        }

        using var archive = ZipFile.Open(destinationZip, ZipArchiveMode.Create);

        if (File.Exists(settingsFile))
        {
            archive.CreateEntryFromFile(settingsFile, "settings.json");
        }

        if (Directory.Exists(logDirectory))
        {
            foreach (var file in Directory.EnumerateFiles(logDirectory))
            {
                archive.CreateEntryFromFile(file, $"logs/{Path.GetFileName(file)}");
            }
        }

        var info = archive.CreateEntry("version.txt");
        using var writer = new StreamWriter(info.Open());
        writer.Write($"AirPods for Windows {appVersion}{Environment.NewLine}");
        writer.Write($"exported {DateTimeOffset.Now:O}{Environment.NewLine}");
        writer.Write($"os {Environment.OSVersion.VersionString}{Environment.NewLine}");

        return destinationZip;
    }
}
