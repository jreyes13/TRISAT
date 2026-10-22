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
        #if os(macOS)
        locationManager.startUpdatingLocation()
        #endif
    }
    
    func requestPermissions() {
        #if os(iOS)
        locationManager.requestWhenInUseAuthorization()
        #else
        locationManager.startUpdatingLocation()
        #endif
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
            if isAuthorized || manager.authorizationStatus == .notDetermined {
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

    func locationManager(_ manager: CLLocationManager, didFailWithError error: Error) {
        #if DEBUG
        print("Location manager error: \(error.localizedDescription)")
        #endif
    }
}
