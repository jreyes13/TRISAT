import SwiftUI

/// Enumeration identifying the GNSS constellation
enum GnssConstellation: String, CaseIterable, Codable {
    case galileo = "Galileo"
    case gps = "GPS"
    case glonass = "GLONASS"
    case beidou = "BeiDou"

    var color: Color {
        switch self {
        case .galileo: return Color(red: 0.0, green: 0.9, blue: 1.0)  // Cyan
        case .gps: return Color(red: 1.0, green: 0.72, blue: 0.0)      // Amber
        case .glonass: return Color(red: 1.0, green: 0.0, blue: 0.33)  // Red
        case .beidou: return Color(red: 0.22, green: 0.7, blue: 0.0)   // Green
        }
    }
}

/// Represents a satellite in the celestial sphere with a guaranteed unique ID
struct SatelliteInfo: Identifiable {
    let id: String                // Unique Identifier (e.g., "Galileo_PRN_01_0", "GPS_PRN_12_1")
    let svid: Int                 // PRN / SVID number (1...36)
    let elevation: Double         // Elevation in degrees (0° at horizon, 90° at zenith)
    let azimuth: Double           // Azimuth in degrees (0° to 360°, clockwise from True North)
    let cno: Int                  // Carrier-to-Noise Ratio C/N0 (in dB-Hz)
    let isGalileo: Bool           // Quick flag for Galileo
    let constellation: GnssConstellation // Associated constellation
    let usedInFix: Bool           // Whether satellite is used in PVT solution
    let hasInav: Bool             // Whether I/NAV frames are present for OSNMA

    var formattedLabel: String {
        switch constellation {
        case .galileo: return "E\(String(format: "%02d", svid))"
        case .gps: return "G\(String(format: "%02d", svid))"
        case .glonass: return "R\(String(format: "%02d", svid))"
        case .beidou: return "B\(String(format: "%02d", svid))"
        }
    }
}

/// Custom view rendering the celestial dome (polar Skyplot)
struct SkyPlotView: View {
    let satellites: [SatelliteInfo]
    var height: CGFloat = 220
    var isAwaitingLocation: Bool = false
    var isAwaitingTle: Bool = false

    var body: some View {
        GeometryReader { geometry in
            let center = CGPoint(x: geometry.size.width / 2, y: geometry.size.height / 2)
            let radius = min(geometry.size.width, geometry.size.height) / 2 - 20
            
            ZStack {
                // 1. Concentric elevation circles (0°, 30°, 60°)
                Circle()
                    .stroke(Color.gray.opacity(0.3), lineWidth: 1.5)
                    .frame(width: radius * 2, height: radius * 2)
                
                Circle()
                    .stroke(Color.gray.opacity(0.2), style: StrokeStyle(lineWidth: 1, dash: [4]))
                    .frame(width: radius * 4/3, height: radius * 4/3)
                
                Circle()
                    .stroke(Color.gray.opacity(0.2), style: StrokeStyle(lineWidth: 1, dash: [4]))
                    .frame(width: radius * 2/3, height: radius * 2/3)
                
                // 2. Cardinal direction axes (N-S, E-W)
                Path { path in
                    path.move(to: CGPoint(x: center.x, y: center.y - radius))
                    path.addLine(to: CGPoint(x: center.x, y: center.y + radius))
                    path.move(to: CGPoint(x: center.x - radius, y: center.y))
                    path.addLine(to: CGPoint(x: center.x + radius, y: center.y))
                }
                .stroke(Color.gray.opacity(0.3), lineWidth: 1)
                
                // Cardinal Direction Labels
                Text("N").font(.caption2).bold().position(x: center.x, y: center.y - radius - 12)
                Text("S").font(.caption2).bold().position(x: center.x, y: center.y + radius + 12)
                Text("E").font(.caption2).bold().position(x: center.x + radius + 12, y: center.y)
                Text("W").font(.caption2).bold().position(x: center.x - radius - 12, y: center.y)
                
                // 3. Draw each satellite in its polar position as a unified circle
                ForEach(satellites, id: \.id) { sat in
                    let point = pointForSatellite(sat, center: center, radius: radius)
                    
                    ZStack {
                        Circle()
                            .stroke(sat.constellation.color, lineWidth: 2)
                            .background(Circle().fill(sat.constellation.color.opacity(0.35)))
                            .frame(width: 20, height: 20)

                        Text(sat.formattedLabel)
                            .font(.system(size: 8, weight: .bold, design: .monospaced))
                            .foregroundColor(.white)
                            .offset(y: 16)
                    }
                    .position(point)
                }

                // 4. Empty State Informative HUD Overlay
                if satellites.isEmpty {
                    VStack(spacing: 6) {
                        if isAwaitingLocation {
                            Image(systemName: "location.slash")
                                .font(.system(size: 24))
                                .foregroundColor(.orange)
                            Text("Awaiting Location Fix")
                                .font(.caption)
                                .fontWeight(.bold)
                                .foregroundColor(.primary)
                            Text("Coordinates required for orbital projection")
                                .font(.system(size: 9))
                                .foregroundColor(.secondary)
                        } else if isAwaitingTle {
                            Image(systemName: "tray.and.arrow.down")
                                .font(.system(size: 24))
                                .foregroundColor(.cyan)
                            Text("No TLE Orbits Loaded")
                                .font(.caption)
                                .fontWeight(.bold)
                                .foregroundColor(.primary)
                            Text("Download ephemeris in Settings")
                                .font(.system(size: 9))
                                .foregroundColor(.secondary)
                        } else {
                            Image(systemName: "circle.dashed")
                                .font(.system(size: 22))
                                .foregroundColor(.secondary)
                            Text("No Satellites Above 10° Mask")
                                .font(.caption)
                                .fontWeight(.bold)
                                .foregroundColor(.secondary)
                        }
                    }
                    .padding(12)
                    .background(Color.black.opacity(0.65))
                    .cornerRadius(10)
                    .position(center)
                }
            }
        }
        .frame(height: height)
    }
    
    private func pointForSatellite(_ sat: SatelliteInfo, center: CGPoint, radius: CGFloat) -> CGPoint {
        let r = radius * CGFloat(90.0 - sat.elevation) / 90.0
        let rad = (sat.azimuth - 90.0) * .pi / 180.0
        
        let x = center.x + r * cos(rad)
        let y = center.y + r * sin(rad)
        
        return CGPoint(x: x, y: y)
    }
}
