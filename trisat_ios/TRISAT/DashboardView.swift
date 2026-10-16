import SwiftUI

struct DashboardView: View {
    @ObservedObject var viewModel: GnssViewModel
    @State private var isCopied: Bool = false

    @State private var mockSatellites: [SatelliteInfo] = [
        SatelliteInfo(id: 1, elevation: 65, azimuth: 45, cno: 42, constellation: .galileo, usedInFix: true, hasInav: true),
        SatelliteInfo(id: 2, elevation: 30, azimuth: 120, cno: 38, constellation: .galileo, usedInFix: true, hasInav: true),
        SatelliteInfo(id: 5, elevation: 80, azimuth: 210, cno: 45, constellation: .galileo, usedInFix: true, hasInav: true),
        SatelliteInfo(id: 12, elevation: 45, azimuth: 180, cno: 40, constellation: .gps, usedInFix: true, hasInav: false),
        SatelliteInfo(id: 8, elevation: 25, azimuth: 300, cno: 35, constellation: .glonass, usedInFix: false, hasInav: false),
        SatelliteInfo(id: 24, elevation: 15, azimuth: 75, cno: 29, constellation: .beidou, usedInFix: false, hasInav: false)
    ]
    
    var body: some View {
        NavigationView {
            ScrollView {
                VStack(spacing: 16) {
                    // Banner Image
                    Image("AppLogo")
                        .resizable()
                        .scaledToFit()
                        .frame(maxWidth: .infinity)
                    
                    // Card 1: Open Sky Satellite View (Skyplot)
                    VStack(alignment: .leading, spacing: 8) {
                        HStack {
                            Image(systemName: "globe.americas.fill")
                                .foregroundColor(.cyan)
                            Text("Open Sky Satellite View")
                                .font(.subheadline)
                                .fontWeight(.bold)
                            Spacer()
                            Text("Primary: Galileo")
                                .font(.caption)
                                .foregroundColor(.cyan)
                                .fontWeight(.semibold)
                        }

                        SkyPlotView(satellites: mockSatellites)
                            .padding(.vertical, 8)
                    }
                    .padding()
                    .background(Color.gray.opacity(0.08))
                    .cornerRadius(12)
                    .padding(.horizontal)
                    
                    // Card 2: Position Telemetry & Navigator Exporter
                    VStack(alignment: .leading, spacing: 10) {
                        HStack {
                            Image(systemName: "location.fill")
                                .foregroundColor(.blue)
                            Text("Authenticated Position & Navigator")
                                .font(.subheadline)
                                .fontWeight(.bold)
                            Spacer()
                            Text(isCopied ? "COPIED!" : "3D FIX")
                                .font(.caption2)
                                .fontWeight(.bold)
                                .padding(.horizontal, 6)
                                .padding(.vertical, 2)
                                .background(isCopied ? Color.green.opacity(0.2) : Color.blue.opacity(0.2))
                                .foregroundColor(isCopied ? .green : .blue)
                                .cornerRadius(6)
                        }
                        
                        Text(viewModel.locationText)
                            .font(.system(.body, design: .monospaced))
                            .fontWeight(.semibold)

                        Text(viewModel.accuracyText)
                            .font(.caption)
                            .foregroundColor(.gray)

                        HStack(spacing: 10) {
                            Button(action: {
                                UIPasteboard.general.string = viewModel.locationText
                                isCopied = true
                                DispatchQueue.main.asyncAfter(deadline: .now() + 2) {
                                    isCopied = false
                                }
                            }) {
                                HStack {
                                    Image(systemName: "doc.on.doc")
                                    Text("Copy Coords")
                                }
                                .font(.caption)
                                .fontWeight(.bold)
                                .frame(maxWidth: .infinity)
                                .padding(.vertical, 8)
                                .background(Color.blue)
                                .foregroundColor(.white)
                                .cornerRadius(8)
                            }

                            Button(action: {
                                if let url = URL(string: "https://maps.apple.com/?q=\(viewModel.locationText.addingPercentEncoding(withAllowedCharacters: .urlQueryAllowed) ?? "")") {
                                    UIApplication.shared.open(url)
                                }
                            }) {
                                HStack {
                                    Image(systemName: "map")
                                    Text("Apple Maps")
                                }
                                .font(.caption)
                                .fontWeight(.bold)
                                .frame(maxWidth: .infinity)
                                .padding(.vertical, 8)
                                .background(Color.gray.opacity(0.2))
                                .foregroundColor(.primary)
                                .cornerRadius(8)
                            }
                        }
                    }
                    .padding()
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .background(Color.gray.opacity(0.12))
                    .cornerRadius(12)
                    .padding(.horizontal)
                    
                    // Card 3: Quick OSNMA Status
                    HStack(spacing: 12) {
                        Image(systemName: "checkmark.shield.fill")
                            .font(.title2)
                            .foregroundColor(.green)
                        VStack(alignment: .leading, spacing: 2) {
                            Text("Galileo OSNMA Engine Active")
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
            .navigationBarHidden(true)
            .onAppear { viewModel.start() }
        }
    }
}
