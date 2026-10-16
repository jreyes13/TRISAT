import SwiftUI

struct MainTabView: View {
    @StateObject private var viewModel: GnssViewModel
    @StateObject private var bluetoothManager = BluetoothManager()
    
    init() {
        _viewModel = StateObject(wrappedValue: GnssViewModel())
    }
    
    var body: some View {
        TabView {
            // Tab 1: Dashboard
            DashboardView(viewModel: viewModel)
                .tabItem {
                    Image(systemName: "gauge.with.needle.fill")
                    Text("Dashboard")
                }
            
            // Tab 2: Signal Processing
            SignalProcessingView(viewModel: viewModel)
                .tabItem {
                    Image(systemName: "waveform.path.ecg")
                    Text("Signal Proc.")
                }
            
            // Tab 3: Threat Analysis
            ThreatAnalysisView(viewModel: viewModel)
                .tabItem {
                    Image(systemName: "exclamationmark.shield.fill")
                    Text("Threat Analysis")
                }
            
            // Tab 4: Settings
            SettingsView(bluetoothManager: bluetoothManager)
                .tabItem {
                    Image(systemName: "gearshape.fill")
                    Text("Settings")
                }
        }
        .accentColor(.blue)
    }
}