import SwiftUI

/// Enumeración para identificar la constelación GNSS
enum GnssConstellation: String, CaseIterable {
    case galileo = "Galileo"
    case gps = "GPS"
    case glonass = "GLONASS"
    case beidou = "BeiDou"
}

/// Representa un satélite en la bóveda celeste con metadatos completos
struct SatelliteInfo: Identifiable {
    let id: Int                   // PRN / SVID (ej. 1 para E01)
    let elevation: Double         // Elevación en grados (0° en horizonte, 90° en cénit)
    let azimuth: Double           // Acimut en grados (0° a 360°, relativo al Norte)
    let cno: Int                  // Relación señal-ruido C/N0 (en dB-Hz)
    let isGalileo: Bool           // Flag rápido para Galileo
    let constellation: GnssConstellation // Constelación asociada
    let usedInFix: Bool           // Si el satélite participa en la solución PVT
    let hasInav: Bool             // Si dispone de tramas I/NAV para OSNMA
}

/// Vista personalizada que renderiza la bóveda celeste (Skyplot polar)
struct SkyPlotView: View {
    let satellites: [SatelliteInfo]
    
    var body: some View {
        GeometryReader { geometry in
            let center = CGPoint(x: geometry.size.width / 2, y: geometry.size.height / 2)
            let radius = min(geometry.size.width, geometry.size.height) / 2 - 20
            
            ZStack {
                // 1. Círculos concéntricos de elevación (0°, 30°, 60°)
                Circle()
                    .stroke(Color.gray.opacity(0.3), lineWidth: 1)
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
                
                // 3. Dibujar cada satélite en su posición polar
                ForEach(satellites) { sat in
                    let point = pointForSatellite(sat, center: center, radius: radius)
                    
                    ZStack {
                        Circle()
                            .fill(sat.isGalileo ? (sat.hasInav ? Color.blue : Color.cyan) : Color.gray)
                            .frame(width: 22, height: 22)
                            .shadow(radius: sat.usedInFix ? 3 : 1)
                        
                        Text("E\(String(format: "%02d", sat.id))")
                            .font(.system(size: 8, weight: .bold, design: .monospaced))
                            .foregroundColor(.white)
                    }
                    .position(point)
                }
            }
        }
        .frame(height: 220)
    }
    
    /// Convierte la elevación y acimut a coordenadas cartesianas (X, Y)
    private func pointForSatellite(_ sat: SatelliteInfo, center: CGPoint, radius: CGFloat) -> CGPoint {
        let r = radius * CGFloat(90.0 - sat.elevation) / 90.0
        let rad = (sat.azimuth - 90.0) * .pi / 180.0
        
        let x = center.x + r * cos(rad)
        let y = center.y + r * sin(rad)
        
        return CGPoint(x: x, y: y)
    }
}