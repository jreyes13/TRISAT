import Foundation

/// Data structure representing a captured Galileo I/NAV page payload
struct GalileoINavMessage: Identifiable, Hashable {
    let id = UUID()
    let svid: Int             // Satellite Vehicle ID (Galileo PRN, e.g., 1..36)
    let submessageId: Int     // Message type / I/NAV subpage ID
    let data: Data            // Raw byte payload
    let status: Int           // Frame status flag
    let timestamp: Date       // Arrival timestamp
    var osnma40Hex: String? = nil
    var crcValid: Bool = false
    var gstWeek: Int? = nil
    var gstTow: Int? = nil

    init(
        svid: Int,
        submessageId: Int,
        data: Data,
        status: Int,
        timestamp: Date,
        osnma40Hex: String? = nil,
        crcValid: Bool = false,
        gstWeek: Int? = nil,
        gstTow: Int? = nil
    ) {
        self.svid = svid
        self.submessageId = submessageId
        self.data = data
        self.status = status
        self.timestamp = timestamp
        self.osnma40Hex = osnma40Hex
        self.crcValid = crcValid
        self.gstWeek = gstWeek
        self.gstTow = gstTow
    }
    
    static func decodeHexPayload(_ hex: String) -> Data? {
        let clean = hex.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !clean.isEmpty, clean.count.isMultiple(of: 2) else { return nil }
        var data = Data()
        var index = clean.startIndex
        while index < clean.endIndex {
            let next = clean.index(index, offsetBy: 2)
            guard let byte = UInt8(clean[index..<next], radix: 16) else { return nil }
            data.append(byte)
            index = next
        }
        return data
    }

    /// Converts the byte payload to a hexadecimal string for UI display
    var hexPayload: String {
        data.map { String(format: "%02X", $0) }.joined()
    }
}