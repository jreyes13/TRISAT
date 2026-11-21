using System.Globalization;
using Trisat.Core.Parsers;

namespace Trisat.Core.Services;

/// <summary>
/// Built-in 1 Hz simulated NMEA + $GALINAV telemetry generator for standalone bench testing on Windows.
/// Clearly tagged as simulated data.
/// </summary>
public sealed class TestTelemetryGenerator : IDisposable
{
    private CancellationTokenSource? _cts;
    private int _tick;

    public bool IsRunning { get; private set; }
    public event Action<string>? SentenceGenerated;

    public void Start()
    {
        Stop();
        _cts = new CancellationTokenSource();
        IsRunning = true;
        _ = Task.Run(() => LoopAsync(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        IsRunning = false;
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        int[] wordTypes = [1, 2, 3, 4, 5, 6, 16];
        int[] galileoPrns = [2, 11, 19, 24, 31, 36];

        while (!ct.IsCancellationRequested)
        {
            try
            {
                _tick++;
                var utc = DateTime.UtcNow.ToString("HHmmss.00", CultureInfo.InvariantCulture);

                // Fuentecén (41°38.2680' N, 003°52.1340' W) with Fix Quality 0 (simulated provenance)
                var gga = NmeaParser.FormatSentence($"GNGGA,{utc},4138.2680,N,00352.1340,W,0,10,0.88,837.0,M,48.0,M,,");
                var gsa = NmeaParser.FormatSentence("GNGSA,A,3,02,11,19,24,31,36,03,14,22,29,,,1.38,0.88,1.06");
                var gst = NmeaParser.FormatSentence("GNGST," + utc + ",0.42,0.55,0.38,14.2,0.32,0.29,0.61");

                int jitter = (_tick % 3) - 1;
                var gaGsv1 = NmeaParser.FormatSentence(
                    $"GAGSV,2,1,06,02,62,045,{44 + jitter},11,48,132,{42 - jitter},19,35,220,{39 + jitter},24,74,310,{47 + jitter}");
                var gaGsv2 = NmeaParser.FormatSentence(
                    $"GAGSV,2,2,06,31,28,175,{37 - jitter},36,53,285,{43 + jitter}");
                var gpGsv = NmeaParser.FormatSentence(
                    $"GPGSV,1,1,04,03,51,078,{41 + jitter},14,39,192,{38 - jitter},22,67,330,{45 + jitter},29,24,115,{34 + jitter}");

                SentenceGenerated?.Invoke(gga);
                SentenceGenerated?.Invoke(gsa);
                SentenceGenerated?.Invoke(gst);
                SentenceGenerated?.Invoke(gaGsv1);
                SentenceGenerated?.Invoke(gaGsv2);
                SentenceGenerated?.Invoke(gpGsv);

                int svid = galileoPrns[_tick % galileoPrns.Length];
                int wt = wordTypes[_tick % wordTypes.Length];
                string payloadHex = $"0{(wt & 0xF):X}8210492204E060610BDF26D77B5BF8C9CBFCF70422081475FD445DF0{_tick % 256:X2}";
                var galinav = NmeaParser.FormatSentence($"GALINAV,{svid},{wt},{payloadHex},827BB238C8");
                SentenceGenerated?.Invoke(galinav);

                await timer.WaitForNextTickAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public void Dispose() => Stop();
}
