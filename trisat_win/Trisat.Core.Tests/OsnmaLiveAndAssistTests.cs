using System.Net;
using System.Net.Sockets;
using Trisat.Core.Engines;
using Trisat.Core.Models;
using Trisat.Core.Parsers;
using Trisat.Core.Services;
using Xunit;

namespace Trisat.Core.Tests;

public sealed class OsnmaLiveAndAssistTests
{
    [Fact]
    public void GalileoINavParser_Ubx228AndFull240_DecodeCrc24qOsnmaAndGstExact()
    {
        var wt5 = GalileoINavParser.BuildWordType5(wn: 1248, tow: 345660);
        byte hkroot = 0x82;
        byte[] mack = [0xDE, 0xAD, 0xBE, 0xEF];

        // 1. u-blox ZED-F9P 228-bit UBX-RXM-SFRBX layout
        var ubxWords = GalileoINavParser.BuildUbxSfrbxPayloadWords(wt5, hkroot, mack, sar22Bits: 0x155555, sspByte: 0x2B);
        var parsedUbx = GalileoINavParser.TryParseUbxSfrbxPayload(2, ubxWords);
        Assert.NotNull(parsedUbx);
        Assert.True(parsedUbx.CrcValid);
        Assert.Equal(5, parsedUbx.WordType);
        Assert.Equal("82", parsedUbx.HkrootHex);
        Assert.Equal("DEADBEEF", parsedUbx.MackHex);
        Assert.Equal("82DEADBEEF", parsedUbx.Osnma40Hex);
        Assert.Equal(new GstTime(1248, 345660), parsedUbx.DecodedGst);
        Assert.Equal((byte)0x2B, parsedUbx.SspByte);

        // Flip 1 bit -> CRC-24Q must detect corruption
        var corrupted = (byte[])ubxWords.Clone();
        corrupted[10] ^= 0x01;
        var parsedCorrupt = GalileoINavParser.TryParseUbxSfrbxPayload(2, corrupted);
        Assert.NotNull(parsedCorrupt);
        Assert.False(parsedCorrupt.CrcValid);

        // 2. Full 240-bit nominal page (Android TYPE_GAL_I / SBF layout)
        var wt0 = GalileoINavParser.BuildWordType0(wn: 1295, tow: 123456);
        var page240 = GalileoINavParser.BuildFull240BitNominalPage(wt0, 0x82, [0x11, 0x22, 0x33, 0x44]);
        var parsed240 = GalileoINavParser.TryParseFull240BitPage(11, page240);
        Assert.NotNull(parsed240);
        Assert.True(parsed240.CrcValid);
        Assert.Equal(0, parsed240.WordType);
        Assert.Equal("8211223344", parsed240.Osnma40Hex);
        Assert.Equal(new GstTime(1295, 123456), parsed240.DecodedGst);
    }

    [Fact]
    public void UbxF9pStream_DecodesRealCrcValidGalileoSfrbxWithOsnmaAndGst()
    {
        var wt5 = GalileoINavParser.BuildWordType5(wn: 1248, tow: 345660);
        var ubxWords = GalileoINavParser.BuildUbxSfrbxPayloadWords(wt5, 0x82, [0x7B, 0xB2, 0x38, 0xC8]);

        var payload = new byte[8 + 32];
        payload[0] = 2;  // Galileo
        payload[1] = 2;  // E02
        payload[4] = 8;  // 8 words
        Buffer.BlockCopy(ubxWords, 0, payload, 8, 32);

        var frame = UbxF9pStream.BuildUbxMessage(0x02, 0x13, payload);
        var decoded = UbxF9pStream.TryDecodeGalileoSfrbx(frame);

        Assert.NotNull(decoded);
        var (msg, sentence) = decoded.Value;
        Assert.True(msg.CrcValid);
        Assert.Equal(2, msg.Svid);
        Assert.Equal(5, msg.SubmessageId);
        Assert.Equal("82", msg.OsnmaHkrootHex);
        Assert.Equal("7BB238C8", msg.OsnmaMackHex);
        Assert.Equal(1248, msg.GstWeek);
        Assert.Equal(345660, msg.GstTow);
        Assert.Contains(",827BB238C8*", sentence);
        Assert.True(NmeaParser.VerifyChecksum(sentence));
    }

    [Fact]
    public void GstClockService_DetectsTimeSpoofingDriftAndEnforcesOsnmaSyncWindow()
    {
        var clock = new GstClockService(leapSeconds: 18);
        var refUtc = new DateTimeOffset(2024, 6, 12, 12, 0, 0, TimeSpan.Zero);
        clock.ApplyExternalNetworkUtc(refUtc, "SNTP (test)");

        var expectedGst = GstTime.FromUtc(refUtc, 18);
        Assert.Equal(refUtc, expectedGst.ToUtc(18));

        // Nominal sync: 1s offset -> trusted, no spoofing alert
        clock.ReportReceiverGst(GstTime.FromTotalSeconds(expectedGst.TotalSeconds + 1), DateTimeOffset.UtcNow);
        var nominal = clock.GetStatus();
        Assert.True(nominal.IsNetworkSynced);
        Assert.True(nominal.IsTimeTrusted);
        Assert.False(nominal.IsTimeSpoofingSuspected);

        // Meaconing / Time-Spoofing: 45s delayed receiver clock -> alert + untrusted for TESLA
        clock.ReportReceiverGst(GstTime.FromTotalSeconds(expectedGst.TotalSeconds - 45), DateTimeOffset.UtcNow);
        var spoofed = clock.GetStatus();
        Assert.True(spoofed.IsTimeSpoofingSuspected);
        Assert.False(spoofed.IsTimeTrusted);
        Assert.False(clock.IsSubframeTimeAcceptable(GstTime.FromTotalSeconds(expectedGst.TotalSeconds - 45)));
    }

    [Fact]
    public void EuspaOsnmaXmlLoader_ParsesOfficialStyleMerkleAndPublicKeyXml()
    {
        string merkleXml = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <signalData>
              <body>
                <MerkleTree>
                  <HashFunction>SHA-256</HashFunction>
                  <TreeNode><i>4</i><j>0</j><x_ji>{OsnmaEngine.AnnexAMerkleRoot}</x_ji></TreeNode>
                </MerkleTree>
              </body>
            </signalData>
            """;

        string pubKeyXml = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <signalData>
              <body>
                <PublicKey>
                  <PKID>2</PKID>
                  <PKType>ECDSA P-256/SHA-256</PKType>
                  <point>{OsnmaEngine.AnnexANpkCompressed}</point>
                </PublicKey>
              </body>
            </signalData>
            """;

        var merkle = EuspaOsnmaXmlLoader.ParseMerkleTreeXml(merkleXml);
        var pubKey = EuspaOsnmaXmlLoader.ParsePublicKeyXml(pubKeyXml);

        Assert.Equal(OsnmaEngine.AnnexAMerkleRoot, merkle.RootHex);
        Assert.Equal("SHA-256", merkle.HashFunction);
        Assert.Equal(2, pubKey.PublicKeyId);
        Assert.Equal(OsnmaEngine.AnnexANpkCompressed, pubKey.CompressedHex);
    }

    [Fact]
    public void OsnmaLiveVerifier_ReassemblesDsmKrootOverTheAirAndAuthenticatesTeslaAndTag()
    {
        // Build the exact 104-byte (8-block) Annex A DSM-KROOT:
        //   Byte 0: NB_DK=2 (8 blocks) | PKID=2 => 0x22
        //   Bytes 1..28: AnnexAKrootM[1..28]
        //   Bytes 29..92: AnnexAKrootDs (64-byte ECDSA P-256 signature)
        //   Bytes 93..103: AnnexAPdk (11-byte SHA-256 padding)
        byte[] dsmKroot104 = BuildAnnexADsmKroot104Bytes();

        // Sync independent clock to Annex A WN=1248, TOW=345690 (when K4 is disclosed)
        var k4Time = new GstTime(1248, 345690);
        var clock = new GstClockService();
        clock.ApplyExternalNetworkUtc(k4Time.ToUtc(), "SNTP (AnnexA)");

        var verifier = new OsnmaLiveVerifier(clock);
        verifier.ApplyAssistMaterial(new OsnmaAssistMaterial(
            MerkleRootHex: OsnmaEngine.AnnexAMerkleRoot,
            PublicKeyId: 2,
            PublicKeyCompressedHex: OsnmaEngine.AnnexANpkCompressed,
            Source: "EUSPA XML"), isAssistedSource: false);

        // Feed 8 subframes (8 x 15 = 120 CRC-valid Galileo I/NAV pages) carrying the 8 blocks of DSM-KROOT
        for (int blockIdx = 0; blockIdx < 8; blockIdx++)
        {
            int sfTow = 345420 + (blockIdx * 30);
            byte[] hkroot15 = new byte[15];
            hkroot15[0] = 0x82; // NMA Header (Operational, CID=0, CPKS=1)
            hkroot15[1] = (byte)((12 << 4) | blockIdx); // DSM_ID=12 (DSM-KROOT), DSM_BID=blockIdx
            Buffer.BlockCopy(dsmKroot104, blockIdx * 13, hkroot15, 2, 13);

            for (int pageIdx = 0; pageIdx < 15; pageIdx++)
            {
                int tow = sfTow + (pageIdx * 2);
                var wt5 = GalileoINavParser.BuildWordType5(1248, tow);
                var ubxWords = GalileoINavParser.BuildUbxSfrbxPayloadWords(wt5, hkroot15[pageIdx], [0x01, 0x02, 0x03, 0x04]);
                var parsed = GalileoINavParser.TryParseUbxSfrbxPayload(2, ubxWords)!;
                verifier.IngestPage(parsed, new GstTime(1248, tow));
            }
        }

        // DSM-KROOT must now be cryptographically verified via ECDSA P-256 + P_DK!
        var midSummary = verifier.GetSummary(hasAnyInavMessages: true);
        Assert.True(midSummary.HasVerifiedKroot);
        Assert.Equal(OsnmaEngine.AnnexAK0, midSummary.ActiveKrootHex);
        Assert.Equal(OsnmaNmasStatus.Operational, midSummary.NmaHeader?.Nmas);

        // Now verify Annex A Tag2 (ADKD=4) using TESLA Key K4 at WN=1248, TOW=345690
        var tag2Msg = Convert.FromHexString("02024E05463C03BFFFFFFFC00000449238227897FDEFF93040");
        bool tagOk = verifier.VerifyLiveTagWithTeslaKey(
            svid: 2,
            teslaKeyHex: OsnmaEngine.AnnexAK4,
            keyGst: k4Time,
            paddedMessage: tag2Msg,
            expectedTagHex: "7BB238C883");

        Assert.True(tagOk);
        var finalSummary = verifier.GetSummary(hasAnyInavMessages: true);
        Assert.Equal(OsnmaReceptionStatus.Authenticated, finalSummary.Status);
        Assert.Contains(2, finalSummary.AuthenticatedSvids);
        Assert.Equal(1, finalSummary.VerifiedTags);
        Assert.Equal(1, finalSummary.VerifiedTeslaKeys);
    }

    [Fact]
    public async Task TrisatAssist_EndToEndServerClient_AssistedBootstrapAndBitwiseAntiSpoofing()
    {
        int port = GetFreeTcpPort();
        var refClock = new GstClockService();
        var refGst = new GstTime(1248, 345690);
        refClock.ApplyExternalNetworkUtc(refGst.ToUtc(), "SNTP (RefStation)");

        var refVerifier = new OsnmaLiveVerifier(refClock);
        byte[] dsmKroot104 = BuildAnnexADsmKroot104Bytes();
        refVerifier.ApplyAssistMaterial(new OsnmaAssistMaterial(
            MerkleRootHex: OsnmaEngine.AnnexAMerkleRoot,
            PublicKeyId: 2,
            PublicKeyCompressedHex: OsnmaEngine.AnnexANpkCompressed,
            DsmKrootHex: Convert.ToHexString(dsmKroot104),
            NmaHeaderByte: 0x82,
            Source: "Reference ZED-F9P"), isAssistedSource: false);

        var store = new TrisatAssistStore(retentionSeconds: 60);
        store.RecordReferencePage(svid: 2, gst: refGst, osnma40Hex: "827BB238C8", nmaHeaderByte: 0x82);

        using var server = new TrisatAssistServer(store, refClock, refVerifier);
        server.Start(port);

        using var client = new TrisatAssistClient();
        string baseUrl = $"http://127.0.0.1:{port}";

        // 1. Client fetches independent GST from TRISAT-Assist
        var remoteGst = await client.FetchGstAsync(baseUrl);
        Assert.NotNull(remoteGst);
        Assert.True(remoteGst.TotalSeconds > 0);

        // 2. Client fetches Assisted OSNMA bootstrap bundle & verifies ECDSA P-256 KROOT immediately
        var assistMat = await client.FetchAssistMaterialAsync(baseUrl);
        Assert.NotNull(assistMat);
        Assert.Equal(OsnmaEngine.AnnexAMerkleRoot, assistMat.MerkleRootHex);

        var clientVerifier = new OsnmaLiveVerifier(refClock);
        bool bootstrapped = clientVerifier.ApplyAssistMaterial(assistMat, isAssistedSource: true);
        Assert.True(bootstrapped);
        Assert.True(clientVerifier.GetSummary(true).HasVerifiedKroot);

        // 3. Bitwise 40-bit cross-check: exact match (40/40 bits) -> BitwiseVerified
        var match = await client.CheckBitwiseAsync(baseUrl, 2, refGst.TotalSeconds, "827BB238C8");
        Assert.NotNull(match);
        Assert.True(match.HasReference);
        Assert.True(match.Matched);
        Assert.Equal(40, match.MatchingBits);

        clientVerifier.RecordBitwiseResult(match);
        var analyzer = new ThreatAnalyzer();
        var healthySats = new List<SatelliteInfo> { new("E02", 2, 60, 120, 44, GnssConstellation.Galileo, true, true) };
        var safeEval = analyzer.Evaluate(
            healthySats,
            Array.Empty<GalileoINavMessage>(),
            "±0.35 m",
            "N/A",
            "N/A",
            new ThreatSimulationFlags(),
            clientVerifier.GetSummary(true),
            refClock.GetStatus(),
            new BitwiseSessionStats(1, 1, 0, 0, match));
        Assert.Equal(OsnmaReceptionStatus.BitwiseVerified, safeEval.OsnmaStatus);
        Assert.False(safeEval.IsSpoofingRisk);

        // 4. Bitwise 40-bit cross-check with 1 spoofed bit -> instant TamperDetected + Spoofing alert!
        var tampered = await client.CheckBitwiseAsync(baseUrl, 2, refGst.TotalSeconds, "827BB238C9");
        Assert.NotNull(tampered);
        Assert.True(tampered.HasReference);
        Assert.False(tampered.Matched);
        Assert.Equal(39, tampered.MatchingBits);

        clientVerifier.RecordBitwiseResult(tampered);
        var spoofEval = analyzer.Evaluate(
            healthySats,
            Array.Empty<GalileoINavMessage>(),
            "±0.35 m",
            "N/A",
            "N/A",
            new ThreatSimulationFlags(),
            clientVerifier.GetSummary(true),
            refClock.GetStatus(),
            new BitwiseSessionStats(2, 1, 1, 0, tampered));
        Assert.Equal(OsnmaReceptionStatus.TamperDetected, spoofEval.OsnmaStatus);
        Assert.True(spoofEval.IsSpoofingRisk);
        Assert.Contains("MISMATCH", spoofEval.OsnmaStatusText);
        Assert.True(spoofEval.ThreatScore >= 45);
    }

    private static byte[] BuildAnnexADsmKroot104Bytes()
    {
        var m = Convert.FromHexString(OsnmaEngine.AnnexAKrootM);   // 29 bytes (byte 0 is NMA header 0x82)
        var ds = Convert.FromHexString(OsnmaEngine.AnnexAKrootDs); // 64 bytes
        var pdk = Convert.FromHexString(OsnmaEngine.AnnexAPdk);    // 11 bytes

        var dsm = new byte[104];
        dsm[0] = 0x22; // NB_DK = 2 (8 blocks) | PKID = 2
        Buffer.BlockCopy(m, 1, dsm, 1, 28);
        Buffer.BlockCopy(ds, 0, dsm, 29, 64);
        Buffer.BlockCopy(pdk, 0, dsm, 93, 11);
        return dsm;
    }

    private static int GetFreeTcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
