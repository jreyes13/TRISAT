import SwiftUI

struct SettingsView: View {
    @ObservedObject var bluetoothManager: BluetoothManager
    @ObservedObject var viewModel: GnssViewModel
    @ObservedObject var telemetryBridge: GnssTelemetryBridge

    @Environment(\.horizontalSizeClass) var horizontalSizeClass
    @State private var showBlePickerSheet: Bool = false
    @State private var selectedBleDeviceName: String = ""
    @State private var selectedBridgeOption: Int = 2

    #if os(macOS)
    @StateObject private var macSerialManager = MacSerialPortManager()
    #endif

    init(bluetoothManager: BluetoothManager, viewModel: GnssViewModel, telemetryBridge: GnssTelemetryBridge = GnssTelemetryBridge()) {
        self.bluetoothManager = bluetoothManager
        self.viewModel = viewModel
        self.telemetryBridge = telemetryBridge
    }

    private var isWideLayout: Bool {
        #if os(macOS)
        return true
        #else
        return horizontalSizeClass == .regular
        #endif
    }

    var body: some View {
        ScrollView {
            VStack(spacing: 12) {
                // Compact Top Header Banner
                HStack {
                    VStack(alignment: .leading, spacing: 2) {
                        Text("Settings & Hardware Configuration")
                            .font(.headline)
                            .bold()
                        Text("Centralized SDR / BLE / USB Receiver Control, 4-Constellation CelesTrak TLE Orbits & OSNMA Keys")
                            .font(.caption)
                            .foregroundColor(.secondary)
                    }
                    Spacer()
                }
                .padding(.horizontal, 16)
                .padding(.top, 8)

                if isWideLayout {
                    // macOS & iPad Balanced 2-Column Desktop Layout
                    HStack(alignment: .top, spacing: 14) {
                        // Left Column: Receiver Mode, Dual-SDR & Bridges, Bluetooth, Serial
                        VStack(spacing: 12) {
                            receiverModeSection
                            usbBridgeSectionCard
                            bluetoothSection
                            #if os(macOS)
                            macSerialSection
                            #endif
                        }
                        .frame(maxWidth: .infinity)

                        // Right Column: 4-Constellation CelesTrak TLE Manager + Live SkyPlot Preview + OSNMA & About
                        VStack(spacing: 12) {
                            tleAndOrbitPreviewCard
                            HStack(alignment: .top, spacing: 12) {
                                osnmaSection
                                    .frame(maxWidth: .infinity)
                                aboutSection
                                    .frame(maxWidth: .infinity)
                            }
                        }
                        .frame(maxWidth: .infinity)
                    }
                    .padding(.horizontal, 16)
                } else {
                    // Compact 1-Column Layout
                    VStack(spacing: 14) {
                        receiverModeSection
                        usbBridgeSectionCard
                        tleAndOrbitPreviewCard
                        bluetoothSection
                        #if os(macOS)
                        macSerialSection
                        #endif
                        osnmaSection
                        aboutSection
                    }
                    .padding(.horizontal)
                }
            }
            .padding(.bottom, 16)
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

    // MARK: - Section 1: GNSS Receiver Mode

    private var receiverModeSection: some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack {
                Image(systemName: "cpu")
                    .foregroundColor(.cyan)
                Text("GNSS Receiver & Telemetry Mode")
                    .font(.subheadline)
                    .fontWeight(.bold)
                Spacer()
                Text(viewModel.selectedMode == 2 ? "SGP4 ORBIT SIM" : (viewModel.selectedMode == 1 ? "LIVE SDR / BRIDGE" : "INTERNAL + TLE"))
                    .font(.system(size: 9, weight: .bold))
                    .padding(.horizontal, 6)
                    .padding(.vertical, 3)
                    .background(Color.cyan.opacity(0.2))
                    .foregroundColor(.cyan)
                    .cornerRadius(6)
            }

            Picker("Receiver Mode", selection: $viewModel.selectedMode) {
                Text("Internal + TLE").tag(0)
                Text("External BLE / SDR Bridge").tag(1)
                Text("CelesTrak TLE Simulation").tag(2)
            }
            .pickerStyle(.segmented)
            .onChange(of: viewModel.selectedMode) { newValue in
                viewModel.updateSatellites()
                if newValue == 1 {
                    bluetoothManager.startScanning(trisatOnly: false)
                }
            }
        }
        .padding(12)
        .background(Color.gray.opacity(0.08))
        .cornerRadius(14)
    }

    // MARK: - Section 2: TRISAT-SDR Dual-HackRF & Local UDP Bridge Configuration

    private var usbBridgeSectionCard: some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack {
                Image(systemName: "network")
                    .foregroundColor(.green)
                Text("TRISAT-SDR Dual-HackRF & Local Bridges")
                    .font(.subheadline)
                    .fontWeight(.bold)
                Spacer()
                Text(telemetryBridge.isSdrEngineRunning ? "SDR RUNNING" : (telemetryBridge.isListening ? "UDP READY" : "IDLE"))
                    .font(.system(size: 9, weight: .bold))
                    .padding(.horizontal, 6)
                    .padding(.vertical, 3)
                    .background((telemetryBridge.isSdrEngineRunning || telemetryBridge.isListening) ? Color.green.opacity(0.2) : Color.orange.opacity(0.2))
                    .foregroundColor((telemetryBridge.isSdrEngineRunning || telemetryBridge.isListening) ? .green : .orange)
                    .cornerRadius(6)
            }

            Picker("Local Bridge", selection: $selectedBridgeOption) {
                Text("None / Inactive").tag(0)
                Text("SXblue USB Bridge (UDP 127.0.0.1:9090)").tag(1)
                Text("TRISAT-SDR Dual-HackRF (E1/E5/E6 UDP :9090)").tag(2)
            }
            .pickerStyle(.menu)
            .onChange(of: selectedBridgeOption) { opt in
                if opt == 2 {
                    telemetryBridge.startTrisatSdrEngine()
                } else if opt == 1 {
                    telemetryBridge.startUdpListener(port: 9090)
                } else {
                    telemetryBridge.stopTrisatSdrEngine()
                    telemetryBridge.stopUdpListener()
                }
            }

            #if os(macOS)
            // Centralized HackRF #1 Secondary Band Selector (moved from Signal Processing)
            HStack {
                Text("HackRF #1 Secondary Band:")
                    .font(.caption)
                    .foregroundColor(.secondary)
                Spacer()
                Picker("HackRF #1 Band", selection: $telemetryBridge.selectedSdrBand2) {
                    Text("E6B/C (HAS PPP SL1 - 1278.75 MHz)").tag("GAL_E6")
                    Text("E5a (F/NAV Dual-Freq - 1176.45 MHz)").tag("GAL_E5A")
                    Text("E5b (I/NAV Dual-Freq - 1207.14 MHz)").tag("GAL_E5B")
                    Text("E5 Full AltBOC (1191.795 MHz)").tag("GAL_E5_ALTBOC")
                }
                .labelsHidden()
                .pickerStyle(.menu)
                .frame(maxWidth: 250)
                .onChange(of: telemetryBridge.selectedSdrBand2) { newBand in
                    telemetryBridge.switchSdrSecondaryBand(newBand)
                }
            }
            #endif

            HStack(spacing: 8) {
                #if os(macOS)
                Button(action: {
                    if telemetryBridge.isSdrEngineRunning {
                        telemetryBridge.stopTrisatSdrEngine()
                    } else {
                        selectedBridgeOption = 2
                        viewModel.selectedMode = 1
                        telemetryBridge.startTrisatSdrEngine()
                    }
                }) {
                    HStack(spacing: 4) {
                        Image(systemName: telemetryBridge.isSdrEngineRunning ? "stop.circle.fill" : "play.circle.fill")
                        Text(telemetryBridge.isSdrEngineRunning ? "Stop TRISAT-SDR" : "Start TRISAT-SDR")
                    }
                    .font(.caption)
                    .fontWeight(.bold)
                }
                .buttonStyle(.borderedProminent)
                .tint(telemetryBridge.isSdrEngineRunning ? .red : .green)

                Button(action: {
                    telemetryBridge.openWebDashboard()
                }) {
                    HStack(spacing: 4) {
                        Image(systemName: "safari.fill")
                        Text("Web Dashboard (:8085)")
                    }
                    .font(.caption)
                    .fontWeight(.bold)
                }
                .buttonStyle(.borderedProminent)
                .tint(.cyan)

                Button(action: {
                    launchDaemonWithAdminPrivileges()
                }) {
                    HStack(spacing: 4) {
                        Image(systemName: "lock.shield.fill")
                        Text("SXblue (sudo)")
                    }
                    .font(.caption)
                    .fontWeight(.bold)
                }
                .buttonStyle(.borderedProminent)
                .tint(.purple)
                #endif

                Spacer()

                Button(telemetryBridge.isListening ? "Stop UDP" : "Start UDP") {
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
                Text(telemetryBridge.statusText)
                    .font(.caption2)
                    .foregroundColor(.secondary)
                    .lineLimit(1)
                Spacer()
                Text("Datagrams Rx: \(telemetryBridge.packetsReceivedCount)")
                    .font(.caption2)
                    .fontWeight(.bold)
                    .foregroundColor(.cyan)
            }
        }
        .padding(12)
        .background(Color.gray.opacity(0.08))
        .cornerRadius(14)
    }

    private func launchDaemonWithAdminPrivileges() {
        #if os(macOS)
        let repoRootUrl = URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent()
            .deletingLastPathComponent()
            .deletingLastPathComponent()
        let scriptPath = repoRootUrl.appendingPathComponent("SX_Blue/run_bridge.sh").path
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

    // MARK: - Section 3: CelesTrak 4-Constellation TLE Manager & Live Orbit Preview

    private var tleAndOrbitPreviewCard: some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack {
                Image(systemName: "globe.americas.fill")
                    .foregroundColor(.cyan)
                Text("CelesTrak 4-Constellation TLE Orbits & Skyplot")
                    .font(.subheadline)
                    .fontWeight(.bold)
                Spacer()
                Text("\(viewModel.displayedSatellites.count) SATS VISIBLE")
                    .font(.system(size: 9, weight: .bold))
                    .padding(.horizontal, 6)
                    .padding(.vertical, 3)
                    .background(Color.green.opacity(0.2))
                    .foregroundColor(.green)
                    .cornerRadius(6)
            }

            // 4-Constellation TLE Catalog Badges
            HStack(spacing: 8) {
                tleCountBadge(name: "Galileo", count: viewModel.tleDownloader.galileoCount, color: .cyan)
                tleCountBadge(name: "GPS", count: viewModel.tleDownloader.gpsCount, color: .orange)
                tleCountBadge(name: "GLONASS", count: viewModel.tleDownloader.glonassCount, color: .red)
                tleCountBadge(name: "BeiDou", count: viewModel.tleDownloader.beidouCount, color: .green)
            }

            // Interactive Constellation Filter Toggles
            HStack(spacing: 6) {
                FilterToggleChip(title: "Galileo", iconName: "globe.europe.africa.fill", isSelected: $viewModel.isGalileoEnabled, color: .cyan)
                FilterToggleChip(title: "GPS", iconName: "location.north.line.fill", isSelected: $viewModel.isGpsEnabled, color: .orange)
                FilterToggleChip(title: "GLONASS", iconName: "antenna.radiowaves.left.and.right", isSelected: $viewModel.isGlonassEnabled, color: .red)
                FilterToggleChip(title: "BeiDou", iconName: "star.fill", isSelected: $viewModel.isBeidouEnabled, color: .green)
            }

            SkyPlotView(satellites: viewModel.displayedSatellites, height: 210)
                .padding(.vertical, 2)

            // 24-Hour Keplerian Orbit Propagation Slider (auto-activates Mode 2 TLE Simulation when scrubbed)
            VStack(alignment: .leading, spacing: 4) {
                HStack {
                    Text("Keplerian Orbit Time Offset:")
                        .font(.caption)
                        .fontWeight(.semibold)
                    Spacer()
                    Text(String(format: "%+.1f Hours (±12h)", viewModel.timeOffsetHours))
                        .font(.caption)
                        .fontWeight(.bold)
                        .foregroundColor(.cyan)
                    if viewModel.timeOffsetHours != 0.0 {
                        Button("Reset 0h") {
                            viewModel.timeOffsetHours = 0.0
                            viewModel.updateSatellites()
                        }
                        .font(.caption2)
                        .buttonStyle(.plain)
                        .foregroundColor(.orange)
                    }
                }

                Slider(value: $viewModel.timeOffsetHours, in: -12.0...12.0, step: 0.5) { _ in
                    if viewModel.selectedMode != 2 {
                        viewModel.selectedMode = 2
                    }
                    viewModel.updateSatellites()
                }
            }
            .padding(8)
            .background(Color.cyan.opacity(0.08))
            .cornerRadius(8)

            HStack {
                VStack(alignment: .leading, spacing: 2) {
                    Text(viewModel.tleDownloader.downloadStatusText)
                        .font(.caption2)
                        .fontWeight(.bold)
                        .foregroundColor(.cyan)
                    Text("Updated: \(viewModel.tleDownloader.formattedLastDownloadDate)")
                        .font(.system(size: 10))
                        .foregroundColor(.secondary)
                }
                Spacer()
                Button(action: {
                    Task {
                        await viewModel.tleDownloader.fetchAllTleOrbits()
                        viewModel.selectedMode = 2
                        viewModel.updateSatellites()
                    }
                }) {
                    HStack(spacing: 4) {
                        Image(systemName: "arrow.clockwise.icloud")
                        Text(viewModel.tleDownloader.isDownloading ? "Syncing..." : "Refresh CelesTrak TLEs")
                    }
                    .font(.caption)
                    .fontWeight(.bold)
                    .padding(.horizontal, 10)
                    .padding(.vertical, 6)
                    .background(Color.blue.opacity(0.2))
                    .foregroundColor(.blue)
                    .cornerRadius(8)
                }
                .buttonStyle(.plain)
                .disabled(viewModel.tleDownloader.isDownloading)
            }
        }
        .padding(12)
        .background(Color.gray.opacity(0.08))
        .cornerRadius(14)
    }

    private func tleCountBadge(name: String, count: Int, color: Color) -> some View {
        VStack(spacing: 2) {
            Text(name)
                .font(.system(size: 9, weight: .bold))
                .foregroundColor(.secondary)
            Text("\(count) TLEs")
                .font(.system(size: 11, weight: .bold, design: .monospaced))
                .foregroundColor(color)
        }
        .frame(maxWidth: .infinity)
        .padding(.vertical, 5)
        .background(color.opacity(0.1))
        .cornerRadius(6)
    }

    // MARK: - Section 4: Bluetooth GNSS Receivers

    private var bluetoothSection: some View {
        VStack(alignment: .leading, spacing: 8) {
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
                Text("Discovered BLE Devices (\(bluetoothManager.discoveredPeripherals.count)):")
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
                .font(.caption)
                .buttonStyle(.bordered)
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
            }

            if bluetoothManager.isConnected {
                HStack {
                    Image(systemName: "checkmark.circle.fill")
                        .foregroundColor(.green)
                    Text("Active: \(bluetoothManager.connectedPeripheralName)")
                        .font(.caption)
                        .fontWeight(.bold)
                        .lineLimit(1)
                    Spacer()
                    Button("Disconnect") {
                        bluetoothManager.disconnect()
                    }
                    .font(.caption2)
                    .foregroundColor(.red)
                }
                .padding(6)
                .background(Color.green.opacity(0.1))
                .cornerRadius(8)
            }
        }
        .padding(12)
        .background(Color.gray.opacity(0.08))
        .cornerRadius(14)
    }

    // MARK: - Section 5: macOS Hardware Serial Ports

    #if os(macOS)
    private var macSerialSection: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack {
                Image(systemName: "cable.connector.horizontal")
                    .foregroundColor(.green)
                Text("Hardware USB / Serial Ports")
                    .font(.subheadline)
                    .fontWeight(.bold)
                Spacer()
                Button(action: { macSerialManager.refreshPorts() }) {
                    Label("Refresh", systemImage: "arrow.clockwise")
                        .font(.caption2)
                }
                .buttonStyle(.bordered)
            }

            if !macSerialManager.availablePorts.isEmpty {
                HStack {
                    Picker("USB Serial Port", selection: $macSerialManager.selectedPortPath) {
                        ForEach(macSerialManager.availablePorts) { port in
                            Text(port.name).tag(port.path)
                        }
                    }
                    .labelsHidden()
                    .pickerStyle(.menu)

                    if macSerialManager.isConnected {
                        Button("Disconnect") {
                            macSerialManager.closePort()
                        }
                        .font(.caption)
                        .buttonStyle(.borderedProminent)
                        .tint(.red)
                    } else {
                        Button("Connect") {
                            macSerialManager.connectToSelectedPort()
                        }
                        .font(.caption)
                        .buttonStyle(.borderedProminent)
                        .tint(.green)
                    }
                }
            } else {
                Text("No direct POSIX /dev/cu.usbserial ports found (Use 'TRISAT-SDR' or 'SXblue' above).")
                    .font(.caption2)
                    .foregroundColor(.secondary)
            }
        }
        .padding(12)
        .background(Color.gray.opacity(0.08))
        .cornerRadius(14)
    }
    #endif

    // MARK: - Section 6: OSNMA & About Cards

    private var osnmaSection: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack {
                Image(systemName: "key.fill")
                    .foregroundColor(.orange)
                Text("OSNMA Engine")
                    .font(.subheadline)
                    .fontWeight(.bold)
            }

            HStack {
                Text("Root Key")
                    .font(.caption2)
                    .foregroundColor(.secondary)
                Spacer()
                Text("Galileo HKROOT v1.1")
                    .font(.caption2)
                    .fontWeight(.bold)
            }

            HStack {
                Text("ECDSA / MAC")
                    .font(.caption2)
                    .foregroundColor(.secondary)
                Spacer()
                Text("P-256 / HMAC-256")
                    .font(.caption2)
                    .fontWeight(.bold)
            }

            HStack {
                Text("HAS Decoder")
                    .font(.caption2)
                    .foregroundColor(.secondary)
                Spacer()
                Text("RS(255,32) SL1")
                    .font(.caption2)
                    .fontWeight(.bold)
                    .foregroundColor(.cyan)
            }
        }
        .padding(12)
        .background(Color.gray.opacity(0.08))
        .cornerRadius(14)
    }

    private var aboutSection: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack {
                Image(systemName: "info.circle.fill")
                    .foregroundColor(.gray)
                Text("About TRISAT")
                    .font(.subheadline)
                    .fontWeight(.bold)
            }

            HStack {
                Text("Version")
                    .font(.caption2)
                    .foregroundColor(.secondary)
                Spacer()
                Text("v\(BuildInfo.version) (\(BuildInfo.buildNumber))")
                    .font(.caption2)
                    .fontWeight(.bold)
            }

            HStack {
                Text("Build Date")
                    .font(.caption2)
                    .foregroundColor(.secondary)
                Spacer()
                Text(BuildInfo.buildDateString)
                    .font(.caption2)
            }

            HStack {
                Text("Platform")
                    .font(.caption2)
                    .foregroundColor(.secondary)
                Spacer()
                Text("macOS / iOS / Android")
                    .font(.caption2)
                    .fontWeight(.semibold)
            }
        }
        .padding(12)
        .background(Color.gray.opacity(0.08))
        .cornerRadius(14)
    }
}
