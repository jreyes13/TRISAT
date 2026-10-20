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

## 📱 Hardware Compatibility & Android GNSS Benchmarking

### OEM Driver Support & Requirements
Running client-side Galileo OS-NMA verification directly on Android mobile devices requires OEM driver support for raw navigation message delivery via `GnssNavigationMessage.Callback` (specifically `TYPE_GAL_I`). While Google introduced raw GNSS measurement APIs in Android 7/8+, hardware support varies across chipset vendors (Qualcomm Snapdragon, MediaTek, Samsung Exynos, Unisoc).

### Industry Diagnostic Reference: GPSTest
TRISAT benchmarks device compatibility against the open-source **GPSTest** application and its crowdsourced capabilities database:
* **GPSTest App:** [GPSTest on Google Play](https://play.google.com/store/apps/details?id=com.android.gpstest)
* **GitHub Repository:** [barbeau/gpstest](https://github.com/barbeau/gpstest) (Created and maintained by Sean Barbeau)
* **Android Capabilities Matrix:** [GPSTest Crowdsourced GNSS Database (Google Sheets)](https://docs.google.com/spreadsheets/d/1jXtRCoEnnFNWj6_oFlVWflsf-b0jkfZpyhN-BXsv7uo/edit?gid=0#gid=0)

#### Minimum Device Requirements for Native OSNMA Extraction:
| Capability / API | Minimum Status Required | Criticality for TRISAT | Impact if Unsupported |
| :--- | :---: | :--- | :--- |
| **Android Version** | Android 8.0+ (API 26) | Mandatory | Core raw GNSS and navigation message APIs are unavailable. |
| **Navigation Messages** | `SUPPORTED` | Mandatory | Inability to extract Galileo I/NAV frames; OSNMA verification cannot operate locally. |
| **Dual-Frequency (E1/E5a)** | `SUPPORTED` | Recommended | Requires single-frequency ionospheric models (NeQuick-G), reducing overall accuracy. |
| **Accumulated Delta Range (ADR)** | `SUPPORTED` | Recommended | Prevents carrier-phase smoothing, limiting positioning accuracy to code pseudorange baseline. |

Before deploying or testing TRISAT on an Android smartphone, verify in the [GPSTest Database](https://docs.google.com/spreadsheets/d/1jXtRCoEnnFNWj6_oFlVWflsf-b0jkfZpyhN-BXsv7uo/edit?gid=0#gid=0) that your device model has **`SUPPORTED`** in the `Navigation messages` column.

> **Note for iOS Devices:** Apple's `CoreLocation` framework abstracts raw constellation navigation messages and does not provide an equivalent to Android's `GnssNavigationMessage`. TRISAT for iOS retrieves high-accuracy position and telemetry via `CoreLocation`, and supports streaming raw Galileo I/NAV pages via an external GNSS receiver (e.g., u-blox ZED-F9P, Septentrio, SXblue) over Bluetooth Low Energy (BLE).

---

## 📖 Documentation

* For complete usage instructions, technical architecture, and OSNMA testing workflows, please refer to the [USER_MANUAL.md](USER_MANUAL.md).
* **State of the Art & Overview:** Consult the technical state of the art and ecosystem overview in the [TRISAT Overview Document](https://docs.google.com/document/d/1hdZqK1QkqJp_4wGj9-DmBCKDsRlN9INIX3hqUMN9QLg/edit?usp=sharing).

---

## 🛠️ How to Build

### Android App (`trisat_android`)
```bash
# Assemble Debug APK
./gradlew assembleDebug