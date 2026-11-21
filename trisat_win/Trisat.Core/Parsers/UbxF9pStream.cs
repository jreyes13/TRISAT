using System.Text;
using Trisat.Core.Models;

namespace Trisat.Core.Parsers;

public enum FramedPacketKind
{
    Nmea,
    Ubx
}

public sealed record FramedPacket(FramedPacketKind Kind, byte[] RawBytes)
{
    public string AsAsciiText() => Encoding.ASCII.GetString(RawBytes).Trim();
}

/// <summary>
/// Frames mixed ASCII NMEA 0183 sentences and binary u-blox UBX packets from a ZED-F9P serial stream,
/// validates UBX Fletcher-8 checksums, extracts Galileo I/NAV subframes from UBX-RXM-SFRBX (0x02 0x13),
/// and constructs u-blox UBX-CFG configuration & factory-reset messages.
/// </summary>
public sealed class UbxF9pStream
{
    private const byte Sync1 = 0xB5;
    private const byte Sync2 = 0x62;
    private const int MaxNmeaLength = 1024;

    private readonly List<byte> _buffer = new(4096);

    public static (byte CkA, byte CkB) CalculateChecksum(ReadOnlySpan<byte> classIdLenPayload)
    {
        byte ckA = 0;
        byte ckB = 0;
        foreach (byte b in classIdLenPayload)
        {
            ckA = unchecked((byte)(ckA + b));
            ckB = unchecked((byte)(ckB + ckA));
        }
        return (ckA, ckB);
    }

    public static byte[] BuildUbxMessage(byte msgClass, byte msgId, ReadOnlySpan<byte> payload = default)
    {
        var msg = new byte[6 + payload.Length + 2];
        msg[0] = Sync1;
        msg[1] = Sync2;
        msg[2] = msgClass;
        msg[3] = msgId;
        msg[4] = (byte)(payload.Length & 0xFF);
        msg[5] = (byte)((payload.Length >> 8) & 0xFF);
        payload.CopyTo(msg.AsSpan(6));

        var (ckA, ckB) = CalculateChecksum(msg.AsSpan(2, 4 + payload.Length));
        msg[^2] = ckA;
        msg[^1] = ckB;
        return msg;
    }

    /// <summary>
    /// Builds the standard TRISAT ZED-F9P initialization sequence (poll MON-VER + enable GSV/GGA/RMC/GSA/GST + UBX-RXM-SFRBX).
    /// </summary>
    public static IReadOnlyList<byte[]> BuildEnableNavigationSequence()
    {
        var list = new List<byte[]>
        {
            BuildUbxMessage(0x0A, 0x04) // UBX-MON-VER poll
        };

        (byte Cls, byte Id)[] messages =
        [
            (0xF0, 0x03), // GSV
            (0xF0, 0x00), // GGA
            (0xF0, 0x04), // RMC
            (0xF0, 0x02), // GSA
            (0xF0, 0x07), // GST
            (0x02, 0x13)  // UBX-RXM-SFRBX (Raw Galileo I/NAV subframes)
        ];

        foreach (var (cls, id) in messages)
            list.Add(BuildUbxMessage(0x06, 0x01, [cls, id, 0x01]));

        return list;
    }

    /// <summary>
    /// Builds the full 5-step factory reset & TRISAT optimization sequence (matching trisat_hw/zed_f9p/f9p_factory_reset.py).
    /// </summary>
    public static IReadOnlyList<byte[]> BuildFactoryResetSequence()
    {
        var list = new List<byte[]>
        {
            // Step 1: Revert to factory defaults
            BuildUbxMessage(0x06, 0x09, [
                0xFF, 0xFF, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0xFF, 0xFF, 0x00, 0x00,
                0x07
            ]),
            // Step 2: UART1 115200 8N1 UBX+NMEA
            BuildUbxMessage(0x06, 0x00, [
                0x01, 0x00, 0x00, 0x00,
                0xD0, 0x08, 0x00, 0x00,
                0x00, 0xC2, 0x01, 0x00,
                0x07, 0x00, 0x03, 0x00,
                0x00, 0x00, 0x00, 0x00
            ]),
            // Step 2b: UART2 115200 8N1 UBX+NMEA
            BuildUbxMessage(0x06, 0x00, [
                0x02, 0x00, 0x00, 0x00,
                0xD0, 0x08, 0x00, 0x00,
                0x00, 0xC2, 0x01, 0x00,
                0x07, 0x00, 0x03, 0x00,
                0x00, 0x00, 0x00, 0x00
            ])
        };

        // Step 3: Enable NMEA + UBX-RXM-SFRBX on all ports at 1 Hz
        (byte Cls, byte Id)[] allPortMsgs =
        [
            (0xF0, 0x00), (0xF0, 0x04), (0xF0, 0x03),
            (0xF0, 0x01), (0xF0, 0x05), (0xF0, 0x07), (0x02, 0x13)
        ];
        foreach (var (cls, id) in allPortMsgs)
            list.Add(BuildUbxMessage(0x06, 0x01, [cls, id, 0x01, 0x01, 0x01, 0x01, 0x01, 0x00]));

        // Step 4: 1 Hz navigation rate
        list.Add(BuildUbxMessage(0x06, 0x08, [0xE8, 0x03, 0x01, 0x00, 0x01, 0x00]));

        // Step 5: Save to BBR + Flash + EEPROM
        list.Add(BuildUbxMessage(0x06, 0x09, [
            0x00, 0x00, 0x00, 0x00,
            0xFF, 0xFF, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x07
        ]));

        return list;
    }

    /// <summary>
    /// Feeds raw bytes from a serial port or UDP stream and returns all complete validated frames in order.
    /// Pass an empty span to flush/skip past a stalled incomplete header on timeout.
    /// </summary>
    public IReadOnlyList<FramedPacket> Feed(ReadOnlySpan<byte> chunk)
    {
        var packets = new List<FramedPacket>();
        if (!chunk.IsEmpty)
            _buffer.AddRange(chunk.ToArray());

        while (_buffer.Count > 0)
        {
            int start = FindMessageStart(_buffer, 0);
            if (start < 0)
            {
                if (!chunk.IsEmpty && _buffer[^1] == Sync1)
                {
                    _buffer.RemoveRange(0, _buffer.Count - 1);
                }
                else
                {
                    _buffer.Clear();
                }
                break;
            }

            if (start > 0)
                _buffer.RemoveRange(0, start);

            if (_buffer.Count >= 2 && _buffer[0] == Sync1 && _buffer[1] == Sync2)
            {
                if (_buffer.Count >= 6)
                {
                    int payloadLen = _buffer[4] | (_buffer[5] << 8);
                    int totalLen = payloadLen + 8;
                    if (_buffer.Count >= totalLen)
                    {
                        var frame = _buffer.GetRange(0, totalLen).ToArray();
                        var (ckA, ckB) = CalculateChecksum(frame.AsSpan(2, totalLen - 4));
                        if (ckA == frame[^2] && ckB == frame[^1])
                        {
                            _buffer.RemoveRange(0, totalLen);
                            packets.Add(new FramedPacket(FramedPacketKind.Ubx, frame));
                        }
                        else
                        {
                            _buffer.RemoveAt(0);
                        }
                        continue;
                    }
                }
            }
            else if (_buffer[0] == (byte)'$')
            {
                int nlIdx = _buffer.IndexOf((byte)'\n');
                int end = nlIdx >= 0 ? nlIdx + 1 : 0;
                int following = FindMessageStart(_buffer, 1);

                if (following > 0 && (end == 0 || following < end))
                {
                    _buffer.RemoveRange(0, following);
                    continue;
                }

                if (end > 0)
                {
                    var frame = _buffer.GetRange(0, end).ToArray();
                    _buffer.RemoveRange(0, end);
                    if (end <= MaxNmeaLength && IsPrintableAsciiLine(frame))
                        packets.Add(new FramedPacket(FramedPacketKind.Nmea, frame));
                    continue;
                }

                if (_buffer.Count >= MaxNmeaLength)
                {
                    _buffer.RemoveAt(0);
                    continue;
                }
            }

            if (!chunk.IsEmpty)
                break;

            _buffer.RemoveAt(0);
        }

        return packets;
    }

    /// <summary>
    /// Decodes a validated UBX-RXM-SFRBX (Class 0x02, ID 0x13) Galileo packet into a GalileoINavMessage
    /// and its corresponding $GALINAV sentence.
    /// </summary>
    public static (GalileoINavMessage Message, string GalinavSentence)? TryDecodeGalileoSfrbx(ReadOnlySpan<byte> ubxFrame)
    {
        if (ubxFrame.Length < 16 || ubxFrame[0] != Sync1 || ubxFrame[1] != Sync2)
            return null;

        byte msgClass = ubxFrame[2];
        byte msgId = ubxFrame[3];
        if (msgClass != 0x02 || msgId != 0x13)
            return null;

        int payloadLen = ubxFrame[4] | (ubxFrame[5] << 8);
        if (ubxFrame.Length < payloadLen + 8 || payloadLen < 8)
            return null;

        var payload = ubxFrame.Slice(6, payloadLen);
        byte gnssId = payload[0];
        byte svId = payload[1];
        byte numWords = payload[4];

        // Galileo gnssId == 2, nominal 8 words (250 bits I/NAV)
        if (gnssId != 2 || svId < 1 || svId > 36 || numWords < 8)
            return null;

        int dataBytes = Math.Min(numWords * 4, 32);
        if (payload.Length < 8 + dataBytes)
            return null;

        var frameBytes = payload.Slice(8, dataBytes).ToArray();

        // Attempt bit-exact Galileo OS SIS ICD v2.1 decoding (228-bit UBX-RXM-SFRBX layout + CRC-24Q + 40-bit OSNMA + GST)
        var parsed = dataBytes >= 32
            ? GalileoINavParser.TryParseUbxSfrbxPayload(svId, frameBytes)
            : null;

        int wordType;
        string? hkrootHex = null;
        string? mackHex = null;
        bool crcValid = false;
        int? gstWeek = null;
        int? gstTow = null;

        if (parsed is { CrcValid: true })
        {
            wordType = parsed.WordType;
            crcValid = true;
            if (parsed.HasNonZeroOsnma)
            {
                hkrootHex = parsed.HkrootHex;
                mackHex = parsed.MackHex;
            }
            gstWeek = parsed.DecodedGst?.WeekNumber;
            gstTow = parsed.DecodedGst?.TowSeconds ?? parsed.DecodedTowOnly;
        }
        else
        {
            // Fallback for legacy/unaligned/synthetic frames without valid CRC-24Q
            uint w0 = frameBytes[0]
                      | ((uint)frameBytes[1] << 8)
                      | ((uint)frameBytes[2] << 16)
                      | ((uint)frameBytes[3] << 24);
            wordType = (int)((w0 >> 2) & 0x3F);
            if (wordType == 0) wordType = 1;
        }

        var hex = Convert.ToHexString(frameBytes);
        var sentenceBody = hkrootHex is not null && mackHex is not null
            ? $"GALINAV,{svId},{wordType},{hex},{hkrootHex}{mackHex}"
            : $"GALINAV,{svId},{wordType},{hex}";
        var sentence = NmeaParser.FormatSentence(sentenceBody);

        var msg = new GalileoINavMessage(
            Id: Guid.NewGuid(),
            Svid: svId,
            SubmessageId: wordType,
            Data: frameBytes,
            Status: crcValid ? 1 : 0,
            Timestamp: DateTimeOffset.UtcNow,
            Band: "GAL_E1",
            OsnmaHkrootHex: hkrootHex,
            OsnmaMackHex: mackHex,
            IsSimulated: false,
            CrcValid: crcValid,
            GstWeek: gstWeek,
            GstTow: gstTow);

        return (msg, sentence);
    }

    private static int FindMessageStart(List<byte> buf, int startIndex)
    {
        for (int i = startIndex; i < buf.Count; i++)
        {
            if (buf[i] == (byte)'$')
                return i;
            if (buf[i] == Sync1 && i + 1 < buf.Count && buf[i + 1] == Sync2)
                return i;
        }
        return -1;
    }

    private static bool IsPrintableAsciiLine(byte[] frame)
    {
        int len = frame.Length;
        while (len > 0 && (frame[len - 1] == '\r' || frame[len - 1] == '\n'))
            len--;

        for (int i = 0; i < len; i++)
        {
            if (frame[i] < 32 || frame[i] > 126)
                return false;
        }
        return len > 0;
    }
}
