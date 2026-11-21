using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using Trisat.Core.Models;

namespace Trisat.Core.Services;

/// <summary>
/// Independent Network-Time to Galileo System Time (GST) synchronization & anti-time-spoofing guard.
/// Implements RFC 4330 SNTP UDP query (with HTTPS Date header & local fallback) to verify the
/// OSNMA Receiver Guidelines loose time-synchronization constraint before accepting TESLA keys.
/// </summary>
public sealed class GstClockService
{
    public const double StrictSpoofingDriftThresholdSeconds = 5.0;
    public const double OsnmaMaxTeslaSyncOffsetSeconds = 30.0;

    private static readonly string[] DefaultNtpHosts =
    [
        "time.cloudflare.com",
        "time.google.com",
        "pool.ntp.org"
    ];

    private readonly object _sync = new();
    private readonly int _leapSeconds;
    private double _networkClockOffsetSeconds;
    private bool _isNetworkSynced;
    private string _clockSource = "SystemUTC (Unsynced)";
    private DateTimeOffset _lastSyncedUtc = DateTimeOffset.MinValue;
    private GstTime? _lastReceiverGst;
    private DateTimeOffset _lastReceiverGstCapturedUtc = DateTimeOffset.MinValue;

    public GstClockService(int leapSeconds = GstTime.DefaultLeapSeconds)
    {
        _leapSeconds = leapSeconds;
    }

    /// <summary>
    /// Current best estimate of independent UTC time (local monotonic clock adjusted by last NTP/HTTPS offset).
    /// </summary>
    public DateTimeOffset GetCurrentNetworkUtc()
    {
        lock (_sync)
            return DateTimeOffset.UtcNow.AddSeconds(_networkClockOffsetSeconds);
    }

    /// <summary>
    /// Current best estimate of independent Galileo System Time (GST).
    /// </summary>
    public GstTime GetCurrentNetworkGst()
        => GstTime.FromUtc(GetCurrentNetworkUtc(), _leapSeconds);

    /// <summary>
    /// Records an independently obtained network UTC timestamp (e.g., from NTP, HTTPS, or TRISAT-Assist /api/osnma/gst).
    /// </summary>
    public void ApplyExternalNetworkUtc(DateTimeOffset networkUtc, string source)
    {
        var now = DateTimeOffset.UtcNow;
        lock (_sync)
        {
            _networkClockOffsetSeconds = (networkUtc.ToUniversalTime() - now).TotalSeconds;
            _isNetworkSynced = true;
            _clockSource = source;
            _lastSyncedUtc = now;
        }
    }

    /// <summary>
    /// Records the latest GST decoded from the GNSS receiver (I/NAV WT0/WT5 or receiver time message).
    /// </summary>
    public void ReportReceiverGst(GstTime receiverGst, DateTimeOffset? capturedAtUtc = null)
    {
        lock (_sync)
        {
            _lastReceiverGst = receiverGst;
            _lastReceiverGstCapturedUtc = capturedAtUtc ?? DateTimeOffset.UtcNow;
        }
    }

    /// <summary>
    /// Synchronizes independent network time via SNTP (RFC 4330 UDP :123), falling back cleanly if offline.
    /// </summary>
    public async Task<GstClockStatus> SyncWithNetworkAsync(
        IEnumerable<string>? ntpHosts = null,
        CancellationToken cancellationToken = default)
    {
        var hosts = ntpHosts ?? DefaultNtpHosts;
        foreach (var host in hosts)
        {
            try
            {
                var ntpUtc = await QuerySntpUtcAsync(host, TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
                if (ntpUtc.HasValue)
                {
                    ApplyExternalNetworkUtc(ntpUtc.Value, $"SNTP ({host})");
                    return GetStatus();
                }
            }
            catch
            {
                // Try next host
            }
        }

        // Fallback: HTTPS Date header from reliable public endpoint if UDP 123 is blocked by firewall
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            using var req = new HttpRequestMessage(HttpMethod.Head, "https://cloudflare.com");
            using var resp = await http.SendAsync(req, cancellationToken).ConfigureAwait(false);
            if (resp.Headers.Date.HasValue)
            {
                ApplyExternalNetworkUtc(resp.Headers.Date.Value, "HTTPS Date");
                return GetStatus();
            }
        }
        catch
        {
            // Offline: retain local system clock clearly labeled as fallback
        }

        return GetStatus();
    }

    /// <summary>
    /// Evaluates current independent GST status and drift against receiver-reported GST.
    /// </summary>
    public GstClockStatus GetStatus(DateTimeOffset? nowUtcOverride = null)
    {
        var nowUtc = nowUtcOverride ?? DateTimeOffset.UtcNow;
        lock (_sync)
        {
            var netUtc = nowUtc.AddSeconds(_networkClockOffsetSeconds);
            var netGst = GstTime.FromUtc(netUtc, _leapSeconds);

            double? drift = null;
            bool isSpoofing = false;
            bool isTrusted = true;

            if (_lastReceiverGst.HasValue)
            {
                // Advance receiver GST by elapsed wall time since it was captured so stale captures don't falsely drift
                double elapsedSinceCapture = Math.Max(0, (nowUtc - _lastReceiverGstCapturedUtc).TotalSeconds);
                double estimatedRxTotal = _lastReceiverGst.Value.TotalSeconds + elapsedSinceCapture;
                drift = estimatedRxTotal - netGst.TotalSeconds;
                double absDrift = Math.Abs(drift.Value);

                isSpoofing = absDrift > StrictSpoofingDriftThresholdSeconds;
                isTrusted = absDrift <= OsnmaMaxTeslaSyncOffsetSeconds;
            }

            string syncBadge = isSpoofing ? "[TIME-SPOOFING ALERT]" : "[SYNC OK]";
            string summary = drift.HasValue
                ? string.Create(
                    CultureInfo.InvariantCulture,
                    $"GST {netGst} | Source: {_clockSource} | Δt(RX-Net): {drift.Value:+0.0;-0.0;0.0}s {syncBadge}")
                : $"GST {netGst} | Source: {_clockSource} | Awaiting receiver GST";

            return new GstClockStatus(
                NetworkGst: netGst,
                ReceiverGst: _lastReceiverGst,
                DriftSeconds: drift,
                ClockSource: _clockSource,
                IsNetworkSynced: _isNetworkSynced,
                IsTimeTrusted: isTrusted,
                IsTimeSpoofingSuspected: isSpoofing,
                LastSyncedUtc: _lastSyncedUtc,
                Summary: summary);
        }
    }

    /// <summary>
    /// Checks whether a candidate OSNMA subframe GST is within the allowed OSNMA loose time synchronization window
    /// (OSNMA RX Guidelines: receiver must reject future/delayed TESLA keys outside the trust window).
    /// </summary>
    public bool IsSubframeTimeAcceptable(GstTime subframeGst, double maxOffsetSeconds = OsnmaMaxTeslaSyncOffsetSeconds, DateTimeOffset? nowUtcOverride = null)
    {
        var status = GetStatus(nowUtcOverride);
        double delta = Math.Abs(subframeGst.TotalSeconds - status.NetworkGst.TotalSeconds);
        return delta <= maxOffsetSeconds;
    }

    /// <summary>
    /// Performs a minimal RFC 4330 SNTPv4 client request over UDP port 123 and returns the midpoint-compensated UTC time.
    /// </summary>
    public static async Task<DateTimeOffset?> QuerySntpUtcAsync(string host, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
        var ipv4 = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
        if (ipv4 is null)
            return null;

        var request = new byte[48];
        request[0] = 0x23; // LI=0, VN=4, Mode=3 (Client)

        using var udp = new UdpClient(AddressFamily.InterNetwork);
        var ep = new IPEndPoint(ipv4, 123);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        var sw = Stopwatch.StartNew();
        await udp.SendAsync(request.AsMemory(), ep, cts.Token).ConfigureAwait(false);
        var result = await udp.ReceiveAsync(cts.Token).ConfigureAwait(false);
        sw.Stop();

        if (result.Buffer.Length < 48)
            return null;

        ulong intPart = ((ulong)result.Buffer[40] << 24)
                        | ((ulong)result.Buffer[41] << 16)
                        | ((ulong)result.Buffer[42] << 8)
                        | result.Buffer[43];
        ulong fracPart = ((ulong)result.Buffer[44] << 24)
                         | ((ulong)result.Buffer[45] << 16)
                         | ((ulong)result.Buffer[46] << 8)
                         | result.Buffer[47];

        if (intPart == 0)
            return null;

        // NTP epoch: 1900-01-01 00:00:00 UTC
        double seconds = intPart + (fracPart / 4294967296.0) + (sw.Elapsed.TotalSeconds * 0.5);
        var ntpEpoch = new DateTimeOffset(1900, 1, 1, 0, 0, 0, TimeSpan.Zero);
        return ntpEpoch.AddSeconds(seconds);
    }
}
