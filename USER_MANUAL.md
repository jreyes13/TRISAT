# TRISAT - Multiplatform User Manual & Technical Reference Guide

Welcome to the **TRISAT (Authenticated Earth Intelligence)** multiplatform application manual. This document explains the repository architecture, features, GNSS testing workflows, and OSNMA verification tools provided by TRISAT across Android, iOS, iPadOS, and macOS ecosystems.

---

## 1. Application Overview & Repository Structure

TRISAT is a **multiplatform GNSS intelligence solution** designed for **Galileo OSNMA (Open Service Navigation Message Authentication) testing**, multi-constellation GNSS signal analysis, anti-spoofing diagnostic evaluation, and spatial coordinate verification.

### Repository Layout:
* 📂 **`trisat_android/`**: Native Android Application (Kotlin, Jetpack Compose, Android GNSS HAL Location APIs).
* 📂 **`trisat_ios/`**: Native iOS & iPadOS Application (Swift, SwiftUI, CoreLocation, CoreBluetooth, MapKit).
* 📂 **`trisat_mac/`**: Native macOS Application (Swift, SwiftUI, Network.framework, PyUSB, libusb, POSIX serial port).
* 📂 **`SX_Blue/`**: Python USB hardware drivers and telemetry bridges for Geneq SXblue receivers:
  * `sxblue_config.py`: One-time EEPROM setup tool for FTDI UART at 19200 bps ($JBIN 94/95).
  * `sxblue_reader.py`: Continuous USB reader and local UDP telemetry broadcaster (`127.0.0.1:9090`).
  * `run_bridge.sh`: Executable launcher with privilege elevation (`sudo arch -x86_64`).
* 📄 **`README.md`**: Project overview, feature summary, and build instructions.
* 📄 **`USER_MANUAL.md`**: Complete user manual and technical reference guide.

---

## 2. Core Capabilities

* **Anti-Flicker Open Sky Polar Skyplot:** Interactive 360° skyplot with constellation color coding (Galileo Cyan, GPS Amber, GLONASS Red, BeiDou Green) with a persistent 30-second satellite cache that **completely eliminates flickering**.
* **Satellite C/N0 Signal Strength Bar Chart (Diagrama de Barras):** Real-time vertical SNR bar chart displaying carrier-to-noise ratio in dB-Hz color-coded by signal quality (Green $\ge 35$ dB-Hz, Yellow $\ge 25$ dB-Hz, Red $< 25$ dB-Hz) with constellation PRN badges (`E02`, `G08`, `R05`, `B12`).
* **Geneq SXblue USB Telemetry Bridge:** Low-level PyUSB/libusb driver interfacing with the Geneq SXblue FTDI USB chip (`VID: 0x0403`, `PID: 0xb5a0`) at 19200 bps, broadcasting JSON and raw NMEA/$BIN datagrams to `127.0.0.1:9090` via `Network.framework`.
* **Galileo I/NAV & OSNMA Receiver Test:** Direct extraction and inspection of raw Galileo `TYPE_GAL_I` navigation frames and 250-bit subframe pages.
* **CelesTrak TLE Orbital Propagator & 24h Time Slider:** Download real-time satellite orbital elements and simulate constellation geometries across a 24-hour window ($\pm 12\text{h}$).
* **Differential DGPS / RTK Telemetry Analysis:** Real-time decoding of `$GPGGA` differential fix mode, base station ID, correction age (seconds), HDOP/PDOP, and `$GPGST` pseudorange RMS error statistics.
* **Geospatial Navigator Exporter:** Dynamic hemisphere coordinates (`N`/`S` and `E`/`W`), clean decimal clipboard copying, and direct Apple Maps / Google Maps intent launching.

---

## 3. Screen & Feature Guide

### 📱 Dashboard Tab
1. **Open Sky Satellite View (Skyplot):**
   * Displays satellites in polar coordinates (Azimuth $0^\circ - 360^\circ$, Elevation $0^\circ - 90^\circ$).
   * Interactive filter chips allow toggling individual constellations (Galileo, GPS, GLONASS, BeiDou) on or off.
   * **Adaptive Layout:** 1-column vertical on iPhone; 2-column side-by-side on iPad & macOS with 320pt large Skyplot!
2. **Constellation Signal Matrix:**
   * Grid summary showing visible satellite count and average signal power ($C/N_0$) across Galileo, GPS, GLONASS, and BeiDou networks.
3. **DOP Geometry Quality Card:**
   * Displays HDOP, VDOP, PDOP, and 3D Geometry Quality indicator.
4. **Embedded Geospatial Mini Map:**
   * Live interactive MapKit view displaying current GPS/Galileo position pin marked `GNSS LIVE`.

---

### 📶 Signal Processing Tab (Unified 4-Quadrant View)

The **Signal Processing** tab features a clean 4-quadrant layout displaying all telemetric fields on a single screen without switching tabs:

1. **Top Differential & Hardware Metrics Header:**
   * **Fix Mode:** *DGPS Differential (SXblue)*, *GPS Autonomous*, *RTK Fixed (Centimeter)*.
   * **Sats in Fix / HDOP:** Live satellite count and Horizontal Dilution of Precision.
   * **DGPS Base Station:** Base Station ID (e.g. `0124`) and correction age in seconds.
   * **GST Pseudorange Error:** RMS pseudorange noise error in meters.
2. **Quadrant 1 — Open Sky Radar (Skyplot Compacto):**
   * Compact polar radar displaying satellite positions ($\bigcirc$) without flickering.
3. **Quadrant 2 — Satellite Signal Strength C/N0 Bar Chart:**
   * Vertical SNR bars in dB-Hz color-coded green/yellow/red with PRN badges (`E02`, `G08`, `R05`, `B12`).
4. **Quadrant 3 — ASCII NMEA Telemetry Stream:**
   * Full-width monospaced green terminal scrolling incoming `$GPGGA`, `$GAGSV`, `$GPGST`, `$GPRMC` sentences.
5. **Quadrant 4 — Galileo I/NAV Subframes:**
   * Live cards showing demodulated 250-bit Galileo navigation messages.

---

### 🛡️ Threat Analysis Tab
1. **Overall Threat Score Gauge:**
   * Real-time 0–100% Threat Score with `NOMINAL / SAFE`, `MODERATE RISK`, or `CRITICAL THREAT` classification.
2. **Galileo OSNMA Cryptographic Auth:**
   * Dynamic status (`NO I/NAV DATA`, `SYNCING KEYS`, `AUTHENTICATED`, `AUTH FAILED`) evaluating HKROOT certificate and TESLA key chains.
3. **Jamming & RF Interference Detection:**
   * Real-time average $C/N_0$ SNR analysis across tracked satellites and Automatic Gain Control (AGC) monitoring.
4. **Spoofing / Meaconing Analysis:**
   * Pseudorange residual noise checks (`$GPGST`) and orbit consistency checks against CelesTrak TLE propagations.
5. **Differential DGPS & Base Station Integrity:**
   * Verifies differential station ID and correction age to protect against base station spoofing attacks.

---

### ⚙️ Settings Tab
1. **Bluetooth GNSS Receivers Card:**
   * Dedicated BLE device scanner and connection dropdown.
2. **USB / Local Bridges (Daemon) Card:**
   * Dedicated selector for `SXblue USB Bridge (UDP 127.0.0.1:9090)`.
   * Real-time listener status (`Disconnected`, `Listening on :9090`, `Streaming Telemetry`) and datagram counter.
3. **CelesTrak TLE Orbit Downloader:**
   * Asynchronously fetches and caches fresh TLE orbits.
   * Displays formatted **Last Download Timestamp** stored persistently in `UserDefaults`.

---

## 4. How to Connect the Geneq SXblue Receiver (macOS)

Because Geneq SXblue USB receivers use a custom FTDI Product ID (`VID: 0x0403, PID: 0xb5a0`), macOS does not generate a standard `/dev/cu.usbserial` node. Follow these steps to connect:

### 1️⃣ Run the USB Reader Daemon
Open Terminal and run the launcher script:
```bash
sudo ./SX_Blue/run_bridge.sh
```
*(Enter your Mac password when prompted)*. Terminal will confirm:
```text
======================================================
 TRISAT SXblue USB Telemetry Daemon (Localhost UDP 9090)
======================================================
[+] UDP Socket Active -> 127.0.0.1:9090
[+] Attached to SXblue Receiver (VID: 0x403, PID: 0xb5a0)
[+] FTDI UART configured at 19200 bps.

[+] Streaming USB Telemetry to localhost:9090... Press Ctrl+C to stop.
```

### 2️⃣ Launch TRISAT macOS App
Open TRISAT in Xcode (`trisat_mac`) and press **`Run`** (`Cmd + R`).
* In **Signal Processing**, all 4 quadrants (Skyplot, C/N0 Bar Chart, NMEA Stream, Galileo I/NAV) will update live in real-time.

---

## 5. How to Test OSNMA on a Physical Device

### Android Device (`trisat_android`):
1. Build and install the APK:
   ```bash
   ./gradlew assembleDebug
   ```
2. Open TRISAT outdoors under clear open sky and grant location permissions.
3. Go to **Signal Processing** $\rightarrow$ **Galileo I/NAV & OSNMA Receiver Test**.
4. **Result:**
   * **Green Badge (`I/NAV CAPABLE (OSNMA OK)`)**: Device delivers raw Galileo `TYPE_GAL_I` navigation frames.
   * **Orange Badge (`NO I/NAV DETECTED`)**: Device hardware driver does not expose raw I/NAV pages required for OSNMA.

### iOS / macOS Target (`trisat_ios` / `trisat_mac`):
1. Open `trisat_mac/TRISAT.xcodeproj` or `trisat_ios/TRISAT.xcodeproj` in Xcode.
2. Build and run the app (`Cmd + R`).
3. Connect the SXblue receiver over USB daemon or Bluetooth LE to stream raw Galileo I/NAV frames into the app.
