namespace Trisat.Core.Models;

/// <summary>
/// Supported GNSS constellations in TRISAT.
/// </summary>
public enum GnssConstellation
{
    Galileo,
    Gps,
    Glonass,
    BeiDou
}

/// <summary>
/// Three mutually exclusive operating modes matching TRISAT iOS/macOS architecture.
/// </summary>
public enum ReceiverMode
{
    InternalPlusTle = 0,
    ExternalHardware = 1,
    TleSimulation = 2
}

/// <summary>
/// Tracks provenance of currently displayed telemetry so simulated and live hardware data are never conflated.
/// </summary>
public enum TelemetrySourceKind
{
    None,
    WindowsLocation,
    TlePredicted,
    SerialCom,
    BluetoothLe,
    UdpBridge,
    TcpSdr,
    Simulated
}

/// <summary>
/// Represents a single GNSS satellite in the celestial dome.
/// </summary>
public sealed record SatelliteInfo(
    string Id,
    int Svid,
    double Elevation,
    double Azimuth,
    int Cno,
    GnssConstellation Constellation,
    bool UsedInFix,
    bool HasInav,
    string Band = "E1/L1",
    bool HasHasPpp = false)
{
    public bool IsGalileo => Constellation == GnssConstellation.Galileo;

    public string FormattedLabel => Constellation switch
    {
        GnssConstellation.Galileo => $"E{Svid:D2}",
        GnssConstellation.Gps => $"G{Svid:D2}",
        GnssConstellation.Glonass => $"R{Svid:D2}",
        GnssConstellation.BeiDou => $"B{Svid:D2}",
        _ => $"S{Svid:D2}"
    };
}

/// <summary>
/// Geographic position telemetry snapshot.
/// </summary>
public sealed record GnssPosition(
    double Latitude,
    double Longitude,
    double AltitudeMeters,
    double? HorizontalAccuracyMeters = null,
    string FixDescription = "Autonomous GNSS",
    int SatellitesInFix = 0,
    string Hdop = "--",
    string DgpsAge = "N/A",
    string DgpsStationId = "N/A",
    bool IsSimulated = false)
{
    public string FormattedCoordinates
    {
        get
        {
            var latDir = Latitude >= 0 ? "N" : "S";
            var lonDir = Longitude >= 0 ? "E" : "W";
            return FormattableString.Invariant(
                $"{Math.Abs(Latitude):F6}° {latDir}, {Math.Abs(Longitude):F6}° {lonDir}");
        }
    }
}

/// <summary>
/// Dilution of Precision geometry values.
/// </summary>
public sealed record DopMetrics(
    double? Pdop,
    double? Hdop,
    double? Vdop,
    double? Gdop)
{
    public string PdopText => Pdop.HasValue ? FormattableString.Invariant($"{Pdop.Value:F2}") : "--";
    public string HdopText => Hdop.HasValue ? FormattableString.Invariant($"{Hdop.Value:F2}") : "--";
    public string VdopText => Vdop.HasValue ? FormattableString.Invariant($"{Vdop.Value:F2}") : "--";
    public string GdopText => Gdop.HasValue ? FormattableString.Invariant($"{Gdop.Value:F2}") : "--";

    public string QualityBadge
    {
        get
        {
            if (!Pdop.HasValue) return "AWAITING FIX";
            if (Pdop.Value <= 2.0) return "EXCELLENT 3D";
            if (Pdop.Value <= 4.0) return "GOOD 3D";
            if (Pdop.Value <= 6.0) return "MODERATE";
            return "DEGRADED";
        }
    }
}

/// <summary>
/// Named reference site preset for Mode 2 TLE orbital simulation.
/// </summary>
public sealed record ReferenceSitePreset(
    string Name,
    double Latitude,
    double Longitude,
    double AltitudeMeters)
{
    public static IReadOnlyList<ReferenceSitePreset> Defaults { get; } =
    [
        new("Fuentecén (Ribera del Duero, ES)", 41.6378, -3.8689, 837.0),
        new("Madrid (ES)", 40.4168, -3.7038, 667.0),
        new("Prague — EUSPA HQ (CZ)", 50.0755, 14.4378, 235.0),
        new("Noordwijk — ESA ESTEC (NL)", 52.2179, 4.4211, 5.0),
        new("Toulouse — CNES (FR)", 43.5608, 1.4810, 150.0)
    ];
}
