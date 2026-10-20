import SwiftUI

/// Enumeración para identificar la constelación GNSS
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

/// Representa un satélite en la bóveda celeste con ID único garantizado
struct SatelliteInfo: Identifiable {
    let id: String                // Unique Identifier (e.g., "Galileo_01", "GPS_12")
    let svid: Int                 // PRN / SVID number (1...36)
    let elevation: Double         // Elevación en grados (0° en horizonte, 90° en cénit)
    let azimuth: Double           // Acimut en grados (0° a 360°, relativo al Norte)
    let cno: Int                  // Relación señal-ruido C/N0 (en dB-Hz)
    let isGalileo: Bool           // Flag rápido para Galileo
    let constellation: GnssConstellation // Constelación asociada
    let usedInFix: Bool           // Si el satélite participa en la solución PVT
    let hasInav: Bool             // Si dispone de tramas I/NAV para OSNMA

    var formattedLabel: String {
        switch constellation {
        case .galileo: return "E\(String(format: "%02d", svid))"
        case .gps: return "G\(String(format: "%02d", svid))"
        case .glonass: return "R\(String(format: "%02d", svid))"
        case .beidou: return "B\(String(format: "%02d", svid))"
        }
    }
}

/// Vista personalizada que renderiza la bóveda celeste (Skyplot polar)
struct SkyPlotView: View {
    let satellites: [SatelliteInfo]
    var height: CGFloat = 220

    var body: some View {
        GeometryReader { geometry in
            let center = CGPoint(x: geometry.size.width / 2, y: geometry.size.height / 2)
            let radius = min(geometry.size.width, geometry.size.height) / 2 - 20
            
            ZStack {
                // 1. Círculos concéntricos de elevación (0°, 30°, 60°)
                Circle()
                    .stroke(Color.gray.opacity(0.3), lineWidth: 1.5)
                    .frame(width: radius * 2, height: radius * 2)
                
                Circle()
                    .stroke(Color.gray.opacity(0.2), style: StrokeStyle(lineWidth: 1, dash: [4]))
                    .frame(width: radius * 4/3, height: radius * 4/3)
                
                Circle()
                    .stroke(Color.gray.opacity(0.2), style: StrokeStyle(lineWidth: 1, dash: [4]))
                    .frame(width: radius * 2/3, height: radius * 2/3)
                
                // 2. Líneas de los puntos cardinales (N-S, E-O)
                Path { path in
                    path.move(to: CGPoint(x: center.x, y: center.y - radius))
                    path.addLine(to: CGPoint(x: center.x, y: center.y + radius))
                    path.move(to: CGPoint(x: center.x - radius, y: center.y))
                    path.addLine(to: CGPoint(x: center.x + radius, y: center.y))
                }
                .stroke(Color.gray.opacity(0.3), lineWidth: 1)
                
                // Labels de Puntos Cardinales
                Text("N").font(.caption2).bold().position(x: center.x, y: center.y - radius - 12)
                Text("S").font(.caption2).bold().position(x: center.x, y: center.y + radius + 12)
                Text("E").font(.caption2).bold().position(x: center.x + radius + 12, y: center.y)
                Text("W").font(.caption2).bold().position(x: center.x - radius - 12, y: center.y)
                
                // 3. Dibujar cada satélite en su posición polar como Círculo simple unificado
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
