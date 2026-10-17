import SwiftUI

struct SettingsView: View {
    @ObservedObject var bluetoothManager: BluetoothManager
    @ObservedObject var viewModel: GnssViewModel
    @Environment(\.horizontalSizeClass) var horizontalSizeClass
    
    var body: some View {
        NavigationView {
            Group {
                if horizontalSizeClass == .regular {
                    // iPad 2-Column Centered Layout with Live SkyPlot Preview
                    HStack(alignment: .top, spacing: 20) {
                        // Left Column: Settings Form
                        Form {
                            receiverModeSection
                            tleDownloadSection
                            osnmaSection
                            aboutSection
                        }
                        .frame(maxWidth: .infinity)

                        // Right Column: Live TLE Simulation Skyplot Preview Card
                        VStack(alignment: .leading, spacing: 14) {
                            HStack {
                                Image(systemName: "globe.americas.fill")
                                    .foregroundColor(.cyan)
                                Text("Live Orbit Simulation Preview")
                                    .font(.headline)
                                    .fontWeight(.bold)
                                Spacer()
                                Text("\(viewModel.displayedSatellites.count) SATS")
                                    .font(.caption2)
                                    .fontWeight(.bold)
                                    .padding(.horizontal, 6)
                                    .padding(.vertical, 2)
                                    .background(Color.green.opacity(0.2))
                                    .foregroundColor(.green)
                                    .cornerRadius(6)
                            }

                            SkyPlotView(satellites: viewModel.displayedSatellites, height: 320)
                                .padding(.vertical, 8)

                            VStack(alignment: .leading, spacing: 6) {
                                Text("Orbit Offset: \(String(format: "%.1f", viewModel.timeOffsetHours)) Hours")
                                    .font(.caption)
                                    .fontWeight(.bold)
                                    .foregroundColor(.blue)

                                Slider(value: $viewModel.timeOffsetHours, in: -12.0...12.0, step: 0.5) { _ in
                                    viewModel.updateSatellites()
                                }
                            }
                            .padding(10)
                            .background(Color.blue.opacity(0.1))
                            .cornerRadius(10)
                        }
                        .padding(20)
                        .background(Color.gray.opacity(0.08))
                        .cornerRadius(16)
                        .frame(maxWidth: .infinity)
                    }
                    .padding()
                } else {
                    // iPhone Centered Form Layout
                    Form {
                        receiverModeSection
                        tleDownloadSection
                        osnmaSection
                        aboutSection
                    }
                }
            }
            .navigationTitle("Settings & Configuration")
        }
        .navigationViewStyle(.stack)
    }

    // MARK: - Form Sections

    private var receiverModeSection: some View {
        Section(header: Text("GNSS Receiver Mode")) {
            Picker("Receiver Mode", selection: $viewModel.selectedMode) {
                Text("Internal").tag(0)
                Text("External BLE").tag(1)
                Text("TLE Simulation").tag(2)
            }
            .pickerStyle(.segmented)
            .onChange(of: viewModel.selectedMode) { _ in
                viewModel.updateSatellites()
            }

            if viewModel.selectedMode == 1 {
                HStack {
                    Text("Bluetooth Status")
                    Spacer()
                    Text(bluetoothManager.isBluetoothOn ? "Active" : "Disabled")
                        .foregroundColor(bluetoothManager.isBluetoothOn ? .green : .red)
                }

                Button(action: {
                    bluetoothManager.startScanning()
                }) {
                    HStack {
                        Image(systemName: "antenna.radiowaves.left.and.right")
                        Text(bluetoothManager.isScanning ? "Scanning BLE Receivers..." : "Scan BLE Receivers")
                    }
                }
            }
        }
    }

    private var tleDownloadSection: some View {
        Section(header: Text("CelesTrak TLE Orbital Data")) {
            VStack(alignment: .leading, spacing: 8) {
                HStack {
                    Text("Status:")
                        .font(.caption)
                        .foregroundColor(.secondary)
                    Spacer()
                    Text(viewModel.tleDownloader.downloadStatusText)
                        .font(.caption)
                        .fontWeight(.bold)
                        .foregroundColor(.cyan)
                }
                
                HStack {
                    Text("Last Download:")
                        .font(.caption)
                        .foregroundColor(.secondary)
                    Spacer()
                    Text(viewModel.tleDownloader.formattedLastDownloadDate)
                        .font(.caption)
                        .fontWeight(.semibold)
                        .foregroundColor(.primary)
                }

                if viewModel.tleDownloader.totalTleCount > 0 {
                    Text("Galileo: \(viewModel.tleDownloader.galileoCount) | GPS: \(viewModel.tleDownloader.gpsCount) | GLO: \(viewModel.tleDownloader.glonassCount) | BDS: \(viewModel.tleDownloader.beidouCount)")
                        .font(.caption2)
                        .foregroundColor(.secondary)
                }

                Button(action: {
                    Task {
                        await viewModel.tleDownloader.fetchAllTleOrbits()
                        viewModel.updateSatellites()
                    }
                }) {
                    HStack {
                        Image(systemName: "arrow.clockwise.icloud")
                        Text(viewModel.tleDownloader.isDownloading ? "Downloading Orbits..." : "Download Fresh TLE Orbits (CelesTrak)")
                    }
                    .font(.caption)
                    .fontWeight(.bold)
                }
                .disabled(viewModel.tleDownloader.isDownloading)
            }
            .padding(.vertical, 4)

            if viewModel.selectedMode == 2 && horizontalSizeClass == .compact {
                VStack(alignment: .leading, spacing: 6) {
                    HStack {
                        Text("Orbit Time Offset:")
                            .font(.caption)
                        Spacer()
                        Text(String(format: "%.1f Hours (±12h)", viewModel.timeOffsetHours))
                            .font(.caption)
                            .fontWeight(.bold)
                            .foregroundColor(.blue)
                    }

                    Slider(value: $viewModel.timeOffsetHours, in: -12.0...12.0, step: 0.5) { _ in
                        viewModel.updateSatellites()
                    }
                }
                .padding(.vertical, 4)
            }
        }
    }

    private var osnmaSection: some View {
        Section(header: Text("OSNMA Security Engine")) {
            HStack {
                Text("Public Key Root")
                Spacer()
                Text("Galileo HKROOT v1")
                    .foregroundColor(.secondary)
            }
            HStack {
                Text("Verification Mode")
                Spacer()
                Text("Loose / Strict MAC")
                    .foregroundColor(.secondary)
            }
        }
    }

    private var aboutSection: some View {
        Section(header: Text("About TRISAT iOS")) {
            HStack {
                Text("Application Version")
                Spacer()
                Text("v\(BuildInfo.version) (\(BuildInfo.buildNumber))")
                    .foregroundColor(.secondary)
            }
            HStack {
                Text("Compilation Timestamp")
                Spacer()
                Text(BuildInfo.buildDateString)
                    .font(.caption)
                    .foregroundColor(.secondary)
            }
            HStack {
                Text("Target Architecture")
                Spacer()
                Text("Multiplatform (Android / iOS)")
                    .foregroundColor(.secondary)
            }
        }
    }
}
