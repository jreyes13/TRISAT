import SwiftUI

struct SettingsView: View {
    @ObservedObject var bluetoothManager: BluetoothManager
    @ObservedObject var viewModel: GnssViewModel
    @ObservedObject var telemetryBridge: GnssTelemetryBridge

    @Environment(\.horizontalSizeClass) var horizontalSizeClass
    @State private var showBlePickerSheet: Bool = false
    @State private var selectedBleDeviceName: String = ""
    @State private var selectedBridgeOption: Int = 1

    #if os(macOS)
    @StateObject private var macSerialManager = MacSerialPortManager()
    #endif

    init(bluetoothManager: BluetoothManager, viewModel: GnssViewModel, telemetryBridge: GnssTelemetryBridge = GnssTelemetryBridge()) {
        self.bluetoothManager = bluetoothManager
        self.viewModel = viewModel
        self.telemetryBridge = telemetryBridge
    }

    var body: some View {
        ScrollView {
            VStack(spacing: 18) {
                // Top Header Banner
                HStack {
                    VStack(alignment: .leading, spacing: 4) {
                        Text("Settings & Configuration")
                            .font(.title2)
                            .bold()
                        Text("Receiver Modes, CelesTrak TLE Orbits & OSNMA")
                            .font(.caption)
                            .foregroundColor(.secondary)
                    }
                    Spacer()
                }
                .padding(.horizontal)
                .padding(.top, 8)

                if horizontalSizeClass == .regular {
                    // iPad & Mac 2-Column Full Screen Layout
                    HStack(alignment: .top, spacing: 20) {
                        // Left Column: All Settings Sections
                        VStack(spacing: 16) {
                            bluetoothSection
                            usbBridgeSectionCard
                            #if os(macOS)
                            macSerialSection
                            #endif
                            receiverModeSection
                            tleDownloadSection
                            osnmaSection
                            aboutSection
                        }
                        .frame(maxWidth: .infinity)

                        // Right Column: Live Orbit Simulation SkyPlot Preview Card
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
                        .padding(18)
                        .background(Color.gray.opacity(0.08))
                        .cornerRadius(16)
                        .frame(maxWidth: .infinity)
                    }
                    .padding(.horizontal)
                } else {
                    // iPhone 1-Column Full Screen Layout
                    VStack(spacing: 16) {
                        bluetoothSection
                        usbBridgeSectionCard
                        #if os(macOS)
                        macSerialSection
                        #endif
                        receiverModeSection
                        tleDownloadSection
                        osnmaSection
                        aboutSection
                    }
                    .padding(.horizontal)
                }
            }
            .padding(.bottom, 24)
        }
        .sheet(isPresented: $showBlePickerSheet) {
            BleDevicePickerSheet(bluetoothManager: bluetoothManager)
        }
        .onAppear {
            #if os(macOS)
            macSerialManager.onLineReceived = { line in
                DispatchQueue.main.async {
                    bluetoothManager.isConnected = true
                    bluetoothManager.connectedPeripheralName = macSerialManager.connectedPortName
                    if bluetoothManager.nmeaSentences.count >= 100 {
                        bluetoothManager.nmeaSentences.removeFirst()
                    }
                    bluetoothManager.nmeaSentences.append(line)
                    bluetoothManager.onNmeaLineReceived?(line)
                }
            }
            #endif
        }
    }

    // MARK: - Menú 1: Bluetooth GNSS Receivers

    private var bluetoothSection: some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack {
                Image(systemName: "antenna.radiowaves.left.and.right")
                    .foregroundColor(.blue)
                Text("Bluetooth GNSS Receivers")
                    .font(.subheadline)
                    .fontWeight(.bold)
                Spacer()
                Text(bluetoothManager.isConnected ? "CONNECTED" : "DISCONNECTED")
                    .font(.system(size: 9, weight: .bold))
                    .padding(.horizontal, 6)
                    .padding(.vertical, 3)
                    .background(bluetoothManager.isConnected ? Color.green.opacity(0.2) : Color.red.opacity(0.2))
                    .foregroundColor(bluetoothManager.isConnected ? .green : .red)
                    .cornerRadius(6)
            }

            HStack {
                Text("Discovered BLE Devices:")
                    .font(.caption)
                    .foregroundColor(.secondary)
                Spacer()
                Button(bluetoothManager.isScanning ? "Stop Scan" : "Scan BLE") {
                    if bluetoothManager.isScanning {
                        bluetoothManager.stopScanning()
                    } else {
                        bluetoothManager.startScanning(trisatOnly: false)
                    }
                }
                .font(.caption2)
            }

            if !bluetoothManager.discoveredPeripherals.isEmpty {
                Picker("BLE Receiver", selection: $selectedBleDeviceName) {
                    Text("Select a BLE Receiver...").tag("")
                    ForEach(bluetoothManager.discoveredPeripherals) { item in
                        Text(item.name).tag(item.name)
                    }
                }
                .pickerStyle(.menu)
                .onChange(of: selectedBleDeviceName) { newName in
                    if let target = bluetoothManager.discoveredPeripherals.first(where: { $0.name == newName }) {
                        bluetoothManager.connect(to: target)
                    }
                }
            } else {
                Text("No BLE devices discovered yet. Click 'Scan BLE' to search.")
                    .font(.caption2)
                    .foregroundColor(.secondary)
            }

            if bluetoothManager.isConnected {
                HStack {
                    Image(systemName: "checkmark.circle.fill")
                        .foregroundColor(.green)
                    Text("Active: \(bluetoothManager.connectedPeripheralName)")
                        .font(.caption)
                        .fontWeight(.bold)
                    Spacer()
                    Button("Disconnect") {
                        bluetoothManager.disconnect()
                    }
                    .font(.caption2)
                    .foregroundColor(.red)
                }
                .padding(8)
                .background(Color.green.opacity(0.1))
                .cornerRadius(8)
            }
        }
        .padding()
        .background(Color.gray.opacity(0.08))
        .cornerRadius(14)
    }

    // MARK: - Menú 2: USB / Local Bridges (Daemon)

    private var usbBridgeSectionCard: some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack {
                Image(systemName: "network")
                    .foregroundColor(.green)
                Text("USB / Local Bridges (Daemon)")
                    .font(.subheadline)
                    .fontWeight(.bold)
                Spacer()
                Text(telemetryBridge.statusText.uppercased())
                    .font(.system(size: 9, weight: .bold))
                    .padding(.horizontal, 6)
                    .padding(.vertical, 3)
                    .background(telemetryBridge.isListening ? Color.green.opacity(0.2) : Color.orange.opacity(0.2))
                    .foregroundColor(telemetryBridge.isListening ? .green : .orange)
                    .cornerRadius(6)
            }

            Picker("Local Bridge", selection: $selectedBridgeOption) {
                Text("None / Inactive").tag(0)
                Text("SXblue USB Bridge (UDP 127.0.0.1:9090)").tag(1)
            }
            .pickerStyle(.menu)
            .onChange(of: selectedBridgeOption) { opt in
                if opt == 1 {
                    telemetryBridge.startUdpListener(port: 9090)
                } else {
                    telemetryBridge.stopUdpListener()
                }
            }

            HStack(spacing: 8) {
                #if os(macOS)
                Button(action: {
                    launchDaemonWithAdminPrivileges()
                }) {
                    HStack {
                        Image(systemName: "lock.shield.fill")
                        Text("Launch SXblue Daemon (sudo)")
                    }
                    .font(.caption)
                    .fontWeight(.bold)
                }
                .buttonStyle(.borderedProminent)
                .tint(.purple)
                #endif

                Spacer()

                Button(telemetryBridge.isListening ? "Deactivate UDP" : "Activate UDP") {
                    if telemetryBridge.isListening {
                        telemetryBridge.stopUdpListener()
                        selectedBridgeOption = 0
                    } else {
                        telemetryBridge.startUdpListener(port: 9090)
                        selectedBridgeOption = 1
                    }
                }
                .font(.caption)
                .buttonStyle(.bordered)
            }

            HStack {
                Text("Datagrams Rx: \(telemetryBridge.packetsReceivedCount)")
                    .font(.caption2)
                    .fontWeight(.bold)
                    .foregroundColor(.blue)
                Spacer()
                Text("Run: ./SX_Blue/run_bridge.sh")
                    .font(.caption2)
                    .foregroundColor(.secondary)
            }
        }
        .padding()
        .background(Color.gray.opacity(0.08))
        .cornerRadius(14)
    }

    private func launchDaemonWithAdminPrivileges() {
        #if os(macOS)
        let scriptPath = "/Users/jreyes13/Documents/GitHub/TRISAT/SX_Blue/run_bridge.sh"
        let source = "do shell script \"\(scriptPath)\" with administrator privileges"
        if let script = NSAppleScript(source: source) {
            var error: NSDictionary?
            script.executeAndReturnError(&error)
            if error == nil {
                telemetryBridge.startUdpListener(port: 9090)
            }
        }
        #endif
    }

    // MARK: - macOS Serial Port Section

    #if os(macOS)
    private var macSerialSection: some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack {
                Image(systemName: "cable.connector.horizontal")
                    .foregroundColor(.green)
                Text("Hardware USB / Serial Ports")
                    .font(.subheadline)
                    .fontWeight(.bold)
            }

            HStack {
                Text("Detected USB Ports:")
                    .font(.caption)
                    .foregroundColor(.secondary)
                Spacer()
                Button(action: { macSerialManager.refreshPorts() }) {
                    Image(systemName: "arrow.clockwise")
                }
                .font(.caption2)
            }

            if !macSerialManager.availablePorts.isEmpty {
                Picker("USB Serial Port", selection: $macSerialManager.selectedPortPath) {
                    ForEach(macSerialManager.availablePorts) { port in
                        Text(port.name).tag(port.path)
                    }
                }
                .pickerStyle(.menu)

                HStack {
                    if macSerialManager.isConnected {
                        Button("Disconnect Port") {
                            macSerialManager.closePort()
                        }
                        .font(.caption)
                        .buttonStyle(.borderedProminent)
                        .tint(.red)
                    } else {
                        Button("Connect Hardware Serial Port") {
                            macSerialManager.connectToSelectedPort()
                        }
                        .font(.caption)
                        .buttonStyle(.borderedProminent)
                        .tint(.green)
                    }
                }
            } else {
                Text("No direct POSIX /dev/cu.usbserial ports found (Use 'Launch SXblue Daemon' above).")
                    .font(.caption2)
                    .foregroundColor(.secondary)
            }

            if !macSerialManager.logText.isEmpty {
                Text(macSerialManager.logText)
                    .font(.caption2)
                    .foregroundColor(.cyan)
            }
        }
        .padding()
        .background(Color.gray.opacity(0.08))
        .cornerRadius(14)
    }
    #endif

    // MARK: - Settings Section Cards

    private var receiverModeSection: some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack {
                Image(systemName: "cpu")
                    .foregroundColor(.cyan)
                Text("GNSS Receiver Mode")
                    .font(.subheadline)
                    .fontWeight(.bold)
            }

            Picker("Receiver Mode", selection: $viewModel.selectedMode) {
                Text("Internal").tag(0)
                Text("External BLE / USB Bridge").tag(1)
                Text("TLE Simulation").tag(2)
            }
            .pickerStyle(.segmented)
            .onChange(of: viewModel.selectedMode) { newValue in
                viewModel.updateSatellites()
                if newValue == 1 {
                    bluetoothManager.startScanning(trisatOnly: false)
                }
            }
        }
        .padding()
        .background(Color.gray.opacity(0.08))
        .cornerRadius(14)
    }

    private var tleDownloadSection: some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack {
                Image(systemName: "icloud.and.arrow.down")
                    .foregroundColor(.blue)
                Text("CelesTrak TLE Orbital Data")
                    .font(.subheadline)
                    .fontWeight(.bold)
            }

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
                .frame(maxWidth: .infinity)
                .padding(.vertical, 8)
                .background(Color.blue.opacity(0.15))
                .foregroundColor(.blue)
                .cornerRadius(8)
            }
            .disabled(viewModel.tleDownloader.isDownloading)

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
                .padding(.top, 4)
            }
        }
        .padding()
        .background(Color.gray.opacity(0.08))
        .cornerRadius(14)
    }

    private var osnmaSection: some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack {
                Image(systemName: "key.fill")
                    .foregroundColor(.orange)
                Text("OSNMA Security Engine")
                    .font(.subheadline)
                    .fontWeight(.bold)
            }

            HStack {
                Text("Public Key Root")
                    .font(.caption)
                Spacer()
                Text("Galileo HKROOT v1")
                    .font(.caption)
                    .fontWeight(.bold)
                    .foregroundColor(.secondary)
            }

            HStack {
                Text("Verification Mode")
                    .font(.caption)
                Spacer()
                Text("Loose / Strict MAC")
                    .font(.caption)
                    .fontWeight(.bold)
                    .foregroundColor(.secondary)
            }
        }
        .padding()
        .background(Color.gray.opacity(0.08))
        .cornerRadius(14)
    }

    private var aboutSection: some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack {
                Image(systemName: "info.circle.fill")
                    .foregroundColor(.gray)
                Text("About TRISAT iOS")
                    .font(.subheadline)
                    .fontWeight(.bold)
            }

            HStack {
                Text("Application Version")
                    .font(.caption)
                Spacer()
                Text("v\(BuildInfo.version) (\(BuildInfo.buildNumber))")
                    .font(.caption)
                    .fontWeight(.bold)
                    .foregroundColor(.secondary)
            }

            HStack {
                Text("Compilation Timestamp")
                    .font(.caption)
                Spacer()
                Text(BuildInfo.buildDateString)
                    .font(.caption)
                    .foregroundColor(.secondary)
            }

            HStack {
                Text("Target Architecture")
                    .font(.caption)
                Spacer()
                Text("Multiplatform (Android / iOS)")
                    .font(.caption)
                    .foregroundColor(.secondary)
            }
        }
        .padding()
        .background(Color.gray.opacity(0.08))
        .cornerRadius(14)
    }
}
