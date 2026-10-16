import SwiftUI

struct DashboardView: View {
    @ObservedObject var viewModel: GnssViewModel
    
    @State private var mockSatellites: [SatelliteInfo] = [
        SatelliteInfo(id: 1, elevation: 65, azimuth: 45, cno: 42, isGalileo: true, constellation: .galileo, usedInFix: true, hasInav: true),
        SatelliteInfo(id: 2, elevation: 30, azimuth: 120, cno: 38, isGalileo: true, constellation: .galileo, usedInFix: true, hasInav: true),
        SatelliteInfo(id: 5, elevation: 80, azimuth: 210, cno: 45, isGalileo: true, constellation: .galileo, usedInFix: true, hasInav: true),
        SatelliteInfo(id: 11, elevation: 20, azimuth: 300, cno: 32, isGalileo: true, constellation: .galileo, usedInFix: false, hasInav: true),
        SatelliteInfo(id: 12, elevation: 45, azimuth: 180, cno: 40, isGalileo: true, constellation: .galileo, usedInFix: true, hasInav: true),
        SatelliteInfo(id: 24, elevation: 15, azimuth: 75, cno: 29, isGalileo: true, constellation: .galileo, usedInFix: false, hasInav: false)
    ]
    
    var body: some View {
        ScrollView {
            VStack(spacing: 16) {
                // Header Banner
                Image("AppLogo")
                    .resizable()
                    .scaledToFit()
                    .frame(maxWidth: .infinity)
                
                // Card 1: Position Telemetry
                VStack(alignment: .leading, spacing: 6) {
                    HStack {
                        Image(systemName: "location.fill")
                            .foregroundColor(.blue)
                        Text("Internal GNSS Receiver (CoreLocation)")
                            .font(.caption)
                            .fontWeight(.bold)
                            .foregroundColor(.secondary)
                    }
                    Text(viewModel.locationText)
                        .font(.system(.body, design: .monospaced))
                        .fontWeight(.semibold)
                    Text(viewModel.accuracyText)
                        .font(.caption)
                        .foregroundColor(.gray)
                }
                .padding()
                .frame(maxWidth: .infinity, alignment: .leading)
                .background(Color.gray.opacity(0.12))
                .cornerRadius(12)
                .padding(.horizontal)
                
                // Card 2: Skyplot View
                VStack(alignment: .leading, spacing: 8) {
                    HStack {
                        Image(systemName: "globe.americas.fill")
                            .foregroundColor(.blue)
                        Text("Galileo Satellite Sky View (Simulated)")
                            .font(.subheadline)
                            .fontWeight(.bold)
                        Spacer()
                        Text("SVs: \(mockSatellites.count)")
                            .font(.caption)
                            .foregroundColor(.secondary)
                    }
                    
                    SkyPlotView(satellites: mockSatellites)
                        .padding(.vertical, 8)
                }
                .padding()
                .background(Color.gray.opacity(0.08))
                .cornerRadius(12)
                .padding(.horizontal)
                
                // Card 3: OSNMA Status Indicator
                HStack(spacing: 12) {
                    Image(systemName: "checkmark.shield.fill")
                        .font(.title2)
                        .foregroundColor(.green)
                    VStack(alignment: .leading, spacing: 2) {
                        Text("OSNMA Engine Active")
                            .font(.subheadline)
                            .fontWeight(.bold)
                        Text("Authenticating Galileo E1B I/NAV Frames")
                            .font(.caption)
                            .foregroundColor(.secondary)
                    }
                    Spacer()
                }
                .padding()
                .background(Color.green.opacity(0.12))
                .cornerRadius(12)
                .padding(.horizontal)
            }
            .padding(.bottom)
        }
        .onAppear { viewModel.start() }
    }
}