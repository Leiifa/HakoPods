using Microsoft.Win32;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace HakoPods.App.Notifications;

/// <summary>
/// A plain toast: a title and up to three short lines, no buttons, no activation.
///
/// An unpackaged exe can show toasts only if its AppUserModelID is registered, which is one
/// registry key with a display name. Without that, CreateToastNotifier silently produces nothing —
/// the classic "toasts work in the sample but not in my app" trap.
/// </summary>
public static class ToastNotifier
{
    private const string AppId = "HakoPods.Tray";
    private static bool _registered;

    public static void EnsureRegistered(string displayName)
    {
        if (_registered)
        {
            return;
        }

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\AppUserModelId\{AppId}");
            key?.SetValue("DisplayName", displayName);
            key?.SetValue("IconUri", Environment.ProcessPath ?? string.Empty);
            _registered = true;
        }
        catch (Exception)
        {
            // Without the key the toast below simply does nothing; the card still works.
        }
    }

    /// <param name="lines">Short facts, one per line. Windows shows three comfortably.</param>
    public static bool Show(string title, params string[] lines)
    {
        try
        {
            var xml = "<toast><visual><binding template=\"ToastGeneric\">"
                + $"<text>{Escape(title)}</text>"
                + string.Concat(lines.Take(3).Select(line => $"<text>{Escape(line)}</text>"))
                + "</binding></visual></toast>";

            var document = new XmlDocument();
            document.LoadXml(xml);

            ToastNotificationManager.CreateToastNotifier(AppId)
                .Show(new ToastNotification(document));

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string Escape(string value) =>
        value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
