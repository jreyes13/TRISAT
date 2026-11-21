import Foundation
import CryptoKit

/// Galileo OSNMA & Assisted OSNMA Verification Engine for iOS & macOS
/// Implements Galileo OS SIS ICD v2.1 I/NAV decoding (CRC-24Q, 40-bit OSNMA, GST) and
/// Galileo OSNMA SIS ICD v1.1 / RX Guidelines v1.3 cryptographic primitives + Assisted OSNMA HTTP client.
enum OsnmaAuthStatus: Equatable {
    case notAvailable
    case unverified
    case bitwiseVerified
    case authenticated
    case tamperDetected
}

/// Galileo System Time (GST: Week Number since 1999-08-22 00:00:00 UTC + Time of Week in seconds).
struct GstTime: Codable, Equatable {
    static let secondsPerWeek: Int = 604_800
    static let defaultLeapSeconds: Int = 18
    /// 1999-08-22T00:00:00Z in Unix epoch seconds.
    static let galileoEpochUnixSeconds: TimeInterval = 935_280_000.0

    let weekNumber: Int
    let towSeconds: Int

    var totalSeconds: Int64 {
        Int64(weekNumber) * Int64(Self.secondsPerWeek) + Int64(towSeconds)
    }

    var subframeStartTow: Int {
        (towSeconds / 30) * 30
    }

    static func fromUtc(_ date: Date, leapSeconds: Int = defaultLeapSeconds) -> GstTime {
        let elapsed = date.timeIntervalSince1970 - galileoEpochUnixSeconds + Double(leapSeconds)
        guard elapsed >= 0 else { return GstTime(weekNumber: 0, towSeconds: 0) }
        let total = Int64(floor(elapsed))
        let wn = Int(total / Int64(secondsPerWeek))
        let tow = Int(total % Int64(secondsPerWeek))
        return GstTime(weekNumber: wn, towSeconds: tow)
    }

    static func fromTotalSeconds(_ total: Int64) -> GstTime {
        guard total >= 0 else { return GstTime(weekNumber: 0, towSeconds: 0) }
        let wn = Int(total / Int64(secondsPerWeek))
        let tow = Int(total % Int64(secondsPerWeek))
        return GstTime(weekNumber: wn, towSeconds: tow)
    }

    var formatted: String {
        String(format: "WN %d · TOW %06ds", weekNumber, towSeconds)
    }
}

/// Bit-exact decoded Galileo E1-B I/NAV nominal page.
struct ParsedGalileoINavPage: Equatable {
    let svid: Int
    let wordType: Int
    let data128: [UInt8]
    let hkrootHex: String
    let mackHex: String
    let osnma40Hex: String
    let crcValid: Bool
    let decodedGst: GstTime?
    let decodedTowOnly: Int?

    var hasNonZeroOsnma: Bool {
        osnma40Hex != "0000000000"
    }
}

/// Clean-room Galileo E1-B I/NAV nominal page bit parser (228-bit UBX-RXM-SFRBX & 240-bit full nominal page).
enum GalileoINavBitParser {

    static func parseUbxSfrbx228(svid: Int, data: Data) -> ParsedGalileoINavPage? {
        guard (1...36).contains(svid), data.count >= 32 else { return nil }
        let raw = [UInt8](data.prefix(32))
        var msb = [UInt8](repeating: 0, count: 32)
        for w in 0..<8 {
            let base = w * 4
            msb[base + 0] = raw[base + 3]
            msb[base + 1] = raw[base + 2]
            msb[base + 2] = raw[base + 1]
            msb[base + 3] = raw[base + 0]
        }
        return parseLayout(svid: svid, bits: msb, oddOffsetBits: 114)
    }

    static func parseFull240Bit(svid: Int, data: Data) -> ParsedGalileoINavPage? {
        guard (1...36).contains(svid), data.count >= 30 else { return nil }
        return parseLayout(svid: svid, bits: [UInt8](data.prefix(30)), oddOffsetBits: 120)
    }

    private static func parseLayout(svid: Int, bits: [UInt8], oddOffsetBits: Int) -> ParsedGalileoINavPage {
        let evenFlag = getBit(bits, 0)
        let oddFlag = getBit(bits, oddOffsetBits)

        var data128 = [UInt8](repeating: 0, count: 16)
        copyBits(src: bits, srcOffset: 2, dst: &data128, dstOffset: 0, length: 112)
        copyBits(src: bits, srcOffset: oddOffsetBits + 2, dst: &data128, dstOffset: 112, length: 16)

        let wordType = Int(readBits(data128, offset: 0, length: 6))

        var osnma40 = [UInt8](repeating: 0, count: 5)
        copyBits(src: bits, srcOffset: oddOffsetBits + 18, dst: &osnma40, dstOffset: 0, length: 40)

        let crcReceived = UInt32(readBits(bits, offset: oddOffsetBits + 82, length: 24))

        var crcInput = [UInt8](repeating: 0, count: 25)
        copyBits(src: bits, srcOffset: 0, dst: &crcInput, dstOffset: 4, length: 114)
        copyBits(src: bits, srcOffset: oddOffsetBits, dst: &crcInput, dstOffset: 118, length: 82)
        let crcComputed = OsnmaEngine.computeCrc24q(crcInput)

        let crcValid = (evenFlag == 0) && (oddFlag == 1) && (crcReceived == crcComputed)
        let (gst, towOnly) = decodeGstFromWord(wordType: wordType, data128: data128)

        let hkHex = String(format: "%02X", osnma40[0])
        let mackHex = osnma40[1...4].map { String(format: "%02X", $0) }.joined()
        return ParsedGalileoINavPage(
            svid: svid,
            wordType: wordType,
            data128: data128,
            hkrootHex: hkHex,
            mackHex: mackHex,
            osnma40Hex: hkHex + mackHex,
            crcValid: crcValid,
            decodedGst: gst,
            decodedTowOnly: towOnly
        )
    }

    private static func decodeGstFromWord(wordType: Int, data128: [UInt8]) -> (GstTime?, Int?) {
        switch wordType {
        case 0:
            let timeFlag = Int(readBits(data128, offset: 6, length: 2))
            if timeFlag == 2 {
                let wn = Int(readBits(data128, offset: 96, length: 12))
                let tow = Int(readBits(data128, offset: 108, length: 20))
                if tow < GstTime.secondsPerWeek {
                    return (GstTime(weekNumber: wn, towSeconds: tow), tow)
                }
            }
            return (nil, nil)
        case 5:
            let wn = Int(readBits(data128, offset: 73, length: 12))
            let tow = Int(readBits(data128, offset: 85, length: 20))
            if tow < GstTime.secondsPerWeek {
                return (GstTime(weekNumber: wn, towSeconds: tow), tow)
            }
            return (nil, nil)
        case 6:
            let tow = Int(readBits(data128, offset: 105, length: 20))
            if tow < GstTime.secondsPerWeek {
                return (nil, tow)
            }
            return (nil, nil)
        default:
            return (nil, nil)
        }
    }

    private static func getBit(_ data: [UInt8], _ index: Int) -> Int {
        Int((data[index >> 3] >> (7 - (index & 7))) & 1)
    }

    private static func setBit(_ data: inout [UInt8], _ index: Int, _ val: Int) {
        let byteIdx = index >> 3
        let mask: UInt8 = 1 << (7 - (index & 7))
        if val != 0 {
            data[byteIdx] |= mask
        } else {
            data[byteIdx] &= ~mask
        }
    }

    private static func copyBits(src: [UInt8], srcOffset: Int, dst: inout [UInt8], dstOffset: Int, length: Int) {
        for i in 0..<length {
            setBit(&dst, dstOffset + i, getBit(src, srcOffset + i))
        }
    }

    private static func readBits(_ data: [UInt8], offset: Int, length: Int) -> UInt64 {
        var value: UInt64 = 0
        for i in 0..<length {
            value = (value << 1) | UInt64(getBit(data, offset + i))
        }
        return value
    }
}

// MARK: - Assisted OSNMA Open-Source REST Client DTOs

struct AssistedOsnmaGstResponse: Codable {
    let weekNumber: Int
    let towSeconds: Int
    let totalSeconds: Int64
    let utcIso: String
    let clockSource: String
    let isNetworkSynced: Bool
}

struct AssistedOsnmaMaterial: Codable {
    let merkleRootHex: String?
    let publicKeyId: Int?
    let publicKeyType: String?
    let publicKeyCompressedHex: String?
    let dsmKrootHex: String?
    let krootHex: String?
    let alphaHex: String?
    let wnKroot: Int?
    let tow0Kroot: Int?
    let keySizeBits: Int
    let tagSizeBits: Int
    let macltId: Int
    let nmaHeaderByte: UInt8?
    let source: String
}

struct AssistedOsnmaBitwiseRequest: Codable {
    let svid: Int
    let gstSeconds: Int64
    let osnmaHex: String
}

struct AssistedOsnmaBitwiseResult: Codable {
    let svid: Int
    let gstSeconds: Int64
    let hasReference: Bool
    let matched: Bool
    let matchingBits: Int
    let totalBits: Int
    let localOsnmaHex: String
    let referenceOsnmaHex: String?
    let reason: String
}

struct AssistedOsnmaIngestRequest: Codable {
    let svid: Int
    let gstSeconds: Int64
    let osnmaHex: String
}

/// Lightweight HTTP JSON client for TRISAT Assisted OSNMA (independent GST sync,
/// fast cold-start KROOT bootstrap, and 2-second 40-bit OSNMA bitwise anti-spoofing).
final class AssistedOsnmaClient {
    private let session: URLSession

    init(timeoutSeconds: TimeInterval = 4.0) {
        let cfg = URLSessionConfiguration.default
        cfg.timeoutIntervalForRequest = timeoutSeconds
        cfg.timeoutIntervalForResource = timeoutSeconds
        self.session = URLSession(configuration: cfg)
    }

    func fetchGst(baseUrl: String) async throws -> AssistedOsnmaGstResponse {
        let url = try makeUrl(baseUrl, path: "/api/osnma/gst")
        let (data, _) = try await session.data(from: url)
        return try JSONDecoder().decode(AssistedOsnmaGstResponse.self, from: data)
    }

    func fetchAssistMaterial(baseUrl: String) async throws -> AssistedOsnmaMaterial {
        let url = try makeUrl(baseUrl, path: "/api/osnma/assist")
        let (data, _) = try await session.data(from: url)
        return try JSONDecoder().decode(AssistedOsnmaMaterial.self, from: data)
    }

    func checkBitwise(baseUrl: String, svid: Int, gstSeconds: Int64, osnma40Hex: String) async throws -> AssistedOsnmaBitwiseResult {
        let url = try makeUrl(baseUrl, path: "/api/osnma/bitwise-check")
        var req = URLRequest(url: url)
        req.httpMethod = "POST"
        req.setValue("application/json", forHTTPHeaderField: "Content-Type")
        req.httpBody = try JSONEncoder().encode(
            AssistedOsnmaBitwiseRequest(svid: svid, gstSeconds: gstSeconds, osnmaHex: osnma40Hex)
        )
        let (data, _) = try await session.data(for: req)
        return try JSONDecoder().decode(AssistedOsnmaBitwiseResult.self, from: data)
    }

    func pushReferencePage(baseUrl: String, svid: Int, gstSeconds: Int64, osnma40Hex: String) async -> Bool {
        guard let url = try? makeUrl(baseUrl, path: "/api/osnma/ingest") else { return false }
        var req = URLRequest(url: url)
        req.httpMethod = "POST"
        req.setValue("application/json", forHTTPHeaderField: "Content-Type")
        guard let body = try? JSONEncoder().encode(
            AssistedOsnmaIngestRequest(svid: svid, gstSeconds: gstSeconds, osnmaHex: osnma40Hex)
        ) else { return false }
        req.httpBody = body
        guard let (_, resp) = try? await session.data(for: req),
              let http = resp as? HTTPURLResponse else { return false }
        return (200...299).contains(http.statusCode)
    }

    private func makeUrl(_ baseUrl: String, path: String) throws -> URL {
        let clean = baseUrl.trimmingCharacters(in: .whitespacesAndNewlines).trimmingCharacters(in: CharacterSet(charactersIn: "/"))
        guard let url = URL(string: clean + path) else {
            throw URLError(.badURL)
        }
        return url
    }
}

// MARK: - Core OSNMA Cryptographic Primitives & Annex A Self-Test

class OsnmaEngine {

    static let crc24qPoly: UInt32 = 0x1864CFB

    // Official Galileo Annex A Reference Constants (Configuration 2, Sat E02, WN=1248, TOW=345660)
    static let annexAKrootHex = "5BF8C9CBFCF70422081475FD445DF0FF"
    static let annexAK4Hex    = "69C00AA7364237A65EBF006AD8DDBC73"
    static let annexAAlphaHex = "610BDF26D77B"

    /// Computes Galileo 24-bit CRC-24Q (generator polynomial 0x1864CFB) over a byte buffer.
    static func computeCrc24q(_ bytes: [UInt8]) -> UInt32 {
        var crc: UInt32 = 0
        for b in bytes {
            crc ^= (UInt32(b) << 16)
            for _ in 0..<8 {
                crc <<= 1
                if (crc & 0x1000000) != 0 {
                    crc ^= crc24qPoly
                }
            }
        }
        return crc & 0xFFFFFF
    }

    /// Performs one step of the Galileo OSNMA TESLA chain one-way function:
    /// K_I = trunc(128, SHA256(K_{I+1} || GST_{SF,I} || alpha))
    static func computeTeslaKeyStep(nextKeyHex: String, wnTarget: UInt32, towTarget: UInt32, alphaHex: String) -> String {
        guard let kNext = hexToData(nextKeyHex),
              let alpha = hexToData(alphaHex) else { return "" }
        let gst32: UInt32 = ((wnTarget & 0xFFF) << 20) | (towTarget & 0xFFFFF)
        var gstBig = gst32.bigEndian
        let gstData = Data(bytes: &gstBig, count: 4)

        var input = Data()
        input.append(kNext)
        input.append(gstData)
        input.append(alpha)

        let digest = SHA256.hash(data: input)
        return digest.prefix(16).map { String(format: "%02X", $0) }.joined()
    }

    /// Runs the official Galileo Annex A TESLA & HMAC-SHA256 verification self-test using Apple CryptoKit.
    static func verifyEuspaAnnexATestVector() -> Bool {
        let k3 = computeTeslaKeyStep(nextKeyHex: annexAK4Hex, wnTarget: 1248, towTarget: 345660, alphaHex: annexAAlphaHex)
        let k2 = computeTeslaKeyStep(nextKeyHex: k3, wnTarget: 1248, towTarget: 345630, alphaHex: annexAAlphaHex)
        let k1 = computeTeslaKeyStep(nextKeyHex: k2, wnTarget: 1248, towTarget: 345600, alphaHex: annexAAlphaHex)
        let k0 = computeTeslaKeyStep(nextKeyHex: k1, wnTarget: 1248, towTarget: 345570, alphaHex: annexAAlphaHex)
        guard k0 == annexAKrootHex else { return false }

        // Verify Tag 2 (ADKD=4, CTR=3, PRN_D=2, PRN_A=2) == 0x7BB238C883
        guard let keyData = hexToData(annexAK4Hex),
              let msgData = hexToData("02024E05463C03BFFFFFFFC00000449238227897FDEFF93040") else { return false }
        let symKey = SymmetricKey(data: keyData)
        let mac = HMAC<SHA256>.authenticationCode(for: msgData, using: symKey)
        let tagHex = Data(mac).prefix(5).map { String(format: "%02X", $0) }.joined()
        return tagHex == "7BB238C883"
    }

    /// Reports reception when live pages have not yet been cryptographically or bitwise verified.
    func receptionStatus(inavMessages: [GalileoINavMessage]) -> OsnmaAuthStatus {
        return inavMessages.contains { !$0.data.isEmpty } ? .unverified : .notAvailable
    }

    private static func hexToData(_ hex: String) -> Data? {
        let clean = hex.trimmingCharacters(in: .whitespacesAndNewlines)
        guard clean.count % 2 == 0 else { return nil }
        var data = Data(capacity: clean.count / 2)
        var idx = clean.startIndex
        while idx < clean.endIndex {
            let next = clean.index(idx, offsetBy: 2)
            guard let byte = UInt8(clean[idx..<next], radix: 16) else { return nil }
            data.append(byte)
            idx = next
        }
        return data
    }
}
