import SwiftUI

struct MainTabView: View {
    @StateObject private var viewModel: GnssViewModel
    @StateObject private var bluetoothManager = BluetoothManager()
    @State private var selectedTab: Int = 0
    
    init() {
        _viewModel = StateObject(wrappedValue: GnssViewModel())
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
            SignalProcessingView(viewModel: viewModel)
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
            
            // Tab 4: Settings
            SettingsView(bluetoothManager: bluetoothManager)
                .tabItem {
                    Image(systemName: "gearshape.fill")
                    Text("Settings")
                }
                .tag(3)
        }
        .accentColor(.blue)
    }
}