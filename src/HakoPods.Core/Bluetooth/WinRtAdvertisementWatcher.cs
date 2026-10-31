using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Storage.Streams;

namespace HakoPods.Core.Bluetooth;

/// <summary>
/// Thin adapter over <see cref="BluetoothLEAdvertisementWatcher"/>.
///
/// Two details learned the hard way (docs/RESEARCH.md §6): extended advertisements must be
/// enabled explicitly, and company-ID filtering is done here rather than through
/// <c>AdvertisementFilter</c>, because filtering on a manufacturer data section is unreliable
/// across adapters.
/// </summary>
public sealed class WinRtAdvertisementWatcher : IAdvertisementWatcher
{
    private const ushort AppleCompanyId = 0x004C;

    private readonly BluetoothLEAdvertisementWatcher _watcher;
    private bool _disposed;

    public WinRtAdvertisementWatcher()
    {
        _watcher = new BluetoothLEAdvertisementWatcher
        {
            ScanningMode = BluetoothLEScanningMode.Active,
            AllowExtendedAdvertisements = true,
        };

        _watcher.Received += OnReceived;
        _watcher.Stopped += OnStopped;
    }

    public event Action<RawAdvertisement>? Received;

    public event Action<string?>? Stopped;

    public WatcherStatus Status => _watcher.Status switch
    {
        BluetoothLEAdvertisementWatcherStatus.Started => WatcherStatus.Started,
        BluetoothLEAdvertisementWatcherStatus.Stopping => WatcherStatus.Stopping,
        BluetoothLEAdvertisementWatcherStatus.Stopped => WatcherStatus.Stopped,
        BluetoothLEAdvertisementWatcherStatus.Aborted => WatcherStatus.Aborted,
        _ => WatcherStatus.Created,
    };

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _watcher.Start();
    }

    public void Stop()
    {
        if (!_disposed)
        {
            _watcher.Stop();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _watcher.Received -= OnReceived;
        _watcher.Stopped -= OnStopped;
        _watcher.Stop();
    }

    private void OnReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
    {
        var sections = args.Advertisement.GetManufacturerDataByCompanyId(AppleCompanyId);
        if (sections.Count == 0)
        {
            return;
        }

        // WinRT includes the 2-byte company ID in the section payload.
        var data = ToBytes(sections[0].Data);
        if (data.Length >= 2 && data[0] == 0x4C && data[1] == 0x00)
        {
            data = data[2..];
        }

        Received?.Invoke(new RawAdvertisement(args.BluetoothAddress, args.RawSignalStrengthInDBm, data));
    }

    private void OnStopped(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementWatcherStoppedEventArgs args) =>
        Stopped?.Invoke(args.Error == BluetoothError.Success ? null : args.Error.ToString());

    private static byte[] ToBytes(IBuffer buffer)
    {
        var reader = DataReader.FromBuffer(buffer);
        var bytes = new byte[buffer.Length];
        reader.ReadBytes(bytes);
        return bytes;
    }
}
