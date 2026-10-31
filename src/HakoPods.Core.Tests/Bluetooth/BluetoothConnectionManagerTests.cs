using HakoPods.Core.Bluetooth;
using HakoPods.Core.Logging;
using Xunit;

namespace HakoPods.Core.Tests.Bluetooth;

public class BluetoothConnectionManagerTests
{
    private const ulong Address = 0x74650C77CE1F;

    [Fact]
    public async Task Connect_AlwaysQueriesTheRadio_BecauseConnectedIsPerDeviceNotPerService()
    {
        // A pair connected over HFP reports fConnected = true while A2DP is off. Short-circuiting
        // on that flag left the machine with "Connected mic" and no output endpoint.
        var native = new FakeNative { Connected = true };
        using var manager = new BluetoothConnectionManager(native, NullAppLogger.Instance);

        var result = await manager.ConnectAsync(Address);

        Assert.True(result.Success);
        Assert.Single(native.Calls);
        Assert.Equal(Win32BluetoothNative.A2dpSinkService, native.Calls[0].Service);
        Assert.True(native.Calls[0].Enable);
    }

    [Fact]
    public async Task Connect_RetriesTransientFailure_ThenSucceeds()
    {
        var native = new FakeNative();
        native.ServiceResults.Enqueue(0x80070005);

        using var manager = new BluetoothConnectionManager(native, NullAppLogger.Instance, maxAttempts: 3);

        var result = await manager.ConnectAsync(Address);

        Assert.True(result.Success);
        Assert.Equal(2, result.Attempts);
        Assert.Equal(2, native.Calls.Count);
    }

    [Fact]
    public async Task Connect_WhenEveryAttemptFails_ReturnsLastNativeError()
    {
        var native = new FakeNative();
        native.ServiceResults.Enqueue(1168);
        native.ServiceResults.Enqueue(1168);

        using var manager = new BluetoothConnectionManager(native, NullAppLogger.Instance, maxAttempts: 2);

        var result = await manager.ConnectAsync(Address);

        Assert.False(result.Success);
        Assert.Equal(2, result.Attempts);
        Assert.NotNull(result.Error);
        Assert.Contains("00000490", result.Error);
        Assert.Contains("not found", result.Error);
    }

    [Fact]
    public async Task Connect_WhenDeviceIsUnreachable_FailsWithoutCallingTheService()
    {
        var native = new FakeNative { Paired = false };
        using var manager = new BluetoothConnectionManager(native, NullAppLogger.Instance);

        var result = await manager.ConnectAsync(Address);

        Assert.False(result.Success);
        Assert.Empty(native.Calls);
        Assert.Contains("not reachable", result.Error);
    }

    [Fact]
    public async Task Connect_WhenTheServiceIsAlreadyEnabled_TreatsInvalidParameterAsDone()
    {
        // Windows returns 87 in ~2 ms when the service is already enabled; a real enable blocks
        // ~3 s and returns 0. Reporting failure here would break every connect on a sleeping pair.
        var native = new FakeNative();
        native.ServiceResults.Enqueue(87);

        using var manager = new BluetoothConnectionManager(native, NullAppLogger.Instance, maxAttempts: 3);

        var result = await manager.ConnectAsync(Address);

        Assert.True(result.Success);
        Assert.Equal(1, result.Attempts);
        Assert.Equal("already enabled", result.Note);
    }

    [Fact]
    public async Task Disconnect_DisablesStereoAndHandsfree_StereoFirst()
    {
        // Disabling A2DP alone leaves the pair linked over HFP: Windows still shows it as
        // connected and it never hands back to the phone.
        var native = new FakeNative { Connected = true };
        using var manager = new BluetoothConnectionManager(native, NullAppLogger.Instance);

        var result = await manager.DisconnectAsync(Address);

        Assert.True(result.Success);
        Assert.Equal(2, native.Calls.Count);

        Assert.Equal(Win32BluetoothNative.A2dpSinkService, native.Calls[0].Service);
        Assert.False(native.Calls[0].Enable);

        Assert.Equal(Win32BluetoothNative.HandsfreeService, native.Calls[1].Service);
        Assert.False(native.Calls[1].Enable);
    }

    [Fact]
    public async Task Disconnect_ReportsFailureWhenOneProfileFails()
    {
        var native = new FakeNative();
        native.ServiceResults.Enqueue(0);        // A2DP disabled
        native.ServiceResults.Enqueue(0x80070005); // HFP refused

        using var manager = new BluetoothConnectionManager(native, NullAppLogger.Instance, maxAttempts: 1);

        var result = await manager.DisconnectAsync(Address);

        Assert.False(result.Success);
        Assert.Contains("access denied", result.Error);
    }

    [Fact]
    public async Task Disconnect_WhenBothProfilesAreAlreadyOff_SucceedsWithANote()
    {
        var native = new FakeNative();
        native.ServiceResults.Enqueue(87);
        native.ServiceResults.Enqueue(87);

        using var manager = new BluetoothConnectionManager(native, NullAppLogger.Instance);

        var result = await manager.DisconnectAsync(Address);

        Assert.True(result.Success);
        Assert.Equal("already disabled", result.Note);
    }

    [Fact]
    public async Task Failures_AreLoggedWithTheNativeCode()
    {
        var logger = new RecordingLogger();
        var native = new FakeNative();
        native.ServiceResults.Enqueue(0x80070005);

        using var manager = new BluetoothConnectionManager(native, logger, maxAttempts: 1);

        await manager.ConnectAsync(Address);

        Assert.Contains(logger.Warnings, w => w.Contains("0x80070005") && w.Contains("access denied"));
    }

    private sealed class FakeNative : IBluetoothNative
    {
        public bool Paired { get; init; } = true;

        public bool Connected { get; init; }

        public Queue<uint> ServiceResults { get; } = new();

        public List<(Guid Service, bool Enable)> Calls { get; } = new();

        public bool TryGetDevice(ulong address, out BluetoothDeviceSnapshot snapshot)
        {
            snapshot = new BluetoothDeviceSnapshot(Connected, Remembered: true, "23's AirPods Pro");
            return Paired;
        }

        public uint SetServiceState(ulong address, Guid service, bool enable)
        {
            Calls.Add((service, enable));
            return ServiceResults.Count > 0 ? ServiceResults.Dequeue() : 0;
        }
    }

    private sealed class RecordingLogger : IAppLogger
    {
        public List<string> Infos { get; } = new();

        public List<string> Warnings { get; } = new();

        public void Info(string message) => Infos.Add(message);

        public void Warn(string message) => Warnings.Add(message);

        public void Error(string message, Exception? exception = null) => Warnings.Add(message);
    }
}
