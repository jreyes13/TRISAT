namespace Trisat.Core.Models;

/// <summary>
/// Represents a captured Galileo I/NAV (or E6 HAS / E5) navigation subframe payload.
/// </summary>
public sealed record GalileoINavMessage(
    Guid Id,
    int Svid,
    int SubmessageId,
    byte[] Data,
    int Status,
    DateTimeOffset Timestamp,
    string Band = "GAL_E1",
    string? OsnmaHkrootHex = null,
    string? OsnmaMackHex = null,
    bool IsSimulated = false,
    bool CrcValid = false,
    int? GstWeek = null,
    int? GstTow = null,
    string? AuthBadge = null)
{
    public string HexPayload => Convert.ToHexString(Data);

    public string FormattedSvid => $"Galileo E{Svid:D2}";

    public bool HasOsnmaField => !string.IsNullOrEmpty(OsnmaHkrootHex) && !string.IsNullOrEmpty(OsnmaMackHex);

    public string OsnmaFieldSummary => HasOsnmaField
        ? $"OSNMA HK:{OsnmaHkrootHex} MACK:{OsnmaMackHex}" + (GstTow.HasValue ? $" · TOW {GstTow.Value}s" : string.Empty) + (CrcValid ? " · CRC OK" : string.Empty)
        : (CrcValid ? "I/NAV CRC-24Q OK" : string.Empty);

    public string WordTypeDescription => SubmessageId switch
    {
        0 => "WT 0 · Spare / Time",
        1 => "WT 1 · Ephemeris 1/4",
        2 => "WT 2 · Ephemeris 2/4",
        3 => "WT 3 · Ephemeris 3/4 & SISA",
        4 => "WT 4 · Ephemeris 4/4 & Clock",
        5 => "WT 5 · Ionosphere, BGD & Health",
        6 => "WT 6 · GST-UTC Conversion",
        7 => "WT 7 · Almanac 1/4",
        8 => "WT 8 · Almanac 2/4",
        9 => "WT 9 · Almanac 3/4",
        10 => "WT 10 · Almanac 4/4 & GGTO",
        16 => "WT 16 · Reduced CED (I/NAV v2.2)",
        17 => "WT 17 · FEC2 Reed-Solomon Parity",
        18 => "WT 18 · FEC2 Reed-Solomon Parity",
        19 => "WT 19 · FEC2 Reed-Solomon Parity",
        20 => "WT 20 · FEC2 Reed-Solomon Parity",
        22 => "WT 22 · Emergency Warning (EWSS)",
        44 => "WT 44 · Timing & Service Mgmt",
        _ => $"WT {SubmessageId} · Galileo I/NAV"
    };

    public string ProvenanceBadge => !string.IsNullOrWhiteSpace(AuthBadge)
        ? AuthBadge
        : (IsSimulated ? "SIMULATED" : (CrcValid ? "CRC OK · UNVERIFIED" : "UNVERIFIED RX"));

    public static byte[]? DecodeHexPayload(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return null;

        var clean = hex.Trim();
        if (clean.Length == 0 || (clean.Length & 1) != 0)
            return null;

        try
        {
            return Convert.FromHexString(clean);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
