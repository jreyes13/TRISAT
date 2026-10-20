import Foundation
import CoreLocation
import Combine

class GnssRepository: NSObject, ObservableObject, CLLocationManagerDelegate {
    private let locationManager = CLLocationManager()
    
    // Observable properties
    @Published var lastLocation: CLLocation?
    @Published var authorizationStatus: CLAuthorizationStatus?
    @Published var capturedMessages: [GalileoINavMessage] = []

    override init() {
        super.init()
        locationManager.delegate = self
        locationManager.desiredAccuracy = kCLLocationAccuracyBest
        locationManager.distanceFilter = kCLDistanceFilterNone
    }
    
    func requestPermissions() {
        locationManager.requestWhenInUseAuthorization()
    }
    
    func startLocationUpdates() {
        locationManager.startUpdatingLocation()
    }
    
    func stopLocationUpdates() {
        locationManager.stopUpdatingLocation()
    }
    
    func appendRealInavMessage(_ message: GalileoINavMessage) {
        DispatchQueue.main.async {
            if self.capturedMessages.count >= 100 {
                self.capturedMessages.removeFirst()
            }
            self.capturedMessages.append(message)
        }
    }
    
    func clearMessages() {
        DispatchQueue.main.async {
            self.capturedMessages.removeAll()
        }
    }
    
    // MARK: - CLLocationManagerDelegate
    func locationManagerDidChangeAuthorization(_ manager: CLLocationManager) {
        DispatchQueue.main.async {
            self.authorizationStatus = manager.authorizationStatus
            #if os(iOS)
            let isAuthorized = manager.authorizationStatus == .authorizedWhenInUse || manager.authorizationStatus == .authorizedAlways
            #else
            let isAuthorized = manager.authorizationStatus == .authorizedAlways || manager.authorizationStatus == .authorized
            #endif
            if isAuthorized {
                self.startLocationUpdates()
            }
        }
    }
    
    func locationManager(_ manager: CLLocationManager, didUpdateLocations locations: [CLLocation]) {
        guard let location = locations.last else { return }
        DispatchQueue.main.async {
            self.lastLocation = location
        }
    }
}
