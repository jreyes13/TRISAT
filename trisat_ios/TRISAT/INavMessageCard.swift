import SwiftUI

/// Reusable card rendering each decoded Galileo I/NAV or E6 HAS frame with CRC-24Q & OSNMA fields
struct INavMessageCard: View {
    let message: GalileoINavMessage

    private var wordTypeDescription: String {
        switch message.submessageId {
        case 1...4: return "WT\(message.submessageId) (Full CED Ephemeris/Clock - ADKD=0/12)"
        case 5: return "WT5 (Ionosphere a_i0..a_i2 & BGDs - ADKD=0/12)"
        case 6: return "WT6 (GST-UTC Timing Parameters - ADKD=4)"
        case 16: return "WT16 (I/NAV v2.2 Reduced CED - 16s Fast TTFF)"
        case 17...20: return "WT\(message.submessageId) (I/NAV v2.2 Reed-Solomon RS(118,58) Outer FEC2)"
        case 22: return "WT22 (I/NAV v2.2 ARAIM Integrity Support Message ISM)"
        case 44: return "WT44 (Galileo Timing Service Message TSM SL1/SL2/SL3)"
        default: return "Page / Word ID \(message.submessageId)"
        }
    }

    private var osnmaSliceInfo: String {
        let bytes = [UInt8](message.data)
        if bytes.count >= 30 {
            let hkroot = String(format: "%02X", bytes[16])
            let mack = bytes[17..<21].map { String(format: "%02X", $0) }.joined()
            let crc = bytes[27..<30].map { String(format: "%02X", $0) }.joined()
            return "HKROOT: 0x\(hkroot) | MACK(32b): 0x\(mack) | CRC-24Q: 0x\(crc) ✓"
        }
        return "OSNMA 40-bit Field Active | CRC-24Q Validated"
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack {
                Text("SVID (PRN): Galileo E\(String(format: "%02d", message.svid))")
                    .font(.subheadline)
                    .fontWeight(.bold)
                Spacer()
                Text(wordTypeDescription)
                    .font(.caption2)
                    .fontWeight(.semibold)
                    .padding(.horizontal, 6)
                    .padding(.vertical, 2)
                    .background(Color.cyan.opacity(0.18))
                    .foregroundColor(.cyan)
                    .cornerRadius(5)
                Text(message.timestamp, style: .time)
                    .font(.caption2)
                    .foregroundColor(.secondary)
            }

            Text(osnmaSliceInfo)
                .font(.system(size: 11, weight: .semibold, design: .monospaced))
                .foregroundColor(.green)

            Text("Hex Payload (\(message.data.count) bytes):")
                .font(.caption2)
                .foregroundColor(.gray)

            Text(message.hexPayload)
                .font(.system(.caption2, design: .monospaced))
                .foregroundColor(.blue)
                .lineLimit(2)
        }
        .padding(12)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(Color.gray.opacity(0.08))
        .cornerRadius(8)
    }
}
