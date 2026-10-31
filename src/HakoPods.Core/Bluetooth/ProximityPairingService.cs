using System.Threading.Channels;
using HakoPods.Core.Advertisements;

namespace HakoPods.Core.Bluetooth;

/// <summary>One decoded, de-duplicated sample from a nearby pair.</summary>
public sealed record ProximityPairingSample(
    ProximityPairingAdvertisement Advertisement,
    ulong Address,
    short Rssi,
    DateTimeOffset ReceivedAt);

/// <summary>
/// Turns a raw advertisement stream into a clean, de-duplicated stream of decoded samples,
/// and keeps the watcher alive.
///
/// Responsibilities that are deliberately testable without a radio:
///   • drop anything that is not an Apple proximity-pairing payload;
///   • drop repeats of the same payload from the same address inside the dedupe window
///     (Windows re-delivers packets, and the payload rarely changes);
///   • restart the watcher after it stops on its own — sleep/resume, radio toggle, adapter
///     removal all stop it silently, with no exception.
/// </summary>
public sealed class ProximityPairingService : IAsyncDisposable
{
    private readonly IAdvertisementWatcher _watcher;
    private readonly TimeSpan _dedupeWindow;
    private readonly TimeSpan _restartDelay;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Channel<ProximityPairingSample> _channel;
    private readonly Dictionary<ulong, (string Payload, DateTimeOffset At)> _lastSeen = new();
    private readonly object _gate = new();
    private readonly CancellationTokenSource _disposeCts = new();

    private bool _started;
    private bool _disposed;
    private int _restartCount;

    public ProximityPairingService(
        IAdvertisementWatcher watcher,
        TimeSpan? dedupeWindow = null,
        TimeSpan? restartDelay = null,
        Func<DateTimeOffset>? clock = null)
    {
        _watcher = watcher;
        _dedupeWindow = dedupeWindow ?? TimeSpan.FromMilliseconds(250);
        _restartDelay = restartDelay ?? TimeSpan.FromSeconds(1);
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _channel = Channel.CreateUnbounded<ProximityPairingSample>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

        _watcher.Received += OnReceived;
        _watcher.Stopped += OnStopped;
    }

    /// <summary>How many times the watcher had to be revived. Useful in diagnostics.</summary>
    public int RestartCount => Volatile.Read(ref _restartCount);

    public IAsyncEnumerable<ProximityPairingSample> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);

    public void Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_started)
            {
                return;
            }

            _started = true;
        }

        _watcher.Start();
    }

    public void Stop()
    {
        lock (_gate)
        {
            _started = false;
        }

        _watcher.Stop();
    }

    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _watcher.Received -= OnReceived;
        _watcher.Stopped -= OnStopped;

        await _disposeCts.CancelAsync().ConfigureAwait(false);
        _watcher.Dispose();
        _channel.Writer.TryComplete();
        _disposeCts.Dispose();
    }

    private void OnReceived(RawAdvertisement raw)
    {
        var advert = ProximityPairingParser.Parse(raw.Payload);
        if (advert is null)
        {
            return;
        }

        var now = _clock();
        var key = Convert.ToHexString(raw.Payload);

        lock (_gate)
        {
            if (_lastSeen.TryGetValue(raw.Address, out var previous)
                && previous.Payload == key
                && now - previous.At < _dedupeWindow)
            {
                return;
            }

            _lastSeen[raw.Address] = (key, now);
        }

        _channel.Writer.TryWrite(new ProximityPairingSample(advert, raw.Address, raw.Rssi, now));
    }

    private void OnStopped(string? error)
    {
        bool shouldRestart;
        lock (_gate)
        {
            shouldRestart = _started && !_disposed;
        }

        if (shouldRestart)
        {
            _ = RestartAsync();
        }
    }

    private async Task RestartAsync()
    {
        try
        {
            await Task.Delay(_restartDelay, _disposeCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        lock (_gate)
        {
            if (!_started || _disposed)
            {
                return;
            }
        }

        Interlocked.Increment(ref _restartCount);
        _watcher.Start();
    }
}
