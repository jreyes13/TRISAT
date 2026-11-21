using System.Net;
using System.Net.Sockets;
using System.Text;
using Trisat.Core.Engines;
using Trisat.Core.Models;
using Trisat.Core.Services;
using Xunit;

namespace Trisat.Core.Tests;

public sealed class UdpAndThreatTests
{
    [Fact]
    public async Task UdpTelemetryService_ReceivesAsciiAndRawBinaryLoopbackPackets()
    {
        int testPort = 19091;
        using var svc = new UdpTelemetryService();

        var asciiTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var rawTcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);

        svc.LineReceived += (line, _) => asciiTcs.TrySetResult(line);
        svc.RawBinaryReceived += (bytes, _) => rawTcs.TrySetResult(bytes);

        svc.Start(testPort);
        Assert.True(svc.IsListening);

        using var sender = new UdpClient();
        var ep = new IPEndPoint(IPAddress.Loopback, testPort);

        var nmeaBytes = Encoding.UTF8.GetBytes("$GNGGA,120000.00,4138.2680,N,00352.1340,W,1,09,0.9,837.0,M,48.0,M,,*63\n");
        await sender.SendAsync(nmeaBytes, ep);

        var rawBytes = new byte[] { (byte)'R', (byte)'A', (byte)'W', (byte)':', 0xB5, 0x62, 0x02, 0x13 };
        await sender.SendAsync(rawBytes, ep);

        var receivedLine = await asciiTcs.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var receivedRaw = await rawTcs.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.StartsWith("$GNGGA,", receivedLine);
        Assert.Equal([0xB5, 0x62, 0x02, 0x13], receivedRaw);
        Assert.True(svc.PacketsReceived >= 2);
    }

    [Fact]
    public void SatelliteStore_RetainsWithinWindowAndPrunesExpired()
    {
        var store = new SatelliteStore(TimeSpan.FromSeconds(30));
        var t0 = DateTimeOffset.UtcNow;
        var sat1 = new SatelliteInfo("Galileo_2", 2, 60, 100, 44, GnssConstellation.Galileo, true, true);
        var sat2 = new SatelliteInfo("Gps_14", 14, 40, 210, 40, GnssConstellation.Gps, true, false);

        store.UpsertRange([sat1], t0);
        var mid = store.UpsertRange([sat2], t0.AddSeconds(20));
        Assert.Equal(2, mid.Count);

        var afterExpiry = store.GetActive(t0.AddSeconds(35));
        Assert.Single(afterExpiry);
        Assert.Equal(14, afterExpiry[0].Svid);
    }

    [Fact]
    public void ThreatAnalyzer_FlagsJammingAndSimulatedDrillsAccurately()
    {
        var analyzer = new ThreatAnalyzer();
        var healthySats = new List<SatelliteInfo>
        {
            new("E02", 2, 60, 90, 45, GnssConstellation.Galileo, true, true),
            new("E11", 11, 50, 180, 43, GnssConstellation.Galileo, true, true)
        };

        var nominal = analyzer.Evaluate(healthySats, Array.Empty<GalileoINavMessage>(), "±0.40 m", "0123", "2.0s", new ThreatSimulationFlags());
        Assert.Equal(5, nominal.ThreatScore);
        Assert.False(nominal.IsJammingRisk);
        Assert.True(nominal.IsDgpsActive);

        var drill = analyzer.Evaluate(healthySats, Array.Empty<GalileoINavMessage>(), "±0.40 m", "N/A", "N/A", new ThreatSimulationFlags(Jamming: true, MacTamper: true));
        Assert.True(drill.ThreatScore >= 80);
        Assert.True(drill.IsJammingRisk);
        Assert.True(drill.HasActiveSimulation);
        Assert.Contains("SIMULATED", drill.OsnmaStatusText);
    }
}
