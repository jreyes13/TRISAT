import Foundation
import CoreLocation
import Combine

@MainActor
class GnssViewModel: ObservableObject {
    private let repository: GnssRepository
    private var cancellables = Set<AnyCancellable>()
    
    @Published var locationText: String = "Searching coordinates..."
    @Published var accuracyText: String = "Acc: ±-- m"
    @Published var messages: [GalileoINavMessage] = []
    
    init(repository: GnssRepository = GnssRepository()) {
        self.repository = repository
        setupBindings()
    }
    
    func start() {
        repository.requestPermissions()
        repository.startINavFeed()
    }
    
    func stop() {
        repository.stopINavFeed()
        repository.stopLocationUpdates()
    }
    
    private func setupBindings() {
        // Internal GNSS chip location binding
        repository.$lastLocation
            .compactMap { $0 }
            .receive(on: DispatchQueue.main)
            .sink { [weak self] loc in
                self?.locationText = String(format: "%.6f° N, %.6f° W", loc.coordinate.latitude, loc.coordinate.longitude)
                self?.accuracyText = String(format: "Acc: ±%.1f m | Alt: %.1f m", loc.horizontalAccuracy, loc.altitude)
            }
            .store(in: &cancellables)
        
        // I/NAV message stream binding
        repository.$capturedMessages
            .receive(on: DispatchQueue.main)
            .assign(to: &$messages)
    }
}