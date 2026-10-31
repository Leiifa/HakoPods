using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using HakoPods.Core.Bluetooth;
using HakoPods.Core.Logging;
using HakoPods.Core.Presentation;
using HakoPods.Core.State;

namespace HakoPods.App.Tray;

/// <summary>
/// The notification-area presence: one icon, a tooltip with both buds and the case, and a menu.
///
/// Icon files are the signed-off PNGs from `docs/design/icons/`. Windows asks a tray icon for a
/// 16 px image; it is given the 32 px master and scales it, which keeps the glyph sharp at
/// 150 % and 200 % because the process is per-monitor DPI aware (see app.manifest).
/// </summary>
public sealed class TrayIconHost : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _connectItem;
    private readonly ToolStripMenuItem _disconnectItem;
    private readonly ToolStripMenuItem? _simulateItem;
    private readonly Dictionary<(TrayGlyph Glyph, bool Dark), Icon> _icons = new();
    private readonly int _iconSize;
    private readonly BluetoothConnectionManager _connection;
    private readonly IAppLogger _logger;
    private readonly ulong? _address;
    private DeviceState _state = DeviceState.Empty;
    private bool _dark;
    private bool _disposed;

    public TrayIconHost(
        BluetoothConnectionManager connection,
        string? deviceAddressHex,
        IAppLogger logger,
        bool includeSimulationMenu = false)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _address = TryParseAddress(deviceAddressHex);
        _dark = IsDarkTaskbar();
        _iconSize = TrayIconSizeForDpi();

        _statusItem = new ToolStripMenuItem("No AirPods nearby") { Enabled = false };
        _connectItem = new ToolStripMenuItem("Connect");
        _disconnectItem = new ToolStripMenuItem("Disconnect");
        var settingsItem = new ToolStripMenuItem("Settings…");
        var quitItem = new ToolStripMenuItem("Quit");

        _connectItem.Click += async (_, _) => await RunConnectionAsync(connect: true);
        _disconnectItem.Click += async (_, _) => await RunConnectionAsync(connect: false);
        settingsItem.Click += (_, _) => ShowPlaceholder("Settings arrive with APW-301.");
        quitItem.Click += (_, _) => QuitRequested?.Invoke(this, EventArgs.Empty);

        var items = new List<ToolStripItem> { _statusItem, new ToolStripSeparator(), _connectItem, _disconnectItem };

        if (includeSimulationMenu)
        {
            _simulateItem = BuildSimulationMenu();
            items.Add(_simulateItem);
        }

        items.Add(new ToolStripSeparator());
        items.Add(settingsItem);
        items.Add(quitItem);

        _menu = new ContextMenuStrip { ShowImageMargin = false, RenderMode = ToolStripRenderMode.System };
        _menu.Items.AddRange(items.ToArray());

        _icon = new NotifyIcon
        {
            ContextMenuStrip = _menu,
            Text = "AirPods for Windows",
            Visible = true,
            Icon = LoadIcon(TrayGlyph.Disconnected),
        };

        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                // The popup window is APW-108; until it exists a click must not do nothing
                // silently, so it reports the state it would have shown.
                PopupRequested?.Invoke(this, _state);
            }
        };

        ApplyState(DeviceState.Empty);
    }

    public event EventHandler? QuitRequested;

    public event EventHandler<DeviceState>? PopupRequested;

    /// <summary>Pushes a new snapshot into the icon, tooltip and menu. Cheap; call on every update.</summary>
    public void ApplyState(DeviceState state)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _state = state ?? DeviceState.Empty;

        var glyph = TrayGlyphMapper.From(_state);
        _icon.Icon = LoadIcon(glyph);

        // Tooltip is capped at 127 characters by the shell; Describe() stays well under it.
        var text = TrayGlyphMapper.Describe(_state);
        _icon.Text = text.Length > 127 ? text[..127] : text;

        _statusItem.Text = text;
        UpdateMenuAvailability();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();

        foreach (var icon in _icons.Values)
        {
            icon.Dispose();
        }

        _icons.Clear();
    }

    private async Task RunConnectionAsync(bool connect)
    {
        if (_address is not { } address)
        {
            ShowPlaceholder("Pick your AirPods first — device selection arrives with APW-105's settings.");
            return;
        }

        _connectItem.Enabled = false;
        _disconnectItem.Enabled = false;

        try
        {
            var result = connect
                ? await _connection.ConnectAsync(address)
                : await _connection.DisconnectAsync(address);

            _logger.Info($"Tray {(connect ? "connect" : "disconnect")}: success={result.Success} in {result.Elapsed.TotalMilliseconds:0} ms.");

            if (!result.Success)
            {
                ShowPlaceholder(result.Error ?? "The radio refused the request.");
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Tray connection action failed.", ex);
            ShowPlaceholder("That did not work: " + ex.Message);
        }
        finally
        {
            UpdateMenuAvailability();
        }
    }

    private void UpdateMenuAvailability()
    {
        var reachable = _state.IsPresent;
        _connectItem.Enabled = _address is not null && reachable;
        _disconnectItem.Enabled = _address is not null && reachable;

        // A sleeping pair cannot be woken by an API call (measurement 14), so offering Connect
        // when nothing is nearby would be a button that does nothing.
        _connectItem.ToolTipText = reachable ? string.Empty : "Open the case or put the AirPods in to wake them.";
    }

    private ToolStripMenuItem BuildSimulationMenu()
    {
        var menu = new ToolStripMenuItem("Simulate state (debug)");

        void Add(string label, DeviceState state) =>
            menu.DropDownItems.Add(new ToolStripMenuItem(label, null, (_, _) => ApplyState(state)));

        Add("Nearby · full", Sample(78, 74, 62));
        Add("Nearby · low right", Sample(76, 8, 60));
        Add("Nearby · charging", Sample(41, 39, 12) with { LeftCharging = true, CaseCharging = true });
        Add("Nearby · unknown levels", new DeviceState { ModelName = "AirPods Pro", IsPresent = true, LastSeenAt = DateTimeOffset.Now });
        Add("Not nearby", new DeviceState { ModelName = "AirPods Pro", IsPresent = false, LastSeenAt = DateTimeOffset.Now });

        return menu;
    }

    private static DeviceState Sample(int left, int right, int casePercent) => new()
    {
        ModelName = "AirPods Pro",
        IsPresent = true,
        LastSeenAt = DateTimeOffset.Now,
        LeftBatteryPercent = left,
        RightBatteryPercent = right,
        CaseBatteryPercent = casePercent,
        LidOpen = true,
    };

    private static void ShowPlaceholder(string message) =>
        MessageBox.Show(message, "AirPods for Windows", MessageBoxButtons.OK, MessageBoxIcon.Information);

    private Icon LoadIcon(TrayGlyph glyph)
    {
        if (_icons.TryGetValue((glyph, _dark), out var cached))
        {
            return cached;
        }

        var theme = _dark ? "dark" : "light";
        var name = glyph.ToString().ToLowerInvariant();

        // Two masters per state: a simplified 16 px drawing and the detailed 32 px one. Windows
        // scales a 16 px icon to 24 px far better than it shrinks a 32 px icon to 16, where the
        // bud stems collapse into a wedge.
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Tray", $"{name}-{theme}-{_iconSize}.png");

        if (!File.Exists(path))
        {
            _logger.Warn($"Tray glyph missing: {path}");
            return SystemIcons.Application;
        }

        using var bitmap = new Bitmap(path);
        var handle = bitmap.GetHicon();

        try
        {
            using var temporary = Icon.FromHandle(handle);
            var icon = (Icon)temporary.Clone();
            _icons[(glyph, _dark)] = icon;
            return icon;
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static bool IsDarkTaskbar()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("SystemUsesLightTheme") is int value && value == 0;
    }

    private static int TrayIconSizeForDpi()
    {
        using var graphics = Graphics.FromHwnd(IntPtr.Zero);
        return graphics.DpiX >= 144 ? 32 : 16;
    }

    private static ulong? TryParseAddress(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return null;
        }

        var cleaned = hex.Replace(":", string.Empty).Replace("-", string.Empty).Trim();
        return cleaned.Length > 0 && ulong.TryParse(cleaned, System.Globalization.NumberStyles.HexNumber, null, out var address)
            ? address
            : null;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);
}
