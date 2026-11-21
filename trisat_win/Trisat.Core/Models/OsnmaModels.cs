using System.Globalization;

namespace Trisat.Core.Models;

/// <summary>
/// Galileo System Time (GST): Week Number (since 1999-08-22 00:00:00 UTC) and Time of Week in seconds.
/// </summary>
public readonly record struct GstTime(int WeekNumber, int TowSeconds)
{
    public const int SecondsPerWeek = 604_800;
    public const int DefaultLeapSeconds = 18; // Valid since 2017-01-01 (TAI-UTC=37, GST-UTC=18)
    public static readonly DateTimeOffset GalileoEpochUtc = new(1999, 8, 22, 0, 0, 0, TimeSpan.Zero);

    public long TotalSeconds => (long)WeekNumber * SecondsPerWeek + TowSeconds;

    /// <summary>
    /// Start TOW (seconds, multiple of 30) of the 30-second OSNMA subframe containing this GST.
    /// </summary>
    public int SubframeStartTow => (TowSeconds / 30) * 30;

    /// <summary>
    /// Nominal 2-second page index within the 30-second OSNMA subframe (0..14).
    /// </summary>
    public int PageIndexInSubframe => (TowSeconds % 30) / 2;

    public static GstTime FromUtc(DateTimeOffset utc, int leapSeconds = DefaultLeapSeconds)
    {
        var elapsed = (utc.ToUniversalTime() - GalileoEpochUtc).TotalSeconds + leapSeconds;
        if (elapsed < 0)
            return new GstTime(0, 0);

        long totalSec = (long)Math.Floor(elapsed);
        int wn = (int)(totalSec / SecondsPerWeek);
        int tow = (int)(totalSec % SecondsPerWeek);
        return new GstTime(wn, tow);
    }

    public static GstTime FromTotalSeconds(long totalSeconds)
    {
        if (totalSeconds < 0)
            return new GstTime(0, 0);
        int wn = (int)(totalSeconds / SecondsPerWeek);
        int tow = (int)(totalSeconds % SecondsPerWeek);
        return new GstTime(wn, tow);
    }

    public DateTimeOffset ToUtc(int leapSeconds = DefaultLeapSeconds)
        => GalileoEpochUtc.AddSeconds(TotalSeconds - leapSeconds);

    public override string ToString()
        => string.Create(CultureInfo.InvariantCulture, $"WN {WeekNumber} · TOW {TowSeconds:D6}s");
}

/// <summary>
/// Galileo OSNMA NMA Status field (2-bit NMAS inside the 8-bit NMA Header, OSNMA SIS ICD Table 4).
/// </summary>
public enum OsnmaNmasStatus
{
    Reserved = 0,
    Test = 1,
    Operational = 2,
    DontUse = 3
}

/// <summary>
/// Decoded 8-bit Galileo OSNMA NMA Header (first byte of every 120-bit HKROOT message).
/// </summary>
public sealed record OsnmaNmaHeader(
    byte RawByte,
    OsnmaNmasStatus Nmas,
    int ChainId,
    int Cpks)
{
    public static OsnmaNmaHeader Parse(byte raw)
    {
        var nmas = (OsnmaNmasStatus)((raw >> 6) & 0x03);
        int cid = (raw >> 4) & 0x03;
        int cpks = (raw >> 1) & 0x07;
        return new OsnmaNmaHeader(raw, nmas, cid, cpks);
    }

    public string Summary => $"{Nmas.ToString().ToUpperInvariant()} (CID={ChainId}, CPKS={Cpks})";
}

/// <summary>
/// Source bit layout used when decoding a Galileo E1-B I/NAV nominal page.
/// </summary>
public enum GalileoINavLayout
{
    UbxSfrbx228Bit,
    FullNominal240Bit
}

/// <summary>
/// Bit-exact decoded Galileo E1-B I/NAV nominal page per Galileo OS SIS ICD v2.1.
/// </summary>
public sealed record ParsedGalileoINavPage(
    int Svid,
    int WordType,
    byte[] Data128Bytes,
    byte HkrootByte,
    byte[] MackBytes,
    byte[] Osnma40Bytes,
    uint Crc24qReceived,
    uint Crc24qComputed,
    bool CrcValid,
    uint SarBits,
    byte SspByte,
    GstTime? DecodedGst,
    int? DecodedTowOnly,
    GalileoINavLayout Layout)
{
    public string HkrootHex => HkrootByte.ToString("X2", CultureInfo.InvariantCulture);
    public string MackHex => Convert.ToHexString(MackBytes);
    public string Osnma40Hex => Convert.ToHexString(Osnma40Bytes);
    public bool HasNonZeroOsnma => Osnma40Bytes.Any(b => b != 0);
}

/// <summary>
/// Result of comparing independent network GST against receiver-reported GST (anti-time-spoofing guard).
/// </summary>
public sealed record GstClockStatus(
    GstTime NetworkGst,
    GstTime? ReceiverGst,
    double? DriftSeconds,
    string ClockSource,
    bool IsNetworkSynced,
    bool IsTimeTrusted,
    bool IsTimeSpoofingSuspected,
    DateTimeOffset LastSyncedUtc,
    string Summary);

/// <summary>
/// Assisted or XML-loaded OSNMA public cryptographic bootstrap material (100% open EUSPA standard fields).
/// </summary>
public sealed record OsnmaAssistMaterial(
    string? MerkleRootHex = null,
    int? PublicKeyId = null,
    string? PublicKeyType = null,
    string? PublicKeyCompressedHex = null,
    string? DsmKrootHex = null,
    string? KrootHex = null,
    string? AlphaHex = null,
    int? WnKroot = null,
    int? Tow0Kroot = null,
    int KeySizeBits = 128,
    int TagSizeBits = 40,
    int MacltId = 34,
    byte? NmaHeaderByte = null,
    string Source = "Local");

/// <summary>
/// Result of a 40-bit OSNMA bitwise cross-check against a trusted TRISAT-Assist reference station.
/// </summary>
public sealed record BitwiseCheckResult(
    int Svid,
    long GstSeconds,
    bool HasReference,
    bool Matched,
    int MatchingBits,
    int TotalBits,
    string LocalOsnmaHex,
    string? ReferenceOsnmaHex,
    string Reason);

/// <summary>
/// Aggregate statistics for real-time Bitwise cross-check validation.
/// </summary>
public sealed record BitwiseSessionStats(
    int TotalChecked,
    int MatchedCount,
    int MismatchCount,
    int NoReferenceCount,
    BitwiseCheckResult? LastResult)
{
    public static BitwiseSessionStats Empty { get; } = new(0, 0, 0, 0, null);
    public bool HasMismatch => MismatchCount > 0;
}

/// <summary>
/// Per-satellite OSNMA live status across Galileo E01..E36.
/// </summary>
public sealed record OsnmaSatelliteStatus(
    int Svid,
    bool HasOsnmaSignal,
    int AuthenticatedTags,
    int FailedTags,
    int BitwiseMatches,
    int BitwiseMismatches,
    string? LastHkrootHex,
    string? LastMackHex,
    long? LastGstSeconds,
    DateTimeOffset LastSeenUtc)
{
    public string FormattedSvid => $"E{Svid:D2}";
}

/// <summary>
/// Snapshot of the live OSNMA verifier session (strictly separated from the static Annex A self-test).
/// </summary>
public sealed record OsnmaLiveSummary(
    OsnmaReceptionStatus Status,
    OsnmaNmaHeader? NmaHeader,
    bool HasTrustedPublicKey,
    bool HasVerifiedKroot,
    bool IsAssistedBootstrap,
    string? ActiveKrootHex,
    string? LatestTeslaKeyHex,
    int AssembledSubframes,
    int VerifiedTeslaKeys,
    int VerifiedTags,
    int FailedTags,
    int ActiveOsnmaSatellites,
    IReadOnlyList<int> AuthenticatedSvids,
    IReadOnlyList<OsnmaSatelliteStatus> SatelliteStatuses,
    string DetailText);
