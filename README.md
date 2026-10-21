# TRISAT - Authenticated Earth Intelligence

TRISAT is an advanced **multiplatform GNSS intelligence application** (Android, iOS & macOS) designed for **Galileo OSNMA (Open Service Navigation Message Authentication) testing**, multi-constellation GNSS signal analysis, anti-spoofing diagnostic evaluation, and spatial coordinate verification.

![TRISAT Banner](trisat_android/src/main/res/drawable/trisat_logo.png)

## 🌐 Multiplatform Architecture

TRISAT is built as a cross-ecosystem solution with native, high-performance implementations for Android, iOS, and macOS:

| Platform | Directory | Tech Stack | Key Capabilities |
| :--- | :--- | :--- | :--- |
| **Android** | `trisat_android/` | Kotlin, Jetpack Compose, Android GNSS HAL APIs | Raw `TYPE_GAL_I` I/NAV frames, AGC levels, RINEX logging, A-GNSS cold-start reset |
| **iOS** | `trisat_ios/` | Swift, SwiftUI, CoreLocation, CoreBluetooth, MapKit, CryptoKit | CelesTrak TLE orbit propagator, 24h time scrubbing, BLE GNSS receiver, EUSPA OSNMA Annex A engine |
| **macOS** | `trisat_ios/` & `build/TRISAT.app` | Swift, SwiftUI, Network.framework, CryptoKit, PyUSB | Integrated **TRISAT-SDR (E1 OSNMA + E6 HAS-UA)** launcher, Geneq SXblue FTDI USB driver, anti-flicker Skyplot & C/N0 Bar Chart |
| **SDR Engine** | `trisat_sdr/` | Python 3, NumPy/SciPy, Dual-HackRF (`10 MHz CLKOUT/CLKIN`) | Portable SDR receiver (`E1B/C`, `E5a`, `E5b`, `E5 AltBOC`, `E6B/C HAS`, `GPS L1`), EUSPA **OSNMA v1.1**, **HAS-UA (`PPPSD`/`DIA`/`NTCM-G`)** & Web UI (`:8085`) |

---

## 📓 Interactive Google Colab Notebooks (`notebooks/`)

Run and explore TRISAT's **Galileo I/NAV v2.2**, **OSNMA v1.1**, and **E6 High Accuracy Service (`HAS-UA`)** cryptographic and positioning algorithms directly in your browser using Google Colab:

| # | Notebook | Topics Covered | Launch in Browser |
| :-: | :--- | :--- | :-: |
| **01** | [`notebooks/01_galileo_inav_demo.ipynb`](notebooks/01_galileo_inav_demo.ipynb) | **Galileo E1-B I/NAV v2.2 & OSNMA Bit-Level Decoder:** 240-bit nominal page structure, `CRC-24Q` (`0x1864CFB`), `WT 1..6`, `WT 16 RedCED`, `WT 17..20 RS(118,58)`, `WT 22 ARAIM`, `WT 44 TSM`, `HKROOT`/`MACK` extraction & EUSPA Annex A TESLA/HMAC verification. | [![Open In Colab](https://colab.research.google.com/assets/colab-badge.svg)](https://colab.research.google.com/github/jreyes13/TRISAT/blob/main/notebooks/01_galileo_inav_demo.ipynb) |
| **02** | [`notebooks/02_euspa_osnma_has_complete_suite.ipynb`](notebooks/02_euspa_osnma_has_complete_suite.ipynb) | **Full EUSPA Reference Suite (`OSNMA` + `HAS-UA` + `SIS v2.2`):** Dual-HackRF `CBOC(6,1,1/11)` vs `BPSK(5)`, Merkle Tree + `ECDSA P-256` + `TESLA` + `MACSEQ`, Reed-Solomon `RS(255,32)` over $\text{GF}(2^8)$, `NTW->ECEF` orbit corrections, `PPPSD` EKF + `DIA` Baarda $w$-test, `NTCM-G` 12-coeff ionosphere & `EWSS-CAMF` 122-bit emergency alert ellipse. | [![Open In Colab](https://colab.research.google.com/assets/colab-badge.svg)](https://colab.research.google.com/github/jreyes13/TRISAT/blob/main/notebooks/02_euspa_osnma_has_complete_suite.ipynb) |

---

## 🛰️ Key Features

* 🌌 **Anti-Flicker Open Sky Polar Skyplot:** Interactive 360° skyplot with constellation color coding (Galileo Cyan, GPS Amber, GLONASS Red, BeiDou Green) and a persistent 12-second satellite cache that **eliminates flickering**.
* 📊 **Satellite C/N0 Signal Strength Bar Chart (Diagrama de Barras):** Real-time vertical SNR bar chart displaying carrier-to-noise ratio in dB-Hz color-coded by signal quality (Green $\ge 35$ dB-Hz, Yellow $\ge 25$ dB-Hz, Red $< 25$ dB-Hz) with constellation PRN badges (`E02`, `G08`, `R05`, `B12`).
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
Open TRISAT from Xcode (`trisat_mac`) or Terminal:
```bash
open /Users/jreyes13/Library/Developer/Xcode/DerivedData/TRISAT-hfmnxvykdnelswdxkwyavntnmwht/Build/Products/Debug/TRISAT.app
```
* **Signal Processing:** View live full-width green NMEA stream, Galileo I/NAV cards, and the C/N0 SNR Bar Chart.
* **Dashboard:** View real-time coordinates, altitude, fix quality (*GPS Fix*, *DGPS Fix*, *RTK Fixed*), and anti-flicker Skyplot.

---

## 📖 Documentation

* For complete usage instructions, technical architecture, and OSNMA testing workflows, please refer to [USER_MANUAL.md](USER_MANUAL.md).

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
