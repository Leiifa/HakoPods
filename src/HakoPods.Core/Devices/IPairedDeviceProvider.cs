namespace HakoPods.Core.Devices;

/// <summary>Lists devices Windows already knows about. The seam exists so selection logic is testable.</summary>
public interface IPairedDeviceProvider
{
    Task<IReadOnlyList<PairedDevice>> ListAsync(CancellationToken cancellationToken = default);
}
