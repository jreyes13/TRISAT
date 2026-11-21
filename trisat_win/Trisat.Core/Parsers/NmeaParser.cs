using System.Globalization;
using Trisat.Core.Models;

namespace Trisat.Core.Parsers;

public sealed record ParsedGst(string RmsError, string LatLonError);

public sealed record ParsedGsa(double? Pdop, double? Hdop, double? Vdop);

/// <summary>
/// Full NMEA 0183 + TRISAT custom navigation sentence ($GALINAV, $RAWNAV, $GALE6HAS, $GALE5) parser.
/// </summary>
public static class NmeaParser
{
    public static bool VerifyChecksum(string sentence)
    {
        if (string.IsNullOrWhiteSpace(sentence)) return false;
        var s = sentence.Trim();
        if (!s.StartsWith('$')) return false;
        var starIdx = s.IndexOf('*');
        if (starIdx < 1 || starIdx + 3 > s.Length) return false;

        byte computed = 0;
        for (int i = 1; i < starIdx; i++)
            computed ^= (byte)s[i];

        var providedHex = s.Substring(starIdx + 1, 2);
        return byte.TryParse(providedHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var expected)
               && computed == expected;
    }

    public static string ComputeChecksum(string bodyWithoutDollarOrStar)
    {
        byte cs = 0;
        foreach (char c in bodyWithoutDollarOrStar)
            cs ^= (byte)c;
        return cs.ToString("X2", CultureInfo.InvariantCulture);
    }

    public static string FormatSentence(string bodyWithoutDollarOrStar)
        => $"${bodyWithoutDollarOrStar}*{ComputeChecksum(bodyWithoutDollarOrStar)}";

    public static double? ParseNmeaCoordinate(string rawVal, string hemisphere)
    {
        if (!double.TryParse(rawVal, NumberStyles.Float, CultureInfo.InvariantCulture, out var num) || num <= 0)
            return null;

        var degrees = Math.Floor(num / 100.0);
        var minutes = num - (degrees * 100.0);
        var decimalDeg = degrees + (minutes / 60.0);

        if (hemisphere.Equals("S", StringComparison.OrdinalIgnoreCase) ||
            hemisphere.Equals("W", StringComparison.OrdinalIgnoreCase))
        {
            decimalDeg = -decimalDeg;
        }

        return decimalDeg;
    }

    public static GnssPosition? TryParseGga(string line)
    {
        if (!line.Contains("GGA,", StringComparison.OrdinalIgnoreCase))
            return null;

        var clean = StripChecksum(line);
        var parts = clean.Split(',');
        if (parts.Length < 10)
            return null;

        var lat = ParseNmeaCoordinate(parts[2], parts[3]);
        var lon = ParseNmeaCoordinate(parts[4], parts[5]);
        if (!lat.HasValue || !lon.HasValue)
            return null;

        var fixQuality = parts[6].Trim();
        int.TryParse(parts[7], NumberStyles.Integer, CultureInfo.InvariantCulture, out var numSats);
        var hdop = string.IsNullOrWhiteSpace(parts[8]) ? "--" : parts[8].Trim();
        double.TryParse(parts[9], NumberStyles.Float, CultureInfo.InvariantCulture, out var alt);

        var fixDesc = fixQuality switch
        {
            "0" => "No Fix / Simulated",
            "1" => "GPS Autonomous",
            "2" => "DGPS Differential",
            "4" => "RTK Fixed (Centimeter)",
            "5" => "RTK Float",
            "6" => "Dead Reckoning",
            _ => $"Fix Mode {fixQuality}"
        };

        var dgpsAge = "N/A";
        var dgpsStation = "N/A";
        if (parts.Length >= 15)
        {
            if (!string.IsNullOrWhiteSpace(parts[13]))
                dgpsAge = $"{parts[13].Trim()}s";
            if (!string.IsNullOrWhiteSpace(parts[14]))
                dgpsStation = parts[14].Trim();
        }
        else if (parts.Length >= 13)
        {
            if (!string.IsNullOrWhiteSpace(parts[11]))
                dgpsAge = $"{parts[11].Trim()}s";
            if (!string.IsNullOrWhiteSpace(parts[12]))
                dgpsStation = parts[12].Trim();
        }

        return new GnssPosition(
            Latitude: lat.Value,
            Longitude: lon.Value,
            AltitudeMeters: alt,
            HorizontalAccuracyMeters: null,
            FixDescription: fixDesc,
            SatellitesInFix: numSats,
            Hdop: hdop,
            DgpsAge: dgpsAge,
            DgpsStationId: dgpsStation,
            IsSimulated: fixQuality == "0");
    }

    public static ParsedGst? TryParseGst(string line)
    {
        if (!line.Contains("GST,", StringComparison.OrdinalIgnoreCase))
            return null;

        var clean = StripChecksum(line);
        var parts = clean.Split(',');
        if (parts.Length < 8)
            return null;

        var rmsText = "N/A";
        if (double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var rmsVal))
            rmsText = FormattableString.Invariant($"±{rmsVal:F2} m");

        var latLonText = "N/A";
        if (double.TryParse(parts[6], NumberStyles.Float, CultureInfo.InvariantCulture, out var latErr) &&
            double.TryParse(parts[7], NumberStyles.Float, CultureInfo.InvariantCulture, out var lonErr))
        {
            latLonText = FormattableString.Invariant($"Lat ±{latErr:F2}m, Lon ±{lonErr:F2}m");
        }

        return new ParsedGst(rmsText, latLonText);
    }

    public static ParsedGsa? TryParseGsa(string line)
    {
        if (!line.Contains("GSA,", StringComparison.OrdinalIgnoreCase))
            return null;

        var clean = StripChecksum(line);
        var parts = clean.Split(',');
        if (parts.Length < 17)
            return null;

        double? pdop = double.TryParse(parts[15], NumberStyles.Float, CultureInfo.InvariantCulture, out var p) ? p : null;
        double? hdop = double.TryParse(parts[16], NumberStyles.Float, CultureInfo.InvariantCulture, out var h) ? h : null;
        double? vdop = parts.Length >= 18 && double.TryParse(parts[17], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;

        return new ParsedGsa(pdop, hdop, vdop);
    }

    public static IReadOnlyList<SatelliteInfo> TryParseGsv(string line)
    {
        if (!line.Contains("GSV,", StringComparison.OrdinalIgnoreCase))
            return Array.Empty<SatelliteInfo>();

        var clean = StripChecksum(line);
        var parts = clean.Split(',');
        if (parts.Length < 8)
            return Array.Empty<SatelliteInfo>();

        GnssConstellation constellation;
        if (clean.StartsWith("$GA", StringComparison.OrdinalIgnoreCase))
            constellation = GnssConstellation.Galileo;
        else if (clean.StartsWith("$GP", StringComparison.OrdinalIgnoreCase))
            constellation = GnssConstellation.Gps;
        else if (clean.StartsWith("$GL", StringComparison.OrdinalIgnoreCase))
            constellation = GnssConstellation.Glonass;
        else if (clean.StartsWith("$BD", StringComparison.OrdinalIgnoreCase) ||
                 clean.StartsWith("$GB", StringComparison.OrdinalIgnoreCase))
            constellation = GnssConstellation.BeiDou;
        else
            return Array.Empty<SatelliteInfo>();

        var result = new List<SatelliteInfo>();
        for (int idx = 4; idx + 3 < parts.Length; idx += 4)
        {
            if (!int.TryParse(parts[idx], NumberStyles.Integer, CultureInfo.InvariantCulture, out var svid))
                continue;
            if (!double.TryParse(parts[idx + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var elev))
                continue;
            if (!double.TryParse(parts[idx + 2], NumberStyles.Float, CultureInfo.InvariantCulture, out var azim))
                continue;

            int.TryParse(parts[idx + 3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var cno);

            var key = $"{constellation}_{svid}";
            result.Add(new SatelliteInfo(
                Id: key,
                Svid: svid,
                Elevation: elev,
                Azimuth: azim,
                Cno: cno,
                Constellation: constellation,
                UsedInFix: cno > 20,
                HasInav: constellation == GnssConstellation.Galileo));
        }

        return result;
    }

    public static GalileoINavMessage? TryParseNavigationSentence(string line, bool isSimulated = false)
    {
        var clean = StripChecksum(line);
        var parts = clean.Split(',');

        if (clean.StartsWith("$GALINAV,", StringComparison.OrdinalIgnoreCase) ||
            clean.StartsWith("$RAWNAV,", StringComparison.OrdinalIgnoreCase))
        {
            if (parts.Length < 4) return null;
            if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var svid) || svid < 1 || svid > 36)
                return null;
            if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var submsgId))
                return null;

            var payload = GalileoINavMessage.DecodeHexPayload(parts[3]);
            if (payload is null) return null;

            string? hkroot = null;
            string? mack = null;
            if (parts.Length >= 5 && parts[4].Length >= 10)
            {
                hkroot = parts[4][..2];
                mack = parts[4][2..];
            }

            return new GalileoINavMessage(
                Id: Guid.NewGuid(),
                Svid: svid,
                SubmessageId: submsgId,
                Data: payload,
                Status: 0,
                Timestamp: DateTimeOffset.UtcNow,
                Band: "GAL_E1",
                OsnmaHkrootHex: hkroot,
                OsnmaMackHex: mack,
                IsSimulated: isSimulated);
        }

        if (clean.StartsWith("$GALE6HAS,", StringComparison.OrdinalIgnoreCase) && parts.Length >= 5)
        {
            if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var svid))
                return null;
            if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var pageId))
                return null;
            var payload = GalileoINavMessage.DecodeHexPayload(parts[4]);
            if (payload is null) return null;

            return new GalileoINavMessage(
                Id: Guid.NewGuid(),
                Svid: svid,
                SubmessageId: pageId,
                Data: payload,
                Status: 0,
                Timestamp: DateTimeOffset.UtcNow,
                Band: "GAL_E6_HAS",
                IsSimulated: isSimulated);
        }

        if (clean.StartsWith("$GALE5,", StringComparison.OrdinalIgnoreCase) && parts.Length >= 5)
        {
            var band = parts[1].Trim();
            if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var svid))
                return null;
            int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var pageType);
            var payload = GalileoINavMessage.DecodeHexPayload(parts[4]);
            if (payload is null) return null;

            return new GalileoINavMessage(
                Id: Guid.NewGuid(),
                Svid: svid,
                SubmessageId: pageType,
                Data: payload,
                Status: 0,
                Timestamp: DateTimeOffset.UtcNow,
                Band: string.IsNullOrWhiteSpace(band) ? "GAL_E5" : band,
                IsSimulated: isSimulated);
        }

        return null;
    }

    private static string StripChecksum(string sentence)
    {
        var trimmed = sentence.Trim();
        var starIdx = trimmed.IndexOf('*');
        return starIdx >= 0 ? trimmed[..starIdx] : trimmed;
    }
}
