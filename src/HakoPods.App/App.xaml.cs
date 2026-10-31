using System.IO;
using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using HakoPods.App.Popup;
using HakoPods.App.State;
using HakoPods.App.Tray;
using HakoPods.Core.Bluetooth;
using HakoPods.Core.Logging;
using HakoPods.Core.Settings;

namespace HakoPods.App;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstance;
    private ulong? _address;
    private AppSettings _settings = new();
    private TrayIconHost? _tray;
    private PopupWindow? _popup;
    private LiveStateFeed? _feed;
    private Win32BluetoothNative? _native;
    private BluetoothConnectionManager? _connection;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // One tray icon per user: a second launch must not create a second icon or a second
        // watcher fighting over the radio.
        _singleInstance = new Mutex(initiallyOwned: true, @"Local\HakoPods.SingleInstance", out var isFirst);
        if (!isFirst)
        {
            Shutdown();
            return;
        }

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

        _settings = settings;
        _address = ParseAddress(settings.PreferredDeviceAddress);
        try
        {
            HakoPods.Core.Startup.AutostartService.SyncWith(settings.StartWithWindows, Environment.ProcessPath ?? string.Empty);
        }
        catch (Exception)
        {
            // A locked-down policy key must not stop the app from running.
        }

        _native = new Win32BluetoothNative();
        _connection = new BluetoothConnectionManager(_native, logger);

        // Simulation menu is on until the live pipeline is wired (APW-109/APW-110): without it
        // there is no way to see the five glyph states, and "it compiles" is not a demo.
        _tray = new TrayIconHost(_connection, settings.PreferredDeviceAddress, logger, includeSimulationMenu: true);
        _tray.QuitRequested += (_, _) => Shutdown();
        _tray.PopupRequested += (_, state) => ShowPopup(state);

        // The tray is useless without this: adverts → samples → state.
        //
        // Deliberately NOT filtered by the paired address. That address is a Classic one;
        // proximity-pairing adverts come from rotating random addresses (docs/research/IDENTITY.md),
        // so filtering on it would discard every packet. v1 accepts any Apple proximity-pairing
        // advert, which is correct while exactly one pair is in range.
        _feed = new LiveStateFeed(address: null, Dispatcher, settings.NearThresholdDbm);
        _feed.StateChanged += (_, state) =>
        {
            _tray.ApplyState(state);
            OnStateChanged(state);
        };
        _feed.Start();

        // Development aid: `HakoPods.exe --demo-popup` renders the card once so the layout can be
        // inspected (and screenshotted) without waiting for a lid event.
        if (e.Args.Contains("--demo-unknown", StringComparer.OrdinalIgnoreCase))
        {
            ShowPopup(new HakoPods.Core.State.DeviceState
            {
                ModelName = "AirPods Pro",
                IsPresent = true,
                LidOpen = true,
                LeftBatteryPercent = null,
                RightBatteryPercent = 5,
                CaseBatteryPercent = null,
                RightInEar = true,
                LastSeenAt = DateTimeOffset.Now,
            });
        }

        if (e.Args.Contains("--demo-popup", StringComparer.OrdinalIgnoreCase))
        {
            ShowPopup(new HakoPods.Core.State.DeviceState
            {
                ModelName = "AirPods Pro",
                IsPresent = true,
                LidOpen = true,
                LeftBatteryPercent = 80,
                RightBatteryPercent = 5,
                CaseBatteryPercent = 60,
                CaseCharging = true,
                RightInEar = true,
                LastSeenAt = DateTimeOffset.Now,
            });
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_feed is not null)
        {
            _feed.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        _singleInstance?.ReleaseMutex();
        _singleInstance?.Dispose();

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

    private DateTimeOffset _lastPopupAt = DateTimeOffset.MinValue;
    private bool _lidWasOpen;

    /// <summary>
    /// Fires the card on the closed → open edge only: opening the lid is the moment the user
    /// wants to know the levels, and firing on every advertisement would be a slideshow.
    /// Suppressed for ten seconds after a dismissal, and when a fullscreen app owns the screen.
    /// </summary>
    private void OnStateChanged(HakoPods.Core.State.DeviceState state)
    {
        var openedNow = state.LidOpen && !_lidWasOpen;
        _lidWasOpen = state.LidOpen;

        if (!openedNow || !state.IsPresent)
        {
            return;
        }

        if (DateTimeOffset.UtcNow - _lastPopupAt < TimeSpan.FromSeconds(10))
        {
            return;
        }

        if (!IsOurPairedDevice())
        {
            return;
        }

        if (IsFullscreenAppActive())
        {
            return;
        }

        _lastPopupAt = DateTimeOffset.UtcNow;
        ShowPopup(state);

        AutoConnectIfNeeded();
    }

    /// <summary>
    /// Lid open is when Windows is most likely to connect by itself (0–1 s, APW-013), so this is a
    /// nudge rather than the main path: it only acts when the link is genuinely down, and it never
    /// blocks the UI. Failures are logged and swallowed — a refused connect must not surface as a
    /// dialog over someone's work.
    /// </summary>
    private void AutoConnectIfNeeded()
    {
        if (!_settings.AutoConnectOnLidOpen || _address is not { } address || IsConnected() || _connection is null)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var result = await _connection.ConnectAsync(address).ConfigureAwait(false);
                if (!result.Success)
                {
                    LastConnectError = result.Error;
                }
            }
            catch (Exception ex)
            {
                LastConnectError = ex.Message;
            }
        });
    }

    /// <summary>Kept for the diagnostics surface; null when the last auto-connect worked.</summary>
    public static string? LastConnectError { get; private set; }

    /// <summary>
    /// The card is for the pair this PC knows, not for whatever AirPods happen to be on the desk.
    /// Remembrance is the only identity the radio exposes for Classic devices.
    /// </summary>
    private bool IsOurPairedDevice() =>
        _address is { } address
        && _native is not null
        && _native.TryGetDevice(address, out var snapshot)
        && snapshot.Remembered;

    /// <summary>Shell tells us when a game or presentation owns the screen; do not interrupt it.</summary>
    private static bool IsFullscreenAppActive()
    {
        try
        {
            if (SHQueryUserNotificationState(out var state) != 0)
            {
                return false;
            }

            return state is 2 or 3 or 4; // busy, D3D fullscreen, presentation mode
        }
        catch (DllNotFoundException)
        {
            return false;
        }
    }

    [System.Runtime.InteropServices.DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int state);

    private void ShowPopup(HakoPods.Core.State.DeviceState state)
    {
        _popup ??= new PopupWindow();
        _popup.Apply(state, IsDarkTheme(), IsConnected());
        _popup.AccentBrush = AccentBrush();

        var cursor = System.Windows.Forms.Cursor.Position;
        var screen = System.Windows.Forms.Screen.FromPoint(cursor);
        var scale = VisualTreeHelper.GetDpi(_popup);

        // Screen bounds are physical pixels; the window positions in DIPs. Divide by the monitor
        // scale so the card lands on the right monitor before it is shown.
        var work = new Rect(
            screen.WorkingArea.X / scale.DpiScaleX,
            screen.WorkingArea.Y / scale.DpiScaleY,
            screen.WorkingArea.Width / scale.DpiScaleX,
            screen.WorkingArea.Height / scale.DpiScaleY);

        _popup.ShowAt(work, TaskbarEdge.Bottom);
    }

    /// <summary>
    /// True when the radio reports an active link to the configured pair. Device-level, not
    /// per-profile, but with both profiles disabled on disconnect (APW-106) it is an honest
    /// "Connected" signal for the status line.
    /// </summary>
    private bool IsConnected() =>
        _address is { } address && _native is not null
        && _native.TryGetDevice(address, out var snapshot) && snapshot.Connected;

    private static bool IsDarkTheme()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("SystemUsesLightTheme") is int value && value == 0;
    }

    private static Brush AccentBrush()
    {
        // DWM stores the accent as 0xAARRGGBB; fall back to the blue used in the design renders.
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
        if (key?.GetValue("ColorizationColor") is int raw)
        {
            var value = unchecked((uint)raw);
            var colour = Color.FromRgb((byte)(value >> 16), (byte)(value >> 8), (byte)value);
            return new SolidColorBrush(colour);
        }

        return new SolidColorBrush(Color.FromRgb(0x00, 0x71, 0xE3));
    }
}
