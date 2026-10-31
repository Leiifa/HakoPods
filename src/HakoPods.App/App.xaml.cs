using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
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
    private JsonSettingsStore? _store;
    private DispatcherTimer? _connectionWatch;
    private bool _wasConnected;
    private bool _routedSinceConnect;
    private bool? _anyInEar;
    private bool _pausedByEar;
    private DateTimeOffset? _earCandidateAt;
    private bool _revertedUnworn;
    private readonly HakoPods.Core.Audio.CaptureSessionMonitor _capture = new();
    private bool _hfpUp;
    private DateTimeOffset _captureIdleSince = DateTimeOffset.MinValue;
    private DateTimeOffset _lastRelaunch = DateTimeOffset.MinValue;
    private CancellationTokenSource? _radioWatch;
    private bool _radioToggled;
    private string? _previousEndpoint;
    private TrayIconHost? _tray;
    private PopupWindow? _popup;
    private LiveStateFeed? _feed;
    private Win32BluetoothNative? _native;
    private BluetoothConnectionManager? _connection;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        CrashLog.Install(this);

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
            _store = new JsonSettingsStore(settingsPath, logger);
            settings = _store.Load();
        }
        catch (Exception)
        {
            settings = new AppSettings();
        }

        _settings = settings;
        _previousEndpoint = settings.PreviousAudioEndpointId;
        _address = ParseAddress(settings.PreferredDeviceAddress);
        try
        {
            HakoPods.Core.Startup.AutostartService.SyncWith(settings.StartWithWindows, Environment.ProcessPath ?? string.Empty);
        }
        catch (Exception)
        {
            // A locked-down policy key must not stop the app from running.
        }

        // Toasts need both: a Start-menu shortcut carrying the AppUserModelID, and a matching
        // registry entry. Either alone shows nothing.
        HakoPods.App.Notifications.ShortcutFactory.EnsureShortcut(
            "HakoPods.Tray", Environment.ProcessPath ?? string.Empty, "HakoPods");
        HakoPods.App.Notifications.ToastNotifier.EnsureRegistered("HakoPods");

        _native = new Win32BluetoothNative();
        _connection = new BluetoothConnectionManager(_native, logger);

        // Simulation menu is on until the live pipeline is wired (APW-109/APW-110): without it
        // there is no way to see the five glyph states, and "it compiles" is not a demo.
        _tray = new TrayIconHost(_connection, settings.PreferredDeviceAddress, logger, includeSimulationMenu: true);
        _tray.QuitRequested += (_, _) => Shutdown();
        _tray.SettingsRequested += (_, _) => OpenSettings();
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
            PauseOnEarChange(state);
            RouteOnWear(state);
        };
        _feed.Start();

        // Advertisements stop when the pair sleeps, so a disconnect is invisible to the feed.
        // Polling the link is the only way to notice it and restore the speakers.
        _connectionWatch = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, (_, _) => PollConnection(), Dispatcher);
        _connectionWatch.Start();
        StartRadioWatch();

        // Sleep/resume and radio toggles both leave the watcher deaf without an error. Hooking the
        // power event is the only reliable place to recover.
        Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerModeChanged;

        // Development aid: `HakoPods.exe --demo-popup` renders the card once so the layout can be
        // inspected (and screenshotted) without waiting for a lid event.
#if DEBUG
        if (e.Args.Contains("--test-crash", StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Forced crash for the crash-log test.");
        }
#endif

        if (e.Args.Contains("--mic-probe", StringComparer.OrdinalIgnoreCase))
        {
            var monitor = new HakoPods.Core.Audio.CaptureSessionMonitor();
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HakoPods", "mic-probe.log");

            for (var i = 1; i <= 90; i++)
            {
                var line = $"{i,2}s active={monitor.IsAnyCaptureActive()} :: {string.Join(" | ", monitor.Describe())}";
                File.AppendAllText(path, line + Environment.NewLine);
                System.Threading.Thread.Sleep(1000);
            }

            Shutdown();
            return;
        }

        if (e.Args.Contains("--test-toast", StringComparer.OrdinalIgnoreCase))
        {
            HakoPods.App.Notifications.ToastNotifier.Show("AirPods Pro",
                "Left 80 %", "Right 90 %", "Case 60 %");
        }

        if (e.Args.Contains("--settings", StringComparer.OrdinalIgnoreCase))
        {
            OpenSettings();
        }

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

        _radioWatch?.Cancel();
        _radioWatch?.Dispose();

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

        // Toast is parked: Windows only shows toasts for unpackaged exes that own a Start-menu
        // shortcut carrying the AppUserModelID, so Show() succeeds and nothing appears. Until that
        // shortcut exists (APW-305 packaging), the card is the surface that actually works.
        ShowPopup(state);

        AutoConnectIfNeeded();
        RouteAudioIfConfigured();
    }

    /// <summary>
    /// Switches playback to the endpoint the user chose, once per lid open. Gated on the setting,
    /// and silent when no endpoint has been learned yet — better no routing than routing to a
    /// stale id that Windows has since re-numbered.
    /// </summary>
    /// <summary>
    /// Starts a new copy of this exe and exits. The single-instance mutex means the new process
    /// takes over the tray cleanly once this one lets go.
    /// </summary>
    private void Relaunch()
    {
        try
        {
            if (Environment.ProcessPath is { } exe)
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe)
                {
                    UseShellExecute = true,
                });
            }
        }
        catch (Exception)
        {
            // If the relaunch cannot start, staying alive is better than vanishing.
            return;
        }

        Shutdown();
    }

    /// <summary>
    /// Watches the Bluetooth radio itself. Two things matter: a radio that comes back should get a
    /// fresh watcher immediately, and only a radio that actually toggled justifies the relaunch
    /// backstop — silence from an absent pair is not a fault.
    /// </summary>
    private void StartRadioWatch()
    {
        _radioWatch = new CancellationTokenSource();
        var token = _radioWatch.Token;

        _ = Task.Run(async () =>
        {
            bool? last = null;

            while (!token.IsCancellationRequested)
            {
                bool on;

                try
                {
                    var radios = await global::Windows.Devices.Radios.Radio.GetRadiosAsync().AsTask(token).ConfigureAwait(false);
                    var bluetooth = radios.FirstOrDefault(r => r.Kind == global::Windows.Devices.Radios.RadioKind.Bluetooth);
                    on = bluetooth is not null && bluetooth.State == global::Windows.Devices.Radios.RadioState.On;

                }
                catch (Exception)
                {
                    on = last ?? true;
                }

                if (last == false && on)
                {
                    _ = Dispatcher.BeginInvoke(() => _feed?.Restart());
                }

                if (last == true && !on)
                {
                    _radioToggled = true;
                }

                last = on;

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        });
    }

    private void OnPowerModeChanged(object sender, Microsoft.Win32.PowerModeChangedEventArgs e)
    {
        if (e.Mode != Microsoft.Win32.PowerModes.Resume)
        {
            return;
        }

        try
        {
            _feed?.Restart();
        }
        catch (Exception)
        {
            // Nothing useful to do if the radio refuses to come back; the next launch will retry.
        }

        _wasConnected = false;
        _routedSinceConnect = false;
        _anyInEar = null;
    }

    /// <summary>
    /// Mic on demand: while the stereo policy is on, the hands-free profile is raised only while
    /// an app is actually capturing, and dropped again once it has been quiet for five seconds.
    /// Music therefore stays stereo all day and only a call pays the narrowband cost.
    /// </summary>
    private void ApplyMicOnDemand()
    {
        if (!_settings.DisableHandsFreeProfile || _address is not { } address || _connection is null || !IsConnected())
        {
            return;
        }

        var capturing = _capture.IsAnyCaptureActive();

        if (capturing)
        {
            _captureIdleSince = DateTimeOffset.MinValue;

            if (!_hfpUp)
            {
                _hfpUp = true;
                _ = Task.Run(() => _connection.EnableHandsFreeAsync(address));
            }

            return;
        }

        if (!_hfpUp)
        {
            return;
        }

        if (_captureIdleSince == DateTimeOffset.MinValue)
        {
            _captureIdleSince = DateTimeOffset.UtcNow;
            return;
        }

        if (DateTimeOffset.UtcNow - _captureIdleSince < TimeSpan.FromSeconds(5))
        {
            return;
        }

        _hfpUp = false;
        _captureIdleSince = DateTimeOffset.MinValue;
        _ = Task.Run(() => _connection.DisableHandsFreeAsync(address));
    }

    private void PollConnection()
    {
        _feed?.EnsureAlive();
        ApplyMicOnDemand();

        // In-process revival is unproven after a radio toggle; a fresh process is not. Two failed
        // revivals mean give up on the watcher and hand over, at most once every ten minutes so a
        // genuinely broken environment cannot become a relaunch loop.
        if (_radioToggled && _feed is not null && _feed.RestartsSinceLastSample >= 2
            && DateTimeOffset.UtcNow - _lastRelaunch > TimeSpan.FromMinutes(10))
        {
            _lastRelaunch = DateTimeOffset.UtcNow;
            Relaunch();
            return;
        }

        var connected = IsConnected();

        // Windows switches the default output to a newly linked pair on its own. If no bud is
        // worn, that move has to be undone — otherwise music plays into a closed case. This runs
        // on the steady-state tick, not just the transition, because the OS switch can land a
        // second or two after the link comes up.
        if (connected && !_revertedUnworn && _feed?.Current.AnyInEar != true && DefaultIsAirPods())
        {
            _revertedUnworn = true;
            RestorePreviousOutput();
        }

        if (connected == _wasConnected)
        {
            return;
        }

        _wasConnected = connected;

        if (connected)
        {
            // Only remember where audio was; routing waits until a bud is actually worn. Windows
            // links the pair as soon as the lid opens, buds in the case included, and switching
            // output then steals audio for nobody.
            RememberCurrentOutput();

            // Stereo is the default posture, but only when nothing is recording: disabling HFP
            // unconditionally raced the mic-on-demand policy and killed the mic a tick after it
            // was raised.
            if (_settings.DisableHandsFreeProfile && !_capture.IsAnyCaptureActive())
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        if (_address is { } address && _connection is not null)
                        {
                            await _connection.DisableHandsFreeAsync(address).ConfigureAwait(false);
                        }
                    }
                    catch (Exception)
                    {
                        // The policy is a preference; failing it must not disturb playback.
                    }
                });
            }
        }
        else
        {
            _routedSinceConnect = false;
            _earCandidateAt = null;
            _revertedUnworn = false;
            _pausedByEar = false;
            RestorePreviousOutput();
        }
    }

    /// <summary>
    /// Captures what was playing before the buds arrived, so it can be put back. Skipped when the
    /// current default is already the AirPods — otherwise a reconnect would remember the buds as
    /// "previous" and restore to them.
    /// </summary>
    private void RememberCurrentOutput()
    {
        var router = new HakoPods.Core.Audio.AudioEndpointRouter();
        var current = router.GetDefaultEndpointId();
        var airPods = FindAirPodsEndpoint(router);

        if (current is null || current == airPods || current == _previousEndpoint)
        {
            return;
        }

        _previousEndpoint = current;

        // Persist it: the app may be restarted between connect and disconnect, and restoring to
        // nothing is worse than restoring to where the user was.
        _settings = _settings with { PreviousAudioEndpointId = current };
        try
        {
            _store?.Save(_settings);
        }
        catch (Exception)
        {
            // Losing the memory is acceptable; crashing the tray is not.
        }
    }

    private static bool DefaultIsAirPods()
    {
        var router = new HakoPods.Core.Audio.AudioEndpointRouter();
        var airPods = FindAirPodsEndpoint(router);
        return airPods is not null && router.GetDefaultEndpointId() == airPods;
    }

    private void RestorePreviousOutput()
    {
        if (_previousEndpoint is not { } previous || !_settings.SwitchDefaultAudioEndpoint)
        {
            return;
        }

        var router = new HakoPods.Core.Audio.AudioEndpointRouter();
        if (router.GetDefaultEndpointId() is { } current && FindAirPodsEndpoint(router) == current)
        {
            router.TrySetDefault(previous);
        }
    }

    /// <summary>
    /// Pauses when the last bud comes out and resumes when one goes back in — the AirPods
    /// behaviour everyone notices. Transitions only: a burst of packets carrying the same ear
    /// state must not toggle playback repeatedly.
    /// </summary>
    private void PauseOnEarChange(HakoPods.Core.State.DeviceState state)
    {
        if (!_settings.PauseOnEarChange || !IsFresh(state))
        {
            return;
        }

        var inEar = state.AnyInEar;
        if (_anyInEar is null)
        {
            _anyInEar = inEar;
            return;
        }

        if (inEar == _anyInEar.Value)
        {
            return;
        }

        _anyInEar = inEar;

        if (!inEar)
        {
            // Removal: pause, and remember that *we* paused it.
            HakoPods.Core.Audio.MediaKeySender.TogglePlayPause();
            _pausedByEar = true;
            return;
        }

        // Insertion: only resume something we paused. Toggling blindly is what paused music the
        // user was already listening to when a bud went in.
        if (_pausedByEar)
        {
            HakoPods.Core.Audio.MediaKeySender.TogglePlayPause();
            _pausedByEar = false;
        }
    }

    /// <summary>Ear state older than this is not worth acting on.</summary>
    private static bool IsFresh(HakoPods.Core.State.DeviceState state) =>
        state.IsPresent && state.LastSeenAt is { } seen
        && DateTimeOffset.UtcNow - seen < TimeSpan.FromSeconds(10);

    /// <summary>
    /// Switches playback the first time a bud is worn after connecting. `AnyInEar` comes from the
    /// advertisement, so a pair sitting in an open case never triggers it.
    /// </summary>
    private void RouteOnWear(HakoPods.Core.State.DeviceState state)
    {
        if (_routedSinceConnect || !state.AnyInEar || !IsConnected())
        {
            _earCandidateAt = null;
            return;
        }

        // Two consecutive samples must agree before acting. After a radio toggle Windows replays
        // a cached advertisement carrying the ear state from before, and one packet is enough to
        // steal audio while the buds are still in their case.
        if (_earCandidateAt != state.LastSeenAt)
        {
            _earCandidateAt = state.LastSeenAt;
            return;
        }

        _routedSinceConnect = true;
        RouteAudioIfConfigured();
    }

    private void RouteAudioIfConfigured()
    {
        if (!_settings.SwitchDefaultAudioEndpoint)
        {
            return;
        }

        var router = new HakoPods.Core.Audio.AudioEndpointRouter();
        var target = FindAirPodsEndpoint(router);

        // Never force an arbitrary saved id: an earlier version did, and pushed playback back to
        // the speakers a second after Windows had correctly switched to the buds.
        if (target is null)
        {
            return;
        }

        if (!router.TrySetDefault(target))
        {
            LastConnectError = "could not switch playback to the AirPods endpoint";
            return;
        }

        LastConnectError = null;

        if (_settings.PreferredAudioEndpointId != target && _store is not null)
        {
            _settings = _settings with { PreferredAudioEndpointId = target };
            try
            {
                _store.Save(_settings);
            }
            catch (Exception)
            {
                // Remembering the id is a convenience; failing to persist it must not matter.
            }
        }
    }

    /// <summary>
    /// Endpoint ids are opaque and change per pairing, so the only handle a user recognises is
    /// the friendly name. Matching "AirPods" is a heuristic, but it is the honest one available.
    /// </summary>
    private static string? FindAirPodsEndpoint(HakoPods.Core.Audio.AudioEndpointRouter router) =>
        router.ListRenderEndpointsDetailed()
            .Where(e => e.Name is not null && e.Name.Contains("AirPods", StringComparison.OrdinalIgnoreCase))
            .Select(e => e.Id)
            .FirstOrDefault();

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

    private void OpenSettings()
    {
        if (_store is null)
        {
            return;
        }

        var window = new SettingsWindow(_store, _settings);
        if (window.ShowDialog() == true && window.SavedSettings is { } saved)
        {
            _settings = saved;
        }
    }

    /// <summary>
    /// The lid-open surface: a plain toast with the three facts worth interrupting for. The card
    /// stays behind the tray icon, where someone is deliberately looking for detail.
    /// </summary>
    private static bool ShowToast(HakoPods.Core.State.DeviceState state)
    {
        var lines = new List<string>(3);

        if (state.LeftBatteryPercent is { } left)
        {
            lines.Add($"Left {left} %");
        }

        if (state.RightBatteryPercent is { } right)
        {
            lines.Add($"Right {right} %");
        }

        if (state.CaseBatteryPercent is { } casePercent)
        {
            lines.Add($"Case {casePercent} %");
        }

        if (lines.Count == 0)
        {
            lines.Add("Battery not reported yet");
        }

        var title = string.IsNullOrWhiteSpace(state.ModelName) ? "AirPods" : state.ModelName;
        return HakoPods.App.Notifications.ToastNotifier.Show(title, lines.ToArray());
    }

    private void ShowPopup(HakoPods.Core.State.DeviceState state)
    {
        _popup ??= new PopupWindow();

        // Prefer the freshest snapshot; fall back to the one the click carried if the feed is
        // older than this, which the window renders as em dashes and "Last reading".
        state = _feed?.Fresh(TimeSpan.FromSeconds(8)) ?? state;
        _popup.AccentBrush = AccentBrush();
        _popup.Apply(state, IsDarkTheme(), IsConnected());

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
