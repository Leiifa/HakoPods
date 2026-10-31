using System.IO.Compression;
using HakoPods.Core.Diagnostics;
using HakoPods.Core.Logging;
using Xunit;

namespace HakoPods.Core.Tests.Diagnostics;

public class FileLoggerTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "apw-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Writes_a_readable_line()
    {
        var logger = new FileLogger(_directory);

        logger.Info("hello");
        logger.Error("boom", new InvalidOperationException("nope"));

        var text = File.ReadAllText(logger.CurrentFile);
        Assert.Contains("hello", text);
        Assert.Contains("INFO", text);
        Assert.Contains("boom", text);
        Assert.Contains("InvalidOperationException", text);
    }

    [Fact]
    public void Rolls_and_keeps_a_bounded_number_of_files()
    {
        var logger = new FileLogger(_directory, maxBytes: 200, maxBackups: 2);

        for (var i = 0; i < 40; i++)
        {
            logger.Info($"line {i} with a little padding to fill the file");
        }

        var files = Directory.GetFiles(_directory, "app.log*");
        Assert.True(files.Length >= 2, "expected at least one roll");
        Assert.True(files.Length <= 3, $"expected at most 3 files, found {files.Length}");
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}

public class DiagnosticsExporterTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "apw-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Zip_contains_settings_logs_and_version()
    {
        Directory.CreateDirectory(Path.Combine(_directory, "logs"));
        File.WriteAllText(Path.Combine(_directory, "settings.json"), "{}");
        File.WriteAllText(Path.Combine(_directory, "logs", "app.log"), "line");
        File.WriteAllText(Path.Combine(_directory, "logs", "app.log.1"), "old");

        var zipPath = Path.Combine(_directory, "diagnostics.zip");
        DiagnosticsExporter.Export(
            zipPath,
            Path.Combine(_directory, "settings.json"),
            Path.Combine(_directory, "logs"),
            "0.1.0");

        using var archive = ZipFile.OpenRead(zipPath);
        var names = archive.Entries.Select(e => e.FullName).ToList();

        Assert.Contains("settings.json", names);
        Assert.Contains("logs/app.log", names);
        Assert.Contains("logs/app.log.1", names);
        Assert.Contains("version.txt", names);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
