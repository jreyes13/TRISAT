import SwiftUI

struct SettingsView: View {
    @ObservedObject var bluetoothManager: BluetoothManager
    @State private var selectedHardwareSource = 0 // 0: Internal, 1: External Bluetooth
    
    var body: some View {
        NavigationView {
            Form {
                // Section 1: GNSS Hardware Receiver
                Section(header: Text("GNSS Hardware Source")) {
                    Picker("Receiver Type", selection: $selectedHardwareSource) {
                        Text("Internal iPhone Receiver").tag(0)
                        Text("External Bluetooth GNSS").tag(1)
                    }
                    .pickerStyle(.segmented)
                    
                    if selectedHardwareSource == 1 {
                        HStack {
                            Text("Bluetooth Status")
                            Spacer()
                            Text(bluetoothManager.isBluetoothOn ? "On" : "Off")
                                .foregroundColor(bluetoothManager.isBluetoothOn ? .green : .red)
                        }
                        
                        Button(action: {
                            bluetoothManager.startScanning()
                        }) {
                            HStack {
                                Image(systemName: "antenna.radiowaves.left.and.right")
                                Text(bluetoothManager.isScanning ? "Scanning for Receivers..." : "Scan BLE Receivers")
                            }
                        }
                    }
                }
                
                // Section 2: OSNMA Configuration
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
                
                // Section 3: App Information
                Section(header: Text("About TRISAT")) {
                    HStack {
                        Text("Application Version")
                        Spacer()
                        Text("1.0.0 (Build 1)")
                            .foregroundColor(.secondary)
                    }
                    HStack {
                        Text("System Target")
                        Spacer()
                        Text("Galileo OSNMA / EUSPA")
                            .foregroundColor(.secondary)
                    }
                }
            }
            .navigationTitle("Settings")
        }
    }
}