import Foundation

/// Data structure representing a captured Galileo I/NAV or E5b/E6 page payload
struct GalileoINavMessage: Identifiable, Hashable {
    let id = UUID()
    let svid: Int             // Satellite Vehicle ID (Galileo PRN, e.g., 1..36)
    let submessageId: Int     // Message type / I/NAV subpage ID
    let data: Data            // Raw byte payload
    let status: Int           // Frame status flag
    let timestamp: Date       // Arrival timestamp
    var band: String = "E1-B" // "E1-B" (OSNMA), "E5b-I" (Dual-Band I/NAV), "E6-B" (HAS)

    init(
        svid: Int,
        submessageId: Int,
        data: Data,
        status: Int,
        timestamp: Date,
        band: String = "E1-B"
    ) {
        self.svid = svid
        self.submessageId = submessageId
        self.data = data
        self.status = status
        self.timestamp = timestamp
        self.band = band
    }

    /// Converts the byte payload to a hexadecimal string for UI display
    var hexPayload: String {
        data.map { String(format: "%02X", $0) }.joined()
    }
}
