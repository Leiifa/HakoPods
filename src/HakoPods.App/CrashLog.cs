using System.IO;
using System.Text;

namespace HakoPods.App;

/// <summary>
/// Last-resort crash logging. A tray app has no console and no window, so an unhandled exception
/// would otherwise vanish: the icon disappears and the user has nothing to report.
///
/// Writes to the app's own data folder with the version and the stack. Deliberately no device
/// names, addresses or file paths beyond the app's own — a crash report is not a diagnostic dump.
/// </summary>
public static class CrashLog
{
    private static readonly object Gate = new();

    public static string Path { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HakoPods",
        "crash.log");

    public static void Install(System.Windows.Application app)
    {
        app.DispatcherUnhandledException += (_, e) =>
        {
            Write("Dispatcher", e.Exception);
            // Keep the app alive: a tray app that dies silently looks like it was never running.
            e.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Write("AppDomain", e.ExceptionObject as Exception);

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Write("Task", e.Exception);
            e.SetObserved();
        };
    }

    public static void Write(string source, Exception? exception)
    {
        try
        {
            var text = new StringBuilder()
                .AppendLine($"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}] {source}")
                .AppendLine($"version: {HakoPods.Core.AppInfo.Version}")
                .AppendLine(exception?.ToString() ?? "(no exception object)")
                .AppendLine(new string('-', 60))
                .ToString();

            lock (Gate)
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                File.AppendAllText(Path, text);
            }
        }
        catch (Exception)
        {
            // Nothing sensible left to do if even logging fails.
        }
    }
}
