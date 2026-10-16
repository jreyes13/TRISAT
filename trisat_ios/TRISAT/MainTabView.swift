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
                    Label("Dashboard", systemImage: "gauge.with.needle.fill")
                }
            
            // Tab 2: Signal Processing
            SignalProcessingView(viewModel: viewModel)
                .tabItem {
                    Label("Signal Proc.", systemImage: "waveform.path.ecg")
                }
            
            // Tab 3: Threat Analysis
            ThreatAnalysisView(viewModel: viewModel)
                .tabItem {
                    Label("Threat Analysis", systemImage: "shield.exclamationmark.fill")
                }
            
            // Tab 4: Settings
            SettingsView(bluetoothManager: bluetoothManager)
                .tabItem {
                    Label("Settings", systemImage: "gearshape.fill")
                }
        }
        .accentColor(.blue)
    }
}