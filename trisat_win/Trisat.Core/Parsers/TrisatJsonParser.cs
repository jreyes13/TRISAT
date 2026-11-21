using System.Text.Json;
using Trisat.Core.Models;

namespace Trisat.Core.Parsers;

public sealed record TrisatJsonTelemetry(
    string PacketType,
    bool IsSimulated,
    GnssPosition? Position = null,
    DopMetrics? Dop = null,
    IReadOnlyList<SatelliteInfo>? Satellites = null,
    GalileoINavMessage? InavMessage = null,
    string? EmbeddedNmeaLine = null,
    string? RawBinaryHex = null);

/// <summary>
/// Parses TRISAT JSON telemetry datagrams produced by TRISAT-SDR (UDP/TCP),
/// TRISAT Android/ESP32 BLE GATT bridges, and SXblue JSON broadcasters.
/// </summary>
public static class TrisatJsonParser
{
    public static TrisatJsonTelemetry? TryParse(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        var trimmed = line.Trim();
        if (!trimmed.StartsWith('{') || !trimmed.EndsWith('}'))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var typeProp))
                return null;

            var packetType = typeProp.GetString() ?? string.Empty;
            bool isSimulated = root.TryGetProperty("source", out var srcProp) &&
                               string.Equals(srcProp.GetString(), "simulated", StringComparison.OrdinalIgnoreCase);

            return packetType switch
            {
                "location" => ParseLocation(root, isSimulated),
                "sat_status" => ParseSatStatus(root, isSimulated),
                "inav_frame" or "gnss_frame" => ParseNavigationFrame(root, packetType, isSimulated),
                "nmea" => new TrisatJsonTelemetry(
                    packetType,
                    isSimulated,
                    EmbeddedNmeaLine: root.TryGetProperty("data", out var d) ? d.GetString() : null),
                "bin" => new TrisatJsonTelemetry(
                    packetType,
                    isSimulated,
                    RawBinaryHex: root.TryGetProperty("hex", out var h) ? h.GetString() : null),
                _ => null
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static TrisatJsonTelemetry? ParseLocation(JsonElement root, bool isSimulated)
    {
        if (!TryGetDouble(root, "lat", out var lat) || !TryGetDouble(root, "lon", out var lon))
            return null;

        TryGetDouble(root, "alt", out var alt);
        double? acc = TryGetDouble(root, "acc", out var accVal) ? accVal : null;
        double? hdop = TryGetDouble(root, "hdop", out var hdopVal) ? hdopVal : null;
        double? pdop = TryGetDouble(root, "pdop", out var pdopVal) ? pdopVal : null;
        var mode = root.TryGetProperty("mode", out var modeProp) ? modeProp.GetString() ?? "GNSS" : "GNSS";

        var pos = new GnssPosition(
            Latitude: lat,
            Longitude: lon,
            AltitudeMeters: alt,
            HorizontalAccuracyMeters: acc,
            FixDescription: isSimulated ? $"{mode} (SIMULATED)" : mode,
            Hdop: hdop.HasValue ? FormattableString.Invariant($"{hdop.Value:F2}") : "--",
            IsSimulated: isSimulated);

        var dop = (hdop.HasValue || pdop.HasValue)
            ? new DopMetrics(pdop, hdop, null, null)
            : null;

        return new TrisatJsonTelemetry("location", isSimulated, Position: pos, Dop: dop);
    }

    private static TrisatJsonTelemetry? ParseSatStatus(JsonElement root, bool isSimulated)
    {
        if (!root.TryGetProperty("satellites", out var satsEl) || satsEl.ValueKind != JsonValueKind.Array)
            return null;

        var sats = new List<SatelliteInfo>();
        foreach (var item in satsEl.EnumerateArray())
        {
            if (!TryGetInt(item, "svid", out var svid))
                continue;

            var constStr = item.TryGetProperty("const", out var cProp) ? cProp.GetString() ?? "GALILEO" : "GALILEO";
            var constellation = ParseConstellation(constStr);
            TryGetDouble(item, "elev", out var elev);
            TryGetDouble(item, "azim", out var azim);
            TryGetInt(item, "cno", out var cno);
            bool used = item.TryGetProperty("used", out var uProp) && uProp.ValueKind == JsonValueKind.True;
            bool osnma = item.TryGetProperty("osnma", out var oProp) && oProp.ValueKind == JsonValueKind.True;
            bool hasPpp = item.TryGetProperty("hasPpp", out var pProp) && pProp.ValueKind == JsonValueKind.True;
            var band = item.TryGetProperty("band", out var bProp) ? bProp.GetString() ?? "E1/L1" : "E1/L1";

            sats.Add(new SatelliteInfo(
                Id: $"{constellation}_{svid}",
                Svid: svid,
                Elevation: elev,
                Azimuth: azim,
                Cno: cno,
                Constellation: constellation,
                UsedInFix: used,
                HasInav: osnma || constellation == GnssConstellation.Galileo,
                Band: band,
                HasHasPpp: hasPpp));
        }

        return new TrisatJsonTelemetry("sat_status", isSimulated, Satellites: sats);
    }

    private static TrisatJsonTelemetry? ParseNavigationFrame(JsonElement root, string packetType, bool isSimulated)
    {
        if (!TryGetInt(root, "svid", out var svid))
            return null;

        TryGetInt(root, "submsgId", out var submsgId);
        var hex = root.TryGetProperty("hex", out var hexProp) ? hexProp.GetString() : null;
        var data = GalileoINavMessage.DecodeHexPayload(hex);
        if (data is null) return null;

        var band = root.TryGetProperty("band", out var bProp) ? bProp.GetString() ?? "GAL_E1" : "GAL_E1";
        var hkroot = root.TryGetProperty("osnmaHkroot", out var hkProp) ? hkProp.GetString() : null;
        var mack = root.TryGetProperty("osnmaMack", out var mProp) ? mProp.GetString() : null;

        var msg = new GalileoINavMessage(
            Id: Guid.NewGuid(),
            Svid: svid,
            SubmessageId: submsgId,
            Data: data,
            Status: 0,
            Timestamp: DateTimeOffset.UtcNow,
            Band: band,
            OsnmaHkrootHex: hkroot,
            OsnmaMackHex: mack,
            IsSimulated: isSimulated);

        return new TrisatJsonTelemetry(packetType, isSimulated, InavMessage: msg);
    }

    private static GnssConstellation ParseConstellation(string raw) => raw.Trim().ToUpperInvariant() switch
    {
        "GALILEO" => GnssConstellation.Galileo,
        "GPS" => GnssConstellation.Gps,
        "GLONASS" => GnssConstellation.Glonass,
        "BEIDOU" or "BDS" => GnssConstellation.BeiDou,
        _ => GnssConstellation.Galileo
    };

    private static bool TryGetDouble(JsonElement el, string prop, out double value)
    {
        value = 0;
        if (!el.TryGetProperty(prop, out var p)) return false;
        return p.ValueKind == JsonValueKind.Number && p.TryGetDouble(out value);
    }

    private static bool TryGetInt(JsonElement el, string prop, out int value)
    {
        value = 0;
        if (!el.TryGetProperty(prop, out var p)) return false;
        if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out value)) return true;
        if (p.ValueKind == JsonValueKind.Number && p.TryGetDouble(out var d))
        {
            value = (int)Math.Round(d);
            return true;
        }
        return false;
    }
}
