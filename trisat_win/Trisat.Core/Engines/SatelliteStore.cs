using Trisat.Core.Models;

namespace Trisat.Core.Engines;

/// <summary>
/// Thread-safe anti-flicker satellite retention cache (30-second sliding window),
/// matching TRISAT iOS/macOS persistent open-sky tracking behavior.
/// </summary>
public sealed class SatelliteStore
{
    private readonly object _lock = new();
    private readonly Dictionary<string, (SatelliteInfo Info, DateTimeOffset LastSeen)> _entries = new();
    private readonly TimeSpan _retentionWindow;

    public SatelliteStore(TimeSpan? retentionWindow = null)
    {
        _retentionWindow = retentionWindow ?? TimeSpan.FromSeconds(30);
    }

    public IReadOnlyList<SatelliteInfo> UpsertRange(IEnumerable<SatelliteInfo> satellites, DateTimeOffset? nowUtc = null)
    {
        var now = nowUtc ?? DateTimeOffset.UtcNow;
        lock (_lock)
        {
            foreach (var sat in satellites)
            {
                var key = $"{sat.Constellation}_{sat.Svid}";
                _entries[key] = (sat with { Id = key }, now);
            }

            PruneLocked(now);
            return SnapshotLocked();
        }
    }

    public IReadOnlyList<SatelliteInfo> GetActive(DateTimeOffset? nowUtc = null)
    {
        var now = nowUtc ?? DateTimeOffset.UtcNow;
        lock (_lock)
        {
            PruneLocked(now);
            return SnapshotLocked();
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _entries.Clear();
        }
    }

    private void PruneLocked(DateTimeOffset now)
    {
        var cutoff = now - _retentionWindow;
        var staleKeys = _entries.Where(kv => kv.Value.LastSeen < cutoff).Select(kv => kv.Key).ToList();
        foreach (var k in staleKeys)
            _entries.Remove(k);
    }

    private IReadOnlyList<SatelliteInfo> SnapshotLocked() =>
        _entries.Values
            .Select(v => v.Info)
            .OrderBy(s => s.Constellation)
            .ThenBy(s => s.Svid)
            .ToList();
}
