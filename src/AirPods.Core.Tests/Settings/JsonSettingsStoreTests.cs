using AirPods.Core.Logging;
using AirPods.Core.Settings;
using Xunit;

namespace AirPods.Core.Tests.Settings;

public class JsonSettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "apw-tests", Guid.NewGuid().ToString("N"));

    private string SettingsPath => Path.Combine(_directory, "settings.json");

    [Fact]
    public void Missing_file_yields_defaults()
    {
        var store = new JsonSettingsStore(SettingsPath);

        var settings = store.Load();

        Assert.Equal(AppSettings.CurrentSchemaVersion, settings.SchemaVersion);
        Assert.True(settings.SwitchDefaultAudioEndpoint);
        Assert.False(settings.AutoConnectOnLidOpen);
        Assert.Equal(-72, settings.NearThresholdDbm);
    }

    [Fact]
    public void Round_trips_every_value()
    {
        var store = new JsonSettingsStore(SettingsPath);
        var original = new AppSettings
        {
            AutoConnectOnLidOpen = true,
            DisableHandsFreeProfile = true,
            PreferredDeviceAddress = "74650C77CE1F",
            NearThresholdDbm = -80,
            StartWithWindows = true,
        };

        store.Save(original);
        var loaded = store.Load();

        Assert.Equal(original, loaded);
    }

    [Fact]
    public void Corrupt_file_falls_back_to_defaults_and_logs()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, "{ this is not json");
        var logger = new RecordingLogger();
        var store = new JsonSettingsStore(SettingsPath, logger);

        var settings = store.Load();

        Assert.Equal(AppSettings.CurrentSchemaVersion, settings.SchemaVersion);
        Assert.False(settings.AutoConnectOnLidOpen);
        Assert.Single(logger.Warnings);
    }

    [Fact]
    public void Save_is_atomic_and_leaves_no_temp_file()
    {
        var store = new JsonSettingsStore(SettingsPath);

        store.Save(new AppSettings { AutoConnectOnLidOpen = true });
        store.Save(new AppSettings { AutoConnectOnLidOpen = false });

        Assert.True(File.Exists(SettingsPath));
        Assert.False(File.Exists(SettingsPath + ".tmp"));
        Assert.False(store.Load().AutoConnectOnLidOpen);
    }

    [Fact]
    public void Unknown_members_do_not_break_loading()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, """{ "schemaVersion": 1, "futureSetting": "yes", "autoConnectOnLidOpen": true }""");

        var settings = new JsonSettingsStore(SettingsPath).Load();

        Assert.True(settings.AutoConnectOnLidOpen);
        Assert.True(settings.SwitchDefaultAudioEndpoint);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class RecordingLogger : IAppLogger
    {
        public List<string> Warnings { get; } = [];

        public void Info(string message)
        {
        }

        public void Warn(string message) => Warnings.Add(message);

        public void Error(string message, Exception? exception = null)
        {
        }
    }
}
