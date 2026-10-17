# TRISAT - Multiplatform User Manual & Technical Reference Guide

Welcome to the **TRISAT (Authenticated Earth Intelligence)** multiplatform application manual. This document explains the repository architecture, features, GNSS testing workflows, and OSNMA verification tools provided by TRISAT across Android, iOS, iPadOS, macOS, and watchOS ecosystems.

---

## 1. Application Overview & Repository Structure

TRISAT is a **multiplatform GNSS intelligence solution** designed for **Galileo OSNMA (Open Service Navigation Message Authentication) testing**, multi-constellation GNSS signal analysis, anti-spoofing diagnostic evaluation, and spatial coordinate verification.

### Repository Layout:
* 📂 **`trisat_android/`**: Native Android Application (Kotlin, Jetpack Compose, Android GNSS HAL Location APIs).
* 📂 **`trisat_ios/`**: Native iOS, iPadOS, macOS, and watchOS Application (Swift, SwiftUI, CoreLocation, CoreBluetooth, MapKit).
* 📄 **`README.md`**: Project overview, feature summary, and build instructions.
* 📄 **`USER_MANUAL.md`**: Complete user manual and technical reference guide.

---

## 2. Core Capabilities

* **Real-time Open Sky View (Skyplot):** Polar visualization of tracked satellites categorized by constellation (Galileo Primary, GPS, GLONASS, BeiDou) with clean circle markers ($\bigcirc$).
* **Galileo I/NAV & OSNMA Receiver Test:** Direct extraction and inspection of raw Galileo `TYPE_GAL_I` navigation frames on supported Android devices or external BLE receivers on iOS/macOS.
* **CelesTrak TLE Orbital Propagator & 24h Time Slider:** Download real-time satellite orbital elements and simulate constellation geometries across a 24-hour window ($\pm 12\text{h}$).
* **Galileo-Only Authenticated Position Solution:** Calculates position convergence time and 3D fix accuracy exclusively from authenticated Galileo satellites.
* **Multi-Constellation Solution Matrix:** Side-by-side comparison of position, 2D/1D error, and sat count for Galileo OS-NMA, Galileo-Only, GPS-Only, GLONASS-Only, and BeiDou-Only.
* **Position History & Error Statistics:** Tracks RMS horizontal (2D), vertical (1D), and 3D position errors across logged epochs.
* **Cold-Start A-GNSS Purge:** Issues native Android HAL commands to flush cached A-GPS/A-GNSS ephemeris and almanac data.
* **Geospatial Navigator Exporter:** Dynamic hemisphere coordinates (`N`/`S` and `E`/`W`), clean decimal clipboard copying, and direct Apple Maps / Google Maps intent launching.

---

## 3. Screen & Feature Guide

### 📱 Dashboard Tab
1. **Open Sky Satellite View (Skyplot):**
   * Displays satellites in polar coordinates (Azimuth $0^\circ - 360^\circ$, Elevation $0^\circ - 90^\circ$).
   * Interactive filter chips allow toggling individual constellations (Galileo, GPS, GLONASS, BeiDou) on or off.
   * **Adaptive Layout:** 1-column vertical on iPhone; 2-column side-by-side on iPad with 320pt large Skyplot!
2. **Constellation Signal Matrix:**
   * Grid summary showing visible satellite count and average signal power ($C/N_0$) across Galileo, GPS, GLONASS, and BeiDou networks.
3. **DOP Geometry Quality Card:**
   * Displays HDOP, VDOP, PDOP, and 3D Geometry Quality indicator.
4. **Embedded Geospatial Mini Map:**
   * Live interactive MapKit view displaying current GPS position pin marked `GNSS LIVE`.

---

### 📶 Signal Processing Tab
1. **GNSS Raw Signal Monitor Header:**
   * Shows active mode status (`REAL HARDWARE` vs `SIMULATION MODE`) with toggle button.
   * Displays total tracked satellites, average $C/N_0$ (dB-Hz), and HDOP.
2. **Galileo I/NAV & OSNMA Receiver Test:**
   * Evaluates if your device's hardware driver exposes raw `TYPE_GAL_I` I/NAV pages.
   * Displays real-time captured I/NAV frame count and raw hexadecimal payloads (`0x1B0A4F...`).
3. **Multi-Constellation Solution Matrix:**
   * Comparative table showing position, 2D/1D error, and sat count across all 5 constellation configurations.

---

### 🛡️ Threat Analysis Tab
1. **Trustability Overview Gauge Meter:**
   * Real-time 0–100% Trustability Score with High/Medium/Low trust classification.
2. **Anti-Spoofing Status & Interactive Threat Scenario Switcher:**
   * Interactively test GNSS attack vectors: Nominal, Spoofing, Jamming, Clock Drift, OSNMA Key Failure.
3. **Real-Time Threat Intelligence Event Logs:**
   * Live timeline logging detected signal anomalies and security events.

---

### ⚙️ Settings & Logs Tab
1. **GNSS Receiver Mode Selector:**
   * Toggle between Internal Receiver, External Bluetooth GNSS Receiver, and Orbit TLE Simulation.
2. **CelesTrak TLE Orbit Downloader:**
   * Asynchronously fetches and caches fresh TLE orbits.
   * Displays formatted **Last Download Timestamp** stored persistently in `UserDefaults`.
3. **Live Orbit Simulation SkyPlot Preview (iPad):**
   * Real-time live Skyplot preview inside Settings updating as the user adjusts the 24-hour time slider ($\pm 12\text{h}$).
4. **System Information & Build Details:**
   * Displays dynamic Build Version (`v1.1.0`), Code (`2`), and Compilation Timestamp (`17 Oct 2026`).

---

## 4. Multi-Device Compatibility (macOS & watchOS)

TRISAT is designed using modular SwiftUI and Combine, making cross-apple ecosystem extensions seamless:

### 💻 macOS Extension (Mac Catalyst / Native macOS):
* Enable `macOS (Designed for iPad)` or `Mac Catalyst` in Xcode target settings.
* Connect external high-precision GNSS receivers (u-blox ZED-F9P / Septentrio) via Bluetooth LE or USB Serial on macOS.
* Add a `MenuBarExtra` in SwiftUI to run TRISAT in the macOS top menu bar showing real-time GNSS security status.

### ⌚ watchOS Extension (Apple Watch):
* Add a `watchOS App` target in `TRISAT.xcodeproj`.
* CoreLocation & CoreBluetooth run natively on Apple Watch.
* Provides a compact wrist Skyplot, instant coordinate reader, and haptic wrist alerts when spoofing or jamming is detected.

---

## 5. How to Test OSNMA on a Physical Device

### Android Device (`trisat_android`):
1. Build and install the APK:
   ```bash
   ./gradlew assembleDebug
   ```
2. Open TRISAT outdoors under clear open sky and grant location permissions.
3. Go to **Signal Monitor** $\rightarrow$ **Galileo I/NAV & OSNMA Receiver Test**.
4. **Result:**
   * **Green Badge (`I/NAV CAPABLE (OSNMA OK)`)**: Device delivers raw Galileo `TYPE_GAL_I` navigation frames. The device is fully capable of Galileo OSNMA authentication!
   * **Orange Badge (`NO I/NAV DETECTED`)**: Device hardware driver does not expose raw I/NAV pages required for OSNMA.

### iOS / iPadOS Device (`trisat_ios`):
Open `trisat_ios/TRISAT.xcodeproj` in Xcode, connect your iPhone or iPad, and run the app (`Cmd + R`) to test location & TLE satellite simulation.
