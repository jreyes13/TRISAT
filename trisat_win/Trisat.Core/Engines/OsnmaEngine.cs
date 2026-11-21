using System.Numerics;
using System.Security.Cryptography;
using Trisat.Core.Models;

namespace Trisat.Core.Engines;

/// <summary>
/// Galileo OSNMA status & cryptographic Annex A self-test engine.
/// Maintains strict provenance separation: live received I/NAV pages are reported as Unverified
/// unless externally authenticated, while the EUSPA Annex A reference vector self-test runs real
/// SHA-256, ECDSA P-256, TESLA chain, and HMAC-SHA-256 verification independently.
/// </summary>
public sealed class OsnmaEngine
{
    // Official EUSPA Galileo OSNMA Annex A Reference Constants (RX Guidelines v1.3, Sat E02, WN=1248)
    public const string AnnexAMerkleRoot = "A10C440F3AA62453526DB4AF76DF8D9410D35D8277397D7053C700D192702B0D";
    public const string AnnexANpkCompressed = "0303B2CE64BC207BDD8BC4DF859187FCB686320D63FFA091410FC158FBB77980EA";
    public const string AnnexALeafM1 = "120303B2CE64BC207BDD8BC4DF859187FCB686320D63FFA091410FC158FBB77980EA";
    public const string AnnexAPdp = "B8884C0D33D6";
    public const string AnnexAKrootM = "8210492204E060610BDF26D77B5BF8C9CBFCF70422081475FD445DF0FF";
    public const string AnnexAKrootDs =
        "F8CD88299FA4605800207BFEBEAC55024053F30F7C69B35C15E60800AC3B6FE" +
        "3ED0639952F7B028D86867445961FFE94FB226BFF7006E0C451EE3F8728C177FB";
    public const string AnnexAPdk = "5E130DA4B44BBE7EC29522";
    public const string AnnexAAlpha = "610BDF26D77B";
    public const string AnnexAK0 = "5BF8C9CBFCF70422081475FD445DF0FF";
    public const string AnnexAK1 = "EFF999040E19B570835060BEBD23ED92";
    public const string AnnexAK2 = "2DC3A3CDB117FAADB83B5F0B6FEA88EB";
    public const string AnnexAK3 = "2E9C651C410CFF23D370497D28AB4B14";
    public const string AnnexAK4 = "69C00AA7364237A65EBF006AD8DDBC73";
    public const string AnnexAK14 = "ED2B77AAED7B633B58A69551FC388421";

    private static readonly BigInteger P256P = ParseUnsignedBigEndianHex("FFFFFFFF00000001000000000000000000000000FFFFFFFFFFFFFFFFFFFFFFFF");
    private static readonly BigInteger P256A = P256P - 3;
    private static readonly BigInteger P256B = ParseUnsignedBigEndianHex("5AC635D8AA3A93E7B3EBBD55769886BC651D06B0CC53B0F63BCE3C3E27D2604B");

    /// <summary>
    /// Reports live I/NAV reception status without overclaiming cryptographic verification.
    /// </summary>
    public OsnmaReceptionStatus GetReceptionStatus(IEnumerable<GalileoINavMessage> messages)
        => messages.Any(m => m.Data.Length > 0)
            ? OsnmaReceptionStatus.Unverified
            : OsnmaReceptionStatus.NotAvailable;

    public static byte[] FormatGstSubframe(int wn, int tow)
    {
        uint val = (((uint)wn & 0xFFFu) << 20) | ((uint)tow & 0xFFFFFu);
        return
        [
            (byte)(val >> 24),
            (byte)(val >> 16),
            (byte)(val >> 8),
            (byte)val
        ];
    }

    public static string ComputeTeslaKeyStep(string nextKeyHex, int wnTarget, int towTarget, string alphaHex, int keySizeBits = 128)
    {
        var kNext = Convert.FromHexString(nextKeyHex);
        var gst = FormatGstSubframe(wnTarget, towTarget);
        var alpha = Convert.FromHexString(alphaHex);

        var input = new byte[kNext.Length + gst.Length + alpha.Length];
        Buffer.BlockCopy(kNext, 0, input, 0, kNext.Length);
        Buffer.BlockCopy(gst, 0, input, kNext.Length, gst.Length);
        Buffer.BlockCopy(alpha, 0, input, kNext.Length + gst.Length, alpha.Length);

        var digest = SHA256.HashData(input);
        return Convert.ToHexString(digest.AsSpan(0, keySizeBits / 8));
    }

    public static bool VerifyTeslaKeyAgainstKroot(
        string keyHex,
        int wnKey,
        int towKey,
        string krootHex,
        int wnKroot,
        int tow0Kroot,
        string alphaHex,
        int keySizeBits = 128)
    {
        int steps = ((wnKey - wnKroot) * 604800 + (towKey - (tow0Kroot - 30))) / 30;
        if (steps < 0 || steps > 5000)
            return false;

        string currKey = keyHex.ToUpperInvariant();
        int currTow = towKey;
        int currWn = wnKey;

        for (int s = 0; s < steps; s++)
        {
            currTow -= 30;
            if (currTow < 0)
            {
                currTow += 604800;
                currWn -= 1;
            }
            currKey = ComputeTeslaKeyStep(currKey, currWn, currTow, alphaHex, keySizeBits);
        }

        return string.Equals(currKey, krootHex, StringComparison.OrdinalIgnoreCase);
    }

    public static int ComputeMacSeq(string keyNextHex, int prnA, int wnSf, int towSf, IReadOnlyList<ushort> mflexWords)
    {
        var keyBytes = Convert.FromHexString(keyNextHex);
        var gst = FormatGstSubframe(wnSf, towSf);
        var msg = new byte[1 + 4 + mflexWords.Count * 2];
        msg[0] = (byte)(prnA & 0xFF);
        Buffer.BlockCopy(gst, 0, msg, 1, 4);
        for (int i = 0; i < mflexWords.Count; i++)
        {
            msg[5 + i * 2] = (byte)(mflexWords[i] >> 8);
            msg[6 + i * 2] = (byte)(mflexWords[i] & 0xFF);
        }

        var mac = HMACSHA256.HashData(keyBytes, msg);
        int top16 = (mac[0] << 8) | mac[1];
        return (top16 >> 4) & 0xFFF;
    }

    public static string ComputeTruncatedTag(string keyHex, byte[] paddedMessage, int tagSizeBits = 40)
    {
        var keyBytes = Convert.FromHexString(keyHex);
        var mac = HMACSHA256.HashData(keyBytes, paddedMessage);
        return Convert.ToHexString(mac.AsSpan(0, tagSizeBits / 8));
    }

    public static (byte[] X, byte[] Y) DecompressP256PublicKey(string compressedHex)
    {
        var raw = Convert.FromHexString(compressedHex);
        if (raw.Length != 33 || (raw[0] != 0x02 && raw[0] != 0x03))
            throw new ArgumentException("Invalid SEC1 compressed P-256 public key.", nameof(compressedHex));

        int parity = raw[0] & 1;
        var x = new BigInteger(raw.AsSpan(1), isUnsigned: true, isBigEndian: true);
        var rhs = (BigInteger.ModPow(x, 3, P256P) + P256A * x + P256B) % P256P;
        if (rhs < 0) rhs += P256P;

        var y = BigInteger.ModPow(rhs, (P256P + 1) / 4, P256P);
        if ((int)(y & 1) != parity)
            y = P256P - y;

        return (ToFixedBigEndian32(x), ToFixedBigEndian32(y));
    }

    public static bool VerifyEcdsaP256Sha256(string compressedPublicKeyHex, byte[] messageBytes, string rawSignatureHex)
    {
        var sigBytes = Convert.FromHexString(rawSignatureHex);
        if (sigBytes.Length != 64)
            return false;

        var (x, y) = DecompressP256PublicKey(compressedPublicKeyHex);
        var ecParams = new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = x, Y = y }
        };

        using var ecdsa = ECDsa.Create(ecParams);
        return ecdsa.VerifyData(messageBytes, sigBytes, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }

    /// <summary>
    /// Runs the full 6-stage EUSPA Galileo OSNMA Annex A cryptographic self-test vector.
    /// </summary>
    public OsnmaSelfTestResult RunAnnexASelfTest()
    {
        // 1. DSM-PKR padding P_DP
        var pdpCalc = Convert.ToHexString(
            SHA256.HashData(Convert.FromHexString(AnnexAMerkleRoot + AnnexALeafM1)).AsSpan(0, 6));
        bool pdpOk = string.Equals(pdpCalc, AnnexAPdp, StringComparison.OrdinalIgnoreCase);

        // 2. DSM-KROOT ECDSA P-256 signature verification
        bool ecdsaOk = VerifyEcdsaP256Sha256(AnnexANpkCompressed, Convert.FromHexString(AnnexAKrootM), AnnexAKrootDs);

        // 3. DSM-KROOT padding P_DK
        var pdkCalc = Convert.ToHexString(
            SHA256.HashData(Convert.FromHexString(AnnexAKrootM + AnnexAKrootDs)).AsSpan(0, 11));
        bool pdkOk = string.Equals(pdkCalc, AnnexAPdk, StringComparison.OrdinalIgnoreCase);

        // 4. TESLA key chain K4 -> K3 -> K2 -> K1 -> K0 and K14 -> K0
        var k3 = ComputeTeslaKeyStep(AnnexAK4, 1248, 345660, AnnexAAlpha);
        var k2 = ComputeTeslaKeyStep(k3, 1248, 345630, AnnexAAlpha);
        var k1 = ComputeTeslaKeyStep(k2, 1248, 345600, AnnexAAlpha);
        var k0 = ComputeTeslaKeyStep(k1, 1248, 345570, AnnexAAlpha);
        bool teslaOk = k3 == AnnexAK3 && k2 == AnnexAK2 && k1 == AnnexAK1 && k0 == AnnexAK0
                       && VerifyTeslaKeyAgainstKroot(AnnexAK14, 1248, 345990, AnnexAK0, 1248, 345600, AnnexAAlpha);

        // 5. MACSEQ == 0xB08 at WN=1248, TOW=345660
        bool macSeqOk = ComputeMacSeq(AnnexAK4, 2, 1248, 345660, [0x1205, 0x050F]) == 0xB08;

        // 6. Tag2 (ADKD=4) == 7BB238C883
        var tag2Msg = Convert.FromHexString("02024E05463C03BFFFFFFFC00000449238227897FDEFF93040");
        bool tag2Ok = string.Equals(ComputeTruncatedTag(AnnexAK4, tag2Msg, 40), "7BB238C883", StringComparison.OrdinalIgnoreCase);

        bool allPassed = pdpOk && ecdsaOk && pdkOk && teslaOk && macSeqOk && tag2Ok;
        var summary = allPassed
            ? "PASS · Annex A Reference Vector Verified (P_DP, ECDSA P-256 KROOT, P_DK, TESLA K14→K0, MACSEQ 0xB08, ADKD=4 Tag2)"
            : "FAIL · One or more Annex A cryptographic checks failed";

        return new OsnmaSelfTestResult(
            Passed: allPassed,
            DsmPkrPaddingPassed: pdpOk,
            EcdsaP256KrootPassed: ecdsaOk,
            DsmKrootPaddingPassed: pdkOk,
            TeslaChainPassed: teslaOk,
            MacSeqPassed: macSeqOk,
            Adkd4Tag2Passed: tag2Ok,
            Summary: summary,
            ExecutedAtUtc: DateTimeOffset.UtcNow);
    }

    private static BigInteger ParseUnsignedBigEndianHex(string hex)
        => new(Convert.FromHexString(hex), isUnsigned: true, isBigEndian: true);

    private static byte[] ToFixedBigEndian32(BigInteger value)
    {
        var bytes = value.ToByteArray(isUnsigned: true, isBigEndian: true);
        if (bytes.Length == 32) return bytes;
        var padded = new byte[32];
        int copyLen = Math.Min(bytes.Length, 32);
        Buffer.BlockCopy(bytes, bytes.Length - copyLen, padded, 32 - copyLen, copyLen);
        return padded;
    }
}
