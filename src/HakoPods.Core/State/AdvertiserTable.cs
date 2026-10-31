
namespace HakoPods.Core.State;

public readonly record struct AdvertiserEntry(DeviceState State, short Rssi, DateTimeOffset SeenAt, int EarAgreements);

/// <summary>
/// Keeps one snapshot per advertiser instead of one global snapshot.
///
/// The pair broadcasts from several rotating addresses, and they disagree — different status
/// bytes, different ear bits, RSSI from −39 to −81 dBm. Applying whichever packet arrived last
/// meant the card mixed values from different devices and reported buds that were not worn. The
/// table keeps each advertiser's own story intact and hands the display a single coherent one.
/// </summary>
public sealed class AdvertiserTable
{
    private readonly Dictionary<ulong, AdvertiserEntry> _entries = new();

    public IReadOnlyDictionary<ulong, AdvertiserEntry> Entries => _entries;

    /// <summary>Applies a decoded packet to its own advertiser, tracking ear stability.</summary>
    public void Observe(ulong address, DeviceState state, short rssi, DateTimeOffset seenAt)
    {
        ArgumentNullException.ThrowIfNull(state);

        var agreements = 0;

        if (_entries.TryGetValue(address, out var previous)
            && previous.State.LeftInEar == state.LeftInEar
            && previous.State.RightInEar == state.RightInEar)
        {
            agreements = previous.EarAgreements + 1;
        }

        _entries[address] = new AdvertiserEntry(state, rssi, seenAt, agreements);
    }

    /// <summary>
    /// The advertiser to show. Stability first, then closeness, then recency: a device whose ear
    /// bits keep repeating is describing a real wearer, while a one-off packet is usually a
    /// cached replay. Ties fall to the strongest signal, which is the nearest bud.
    /// </summary>
    public DeviceState? Best(DateTimeOffset now, TimeSpan maxAge)
    {
        AdvertiserEntry? winner = null;

        foreach (var entry in _entries.Values)
        {
            if (now - entry.SeenAt > maxAge)
            {
                continue;
            }

            if (winner is null)
            {
                winner = entry;
                continue;
            }

            var better = entry.EarAgreements != winner.Value.EarAgreements
                ? entry.EarAgreements > winner.Value.EarAgreements
                : entry.Rssi != winner.Value.Rssi
                    ? entry.Rssi > winner.Value.Rssi
                    : entry.SeenAt > winner.Value.SeenAt;

            if (better)
            {
                winner = entry;
            }
        }

        return winner?.State;
    }

    public void Clear() => _entries.Clear();
}
