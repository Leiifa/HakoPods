using System.IO;
using System.Windows;
using HakoPods.App.State;
using HakoPods.App.Tray;
using HakoPods.Core.Bluetooth;
using HakoPods.Core.Logging;
using HakoPods.Core.Settings;

namespace HakoPods.App;

public partial class App : System.Windows.Application
{
    private TrayIconHost? _tray;
    private LiveStateFeed? _feed;
    private Win32BluetoothNative? _native;
    private BluetoothConnectionManager? _connection;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var logger = NullAppLogger.Instance;
        var settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HakoPods",
            "settings.json");

        AppSettings settings;
        try
        {
            settings = new JsonSettingsStore(settingsPath, logger).Load();
        }
        catch (Exception)
        {
            settings = new AppSettings();
        }

        _native = new Win32BluetoothNative();
        _connection = new BluetoothConnectionManager(_native, logger);

        // Simulation menu is on until the live pipeline is wired (APW-109/APW-110): without it
        // there is no way to see the five glyph states, and "it compiles" is not a demo.
        _tray = new TrayIconHost(_connection, settings.PreferredDeviceAddress, logger, includeSimulationMenu: true);
        _tray.QuitRequested += (_, _) => Shutdown();
        _tray.PopupRequested += (_, state) => ShowPopupPlaceholder(state);

        // The tray is useless without this: adverts → samples → state.
        //
        // Deliberately NOT filtered by the paired address. That address is a Classic one;
        // proximity-pairing adverts come from rotating random addresses (docs/research/IDENTITY.md),
        // so filtering on it would discard every packet. v1 accepts any Apple proximity-pairing
        // advert, which is correct while exactly one pair is in range.
        _feed = new LiveStateFeed(address: null, Dispatcher);
        _feed.StateChanged += (_, state) => _tray.ApplyState(state);
        _feed.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_feed is not null)
        {
            _feed.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        _tray?.Dispose();
        _connection?.Dispose();
        _native?.Dispose();
        base.OnExit(e);
    }

    private static ulong? ParseAddress(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return null;
        }

        var cleaned = hex.Replace(":", string.Empty).Replace("-", string.Empty).Trim();
        return cleaned.Length > 0
            && ulong.TryParse(cleaned, System.Globalization.NumberStyles.HexNumber, null, out var address)
            ? address
            : null;
    }

    private static void ShowPopupPlaceholder(HakoPods.Core.State.DeviceState state)
    {
        // APW-108 replaces this with the signed-off popup window.
        System.Windows.MessageBox.Show(
            HakoPods.Core.Presentation.TrayGlyphMapper.Describe(state),
            "AirPods for Windows",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }
}
