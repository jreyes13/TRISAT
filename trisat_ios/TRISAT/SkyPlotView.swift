import SwiftUI

enum ConstellationKind {
    case galileo
    case gps
    case glonass
    case beidou

    var color: Color {
        switch self {
        case .galileo: return Color(red: 0.0, green: 0.9, blue: 1.0) // Cyan
        case .gps: return Color(red: 1.0, green: 0.7, blue: 0.0)     // Amber
        case .glonass: return Color(red: 1.0, green: 0.2, blue: 0.3) // Red
        case .beidou: return Color(red: 0.2, green: 0.8, blue: 0.2)  // Green
        }
    }
}

/// Representa un satélite en la bóveda celeste
struct SatelliteInfo: Identifiable {
    let id: Int           // SVID
    let elevation: Double // Elevación en grados (0° - 90°)
    let azimuth: Double   // Acimut en grados (0° - 360°)
    let cno: Int          // Relación C/N0 (dB-Hz)
    let constellation: ConstellationKind
    let usedInFix: Bool
    let hasInav: Bool

    var formattedLabel: String {
        switch constellation {
        case .galileo: return "E\(String(format: "%02d", id))"
        case .gps: return "G\(String(format: "%02d", id))"
        case .glonass: return "R\(String(format: "%02d", id))"
        case .beidou: return "B\(String(format: "%02d", id))"
        }
    }
}

/// Vista de Bóveda Celeste Polar para iOS (SwiftUI)
struct SkyPlotView: View {
    let satellites: [SatelliteInfo]
    
    var body: some View {
        GeometryReader { geometry in
            let center = CGPoint(x: geometry.size.width / 2, y: geometry.size.height / 2)
            let radius = min(geometry.size.width, geometry.size.height) / 2 - 20
            
            ZStack {
                // 1. Concentric elevation rings
                Circle()
                    .stroke(Color.gray.opacity(0.3), lineWidth: 1.5)
                    .frame(width: radius * 2, height: radius * 2)
                
                Circle()
                    .stroke(Color.gray.opacity(0.2), style: StrokeStyle(lineWidth: 1, dash: [4]))
                    .frame(width: radius * 4/3, height: radius * 4/3)
                
                Circle()
                    .stroke(Color.gray.opacity(0.2), style: StrokeStyle(lineWidth: 1, dash: [4]))
                    .frame(width: radius * 2/3, height: radius * 2/3)
                
                // 2. Cardinal Crosshairs
                Path { path in
                    path.move(to: CGPoint(x: center.x, y: center.y - radius))
                    path.addLine(to: CGPoint(x: center.x, y: center.y + radius))
                    path.move(to: CGPoint(x: center.x - radius, y: center.y))
                    path.addLine(to: CGPoint(x: center.x + radius, y: center.y))
                }
                .stroke(Color.gray.opacity(0.3), lineWidth: 1)
                
                // Cardinal Labels
                Text("N").font(.caption2).bold().position(x: center.x, y: center.y - radius - 12)
                Text("S").font(.caption2).bold().position(x: center.x, y: center.y + radius + 12)
                Text("E").font(.caption2).bold().position(x: center.x + radius + 12, y: center.y)
                Text("W").font(.caption2).bold().position(x: center.x - radius - 12, y: center.y)
                
                // 3. Satellites
                ForEach(satellites) { sat in
                    let point = pointForSatellite(sat, center: center, radius: radius)
                    
                    ZStack {
                        if sat.usedInFix {
                            // Square symbol
                            Rectangle()
                                .stroke(sat.constellation.color, lineWidth: 2)
                                .background(sat.constellation.color.opacity(0.25))
                                .frame(width: 20, height: 20)

                            if sat.hasInav {
                                Rectangle()
                                    .fill(sat.constellation.color)
                                    .frame(width: 8, height: 8)
                            }
                        } else {
                            // Circle symbol
                            Circle()
                                .stroke(sat.constellation.color, lineWidth: 2)
                                .background(sat.constellation.color.opacity(0.25))
                                .frame(width: 20, height: 20)

                            if sat.hasInav {
                                Circle()
                                    .fill(sat.constellation.color)
                                    .frame(width: 8, height: 8)
                            }
                        }
                        
                        Text(sat.formattedLabel)
                            .font(.system(size: 8, weight: .bold, design: .monospaced))
                            .foregroundColor(.white)
                            .offset(y: 16)
                    }
                    .position(point)
                }
            }
        }
        .frame(height: 220)
    }
    
    private func pointForSatellite(_ sat: SatelliteInfo, center: CGPoint, radius: CGFloat) -> CGPoint {
        let r = radius * CGFloat(90.0 - sat.elevation) / 90.0
        let rad = (sat.azimuth - 90.0) * .pi / 180.0
        let x = center.x + r * cos(rad)
        let y = center.y + r * sin(rad)
        return CGPoint(x: x, y: y)
    }
}
