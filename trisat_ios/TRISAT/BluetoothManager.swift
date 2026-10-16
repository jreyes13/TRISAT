import Foundation
import CoreBluetooth
import Combine

/// Data structure representing a discovered Bluetooth GNSS peripheral
struct DiscoveredPeripheral: Identifiable, Hashable {
    let id: UUID
    let name: String
    let rssi: Int
    let peripheral: CBPeripheral
}

class BluetoothManager: NSObject, ObservableObject, CBCentralManagerDelegate, CBPeripheralDelegate {
    private var centralManager: CBCentralManager!
    private var connectedPeripheral: CBPeripheral?
    
    // Published states for SwiftUI reactive bindings
    @Published var isBluetoothOn: Bool = false
    @Published var isScanning: Bool = false
    @Published var isConnected: Bool = false
    @Published var discoveredPeripherals: [DiscoveredPeripheral] = []
    @Published var connectedPeripheralName: String = "None"
    
    // Callback stream for raw byte packets (UBX, SBF, or NMEA raw subframes)
    var onRawBytesReceived: ((Data) -> Void)?
    
    override init() {
        super.init()
        centralManager = CBCentralManager(delegate: self, queue: nil)
    }
    
    func startScanning() {
        guard centralManager.state == .poweredOn else { return }
        discoveredPeripherals.removeAll()
        isScanning = true
        centralManager.scanForPeripherals(withServices: nil, options: [CBCentralManagerScanOptionAllowDuplicatesKey: false])
    }
    
    func stopScanning() {
        centralManager.stopScan()
        isScanning = false
    }
    
    func connect(to peripheralItem: DiscoveredPeripheral) {
        stopScanning()
        connectedPeripheral = peripheralItem.peripheral
        connectedPeripheral?.delegate = self
        centralManager.connect(peripheralItem.peripheral, options: nil)
    }
    
    func disconnect() {
        if let peripheral = connectedPeripheral {
            centralManager.cancelPeripheralConnection(peripheral)
        }
    }
    
    // MARK: - CBCentralManagerDelegate
    
    func centralManagerDidUpdateState(_ central: CBCentralManager) {
        DispatchQueue.main.async {
            self.isBluetoothOn = (central.state == .poweredOn)
            if central.state == .poweredOn {
                self.startScanning()
            }
        }
    }
    
    func centralManager(_ central: CBCentralManager, didDiscover peripheral: CBPeripheral, advertisementData: [String : Any], rssi RSSI: NSNumber) {
        let name = peripheral.name ?? advertisementData[CBAdvertisementDataLocalNameKey] as? String ?? "Unknown GNSS Device"
        let item = DiscoveredPeripheral(id: peripheral.identifier, name: name, rssi: RSSI.intValue, peripheral: peripheral)
        
        DispatchQueue.main.async {
            if !self.discoveredPeripherals.contains(where: { $0.id == item.id }) {
                self.discoveredPeripherals.append(item)
            }
        }
    }
    
    func centralManager(_ central: CBCentralManager, didConnect peripheral: CBPeripheral) {
        DispatchQueue.main.async {
            self.isConnected = true
            self.connectedPeripheralName = peripheral.name ?? "External GNSS Receiver"
        }
        peripheral.discoverServices(nil)
    }
    
    func centralManager(_ central: CBCentralManager, didDisconnectPeripheral peripheral: CBPeripheral, error: Error?) {
        DispatchQueue.main.async {
            self.isConnected = false
            self.connectedPeripheralName = "None"
            self.connectedPeripheral = nil
        }
    }
    
    // MARK: - CBPeripheralDelegate
    
    func peripheral(_ peripheral: CBPeripheral, didDiscoverServices error: Error?) {
        guard let services = peripheral.services else { return }
        for service in services {
            peripheral.discoverCharacteristics(nil, for: service)
        }
    }
    
    func peripheral(_ peripheral: CBPeripheral, didDiscoverCharacteristicsFor service: CBService, error: Error?) {
        guard let characteristics = service.characteristics else { return }
        for characteristic in characteristics {
            if characteristic.properties.contains(.notify) || characteristic.properties.contains(.read) {
                peripheral.setNotifyValue(true, for: characteristic)
            }
        }
    }
    
    func peripheral(_ peripheral: CBPeripheral, didUpdateValueFor characteristic: CBCharacteristic, error: Error?) {
        guard let data = characteristic.value, !data.isEmpty else { return }
        DispatchQueue.main.async {
            self.onRawBytesReceived?(data)
        }
    }
}