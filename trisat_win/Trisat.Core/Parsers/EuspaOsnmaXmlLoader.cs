using System.Numerics;
using System.Security.Cryptography;
using System.Xml.Linq;
using Trisat.Core.Models;

namespace Trisat.Core.Parsers;

/// <summary>
/// Parses official public EUSPA Galileo Service Centre (GSC) OSNMA XML files
/// (Merkle Tree XML and Public Key XML per Galileo OSNMA IDD ICD).
/// Zero third-party dependencies — uses built-in System.Xml.Linq & System.Security.Cryptography.
/// </summary>
public static class EuspaOsnmaXmlLoader
{
    public sealed record LoadedMerkleTree(
        string RootHex,
        string? HashFunction,
        int NodeCount);

    public sealed record LoadedPublicKey(
        int PublicKeyId,
        string PublicKeyType,
        string CompressedHex);

    public static LoadedMerkleTree LoadMerkleTreeFromFile(string xmlPath)
        => ParseMerkleTreeXml(File.ReadAllText(xmlPath));

    public static LoadedPublicKey LoadPublicKeyFromFile(string xmlPath)
        => ParsePublicKeyXml(File.ReadAllText(xmlPath));

    public static LoadedMerkleTree ParseMerkleTreeXml(string xmlContent)
    {
        var doc = XDocument.Parse(xmlContent);
        var elements = doc.Descendants().ToList();

        // Standard EUSPA format: <TreeNode> with <i>4</i>, <j>0</j>, <x_ji>HEX</x_ji>
        string? rootHex = null;
        int maxLevel = -1;
        int nodeCount = 0;

        foreach (var node in elements.Where(e => e.Name.LocalName.Equals("TreeNode", StringComparison.OrdinalIgnoreCase)))
        {
            nodeCount++;
            var iStr = node.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("i", StringComparison.OrdinalIgnoreCase))?.Value;
            var jStr = node.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("j", StringComparison.OrdinalIgnoreCase))?.Value;
            var valElem = node.Elements().FirstOrDefault(e =>
                e.Name.LocalName.Equals("x_ji", StringComparison.OrdinalIgnoreCase) ||
                e.Name.LocalName.Equals("value", StringComparison.OrdinalIgnoreCase));

            if (valElem is null) continue;
            var cleaned = CleanHex(valElem.Value);
            if (cleaned.Length != 64) continue;

            if (int.TryParse(iStr, out int level) && int.TryParse(jStr, out int index))
            {
                if (level > maxLevel || (level == maxLevel && index == 0))
                {
                    maxLevel = level;
                    rootHex = cleaned;
                }
            }
            else if (rootHex is null)
            {
                rootHex = cleaned;
            }
        }

        // Alternative flat XML tags: <MerkleRoot>, <Root>, <x_4_0>
        if (rootHex is null)
        {
            var direct = elements.FirstOrDefault(e =>
                e.Name.LocalName.Equals("MerkleRoot", StringComparison.OrdinalIgnoreCase) ||
                e.Name.LocalName.Equals("Root", StringComparison.OrdinalIgnoreCase) ||
                e.Name.LocalName.Equals("x_4_0", StringComparison.OrdinalIgnoreCase));
            if (direct is not null)
            {
                var cleaned = CleanHex(direct.Value);
                if (cleaned.Length == 64)
                    rootHex = cleaned;
            }
        }

        if (string.IsNullOrEmpty(rootHex))
            throw new FormatException("Could not locate a valid 256-bit (64 hex char) Merkle Root in the EUSPA XML file.");

        var hashFn = elements
            .FirstOrDefault(e => e.Name.LocalName.Equals("HashFunction", StringComparison.OrdinalIgnoreCase))
            ?.Value?.Trim();

        return new LoadedMerkleTree(rootHex, hashFn, Math.Max(nodeCount, 1));
    }

    public static LoadedPublicKey ParsePublicKeyXml(string xmlContent)
    {
        var doc = XDocument.Parse(xmlContent);
        var elements = doc.Descendants().ToList();

        int pkid = 0;
        var pkidEl = elements.FirstOrDefault(e =>
            e.Name.LocalName.Equals("PKID", StringComparison.OrdinalIgnoreCase) ||
            e.Name.LocalName.Equals("PublicKeyId", StringComparison.OrdinalIgnoreCase));
        if (pkidEl is not null)
            int.TryParse(pkidEl.Value.Trim(), out pkid);

        string pkType = elements
            .FirstOrDefault(e =>
                e.Name.LocalName.Equals("PKType", StringComparison.OrdinalIgnoreCase) ||
                e.Name.LocalName.Equals("PublicKeyType", StringComparison.OrdinalIgnoreCase))
            ?.Value?.Trim() ?? "ECDSA P-256/SHA-256";

        var pointEl = elements.FirstOrDefault(e =>
            e.Name.LocalName.Equals("point", StringComparison.OrdinalIgnoreCase) ||
            e.Name.LocalName.Equals("NPK", StringComparison.OrdinalIgnoreCase) ||
            e.Name.LocalName.Equals("PublicKeyValue", StringComparison.OrdinalIgnoreCase) ||
            e.Name.LocalName.Equals("Key", StringComparison.OrdinalIgnoreCase));

        if (pointEl is null)
            throw new FormatException("Could not locate public key material (<point> or <NPK>) in the EUSPA XML file.");

        string rawValue = pointEl.Value.Trim();
        string compressedHex = NormalizeToCompressedP256Hex(rawValue);

        return new LoadedPublicKey(pkid, pkType, compressedHex);
    }

    public static OsnmaAssistMaterial LoadAssistMaterialFromXmlFiles(string? merkleTreeXmlPath, string? publicKeyXmlPath)
    {
        string? merkleRoot = null;
        int? pkid = null;
        string? pkType = null;
        string? pkHex = null;

        if (!string.IsNullOrWhiteSpace(merkleTreeXmlPath) && File.Exists(merkleTreeXmlPath))
            merkleRoot = LoadMerkleTreeFromFile(merkleTreeXmlPath).RootHex;

        if (!string.IsNullOrWhiteSpace(publicKeyXmlPath) && File.Exists(publicKeyXmlPath))
        {
            var pk = LoadPublicKeyFromFile(publicKeyXmlPath);
            pkid = pk.PublicKeyId;
            pkType = pk.PublicKeyType;
            pkHex = pk.CompressedHex;
        }

        return new OsnmaAssistMaterial(
            MerkleRootHex: merkleRoot,
            PublicKeyId: pkid,
            PublicKeyType: pkType,
            PublicKeyCompressedHex: pkHex,
            Source: "EUSPA XML");
    }

    private static string NormalizeToCompressedP256Hex(string raw)
    {
        // 1. PEM SubjectPublicKeyInfo
        if (raw.Contains("BEGIN PUBLIC KEY", StringComparison.OrdinalIgnoreCase))
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(raw);
            return CompressEcPoint(ecdsa.ExportParameters(false).Q);
        }

        // 2. Direct hex (33-byte SEC1 compressed OR 65-byte SEC1 uncompressed 04||X||Y)
        var hex = CleanHex(raw);
        if (hex.Length == 66 && (hex.StartsWith("02", StringComparison.Ordinal) || hex.StartsWith("03", StringComparison.Ordinal)))
            return hex;

        if (hex.Length == 130 && hex.StartsWith("04", StringComparison.Ordinal))
        {
            var bytes = Convert.FromHexString(hex);
            byte prefix = (bytes[64] & 1) == 0 ? (byte)0x02 : (byte)0x03;
            var compressed = new byte[33];
            compressed[0] = prefix;
            Buffer.BlockCopy(bytes, 1, compressed, 1, 32);
            return Convert.ToHexString(compressed);
        }

        // 3. Base64 (either raw 33/65-byte SEC1 or DER SubjectPublicKeyInfo)
        try
        {
            var b64Bytes = Convert.FromBase64String(raw);
            if (b64Bytes.Length == 33 && (b64Bytes[0] == 0x02 || b64Bytes[0] == 0x03))
                return Convert.ToHexString(b64Bytes);
            if (b64Bytes.Length == 65 && b64Bytes[0] == 0x04)
            {
                byte prefix = (b64Bytes[64] & 1) == 0 ? (byte)0x02 : (byte)0x03;
                var compressed = new byte[33];
                compressed[0] = prefix;
                Buffer.BlockCopy(b64Bytes, 1, compressed, 1, 32);
                return Convert.ToHexString(compressed);
            }

            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(b64Bytes, out _);
            return CompressEcPoint(ecdsa.ExportParameters(false).Q);
        }
        catch (Exception ex)
        {
            throw new FormatException($"Unsupported EUSPA public key encoding: {ex.Message}", ex);
        }
    }

    private static string CompressEcPoint(ECPoint q)
    {
        if (q.X is null || q.Y is null || q.X.Length != 32 || q.Y.Length == 0)
            throw new FormatException("Only NIST P-256 (32-byte coordinate) public keys are supported.");

        byte prefix = (q.Y[^1] & 1) == 0 ? (byte)0x02 : (byte)0x03;
        var compressed = new byte[33];
        compressed[0] = prefix;
        Buffer.BlockCopy(q.X, 0, compressed, 1, 32);
        return Convert.ToHexString(compressed);
    }

    private static string CleanHex(string value)
    {
        var chars = value.Where(c => !char.IsWhiteSpace(c) && c != ':' && c != '-').ToArray();
        var s = new string(chars).ToUpperInvariant();
        if (s.StartsWith("0X", StringComparison.Ordinal))
            s = s[2..];
        return s;
    }
}
