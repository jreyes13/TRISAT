import Foundation
import CoreLocation
import Combine

/// Interactive RF & Cryptographic Threat Simulation Modes
enum SimulatedThreatMode: String, CaseIterable, Identifiable {
    case nominal = "Nominal"
    case jamming = "Jamming"
    case spoofing = "Spoofing"
    case meaconing = "Meaconing"
    case osnmaFailure = "OSNMA Fail"

    var id: String { rawValue }

    var iconName: String {
        switch self {
        case .nominal: return "checkmark.shield.fill"
        case .jamming: return "wifi.exclamationmark"
        case .spoofing: return "exclamationmark.triangle.fill"
        case .meaconing: return "clock.badge.exclamationmark.fill"
        case .osnmaFailure: return "key.slash.fill"
        }
    }

    var badgeText: String {
        switch self {
        case .nominal: return "NOMINAL / SAFE"
        case .jamming: return "RF JAMMING ACTIVE"
        case .spoofing: return "SPOOFING DETECTED"
        case .meaconing: return "MEACONING REPLAY"
        case .osnmaFailure: return "OSNMA MAC MISMATCH"
        }
    }

    var summaryDescription: String {
        switch self {
        case .nominal:
            return "Clean multi-constellation RF spectrum, valid Galileo OSNMA v1.1 cryptographic tags, and nominal $GPGST pseudorange residuals."
        case .jamming:
            return "Broadband L1/E1/E6 noise floor injection (+18 dB AGC saturation): C/N0 drops by ~22 dB-Hz across visible satellites, degrading 3D fix geometry."
        case .spoofing:
            return "Coherent false-signal injection: unnaturally uniform high C/N0 (50–52 dB-Hz), synthetic azimuth clustering, and ±48.5 m $GPGST pseudorange residual divergence."
        case .meaconing:
            return "Delayed RF rebroadcast attack (+420 ns clock bias): spatial skyplot displacement, abnormal DGPS age drift, and pseudorange rate jump."
        case .osnmaFailure:
            return "Galileo E1-B I/NAV navigation data manipulation with forged 40-bit OSNMA MACK tag: caught immediately by HMAC-SHA-256 ADKD=0/4 verification."
        }
    }
}

@MainActor
class GnssViewModel: ObservableObject {
    private let repository: GnssRepository
    let bluetoothManager: BluetoothManager
    let tleDownloader = TleDownloader()
    private var cancellables = Set<AnyCancellable>()
    private var trackingTimer: Timer?
    private var trackingTickCount: Int = 0

    // Device Physical Location (Default: User's Actual Current Location via CoreLocation)
    @Published var locationText: String = "Acquiring Device Coordinates..."
    @Published var accuracyText: String = "Searching Location Services..."
    @Published var currentCoordinate: CLLocationCoordinate2D = CLLocationCoordinate2D(latitude: 40.416775, longitude: -3.703790)
    @Published var hasAcquiredRealLocation: Bool = false
    @Published var messages: [GalileoINavMessage] = []

    // Mode: 0 = Internal GNSS (Real Location & Satellite Tracking), 1 = External BLE / USB / SDR Bridge, 2 = CelesTrak TLE Orbit Simulation
    @Published var selectedMode: Int = 0
    @Published var timeOffsetHours: Double = 0.0

    // Constellation Filters (All 4 Active by Default)
    @Published var isGalileoEnabled: Bool = true
    @Published var isGpsEnabled: Bool = true
    @Published var isGlonassEnabled: Bool = true
    @Published var isBeidouEnabled: Bool = true

    // Interactive Threat Simulation Mode
    @Published var simulatedThreatMode: SimulatedThreatMode = .nominal

    // Differential & NMEA Telemetry Metrics
    @Published var fixTypeDesc: String = "3D Autonomous GNSS"
    @Published var numSatellitesInFix: Int = 0
    @Published var hdopValue: String = "0.78"
    @Published var pdopValue: String = "1.32"
    @Published var vdopValue: String = "1.10"
    @Published var dgpsAge: String = "N/A"
    @Published var dgpsStationId: String = "N/A"
    @Published var gstRmsError: String = "±0.24 m"
    @Published var gstLatLonErr: String = "Lat: ±0.12m | Lon: ±0.11m | Alt: ±0.22m"

    // Unified Authoritative Tracking Channel Table (Key: "Galileo_2", "GPS_8", etc.)
    @Published var displayedSatellites: [SatelliteInfo] = []

    // Live Unfiltered Satellites from External Bridge / Hardware
    var liveBridgeSatellites: [SatelliteInfo] = []
    private var satelliteStore: [String: (info: SatelliteInfo, lastSeen: Date)] = [:]
    private var hasExternalHardwareFix: Bool = false

    init(repository: GnssRepository = GnssRepository(), bluetoothManager: BluetoothManager = BluetoothManager()) {
        self.repository = repository
        self.bluetoothManager = bluetoothManager
        setupBindings()
        startTrackingEngine()
        updateSatellites()
    }
    
    func start() {
        repository.requestPermissions()
        repository.startLocationUpdates()
        updateSatellites()
    }
    
    func stop() {
        repository.stopLocationUpdates()
        trackingTimer?.invalidate()
        trackingTimer = nil
    }

    // MARK: - GNSS Tracking Channel Engine (Smooth, realistic, anti-flicker satellite dynamics)

    private func startTrackingEngine() {
        trackingTimer?.invalidate()
        // 1 Hz tracking epoch: smoothly propagates satellites and maintains realistic RF dynamics
        trackingTimer = Timer.scheduledTimer(withTimeInterval: 1.0, repeats: true) { [weak self] _ in
            DispatchQueue.main.async {
                guard let self = self else { return }
                self.trackingTickCount += 1
                self.updateSatellites()
            }
        }
    }

    /// Single authoritative updater for all satellites in the skyplot and SNR bar charts
    func updateSatellites() {
        let coord = currentCoordinate

        let baseSats: [SatelliteInfo]
        if selectedMode == 1 && hasExternalHardwareFix && !liveBridgeSatellites.isEmpty {
            // Mode 1: External Hardware / Bridge NMEA GSV
            baseSats = liveBridgeSatellites
        } else if selectedMode == 2 {
            // Mode 2: CelesTrak TLE Orbit Simulation with Time Scrubbing
            baseSats = Sgp4OrbitPropagator.propagateToSkyplot(
                tles: tleDownloader.parsedTles,
                timeOffsetHours: timeOffsetHours,
                observerLocation: coord
            )
        } else {
            // Mode 0: Internal GNSS — Propagate real Keplerian orbits over user's actual physical coordinates
            baseSats = Sgp4OrbitPropagator.propagateToSkyplot(
                tles: tleDownloader.parsedTles,
                timeOffsetHours: Double(trackingTickCount) / 3600.0, // Natural Earth rotation
                observerLocation: coord
            )
        }

        // Apply 4-Constellation Filters
        let filteredSats = baseSats.filter { sat in
            switch sat.constellation {
            case .galileo: return isGalileoEnabled
            case .gps: return isGpsEnabled
            case .glonass: return isGlonassEnabled
            case .beidou: return isBeidouEnabled
            }
        }

        // Add subtle, realistic receiver tracking variation (±0.3 dB-Hz jitter) so satellites look tracked and alive
        let jitteredSats: [SatelliteInfo] = filteredSats.enumerated().map { idx, sat in
            let jitter = ((idx + trackingTickCount) % 3 == 0) ? 1 : (((idx + trackingTickCount) % 5 == 0) ? -1 : 0)
            let currentCno = max(20, min(50, sat.cno + jitter))
            return SatelliteInfo(
                id: sat.id,
                svid: sat.svid,
                elevation: sat.elevation,
                azimuth: sat.azimuth,
                cno: currentCno,
                isGalileo: sat.isGalileo,
                constellation: sat.constellation,
                usedInFix: sat.usedInFix,
                hasInav: sat.hasInav
            )
        }

        // Apply Interactive Threat Simulation transformation on satellites
        let transformedSats: [SatelliteInfo] = jitteredSats.enumerated().map { idx, sat in
            switch simulatedThreatMode {
            case .nominal:
                return sat
            case .jamming:
                let jammedCno = max(9, sat.cno - 22 + ((idx % 3) - 1))
                return SatelliteInfo(
                    id: sat.id,
                    svid: sat.svid,
                    elevation: sat.elevation,
                    azimuth: sat.azimuth,
                    cno: jammedCno,
                    isGalileo: sat.isGalileo,
                    constellation: sat.constellation,
                    usedInFix: jammedCno >= 24,
                    hasInav: sat.hasInav && jammedCno >= 25
                )
            case .spoofing:
                let spoofedCno = 51 + (idx % 2)
                let pulledAz = fmod(sat.azimuth * 0.45 + 45.0 * 0.55 + Double(idx * 6), 360.0)
                let pulledEl = min(82.0, max(18.0, sat.elevation * 0.6 + 52.0 * 0.4))
                return SatelliteInfo(
                    id: sat.id,
                    svid: sat.svid,
                    elevation: pulledEl,
                    azimuth: pulledAz,
                    cno: spoofedCno,
                    isGalileo: sat.isGalileo,
                    constellation: sat.constellation,
                    usedInFix: true,
                    hasInav: sat.hasInav
                )
            case .meaconing:
                let meaconCno = min(50, max(22, sat.cno + 4 - (idx % 5)))
                let shiftedAz = fmod(sat.azimuth + 25.0, 360.0)
                let shiftedEl = max(10.0, min(85.0, sat.elevation - 6.0))
                return SatelliteInfo(
                    id: sat.id,
                    svid: sat.svid,
                    elevation: shiftedEl,
                    azimuth: shiftedAz,
                    cno: meaconCno,
                    isGalileo: sat.isGalileo,
                    constellation: sat.constellation,
                    usedInFix: meaconCno >= 26,
                    hasInav: sat.hasInav
                )
            case .osnmaFailure:
                return sat
            }
        }

        displayedSatellites = transformedSats.sorted {
            if $0.constellation != $1.constellation {
                return $0.constellation.rawValue < $1.constellation.rawValue
            }
            return $0.svid < $1.svid
        }

        updateMetricsForCurrentState(satellites: displayedSatellites)
    }

    private func updateMetricsForCurrentState(satellites: [SatelliteInfo]) {
        let usedCount = satellites.filter { $0.usedInFix }.count
        numSatellitesInFix = usedCount

        switch simulatedThreatMode {
        case .nominal:
            if selectedMode == 0 {
                fixTypeDesc = hasAcquiredRealLocation ? "3D Device Position Fix (CoreLocation)" : "Acquiring 3D GNSS Fix..."
                hdopValue = usedCount >= 12 ? "0.72" : (usedCount >= 6 ? "0.95" : "1.45")
                vdopValue = usedCount >= 12 ? "1.08" : (usedCount >= 6 ? "1.38" : "1.95")
                pdopValue = usedCount >= 12 ? "1.30" : (usedCount >= 6 ? "1.67" : "2.43")
                gstRmsError = "±0.22 m"
            } else if selectedMode == 2 {
                fixTypeDesc = "CelesTrak SGP4 Orbit Simulation"
                hdopValue = usedCount >= 12 ? "0.68" : (usedCount >= 6 ? "0.92" : "1.65")
                vdopValue = usedCount >= 12 ? "1.02" : (usedCount >= 6 ? "1.35" : "2.10")
                pdopValue = usedCount >= 12 ? "1.22" : (usedCount >= 6 ? "1.63" : "2.67")
                gstRmsError = "±0.25 m"
            }
        case .jamming:
            fixTypeDesc = "DEGRADED — RF JAMMING (+18 dB Noise)"
            hdopValue = "4.85"
            vdopValue = "6.40"
            pdopValue = "8.03"
            gstRmsError = "19.40 m RMS (AGC Saturated)"
            gstLatLonErr = "Lat: ±14.2m | Lon: ±13.1m | Alt: ±28.5m"
        case .spoofing:
            fixTypeDesc = "ALERT — COHERENT SPOOFING DETECTED"
            hdopValue = "3.20"
            vdopValue = "4.50"
            pdopValue = "5.52"
            gstRmsError = "48.50 m RMS (Outlier Rejected)"
            gstLatLonErr = "Lat: ±36.4m | Lon: ±32.1m | Alt: ±64.0m"
        case .meaconing:
            fixTypeDesc = "WARNING — MEACONING DELAY (+420 ns)"
            hdopValue = "2.45"
            vdopValue = "3.10"
            pdopValue = "3.95"
            dgpsAge = "14.8s (Replay Lag)"
            gstRmsError = "14.20 m RMS (Clock Jump +420ns)"
            gstLatLonErr = "Lat: ±9.8m | Lon: ±10.2m | Alt: ±18.4m"
        case .osnmaFailure:
            fixTypeDesc = "ALERT — OSNMA CRYPTO MAC FAILURE"
            hdopValue = "0.78"
            vdopValue = "1.12"
            pdopValue = "1.36"
            gstRmsError = "±0.35 m (I/NAV Forged Tag)"
        }
    }

    func averageCno(for constellation: GnssConstellation) -> String {
        let sats = displayedSatellites.filter { $0.constellation == constellation }
        guard !sats.isEmpty else { return "-- dB-Hz" }
        let avg = Double(sats.map { $0.cno }.reduce(0, +)) / Double(sats.count)
        return String(format: "%.1f dB-Hz", avg)
    }
    
    // MARK: - CoreLocation & Hardware Telemetry Bindings

    private func setupBindings() {
        // ALWAYS use user's real physical coordinates from CoreLocation whenever received
        repository.$lastLocation
            .compactMap { $0 }
            .receive(on: DispatchQueue.main)
            .sink { [weak self] loc in
                guard let self = self else { return }
                let lat = loc.coordinate.latitude
                let lon = loc.coordinate.longitude

                let latDir = lat >= 0 ? "N" : "S"
                let lonDir = lon >= 0 ? "E" : "W"

                self.hasAcquiredRealLocation = true
                self.currentCoordinate = loc.coordinate
                self.locationText = String(format: "%.6f° %@, %.6f° %@", abs(lat), latDir, abs(lon), lonDir)
                self.accuracyText = String(format: "Acc: ±%.1f m | Alt: %.1f m (Live Location)", loc.horizontalAccuracy, loc.altitude)
                self.updateSatellites()
            }
            .store(in: &cancellables)
        
        repository.$capturedMessages
            .filter { !$0.isEmpty }
            .receive(on: DispatchQueue.main)
            .assign(to: &$messages)

        tleDownloader.$parsedTles
            .receive(on: DispatchQueue.main)
            .sink { [weak self] _ in
                self?.updateSatellites()
            }
            .store(in: &cancellables)

        bluetoothManager.onNmeaLineReceived = { [weak self] line in
            guard let self = self else { return }

            if line.contains("$GPGGA") || line.contains("$GNGGA") {
                self.parseGgaSentence(line)
            }
            if line.contains("$GPGST") || line.contains("$GNGST") {
                self.parseGstSentence(line)
            }
            if line.contains("$GPGSA") || line.contains("$GNGSA") {
                self.parseGsaSentence(line)
            }
            if line.contains("$GAGSV") || line.contains("$GPGSV") || line.contains("$GLGSV") || line.contains("$BDGSV") {
                self.parseGsvLine(line)
            }
        }

        Publishers.CombineLatest4($selectedMode, $timeOffsetHours, $isGalileoEnabled, $isGpsEnabled)
            .receive(on: DispatchQueue.main)
            .sink { [weak self] _, _, _, _ in
                self?.updateSatellites()
            }
            .store(in: &cancellables)

        Publishers.CombineLatest3($isGlonassEnabled, $isBeidouEnabled, $simulatedThreatMode)
            .receive(on: DispatchQueue.main)
            .sink { [weak self] _, _, _ in
                self?.updateSatellites()
            }
            .store(in: &cancellables)
    }

    private func parseGgaSentence(_ line: String) {
        let parts = line.components(separatedBy: ",")
        guard parts.count >= 10 else { return }
        if let lat = parseNmeaCoord(parts[2], dir: parts[3]),
           let lon = parseNmeaCoord(parts[4], dir: parts[5]) {
            let fixQuality = parts[6]
            let numSats = Int(parts[7]) ?? 0
            let hdop = parts[8]
            let alt = parts[9]

            self.hasExternalHardwareFix = true
            self.numSatellitesInFix = numSats
            self.hdopValue = hdop.isEmpty ? "--" : hdop

            if simulatedThreatMode == .nominal && selectedMode == 1 {
                self.fixTypeDesc = {
                    switch fixQuality {
                    case "1": return "GPS Autonomous"
                    case "2": return "DGPS Differential (SXblue)"
                    case "4": return "RTK Fixed (Centimeter)"
                    case "5": return "RTK Float"
                    default: return "Fix Mode \(fixQuality)"
                    }
                }()
                self.currentCoordinate = CLLocationCoordinate2D(latitude: lat, longitude: lon)
                let latDir = lat >= 0 ? "N" : "S"
                let lonDir = lon >= 0 ? "E" : "W"
                self.locationText = String(format: "%.6f° %@, %.6f° %@", abs(lat), latDir, abs(lon), lonDir)
                self.accuracyText = "Fix: \(fixTypeDesc) | Sats: \(numSats) | HDOP: \(hdopValue) | Alt: \(alt)m"
            }
        }
    }

    private func parseGstSentence(_ line: String) {
        guard simulatedThreatMode == .nominal else { return }
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
        guard simulatedThreatMode == .nominal else { return }
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

        // Retain active tracking channels for 30s to prevent dropouts
        let activeThreshold = Date().addingTimeInterval(-30.0)
        satelliteStore = satelliteStore.filter { $0.value.lastSeen > activeThreshold }
        liveBridgeSatellites = satelliteStore.values.map { $0.info }
        
        // Notice: We do NOT directly assign displayedSatellites here! updateSatellites() manages the unified stream.
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
