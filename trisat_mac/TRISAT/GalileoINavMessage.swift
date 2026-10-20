import Foundation

/// Data structure representing a captured Galileo I/NAV page payload
struct GalileoINavMessage: Identifiable, Hashable {
    let id = UUID()
    let svid: Int             // Satellite Vehicle ID (Galileo PRN, e.g., 1..36)
    let submessageId: Int     // Message type / I/NAV subpage ID
    let data: Data            // Raw byte payload
    let status: Int           // Frame status flag
    let timestamp: Date       // Arrival timestamp
    
    /// Converts the byte payload to a hexadecimal string for UI display
    var hexPayload: String {
        data.map { String(format: "%02X", $0) }.joined()
    }
}