using System.Text.Json;
using AirPods.Core.Logging;
using AirPods.Core.Settings;

namespace AirPods.Core.Settings;

/// <summary>
/// JSON settings on disk, written atomically (temp file + replace) so a crash mid-write cannot
/// leave a truncated file. A corrupt or unreadable file yields defaults and a log line rather
/// than an exception — settings are not worth crashing over.
/// </summary>
public sealed class JsonSettingsStore
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string _path;
    private readonly IAppLogger _logger;

    public JsonSettingsStore(string path, IAppLogger? logger = null)
    {
        _path = path;
        _logger = logger ?? NullAppLogger.Instance;
    }

    public string Path => _path;

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _logger.Warn($"settings unreadable at '{_path}': {ex.GetType().Name}: {ex.Message}. Using defaults.");
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        var directory = System.IO.Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, Options));
        File.Move(temp, _path, overwrite: true);
    }
}
