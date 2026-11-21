using System.Net;
using System.Text;
using System.Text.Json;
using Trisat.Core.Engines;
using Trisat.Core.Models;

namespace Trisat.Core.Services;

/// <summary>
/// Lightweight, open-source, zero-database HTTP JSON server for TRISAT-Assist (Reference Station & Assisted OSNMA).
/// Exposes:
///   GET  /api/osnma/status
///   GET  /api/osnma/gst
///   GET  /api/osnma/assist
///   GET  /api/osnma/reference[?gst={totalSeconds}]
///   GET  /api/osnma/satellites
///   POST /api/osnma/bitwise-check
///   POST /api/osnma/ingest
/// </summary>
public sealed class TrisatAssistServer : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private readonly TrisatAssistStore _store;
    private readonly GstClockService _clock;
    private readonly OsnmaLiveVerifier _verifier;
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;

    public TrisatAssistServer(
        TrisatAssistStore store,
        GstClockService clock,
        OsnmaLiveVerifier verifier)
    {
        _store = store;
        _clock = clock;
        _verifier = verifier;
    }

    public bool IsRunning { get; private set; }
    public int Port { get; private set; }
    public string BaseUrl { get; private set; } = string.Empty;

    public event Action<string>? StatusChanged;
    public event Action<string>? LogMessage;

    public void Start(int port = 8095, string host = "127.0.0.1")
    {
        Stop();

        var prefix = $"http://{host}:{port}/";
        var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();

        _listener = listener;
        Port = port;
        BaseUrl = prefix.TrimEnd('/');
        IsRunning = true;
        _cts = new CancellationTokenSource();
        _listenTask = Task.Run(() => AcceptLoopAsync(listener, _cts.Token));

        StatusChanged?.Invoke($"Assisted OSNMA Serving ({BaseUrl})");
        LogMessage?.Invoke($"[Assisted OSNMA] Reference & Assist HTTP API started on {BaseUrl}");
    }

    public void Stop()
    {
        if (!IsRunning && _listener is null)
            return;

        IsRunning = false;
        try { _cts?.Cancel(); } catch { }
        try { _listener?.Stop(); } catch { }
        try { _listener?.Close(); } catch { }

        _listener = null;
        _cts?.Dispose();
        _cts = null;
        _listenTask = null;

        StatusChanged?.Invoke("Stopped");
        LogMessage?.Invoke("[Assisted OSNMA] Server stopped.");
    }

    private async Task AcceptLoopAsync(HttpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && listener.IsListening)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await listener.GetContextAsync().ConfigureAwait(false);
            }
            catch
            {
                break;
            }

            _ = Task.Run(() => HandleRequestAsync(ctx), ct);
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext ctx)
    {
        var req = ctx.Request;
        var resp = ctx.Response;
        resp.AddHeader("Access-Control-Allow-Origin", "*");
        resp.AddHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
        resp.AddHeader("Access-Control-Allow-Headers", "Content-Type");

        if (req.HttpMethod.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
        {
            resp.StatusCode = 204;
            resp.Close();
            return;
        }

        try
        {
            var path = (req.Url?.AbsolutePath ?? "/").TrimEnd('/').ToLowerInvariant();
            if (string.IsNullOrEmpty(path)) path = "/";

            if (req.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase))
            {
                switch (path)
                {
                    case "/":
                    case "/api/osnma/status":
                    {
                        var clockStatus = _clock.GetStatus();
                        var summary = _verifier.GetSummary(hasAnyInavMessages: _store.EpochCount > 0);
                        var latestEpoch = _store.GetEpoch();
                        await WriteJsonAsync(resp, 200, new
                        {
                            service = "TRISAT Assisted OSNMA",
                            openSource = true,
                            gst = clockStatus.NetworkGst,
                            gstTotalSeconds = clockStatus.NetworkGst.TotalSeconds,
                            clockSource = clockStatus.ClockSource,
                            driftSeconds = clockStatus.DriftSeconds,
                            osnmaStatus = summary.Status.ToString(),
                            nmaHeader = summary.NmaHeader,
                            hasTrustedPublicKey = summary.HasTrustedPublicKey,
                            hasVerifiedKroot = summary.HasVerifiedKroot,
                            cachedEpochs = _store.EpochCount,
                            retentionSeconds = _store.RetentionSeconds,
                            latestEpochSatellites = latestEpoch?.SatelliteOsnmaHex.Count ?? 0
                        }).ConfigureAwait(false);
                        return;
                    }

                    case "/api/osnma/gst":
                    {
                        var clockStatus = _clock.GetStatus();
                        await WriteJsonAsync(resp, 200, new
                        {
                            weekNumber = clockStatus.NetworkGst.WeekNumber,
                            towSeconds = clockStatus.NetworkGst.TowSeconds,
                            totalSeconds = clockStatus.NetworkGst.TotalSeconds,
                            utcIso = _clock.GetCurrentNetworkUtc().ToString("O"),
                            clockSource = clockStatus.ClockSource,
                            isNetworkSynced = clockStatus.IsNetworkSynced
                        }).ConfigureAwait(false);
                        return;
                    }

                    case "/api/osnma/assist":
                    {
                        // Merge live verifier's verified material with store's cached material
                        var liveMat = _verifier.ExportAssistMaterial();
                        _store.UpdateAssistMaterial(liveMat);
                        var mat = _store.GetAssistMaterial();
                        await WriteJsonAsync(resp, 200, mat).ConfigureAwait(false);
                        return;
                    }

                    case "/api/osnma/reference":
                    {
                        long? requestedGst = null;
                        var gstParam = req.QueryString["gst"];
                        if (!string.IsNullOrWhiteSpace(gstParam) && long.TryParse(gstParam, out long parsedGst))
                            requestedGst = parsedGst;

                        var epoch = _store.GetEpoch(requestedGst);
                        if (epoch is null)
                        {
                            await WriteJsonAsync(resp, 404, new { error = "No reference OSNMA epoch available." }).ConfigureAwait(false);
                            return;
                        }

                        await WriteJsonAsync(resp, 200, epoch).ConfigureAwait(false);
                        return;
                    }

                    case "/api/osnma/satellites":
                    {
                        var summary = _verifier.GetSummary(hasAnyInavMessages: _store.EpochCount > 0);
                        await WriteJsonAsync(resp, 200, new
                        {
                            nmaHeader = summary.NmaHeader,
                            activeOsnmaSatellites = summary.ActiveOsnmaSatellites,
                            authenticatedSvids = summary.AuthenticatedSvids,
                            satellites = summary.SatelliteStatuses
                        }).ConfigureAwait(false);
                        return;
                    }
                }
            }
            else if (req.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase))
            {
                using var reader = new StreamReader(req.InputStream, Encoding.UTF8);
                var body = await reader.ReadToEndAsync().ConfigureAwait(false);

                if (path == "/api/osnma/bitwise-check")
                {
                    var payload = JsonSerializer.Deserialize<BitwiseCheckRequest>(body, JsonOptions);
                    if (payload is null)
                    {
                        await WriteJsonAsync(resp, 400, new { error = "Invalid JSON payload." }).ConfigureAwait(false);
                        return;
                    }

                    var result = _store.CompareBitwise(payload.Svid, payload.GstSeconds, payload.OsnmaHex ?? string.Empty);
                    await WriteJsonAsync(resp, 200, result).ConfigureAwait(false);
                    return;
                }

                if (path == "/api/osnma/ingest")
                {
                    var payload = JsonSerializer.Deserialize<ReferenceIngestRequest>(body, JsonOptions);
                    if (payload is null || payload.Svid < 1 || payload.Svid > 36 || string.IsNullOrWhiteSpace(payload.OsnmaHex))
                    {
                        await WriteJsonAsync(resp, 400, new { error = "Expected { svid, gstSeconds, osnmaHex }." }).ConfigureAwait(false);
                        return;
                    }

                    var gst = payload.GstSeconds > 0
                        ? GstTime.FromTotalSeconds(payload.GstSeconds)
                        : _clock.GetCurrentNetworkGst();
                    _store.RecordReferencePage(payload.Svid, gst, payload.OsnmaHex, payload.NmaHeaderByte);
                    await WriteJsonAsync(resp, 200, new { accepted = true, svid = payload.Svid, gstSeconds = gst.TotalSeconds }).ConfigureAwait(false);
                    return;
                }
            }

            await WriteJsonAsync(resp, 404, new { error = "Endpoint not found." }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            try
            {
                await WriteJsonAsync(resp, 500, new { error = ex.Message }).ConfigureAwait(false);
            }
            catch
            {
                // Ignore client disconnect
            }
        }
    }

    private static async Task WriteJsonAsync<T>(HttpListenerResponse resp, int statusCode, T payload)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        resp.StatusCode = statusCode;
        resp.ContentType = "application/json; charset=utf-8";
        resp.ContentLength64 = bytes.Length;
        await resp.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
        resp.Close();
    }

    public void Dispose() => Stop();

    public sealed record BitwiseCheckRequest(int Svid, long GstSeconds, string? OsnmaHex);
    public sealed record ReferenceIngestRequest(int Svid, long GstSeconds, string? OsnmaHex, byte? NmaHeaderByte = null);
}
