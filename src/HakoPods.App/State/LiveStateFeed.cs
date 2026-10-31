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
    private readonly WinRtAdvertisementWatcher _watcher = new();
    private readonly ProximityPairingService _service;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _expiry;
    private readonly ulong? _address;
    private readonly int _nearThresholdDbm;
    private CancellationTokenSource? _cancellation;
    private DeviceState _state = DeviceState.Empty;
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
        _ = Task.Run(() => PumpAsync(_cancellation.Token));
    }

    /// <summary>
    /// Restarts the watcher. The service revives itself when the watcher reports stopped, but a
    /// machine waking from sleep does not always produce that callback — the radio comes back
    /// without anyone noticing. Resuming is the one case that must be forced.
    /// </summary>
    public void Restart()
    {
        if (_disposed)
        {
            return;
        }

        _service.Stop();
        _service.Start();
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

    private async Task PumpAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var sample in _service.ReadAllAsync(cancellationToken).ConfigureAwait(false))
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

                    Publish(DeviceStateReducer.Apply(_state, sample));
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
        StateChanged?.Invoke(this, next);
    }
}
