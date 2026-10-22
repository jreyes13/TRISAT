import SwiftUI

struct SignalProcessingView: View {
    @ObservedObject var viewModel: GnssViewModel
    @ObservedObject var telemetryBridge: GnssTelemetryBridge
    @Environment(\.horizontalSizeClass) var horizontalSizeClass

    init(viewModel: GnssViewModel, telemetryBridge: GnssTelemetryBridge = GnssTelemetryBridge()) {
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
            VStack(alignment: .leading, spacing: 12) {
                // MARK: - Clean Read-Only Telemetry Header & Metrics
                topMetricsHeader

                if isWideLayout {
                    // macOS & iPad: Clean 2x2 Grid Fitted to Desktop Screen
                    VStack(spacing: 12) {
                        // ROW 1: Compact Open Skyplot + C/N0 SNR Bar Chart
                        HStack(alignment: .top, spacing: 14) {
                            openSkyplotCard
                                .frame(width: 260)

                            snrBarChartCard
                                .frame(maxWidth: .infinity)
                        }

                        // ROW 2: NMEA Live Terminal + Galileo I/NAV Subframe Cards
                        HStack(alignment: .top, spacing: 14) {
                            nmeaStreamCard
                                .frame(maxWidth: .infinity)

                            galileoInavCard
                                .frame(maxWidth: .infinity)
                        }
                    }
                    .padding(.horizontal, 16)
                } else {
                    // Compact Vertical Flow
                    VStack(spacing: 14) {
                        openSkyplotCard
                        snrBarChartCard
                        nmeaStreamCard
                        galileoInavCard
                    }
                    .padding(.horizontal)
                }
            }
            .padding(.bottom, 16)
        }
    }

    // MARK: - Header: Clean Telemetry Status & Differential Metrics (Config Moved to Settings)

    private var topMetricsHeader: some View {
        VStack(alignment: .leading, spacing: 8) {
            if isWideLayout {
                // Desktop / iPad: Wide Single-Row Header
                HStack {
                    sourceStatusBlock
                    Spacer()
                    constellationFilterChips
                }
            } else {
                // iPhone: 2-Row Header (Status on Top, Filter Chips below)
                VStack(alignment: .leading, spacing: 6) {
                    sourceStatusBlock
                    constellationFilterChips
                }
            }

            // Differential & Precision Metrics Badges
            if isWideLayout {
                HStack(spacing: 10) {
                    metricsGrid
                }
            } else {
                LazyVGrid(columns: [GridItem(.flexible()), GridItem(.flexible())], spacing: 6) {
                    metricsGrid
                }
            }
        }
        .padding(12)
        .background(Color.gray.opacity(0.1))
        .cornerRadius(12)
        .padding(.horizontal, 16)
        .padding(.top, 8)
    }

    private var sourceStatusBlock: some View {
        HStack(spacing: 8) {
            Image(systemName: viewModel.bluetoothManager.isConnected ? "checkmark.circle.fill" : "antenna.radiowaves.left.and.right")
                .font(.title3)
                .foregroundColor(viewModel.bluetoothManager.isConnected ? .green : .cyan)

            VStack(alignment: .leading, spacing: 2) {
                Text(viewModel.bluetoothManager.isConnected ? "Active Source: \(viewModel.bluetoothManager.connectedPeripheralName)" : "Source: Internal Receiver / UDP Bridge (:9090)")
                    .font(.subheadline)
                    .bold()
                    .lineLimit(1)
                Text("Multi-Constellation Telemetry, C/N0 & I/NAV Monitor")
                    .font(.caption2)
                    .foregroundColor(.secondary)
            }
        }
    }

    private var constellationFilterChips: some View {
        HStack(spacing: 6) {
            FilterToggleChip(title: "Galileo", iconName: "globe.europe.africa.fill", isSelected: $viewModel.isGalileoEnabled, color: .cyan)
            FilterToggleChip(title: "GPS", iconName: "location.north.line.fill", isSelected: $viewModel.isGpsEnabled, color: .orange)
            FilterToggleChip(title: "GLONASS", iconName: "antenna.radiowaves.left.and.right", isSelected: $viewModel.isGlonassEnabled, color: .red)
            FilterToggleChip(title: "BeiDou", iconName: "star.fill", isSelected: $viewModel.isBeidouEnabled, color: .green)
        }
    }

    @ViewBuilder
    private var metricsGrid: some View {
        metricCard(title: "Fix Mode", value: viewModel.fixTypeDesc, icon: "location.fill", color: .green)
        metricCard(title: "Sats in Fix / HDOP", value: "\(viewModel.numSatellitesInFix) Sats | HDOP \(viewModel.hdopValue)", icon: "satellite.fill", color: .cyan)
        metricCard(title: "Correction Source", value: "ID: \(viewModel.dgpsStationId) (\(viewModel.dgpsAge))", icon: "checkmark.shield.fill", color: .orange)
        metricCard(title: "Pseudorange Error", value: viewModel.gstRmsError, icon: "chart.xyaxis.line", color: .purple)
    }

    private func metricCard(title: String, value: String, icon: String, color: Color) -> some View {
        VStack(alignment: .leading, spacing: 3) {
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
        VStack(alignment: .leading, spacing: 6) {
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

            SkyPlotView(satellites: viewModel.displayedSatellites, height: 195)
                .padding(4)
        }
        .padding(12)
        .background(Color.gray.opacity(0.08))
        .cornerRadius(14)
    }

    // MARK: - Quadrant 2: Satellite C/N0 SNR Bar Chart

    private var snrBarChartCard: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack {
                Image(systemName: "chart.bar.fill")
                    .foregroundColor(.green)
                Text("Signal Strength C/N0 (dB-Hz)")
                    .font(.caption)
                    .fontWeight(.bold)
                    .foregroundColor(.secondary)
                Spacer()
                HStack(spacing: 8) {
                    legendDot(label: "Galileo", color: .cyan)
                    legendDot(label: "GPS", color: .orange)
                    legendDot(label: "GLONASS", color: .red)
                    legendDot(label: "BeiDou", color: .green)
                }
            }

            if viewModel.displayedSatellites.isEmpty {
                VStack(spacing: 12) {
                    Image(systemName: "satellite")
                        .font(.system(size: 30))
                        .foregroundColor(.cyan)
                        .padding(.top, 24)
                    Text("Awaiting Satellite Telemetry...")
                        .font(.caption2)
                        .foregroundColor(.secondary)
                    Spacer()
                }
                .frame(maxWidth: .infinity, minHeight: 195)
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
                                        .frame(width: 20, height: 140)

                                    RoundedRectangle(cornerRadius: 3)
                                        .fill(snrColor(sat.cno))
                                        .frame(width: 20, height: CGFloat(min(max(sat.cno, 0), 54)) * 2.55)
                                }

                                Text(sat.formattedLabel)
                                    .font(.system(size: 8, weight: .bold, design: .monospaced))
                                    .padding(.horizontal, 4)
                                    .padding(.vertical, 2)
                                    .background(sat.constellation.color.opacity(0.22))
                                    .foregroundColor(sat.constellation.color)
                                    .cornerRadius(3)
                            }
                        }
                    }
                    .padding(.vertical, 6)
                    .padding(.horizontal, 8)
                }
                .frame(minHeight: 195)
            }
        }
        .padding(12)
        .background(Color.gray.opacity(0.08))
        .cornerRadius(14)
    }

    private func legendDot(label: String, color: Color) -> some View {
        HStack(spacing: 3) {
            Circle()
                .fill(color)
                .frame(width: 6, height: 6)
            Text(label)
                .font(.system(size: 9, weight: .semibold))
                .foregroundColor(.secondary)
        }
    }

    // MARK: - Quadrant 3: ASCII NMEA Live Stream Terminal

    private var nmeaStreamCard: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack {
                Image(systemName: "terminal.fill")
                    .foregroundColor(.green)
                Text("ASCII NMEA Stream ($GNGGA, $GAGSV, $GNGST, $GALINAV, $GALE6HAS)")
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
                    .buttonStyle(.plain)
                }
            }

            if viewModel.bluetoothManager.nmeaSentences.isEmpty {
                VStack(spacing: 12) {
                    Image(systemName: "terminal")
                        .font(.system(size: 30))
                        .foregroundColor(.cyan)
                        .padding(.top, 24)
                    Text("Waiting for NMEA datagrams...")
                        .font(.caption2)
                        .foregroundColor(.secondary)
                    Spacer()
                }
                .frame(maxWidth: .infinity, minHeight: 210)
                .background(Color.black)
                .cornerRadius(10)
            } else {
                ScrollViewReader { proxy in
                    ScrollView {
                        LazyVStack(alignment: .leading, spacing: 3) {
                            ForEach(Array(viewModel.bluetoothManager.nmeaSentences.enumerated()), id: \.offset) { index, sentence in
                                Text(sentence)
                                    .font(.system(size: 10.5, design: .monospaced))
                                    .foregroundColor(.green)
                                    .id(index)
                            }
                        }
                        .padding(8)
                    }
                    .frame(minHeight: 210, maxHeight: 240)
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
        VStack(alignment: .leading, spacing: 6) {
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
                        .font(.system(size: 30))
                        .foregroundColor(.cyan)
                        .padding(.top, 24)
                    Text("Waiting for 250-bit I/NAV subframes...")
                        .font(.caption2)
                        .foregroundColor(.secondary)
                    Spacer()
                }
                .frame(maxWidth: .infinity, minHeight: 210)
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
                .frame(minHeight: 210, maxHeight: 240)
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
        if cno >= 49 && viewModel.simulatedThreatMode == .spoofing { return .orange }
        if cno >= 35 { return .green }
        if cno >= 25 { return .yellow }
        return .red
    }
}
