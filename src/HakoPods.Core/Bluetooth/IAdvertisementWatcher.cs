namespace HakoPods.Core.Bluetooth;

/// <summary>One raw manufacturer-data payload from a BLE advertiser.</summary>
/// <param name="Address">Rotating BLE address — an ephemeral identifier, never a device identity.</param>
/// <param name="Rssi">Signal strength in dBm; values around −127 are stale/phantom.</param>
/// <param name="Payload">Manufacturer data <em>after</em> the 2-byte company ID.</param>
public readonly record struct RawAdvertisement(ulong Address, short Rssi, byte[] Payload);

public enum WatcherStatus
{
    Created,
    Started,
    Stopping,
    Stopped,
    Aborted,
}

/// <summary>
/// Minimal surface over the OS advertisement watcher so the interesting logic — filtering,
/// de-duplication, self-healing — can be tested without a radio.
/// </summary>
public interface IAdvertisementWatcher : IDisposable
{
    event Action<RawAdvertisement>? Received;

    /// <summary>Raised when the watcher stops; the argument carries an error, or null on a clean stop.</summary>
    event Action<string?>? Stopped;

    WatcherStatus Status { get; }

    void Start();

    void Stop();
}
