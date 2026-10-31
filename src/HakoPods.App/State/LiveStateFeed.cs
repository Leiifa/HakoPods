using System.Windows.Threading;
using HakoPods.Core.Bluetooth;
using HakoPods.Core.State;

namespace HakoPods.App.State;

/// <summary>
/// The live bridge between advertisements and the tray: watcher → de-duplicated samples →
/// reducer → <see cref="DeviceState"/> on the UI thread.
///
/// Everything stateful runs on the dispatcher, so the reducer never sees two writers and the
/// tray never sees a half-applied snapshot. A timer ages the state so a pair that walks away
/// drops its numbers instead of freezing them.
/// </summary>
public sealed class LiveStateFeed : IAsyncDisposable
{
    private WinRtAdvertisementWatcher _watcher = new();
    private ProximityPairingService _service;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _expiry;
    private readonly ulong? _address;
    private readonly int _nearThresholdDbm;
    private CancellationTokenSource? _cancellation;
    private DeviceState _state = DeviceState.Empty;
    private DateTimeOffset _lastSampleAt = DateTimeOffset.MinValue;
    private int _restartsSinceSample;
    private readonly AdvertiserTable _advertisers = new();
    private bool _disposed;

    public LiveStateFeed(ulong? address, Dispatcher dispatcher, int nearThresholdDbm = -72)
    {
        _address = address;
        _nearThresholdDbm = nearThresholdDbm;
        _dispatcher = dispatcher;
        _service = new ProximityPairingService(_watcher);

        _expiry = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = TimeSpan.FromSeconds(5),
        };
        _expiry.Tick += (_, _) => Publish(DeviceStateReducer.Expire(_state, DateTimeOffset.UtcNow));
    }

    public event EventHandler<DeviceState>? StateChanged;

    public DeviceState Current => _state;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_cancellation is not null)
        {
            return;
        }

        _cancellation = new CancellationTokenSource();
        _service.Start();
        _expiry.Start();
        var service = _service;
        _ = Task.Run(() => PumpAsync(service, _cancellation.Token));
    }

    /// <summary>
    /// Kicks the watcher when it has gone quiet for too long. The service revives itself on a
    /// stopped callback, but turning the radio off and on can leave the new watcher failing to
    /// start while nothing reports a stop — the app then sits deaf forever, showing "No AirPods
    /// nearby" while a bud is in an ear.
    /// </summary>
    public void EnsureAlive(TimeSpan? after = null)
    {
        if (_disposed || _cancellation is null)
        {
            return;
        }

        var limit = after ?? TimeSpan.FromSeconds(30);
        if (DateTimeOffset.UtcNow - _lastSampleAt > limit)
        {
            Restart();
            _lastSampleAt = DateTimeOffset.UtcNow;
        }
    }

    /// <summary>
    /// Restarts the watcher. The service revives itself when the watcher reports stopped, but a
    /// machine waking from sleep does not always produce that callback — the radio comes back
    /// without anyone noticing. Resuming is the one case that must be forced.
    /// </summary>
    /// <summary>Revivals attempted since the last advertisement. Two means the watcher is not coming back.</summary>
    public int RestartsSinceLastSample => _restartsSinceSample;

    public void Restart()
    {
        _restartsSinceSample++;
        if (_disposed)
        {
            return;
        }

        // A radio toggle invalidates the WinRT watcher object itself: the same instance will not
        // restart, and no callback says so. Only a fresh watcher and service can recover.
        var oldService = _service;
        var oldWatcher = _watcher;
        var oldCancellation = _cancellation;

        _watcher = new WinRtAdvertisementWatcher();
        _service = new ProximityPairingService(_watcher);
        _cancellation = new CancellationTokenSource();

        _service.Start();
        var service = _service;
        _ = Task.Run(() => PumpAsync(service, _cancellation.Token));

        oldCancellation?.Cancel();
        oldCancellation?.Dispose();
        _ = oldService.DisposeAsync();
        oldWatcher.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _expiry.Stop();
        _cancellation?.Cancel();
        await _service.DisposeAsync().ConfigureAwait(false);
        _watcher.Dispose();
        _cancellation?.Dispose();
    }

    private async Task PumpAsync(ProximityPairingService service, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var sample in service.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                // v1 is single-pair: ignore anything that is not the configured device, so a
                // neighbour's AirPods cannot overwrite the tray.
                // A neighbour's AirPods can advertise the same product id from the next desk.
                // Distance is the only signal that separates them, so anything weaker than the
                // measured near band is ignored entirely rather than becoming a popup.
                if (sample.Rssi < _nearThresholdDbm)
                {
                    continue;
                }

                if (_address is { } wanted && sample.Address != wanted)
                {
                    continue;
                }

                _dispatcher.Invoke(() =>
                {
                    // Windows re-delivers cached adverts after a wake, and they carry the ear
                    // and lid bits from before the case closed. Applying one would flip the
                    // popup back to a stale state, so older packets are dropped.
                    if (sample.ReceivedAt < DateTimeOffset.UtcNow - TimeSpan.FromSeconds(5))
                    {
                        return;
                    }

                    if (sample.ReceivedAt < _state.LastSeenAt)
                    {
                        return;
                    }

                    // Each advertiser keeps its own story; the display takes one coherent one.
                    var previousForAddress = _advertisers.Entries.TryGetValue(sample.Address, out var entry)
                        ? entry.State
                        : DeviceState.Empty;

                    var next = DeviceStateReducer.Apply(previousForAddress, sample);
                    _advertisers.Observe(sample.Address, next, sample.Rssi, sample.ReceivedAt);

                    if (_advertisers.Best(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(30)) is { } best)
                    {
                        Publish(best);
                    }
                });
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown.
        }
    }

    private void Publish(DeviceState next)
    {
        _state = next;
        _lastSampleAt = DateTimeOffset.UtcNow;
        _restartsSinceSample = 0;
        StateChanged?.Invoke(this, next);
    }
}
