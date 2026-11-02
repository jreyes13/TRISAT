import SwiftUI

struct MainTabView: View {
    @StateObject private var bluetoothManager: BluetoothManager
    @StateObject private var viewModel: GnssViewModel
    @StateObject private var telemetryBridge: GnssTelemetryBridge
    #if os(macOS)
    @StateObject private var macSerialManager = MacSerialPortManager()
    #endif
    @State private var selectedTab: Int = 0
    
    init() {
        let sharedBle = BluetoothManager()
        let vm = GnssViewModel(bluetoothManager: sharedBle)
        let bridge = GnssTelemetryBridge(viewModel: vm, bluetoothManager: sharedBle)

        _bluetoothManager = StateObject(wrappedValue: sharedBle)
        _viewModel = StateObject(wrappedValue: vm)
        _telemetryBridge = StateObject(wrappedValue: bridge)
    }
    
    var body: some View {
        TabView(selection: $selectedTab) {
            // Tab 1: Dashboard
            DashboardView(viewModel: viewModel)
                .tabItem {
                    Image(systemName: "gauge.with.needle.fill")
                    Text("Dashboard")
                }
                .tag(0)

            // Tab 2: Signal Processing
            SignalProcessingView(viewModel: viewModel, telemetryBridge: telemetryBridge, bluetoothManager: bluetoothManager)
                .tabItem {
                    Image(systemName: "waveform.path.ecg")
                    Text("Signal Proc.")
                }
                .tag(1)

            // Tab 3: Threat Analysis
            ThreatAnalysisView(viewModel: viewModel, telemetryBridge: telemetryBridge)
                .tabItem {
                    Image(systemName: "exclamationmark.shield.fill")
                    Text("Threat Analysis")
                }
                .tag(2)

            // Tab 4: Settings & Logs
            #if os(macOS)
            SettingsView(bluetoothManager: bluetoothManager, viewModel: viewModel, telemetryBridge: telemetryBridge, macSerialManager: macSerialManager)
                .tabItem {
                    Image(systemName: "gearshape.fill")
                    Text("Settings")
                }
                .tag(3)
            #else
            SettingsView(bluetoothManager: bluetoothManager, viewModel: viewModel, telemetryBridge: telemetryBridge)
                .tabItem {
                    Image(systemName: "gearshape.fill")
                    Text("Settings")
                }
                .tag(3)
            #endif
        }
        .accentColor(.cyan)
        .onAppear {
            telemetryBridge.configure(viewModel: viewModel, bluetoothManager: bluetoothManager)
            #if os(macOS)
            macSerialManager.onLineReceived = { line in
                DispatchQueue.main.async {
                    // Mute hardware USB serial if Simulator is active or mode is Simulator
                    guard !telemetryBridge.isSdrEngineRunning && viewModel.selectedMode != 2 else { return }

                    if viewModel.selectedMode == 0 {
                        viewModel.selectedMode = 1
                    }
                    bluetoothManager.isConnected = true
                    let isDirect = macSerialManager.connectedPortName.contains("usbmodem")
                    let portType = isDirect ? "USB Directo" : "ESP32 Bridge"
                    bluetoothManager.connectedPeripheralName = "u-blox ZED-F9P (\(portType))"
                    if bluetoothManager.nmeaSentences.count >= 100 {
                        bluetoothManager.nmeaSentences.removeFirst()
                    }
                    bluetoothManager.nmeaSentences.append(line)
                    bluetoothManager.onNmeaLineReceived?(line)
                }
            }

            macSerialManager.onInavFrameReceived = { inavMsg in
                DispatchQueue.main.async {
                    // Mute hardware USB serial if Simulator is active or mode is Simulator
                    guard !telemetryBridge.isSdrEngineRunning && viewModel.selectedMode != 2 else { return }
                    viewModel.handleInavMessage(inavMsg)
                }
            }

            macSerialManager.onDisconnection = {
                DispatchQueue.main.async {
                    bluetoothManager.isConnected = false
                    bluetoothManager.connectedPeripheralName = "None"
                    viewModel.resetHardwareTelemetry()
                }
            }

            macSerialManager.onDataTimeout = {
                DispatchQueue.main.async {
                    viewModel.resetHardwareTelemetry()
                }
            }

            macSerialManager.refreshPortsAndAutoConnect()
            #endif
        }
    }
}
