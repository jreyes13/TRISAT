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
* 📂 **`docs/`**: Comprehensive project documentation:
  * `USER_MANUAL.md`: Complete user manual and technical reference guide.
  * `WEB_SERVER_GUIDE.md`: Guide for the SDR Web Dashboard and telemetry REST API.
* 📂 **`notebooks/`**: Interactive Google Colab / Jupyter computational notebooks.
* 📄 **`README.md`**: Project overview, feature summary, and build instructions.

---

## 2. Core Capabilities

* **Anti-Flicker Open Sky Polar Skyplot:** Interactive 360° skyplot with constellation color coding (Galileo Cyan, GPS Amber, GLONASS Red, BeiDou Green) with a persistent 30-second satellite cache that **completely eliminates flickering**.
* **Satellite C/N0 Signal Strength Bar Chart:** Real-time vertical SNR bar chart displaying carrier-to-noise ratio in dB-Hz color-coded by signal quality (Green $\ge 35$ dB-Hz, Yellow $\ge 25$ dB-Hz, Red $< 25$ dB-Hz) with constellation PRN badges (`E02`, `G08`, `R05`, `B12`).
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

### 📶 Signal Processing Tab

The **Signal Processing** tab provides a clean, desktop-fitted 2×2 view of all telemetric, RF, and navigation streams (all hardware/SDR configuration controls are centralized in the **Settings** tab):

1. **Top Differential & Telemetry Status Header:**
   * **Active Source & Constellation Filters:** Displays the active receiver source and interactive filter chips (`Galileo`, `GPS`, `GLONASS`, `BeiDou`).
   * **Fix Mode:** *Galileo E6 HAS PPP (SL1)*, *DGPS Differential (SXblue)*, *GPS Autonomous*, *RTK Fixed (Centimeter)*.
   * **Sats in Fix / HDOP:** Live satellite count and Horizontal Dilution of Precision.
   * **Correction Source:** Station / Service ID (e.g. `E6-HAS-MT1` or `0124`) and correction age in seconds.
   * **Pseudorange Error:** RMS pseudorange noise error and DIA Baarda $w$-test statistics.
2. **Open Sky Radar:**
   * Compact polar radar displaying 4-constellation satellite positions ($\bigcirc$) without flickering.
3. **Satellite Signal Strength C/N0 Bar Chart:**
   * Vertical SNR bars in dB-Hz color-coded green/yellow/red with constellation legend and PRN badges (`E02`, `G08`, `R05`, `B12`).
4. **ASCII NMEA Telemetry Stream:**
   * Full-width monospaced green terminal scrolling incoming `$GNGGA`, `$GPGGA`, `$GAGSV`, `$GPGST`, `$GALINAV`, and `$GALE6HAS` sentences.
5. **Galileo I/NAV & E6 HAS Subframes:**
   * Live cards showing demodulated 240-bit Galileo I/NAV v2.2 pages (`WT 1..6, 16, 17..20, 22, 44`), `CRC-24Q` parity status, and 40-bit OSNMA (`HKROOT` + `MACK`) fields.

---

### 🛡️ Threat Analysis Tab
1. **Interactive Attack Simulation Bar & Threat Score Gauge:**
   * Real-time 0–100% Threat Risk Score with one-click interactive attack simulation buttons:
     * **Nominal:** Clean multi-constellation RF spectrum and valid Galileo OSNMA v1.1 TESLA tags.
     * **Jamming:** Simulates broadband L1/E1/E6 noise floor injection (+18 dB AGC saturation), dropping $C/N_0$ by ~22 dB-Hz and degrading 3D fix geometry.
     * **Spoofing:** Simulates a coherent false-signal emitter with anomalously flat high $C/N_0$ (51–52 dB-Hz), spatial azimuth clustering, and $\pm 48.5\text{ m}$ `$GPGST` pseudorange residual divergence.
     * **Meaconing:** Simulates a delayed RF rebroadcast attack (+420 ns clock jump) with skyplot rotation and DGPS age lag.
     * **OSNMA Fail:** Simulates Galileo E1-B I/NAV navigation data manipulation with a forged 40-bit OSNMA MACK tag caught by HMAC-SHA-256 verification.
2. **Embedded Spatial Skyplot & C/N0 Spectrum Bar Chart:**
   * Displays the live polar **Skyplot** and **Satellite $C/N_0$ Bar Chart** side-by-side inside Threat Analysis so you can visually inspect the spatial and spectral impact of each simulated attack in real time.
3. **6 Diagnostic Security Cards:**
   * **Galileo OSNMA Cryptographic Auth (v1.1)**, **Jamming & RF Interference**, **Spoofing / Coherent Emitter Detection**, **Meaconing / Replay Delay Monitor**, **Galileo E6-B HAS-UA & DIA Integrity**, and **Galileo EWSS-CAMF & WT44 Timing Service**.

---

### ⚙️ Settings Tab
All hardware, SDR, and orbital configuration controls are centralized in the **Settings** tab:
1. **GNSS Receiver & Telemetry Mode:**
   * Segmented selector for `Internal + TLE`, `External BLE / SDR Bridge`, and `CelesTrak TLE Simulation`.
2. **TRISAT-SDR Dual-HackRF & Local Bridges:**
   * Centralized **HackRF #1 Secondary Band** selector (`E6B/C HAS PPP SL1`, `E5a F/NAV`, `E5b I/NAV`, `E5 Full AltBOC`), `Start / Stop TRISAT-SDR` engine control, one-click `Web Dashboard (:8085)` launcher, and `SXblue (sudo)` USB bridge launcher.
3. **CelesTrak 4-Constellation TLE Orbits & Live Skyplot Preview:**
   * Built-in 116-satellite 4-constellation Keplerian Walker catalog (`30 Galileo`, `31 GPS`, `24 GLONASS`, `31 BeiDou`) with live CelesTrak cloud refresh, interactive constellation filter chips, and a $\pm 12\text{h}$ Keplerian orbit time-offset slider.
4. **Bluetooth GNSS Receivers & Hardware USB Serial Ports:**
   * Dedicated BLE scanner and POSIX `/dev/cu.usbserial*` port connector.

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
