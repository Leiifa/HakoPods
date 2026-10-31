using System.Diagnostics;
using HakoPods.Core.Logging;

namespace HakoPods.Core.Bluetooth;

public readonly record struct ConnectionResult(bool Success, string? Error, int Attempts, TimeSpan Elapsed, string? Note = null);

/// <summary>
/// Turns raw <c>BluetoothSetServiceState</c> calls into a UI-safe operation: serialised,
/// idempotent, retried, time-boxed and logged.
///
/// Two measurements shape this class (2026-10-31):
/// <list type="bullet">
/// <item><c>fConnected</c> is per device, not per service. A pair linked over HFP reports
/// connected while A2DP is off, so it is never used to short-circuit a call and never used to
/// prove a disconnect.</item>
/// <item>Disabling A2DP alone leaves the pair linked over HFP — Windows keeps showing it as
/// connected and it never hands back to the phone. Disconnect therefore disables every profile
/// the pair uses.</item>
/// </list>
/// </summary>
public sealed class BluetoothConnectionManager : IDisposable
{
    private readonly IBluetoothNative _native;
    private readonly IAppLogger _logger;
    private readonly TimeSpan _timeout;
    private readonly int _maxAttempts;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Guid _connectService;
    private readonly IReadOnlyList<Guid> _disconnectServices;
    private bool _disposed;

    public BluetoothConnectionManager(
        IBluetoothNative native,
        IAppLogger logger,
        TimeSpan? timeout = null,
        int maxAttempts = 3,
        Guid? service = null,
        IReadOnlyList<Guid>? disconnectServices = null)
    {
        _native = native ?? throw new ArgumentNullException(nameof(native));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeout = timeout ?? TimeSpan.FromSeconds(20);
        _maxAttempts = maxAttempts < 1 ? 1 : maxAttempts;
        _connectService = service ?? Win32BluetoothNative.A2dpSinkService;

        // Order matters: stereo first, then hands-free, so music stops before the link that
        // carries the microphone is torn down.
        _disconnectServices = disconnectServices
            ?? new[] { _connectService, Win32BluetoothNative.HandsfreeService };
    }

    public Task<ConnectionResult> ConnectAsync(ulong address, CancellationToken cancellationToken = default) =>
        RunAsync(address, _connectService, enable: true, cancellationToken);

    /// <summary>
    /// Turns off the hands-free profile while leaving stereo alone. Measured in APW-015: this
    /// removes the microphone endpoints, so Windows cannot drop A2DP into narrowband headset mode
    /// when an app grabs the mic — the "stereo always" policy.
    /// </summary>
    public Task<ConnectionResult> DisableHandsFreeAsync(ulong address, CancellationToken cancellationToken = default) =>
        RunAsync(address, Win32BluetoothNative.HandsfreeService, enable: false, cancellationToken);

    public async Task<ConnectionResult> DisconnectAsync(ulong address, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var results = new List<ConnectionResult>(_disconnectServices.Count);
            foreach (var service in _disconnectServices)
            {
                results.Add(await RunLockedAsync(address, service, enable: false, cancellationToken).ConfigureAwait(false));

                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }

            return Combine(results);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _gate.Dispose();
    }

    private async Task<ConnectionResult> RunAsync(ulong address, Guid service, bool enable, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return await RunLockedAsync(address, service, enable, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Caller must already hold <see cref="_gate"/>.</summary>
    private async Task<ConnectionResult> RunLockedAsync(ulong address, Guid service, bool enable, CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(_timeout);

        var stopwatch = Stopwatch.StartNew();
        var verb = enable ? "connect" : "disconnect";
        var label = DescribeService(service);
        string? lastError = null;

        for (var attempt = 1; attempt <= _maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_native.TryGetDevice(address, out var device))
            {
                // BluetoothGetDeviceInfo returns ERROR_NOT_FOUND both when the device is not
                // paired and when it has dropped out of the radio's list (asleep, out of range,
                // or connected elsewhere). Naming all three keeps people from unpairing a pair
                // that is simply asleep.
                const string error = "device is not reachable (asleep, out of range, or not paired)";
                _logger.Warn($"Cannot {verb} {label} on {FormatAddress(address)}: {error}.");
                return new ConnectionResult(false, error, attempt, stopwatch.Elapsed);
            }

            var code = _native.SetServiceState(address, service, enable);
            if (code == 0)
            {
                _logger.Info($"{device.Name}: {verb} {label} accepted after {stopwatch.ElapsedMilliseconds} ms (attempt {attempt}).");
                return new ConnectionResult(true, null, attempt, stopwatch.Elapsed);
            }

            // Measured 2026-10-31: a call that changes nothing returns ERROR_INVALID_PARAMETER
            // (87) in ~2 ms, while a real enable blocks ~3 s and returns 0. It means "already in
            // the requested state" in both directions.
            if (code is 87 or 0x80070057)
            {
                _logger.Info($"{device.Name}: {label} already {(enable ? "enabled" : "disabled")}; nothing to do.");
                return new ConnectionResult(true, null, attempt, stopwatch.Elapsed, $"already {(enable ? "enabled" : "disabled")}");
            }

            lastError = $"0x{code:X8} ({DescribeError(code)})";
            _logger.Warn($"BthSetServiceState({verb} {label}) for {device.Name} failed with {lastError} (attempt {attempt}/{_maxAttempts}).");

            if (attempt == _maxAttempts)
            {
                break;
            }

            try
            {
                await Task.Delay(Backoff(attempt), timeoutSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                var error = $"timed out after {_timeout.TotalSeconds:0}s";
                _logger.Warn($"Cannot {verb} {label} on {device.Name}: {error}.");
                return new ConnectionResult(false, error, attempt, stopwatch.Elapsed);
            }
        }

        return new ConnectionResult(false, lastError, _maxAttempts, stopwatch.Elapsed);
    }

    private static ConnectionResult Combine(IReadOnlyList<ConnectionResult> results)
    {
        if (results.Count == 0)
        {
            return new ConnectionResult(true, null, 0, TimeSpan.Zero);
        }

        var elapsed = TimeSpan.Zero;
        var attempts = 0;
        var failures = new List<string>();

        foreach (var result in results)
        {
            elapsed += result.Elapsed;
            attempts = Math.Max(attempts, result.Attempts);

            if (!result.Success && result.Error is not null)
            {
                failures.Add(result.Error);
            }
        }

        var note = results.All(r => r.Note is not null) ? "already disabled" : null;
        return failures.Count == 0
            ? new ConnectionResult(true, null, attempts, elapsed, note)
            : new ConnectionResult(false, string.Join("; ", failures.Distinct()), attempts, elapsed);
    }

    private static TimeSpan Backoff(int attempt) => TimeSpan.FromMilliseconds(400 * attempt);

    internal static string DescribeService(Guid service) =>
        service == Win32BluetoothNative.A2dpSinkService ? "A2DP"
        : service == Win32BluetoothNative.HandsfreeService ? "HFP"
        : service.ToString("B");

    private static string FormatAddress(ulong address)
    {
        var text = address.ToString("X12");
        return string.Join(':', Enumerable.Range(0, 6).Select(i => text.Substring(i * 2, 2))).ToLowerInvariant();
    }

    internal static string DescribeError(uint code) => code switch
    {
        0 => "success",
        5 or 0x80070005 => "access denied",
        87 or 0x80070057 => "invalid parameter",
        1168 or 0x80070490 => "not found",
        _ => "see winerror.h",
    };
}
