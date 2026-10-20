import SwiftUI

struct BleDevicePickerSheet: View {
    @ObservedObject var bluetoothManager: BluetoothManager
    @Environment(\.dismiss) var dismiss
    @State private var filterTrisatOnly: Bool = false
    @State private var showDebugConsole: Bool = true

    var filteredPeripherals: [DiscoveredPeripheral] {
        if filterTrisatOnly {
            return bluetoothManager.discoveredPeripherals.filter { $0.isTrisatDevice }
        } else {
            return bluetoothManager.discoveredPeripherals
        }
    }

    var body: some View {
        NavigationView {
            VStack(spacing: 12) {
                // Filter Switch Header
                Toggle(isOn: $filterTrisatOnly) {
                    HStack {
                        Image(systemName: "line.3.horizontal.decrease.circle.fill")
                            .foregroundColor(.cyan)
                        Text("Show TRISAT Android & SXBlue Only")
                            .font(.subheadline)
                            .fontWeight(.bold)
                    }
                }
                .padding(.horizontal)
                .padding(.top, 10)
                .onChange(of: filterTrisatOnly) { newValue in
                    bluetoothManager.startScanning(trisatOnly: newValue)
                }

                if filteredPeripherals.isEmpty {
                    VStack(spacing: 12) {
                        ProgressView()
                            .scaleEffect(1.2)
                        Text(bluetoothManager.isScanning ? "Scanning nearby Bluetooth receivers & MFi accessories..." : "No matching GNSS receivers found.")
                            .font(.caption)
                            .foregroundColor(.secondary)
                            .multilineTextAlignment(.center)
                            .padding(.horizontal)

                        Button("Restart Scan") {
                            bluetoothManager.startScanning(trisatOnly: filterTrisatOnly)
                        }
                        .font(.caption)
                        .buttonStyle(.borderedProminent)
                        .tint(.cyan)
                    }
                    .padding(.vertical, 20)
                } else {
                    // ScrollView + VStack prevents SwiftUI List row tap interception completely!
                    ScrollView {
                        VStack(spacing: 10) {
                            ForEach(filteredPeripherals) { item in
                                BleDeviceCard(
                                    item: item,
                                    isConnecting: bluetoothManager.isConnectingToId == item.id
                                ) {
                                    bluetoothManager.connect(to: item)
                                }
                            }
                        }
                        .padding(.horizontal)
                    }
                    .frame(maxHeight: 260)
                }

                Divider()

                // Live Bluetooth Diagnostic Console
                VStack(alignment: .leading, spacing: 6) {
                    HStack {
                        Image(systemName: "terminal.fill")
                            .foregroundColor(.green)
                        Text("Bluetooth Diagnostic Log")
                            .font(.caption)
                            .fontWeight(.bold)
                            .foregroundColor(.primary)
                        Spacer()
                        Button(showDebugConsole ? "Hide" : "Show") {
                            showDebugConsole.toggle()
                        }
                        .font(.caption2)
                        .foregroundColor(.cyan)
                    }

                    if showDebugConsole {
                        ScrollViewReader { proxy in
                            ScrollView {
                                LazyVStack(alignment: .leading, spacing: 3) {
                                    ForEach(Array(bluetoothManager.debugLogs.enumerated()), id: \.offset) { index, log in
                                        Text(log)
                                            .font(.system(size: 10, design: .monospaced))
                                            .foregroundColor(.green)
                                            .id(index)
                                    }
                                }
                                .padding(8)
                            }
                            .frame(height: 140)
                            .background(Color.black)
                            .cornerRadius(8)
                            .onChange(of: bluetoothManager.debugLogs.count) { _ in
                                if let lastIndex = bluetoothManager.debugLogs.indices.last {
                                    proxy.scrollTo(lastIndex, anchor: .bottom)
                                }
                            }
                        }
                    }
                }
                .padding(.horizontal)
                .padding(.bottom, 12)
            }
            .navigationTitle("Select GNSS Receiver")
            #if os(iOS)
            .navigationBarTitleDisplayMode(.inline)
            #endif
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Close") {
                        dismiss()
                    }
                }
                ToolbarItem(placement: .primaryAction) {
                    Button(action: { bluetoothManager.startScanning(trisatOnly: filterTrisatOnly) }) {
                        Image(systemName: "arrow.clockwise")
                    }
                }
            }
        }
        .onChange(of: bluetoothManager.isConnected) { connected in
            if connected {
                dismiss()
            }
        }
        .onAppear {
            bluetoothManager.startScanning(trisatOnly: filterTrisatOnly)
        }
    }
}

struct BleDeviceCard: View {
    let item: DiscoveredPeripheral
    let isConnecting: Bool
    let onConnectTap: () -> Void

    var body: some View {
        HStack(spacing: 12) {
            Image(systemName: item.isMfiAccessory ? "cable.connector" : (item.isTrisatDevice ? "antenna.radiowaves.left.and.right" : "cpu"))
                .font(.title2)
                .foregroundColor(item.isMfiAccessory ? .green : (item.isTrisatDevice ? .cyan : .gray))
                .frame(width: 32)

            VStack(alignment: .leading, spacing: 3) {
                HStack(spacing: 6) {
                    Text(item.name)
                        .font(.subheadline)
                        .fontWeight(.bold)
                        .foregroundColor(.primary)

                    if item.isMfiAccessory {
                        Text("PAIRED")
                            .font(.system(size: 8, weight: .bold))
                            .padding(.horizontal, 4)
                            .padding(.vertical, 2)
                            .background(Color.green.opacity(0.2))
                            .foregroundColor(.green)
                            .cornerRadius(4)
                    } else if item.isTrisatDevice {
                        Text("TARGET")
                            .font(.system(size: 8, weight: .bold))
                            .padding(.horizontal, 4)
                            .padding(.vertical, 2)
                            .background(Color.cyan.opacity(0.2))
                            .foregroundColor(.cyan)
                            .cornerRadius(4)
                    }
                }

                Text("RSSI Signal: \(item.rssi) dBm")
                    .font(.caption2)
                    .foregroundColor(.secondary)
            }

            Spacer()

            Button(action: onConnectTap) {
                HStack(spacing: 6) {
                    if isConnecting {
                        ProgressView()
                            .scaleEffect(0.8)
                        Text("Connecting...")
                            .font(.caption2)
                            .fontWeight(.bold)
                            .foregroundColor(.orange)
                    } else {
                        Text("Connect")
                            .font(.caption2)
                            .fontWeight(.bold)
                            .padding(.horizontal, 12)
                            .padding(.vertical, 6)
                            .background(item.isMfiAccessory ? Color.green : (item.isTrisatDevice ? Color.cyan : Color.blue))
                            .foregroundColor(.white)
                            .cornerRadius(8)
                    }
                }
            }
            .buttonStyle(PlainButtonStyle())
        }
        .padding(12)
        .background(item.isMfiAccessory ? Color.green.opacity(0.08) : (item.isTrisatDevice ? Color.cyan.opacity(0.08) : Color.gray.opacity(0.08)))
        .overlay(
            RoundedRectangle(cornerRadius: 12)
                .stroke(item.isMfiAccessory ? Color.green.opacity(0.4) : (item.isTrisatDevice ? Color.cyan.opacity(0.3) : Color.clear), lineWidth: 1)
        )
        .cornerRadius(12)
        .contentShape(Rectangle())
        .onTapGesture {
            onConnectTap()
        }
    }
}
