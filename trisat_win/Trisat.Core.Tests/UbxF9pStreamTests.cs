using System.Text;
using Trisat.Core.Parsers;
using Xunit;

namespace Trisat.Core.Tests;

public sealed class UbxF9pStreamTests
{
    [Fact]
    public void Feed_MixedFragmentedNmeaAndUbx_FramesBothCleanly()
    {
        var stream = new UbxF9pStream();
        var nmea = Encoding.ASCII.GetBytes("$GNGGA,120000.00,4138.2680,N,00352.1340,W,1,08,1.0,837.0,M,48.0,M,,*63\r\n");

        // Build a valid UBX-RXM-SFRBX Galileo packet (gnssId=2, svId=11, numWords=8)
        var sfrbxPayload = new byte[8 + 32];
        sfrbxPayload[0] = 2;  // Galileo
        sfrbxPayload[1] = 11; // SVID E11
        sfrbxPayload[4] = 8;  // 8 x 32-bit words
        // Word 0: wordType = 4 encoded in bits 2..7 => 4 << 2 = 0x10
        sfrbxPayload[8] = 0x10;
        sfrbxPayload[9] = 0xAB;
        sfrbxPayload[10] = 0xCD;
        sfrbxPayload[11] = 0xEF;

        var ubx = UbxF9pStream.BuildUbxMessage(0x02, 0x13, sfrbxPayload);
        var combined = new byte[] { 0x00, 0xFF }.Concat(nmea).Concat(ubx).ToArray();

        // Feed byte-by-byte to stress fragmentation handling
        var received = new List<FramedPacket>();
        foreach (byte b in combined)
            received.AddRange(stream.Feed([b]));

        Assert.Equal(2, received.Count);
        Assert.Equal(FramedPacketKind.Nmea, received[0].Kind);
        Assert.StartsWith("$GNGGA,", received[0].AsAsciiText());

        Assert.Equal(FramedPacketKind.Ubx, received[1].Kind);
        var decoded = UbxF9pStream.TryDecodeGalileoSfrbx(received[1].RawBytes);
        Assert.NotNull(decoded);
        Assert.Equal(11, decoded.Value.Message.Svid);
        Assert.Equal(4, decoded.Value.Message.SubmessageId);
        Assert.Equal(32, decoded.Value.Message.Data.Length);
        Assert.StartsWith("$GALINAV,11,4,", decoded.Value.GalinavSentence);
        Assert.True(NmeaParser.VerifyChecksum(decoded.Value.GalinavSentence));
    }

    [Fact]
    public void BuildConfigurationSequences_ProduceValidUbxChecksums()
    {
        var initSeq = UbxF9pStream.BuildEnableNavigationSequence();
        var resetSeq = UbxF9pStream.BuildFactoryResetSequence();

        Assert.True(initSeq.Count >= 7);
        Assert.True(resetSeq.Count >= 11);

        foreach (var msg in initSeq.Concat(resetSeq))
        {
            Assert.Equal(0xB5, msg[0]);
            Assert.Equal(0x62, msg[1]);
            var (ckA, ckB) = UbxF9pStream.CalculateChecksum(msg.AsSpan(2, msg.Length - 4));
            Assert.Equal(ckA, msg[^2]);
            Assert.Equal(ckB, msg[^1]);
        }
    }
}
