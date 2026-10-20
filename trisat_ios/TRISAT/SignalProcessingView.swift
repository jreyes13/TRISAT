import SwiftUI

struct SignalProcessingView: View {
    @ObservedObject var viewModel: GnssViewModel
    @State private var selectedTab: Int = 0 // 0: NMEA Raw Data, 1: Galileo I/NAV
    @State private var showBlePickerSheet: Bool = false

    var body: some View {
        NavigationView {
            VStack(alignment: .leading, spacing: 12) {
                // Device Connected Banner
                HStack(spacing: 10) {
                    Image(systemName: viewModel.bluetoothManager.isConnected ? "checkmark.circle.fill" : "antenna.radiowaves.left.and.right")
                        .font(.title2)
                        .foregroundColor(viewModel.bluetoothManager.isConnected ? .green : .cyan)

                    VStack(alignment: .leading, spacing: 2) {
                        Text(viewModel.bluetoothManager.isConnected ? "Data Source: \(viewModel.bluetoothManager.connectedPeripheralName)" : "Data Source: Internal Receiver / Disconnected")
                            .font(.caption)
                            .fontWeight(.bold)
                            .foregroundColor(.primary)
                        Text(viewModel.bluetoothManager.isConnected ? "Streaming live GNSS telemetry over Bluetooth" : "Connect an external SXBlue or Android receiver in Settings")
                            .font(.caption2)
                            .foregroundColor(.secondary)
                    }

                    Spacer()

                    if !viewModel.bluetoothManager.isConnected {
                        Button("Connect") {
                            showBlePickerSheet = true
                        }
                        .font(.caption2)
                        .buttonStyle(.borderedProminent)
                        .tint(.cyan)
                    }
                }
                .padding(10)
                .background(viewModel.bluetoothManager.isConnected ? Color.green.opacity(0.12) : Color.cyan.opacity(0.12))
                .cornerRadius(10)
                .padding([.horizontal, .top])

                // Segmented Picker for NMEA vs Galileo I/NAV
                Picker("Signal Data View", selection: $selectedTab) {
                    Text("NMEA Raw Data (\(viewModel.bluetoothManager.nmeaSentences.count))").tag(0)
                    Text("Galileo I/NAV (\(viewModel.messages.count))").tag(1)
                }
                .pickerStyle(.segmented)
                .padding(.horizontal)

                if selectedTab == 0 {
                    // TAB 1: NMEA Raw Data Stream
                    nmeaRawDataSection
                } else {
                    // TAB 2: Galileo I/NAV Pages
                    galileoInavSection
                }
            }
            .navigationTitle("Signal Processing")
            #if os(iOS)
            .navigationBarTitleDisplayMode(.inline)
            #endif
            .sheet(isPresented: $showBlePickerSheet) {
                BleDevicePickerSheet(bluetoothManager: viewModel.bluetoothManager)
            }
        }
    }

    // MARK: - Tab 1: NMEA Stream Section
    private var nmeaRawDataSection: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack {
                Text("NMEA 0183 Sentences ($GPGGA, $GAGSV, $GPRMC)")
                    .font(.caption)
                    .fontWeight(.bold)
                    .foregroundColor(.secondary)
                Spacer()
                if !viewModel.bluetoothManager.nmeaSentences.isEmpty {
                    Button("Clear Log") {
                        viewModel.bluetoothManager.nmeaSentences.removeAll()
                    }
                    .font(.caption2)
                    .foregroundColor(.red)
                }
            }
            .padding(.horizontal)

            if viewModel.bluetoothManager.nmeaSentences.isEmpty {
                VStack(spacing: 14) {
                    Image(systemName: "terminal.fill")
                        .font(.system(size: 42))
                        .foregroundColor(.cyan)
                        .padding(.top, 30)

                    Text("Waiting for Raw NMEA Stream...")
                        .font(.headline)
                        .fontWeight(.bold)

                    Text("Connect your SXBlue Platinum or TRISAT Android receiver to view real-time $GPGGA, $GAGSV, and $GPRMC ASCII sentences.")
                        .font(.caption)
                        .foregroundColor(.secondary)
                        .multilineTextAlignment(.center)
                        .padding(.horizontal, 32)

                    HStack(spacing: 10) {
                        Button("Connect GNSS Receiver") {
                            showBlePickerSheet = true
                        }
                        .font(.caption)
                        .buttonStyle(.borderedProminent)
                        .tint(.cyan)

                        Button("Inject Test NMEA Line") {
                            let nowStr = DateFormatter.localizedString(from: Date(), dateStyle: .none, timeStyle: .medium)
                            let sample = "$GPGGA,\(nowStr),3727.8940,N,00210.4040,E,1,08,0.9,120.5,M,48.0,M,,*47"
                            viewModel.bluetoothManager.nmeaSentences.append(sample)
                        }
                        .font(.caption)
                        .buttonStyle(.bordered)
                    }
                    .padding(.top, 4)

                    Spacer()
                }
                .frame(maxWidth: .infinity, maxHeight: .infinity)
            } else {
                ScrollViewReader { proxy in
                    ScrollView {
                        LazyVStack(alignment: .leading, spacing: 4) {
                            ForEach(Array(viewModel.bluetoothManager.nmeaSentences.enumerated()), id: \.offset) { index, sentence in
                                Text(sentence)
                                    .font(.system(size: 11, design: .monospaced))
                                    .foregroundColor(.green)
                                    .id(index)
                            }
                        }
                        .padding(10)
                    }
                    .background(Color.black)
                    .cornerRadius(10)
                    .padding(.horizontal)
                    .onChange(of: viewModel.bluetoothManager.nmeaSentences.count) { _ in
                        if let lastIdx = viewModel.bluetoothManager.nmeaSentences.indices.last {
                            proxy.scrollTo(lastIdx, anchor: .bottom)
                        }
                    }
                }
            }
        }
    }

    // MARK: - Tab 2: Galileo I/NAV Pages Section
    private var galileoInavSection: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack {
                Text("Demodulated Galileo I/NAV Subframes (250-bit)")
                    .font(.caption)
                    .fontWeight(.bold)
                    .foregroundColor(.secondary)
                Spacer()
            }
            .padding(.horizontal)
            
            if viewModel.messages.isEmpty {
                VStack(spacing: 14) {
                    Image(systemName: "lock.shield.fill")
                        .font(.system(size: 42))
                        .foregroundColor(.cyan)
                        .padding(.top, 30)

                    Text("Waiting for Raw Galileo I/NAV Data...")
                        .font(.headline)
                        .fontWeight(.bold)

                    Text("Note: Standard NMEA output (in Tab 1) provides satellite positions and SNR. To stream I/NAV subframes, send $JASC,GALINAV,1 to the SXBlue Platinum in Settings.")
                        .font(.caption)
                        .foregroundColor(.secondary)
                        .multilineTextAlignment(.center)
                        .padding(.horizontal, 32)

                    HStack(spacing: 10) {
                        Button("Configure SXBlue ($JASC,GALINAV,1)") {
                            viewModel.bluetoothManager.sendSxBlueConfigCommands()
                        }
                        .font(.caption)
                        .buttonStyle(.borderedProminent)
                        .tint(.blue)

                        Button("Inject Test I/NAV Frame") {
                            let sample = GalileoINavMessage(
                                svid: 11,
                                submessageId: 1,
                                data: Data((0..<30).map { _ in UInt8.random(in: 0...255) }),
                                status: 0,
                                timestamp: Date()
                            )
                            viewModel.messages.append(sample)
                        }
                        .font(.caption)
                        .buttonStyle(.bordered)
                    }
                    .padding(.top, 4)

                    Spacer()
                }
                .frame(maxWidth: .infinity, maxHeight: .infinity)
            } else {
                ScrollView {
                    LazyVStack(spacing: 8) {
                        ForEach(viewModel.messages.reversed()) { msg in
                            INavMessageCard(message: msg)
                        }
                    }
                    .padding(.horizontal)
                }
            }
        }
    }
}
