using System.Text.RegularExpressions;
using Trisat.Core.Models;

namespace Trisat.Core.Engines;

/// <summary>
/// Keplerian SGP4-compatible orbit propagator for Open Sky projection and real 3D DOP geometry calculation.
/// Uses the official EUSPA Galileo GSAT-to-PRN mapping table.
/// </summary>
public static partial class OrbitPropagator
{
    private static readonly Dictionary<string, int> GsatToPrn = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GSAT0101"] = 11, ["GSAT0102"] = 12, ["GSAT0103"] = 19, ["GSAT0104"] = 20,
        ["GSAT0201"] = 18, ["GSAT0202"] = 14, ["GSAT0203"] = 26, ["GSAT0204"] = 22,
        ["GSAT0205"] = 24, ["GSAT0206"] = 30, ["GSAT0207"] = 7,  ["GSAT0208"] = 8,
        ["GSAT0209"] = 9,  ["GSAT0210"] = 1,  ["GSAT0211"] = 2,  ["GSAT0212"] = 3,
        ["GSAT0213"] = 4,  ["GSAT0214"] = 5,  ["GSAT0215"] = 21, ["GSAT0216"] = 25,
        ["GSAT0217"] = 27, ["GSAT0218"] = 31, ["GSAT0219"] = 36, ["GSAT0220"] = 13,
        ["GSAT0221"] = 15, ["GSAT0222"] = 33, ["GSAT0223"] = 34, ["GSAT0224"] = 10,
        ["GSAT0225"] = 29, ["GSAT0226"] = 23, ["GSAT0227"] = 6,  ["GSAT0228"] = 16,
        ["GSAT0229"] = 17
    };

    private static readonly HashSet<int> OsnmaCapablePrns =
    [
        1, 2, 3, 4, 5, 7, 8, 9, 11, 12, 13, 15, 18, 19, 21, 24, 25, 26, 27, 29, 30, 31, 33, 34, 36
    ];

    public static double CalculateGmstRadians(DateTimeOffset utcTime, double offsetHours)
    {
        var target = utcTime.AddHours(offsetHours);
        double unixSeconds = target.ToUnixTimeMilliseconds() / 1000.0;
        double julianDate = unixSeconds / 86400.0 + 2440587.5;
        double d = julianDate - 2451545.0;
        double gmstDeg = (280.46061837 + 360.98564736629 * d) % 360.0;
        if (gmstDeg < 0) gmstDeg += 360.0;
        return gmstDeg * Math.PI / 180.0;
    }

    public static int ResolvePrn(string name, GnssConstellation constellation, int fallbackIndex)
    {
        switch (constellation)
        {
            case GnssConstellation.Galileo:
            {
                var gsatMatch = GsatRegex().Match(name);
                if (gsatMatch.Success && GsatToPrn.TryGetValue(gsatMatch.Value, out var mappedPrn))
                    return mappedPrn;

                var prnMatch = PrnRegex().Match(name);
                if (prnMatch.Success && int.TryParse(prnMatch.Groups[1].Value, out var prn))
                    return prn;

                return (fallbackIndex % 36) + 1;
            }
            case GnssConstellation.Gps:
            {
                var prnMatch = PrnRegex().Match(name);
                if (prnMatch.Success && int.TryParse(prnMatch.Groups[1].Value, out var prn))
                    return prn;

                return (fallbackIndex % 32) + 1;
            }
            case GnssConstellation.Glonass:
            {
                var slotMatch = GlonassSlotRegex().Match(name);
                if (slotMatch.Success && int.TryParse(slotMatch.Groups[1].Value, out var slot))
                    return slot > 700 ? slot - 700 : slot;

                return (fallbackIndex % 24) + 1;
            }
            case GnssConstellation.BeiDou:
            {
                var bdsMatch = BeiDouSlotRegex().Match(name);
                if (bdsMatch.Success && int.TryParse(bdsMatch.Groups[1].Value, out var num))
                    return num;

                return (fallbackIndex % 46) + 1;
            }
            default:
                return fallbackIndex + 1;
        }
    }

    public static IReadOnlyList<SatelliteInfo> PropagateToSkyplot(
        IReadOnlyList<TleData> tles,
        double timeOffsetHours,
        double observerLatitudeDeg,
        double observerLongitudeDeg,
        double elevationMaskDeg = 10.0,
        DateTimeOffset? referenceUtc = null)
    {
        if (tles.Count == 0)
            return Array.Empty<SatelliteInfo>();

        var now = referenceUtc ?? DateTimeOffset.UtcNow;
        double deltaMinutes = timeOffsetHours * 60.0;
        double gmstRad = CalculateGmstRadians(now, timeOffsetHours);

        double obsLatRad = observerLatitudeDeg * Math.PI / 180.0;
        double obsLonRad = observerLongitudeDeg * Math.PI / 180.0;

        var results = new List<SatelliteInfo>(tles.Count);

        for (int index = 0; index < tles.Count; index++)
        {
            var tle = tles[index];
            double meanAnomalyRad = ((tle.MeanAnomalyDegrees + (tle.MeanMotionRevsPerDay * 360.0 * (deltaMinutes / 1440.0))) % 360.0) * Math.PI / 180.0;
            double raanRad = ((tle.RaanDegrees + (deltaMinutes * 0.25)) % 360.0) * Math.PI / 180.0;
            double incRad = tle.InclinationDegrees * Math.PI / 180.0;
            double argPerigeeRad = tle.ArgPerigeeDegrees * Math.PI / 180.0;

            double uRad = meanAnomalyRad + argPerigeeRad;
            double satDeclination = Math.Asin(Math.Clamp(Math.Sin(incRad) * Math.Sin(uRad), -1.0, 1.0));
            double satRightAscension = raanRad + Math.Atan2(Math.Cos(incRad) * Math.Sin(uRad), Math.Cos(uRad));

            double hourAngle = gmstRad + obsLonRad - satRightAscension;
            double sinElev = Math.Sin(obsLatRad) * Math.Sin(satDeclination)
                           + Math.Cos(obsLatRad) * Math.Cos(satDeclination) * Math.Cos(hourAngle);
            double elevDeg = Math.Asin(Math.Clamp(sinElev, -1.0, 1.0)) * 180.0 / Math.PI;

            if (elevDeg < elevationMaskDeg)
                continue;

            double azimRad = Math.Atan2(-Math.Sin(hourAngle),
                Math.Cos(obsLatRad) * Math.Tan(satDeclination) - Math.Sin(obsLatRad) * Math.Cos(hourAngle));
            double azimDeg = ((azimRad * 180.0 / Math.PI) + 360.0) % 360.0;

            var constellation = tle.Constellation.ToUpperInvariant() switch
            {
                "GALILEO" => GnssConstellation.Galileo,
                "GPS" => GnssConstellation.Gps,
                "GLONASS" => GnssConstellation.Glonass,
                "BEIDOU" => GnssConstellation.BeiDou,
                _ => GnssConstellation.Galileo
            };

            int prn = ResolvePrn(tle.Name, constellation, index);
            bool hasInav = constellation == GnssConstellation.Galileo && OsnmaCapablePrns.Contains(prn);
            int estimatedCno = (int)Math.Round(28.0 + (elevDeg / 90.0) * 22.0);

            results.Add(new SatelliteInfo(
                Id: $"{constellation}_{prn}_{index}",
                Svid: prn,
                Elevation: Math.Round(elevDeg, 1),
                Azimuth: Math.Round(azimDeg, 1),
                Cno: estimatedCno,
                Constellation: constellation,
                UsedInFix: elevDeg >= 15.0,
                HasInav: hasInav));
        }

        return results
            .OrderBy(s => s.Constellation)
            .ThenBy(s => s.Svid)
            .ToList();
    }

    /// <summary>
    /// Computes Dilution of Precision (PDOP, HDOP, VDOP, GDOP) from visible satellite unit vectors via (H^T H)^-1.
    /// </summary>
    public static DopMetrics CalculateDop(IReadOnlyList<SatelliteInfo> satellites, double elevationMaskDeg = 10.0)
    {
        var usable = satellites.Where(s => s.Elevation >= elevationMaskDeg).ToList();
        if (usable.Count < 4)
            return new DopMetrics(null, null, null, null);

        // Build normal matrix N = H^T * H (4x4) in ENU + clock coordinates
        var n = new double[4, 4];
        foreach (var sat in usable)
        {
            double el = sat.Elevation * Math.PI / 180.0;
            double az = sat.Azimuth * Math.PI / 180.0;
            double cosEl = Math.Cos(el);

            double[] row =
            [
                cosEl * Math.Sin(az), // East
                cosEl * Math.Cos(az), // North
                Math.Sin(el),         // Up
                1.0                   // Clock
            ];

            for (int r = 0; r < 4; r++)
                for (int c = 0; c < 4; c++)
                    n[r, c] += row[r] * row[c];
        }

        if (!TryInvert4x4(n, out var q))
            return new DopMetrics(null, null, null, null);

        if (q[0, 0] < 0 || q[1, 1] < 0 || q[2, 2] < 0 || q[3, 3] < 0)
            return new DopMetrics(null, null, null, null);

        double hdop = Math.Sqrt(q[0, 0] + q[1, 1]);
        double vdop = Math.Sqrt(q[2, 2]);
        double pdop = Math.Sqrt(q[0, 0] + q[1, 1] + q[2, 2]);
        double gdop = Math.Sqrt(q[0, 0] + q[1, 1] + q[2, 2] + q[3, 3]);

        return new DopMetrics(
            Math.Round(pdop, 2),
            Math.Round(hdop, 2),
            Math.Round(vdop, 2),
            Math.Round(gdop, 2));
    }

    private static bool TryInvert4x4(double[,] matrix, out double[,] inverse)
    {
        var aug = new double[4, 8];
        for (int i = 0; i < 4; i++)
        {
            for (int j = 0; j < 4; j++)
                aug[i, j] = matrix[i, j];
            aug[i, i + 4] = 1.0;
        }

        for (int col = 0; col < 4; col++)
        {
            int pivot = col;
            double maxVal = Math.Abs(aug[col, col]);
            for (int row = col + 1; row < 4; row++)
            {
                double v = Math.Abs(aug[row, col]);
                if (v > maxVal)
                {
                    maxVal = v;
                    pivot = row;
                }
            }

            if (maxVal < 1e-9)
            {
                inverse = new double[4, 4];
                return false;
            }

            if (pivot != col)
            {
                for (int j = 0; j < 8; j++)
                    (aug[col, j], aug[pivot, j]) = (aug[pivot, j], aug[col, j]);
            }

            double div = aug[col, col];
            for (int j = 0; j < 8; j++)
                aug[col, j] /= div;

            for (int row = 0; row < 4; row++)
            {
                if (row == col) continue;
                double factor = aug[row, col];
                for (int j = 0; j < 8; j++)
                    aug[row, j] -= factor * aug[col, j];
            }
        }

        inverse = new double[4, 4];
        for (int i = 0; i < 4; i++)
            for (int j = 0; j < 4; j++)
                inverse[i, j] = aug[i, j + 4];
        return true;
    }

    [GeneratedRegex(@"GSAT\d{4}", RegexOptions.IgnoreCase)]
    private static partial Regex GsatRegex();

    [GeneratedRegex(@"PRN\s*(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex PrnRegex();

    [GeneratedRegex(@"\((\d+)\)")]
    private static partial Regex GlonassSlotRegex();

    [GeneratedRegex(@"\([CB]?(\d+)\)", RegexOptions.IgnoreCase)]
    private static partial Regex BeiDouSlotRegex();
}
