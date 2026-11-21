using Trisat.Core.Models;

namespace Trisat.Core.Parsers;

/// <summary>
/// Clean-room bit-exact Galileo E1-B I/NAV nominal page parser implementing Galileo OS SIS ICD v2.1:
///   - Extracts Word Type, 128-bit navigation word (Data_k || Data_j), 40-bit OSNMA field (8-bit HKROOT + 32-bit MACK),
///     22-bit SAR, 8-bit SSP, 24-bit CRC-24Q (polynomial 0x1864CFB), and GST (WN/TOW) from WT 0, 5, and 6.
///   - Supports both u-blox ZED-F9P UBX-RXM-SFRBX 8-word (228-bit tail-stripped) and full 240-bit nominal pages.
/// </summary>
public static class GalileoINavParser
{
    public const uint Crc24qPolynomial = 0x1864CFBu;

    /// <summary>
    /// Computes Galileo CRC-24Q (generator polynomial 0x1864CFB, initial value 0) over a byte span.
    /// Per Galileo OS SIS ICD Section 4.3.5, the 196 protected I/NAV bits are left-padded with 4 zero bits
    /// to form 25 bytes (200 bits) before computing CRC-24Q.
    /// </summary>
    public static uint ComputeCrc24q(ReadOnlySpan<byte> data)
    {
        uint crc = 0;
        foreach (byte b in data)
        {
            crc ^= (uint)b << 16;
            for (int i = 0; i < 8; i++)
            {
                crc <<= 1;
                if ((crc & 0x1000000u) != 0)
                    crc ^= Crc24qPolynomial;
            }
        }
        return crc & 0xFFFFFFu;
    }

    /// <summary>
    /// Decodes a u-blox ZED-F9P Galileo E1-B I/NAV UBX-RXM-SFRBX payload (8 x 32-bit little-endian words = 32 bytes).
    /// Each 32-bit word is converted to big-endian MSB-first order, yielding the 228-bit tail-stripped nominal page:
    ///   Even half-page (114 bits): [0] Even/Odd=0, [1] PageType, [2..113] Data_k (112 bits)
    ///   Odd  half-page (114 bits): [114] Even/Odd=1, [115] PageType, [116..131] Data_j (16 bits),
    ///                              [132..171] OSNMA (40 bits = 8b HKROOT + 32b MACK),
    ///                              [172..193] SAR (22 bits), [194..195] Spare (2 bits),
    ///                              [196..219] CRC-24Q (24 bits), [220..227] SSP/Reserved2 (8 bits)
    /// </summary>
    public static ParsedGalileoINavPage? TryParseUbxSfrbxPayload(int svid, ReadOnlySpan<byte> eightLittleEndianWords)
    {
        if (svid < 1 || svid > 36 || eightLittleEndianWords.Length < 32)
            return null;

        Span<byte> msbStream = stackalloc byte[32];
        for (int w = 0; w < 8; w++)
        {
            int src = w * 4;
            msbStream[src + 0] = eightLittleEndianWords[src + 3];
            msbStream[src + 1] = eightLittleEndianWords[src + 2];
            msbStream[src + 2] = eightLittleEndianWords[src + 1];
            msbStream[src + 3] = eightLittleEndianWords[src + 0];
        }

        return ParseBitLayout(svid, msbStream, oddOffsetBits: 114, GalileoINavLayout.UbxSfrbx228Bit);
    }

    /// <summary>
    /// Decodes a full 240-bit (30-byte) Galileo E1-B I/NAV nominal page (even 120b with 6b tail + odd 120b with 6b tail),
    /// as emitted by Android GnssNavigationMessage TYPE_GAL_I or reconstructed Septentrio SBF pages.
    /// </summary>
    public static ParsedGalileoINavPage? TryParseFull240BitPage(int svid, ReadOnlySpan<byte> thirtyBytes)
    {
        if (svid < 1 || svid > 36 || thirtyBytes.Length < 30)
            return null;

        return ParseBitLayout(svid, thirtyBytes, oddOffsetBits: 120, GalileoINavLayout.FullNominal240Bit);
    }

    /// <summary>
    /// Extracts the 196 CRC-protected bits left-padded with 4 zero bits (25 bytes) and validates CRC-24Q.
    /// </summary>
    private static ParsedGalileoINavPage? ParseBitLayout(
        int svid,
        ReadOnlySpan<byte> bits,
        int oddOffsetBits,
        GalileoINavLayout layout)
    {
        // Basic structural check: Even half starts with 0, Odd half starts with 1 (unless inverted/synthetic)
        int evenFlag = GetBit(bits, 0);
        int oddFlag = GetBit(bits, oddOffsetBits);

        // Assemble 128-bit navigation word: Data_k (bits 2..113) || Data_j (bits oddOffset+2 .. oddOffset+17)
        var data128 = new byte[16];
        CopyBits(bits, 2, data128, 0, 112);
        CopyBits(bits, oddOffsetBits + 2, data128, 112, 16);

        int wordType = (int)ReadBits(data128, 0, 6);

        // Extract 40-bit OSNMA field: oddOffset+18 .. oddOffset+57 (8-bit HKROOT + 32-bit MACK)
        var osnma40 = new byte[5];
        CopyBits(bits, oddOffsetBits + 18, osnma40, 0, 40);
        byte hkroot = osnma40[0];
        var mack = new byte[4];
        Buffer.BlockCopy(osnma40, 1, mack, 0, 4);

        uint sar = (uint)ReadBits(bits, oddOffsetBits + 58, 22);
        uint crcReceived = (uint)ReadBits(bits, oddOffsetBits + 82, 24);
        byte ssp = (byte)ReadBits(bits, oddOffsetBits + 106, 8);

        // Build 200-bit (25-byte) CRC input: 4 zero bits || Even[0..113] (114b) || Odd[0..81] (82b)
        Span<byte> crcInput = stackalloc byte[25];
        crcInput.Clear();
        CopyBits(bits, 0, crcInput, 4, 114);
        CopyBits(bits, oddOffsetBits, crcInput, 4 + 114, 82);
        uint crcComputed = ComputeCrc24q(crcInput);

        bool crcValid = evenFlag == 0 && oddFlag == 1 && crcReceived == crcComputed;

        var (gst, towOnly) = TryDecodeGstFromWord(wordType, data128);

        return new ParsedGalileoINavPage(
            Svid: svid,
            WordType: wordType,
            Data128Bytes: data128,
            HkrootByte: hkroot,
            MackBytes: mack,
            Osnma40Bytes: osnma40,
            Crc24qReceived: crcReceived,
            Crc24qComputed: crcComputed,
            CrcValid: crcValid,
            SarBits: sar,
            SspByte: ssp,
            DecodedGst: gst,
            DecodedTowOnly: towOnly,
            Layout: layout);
    }

    /// <summary>
    /// Decodes Galileo System Time (WN and/or TOW) from I/NAV Word Types 0, 5, and 6 per Galileo OS SIS ICD.
    /// </summary>
    public static (GstTime? FullGst, int? TowOnly) TryDecodeGstFromWord(int wordType, ReadOnlySpan<byte> data128)
    {
        if (data128.Length < 16)
            return (null, null);

        switch (wordType)
        {
            case 0:
            {
                // WT 0: bits 6..7 = Time (2 when valid), bits 96..107 = WN (12b), bits 108..127 = TOW (20b)
                int timeFlag = (int)ReadBits(data128, 6, 2);
                if (timeFlag == 2)
                {
                    int wn = (int)ReadBits(data128, 96, 12);
                    int tow = (int)ReadBits(data128, 108, 20);
                    if (tow >= 0 && tow < GstTime.SecondsPerWeek)
                        return (new GstTime(wn, tow), tow);
                }
                return (null, null);
            }
            case 5:
            {
                // WT 5: bits 73..84 = WN (12b), bits 85..104 = TOW (20b)
                int wn = (int)ReadBits(data128, 73, 12);
                int tow = (int)ReadBits(data128, 85, 20);
                if (tow >= 0 && tow < GstTime.SecondsPerWeek)
                    return (new GstTime(wn, tow), tow);
                return (null, null);
            }
            case 6:
            {
                // WT 6: bits 105..124 = TOW (20b)
                int tow = (int)ReadBits(data128, 105, 20);
                if (tow >= 0 && tow < GstTime.SecondsPerWeek)
                    return (null, tow);
                return (null, null);
            }
            default:
                return (null, null);
        }
    }

    /// <summary>
    /// Builds a spec-compliant Galileo E1-B I/NAV 240-bit (30-byte) nominal page with valid CRC-24Q.
    /// Useful for unit tests, self-diagnostics, and reference encoding.
    /// </summary>
    public static byte[] BuildFull240BitNominalPage(
        ReadOnlySpan<byte> data128Bytes,
        byte hkrootByte,
        ReadOnlySpan<byte> mack4Bytes,
        uint sar22Bits = 0,
        byte sspByte = 0x04)
    {
        if (data128Bytes.Length != 16)
            throw new ArgumentException("data128Bytes must be 16 bytes.", nameof(data128Bytes));
        if (mack4Bytes.Length != 4)
            throw new ArgumentException("mack4Bytes must be 4 bytes.", nameof(mack4Bytes));

        var page = new byte[30];
        // Even half-page: [0]=0, [1]=0, [2..113]=Data_k (112 bits), [114..119]=0 tail
        CopyBits(data128Bytes, 0, page, 2, 112);

        // Odd half-page at bit 120: [120]=1, [121]=0, [122..137]=Data_j (16 bits)
        SetBit(page, 120, 1);
        CopyBits(data128Bytes, 112, page, 122, 16);

        // OSNMA 40 bits at bit 138..177
        Span<byte> osnma = stackalloc byte[5];
        osnma[0] = hkrootByte;
        mack4Bytes.CopyTo(osnma[1..]);
        CopyBits(osnma, 0, page, 138, 40);

        // SAR 22 bits at bit 178..199
        WriteBits(page, 178, 22, sar22Bits);

        // Compute CRC-24Q over 4 zero bits || Even[0..113] || Odd[120..201]
        Span<byte> crcInput = stackalloc byte[25];
        crcInput.Clear();
        CopyBits(page, 0, crcInput, 4, 114);
        CopyBits(page, 120, crcInput, 4 + 114, 82);
        uint crc = ComputeCrc24q(crcInput);

        WriteBits(page, 202, 24, crc);
        WriteBits(page, 226, 8, sspByte);
        return page;
    }

    /// <summary>
    /// Builds a spec-compliant u-blox ZED-F9P UBX-RXM-SFRBX 32-byte (8 x little-endian 32-bit words) Galileo I/NAV payload
    /// with valid CRC-24Q.
    /// </summary>
    public static byte[] BuildUbxSfrbxPayloadWords(
        ReadOnlySpan<byte> data128Bytes,
        byte hkrootByte,
        ReadOnlySpan<byte> mack4Bytes,
        uint sar22Bits = 0,
        byte sspByte = 0x04)
    {
        if (data128Bytes.Length != 16)
            throw new ArgumentException("data128Bytes must be 16 bytes.", nameof(data128Bytes));
        if (mack4Bytes.Length != 4)
            throw new ArgumentException("mack4Bytes must be 4 bytes.", nameof(mack4Bytes));

        Span<byte> msb = stackalloc byte[32];
        msb.Clear();

        // Even half-page (114 bits): [0]=0, [1]=0, [2..113]=Data_k
        CopyBits(data128Bytes, 0, msb, 2, 112);

        // Odd half-page at bit 114: [114]=1, [115]=0, [116..131]=Data_j
        SetBit(msb, 114, 1);
        CopyBits(data128Bytes, 112, msb, 116, 16);

        // OSNMA 40 bits at bit 132..171
        Span<byte> osnma = stackalloc byte[5];
        osnma[0] = hkrootByte;
        mack4Bytes.CopyTo(osnma[1..]);
        CopyBits(osnma, 0, msb, 132, 40);

        // SAR 22 bits at bit 172..193
        WriteBits(msb, 172, 22, sar22Bits);

        // CRC-24Q at bit 196..219
        Span<byte> crcInput = stackalloc byte[25];
        crcInput.Clear();
        CopyBits(msb, 0, crcInput, 4, 114);
        CopyBits(msb, 114, crcInput, 4 + 114, 82);
        uint crc = ComputeCrc24q(crcInput);
        WriteBits(msb, 196, 24, crc);

        // SSP 8 bits at bit 220..227
        WriteBits(msb, 220, 8, sspByte);

        // Convert each 32-bit big-endian word into u-blox little-endian word order
        var ubxWords = new byte[32];
        for (int w = 0; w < 8; w++)
        {
            int dst = w * 4;
            ubxWords[dst + 0] = msb[dst + 3];
            ubxWords[dst + 1] = msb[dst + 2];
            ubxWords[dst + 2] = msb[dst + 1];
            ubxWords[dst + 3] = msb[dst + 0];
        }
        return ubxWords;
    }

    /// <summary>
    /// Helper to construct a valid Galileo I/NAV Word Type 0 (Spare + WN/TOW) 128-bit payload.
    /// </summary>
    public static byte[] BuildWordType0(int wn, int tow)
    {
        var word = new byte[16];
        WriteBits(word, 0, 6, 0);   // Word Type = 0
        WriteBits(word, 6, 2, 2);   // Time = 2 (WN + TOW valid)
        WriteBits(word, 96, 12, (uint)(wn & 0xFFF));
        WriteBits(word, 108, 20, (uint)(tow & 0xFFFFF));
        return word;
    }

    /// <summary>
    /// Helper to construct a valid Galileo I/NAV Word Type 5 (Iono + Health + WN/TOW) 128-bit payload.
    /// </summary>
    public static byte[] BuildWordType5(int wn, int tow)
    {
        var word = new byte[16];
        WriteBits(word, 0, 6, 5);   // Word Type = 5
        WriteBits(word, 73, 12, (uint)(wn & 0xFFF));
        WriteBits(word, 85, 20, (uint)(tow & 0xFFFFF));
        return word;
    }

    internal static int GetBit(ReadOnlySpan<byte> data, int bitIndex)
        => (data[bitIndex >> 3] >> (7 - (bitIndex & 7))) & 1;

    internal static void SetBit(Span<byte> data, int bitIndex, int value)
    {
        int byteIdx = bitIndex >> 3;
        int shift = 7 - (bitIndex & 7);
        if (value != 0)
            data[byteIdx] |= (byte)(1 << shift);
        else
            data[byteIdx] &= (byte)~(1 << shift);
    }

    internal static void CopyBits(ReadOnlySpan<byte> src, int srcBitOffset, Span<byte> dst, int dstBitOffset, int bitLength)
    {
        for (int i = 0; i < bitLength; i++)
            SetBit(dst, dstBitOffset + i, GetBit(src, srcBitOffset + i));
    }

    internal static ulong ReadBits(ReadOnlySpan<byte> data, int bitOffset, int bitLength)
    {
        ulong val = 0;
        for (int i = 0; i < bitLength; i++)
            val = (val << 1) | (uint)GetBit(data, bitOffset + i);
        return val;
    }

    internal static void WriteBits(Span<byte> data, int bitOffset, int bitLength, ulong value)
    {
        for (int i = 0; i < bitLength; i++)
        {
            int bit = (int)((value >> (bitLength - 1 - i)) & 1UL);
            SetBit(data, bitOffset + i, bit);
        }
    }
}
