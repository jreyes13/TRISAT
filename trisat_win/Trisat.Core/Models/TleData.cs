namespace Trisat.Core.Models;

/// <summary>
/// Parsed Two-Line Element (TLE) orbital parameters for a GNSS satellite.
/// </summary>
public sealed record TleData(
    string Name,
    string Svid,
    string Constellation,
    string Line1,
    string Line2,
    double InclinationDegrees,
    double RaanDegrees,
    double Eccentricity,
    double ArgPerigeeDegrees,
    double MeanAnomalyDegrees,
    double MeanMotionRevsPerDay);

/// <summary>
/// Persisted TLE cache container stored under %LOCALAPPDATA%\TRISAT.
/// </summary>
public sealed record TleCacheSnapshot(
    DateTimeOffset DownloadedAtUtc,
    IReadOnlyList<TleData> Elements);
