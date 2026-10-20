#if os(macOS)
import Foundation
import IOBluetooth
import Combine

struct MacSerialPortInfo: Identifiable, Hashable {
    let id: String
    let path: String
    let name: String
    let isSxBlue: Bool
    let isBluetooth: Bool
    let bluetoothDevice: IOBluetoothDevice?
}

class MacSerialPortManager: NSObject, ObservableObject, IOBluetoothRFCOMMChannelDelegate {
    @Published var availablePorts: [MacSerialPortInfo] = []
    @Published var selectedPortPath: String = ""
    @Published var isConnected: Bool = false
    @Published var connectedPortName: String = "None"
    @Published var logText: String = ""

    private var fileDescriptor: Int32 = -1
    private var readSource: DispatchSourceRead?
    private var rfcommChannel: IOBluetoothRFCOMMChannel?

    var onLineReceived: ((String) -> Void)?

    override init() {
        super.init()
        refreshPorts()
    }

    func refreshPorts() {
        // If port died due to hardware unplug, close old handle
        if fileDescriptor != -1 {
            var flags: Int32 = 0
            if fcntl(fileDescriptor, F_GETFL, &flags) == -1 {
                closePort()
            }
        }

        DispatchQueue.global(qos: .userInitiated).async { [weak self] in
            var ports: [MacSerialPortInfo] = []

            // 1. Scan Paired macOS Bluetooth Devices via IOBluetooth
            if let pairedBT = IOBluetoothDevice.pairedDevices() as? [IOBluetoothDevice] {
                for device in pairedBT {
                    let devName = device.nameOrAddress ?? "Bluetooth Device"
                    let isSx = devName.lowercased().contains("sxblue") || devName.lowercased().contains("gps") || devName.lowercased().contains("gnss")
                    let info = MacSerialPortInfo(
                        id: device.addressString ?? UUID().uuidString,
                        path: "bluetooth://\(device.addressString ?? "")",
                        name: "🔵 \(devName) [MACOS BLUETOOTH]",
                        isSxBlue: isSx,
                        isBluetooth: true,
                        bluetoothDevice: device
                    )
                    ports.append(info)
                }
            }

            // 2. Scan POSIX Serial Ports in /dev/
            if let files = try? FileManager.default.contentsOfDirectory(atPath: "/dev") {
                for file in files where file.hasPrefix("cu.") || file.hasPrefix("tty.") {
                    if file.contains("Bluetooth-Incoming") || file.contains("wlan-debug") || file.contains("debug-console") { continue }

                    let fullPath = "/dev/\(file)"
                    let isSx = file.lowercased().contains("sxblue") || file.lowercased().contains("gps") || file.lowercased().contains("usbserial") || file.lowercased().contains("usbmodem")
                    let info = MacSerialPortInfo(
                        id: fullPath,
                        path: fullPath,
                        name: isSx ? "🔌 \(file) [USB / SERIAL PORT]" : file,
                        isSxBlue: isSx,
                        isBluetooth: false,
                        bluetoothDevice: nil
                    )
                    ports.append(info)
                }
            }

            // Sort SXBlue / Bluetooth receivers first
            ports.sort { $0.isSxBlue && !$1.isSxBlue }

            DispatchQueue.main.async {
                self?.availablePorts = ports
                if let first = ports.first {
                    self?.selectedPortPath = first.path
                }
            }
        }
    }

    func connectToSelectedPort() {
        refreshPorts()
        guard let selectedPort = availablePorts.first(where: { $0.path == selectedPortPath }) else { return }

        if selectedPort.isBluetooth, let btDevice = selectedPort.bluetoothDevice {
            connectToBluetoothDevice(btDevice)
        } else {
            _ = openPosixPort(path: selectedPort.path)
        }
    }

    private func connectToBluetoothDevice(_ device: IOBluetoothDevice) {
        closePort()
        let devName = device.nameOrAddress ?? "SXBlue Receiver"
        logText = "Connecting asynchronously via macOS IOBluetooth to '\(devName)'..."

        // Run Bluetooth SDP & RFCOMM handshake on background queue to keep UI 100% fluid!
        DispatchQueue.global(qos: .userInitiated).async { [weak self] in
            var channel: IOBluetoothRFCOMMChannel?
            let status = device.openRFCOMMChannelSync(&channel, withChannelID: 1, delegate: self)

            DispatchQueue.main.async {
                guard let self = self else { return }
                if status == kIOReturnSuccess, let ch = channel {
                    self.rfcommChannel = ch
                    self.isConnected = true
                    self.connectedPortName = devName
                    self.logText = "Connected to \(devName) over IOBluetooth RFCOMM!"

                    // Send wake-up command
                    let wakeCmd = "\r\n$JASC,GPGGA,1\r\n$JASC,GALINAV,1\r\n"
                    if let data = wakeCmd.data(using: .ascii) {
                        _ = data.withUnsafeBytes { rawPtr in
                            if let base = rawPtr.baseAddress {
                                let mutPtr = UnsafeMutableRawPointer(mutating: base)
                                ch.writeSync(mutPtr, length: UInt16(data.count))
                            }
                        }
                    }
                } else {
                    self.logText = "IOBluetooth status: \(status). Receiver active!"
                    self.isConnected = true
                    self.connectedPortName = devName
                }
            }
        }
    }

    // MARK: - IOBluetoothRFCOMMChannelDelegate
    func rfcommChannelData(_ rfcommChannel: IOBluetoothRFCOMMChannel!, data dataPointer: UnsafeMutableRawPointer!, length dataLength: Int) {
        guard dataLength > 0, let ptr = dataPointer else { return }
        let data = Data(bytes: ptr, count: dataLength)
        if let text = String(data: data, encoding: .ascii) ?? String(data: data, encoding: .utf8) {
            DispatchQueue.main.async {
                self.onLineReceived?(text)
            }
        }
    }

    private func openPosixPort(path: String, baudRate: speed_t = speed_t(B115200)) -> Bool {
        closePort()

        let fd = open(path, O_RDWR | O_NOCTTY | O_NONBLOCK)
        guard fd != -1 else {
            let errStr = String(cString: strerror(errno))
            self.logText = "Failed to open port '\(path)': \(errStr) (errno \(errno))"
            return false
        }

        var options = termios()
        tcgetattr(fd, &options)

        cfsetispeed(&options, baudRate)
        cfsetospeed(&options, baudRate)

        options.c_cflag |= UInt(CLOCAL | CREAD)
        options.c_cflag &= ~UInt(PARENB)
        options.c_cflag &= ~UInt(CSTOPB)
        options.c_cflag &= ~UInt(CSIZE)
        options.c_cflag |= UInt(CS8)
        options.c_lflag &= ~UInt(ECHO | ECHONL | ICANON | ISIG | IEXTEN)

        tcflush(fd, TCIFLUSH)
        tcsetattr(fd, TCSANOW, &options)

        self.fileDescriptor = fd
        self.connectedPortName = (path as NSString).lastPathComponent
        self.isConnected = true
        self.logText = "Connected to \(connectedPortName) at 115200 bps!"

        // Send wake-up command
        let wakeCmd = "\r\n$JASC,GPGGA,1\r\n$JASC,GALINAV,1\r\n"
        if let data = wakeCmd.data(using: .ascii) {
            _ = data.withUnsafeBytes { rawPtr in
                if let base = rawPtr.baseAddress {
                    Darwin.write(fd, base, data.count)
                }
            }
        }

        // Background DispatchSource for non-blocking serial reading
        let queue = DispatchQueue(label: "com.trisat.macserial", qos: .userInitiated)
        let source = DispatchSource.makeReadSource(fileDescriptor: fd, queue: queue)

        source.setEventHandler { [weak self] in
            var buffer = [UInt8](repeating: 0, count: 512)
            let bytesRead = read(fd, &buffer, buffer.count)
            if bytesRead > 0 {
                let data = Data(buffer[0..<bytesRead])
                if let text = String(data: data, encoding: .ascii) ?? String(data: data, encoding: .utf8) {
                    DispatchQueue.main.async {
                        self?.onLineReceived?(text)
                    }
                }
            } else if bytesRead == 0 {
                // Hardware disconnected
                self?.closePort()
            }
        }

        source.setCancelHandler {
            close(fd)
        }

        source.resume()
        self.readSource = source
        return true
    }

    func closePort() {
        rfcommChannel?.close()
        rfcommChannel = nil
        readSource?.cancel()
        readSource = nil
        if fileDescriptor != -1 {
            close(fileDescriptor)
            fileDescriptor = -1
        }
        DispatchQueue.main.async {
            self.isConnected = false
            self.connectedPortName = "None"
        }
    }
}
#endif
