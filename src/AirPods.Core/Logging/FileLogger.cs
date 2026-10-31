using System.Text;

namespace AirPods.Core.Logging;

/// <summary>
/// Append-only text log with size-based rolling: <c>app.log</c> plus <c>app.log.1 … app.log.N</c>.
/// No network, no telemetry, no PII beyond what the caller writes — diagnostics are exported
/// only when the user asks (ADR-010).
/// </summary>
public sealed class FileLogger : IAppLogger
{
    private readonly string _directory;
    private readonly string _fileName;
    private readonly long _maxBytes;
    private readonly int _maxBackups;
    private readonly object _gate = new();

    public FileLogger(string directory, long maxBytes = 1_048_576, int maxBackups = 5)
    {
        if (maxBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBytes));
        }

        if (maxBackups < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBackups));
        }

        _directory = directory;
        _fileName = "app.log";
        _maxBytes = maxBytes;
        _maxBackups = maxBackups;
    }

    public string CurrentFile => Path.Combine(_directory, _fileName);

    public void Info(string message) => Write("INFO ", message);

    public void Warn(string message) => Write("WARN ", message);

    public void Error(string message, Exception? exception = null) =>
        Write("ERROR", exception is null ? message : $"{message} :: {exception.GetType().Name}: {exception.Message}");

    private void Write(string level, string message)
    {
        var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} {level} {message}{Environment.NewLine}";

        lock (_gate)
        {
            Directory.CreateDirectory(_directory);
            RollIfNeeded(Encoding.UTF8.GetByteCount(line));
            File.AppendAllText(CurrentFile, line, Encoding.UTF8);
        }
    }

    private void RollIfNeeded(int incomingBytes)
    {
        var current = new FileInfo(CurrentFile);
        if (!current.Exists || current.Length + incomingBytes <= _maxBytes)
        {
            return;
        }

        var oldest = $"{CurrentFile}.{_maxBackups}";
        if (File.Exists(oldest))
        {
            File.Delete(oldest);
        }

        for (var i = _maxBackups - 1; i >= 1; i--)
        {
            var from = $"{CurrentFile}.{i}";
            if (File.Exists(from))
            {
                File.Move(from, $"{CurrentFile}.{i + 1}", overwrite: true);
            }
        }

        File.Move(CurrentFile, $"{CurrentFile}.1", overwrite: true);
    }
}
