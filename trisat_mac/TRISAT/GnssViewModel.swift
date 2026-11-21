import Foundation
import CoreLocation
import Combine

@MainActor
class GnssViewModel: ObservableObject {
    private let repository: GnssRepository
    let bluetoothManager: BluetoothManager
    let tleDownloader = TleDownloader()
    private var cancellables = Set<AnyCancellable>()
    
    @Published var locationText: String = "Waiting for GNSS coordinates..."
    @Published var accuracyText: String = "Acc: ±-- m"
    @Published var currentCoordinate: CLLocationCoordinate2D? = nil
    @Published var hasLocationFix: Bool = false
    @Published var messages: [GalileoINavMessage] = []

    // Mode: 0 = Internal + TLE, 1 = External BLE / USB Bridge, 2 = TLE Simulation
    @Published var selectedMode: Int = 1
    var externalSatellites: [SatelliteInfo] = []
    var skySourceLabel: String {
        selectedMode == 1 && !externalSatellites.isEmpty ? "EXTERNAL TELEMETRY" : "PREDICTED SKY"
    }
    @Published var timeOffsetHours: Double = 0.0

    // Default reference coordinate for orbital simulation: Fuentecén (Ribera del Duero / Burgos, ES)
    static let fuentecenCoordinate = CLLocationCoordinate2D(latitude: 41.6378, longitude: -3.8689)

    var effectiveCoordinate: CLLocationCoordinate2D? {
        currentCoordinate ?? (selectedMode == 2 ? Self.fuentecenCoordinate : nil)
    }

    var hasTleData: Bool {
        !tleDownloader.parsedTles.isEmpty
    }

    var isAwaitingLocation: Bool {
        if selectedMode == 2 { return false }
        return currentCoordinate == nil && (selectedMode != 1 || externalSatellites.isEmpty)
    }

    var isAwaitingTle: Bool {
        (selectedMode != 1 || externalSatellites.isEmpty) && tleDownloader.parsedTles.isEmpty
    }

    // Constellation Filters
    @Published var isGalileoEnabled: Bool = true
    @Published var isGpsEnabled: Bool = true
    @Published var isGlonassEnabled: Bool = true
    @Published var isBeidouEnabled: Bool = true

    // Differential & NMEA Telemetry Metrics
    @Published var fixTypeDesc: String = "Autonomous GNSS"
    @Published var numSatellitesInFix: Int = 0
    @Published var hdopValue: String = "--"
    @Published var pdopValue: String = "--"
    @Published var vdopValue: String = "--"
    @Published var dgpsAge: String = "N/A"
    @Published var dgpsStationId: String = "N/A"
    @Published var gstRmsError: String = "N/A"
    @Published var gstLatLonErr: String = "N/A"

    // Computed Satellites for Skyplot & SNR Bar Chart
    @Published var displayedSatellites: [SatelliteInfo] = []

    // Assisted OSNMA Client State (Open-Source HTTP JSON API)
    private let assistedOsnmaClient = AssistedOsnmaClient()
    private var networkClockOffsetSeconds: TimeInterval = 0.0
    @Published var assistedOsnmaUrl: String = "http://127.0.0.1:8095"
    @Published var isAssistedOsnmaConnected: Bool = false
    @Published var assistedOsnmaStatusText: String = "Disconnected"
    @Published var assistedGstBanner: String = "Independent GST: Unsynced"
    @Published var assistedBitwiseBanner: String = "Bitwise Cross-Check: Standby"
    @Published var assistedOsnmaMaterial: AssistedOsnmaMaterial? = nil
    @Published var assistedOsnmaAuthStatus: OsnmaAuthStatus = .notAvailable
    @Published var isTimeSpoofingSuspected: Bool = false
    @Published var bitwiseMatchedCount: Int = 0
    @Published var bitwiseMismatchCount: Int = 0
    @Published var lastBitwiseReason: String? = nil

    // Persistent Anti-Flickering Satellite Store (Key: SatID, Value: (Info, LastSeenTimestamp))
    private var satelliteStore: [String: (info: SatelliteInfo, lastSeen: Date)] = [:]

    init(repository: GnssRepository = GnssRepository(), bluetoothManager: BluetoothManager = BluetoothManager()) {
        self.repository = repository
        self.bluetoothManager = bluetoothManager
        setupBindings()
    }
    
    func start() {
        repository.requestPermissions()
        updateSatellites()
    }
    
    func stop() {
        repository.stopLocationUpdates()
    }

    func clearMessages() {
        self.messages.removeAll()
        self.repository.clearMessages()
        self.bitwiseMatchedCount = 0
        self.bitwiseMismatchCount = 0
        self.lastBitwiseReason = nil
        self.assistedOsnmaAuthStatus = .notAvailable
    }

    func connectAssistedOsnma() {
        let targetUrl = assistedOsnmaUrl.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !targetUrl.isEmpty else { return }
        assistedOsnmaStatusText = "Connecting..."
        Task {
            do {
                let gst = try await assistedOsnmaClient.fetchGst(baseUrl: targetUrl)
                let mat = try await assistedOsnmaClient.fetchAssistMaterial(baseUrl: targetUrl)
                let formatter = ISO8601DateFormatter()
                formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
                if let remoteDate = formatter.date(from: gst.utcIso) ?? ISO8601DateFormatter().date(from: gst.utcIso) {
                    self.networkClockOffsetSeconds = remoteDate.timeIntervalSince(Date())
                }
                self.isAssistedOsnmaConnected = true
                self.assistedOsnmaMaterial = mat
                self.assistedOsnmaStatusText = "Connected (\(targetUrl))"
                self.assistedGstBanner = "GST WN \(gst.weekNumber) · TOW \(gst.towSeconds)s (\(gst.clockSource))"
                self.assistedBitwiseBanner = "Bitwise 40-bit OSNMA armed · 0 matched · 0 mismatch"
            } catch {
                self.isAssistedOsnmaConnected = false
                self.assistedOsnmaStatusText = "Connection failed"
            }
        }
    }

    func disconnectAssistedOsnma() {
        isAssistedOsnmaConnected = false
        assistedOsnmaStatusText = "Disconnected"
        assistedBitwiseBanner = "Bitwise Cross-Check: Standby"
    }

    func handleInavMessage(_ msg: GalileoINavMessage) {
        var enriched = msg
        let parsed: ParsedGalileoINavPage?
        if msg.data.count >= 32 {
            parsed = GalileoINavBitParser.parseUbxSfrbx228(svid: msg.svid, data: msg.data)
        } else if msg.data.count >= 30 {
            parsed = GalileoINavBitParser.parseFull240Bit(svid: msg.svid, data: msg.data)
        } else {
            parsed = nil
        }

        if let p = parsed, p.crcValid {
            enriched.crcValid = true
            if p.hasNonZeroOsnma {
                enriched.osnma40Hex = p.osnma40Hex
            }
            if let gst = p.decodedGst {
                enriched.gstWeek = gst.weekNumber
                enriched.gstTow = gst.towSeconds
                let netGst = GstTime.fromUtc(Date().addingTimeInterval(networkClockOffsetSeconds))
                let drift = abs(Double(gst.totalSeconds - netGst.totalSeconds))
                self.isTimeSpoofingSuspected = drift > 5.0
                self.assistedGstBanner = String(format: "GST %@ | Δt(RX-Net): %.1fs%@", gst.formatted, drift, drift > 5.0 ? " [TIME-SPOOFING ALERT]" : " [SYNC OK]")
            }
        }

        if self.messages.count >= 80 {
            self.messages.removeFirst()
        }
        self.messages.append(enriched)
        if self.assistedOsnmaAuthStatus == .notAvailable {
            self.assistedOsnmaAuthStatus = .unverified
        }

        if self.isAssistedOsnmaConnected, let osnmaHex = enriched.osnma40Hex, osnmaHex.count == 10 {
            let targetUrl = self.assistedOsnmaUrl
            let gstSec: Int64
            if let wn = enriched.gstWeek, let tow = enriched.gstTow {
                gstSec = GstTime(weekNumber: wn, towSeconds: tow).totalSeconds
            } else {
                gstSec = GstTime.fromUtc(Date().addingTimeInterval(self.networkClockOffsetSeconds)).totalSeconds
            }
            Task {
                if let res = try? await self.assistedOsnmaClient.checkBitwise(
                    baseUrl: targetUrl,
                    svid: enriched.svid,
                    gstSeconds: gstSec,
                    osnma40Hex: osnmaHex
                ), res.hasReference {
                    if res.matched {
                        self.bitwiseMatchedCount += 1
                        if self.bitwiseMismatchCount == 0 && self.assistedOsnmaAuthStatus != .authenticated {
                            self.assistedOsnmaAuthStatus = .bitwiseVerified
                        }
                    } else {
                        self.bitwiseMismatchCount += 1
                        self.assistedOsnmaAuthStatus = .tamperDetected
                    }
                    self.lastBitwiseReason = res.reason
                    self.assistedBitwiseBanner = "Bitwise 40-bit OSNMA: \(self.bitwiseMatchedCount) matched · \(self.bitwiseMismatchCount) mismatch"
                }
            }
        }
    }

    private func parseGalinavSentence(_ line: String) {
        let parts = line.components(separatedBy: ",")
        guard parts.count >= 4,
              let svid = Int(parts[1]), (1...36).contains(svid),
              let wt = Int(parts[2]),
              let payload = GalileoINavMessage.decodeHexPayload(parts[3].components(separatedBy: "*")[0])
        else { return }
        var osnmaHex: String? = nil
        if parts.count >= 5 {
            let candidate = parts[4].components(separatedBy: "*")[0].trimmingCharacters(in: .whitespacesAndNewlines).uppercased()
            if candidate.count == 10 {
                osnmaHex = candidate
            }
        }
        let msg = GalileoINavMessage(
            svid: svid,
            submessageId: wt,
            data: payload,
            status: 0,
            timestamp: Date(),
            osnma40Hex: osnmaHex
        )
        handleInavMessage(msg)
    }

    func updateSatellites() {
        let allSats: [SatelliteInfo]
        if selectedMode == 1 && !externalSatellites.isEmpty {
            allSats = externalSatellites
        } else if tleDownloader.parsedTles.isEmpty {
            allSats = []
        } else if let coord = effectiveCoordinate {
            let offset = (selectedMode == 2) ? timeOffsetHours : 0.0
            allSats = Sgp4OrbitPropagator.propagateToSkyplot(
                tles: tleDownloader.parsedTles,
                timeOffsetHours: offset,
                observerLocation: coord
            )
        } else {
            allSats = []
        }

        displayedSatellites = allSats.filter { sat in
            switch sat.constellation {
            case .galileo: return isGalileoEnabled
            case .gps: return isGpsEnabled
            case .glonass: return isGlonassEnabled
            case .beidou: return isBeidouEnabled
            }
        }

        if selectedMode == 2 && currentCoordinate == nil {
            locationText = "41.637800° N, 3.868900° W (Fuentecén, ES)"
            accuracyText = "Simulation Reference Base (Ribera del Duero · 837 m MSL)"
        }
    }
    
    private func setupBindings() {
        repository.$lastLocation
            .compactMap { $0 }
            .receive(on: DispatchQueue.main)
            .sink { [weak self] loc in
                let lat = loc.coordinate.latitude
                let lon = loc.coordinate.longitude

                let latDir = lat >= 0 ? "N" : "S"
                let lonDir = lon >= 0 ? "E" : "W"

                if self?.selectedMode == 0 || self?.currentCoordinate == nil {
                    self?.currentCoordinate = loc.coordinate
                    self?.hasLocationFix = true
                    self?.locationText = String(format: "%.6f° %@, %.6f° %@", abs(lat), latDir, abs(lon), lonDir)
                    self?.accuracyText = String(format: "Acc: ±%.1f m | Alt: %.1f m", loc.horizontalAccuracy, loc.altitude)
                    self?.updateSatellites()
                }
            }
            .store(in: &cancellables)
        
        repository.$capturedMessages
            .receive(on: DispatchQueue.main)
            .assign(to: &$messages)

        bluetoothManager.onNmeaLineReceived = { [weak self] line in
            guard let self = self else { return }

            // 1. Parse $GPGGA / $GNGGA (Coordinates, Fix Quality, DGPS Age, Station ID)
            if line.contains("$GPGGA") || line.contains("$GNGGA") {
                self.parseGgaSentence(line)
            }

            // 2. Parse $GPGST (Pseudorange Noise & RMS Error Statistics)
            if line.contains("$GPGST") || line.contains("$GNGST") {
                self.parseGstSentence(line)
            }

            // 3. Parse $GPGSA / $GNGSA (DOP Dilution of Precision)
            if line.contains("$GPGSA") || line.contains("$GNGSA") {
                self.parseGsaSentence(line)
            }

            // 4. Parse $GAGSV / $GPGSV / $GLGSV / $BDGSV (Satellites with Anti-Flicker)
            if line.contains("$GAGSV") || line.contains("$GPGSV") || line.contains("$GLGSV") || line.contains("$BDGSV") {
                self.parseGsvLine(line)
            }

            // 5. Parse $GALINAV / $RAWNAV (Galileo I/NAV & OSNMA)
            if line.contains("$GALINAV") || line.contains("$RAWNAV") {
                self.parseGalinavSentence(line)
            }
        }

        Publishers.CombineLatest4($selectedMode, $timeOffsetHours, $isGalileoEnabled, $isGpsEnabled)
            .receive(on: DispatchQueue.main)
            .sink { [weak self] mode, _, _, _ in
                if mode == 1 {
                    self?.repository.clearMessages()
                }
                self?.updateSatellites()
            }
            .store(in: &cancellables)

        Publishers.CombineLatest($isGlonassEnabled, $isBeidouEnabled)
            .receive(on: DispatchQueue.main)
            .sink { [weak self] _ in self?.updateSatellites() }
            .store(in: &cancellables)
    }

    private func parseGgaSentence(_ line: String) {
        let parts = line.components(separatedBy: ",")
        if parts.count >= 10 {
            if let lat = parseNmeaCoord(parts[2], dir: parts[3]),
               let lon = parseNmeaCoord(parts[4], dir: parts[5]) {
                let fixQuality = parts[6]
                let numSats = Int(parts[7]) ?? 0
                let hdop = parts[8]
                let alt = parts[9]

                self.numSatellitesInFix = numSats
                self.hdopValue = hdop.isEmpty ? "--" : hdop

                self.fixTypeDesc = {
                    switch fixQuality {
                    case "1": return "GPS Autonomous"
                    case "2": return "DGPS Differential (SXblue)"
                    case "4": return "RTK Fixed (Centimeter)"
                    case "5": return "RTK Float"
                    case "6": return "Dead Reckoning"
                    default: return "Fix Mode \(fixQuality)"
                    }
                }()

                if parts.count >= 14 {
                    let age = parts[11]
                    let station = parts[12].components(separatedBy: "*")[0]
                    self.dgpsAge = age.isEmpty ? "N/A" : "\(age)s"
                    self.dgpsStationId = station.isEmpty ? "N/A" : station
                }

                self.currentCoordinate = CLLocationCoordinate2D(latitude: lat, longitude: lon)
                self.hasLocationFix = true
                let latDir = lat >= 0 ? "N" : "S"
                let lonDir = lon >= 0 ? "E" : "W"
                self.locationText = String(format: "%.6f° %@, %.6f° %@", abs(lat), latDir, abs(lon), lonDir)
                self.accuracyText = "Fix: \(fixTypeDesc) | Sats: \(numSats) | HDOP: \(hdopValue) | Alt: \(alt)m"
            }
        }
    }

    private func parseGstSentence(_ line: String) {
        let parts = line.components(separatedBy: ",")
        if parts.count >= 8 {
            let rms = parts[3]
            let latErr = parts[6]
            let lonErr = parts[7].components(separatedBy: "*")[0]

            if let rmsVal = Double(rms) {
                self.gstRmsError = String(format: "±%.2f m", rmsVal)
            }
            if let latE = Double(latErr), let lonE = Double(lonErr) {
                self.gstLatLonErr = String(format: "Lat ±%.2fm, Lon ±%.2fm", latE, lonE)
            }
        }
    }

    private func parseGsaSentence(_ line: String) {
        let parts = line.components(separatedBy: ",")
        if parts.count >= 17 {
            let pdop = parts[15]
            let hdop = parts[16]
            let vdop = parts.count >= 18 ? parts[17].components(separatedBy: "*")[0] : "--"

            self.pdopValue = pdop.isEmpty ? "--" : pdop
            self.hdopValue = hdop.isEmpty ? "--" : hdop
            self.vdopValue = vdop.isEmpty ? "--" : vdop
        }
    }

    private func parseGsvLine(_ text: String) {
        let parts = text.components(separatedBy: ",")
        guard parts.count >= 8 else { return }

        let constKind: GnssConstellation
        if text.contains("$GAGSV") { constKind = .galileo }
        else if text.contains("$GPGSV") { constKind = .gps }
        else if text.contains("$GLGSV") { constKind = .glonass }
        else { constKind = .beidou }

        let now = Date()
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
                let key = "\(constKind.rawValue)_\(svid)"
                let sat = SatelliteInfo(
                    id: key,
                    svid: svid,
                    elevation: elev,
                    azimuth: azim,
                    cno: cno,
                    isGalileo: constKind == .galileo,
                    constellation: constKind,
                    usedInFix: cno > 20,
                    hasInav: constKind == .galileo
                )
                satelliteStore[key] = (info: sat, lastSeen: now)
            }
            idx += 4
        }

        // Retain satellites for 30 seconds to prevent any screen flickering
        let activeThreshold = Date().addingTimeInterval(-30.0)
        satelliteStore = satelliteStore.filter { $0.value.lastSeen > activeThreshold }

        let activeSats = satelliteStore.values.map { $0.info }

        // Keep received satellites separate from the predicted sky.
        externalSatellites = activeSats.sorted {
            if $0.constellation != $1.constellation {
                return $0.constellation.rawValue < $1.constellation.rawValue
            }
            return $0.svid < $1.svid
        }
        updateSatellites()
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
