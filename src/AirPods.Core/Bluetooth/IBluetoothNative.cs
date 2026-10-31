namespace AirPods.Core.Bluetooth;

public readonly record struct BluetoothDeviceSnapshot(bool Connected, bool Remembered, string Name);

/// <summary>
/// The two Win32 facts the connection manager needs: what the radio thinks of a device, and
/// whether it will enable a profile. Split out so the manager's retry/timeout/idempotency
/// logic is testable without a Bluetooth radio.
/// </summary>
public interface IBluetoothNative
{
    bool TryGetDevice(ulong address, out BluetoothDeviceSnapshot snapshot);

    /// <summary>Returns the raw Win32 error code (0 = success), exactly as the API does.</summary>
    uint SetServiceState(ulong address, Guid service, bool enable);
}
