import Foundation
import CoreLocation
import Combine

@MainActor
class GnssViewModel: ObservableObject {
    private let repository: GnssRepository
    let tleDownloader = TleDownloader()
    private var cancellables = Set<AnyCancellable>()
    
    @Published var locationText: String = "Searching coordinates..."
    @Published var accuracyText: String = "Acc: ±-- m"
    @Published var currentCoordinate: CLLocationCoordinate2D = CLLocationCoordinate2D(latitude: 37.7749, longitude: -122.4194)
    @Published var messages: [GalileoINavMessage] = []

    // Mode: 0 = Internal + TLE, 1 = External BLE, 2 = TLE Simulation
    @Published var selectedMode: Int = 0
    @Published var timeOffsetHours: Double = 0.0

    // Constellation Filters
    @Published var isGalileoEnabled: Bool = true
    @Published var isGpsEnabled: Bool = true
    @Published var isGlonassEnabled: Bool = true
    @Published var isBeidouEnabled: Bool = true

    // Computed Satellites for Skyplot
    @Published var displayedSatellites: [SatelliteInfo] = []

    init(repository: GnssRepository = GnssRepository()) {
        self.repository = repository
        setupBindings()
    }
    
    func start() {
        repository.requestPermissions()
        repository.startINavFeed()
        updateSatellites()
    }
    
    func stop() {
        repository.stopINavFeed()
        repository.stopLocationUpdates()
    }

    func updateSatellites() {
        let coord = currentCoordinate

        let allSats: [SatelliteInfo]
        if selectedMode == 1 {
            allSats = Sgp4OrbitPropagator.propagateToSkyplot(tles: tleDownloader.parsedTles, timeOffsetHours: 0.0, observerLocation: coord)
        } else if selectedMode == 2 {
            allSats = Sgp4OrbitPropagator.propagateToSkyplot(tles: tleDownloader.parsedTles, timeOffsetHours: timeOffsetHours, observerLocation: coord)
        } else {
            allSats = Sgp4OrbitPropagator.propagateToSkyplot(tles: tleDownloader.parsedTles, timeOffsetHours: 0.0, observerLocation: coord)
        }

        displayedSatellites = allSats.filter { sat in
            switch sat.constellation {
            case .galileo: return isGalileoEnabled
            case .gps: return isGpsEnabled
            case .glonass: return isGlonassEnabled
            case .beidou: return isBeidouEnabled
            }
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

                self?.currentCoordinate = loc.coordinate
                self?.locationText = String(format: "%.6f° %@, %.6f° %@", abs(lat), latDir, abs(lon), lonDir)
                self?.accuracyText = String(format: "Acc: ±%.1f m | Alt: %.1f m", loc.horizontalAccuracy, loc.altitude)
                self?.updateSatellites()
            }
            .store(in: &cancellables)
        
        repository.$capturedMessages
            .receive(on: DispatchQueue.main)
            .assign(to: &$messages)

        Publishers.CombineLatest4($selectedMode, $timeOffsetHours, $isGalileoEnabled, $isGpsEnabled)
            .receive(on: DispatchQueue.main)
            .sink { [weak self] _ in self?.updateSatellites() }
            .store(in: &cancellables)

        Publishers.CombineLatest($isGlonassEnabled, $isBeidouEnabled)
            .receive(on: DispatchQueue.main)
            .sink { [weak self] _ in self?.updateSatellites() }
            .store(in: &cancellables)
    }
}
