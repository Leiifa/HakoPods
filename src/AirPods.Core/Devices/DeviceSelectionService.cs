using AirPods.Core.Settings;

namespace AirPods.Core.Devices;

/// <summary>
/// Remembers which pair the user chose. The address survives re-pairing; if the device is gone
/// the selection resolves to <c>null</c> and the UI asks again rather than guessing.
/// </summary>
public sealed class DeviceSelectionService
{
    private readonly IPairedDeviceProvider _provider;
    private readonly JsonSettingsStore _store;
    private AppSettings _settings;

    public DeviceSelectionService(IPairedDeviceProvider provider, JsonSettingsStore store, AppSettings? settings = null)
    {
        _provider = provider;
        _store = store;
        _settings = settings ?? store.Load();
    }

    public string? SelectedAddress => _settings.PreferredDeviceAddress;

    public Task<IReadOnlyList<PairedDevice>> ListAsync(CancellationToken cancellationToken = default) =>
        _provider.ListAsync(cancellationToken);

    /// <summary>The stored device if it is still paired, otherwise null.</summary>
    public PairedDevice? FindSelected(IReadOnlyList<PairedDevice> devices)
    {
        if (string.IsNullOrEmpty(SelectedAddress))
        {
            return null;
        }

        foreach (var device in devices)
        {
            if (string.Equals(device.AddressText, SelectedAddress, StringComparison.OrdinalIgnoreCase))
            {
                return device;
            }
        }

        return null;
    }

    public void Select(PairedDevice device)
    {
        _settings = _settings with { PreferredDeviceAddress = device.AddressText };
        _store.Save(_settings);
    }

    public void Clear()
    {
        _settings = _settings with { PreferredDeviceAddress = null };
        _store.Save(_settings);
    }
}
