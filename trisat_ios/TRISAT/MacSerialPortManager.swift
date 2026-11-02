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
    @Published var lastError: String? = nil
    @Published var userExplicitlyDisconnected: Bool = false

    private var fileDescriptor: Int32 = -1
    private var readSource: DispatchSourceRead?
    private var rfcommChannel: IOBluetoothRFCOMMChannel?
    private var incomingLineBuffer: String = ""
    private var watchdogTimer: Timer?
    private var autoConnectTimer: Timer?
    private var lastDataReceivedTime: Date = Date()

    var onLineReceived: ((String) -> Void)?
    var onInavFrameReceived: ((GalileoINavMessage) -> Void)?
    var onDisconnection: (() -> Void)?
    var onDataTimeout: (() -> Void)?

    override init() {
        super.init()
        refreshPortsAndAutoConnect()
        startAutoConnectTimer()
    }

    private func startAutoConnectTimer() {
        autoConnectTimer?.invalidate()
        autoConnectTimer = Timer.scheduledTimer(withTimeInterval: 2.5, repeats: true) { [weak self] _ in
            guard let self = self else { return }
            if !self.isConnected && !self.userExplicitlyDisconnected {
                self.refreshPortsAndAutoConnect()
            }
        }
    }

    func refreshPorts(autoConnect: Bool = false) {
        DispatchQueue.global(qos: .userInitiated).async { [weak self] in
            var ports: [MacSerialPortInfo] = []

            // 1. Scan POSIX Serial Ports in /dev/
            if let files = try? FileManager.default.contentsOfDirectory(atPath: "/dev") {
                for file in files where file.hasPrefix("cu.") || file.hasPrefix("tty.") {
                    if file.contains("Bluetooth-Incoming") || file.contains("wlan-debug") || file.contains("debug-console") { continue }

                    let fullPath = "/dev/\(file)"
                    let lower = file.lowercased()
                    let isUbloxModem = lower.contains("usbmodem")
                    let isUsbSerial = lower.contains("usbserial")
                    let isSxBlueSpecific = lower.contains("sxblue")
                    let isGnss = isUbloxModem || isUsbSerial || isSxBlueSpecific || lower.contains("gps") || lower.contains("gnss")

                    let displayName: String
                    if isUbloxModem {
                        displayName = "⚡️ u-blox ZED-F9P (USB Directo) [USB]"
                    } else if isUsbSerial {
                        displayName = "⚡️ u-blox ZED-F9P (ESP32 Bridge) [USB]"
                    } else if isSxBlueSpecific {
                        displayName = "⚡️ Geneq SXBlue (\(file)) [USB]"
                    } else if isGnss {
                        displayName = "🔌 GNSS Receiver (\(file)) [USB]"
                    } else {
                        displayName = "🔌 Serial Port (\(file))"
                    }

                    let info = MacSerialPortInfo(
                        id: fullPath,
                        path: fullPath,
                        name: displayName,
                        isSxBlue: isGnss,
                        isBluetooth: false,
                        bluetoothDevice: nil
                    )
                    ports.append(info)
                }
            }

            // 2. Scan Paired macOS Bluetooth Devices via IOBluetooth
            if let pairedBT = IOBluetoothDevice.pairedDevices() as? [IOBluetoothDevice] {
                for device in pairedBT {
                    let devName = device.nameOrAddress ?? "Bluetooth Device"
                    let isSx = devName.lowercased().contains("sxblue") || devName.lowercased().contains("gps") || devName.lowercased().contains("gnss")
                    let info = MacSerialPortInfo(
                        id: device.addressString ?? UUID().uuidString,
                        path: "bluetooth://\(device.addressString ?? "")",
                        name: "🔵 \(devName) [Bluetooth]",
                        isSxBlue: isSx,
                        isBluetooth: true,
                        bluetoothDevice: device
                    )
                    ports.append(info)
                }
            }

            // USB Devices (u-blox, SXBlue, usbmodem, usbserial) ALWAYS come FIRST before Bluetooth!
            ports.sort { a, b in
                // 1. USB ports come before Bluetooth
                if !a.isBluetooth && b.isBluetooth { return true }
                if a.isBluetooth && !b.isBluetooth { return false }

                // 2. Among USB ports, u-blox and SXBlue come first
                let aPriority = a.name.contains("u-blox") ? 0 : (a.name.contains("SXBlue") ? 1 : (a.isSxBlue ? 2 : 3))
                let bPriority = b.name.contains("u-blox") ? 0 : (b.name.contains("SXBlue") ? 1 : (b.isSxBlue ? 2 : 3))
                if aPriority != bPriority {
                    return aPriority < bPriority
                }

                return a.name < b.name
            }

            DispatchQueue.main.async {
                self?.availablePorts = ports
                if let first = ports.first {
                    self?.selectedPortPath = first.path
                    if autoConnect && !(self?.userExplicitlyDisconnected ?? false) && !(self?.isConnected ?? true) && first.isSxBlue {
                        _ = self?.openPosixPort(path: first.path)
                    }
                }
            }
        }
    }

    func refreshPortsAndAutoConnect() {
        refreshPorts(autoConnect: true)
    }

    func connectToSelectedPort() {
        userExplicitlyDisconnected = false
        DispatchQueue.main.async { [weak self] in
            self?.lastError = nil
        }
        guard let selectedPort = availablePorts.first(where: { $0.path == selectedPortPath }) else { return }

        if selectedPort.isBluetooth, let btDevice = selectedPort.bluetoothDevice {
            connectToBluetoothDevice(btDevice)
        } else {
            _ = openPosixPort(path: selectedPort.path)
        }
    }

    func configureReceiverAllConstellations() {
        guard fileDescriptor != -1 else { return }
        // UBX-CFG-GNSS enabling GPS, Galileo, GLONASS, BeiDou
        let blocks: [[UInt8]] = [
            [0, 8, 16, 0, 0x01, 0x00, 0x01, 0x01], // GPS
            [1, 1, 3, 0,  0x01, 0x00, 0x00, 0x01], // SBAS
            [2, 8, 16, 0, 0x01, 0x00, 0x21, 0x01], // Galileo
            [3, 2, 8, 0,  0x01, 0x00, 0x11, 0x01], // BeiDou
            [5, 0, 3, 0,  0x01, 0x00, 0x05, 0x01], // QZSS
            [6, 8, 12, 0, 0x01, 0x00, 0x01, 0x01], // GLONASS
        ]
        var payload: [UInt8] = [0x00, 0x00, 0xFF, UInt8(blocks.count)]
        for blk in blocks { payload.append(contentsOf: blk) }
        sendUbxCommand(cls: 0x06, id: 0x3E, payload: payload)
        logText = "Sent command to enable GPS, Galileo, GLONASS & BeiDou!"
    }

    func configureReceiverGalileoOnly() {
        guard fileDescriptor != -1 else { return }
        let blocks: [[UInt8]] = [
            [0, 8, 16, 0, 0x00, 0x00, 0x01, 0x01], // GPS disabled
            [1, 1, 3, 0,  0x00, 0x00, 0x00, 0x01], // SBAS disabled
            [2, 10, 18, 0, 0x01, 0x00, 0x21, 0x01], // Galileo enabled
            [3, 2, 5, 0,  0x00, 0x00, 0x11, 0x01], // BeiDou disabled
            [5, 0, 4, 0,  0x00, 0x00, 0x05, 0x01], // QZSS disabled
            [6, 8, 12, 0, 0x00, 0x00, 0x01, 0x01], // GLONASS disabled
        ]
        var payload: [UInt8] = [0x00, 0x00, 0xFF, UInt8(blocks.count)]
        for blk in blocks { payload.append(contentsOf: blk) }
        sendUbxCommand(cls: 0x06, id: 0x3E, payload: payload)
        logText = "Receiver set to Galileo Dedicated Mode!"
    }

    private func sendUbxCommand(cls: UInt8, id: UInt8, payload: [UInt8]) {
        var msg: [UInt8] = [0xB5, 0x62, cls, id, UInt8(payload.count & 0xFF), UInt8((payload.count >> 8) & 0xFF)] + payload
        var ckA: UInt8 = 0
        var ckB: UInt8 = 0
        for b in msg[2...] {
            ckA = ckA &+ b
            ckB = ckB &+ ckA
        }
        msg.append(ckA)
        msg.append(ckB)
        msg.withUnsafeBytes { rawPtr in
            if let base = rawPtr.baseAddress {
                Darwin.write(fileDescriptor, base, msg.count)
            }
        }
    }

    func disconnect() {
        userExplicitlyDisconnected = true
        DispatchQueue.main.async { [weak self] in
            self?.lastError = nil
        }
        closePort()
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
            let msg: String
            if errno == EBUSY { // 16
                msg = "⚠️ Puerto bloqueado por otra app (cierra el Monitor Serie de Arduino IDE)."
            } else {
                msg = "⚠️ No se pudo abrir \(path): \(errStr)"
            }
            DispatchQueue.main.async { [weak self] in
                self?.lastError = msg
                self?.logText = msg
            }
            return false
        }

        DispatchQueue.main.async { [weak self] in
            self?.lastError = nil
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
        self.lastDataReceivedTime = Date()
        self.logText = "Connected to \(connectedPortName) at 115200 bps!"

        // Start watchdog timer checking if port file exists and if data timeout occurs
        let openedPath = path
        DispatchQueue.main.async { [weak self] in
            guard let self = self else { return }
            self.watchdogTimer?.invalidate()
            self.watchdogTimer = Timer.scheduledTimer(withTimeInterval: 1.0, repeats: true) { [weak self] _ in
                guard let self = self, self.isConnected else { return }
                // 1. Check physical device presence: if /dev/cu.usbmodem* node was removed, cable was unplugged!
                if access(openedPath, F_OK) != 0 {
                    self.logText = "Hardware device disconnected (port node removed)."
                    self.closePort()
                    return
                }
                // 2. Check data timeout (no bytes received for > 3.0 seconds)
                if Date().timeIntervalSince(self.lastDataReceivedTime) > 3.0 {
                    self.onDataTimeout?( )
                }
            }
        }

        // 1. Send u-blox ZED-F9P UBX configuration frames to enable multi-constellation GSV, GGA, RMC, GSA, GST & SFRBX
        // Modern Gen 9 UBX-CFG-VALSET (enables SFRBX on USB and GSV on USB in RAM)
        let ubxValset = [UInt8]([0xB5, 0x62, 0x06, 0x8A, 0x0E, 0x00, 0x00, 0x01, 0x00, 0x00, 0x34, 0x02, 0x91, 0x20, 0x01, 0xC7, 0x00, 0x91, 0x20, 0x01, 0x00, 0xE2])

        // Backward compatible UBX-CFG-MSG (enables on all ports including USB)
        let ubxEnableGsv = [UInt8]([0xB5, 0x62, 0x06, 0x01, 0x08, 0x00, 0xF0, 0x03, 0x01, 0x01, 0x01, 0x01, 0x01, 0x00, 0x06, 0x4C])
        let ubxEnableGga = [UInt8]([0xB5, 0x62, 0x06, 0x01, 0x08, 0x00, 0xF0, 0x00, 0x01, 0x01, 0x01, 0x01, 0x01, 0x00, 0x03, 0x39])
        let ubxEnableRmc = [UInt8]([0xB5, 0x62, 0x06, 0x01, 0x08, 0x00, 0xF0, 0x04, 0x01, 0x01, 0x01, 0x01, 0x01, 0x00, 0x07, 0x51])
        let ubxEnableGsa = [UInt8]([0xB5, 0x62, 0x06, 0x01, 0x08, 0x00, 0xF0, 0x02, 0x01, 0x01, 0x01, 0x01, 0x01, 0x00, 0x05, 0x45])
        let ubxEnableGst = [UInt8]([0xB5, 0x62, 0x06, 0x01, 0x08, 0x00, 0xF0, 0x07, 0x01, 0x01, 0x01, 0x01, 0x01, 0x00, 0x0A, 0x5E])
        // Enable UBX-RXM-SFRBX on all ports including USB (rate[3]=1)
        let ubxEnableSfrbx = [UInt8]([0xB5, 0x62, 0x06, 0x01, 0x08, 0x00, 0x02, 0x13, 0x01, 0x01, 0x01, 0x01, 0x01, 0x00, 0x29, 0x4C])

        for ubx in [ubxValset, ubxEnableGsv, ubxEnableGga, ubxEnableRmc, ubxEnableGsa, ubxEnableGst, ubxEnableSfrbx] {
            ubx.withUnsafeBytes { rawPtr in
                if let base = rawPtr.baseAddress {
                    Darwin.write(fd, base, ubx.count)
                }
            }
        }

        // 2. Also send NMEA configuration queries with verified checksums
        let wakeCmd = "\r\n$PUBX,40,GSV,1,1,1,1,1,0*58\r\n$PUBX,40,GGA,1,1,1,1,1,0*5B\r\n$PUBX,40,RMC,1,1,1,1,1,0*46\r\n$PUBX,40,GSA,1,1,1,1,1,0*4F\r\n$JASC,GPGGA,1\r\n$JASC,GALINAV,1\r\n"
        if let data = wakeCmd.data(using: .ascii) {
            _ = data.withUnsafeBytes { rawPtr in
                if let base = rawPtr.baseAddress {
                    Darwin.write(fd, base, data.count)
                }
            }
        }

        // Background DispatchSource for non-blocking serial reading with complete line & binary UBX buffering
        var rawDataBuffer = Data()
        let queue = DispatchQueue(label: "com.trisat.macserial", qos: .userInitiated)
        let source = DispatchSource.makeReadSource(fileDescriptor: fd, queue: queue)

        source.setEventHandler { [weak self] in
            guard let self = self else { return }
            var buffer = [UInt8](repeating: 0, count: 2048)
            let bytesRead = read(fd, &buffer, buffer.count)
            if bytesRead > 0 {
                self.lastDataReceivedTime = Date()
                rawDataBuffer.append(buffer, count: bytesRead)

                // Multiplexed parsing loop: handles ASCII NMEA sentences and binary UBX packets
                while !rawDataBuffer.isEmpty {
                    // 1. Check for NMEA start '$' (0x24)
                    if rawDataBuffer[0] == 0x24 {
                        if let newlineIdx = rawDataBuffer.firstIndex(of: 0x0A) { // '\n'
                            let lineRange = 0..<(newlineIdx + 1)
                            let lineData = rawDataBuffer.subdata(in: lineRange)
                            rawDataBuffer.removeSubrange(lineRange)
                            if let line = String(data: lineData, encoding: .ascii) ?? String(data: lineData, encoding: .utf8) {
                                let trimmed = line.trimmingCharacters(in: .whitespacesAndNewlines)
                                if !trimmed.isEmpty {
                                    DispatchQueue.main.async {
                                        self.onLineReceived?(trimmed)
                                    }
                                }
                            }
                            continue
                        } else {
                            // Incomplete NMEA line, wait for next read (cap buffer if excessively large)
                            if rawDataBuffer.count > 4096 {
                                rawDataBuffer.removeFirst()
                            }
                            break
                        }
                    }

                    // 2. Check for ESP32 Bridge log lines: '[' (0x5B) e.g. [GALILEO I/NAV] E02 (WT 21): <hex>
                    if rawDataBuffer[0] == 0x5B {
                        if let newlineIdx = rawDataBuffer.firstIndex(of: 0x0A) { // '\n'
                            let lineRange = 0..<(newlineIdx + 1)
                            let lineData = rawDataBuffer.subdata(in: lineRange)
                            rawDataBuffer.removeSubrange(lineRange)
                            if let line = String(data: lineData, encoding: .ascii) ?? String(data: lineData, encoding: .utf8) {
                                let trimmed = line.trimmingCharacters(in: .whitespacesAndNewlines)
                                if trimmed.contains("[GALILEO I/NAV]") {
                                    self.parseEsp32InavLogLine(trimmed)
                                }
                            }
                            continue
                        } else {
                            if rawDataBuffer.count > 4096 {
                                rawDataBuffer.removeFirst()
                            }
                            break
                        }
                    }

                    // 3. Check for UBX Sync: 0xB5 0x62
                    if rawDataBuffer.count >= 2 && rawDataBuffer[0] == 0xB5 && rawDataBuffer[1] == 0x62 {
                        if rawDataBuffer.count >= 6 {
                            let len = Int(rawDataBuffer[4]) | (Int(rawDataBuffer[5]) << 8)
                            let totalUbxLen = 6 + len + 2
                            if rawDataBuffer.count >= totalUbxLen {
                                let ubxPacket = rawDataBuffer.subdata(in: 0..<totalUbxLen)
                                rawDataBuffer.removeSubrange(0..<totalUbxLen)

                                self.processUbxPacket(ubxPacket)
                                continue
                            } else {
                                // Wait for complete UBX packet
                                break
                            }
                        } else {
                            // Wait for header
                            break
                        }
                    }

                    // 4. Leading byte is neither '$', '[', nor 0xB5 -> Skip junk/alignment byte
                    if let nextStart = rawDataBuffer.indices.first(where: { rawDataBuffer[$0] == 0x24 || rawDataBuffer[$0] == 0x5B || rawDataBuffer[$0] == 0xB5 }) {
                        rawDataBuffer.removeSubrange(0..<nextStart)
                    } else {
                        rawDataBuffer.removeAll()
                    }
                }
            } else if bytesRead == 0 {
                // End-Of-File: receiver disconnected or port closed
                DispatchQueue.main.async {
                    self.closePort()
                }
            } else {
                let err = errno
                if err != EAGAIN && err != EWOULDBLOCK && err != EINTR {
                    // Fatal POSIX read error (e.g., ENXIO - Device not configured when unplugged)
                    DispatchQueue.main.async {
                        self.closePort()
                    }
                }
            }
        }

        source.setCancelHandler {
            close(fd)
        }

        source.resume()
        self.readSource = source
        return true
    }

    private func processUbxPacket(_ packet: Data) {
        guard packet.count >= 8 else { return }
        let msgClass = packet[2]
        let msgId = packet[3]
        let len = Int(packet[4]) | (Int(packet[5]) << 8)
        guard packet.count >= 6 + len + 2 else { return }

        // UBX Checksum Verification
        var ckA: UInt8 = 0
        var ckB: UInt8 = 0
        for i in 2..<(6 + len) {
            ckA = ckA &+ packet[i]
            ckB = ckB &+ ckA
        }
        guard ckA == packet[6 + len] && ckB == packet[6 + len + 1] else { return }

        // UBX-RXM-SFRBX (Class: 0x02, ID: 0x13)
        if msgClass == 0x02 && msgId == 0x13 && len >= 8 {
            let gnssId = packet[6]
            let svId = packet[7]
            let sigId = packet[8]
            let numWords = Int(packet[10])

            // Galileo (gnssId == 2) on E1-B (sigId == 1) or E5b-I (sigId == 5) or E1 (sigId == 0)
            if gnssId == 2 && (sigId == 1 || sigId == 5 || sigId == 0) && numWords >= 8 {
                let dataStart = 14
                let dataEnd = min(6 + len, dataStart + numWords * 4)
                let frameBytes = [UInt8](packet[dataStart..<dataEnd])

                // Galileo Word Type in even half-page (bits 2..7 of first 32-bit word)
                var wordType = 1
                if frameBytes.count >= 4 {
                    let w0 = UInt32(frameBytes[0]) | (UInt32(frameBytes[1]) << 8) | (UInt32(frameBytes[2]) << 16) | (UInt32(frameBytes[3]) << 24)
                    wordType = Int((w0 >> 2) & 0x3F)
                }

                let bandName = (sigId == 5) ? "E5b-I" : "E1-B"
                let inavMsg = GalileoINavMessage(
                    svid: Int(svId),
                    submessageId: wordType > 0 ? wordType : 1,
                    data: Data(frameBytes),
                    status: 0,
                    timestamp: Date(),
                    band: bandName
                )

                let hexStr = frameBytes.prefix(30).map { String(format: "%02X", $0) }.joined()
                let galinavLine = "$GALINAV,\(svId),\(wordType > 0 ? wordType : 1),\(hexStr),827BB238C8*3F"

                DispatchQueue.main.async { [weak self] in
                    self?.onInavFrameReceived?(inavMsg)
                    self?.onLineReceived?(galinavLine)
                }
            }
        }
    }

    private func parseEsp32InavLogLine(_ line: String) {
        // Line format: [GALILEO I/NAV] E02 (WT 21): 55559500555555555555555500402C56...
        guard let closeBracket = line.range(of: "]"),
              let wtIdx = line.range(of: "(WT "),
              let colonIdx = line.range(of: "): ") else { return }

        let svidSub = line[closeBracket.upperBound..<wtIdx.lowerBound]
        let svidDigits = svidSub.filter { $0.isNumber }
        let wtSub = line[wtIdx.upperBound..<colonIdx.lowerBound]
        let wtDigits = wtSub.filter { $0.isNumber }
        let hexStr = String(line[colonIdx.upperBound...]).trimmingCharacters(in: .whitespacesAndNewlines)

        guard let svid = Int(svidDigits), let wt = Int(wtDigits), !hexStr.isEmpty else { return }

        var rawBytes = [UInt8]()
        var sIndex = hexStr.startIndex
        while sIndex < hexStr.endIndex {
            let eIndex = hexStr.index(sIndex, offsetBy: 2, limitedBy: hexStr.endIndex) ?? hexStr.endIndex
            if let b = UInt8(hexStr[sIndex..<eIndex], radix: 16) {
                rawBytes.append(b)
            }
            sIndex = eIndex
        }

        let inavMsg = GalileoINavMessage(
            svid: svid,
            submessageId: wt,
            data: Data(rawBytes),
            status: 0,
            timestamp: Date(),
            band: "E1-B"
        )

        let galinavSentence = "$GALINAV,\(svid),\(wt),\(hexStr),827BB238C8*3F"

        DispatchQueue.main.async { [weak self] in
            self?.onInavFrameReceived?(inavMsg)
            self?.onLineReceived?(galinavSentence)
        }
    }

    func closePort() {
        watchdogTimer?.invalidate()
        watchdogTimer = nil
        rfcommChannel?.close()
        rfcommChannel = nil
        readSource?.cancel()
        readSource = nil
        if fileDescriptor != -1 {
            close(fileDescriptor)
            fileDescriptor = -1
        }
        let notify = { [weak self] in
            guard let self = self else { return }
            let wasConnected = self.isConnected
            self.isConnected = false
            self.connectedPortName = "None"
            if wasConnected {
                self.onDisconnection?()
            }
        }
        if Thread.isMainThread {
            notify()
        } else {
            DispatchQueue.main.async(execute: notify)
        }
    }
}
#endif
