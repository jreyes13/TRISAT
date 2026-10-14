import SwiftUI

struct ContentView: View {
    @StateObject private var viewModel: GnssViewModel
    
    // Generación de satélites Galileo simulados para la bóveda celeste
    @State private var mockSatellites: [SatelliteInfo] = [
        SatelliteInfo(id: 1, elevation: 65, azimuth: 45, cno: 42, isGalileo: true),
        SatelliteInfo(id: 2, elevation: 30, azimuth: 120, cno: 38, isGalileo: true),
        SatelliteInfo(id: 5, elevation: 80, azimuth: 210, cno: 45, isGalileo: true),
        SatelliteInfo(id: 11, elevation: 20, azimuth: 300, cno: 32, isGalileo: true),
        SatelliteInfo(id: 12, elevation: 45, azimuth: 180, cno: 40, isGalileo: true),
        SatelliteInfo(id: 24, elevation: 15, azimuth: 75, cno: 29, isGalileo: true)
    ]
    
    init() {
        _viewModel = StateObject(wrappedValue: GnssViewModel())
    }
    
    var body: some View {
        NavigationView {
            ScrollView {
                VStack(spacing: 16) {
                    // Header Banner: AppLogo occupying full width
                    Image("AppLogo")
                        .resizable()
                        .scaledToFit()
                        .frame(maxWidth: .infinity)
                        .padding(.horizontal, 0)
                    
                    // Card 1: Position Telemetry (Internal iPhone Chip)
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
                    
                    // Card 2: Skyplot (Satellite Constellation View)
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
                        Image(systemName: "shield.trianglebadge.exclamationmark")
                            .font(.title2)
                            .foregroundColor(.orange)
                        VStack(alignment: .leading, spacing: 2) {
                            Text("OSNMA Status: Standby Mode")
                                .font(.subheadline)
                                .fontWeight(.bold)
                            Text("Awaiting I/NAV stream from external receiver")
                                .font(.caption)
                                .foregroundColor(.secondary)
                        }
                        Spacer()
                    }
                    .padding()
                    .background(Color.orange.opacity(0.12))
                    .cornerRadius(12)
                    .padding(.horizontal)
                    
                    // Card 4: Galileo I/NAV Live Feed
                    VStack(alignment: .leading, spacing: 8) {
                        HStack {
                            Text("Galileo I/NAV Live Feed")
                                .font(.headline)
                            Spacer()
                            Text("Frames: \(viewModel.messages.count)")
                                .font(.subheadline)
                                .foregroundColor(.secondary)
                        }
                        .padding(.horizontal)
                        
                        Divider()
                        
                        LazyVStack(spacing: 8) {
                            ForEach(viewModel.messages.reversed()) { msg in
                                INavMessageCard(message: msg)
                            }
                        }
                        .padding(.horizontal)
                    }
                }
                .padding(.bottom)
            }
            .navigationBarHidden(true)
            .onAppear {
                viewModel.start()
            }
            .onDisappear {
                viewModel.stop()
            }
        }
    }
}

// Card view for displaying each captured I/NAV frame
struct INavMessageCard: View {
    let message: GalileoINavMessage
    
    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack {
                Text("SVID (PRN): Galileo E\(String(format: "%02d", message.svid))")
                    .font(.subheadline)
                    .fontWeight(.bold)
                Spacer()
                Text(message.timestamp, style: .time)
                    .font(.caption2)
                    .foregroundColor(.secondary)
            }
            
            Text("Submessage ID: \(message.submessageId)")
                .font(.caption)
                .foregroundColor(.secondary)
            
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