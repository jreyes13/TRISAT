import SwiftUI

struct ContentView: View {
    @StateObject private var viewModel: GnssViewModel
    
    // Lista de satélites simulados con todos los parámetros requeridos y SVID único
    @State private var mockSatellites: [SatelliteInfo] = [
        SatelliteInfo(id: "Galileo_01", svid: 1, elevation: 65, azimuth: 45, cno: 42, isGalileo: true, constellation: .galileo, usedInFix: true, hasInav: true),
        SatelliteInfo(id: "Galileo_02", svid: 2, elevation: 30, azimuth: 120, cno: 38, isGalileo: true, constellation: .galileo, usedInFix: true, hasInav: true),
        SatelliteInfo(id: "Galileo_05", svid: 5, elevation: 80, azimuth: 210, cno: 45, isGalileo: true, constellation: .galileo, usedInFix: true, hasInav: true),
        SatelliteInfo(id: "Galileo_11", svid: 11, elevation: 20, azimuth: 300, cno: 32, isGalileo: true, constellation: .galileo, usedInFix: false, hasInav: true),
        SatelliteInfo(id: "GPS_12", svid: 12, elevation: 45, azimuth: 180, cno: 40, isGalileo: false, constellation: .gps, usedInFix: true, hasInav: false),
        SatelliteInfo(id: "BeiDou_24", svid: 24, elevation: 15, azimuth: 75, cno: 29, isGalileo: false, constellation: .beidou, usedInFix: false, hasInav: false)
    ]
    
    init() {
        _viewModel = StateObject(wrappedValue: GnssViewModel())
    }
    
    var body: some View {
        MainTabView()
    }
}
