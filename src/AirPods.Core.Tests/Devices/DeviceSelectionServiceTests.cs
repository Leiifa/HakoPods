using AirPods.Core.Devices;
using AirPods.Core.Settings;
using Xunit;

namespace AirPods.Core.Tests.Devices;

public class DeviceSelectionServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "apw-tests", Guid.NewGuid().ToString("N"));

    private string SettingsPath => Path.Combine(_directory, "settings.json");

    private static readonly PairedDevice AirPods = new("23's AirPods Pro", 0x74650C77CE1F);
    private static readonly PairedDevice Lamp = new("V-600 LAMP", 0x3073B40EAB63);

    [Fact]
    public async Task Lists_what_the_provider_returns()
    {
        var service = Create(new FakeProvider(AirPods, Lamp));

        var devices = await service.ListAsync();

        Assert.Equal(2, devices.Count);
        Assert.Contains(devices, d => d.Name == "23's AirPods Pro" && d.LooksLikeAirPods);
        Assert.Contains(devices, d => d.Name == "V-600 LAMP" && !d.LooksLikeAirPods);
    }

    [Fact]
    public async Task Selection_persists_across_instances()
    {
        var first = Create(new FakeProvider(AirPods, Lamp));
        first.Select(AirPods);

        var second = Create(new FakeProvider(AirPods, Lamp));

        Assert.Equal(AirPods.AddressText, second.SelectedAddress);
        var devices = await second.ListAsync();
        Assert.Equal(AirPods, second.FindSelected(devices));
    }

    [Fact]
    public async Task Selection_survives_re_pairing_when_the_address_is_unchanged()
    {
        // Re-pairing keeps the Bluetooth address; only the friendly name may change.
        var service = Create(new FakeProvider(AirPods));
        service.Select(AirPods);

        var renamed = new PairedDevice("Living room AirPods", AirPods.Address);
        var devices = await Create(new FakeProvider(renamed)).ListAsync();

        Assert.Equal(renamed, service.FindSelected(devices));
    }

    [Fact]
    public void Selection_resolves_to_null_when_the_device_is_gone()
    {
        var service = Create(new FakeProvider(Lamp));
        service.Select(AirPods); // stored, but not currently paired

        Assert.Null(service.FindSelected(new[] { Lamp }));
    }

    [Fact]
    public async Task Empty_provider_is_not_an_error()
    {
        var service = Create(new FakeProvider());

        var devices = await service.ListAsync();

        Assert.Empty(devices);
        Assert.Null(service.FindSelected(devices));
    }

    [Fact]
    public void Clear_removes_the_selection()
    {
        var service = Create(new FakeProvider(AirPods));
        service.Select(AirPods);

        service.Clear();

        Assert.Null(service.SelectedAddress);
        Assert.Null(Create(new FakeProvider(AirPods)).SelectedAddress);
    }

    [Fact]
    public void Parses_windows_addresses_and_formats_them_without_separators()
    {
        Assert.True(PairedDevice.TryParseWindowsAddress("74:65:0C:77:CE:1F", out var parsed));
        Assert.Equal(0x74650C77CE1FUL, parsed);
        Assert.Equal("74650C77CE1F", new PairedDevice("x", parsed).AddressText);

        Assert.False(PairedDevice.TryParseWindowsAddress("not-an-address", out _));
        Assert.False(PairedDevice.TryParseWindowsAddress(null, out _));
    }

    private DeviceSelectionService Create(IPairedDeviceProvider provider) =>
        new(provider, new JsonSettingsStore(SettingsPath));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class FakeProvider(params PairedDevice[] devices) : IPairedDeviceProvider
    {
        public Task<IReadOnlyList<PairedDevice>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PairedDevice>>(devices);
    }
}
