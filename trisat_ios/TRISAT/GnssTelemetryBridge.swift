import Foundation
import Network
import CoreLocation
import Combine
#if os(macOS)
import AppKit
#endif

/// High-performance UDP Telemetry Bridge service utilizing Apple Network.framework NWListener
class GnssTelemetryBridge: ObservableObject {
    @Published var isListening: Bool = false
    @Published var listenerPort: UInt16 = 9090
    @Published var statusText: String = "SXblue / TRISAT-SDR Bridge Idle (Port 9090)"
    @Published var packetsReceivedCount: Int = 0
    @Published var isSdrEngineRunning: Bool = false
    @Published var selectedSdrBand2: String = "GAL_E6"

    private var listener: NWListener?
    private weak var viewModel: GnssViewModel?
    private weak var bluetoothManager: BluetoothManager?
    #if os(macOS)
    private var sdrProcess: Process?
    #endif

    init(viewModel: GnssViewModel? = nil, bluetoothManager: BluetoothManager? = nil) {
        self.viewModel = viewModel
        self.bluetoothManager = bluetoothManager
        startUdpListener(port: 9090)
    }

    func configure(viewModel: GnssViewModel, bluetoothManager: BluetoothManager) {
        self.viewModel = viewModel
        self.bluetoothManager = bluetoothManager
    }

    // MARK: - Native macOS TRISAT-SDR Engine Launcher & Controller

    func startTrisatSdrEngine(band2: String? = nil) {
        #if os(macOS)
        let targetBand = band2 ?? selectedSdrBand2
        selectedSdrBand2 = targetBand

        // Stop any previous instance cleanly before starting
        stopTrisatSdrEngine(quiet: true)

        let scriptPath = "/Users/jreyes13/Documents/GitHub/TRISAT/trisat_sdr/main.py"
        let proc = Process()
        proc.executableURL = URL(fileURLWithPath: "/usr/bin/python3")
        proc.arguments = [scriptPath, "--band2", targetBand, "--web-port", "8085"]
        proc.currentDirectoryURL = URL(fileURLWithPath: "/Users/jreyes13/Documents/GitHub/TRISAT")
        proc.standardOutput = FileHandle.nullDevice
        proc.standardError = FileHandle.nullDevice

        proc.terminationHandler = { [weak self] _ in
            DispatchQueue.main.async {
                self?.isSdrEngineRunning = false
            }
        }

        do {
            try proc.run()
            self.sdrProcess = proc
            self.isSdrEngineRunning = true
            if !isListening {
                startUdpListener(port: 9090)
            }
            self.statusText = "TRISAT-SDR Active (E1 OSNMA + \(targetBand)) [UDP :9090 | Web :8085]"
        } catch {
            self.statusText = "Failed to launch TRISAT-SDR: \(error.localizedDescription)"
        }
        #endif
    }

    func stopTrisatSdrEngine(quiet: Bool = false) {
        #if os(macOS)
        if let proc = sdrProcess, proc.isRunning {
            proc.terminate()
        }
        sdrProcess = nil

        // Also stop any background trisat_sdr/main.py process started externally
        let killProc = Process()
        killProc.executableURL = URL(fileURLWithPath: "/usr/bin/pkill")
        killProc.arguments = ["-f", "trisat_sdr/main.py"]
        try? killProc.run()
        killProc.waitUntilExit()

        isSdrEngineRunning = false
        if !quiet {
            statusText = "TRISAT-SDR Engine Stopped (UDP Listener Ready :\(listenerPort))"
        }
        #endif
    }

    func switchSdrSecondaryBand(_ band: String) {
        selectedSdrBand2 = band
        #if os(macOS)
        if let url = URL(string: "http://127.0.0.1:8085/api/set_band?band=\(band)") {
            var req = URLRequest(url: url)
            req.httpMethod = "POST"
            URLSession.shared.dataTask(with: req).resume()
        }
        if !isSdrEngineRunning && packetsReceivedCount == 0 {
            startTrisatSdrEngine(band2: band)
        } else {
            statusText = "TRISAT-SDR Active (E1 OSNMA + \(band)) [UDP :9090 | Web :8085]"
        }
        #endif
    }

    func openWebDashboard() {
        #if os(macOS)
        if !isSdrEngineRunning && packetsReceivedCount == 0 {
            startTrisatSdrEngine(band2: selectedSdrBand2)
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.6) {
                if let url = URL(string: "http://127.0.0.1:8085") {
                    NSWorkspace.shared.open(url)
                }
            }
        } else if let url = URL(string: "http://127.0.0.1:8085") {
            NSWorkspace.shared.open(url)
        }
        #endif
    }

    func startUdpListener(port: UInt16 = 9090) {
        stopUdpListener()
        self.listenerPort = port

        do {
            let params = NWParameters.udp
            params.allowLocalEndpointReuse = true

            guard let nwPort = NWEndpoint.Port(rawValue: port) else { return }
            let newListener = try NWListener(using: params, on: nwPort)

            newListener.stateUpdateHandler = { [weak self] state in
                DispatchQueue.main.async {
                    switch state {
                    case .ready:
                        self?.isListening = true
                        self?.statusText = "Listening on UDP 127.0.0.1:\(port) [SXblue Bridge Active]"
                    case .failed(let err):
                        self?.isListening = false
                        self?.statusText = "UDP Bridge Failed: \(err.localizedDescription)"
                    case .cancelled:
                        self?.isListening = false
                        self?.statusText = "UDP Bridge Cancelled"
                    default:
                        break
                    }
                }
            }

            newListener.newConnectionHandler = { [weak self] connection in
                self?.handleIncomingConnection(connection)
            }

            newListener.start(queue: .global(qos: .userInitiated))
            self.listener = newListener

        } catch {
            DispatchQueue.main.async {
                self.statusText = "Failed to start UDP Listener: \(error.localizedDescription)"
            }
        }
    }

    func stopUdpListener() {
        listener?.cancel()
        listener = nil
        DispatchQueue.main.async {
            self.isListening = false
            self.statusText = "UDP Bridge Stopped"
        }
    }

    private func handleIncomingConnection(_ connection: NWConnection) {
        connection.start(queue: .global(qos: .userInitiated))
        receiveNextPacket(connection)
    }

    private func receiveNextPacket(_ connection: NWConnection) {
        connection.receiveMessage { [weak self] content, context, isComplete, error in
            if let data = content, !data.isEmpty {
                DispatchQueue.main.async {
                    self?.packetsReceivedCount += 1
                    self?.processIncomingTelemetryData(data)
                }
            }
            if error == nil {
                self?.receiveNextPacket(connection)
            }
        }
    }

    private func processIncomingTelemetryData(_ data: Data) {
        // Parse JSON datagram from read_sxblue.py or trisat_sdr
        if let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any] {
            if let payload = json["data"] as? String {
                parseNmeaOrBinText(payload)
            }
            if let packetType = json["type"] as? String {
                handleTrisatSdrJsonPacket(packetType: packetType, json: json)
            } else if let hexPayload = json["hex"] as? String {
                bluetoothManager?.logDebug("UDP Received $BIN Hex: \(hexPayload.prefix(40))...")
            }
        } else if let rawString = String(data: data, encoding: .ascii) ?? String(data: data, encoding: .utf8) {
            let lines = rawString.components(separatedBy: .newlines)
            for line in lines {
                let trimmed = line.trimmingCharacters(in: .whitespacesAndNewlines)
                if !trimmed.isEmpty {
                    if trimmed.hasPrefix("{"),
                       let lineData = trimmed.data(using: .utf8),
                       let json = try? JSONSerialization.jsonObject(with: lineData) as? [String: Any],
                       let packetType = json["type"] as? String {
                        handleTrisatSdrJsonPacket(packetType: packetType, json: json)
                    } else {
                        parseNmeaOrBinText(trimmed)
                    }
                }
            }
        }
    }

    private func handleTrisatSdrJsonPacket(packetType: String, json: [String: Any]) {
        bluetoothManager?.isConnected = true
        bluetoothManager?.connectedPeripheralName = "TRISAT-SDR Dual HackRF (UDP :\(listenerPort))"

        if packetType == "inav_frame" || packetType == "gnss_frame" {
            let svid = json["svid"] as? Int ?? 1
            let submsgId = json["submsgId"] as? Int ?? 1
            let hexStr = json["hex"] as? String ?? ""
            let payloadData = hexStringToData(hexStr) ?? Data((0..<30).map { _ in UInt8.random(in: 0...255) })
            let msg = GalileoINavMessage(
                svid: svid,
                submessageId: submsgId,
                data: payloadData,
                status: 0,
                timestamp: Date()
            )
            if viewModel?.messages.count ?? 0 >= 100 {
                viewModel?.messages.removeFirst()
            }
            viewModel?.messages.append(msg)
        } else if packetType == "location" {
            if let lat = json["lat"] as? Double, let lon = json["lon"] as? Double {
                let alt = json["alt"] as? Double ?? 0.0
                let acc = json["acc"] as? Double ?? 1.0
                let mode = json["mode"] as? String ?? "TRISAT-SDR"
                viewModel?.currentCoordinate = CLLocationCoordinate2D(latitude: lat, longitude: lon)
                let latDir = lat >= 0 ? "N" : "S"
                let lonDir = lon >= 0 ? "E" : "W"
                viewModel?.locationText = String(format: "%.6f° %@, %.6f° %@", abs(lat), latDir, abs(lon), lonDir)
                viewModel?.accuracyText = String(format: "%@ | Acc: ±%.2f m | Alt: %.1f m", mode, acc, alt)
            }
        }
    }

    private func hexStringToData(_ hex: String) -> Data? {
        let clean = hex.trimmingCharacters(in: .whitespacesAndNewlines)
        guard clean.count >= 2 && clean.count % 2 == 0 else { return nil }
        var data = Data(capacity: clean.count / 2)
        var index = clean.startIndex
        while index < clean.endIndex {
            let nextIndex = clean.index(index, offsetBy: 2)
            if let b = UInt8(clean[index..<nextIndex], radix: 16) {
                data.append(b)
            } else {
                return nil
            }
            index = nextIndex
        }
        return data
    }

    private func parseNmeaOrBinText(_ text: String) {
        // 1. Update BluetoothManager NMEA Buffer & Log
        if bluetoothManager?.nmeaSentences.count ?? 0 >= 100 {
            bluetoothManager?.nmeaSentences.removeFirst()
        }
        bluetoothManager?.nmeaSentences.append(text)
        bluetoothManager?.onNmeaLineReceived?(text)
        bluetoothManager?.isConnected = true
        bluetoothManager?.connectedPeripheralName = "SXBlue II GPS (USB UDP Bridge :9090)"

        // 2. Parse $GPGGA / $GNGGA for Position, Fix Quality, and Altitude
        if text.contains("$GPGGA") || text.contains("$GNGGA") {
            let parts = text.components(separatedBy: ",")
            if parts.count >= 10 {
                if let lat = parseNmeaCoord(parts[2], dir: parts[3]),
                   let lon = parseNmeaCoord(parts[4], dir: parts[5]) {
                    let fixQuality = parts[6]
                    let numSats = parts[7]
                    let hdop = parts[8]
                    let alt = parts[9]

                    let fixDesc: String = {
                        switch fixQuality {
                        case "1": return "GPS Fix"
                        case "2": return "DGPS Fix (SXblue)"
                        case "4": return "RTK Fixed"
                        case "5": return "RTK Float"
                        default: return "Fix Quality \(fixQuality)"
                        }
                    }()

                    viewModel?.currentCoordinate = CLLocationCoordinate2D(latitude: lat, longitude: lon)
                    let latDir = lat >= 0 ? "N" : "S"
                    let lonDir = lon >= 0 ? "E" : "W"
                    viewModel?.locationText = String(format: "%.6f° %@, %.6f° %@", abs(lat), latDir, abs(lon), lonDir)
                    viewModel?.accuracyText = "Fix: \(fixDesc) | Sats: \(numSats) | HDOP: \(hdop) | Alt: \(alt)m"
                }
            }
        }

        // 3. Parse $GAGSV / $GPGSV for Galileo & GPS Satellites (PRN, Elev, Azim, C/N0)
        if text.contains("$GAGSV") || text.contains("$GPGSV") {
            parseGsvSentence(text)
        }

        // 4. Parse $GALINAV / $GALE6HAS / $GALE5 / $BIN / Hemisphere Binary Messages 94 & 95
        if text.contains("$GALINAV") || text.contains("$GALE6HAS") || text.contains("$GALE5") || text.contains("$BIN") || text.contains("$RAWNAV") {
            parseInavOrBinarySentence(text)
        }
    }

    private func parseGsvSentence(_ text: String) {
        let parts = text.components(separatedBy: ",")
        guard parts.count >= 8 else { return }

        let isGalileo = text.contains("$GAGSV")
        let constKind: GnssConstellation = isGalileo ? .galileo : .gps

        var parsedSats: [SatelliteInfo] = []
        var idx = 4
        while idx + 3 < parts.count {
            let svidStr = parts[idx]
            let elevStr = parts[idx+1]
            let azimStr = parts[idx+2]
            let cnoStr = parts[idx+3].components(separatedBy: "*")[0]

            if let svid = Int(svidStr),
               let elev = Double(elevStr),
               let azim = Double(azimStr),
               let cno = Int(cnoStr) {
                let sat = SatelliteInfo(
                    id: "\(constKind.rawValue)_UDP_\(svid)",
                    svid: svid,
                    elevation: elev,
                    azimuth: azim,
                    cno: cno,
                    isGalileo: isGalileo,
                    constellation: constKind,
                    usedInFix: true,
                    hasInav: isGalileo
                )
                parsedSats.append(sat)
            }
            idx += 4
        }

        if !parsedSats.isEmpty {
            if var current = viewModel?.displayedSatellites {
                current.removeAll(where: { $0.constellation == constKind })
                current.append(contentsOf: parsedSats)
                viewModel?.displayedSatellites = current
            } else {
                viewModel?.displayedSatellites = parsedSats
            }
        }
    }

    private func parseInavOrBinarySentence(_ text: String) {
        let cleanText = text.components(separatedBy: "*")[0]
        let parts = cleanText.components(separatedBy: ",")
        let svid = parts.count > 1 ? (Int(parts[1]) ?? 1) : 1
        let submsgId = parts.count > 2 ? (Int(parts[2]) ?? 1) : 1

        var frameData: Data? = nil
        if parts.count > 3 {
            frameData = hexStringToData(parts[3])
        }
        if frameData == nil && parts.count > 4 {
            frameData = hexStringToData(parts[4])
        }
        let finalData = frameData ?? Data((0..<30).map { _ in UInt8.random(in: 0...255) })

        let msg = GalileoINavMessage(
            svid: svid,
            submessageId: submsgId,
            data: finalData,
            status: 0,
            timestamp: Date()
        )

        if viewModel?.messages.count ?? 0 >= 100 {
            viewModel?.messages.removeFirst()
        }
        viewModel?.messages.append(msg)
    }

    private func parseNmeaCoord(_ rawVal: String, dir: String) -> Double? {
        guard let num = Double(rawVal), num > 0 else { return nil }
        let deg = floor(num / 100.0)
        let min = num - (deg * 100.0)
        var decimal = deg + (min / 60.0)
        if dir == "S" || dir == "W" { decimal = -decimal }
        return decimal
    }
}
