using System.Net.Http.Json;
using System.Text.Json;
using Trisat.Core.Models;

namespace Trisat.Core.Services;

/// <summary>
/// Open-source HTTP JSON client for consuming a TRISAT-Assist server (GST sync, Assisted OSNMA bootstrap,
/// and 2-second Bitwise anti-spoofing cross-checks).
/// </summary>
public sealed class TrisatAssistClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;

    public TrisatAssistClient(HttpClient? httpClient = null)
    {
        if (httpClient is not null)
        {
            _http = httpClient;
            _ownsHttpClient = false;
        }
        else
        {
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
            _ownsHttpClient = true;
        }
    }

    public sealed record RemoteGstResponse(
        int WeekNumber,
        int TowSeconds,
        long TotalSeconds,
        string UtcIso,
        string ClockSource,
        bool IsNetworkSynced);

    public async Task<RemoteGstResponse?> FetchGstAsync(string baseUrl, CancellationToken ct = default)
    {
        var url = $"{NormalizeBaseUrl(baseUrl)}/api/osnma/gst";
        return await _http.GetFromJsonAsync<RemoteGstResponse>(url, JsonOptions, ct).ConfigureAwait(false);
    }

    public async Task<OsnmaAssistMaterial?> FetchAssistMaterialAsync(string baseUrl, CancellationToken ct = default)
    {
        var url = $"{NormalizeBaseUrl(baseUrl)}/api/osnma/assist";
        return await _http.GetFromJsonAsync<OsnmaAssistMaterial>(url, JsonOptions, ct).ConfigureAwait(false);
    }

    public async Task<OsnmaReferenceEpoch?> FetchReferenceEpochAsync(string baseUrl, long? gstSeconds = null, CancellationToken ct = default)
    {
        var suffix = gstSeconds.HasValue && gstSeconds.Value > 0
            ? $"/api/osnma/reference?gst={gstSeconds.Value}"
            : "/api/osnma/reference";
        var url = $"{NormalizeBaseUrl(baseUrl)}{suffix}";
        using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            return null;
        return await resp.Content.ReadFromJsonAsync<OsnmaReferenceEpoch>(JsonOptions, ct).ConfigureAwait(false);
    }

    public async Task<BitwiseCheckResult?> CheckBitwiseAsync(
        string baseUrl,
        int svid,
        long gstSeconds,
        string localOsnma40Hex,
        CancellationToken ct = default)
    {
        var url = $"{NormalizeBaseUrl(baseUrl)}/api/osnma/bitwise-check";
        var payload = new TrisatAssistServer.BitwiseCheckRequest(svid, gstSeconds, localOsnma40Hex);
        using var resp = await _http.PostAsJsonAsync(url, payload, JsonOptions, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            return null;
        return await resp.Content.ReadFromJsonAsync<BitwiseCheckResult>(JsonOptions, ct).ConfigureAwait(false);
    }

    public async Task<bool> PushReferencePageAsync(
        string baseUrl,
        int svid,
        long gstSeconds,
        string osnma40Hex,
        byte? nmaHeaderByte = null,
        CancellationToken ct = default)
    {
        var url = $"{NormalizeBaseUrl(baseUrl)}/api/osnma/ingest";
        var payload = new TrisatAssistServer.ReferenceIngestRequest(svid, gstSeconds, osnma40Hex, nmaHeaderByte);
        using var resp = await _http.PostAsJsonAsync(url, payload, JsonOptions, ct).ConfigureAwait(false);
        return resp.IsSuccessStatusCode;
    }

    private static string NormalizeBaseUrl(string baseUrl)
        => baseUrl.Trim().TrimEnd('/');

    public void Dispose()
    {
        if (_ownsHttpClient)
            _http.Dispose();
    }
}
