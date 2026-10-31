using Windows.Devices.Enumeration;
using Windows.Devices.Bluetooth;

namespace AirPods.Core.Devices;

/// <summary>
/// Enumerates Classic-paired devices through the AEP layer. Deliberately thin: no filtering by
/// name, because the user may pair Beats or a headset, and the UI can label what it likes.
/// </summary>
public sealed class WinRtPairedDeviceProvider : IPairedDeviceProvider
{
    private const string AddressProperty = "System.Devices.Aep.DeviceAddress";

    public async Task<IReadOnlyList<PairedDevice>> ListAsync(CancellationToken cancellationToken = default)
    {
        var selector = BluetoothDevice.GetDeviceSelectorFromPairingState(true);
        var found = await DeviceInformation.FindAllAsync(selector, new[] { AddressProperty })
            .AsTask(cancellationToken)
            .ConfigureAwait(false);

        var devices = new List<PairedDevice>(found.Count);
        foreach (var info in found)
        {
            if (string.IsNullOrWhiteSpace(info.Name))
            {
                continue;
            }

            if (!info.Properties.TryGetValue(AddressProperty, out var raw) || raw is not string text)
            {
                continue;
            }

            if (PairedDevice.TryParseWindowsAddress(text, out var address))
            {
                devices.Add(new PairedDevice(info.Name, address));
            }
        }

        return devices;
    }
}
