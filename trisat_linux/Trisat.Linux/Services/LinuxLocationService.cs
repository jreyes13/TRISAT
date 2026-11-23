using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Tmds.DBus;
using Trisat.Core.Models;

namespace Trisat.Linux.Services;

[DBusInterface("org.freedesktop.GeoClue2.Manager")]
public interface IGeoClue2Manager : IDBusObject
{
    Task<ObjectPath> GetClientAsync();
}

[DBusInterface("org.freedesktop.GeoClue2.Client")]
public interface IGeoClue2Client : IDBusObject
{
    Task StartAsync();
    Task StopAsync();
    Task SetAsync(string prop, object val);
    Task<IDisposable> WatchLocationUpdatedAsync(
        Action<(ObjectPath oldLocation, ObjectPath newLocation)> handler,
        Action<Exception>? onError = null);
}

[DBusInterface("org.freedesktop.GeoClue2.Location")]
public interface IGeoClue2Location : IDBusObject
{
    Task<IDictionary<string, object>> GetAllAsync();
}

/// <summary>
/// Native Linux location provider for Mode 0 (Internal + TLE):
///   1. Connects to local gpsd (127.0.0.1:2947 JSON TPV stream) if running on Ubuntu/Linux.
///   2. Falls back to Linux GeoClue2 D-Bus (org.freedesktop.GeoClue2) system location service.
/// </summary>
public sealed class LinuxLocationService : IDisposable
{
    private const string GeoClueService = "org.freedesktop.GeoClue2";
    private static readonly ObjectPath GeoClueManagerPath = new("/org/freedesktop/GeoClue2/Manager");

    private CancellationTokenSource? _cts;
    private Connection? _bus;
    private IGeoClue2Client? _geoClient;
    private IDisposable? _geoSub;

    public event Action<GnssPosition>? PositionUpdated;
    public event Action<string>? StatusMessage;

    public async Task StartAsync()
    {
        Stop();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        // 1. Try local gpsd daemon first (standard on Linux GNSS setups)
        if (await TryStartGpsdAsync(ct).ConfigureAwait(false))
            return;

        // 2. Fall back to GeoClue2 D-Bus (standard Ubuntu Desktop Location Service)
        await TryStartGeoClue2Async(ct).ConfigureAwait(false);
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch { }
        _cts?.Dispose();
        _cts = null;

        _geoSub?.Dispose();
        _geoSub = null;

        if (_geoClient is not null)
        {
            try { _ = _geoClient.StopAsync(); } catch { }
            _geoClient = null;
        }

        try { _bus?.Dispose(); } catch { }
        _bus = null;
    }

    private async Task<bool> TryStartGpsdAsync(CancellationToken ct)
    {
        TcpClient? tcp = null;
        try
        {
            tcp = new TcpClient();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(600));
            await tcp.ConnectAsync("127.0.0.1", 2947, timeoutCts.Token).ConfigureAwait(false);

            StatusMessage?.Invoke("Connected to local gpsd (127.0.0.1:2947).");
            _ = Task.Run(() => GpsdReadLoopAsync(tcp, ct), ct);
            return true;
        }
        catch
        {
            tcp?.Dispose();
            return false;
        }
    }

    private async Task GpsdReadLoopAsync(TcpClient tcp, CancellationToken ct)
    {
        try
        {
            using (tcp)
            using (var stream = tcp.GetStream())
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                var watchCmd = Encoding.ASCII.GetBytes("?WATCH={\"enable\":true,\"json\":true}\n");
                await stream.WriteAsync(watchCmd, ct).ConfigureAwait(false);
                await stream.FlushAsync(ct).ConfigureAwait(false);

                while (!ct.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                    if (line is null) break;

                    var pos = TryParseGpsdTpv(line);
                    if (pos is not null)
                        PositionUpdated?.Invoke(pos);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on stop
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke($"gpsd stream closed: {ex.Message}");
        }
    }

    public static GnssPosition? TryParseGpsdTpv(string jsonLine)
    {
        if (string.IsNullOrWhiteSpace(jsonLine) || !jsonLine.Contains("\"TPV\"", StringComparison.Ordinal))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(jsonLine);
            var root = doc.RootElement;
            if (!root.TryGetProperty("class", out var cls) || cls.GetString() != "TPV")
                return null;

            int mode = root.TryGetProperty("mode", out var modeProp) && modeProp.TryGetInt32(out var m) ? m : 0;
            if (mode < 2)
                return null;

            if (!root.TryGetProperty("lat", out var latProp) || !latProp.TryGetDouble(out var lat))
                return null;
            if (!root.TryGetProperty("lon", out var lonProp) || !lonProp.TryGetDouble(out var lon))
                return null;

            double alt = 0.0;
            if (root.TryGetProperty("altMSL", out var altMsl) && altMsl.TryGetDouble(out var a1))
                alt = a1;
            else if (root.TryGetProperty("alt", out var altProp) && altProp.TryGetDouble(out var a2))
                alt = a2;

            double? acc = null;
            if (root.TryGetProperty("epx", out var epx) && epx.TryGetDouble(out var ex) &&
                root.TryGetProperty("epy", out var epy) && epy.TryGetDouble(out var ey))
            {
                acc = Math.Sqrt(ex * ex + ey * ey);
            }

            string fixDesc = mode >= 3 ? "Linux gpsd (3D Fix)" : "Linux gpsd (2D Fix)";
            return new GnssPosition(
                Latitude: lat,
                Longitude: lon,
                AltitudeMeters: alt,
                HorizontalAccuracyMeters: acc,
                FixDescription: fixDesc,
                IsSimulated: false);
        }
        catch
        {
            return null;
        }
    }

    private async Task TryStartGeoClue2Async(CancellationToken ct)
    {
        try
        {
            var bus = new Connection(Address.System);
            await bus.ConnectAsync().ConfigureAwait(false);
            _bus = bus;

            var mgr = bus.CreateProxy<IGeoClue2Manager>(GeoClueService, GeoClueManagerPath);
            var clientPath = await mgr.GetClientAsync().ConfigureAwait(false);
            var client = bus.CreateProxy<IGeoClue2Client>(GeoClueService, clientPath);
            _geoClient = client;

            await client.SetAsync("DesktopId", "trisat").ConfigureAwait(false);
            await client.SetAsync("RequestedAccuracyLevel", (uint)8).ConfigureAwait(false); // EXACT

            _geoSub = await client.WatchLocationUpdatedAsync(async evt =>
            {
                if (ct.IsCancellationRequested) return;
                try
                {
                    var loc = bus.CreateProxy<IGeoClue2Location>(GeoClueService, evt.newLocation);
                    var props = await loc.GetAllAsync().ConfigureAwait(false);
                    if (props.TryGetValue("Latitude", out var latObj) && latObj is double lat &&
                        props.TryGetValue("Longitude", out var lonObj) && lonObj is double lon)
                    {
                        double alt = props.TryGetValue("Altitude", out var altObj) && altObj is double a && a > -10000 ? a : 0.0;
                        double? acc = props.TryGetValue("Accuracy", out var accObj) && accObj is double ac ? ac : null;
                        PositionUpdated?.Invoke(new GnssPosition(
                            Latitude: lat,
                            Longitude: lon,
                            AltitudeMeters: alt,
                            HorizontalAccuracyMeters: acc,
                            FixDescription: "Ubuntu GeoClue2 Location",
                            IsSimulated: false));
                    }
                }
                catch
                {
                    // Ignore transient GeoClue2 read error
                }
            }).ConfigureAwait(false);

            await client.StartAsync().ConfigureAwait(false);
            StatusMessage?.Invoke("Linux GeoClue2 Location Service active.");
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke($"Linux Location (gpsd / GeoClue2) unavailable: {ex.Message}");
        }
    }

    public void Dispose() => Stop();
}
