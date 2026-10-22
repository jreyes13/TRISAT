# TRISAT - Authenticated Earth Intelligence

> **TRISAT** (**T**ri-band **R**eal-time **I**ntegrity & **S**atellite **A**uthentication **T**erminal) — representing **Tri-Band** Galileo reception (**E1 / E5 / E6**), **Tri-Service** GNSS verification (**OSNMA** cryptographic authentication, **HAS** high-accuracy PPP, and **EWSS/TSM** integrity), and **Tri-Platform** native execution (**Android, iOS & macOS + SDR**).

TRISAT is an advanced **multiplatform GNSS intelligence application** (Android, iOS, macOS & Software-Defined Radio) designed for **Galileo OSNMA (Open Service Navigation Message Authentication)**, **Galileo E6 HAS (High Accuracy Service)** precise point positioning, multi-constellation GNSS signal analysis, and anti-spoofing diagnostic evaluation.

![TRISAT Banner](trisat_android/src/main/res/drawable/trisat_logo.png)

## 🌐 Multiplatform Architecture

TRISAT is built as a cross-ecosystem solution with native, high-performance implementations for Android, iOS, and macOS:

| Platform | Directory | Tech Stack | Key Capabilities |
| :--- | :--- | :--- | :--- |
| **Android** | `trisat_android/` | Kotlin, Jetpack Compose, Android GNSS HAL APIs | Raw `TYPE_GAL_I` I/NAV frames, AGC levels, RINEX logging, A-GNSS cold-start reset |
| **iOS** | `trisat_ios/` | Swift, SwiftUI, CoreLocation, CoreBluetooth, MapKit, CryptoKit | CelesTrak TLE orbit propagator, 24h time scrubbing, BLE GNSS receiver, Galileo OSNMA Annex A engine |
| **macOS** | `trisat_ios/` & `build/TRISAT.app` | Swift, SwiftUI, Network.framework, CryptoKit, PyUSB | Integrated **TRISAT-SDR (E1 OSNMA + E6 HAS-UA)** launcher, Geneq SXblue FTDI USB driver, anti-flicker Skyplot & C/N0 Bar Chart |
| **SDR Engine** | `trisat_sdr/` | Python 3, NumPy/SciPy, Dual-HackRF (`10 MHz CLKOUT/CLKIN`) | Portable SDR receiver (`E1B/C`, `E5a`, `E5b`, `E5 AltBOC`, `E6B/C HAS`, `GPS L1`), **OSNMA v1.1**, **HAS-UA (`PPPSD`/`DIA`/`NTCM-G`)** & Web UI (`:8085`) |

---

## 🛰️ Key Features

* 🌌 **Anti-Flicker Open Sky Polar Skyplot:** Interactive 360° skyplot with constellation color coding (Galileo Cyan, GPS Amber, GLONASS Red, BeiDou Green) and a persistent 12-second satellite cache that **eliminates flickering**.
* 📊 **Satellite C/N0 Signal Strength Bar Chart:** Real-time vertical SNR bar chart displaying carrier-to-noise ratio in dB-Hz color-coded by signal quality (Green $\ge 35$ dB-Hz, Yellow $\ge 25$ dB-Hz, Red $< 25$ dB-Hz) with constellation PRN badges (`E02`, `G08`, `R05`, `B12`).
* 🔌 **Geneq SXblue USB & UDP Telemetry Bridge:** Low-level PyUSB/libusb driver interfacing with the Geneq SXblue FTDI USB chip (`VID: 0x0403`, `PID: 0xb5a0`) at 19200 bps, broadcasting JSON and raw NMEA/$BIN datagrams to `127.0.0.1:9090` via `Network.framework`.
* 🔒 **Galileo I/NAV & OSNMA Receiver Test:** Direct extraction and inspection of raw `TYPE_GAL_I` navigation frames and 250-bit subframe pages.
* 🛰️ **CelesTrak TLE Orbital Propagator & 24h Time Slider:** Download real-time satellite orbital elements and simulate constellation geometries across a 24-hour window ($\pm 12\text{h}$).
* 📱 **Adaptive Multiplatform Layouts:** Wide 2-column iPad & macOS layouts with full-width centered terminals, DOP quality cards, and clean settings panels.
* 🗺️ **Embedded Geospatial Mini Map:** Interactive Map displaying current GPS/Galileo position pin marked `GNSS LIVE`.

---

## ⚡ Geneq SXblue USB Telemetry Bridge (macOS)

Because Geneq SXblue receivers use a custom FTDI Product ID (`VID: 0x0403, PID: 0xb5a0`), macOS does not create a standard `/dev/cu.usbserial` device. TRISAT bridges the receiver to the macOS app using a local UDP daemon.

### 1️⃣ One-Time Receiver EEPROM Configuration
Program the SXblue EEPROM to enable Galileo I/NAV subframes (`$JBIN,94,1` & `$JBIN,95,1`):
```bash
sudo arch -x86_64 python3 ./SX_Blue/sxblue_config.py
```

### 2️⃣ Continuous Telemetry Daemon
Start the continuous USB reader and local UDP broadcaster (`127.0.0.1:9090`):
```bash
sudo ./SX_Blue/run_bridge.sh
# or: sudo arch -x86_64 python3 ./SX_Blue/sxblue_reader.py
```

### 3️⃣ Launch TRISAT macOS Application
Open TRISAT from Xcode (`trisat_ios/TRISAT.xcodeproj`) or directly from Terminal:
```bash
open ./build/TRISAT.app
```
* **Signal Processing:** View live full-width green NMEA stream, Galileo I/NAV cards, and the C/N0 SNR Bar Chart.
* **Dashboard:** View real-time coordinates, altitude, fix quality (*GPS Fix*, *DGPS Fix*, *RTK Fixed*), and anti-flicker Skyplot.

---

## 📖 Documentation & Interactive Colab Notebooks

* **User Manual:** For complete usage instructions, technical architecture, and OSNMA/HAS testing workflows, please refer to [USER_MANUAL.md](USER_MANUAL.md).
* **Interactive Google Colab Notebooks:** For executable demonstrations of Galileo E1-B I/NAV v2.2 decoding, OSNMA Annex A cryptographic verification, and E6 HAS-UA precise point positioning, see [notebooks/README.md](notebooks/README.md).
* **TRISAT-SDR Documentation:** For Dual-HackRF SDR configuration and GNSS reference engines, see [trisat_sdr/README.md](trisat_sdr/README.md).

---

## 🛠️ How to Build

### Android App (`trisat_android`)
```bash
./gradlew assembleDebug
```

### macOS App (`trisat_mac`)
```bash
xcodebuild -project trisat_mac/TRISAT.xcodeproj -scheme TRISAT -destination 'platform=macOS,arch=arm64' build
```

### iOS App (`trisat_ios`)
```bash
xcodebuild -project trisat_ios/TRISAT.xcodeproj -scheme TRISAT -sdk iphonesimulator build
```
