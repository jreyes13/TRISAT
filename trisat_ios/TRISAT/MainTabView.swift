import SwiftUI

struct MainTabView: View {
    @StateObject private var bluetoothManager: BluetoothManager
    @StateObject private var viewModel: GnssViewModel
    @StateObject private var telemetryBridge: GnssTelemetryBridge
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
            SignalProcessingView(viewModel: viewModel, telemetryBridge: telemetryBridge)
                .tabItem {
                    Image(systemName: "waveform.path.ecg")
                    Text("Signal Proc.")
                }
                .tag(1)

            // Tab 3: Threat Analysis
            ThreatAnalysisView(viewModel: viewModel)
                .tabItem {
                    Image(systemName: "exclamationmark.shield.fill")
                    Text("Threat Analysis")
                }
                .tag(2)

            // Tab 4: Settings & Logs
            SettingsView(bluetoothManager: bluetoothManager, viewModel: viewModel, telemetryBridge: telemetryBridge)
                .tabItem {
                    Image(systemName: "gearshape.fill")
                    Text("Settings")
                }
                .tag(3)
        }
        .accentColor(.cyan)
        .onAppear {
            telemetryBridge.configure(viewModel: viewModel, bluetoothManager: bluetoothManager)
        }
    }
}
