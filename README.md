# TRISAT - Authenticated Earth Intelligence

TRISAT is an advanced **multiplatform GNSS intelligence application** (Android & iOS) designed for **Galileo OSNMA (Open Service Navigation Message Authentication) testing**, multi-constellation GNSS signal analysis, anti-spoofing diagnostic evaluation, and spatial coordinate verification.

![TRISAT Banner](trisat_android/src/main/res/drawable/trisat_logo.png)

## 🌐 Multiplatform Architecture

TRISAT is built as a cross-ecosystem solution with native, high-performance implementations for both major mobile operating systems:

| Platform | Directory in Repository | Tech Stack | Key Capabilities |
| :--- | :--- | :--- | :--- |
| **Android** | `trisat_android/` | Kotlin, Jetpack Compose, Android GNSS HAL APIs | Raw `TYPE_GAL_I` I/NAV frames, AGC levels, RINEX logging, A-GNSS cold-start reset |
| **iOS** | `trisat_ios/` | Swift, SwiftUI, CoreLocation, CoreBluetooth, MapKit | CelesTrak TLE orbit propagator, 24h time scrubbing, BLE GNSS receiver, Live Map |

---

## 🛰️ Key Features

* 🌌 **Open Sky Polar Skyplot:** Interactive 360° skyplot with constellation color coding (Galileo Cyan, GPS Amber, GLONASS Red, BeiDou Green) and clean circle markers ($\bigcirc$).
* 🔒 **Galileo I/NAV & OSNMA Receiver Test:** Direct extraction and inspection of raw `TYPE_GAL_I` navigation frames.
* 🛰️ **CelesTrak TLE Orbital Propagator & 24h Time Slider:** Download real-time satellite orbital elements and simulate constellation geometries across a 24-hour window ($\pm 12\text{h}$).
* 📱 **Adaptive iPhone & iPad Layouts:**
  * **iPhone:** Single-column vertical layout with compact banner logo, Skyplot, position telemetry, and mini map.
  * **iPad:** Dual-column side-by-side layout with wide 320pt Skyplot, Signal Matrix, DOP Quality cards, and live settings orbit preview!
* 🗺️ **Embedded Geospatial Mini Map:** Interactive Map displaying current GPS position pin marked `GNSS LIVE`.
* 📊 **Multi-Constellation Solution Matrix:** Side-by-side comparison of position, 2D/1D error, and sat count for Galileo OS-NMA, Galileo-Only, GPS-Only, GLONASS-Only, and BeiDou-Only.
* 📈 **Position History & Error Statistics:** RMS 2D horizontal, 1D vertical, and 3D position error metrics across logged epochs.
* 🔄 **Cold-Start A-GNSS Purge:** Native Android HAL commands to flush cached A-GPS/A-GNSS ephemeris data.
* 📍 **Geospatial Navigator Exporter:** Dynamic hemisphere coordinates (`N`/`S` and `E`/`W`), clean decimal clipboard copying, and direct Apple Maps / Google Maps intent launching.

---

## 📖 Documentation

For complete usage instructions, technical architecture, and OSNMA testing workflows, please refer to the [USER_MANUAL.md](USER_MANUAL.md).

---

## 🛠️ How to Build

### Android App (`trisat_android`)
```bash
# Assemble Debug APK
./gradlew assembleDebug
```
Output APK location: `trisat_android/build/outputs/apk/debug/trisat_android-debug.apk`

### iOS App (`trisat_ios`)
Open `trisat_ios/TRISAT.xcodeproj` in **Xcode** (`Cmd + R` or `xcodebuild`) and run on your iOS device or simulator.
