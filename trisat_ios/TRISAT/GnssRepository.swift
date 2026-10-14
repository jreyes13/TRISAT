import Foundation
import CoreLocation
import Combine

class GnssRepository: NSObject, ObservableObject, CLLocationManagerDelegate {
    private let locationManager = CLLocationManager()
    
    // Observable properties (equivalent to StateFlow in Kotlin)
    @Published var lastLocation: CLLocation?
    @Published var authorizationStatus: CLAuthorizationStatus?
    @Published var capturedMessages: [GalileoINavMessage] = []
    
    private var mockTimer: Timer?
    
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
    
    // MARK: - I/NAV Feed Mocking (Phase 1: Prior to Bluetooth Integration)
    func startINavFeed() {
        mockTimer?.invalidate()
        mockTimer = Timer.scheduledTimer(withTimeInterval: 2.0, repeats: true) { [weak self] _ in
            guard let self = self else { return }
            let randomSvid = Int.random(in: 1...36)
            let dummyData = Data((0..<30).map { _ in UInt8.random(in: 0...255) })
            
            let message = GalileoINavMessage(
                svid: randomSvid,
                submessageId: 1,
                data: dummyData,
                status: 0,
                timestamp: Date()
            )
            
            DispatchQueue.main.async {
                if self.capturedMessages.count >= 50 {
                    self.capturedMessages.removeFirst()
                }
                self.capturedMessages.append(message)
            }
        }
    }
    
    func stopINavFeed() {
        mockTimer?.invalidate()
        mockTimer = nil
    }
    
    // MARK: - CLLocationManagerDelegate
    func locationManagerDidChangeAuthorization(_ manager: CLLocationManager) {
        DispatchQueue.main.async {
            self.authorizationStatus = manager.authorizationStatus
            if manager.authorizationStatus == .authorizedWhenInUse || manager.authorizationStatus == .authorizedAlways {
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