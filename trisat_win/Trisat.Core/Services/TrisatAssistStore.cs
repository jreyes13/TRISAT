using System.Numerics;
using Trisat.Core.Models;

namespace Trisat.Core.Services;

/// <summary>
/// Snapshot of 40-bit Galileo OSNMA fields across all observed Galileo satellites (E01..E36) for one 2-second GST epoch.
/// </summary>
public sealed record OsnmaReferenceEpoch(
    long GstSeconds,
    int WeekNumber,
    int TowSeconds,
    byte? NmaHeaderByte,
    IReadOnlyDictionary<int, string> SatelliteOsnmaHex,
    DateTimeOffset CapturedAtUtc);

/// <summary>
/// Thread-safe, zero-database in-memory sliding-window store for TRISAT-Assist (Reference Station & Bitwise Cross-Check).
/// Retains the most recent window (default 60 seconds) of 40-bit OSNMA fields per Galileo satellite and GST epoch.
/// </summary>
public sealed class TrisatAssistStore
{
    private readonly object _sync = new();
    private readonly int _retentionSeconds;
    private readonly SortedDictionary<long, EpochBucket> _epochs = new();
    private OsnmaAssistMaterial _assistMaterial = new();
    private byte? _latestNmaHeader;

    public TrisatAssistStore(int retentionSeconds = 60)
    {
        _retentionSeconds = Math.Max(10, retentionSeconds);
    }

    public int RetentionSeconds => _retentionSeconds;

    public void RecordReferencePage(
        int svid,
        GstTime gst,
        string osnma40Hex,
        byte? nmaHeaderByte = null,
        DateTimeOffset? capturedAtUtc = null)
    {
        if (svid < 1 || svid > 36 || string.IsNullOrWhiteSpace(osnma40Hex))
            return;

        var cleanHex = osnma40Hex.Trim().ToUpperInvariant();
        if (cleanHex.Length != 10) // 40 bits = 5 bytes = 10 hex chars
            return;

        long alignedGst = (gst.TotalSeconds / 2) * 2;
        var now = capturedAtUtc ?? DateTimeOffset.UtcNow;

        lock (_sync)
        {
            if (nmaHeaderByte.HasValue)
                _latestNmaHeader = nmaHeaderByte.Value;

            if (!_epochs.TryGetValue(alignedGst, out var bucket))
            {
                var alignedTime = GstTime.FromTotalSeconds(alignedGst);
                bucket = new EpochBucket(alignedGst, alignedTime.WeekNumber, alignedTime.TowSeconds, now);
                _epochs[alignedGst] = bucket;
            }

            bucket.NmaHeaderByte ??= nmaHeaderByte ?? _latestNmaHeader;
            bucket.Satellites[svid] = cleanHex;
            bucket.LastUpdatedUtc = now;

            PruneLocked(alignedGst);
        }
    }

    public void UpdateAssistMaterial(OsnmaAssistMaterial material)
    {
        lock (_sync)
        {
            _assistMaterial = new OsnmaAssistMaterial(
                MerkleRootHex: material.MerkleRootHex ?? _assistMaterial.MerkleRootHex,
                PublicKeyId: material.PublicKeyId ?? _assistMaterial.PublicKeyId,
                PublicKeyType: material.PublicKeyType ?? _assistMaterial.PublicKeyType,
                PublicKeyCompressedHex: material.PublicKeyCompressedHex ?? _assistMaterial.PublicKeyCompressedHex,
                DsmKrootHex: material.DsmKrootHex ?? _assistMaterial.DsmKrootHex,
                KrootHex: material.KrootHex ?? _assistMaterial.KrootHex,
                AlphaHex: material.AlphaHex ?? _assistMaterial.AlphaHex,
                WnKroot: material.WnKroot ?? _assistMaterial.WnKroot,
                Tow0Kroot: material.Tow0Kroot ?? _assistMaterial.Tow0Kroot,
                KeySizeBits: material.KeySizeBits > 0 ? material.KeySizeBits : _assistMaterial.KeySizeBits,
                TagSizeBits: material.TagSizeBits > 0 ? material.TagSizeBits : _assistMaterial.TagSizeBits,
                MacltId: material.MacltId > 0 ? material.MacltId : _assistMaterial.MacltId,
                NmaHeaderByte: material.NmaHeaderByte ?? _assistMaterial.NmaHeaderByte ?? _latestNmaHeader,
                Source: material.Source);
        }
    }

    public OsnmaAssistMaterial GetAssistMaterial()
    {
        lock (_sync)
            return _assistMaterial;
    }

    public OsnmaReferenceEpoch? GetEpoch(long? gstSeconds = null)
    {
        lock (_sync)
        {
            if (_epochs.Count == 0)
                return null;

            EpochBucket? bucket;
            if (!gstSeconds.HasValue || gstSeconds.Value <= 0)
            {
                bucket = _epochs[_epochs.Keys.Last()];
            }
            else
            {
                long aligned = (gstSeconds.Value / 2) * 2;
                if (!_epochs.TryGetValue(aligned, out bucket))
                    return null;
            }

            return new OsnmaReferenceEpoch(
                bucket.GstSeconds,
                bucket.WeekNumber,
                bucket.TowSeconds,
                bucket.NmaHeaderByte,
                new Dictionary<int, string>(bucket.Satellites),
                bucket.LastUpdatedUtc);
        }
    }

    public IReadOnlyList<OsnmaReferenceEpoch> GetAllEpochs()
    {
        lock (_sync)
        {
            return _epochs.Values
                .Select(b => new OsnmaReferenceEpoch(
                    b.GstSeconds,
                    b.WeekNumber,
                    b.TowSeconds,
                    b.NmaHeaderByte,
                    new Dictionary<int, string>(b.Satellites),
                    b.LastUpdatedUtc))
                .ToList();
        }
    }

    public int EpochCount
    {
        get { lock (_sync) return _epochs.Count; }
    }

    /// <summary>
    /// Performs exact 40-bit OSNMA bitwise cross-check for (SVID, GST) against the reference store.
    /// Enables 2-second spoofing and meaconing detection without waiting 30-60s for TESLA key disclosure.
    /// </summary>
    public BitwiseCheckResult CompareBitwise(int svid, long gstSeconds, string localOsnma40Hex)
    {
        var cleanLocal = (localOsnma40Hex ?? string.Empty).Trim().ToUpperInvariant();
        if (cleanLocal.Length != 10)
        {
            return new BitwiseCheckResult(
                svid, gstSeconds, false, false, 0, 40, cleanLocal, null,
                "Invalid local OSNMA field length (expected 40 bits / 10 hex chars).");
        }

        long alignedGst = (gstSeconds / 2) * 2;
        string? refHex = null;

        lock (_sync)
        {
            if (_epochs.TryGetValue(alignedGst, out var bucket))
                bucket.Satellites.TryGetValue(svid, out refHex);
        }

        if (refHex is null)
        {
            return new BitwiseCheckResult(
                svid, alignedGst, false, false, 0, 40, cleanLocal, null,
                $"No reference 40-bit OSNMA epoch cached for E{svid:D2} at GST {alignedGst}s.");
        }

        int matchingBits = CountMatchingBits(Convert.FromHexString(cleanLocal), Convert.FromHexString(refHex));
        bool matched = matchingBits == 40;
        string reason = matched
            ? $"40/40 OSNMA bits matched trusted reference for E{svid:D2} at GST {alignedGst}s."
            : $"BITWISE MISMATCH ({matchingBits}/40 bits matched) for E{svid:D2} at GST {alignedGst}s — spoofing/meaconing suspected.";

        return new BitwiseCheckResult(
            svid, alignedGst, true, matched, matchingBits, 40, cleanLocal, refHex, reason);
    }

    public static int CountMatchingBits(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        int len = Math.Min(a.Length, b.Length);
        int differing = 0;
        for (int i = 0; i < len; i++)
            differing += BitOperations.PopCount((uint)(a[i] ^ b[i]));
        return (len * 8) - differing;
    }

    private void PruneLocked(long newestGst)
    {
        long minAllowed = newestGst - _retentionSeconds;
        var stale = _epochs.Keys.Where(k => k < minAllowed).ToList();
        foreach (var k in stale)
            _epochs.Remove(k);
    }

    private sealed class EpochBucket
    {
        public long GstSeconds { get; }
        public int WeekNumber { get; }
        public int TowSeconds { get; }
        public byte? NmaHeaderByte { get; set; }
        public Dictionary<int, string> Satellites { get; } = new();
        public DateTimeOffset LastUpdatedUtc { get; set; }

        public EpochBucket(long gstSeconds, int weekNumber, int towSeconds, DateTimeOffset createdUtc)
        {
            GstSeconds = gstSeconds;
            WeekNumber = weekNumber;
            TowSeconds = towSeconds;
            LastUpdatedUtc = createdUtc;
        }
    }
}
