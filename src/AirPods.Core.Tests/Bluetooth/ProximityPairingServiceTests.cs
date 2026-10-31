using AirPods.Core.Bluetooth;
using Xunit;

namespace AirPods.Core.Tests.Bluetooth;

public class ProximityPairingServiceTests
{
    private const string LidOpenPacket = "0719010E2001448F010005B731B49FAEC4F527095EE9A21509499B";

    // Same packet with the left-bud nibble changed (40 → 50). Status 0x01 means the right pod
    // is primary, so the left value lives in the high nibble; used to prove a genuine change is
    // not swallowed by the dedupe window.
    private const string BatteryChangedPacket = "0719010E20015A8F010005B731B49FAEC4F527095EE9A21509499B";

    private const ulong Address = 0x6BE0048EE8FD;

    [Fact]
    public async Task Ignores_payloads_that_are_not_proximity_pairing()
    {
        var watcher = new FakeWatcher();
        await using var service = new ProximityPairingService(watcher);
        service.Start();

        var notOurs = Convert.FromHexString(LidOpenPacket);
        notOurs[0] = 0x06;
        watcher.Raise(Address, -50, notOurs);

        Assert.Null(await TryNextAsync(service));
    }

    [Fact]
    public async Task Emits_a_decoded_sample()
    {
        var watcher = new FakeWatcher();
        await using var service = new ProximityPairingService(watcher);
        service.Start();

        watcher.Raise(Address, -47, Convert.FromHexString(LidOpenPacket));

        var sample = await TryNextAsync(service);
        Assert.NotNull(sample);
        Assert.Equal(Address, sample.Address);
        Assert.Equal(-47, sample.Rssi);
        Assert.Equal("AirPods Pro", sample.Advertisement.ModelName);
        Assert.Equal(40, sample.Advertisement.LeftBatteryPercent);
        Assert.True(sample.Advertisement.LidOpen);
    }

    [Fact]
    public async Task Suppresses_a_repeat_inside_the_dedupe_window()
    {
        var now = DateTimeOffset.UnixEpoch;
        var watcher = new FakeWatcher();
        await using var service = new ProximityPairingService(
            watcher, dedupeWindow: TimeSpan.FromMilliseconds(250), clock: () => now);
        service.Start();

        var packet = Convert.FromHexString(LidOpenPacket);
        watcher.Raise(Address, -50, packet);
        now = now.AddMilliseconds(100);

        watcher.Raise(Address, -50, packet);

        Assert.NotNull(await TryNextAsync(service));
        Assert.Null(await TryNextAsync(service));
    }

    [Fact]
    public async Task Emits_again_once_the_window_has_passed()
    {
        var now = DateTimeOffset.UnixEpoch;
        var watcher = new FakeWatcher();
        await using var service = new ProximityPairingService(
            watcher, dedupeWindow: TimeSpan.FromMilliseconds(250), clock: () => now);
        service.Start();

        var packet = Convert.FromHexString(LidOpenPacket);
        watcher.Raise(Address, -50, packet);
        now = now.AddMilliseconds(400);
        watcher.Raise(Address, -50, packet);

        Assert.NotNull(await TryNextAsync(service));
        Assert.NotNull(await TryNextAsync(service));
    }

    [Fact]
    public async Task Emits_when_the_payload_changes()
    {
        var now = DateTimeOffset.UnixEpoch;
        var watcher = new FakeWatcher();
        await using var service = new ProximityPairingService(
            watcher, dedupeWindow: TimeSpan.FromMilliseconds(250), clock: () => now);
        service.Start();

        watcher.Raise(Address, -50, Convert.FromHexString(LidOpenPacket));
        now = now.AddMilliseconds(20);
        watcher.Raise(Address, -50, Convert.FromHexString(BatteryChangedPacket));

        var first = await TryNextAsync(service);
        var second = await TryNextAsync(service);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(40, first.Advertisement.LeftBatteryPercent);
        Assert.Equal(50, second.Advertisement.LeftBatteryPercent);
    }

    [Fact]
    public async Task Restarts_the_watcher_when_it_stops_by_itself()
    {
        var watcher = new FakeWatcher();
        await using var service = new ProximityPairingService(
            watcher, restartDelay: TimeSpan.FromMilliseconds(10));
        service.Start();
        Assert.Equal(1, watcher.StartCount);

        watcher.RaiseStopped("Aborted");

        await WaitForAsync(() => watcher.StartCount == 2, TimeSpan.FromSeconds(2));
        Assert.Equal(2, watcher.StartCount);
        Assert.Equal(1, service.RestartCount);
    }

    [Fact]
    public async Task Does_not_restart_after_an_explicit_stop()
    {
        var watcher = new FakeWatcher();
        await using var service = new ProximityPairingService(
            watcher, restartDelay: TimeSpan.FromMilliseconds(10));
        service.Start();

        service.Stop(); // the fake raises Stopped(null) here, as the real watcher does
        await Task.Delay(80);

        Assert.Equal(1, watcher.StartCount);
        Assert.Equal(0, service.RestartCount);
    }

    [Fact]
    public async Task Start_is_idempotent()
    {
        var watcher = new FakeWatcher();
        await using var service = new ProximityPairingService(watcher);

        service.Start();
        service.Start();

        Assert.Equal(1, watcher.StartCount);
    }

    [Fact]
    public async Task Dispose_unsubscribes_and_disposes_the_watcher()
    {
        var watcher = new FakeWatcher();
        var service = new ProximityPairingService(watcher);
        service.Start();

        await service.DisposeAsync();
        watcher.Raise(Address, -50, Convert.FromHexString(LidOpenPacket));

        Assert.True(watcher.Disposed);
        Assert.False(watcher.HasSubscribers);
    }

    private static async Task<ProximityPairingSample?> TryNextAsync(
        ProximityPairingService service,
        int waitMs = 200)
    {
        using var cts = new CancellationTokenSource(waitMs);
        try
        {
            await foreach (var sample in service.ReadAllAsync(cts.Token))
            {
                return sample;
            }
        }
        catch (OperationCanceledException)
        {
            // expected: nothing arrived inside the window
        }

        return null;
    }

    private static async Task WaitForAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(10);
        }
    }

    private sealed class FakeWatcher : IAdvertisementWatcher
    {
        public event Action<RawAdvertisement>? Received;

        public event Action<string?>? Stopped;

        public WatcherStatus Status { get; private set; } = WatcherStatus.Created;

        public int StartCount { get; private set; }

        public bool Disposed { get; private set; }

        public bool HasSubscribers => Received is not null || Stopped is not null;

        public void Start()
        {
            StartCount++;
            Status = WatcherStatus.Started;
        }

        public void Stop()
        {
            Status = WatcherStatus.Stopped;
            Stopped?.Invoke(null);
        }

        public void Dispose()
        {
            Disposed = true;
            Received = null;
            Stopped = null;
        }

        public void Raise(ulong address, short rssi, byte[] payload) =>
            Received?.Invoke(new RawAdvertisement(address, rssi, payload));

        public void RaiseStopped(string? error)
        {
            Status = WatcherStatus.Stopped;
            Stopped?.Invoke(error);
        }
    }
}
