using System.Globalization;
using System.Text.Json;
using Trisat.Core.Models;

namespace Trisat.Core.Engines;

/// <summary>
/// Downloads live GNSS Two-Line Elements (Galileo, GPS, GLONASS, BeiDou) from CelesTrak
/// and caches them locally in JSON under %LOCALAPPDATA%\TRISAT.
/// </summary>
public sealed class TleService
{
    private static readonly IReadOnlyDictionary<string, string> CelestrakUrls = new Dictionary<string, string>
    {
        ["GALILEO"] = "https://celestrak.org/NORAD/elements/gp.php?GROUP=galileo&FORMAT=tle",
        ["GPS"] = "https://celestrak.org/NORAD/elements/gp.php?GROUP=gps-ops&FORMAT=tle",
        ["GLONASS"] = "https://celestrak.org/NORAD/elements/gp.php?GROUP=glo-ops&FORMAT=tle",
        ["BEIDOU"] = "https://celestrak.org/NORAD/elements/gp.php?GROUP=beidou&FORMAT=tle"
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly string _cacheFilePath;

    public TleService(HttpClient? httpClient = null, string? cacheFilePath = null)
    {
        _httpClient = httpClient ?? CreateDefaultHttpClient();
        _cacheFilePath = cacheFilePath ?? GetDefaultCachePath();
    }

    private static HttpClient CreateDefaultHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("TRISAT-Windows/2.4");
        return client;
    }

    public static string GetDefaultCachePath()
    {
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(baseDir))
            baseDir = Path.GetTempPath();
        return Path.Combine(baseDir, "TRISAT", "tle_cache.json");
    }

    public TleCacheSnapshot? LoadCached()
    {
        try
        {
            if (!File.Exists(_cacheFilePath))
                return null;
            var json = File.ReadAllText(_cacheFilePath);
            return JsonSerializer.Deserialize<TleCacheSnapshot>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public void SaveCache(TleCacheSnapshot snapshot)
    {
        try
        {
            var dir = Path.GetDirectoryName(_cacheFilePath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(snapshot, JsonOptions);
            File.WriteAllText(_cacheFilePath, json);
        }
        catch
        {
            // Cache persistence is best-effort
        }
    }

    public async Task<TleCacheSnapshot?> FetchAllOrbitsAsync(CancellationToken ct = default)
    {
        var all = new List<TleData>();

        foreach (var (constellation, url) in CelestrakUrls)
        {
            try
            {
                var raw = await _httpClient.GetStringAsync(url, ct).ConfigureAwait(false);
                all.AddRange(ParseTleText(raw, constellation));
            }
            catch
            {
                // Continue fetching remaining constellations if one endpoint fails
            }
        }

        if (all.Count == 0)
            return null;

        var snapshot = new TleCacheSnapshot(DateTimeOffset.UtcNow, all);
        SaveCache(snapshot);
        return snapshot;
    }

    public static IReadOnlyList<TleData> ParseTleText(string rawText, string constellation)
    {
        var results = new List<TleData>();
        var lines = rawText
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();

        int i = 0;
        while (i + 2 < lines.Count)
        {
            var name = lines[i];
            var l1 = lines[i + 1];
            var l2 = lines[i + 2];

            if (l1.StartsWith("1 ", StringComparison.Ordinal) && l2.StartsWith("2 ", StringComparison.Ordinal))
            {
                string svidStr = l1.Length >= 7 ? l1.Substring(2, 5).Trim() : i.ToString(CultureInfo.InvariantCulture);
                double inc = ParseSlice(l2, 8, 8, 55.0);
                double raan = ParseSlice(l2, 17, 8, 0.0);
                double ecc = ParseEccentricity(l2);
                double argPerigee = ParseSlice(l2, 34, 8, 0.0);
                double meanAnomaly = ParseSlice(l2, 43, 8, 0.0);
                double meanMotion = ParseSlice(l2, 52, 11, 1.7);

                results.Add(new TleData(
                    Name: name,
                    Svid: svidStr,
                    Constellation: constellation.ToUpperInvariant(),
                    Line1: l1,
                    Line2: l2,
                    InclinationDegrees: inc,
                    RaanDegrees: raan,
                    Eccentricity: ecc,
                    ArgPerigeeDegrees: argPerigee,
                    MeanAnomalyDegrees: meanAnomaly,
                    MeanMotionRevsPerDay: meanMotion));

                i += 3;
            }
            else
            {
                i++;
            }
        }

        return results;
    }

    private static double ParseSlice(string line, int start, int length, double fallback)
    {
        if (line.Length < start + length)
            return fallback;
        var slice = line.Substring(start, length).Trim();
        return double.TryParse(slice, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
    }

    private static double ParseEccentricity(string line2)
    {
        if (line2.Length < 33)
            return 0.001;
        var slice = "0." + line2.Substring(26, 7).Trim();
        return double.TryParse(slice, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0.001;
    }
}
