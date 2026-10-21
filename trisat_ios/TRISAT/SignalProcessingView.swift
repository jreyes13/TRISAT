import SwiftUI

struct SignalProcessingView: View {
    @ObservedObject var viewModel: GnssViewModel
    @ObservedObject var telemetryBridge: GnssTelemetryBridge
    @Environment(\.horizontalSizeClass) var horizontalSizeClass

    init(viewModel: GnssViewModel, telemetryBridge: GnssTelemetryBridge = GnssTelemetryBridge()) {
        self.viewModel = viewModel
        self.telemetryBridge = telemetryBridge
    }

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 16) {
                // MARK: - Top Hardware Status & Differential Metrics Header
                topMetricsHeader

                if horizontalSizeClass == .regular {
                    // iPad & macOS: 2x2 Grid Layout (All 4 Quadrants Side-by-Side)
                    VStack(spacing: 16) {
                        // ROW 1: Small Open Skyplot + C/N0 SNR Bar Chart
                        HStack(alignment: .top, spacing: 16) {
                            openSkyplotCard
                                .frame(width: 260)

                            snrBarChartCard
                                .frame(maxWidth: .infinity)
                        }

                        // ROW 2: NMEA Live Terminal + Galileo I/NAV Subframe Cards
                        HStack(alignment: .top, spacing: 16) {
                            nmeaStreamCard
                                .frame(maxWidth: .infinity)

                            galileoInavCard
                                .frame(maxWidth: .infinity)
                        }
                    }
                    .padding(.horizontal)
                } else {
                    // iPhone: Stacked 4 Quadrants Vertical Flow
                    VStack(spacing: 16) {
                        openSkyplotCard
                        snrBarChartCard
                        nmeaStreamCard
                        galileoInavCard
                    }
                    .padding(.horizontal)
                }
            }
            .padding(.bottom, 24)
        }
    }

    // MARK: - Header: Top Metrics & Differential Status

    private var topMetricsHeader: some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack {
                Image(systemName: viewModel.bluetoothManager.isConnected ? "checkmark.circle.fill" : "antenna.radiowaves.left.and.right")
                    .font(.title2)
                    .foregroundColor(viewModel.bluetoothManager.isConnected ? .green : .cyan)

                VStack(alignment: .leading, spacing: 2) {
                    Text(viewModel.bluetoothManager.isConnected ? "Active Source: \(viewModel.bluetoothManager.connectedPeripheralName)" : "Source: Internal Receiver / UDP Bridge Listening (:9090)")
                        .font(.headline)
                        .bold()
                    Text("Live Multi-Constellation Telemetry, C/N0 Signal Matrix & I/NAV Subframe Monitor")
                        .font(.caption2)
                        .foregroundColor(.secondary)
                }

                Spacer()

                #if os(macOS)
                HStack(spacing: 8) {
                    Picker("HackRF #1", selection: $telemetryBridge.selectedSdrBand2) {
                        Text("E6B/C (HAS PPP)").tag("GAL_E6")
                        Text("E5a (F/NAV)").tag("GAL_E5A")
                        Text("E5b (I/NAV)").tag("GAL_E5B")
                        Text("E5 AltBOC").tag("GAL_E5_ALTBOC")
                    }
                    .pickerStyle(.menu)
                    .frame(width: 175)
                    .onChange(of: telemetryBridge.selectedSdrBand2) { newBand in
                        telemetryBridge.switchSdrSecondaryBand(newBand)
                    }

                    Button(action: {
                        if telemetryBridge.isSdrEngineRunning {
                            telemetryBridge.stopTrisatSdrEngine()
                        } else {
                            telemetryBridge.startTrisatSdrEngine()
                        }
                    }) {
                        HStack(spacing: 4) {
                            Image(systemName: telemetryBridge.isSdrEngineRunning ? "stop.circle.fill" : "play.circle.fill")
                            Text(telemetryBridge.isSdrEngineRunning ? "Stop SDR" : "Start TRISAT-SDR")
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
                }
                #endif
            }

            // Differential & Precision Metrics Badges
            HStack(spacing: 10) {
                metricCard(title: "Fix Mode", value: viewModel.fixTypeDesc, icon: "location.fill", color: .green)
                metricCard(title: "Sats in Fix / HDOP", value: "\(viewModel.numSatellitesInFix) Sats | HDOP \(viewModel.hdopValue)", icon: "satellite.fill", color: .cyan)
                metricCard(title: "DGPS Base Station", value: "ID: \(viewModel.dgpsStationId) (\(viewModel.dgpsAge))", icon: "antenna.radiowaves.left.and.right.slash", color: .orange)
                metricCard(title: "Pseudorange Error", value: viewModel.gstRmsError, icon: "chart.xyaxis.line", color: .purple)
            }
        }
        .padding(14)
        .background(Color.gray.opacity(0.1))
        .cornerRadius(12)
        .padding([.horizontal, .top])
    }

    private func metricCard(title: String, value: String, icon: String, color: Color) -> some View {
        VStack(alignment: .leading, spacing: 4) {
            HStack(spacing: 4) {
                Image(systemName: icon)
                    .font(.caption2)
                    .foregroundColor(color)
                Text(title)
                    .font(.system(size: 9, weight: .bold))
                    .foregroundColor(.secondary)
            }
            Text(value)
                .font(.system(size: 11, weight: .bold, design: .monospaced))
                .foregroundColor(.primary)
                .lineLimit(1)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(8)
        .background(color.opacity(0.08))
        .cornerRadius(8)
    }

    // MARK: - Quadrant 1: Compact Open Sky Polar Skyplot

    private var openSkyplotCard: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack {
                Image(systemName: "circle.circle")
                    .foregroundColor(.cyan)
                Text("Open Sky Radar")
                    .font(.caption)
                    .fontWeight(.bold)
                    .foregroundColor(.secondary)
                Spacer()
                Text("\(viewModel.displayedSatellites.count) Sats")
                    .font(.system(size: 9, weight: .bold))
                    .foregroundColor(.cyan)
            }

            SkyPlotView(satellites: viewModel.displayedSatellites, height: 210)
                .padding(6)
        }
        .padding(12)
        .background(Color.gray.opacity(0.08))
        .cornerRadius(14)
    }

    // MARK: - Quadrant 2: Satellite C/N0 SNR Bar Chart (Diagrama de Barras)

    private var snrBarChartCard: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack {
                Image(systemName: "chart.bar.fill")
                    .foregroundColor(.green)
                Text("Signal Strength C/N0 (dB-Hz)")
                    .font(.caption)
                    .fontWeight(.bold)
                    .foregroundColor(.secondary)
                Spacer()
            }

            if viewModel.displayedSatellites.isEmpty {
                VStack(spacing: 12) {
                    Image(systemName: "satellite")
                        .font(.system(size: 32))
                        .foregroundColor(.cyan)
                        .padding(.top, 30)
                    Text("Awaiting Satellite Telemetry...")
                        .font(.caption2)
                        .foregroundColor(.secondary)
                    Spacer()
                }
                .frame(maxWidth: .infinity, minHeight: 210)
            } else {
                ScrollView(.horizontal, showsIndicators: true) {
                    HStack(alignment: .bottom, spacing: 10) {
                        ForEach(viewModel.displayedSatellites) { sat in
                            VStack(spacing: 4) {
                                Text("\(sat.cno)")
                                    .font(.system(size: 9, weight: .bold, design: .monospaced))
                                    .foregroundColor(snrColor(sat.cno))

                                ZStack(alignment: .bottom) {
                                    RoundedRectangle(cornerRadius: 3)
                                        .fill(Color.gray.opacity(0.15))
                                        .frame(width: 18, height: 140)

                                    RoundedRectangle(cornerRadius: 3)
                                        .fill(snrColor(sat.cno))
                                        .frame(width: 18, height: CGFloat(min(max(sat.cno, 0), 50)) * 2.7)
                                }

                                Text(satBadge(sat))
                                    .font(.system(size: 8, weight: .bold, design: .monospaced))
                                    .padding(.horizontal, 3)
                                    .padding(.vertical, 2)
                                    .background(satColor(sat).opacity(0.2))
                                    .foregroundColor(satColor(sat))
                                    .cornerRadius(3)
                            }
                        }
                    }
                    .padding(.vertical, 8)
                    .padding(.horizontal, 8)
                }
                .frame(minHeight: 210)
            }
        }
        .padding(12)
        .background(Color.gray.opacity(0.08))
        .cornerRadius(14)
    }

    // MARK: - Quadrant 3: ASCII NMEA Live Stream Terminal

    private var nmeaStreamCard: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack {
                Image(systemName: "terminal.fill")
                    .foregroundColor(.green)
                Text("ASCII NMEA Stream ($GPGGA, $GAGSV, $GPGST)")
                    .font(.caption)
                    .fontWeight(.bold)
                    .foregroundColor(.secondary)
                Spacer()
                if !viewModel.bluetoothManager.nmeaSentences.isEmpty {
                    Button("Clear") {
                        viewModel.bluetoothManager.nmeaSentences.removeAll()
                    }
                    .font(.caption2)
                    .foregroundColor(.red)
                }
            }

            if viewModel.bluetoothManager.nmeaSentences.isEmpty {
                VStack(spacing: 12) {
                    Image(systemName: "terminal")
                        .font(.system(size: 32))
                        .foregroundColor(.cyan)
                        .padding(.top, 30)
                    Text("Waiting for NMEA datagrams...")
                        .font(.caption2)
                        .foregroundColor(.secondary)
                    Spacer()
                }
                .frame(maxWidth: .infinity, minHeight: 220)
                .background(Color.black)
                .cornerRadius(10)
            } else {
                ScrollViewReader { proxy in
                    ScrollView {
                        LazyVStack(alignment: .leading, spacing: 3) {
                            ForEach(Array(viewModel.bluetoothManager.nmeaSentences.enumerated()), id: \.offset) { index, sentence in
                                Text(sentence)
                                    .font(.system(size: 11, design: .monospaced))
                                    .foregroundColor(.green)
                                    .id(index)
                            }
                        }
                        .padding(8)
                    }
                    .frame(minHeight: 220, maxHeight: 280)
                    .background(Color.black)
                    .cornerRadius(10)
                    .onChange(of: viewModel.bluetoothManager.nmeaSentences.count) { _ in
                        if let lastIdx = viewModel.bluetoothManager.nmeaSentences.indices.last {
                            proxy.scrollTo(lastIdx, anchor: .bottom)
                        }
                    }
                }
            }
        }
        .padding(12)
        .background(Color.gray.opacity(0.08))
        .cornerRadius(14)
    }

    // MARK: - Quadrant 4: Galileo I/NAV Subframe Cards

    private var galileoInavCard: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack {
                Image(systemName: "lock.shield.fill")
                    .foregroundColor(.cyan)
                Text("Galileo I/NAV Subframes (\(viewModel.messages.count))")
                    .font(.caption)
                    .fontWeight(.bold)
                    .foregroundColor(.secondary)
                Spacer()
            }

            if viewModel.messages.isEmpty {
                VStack(spacing: 12) {
                    Image(systemName: "lock.shield")
                        .font(.system(size: 32))
                        .foregroundColor(.cyan)
                        .padding(.top, 30)
                    Text("Waiting for 250-bit I/NAV subframes...")
                        .font(.caption2)
                        .foregroundColor(.secondary)
                    Spacer()
                }
                .frame(maxWidth: .infinity, minHeight: 220)
                .background(Color.gray.opacity(0.05))
                .cornerRadius(10)
            } else {
                ScrollView {
                    LazyVStack(spacing: 6) {
                        ForEach(viewModel.messages.reversed()) { msg in
                            INavMessageCard(message: msg)
                        }
                    }
                    .padding(4)
                }
                .frame(minHeight: 220, maxHeight: 280)
                .background(Color.gray.opacity(0.05))
                .cornerRadius(10)
            }
        }
        .padding(12)
        .background(Color.gray.opacity(0.08))
        .cornerRadius(14)
    }

    // MARK: - Helper Formatting Methods

    private func snrColor(_ cno: Int) -> Color {
        if cno >= 35 { return .green }
        if cno >= 25 { return .yellow }
        return .red
    }

    private func satBadge(_ sat: SatelliteInfo) -> String {
        let prefix: String = {
            switch sat.constellation {
            case .galileo: return "E"
            case .gps: return "G"
            case .glonass: return "R"
            case .beidou: return "B"
            }
        }()
        return String(format: "%@%02d", prefix, sat.svid)
    }

    private func satColor(_ sat: SatelliteInfo) -> Color {
        switch sat.constellation {
        case .galileo: return .cyan
        case .gps: return .blue
        case .glonass: return .red
        case .beidou: return .purple
        }
    }
}
