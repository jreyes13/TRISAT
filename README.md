# TRISAT - Authenticated Earth Intelligence

> **TRISAT** (**T**ri-band **R**eal-time **I**ntegrity & **S**atellite **A**uthentication **T**erminal) — representing **Tri-Band** Galileo reception (**E1 / E5 / E6**), **Tri-Service** GNSS verification (**OSNMA** cryptographic authentication, **HAS** high-accuracy PPP, and **EWSS/TSM** integrity), and **Tri-Platform** native execution (**Android, iOS & macOS + SDR**).

TRISAT is an advanced **multiplatform GNSS intelligence application** (Android, iOS, macOS & Software-Defined Radio) designed for **Galileo OSNMA (Open Service Navigation Message Authentication)**, **Galileo E6 HAS (High Accuracy Service)** precise point positioning, multi-constellation GNSS signal analysis, and anti-spoofing diagnostic evaluation.

![TRISAT Banner](trisat_android/src/main/res/drawable/trisat_logo.png)

## Data provenance

Live Galileo E1-B I/NAV pages are reported as unverified until validated cryptographically (using official EUSPA public Merkle Tree / Public Key XML files or **Assisted OSNMA** bootstrap material) or cross-checked bit-by-bit against a trusted **Assisted OSNMA** reference station. The SDR navigation and positioning output is simulated, including when an IQ file is used as an input. The OSNMA Annex A self-test checks a fixed official reference vector and is shown separately from live session status.

## 🌐 Multiplatform Architecture

TRISAT is built as a cross-ecosystem solution with native, high-performance implementations for Linux, Windows, iOS, macOS, Android, and embedded telemetry bridges:

| Platform / Module | Directory | Tech Stack | Key Capabilities |
| :--- | :--- | :--- | :--- |
| **iOS / iPadOS** | `trisat_ios/` | Swift, SwiftUI, CoreLocation, CoreBluetooth, MapKit, CryptoKit | 3-mode operation (Internal TLE, BLE Nordic UART, UDP Simulator), Galileo I/NAV bit-exact CRC-24Q/OSNMA parser, **Assisted OSNMA** client (GST sync & 2s bitwise check), CelesTrak 24h orbit propagator |
| **macOS** | `trisat_mac/` & `trisat_ios/` | Swift, SwiftUI, Network.framework, POSIX Serial, CryptoKit, PyUSB | Native macOS desktop app, **Assisted OSNMA** client (GST sync & 2s bitwise anti-spoofing), integrated **TRISAT-SDR (E1 OSNMA + E6 HAS-UA)** launcher, POSIX direct USB serial for u-blox & SXblue |
| **Linux (Ubuntu)** | `trisat_linux/` | C# (.NET 8), Avalonia UI 11 (X11/Wayland), BlueZ D-Bus, `gpsd`/GeoClue2, `System.IO.Ports` | Native Linux desktop cockpit sharing `Trisat.Core`, direct `/dev/ttyACM*`/`/dev/ttyUSB*` (`sysfs` VID/PID detection for u-blox ZED-F9P `UBX-RXM-SFRBX` & SXblue), BlueZ BLE NUS/GATT, `gpsd`/GeoClue2 location, live EUSPA XML OSNMA verifier, **Assisted OSNMA** Reference Server (`:8095`) & Client, EUSPA Annex A self-test, TRISAT-SDR & SBF Replay launcher, `.desktop` installer & `udev` rules |
| **Windows** | `trisat_win/` | C# (.NET 8), WPF, WinRT BLE & Geolocation, `System.IO.Ports` | Native Windows 10/11 cockpit, direct USB `COM*` (u-blox ZED-F9P `UBX-RXM-SFRBX` + SXblue), bit-exact Galileo I/NAV (`CRC-24Q`, 40-bit OSNMA, `GST`), independent SNTP/GST clock, live EUSPA XML OSNMA verifier, **Assisted OSNMA** HTTP JSON Reference Server (`:8095`) & Client, WinRT BLE, UDP/TCP bridges, EUSPA Annex A self-test, TRISAT-SDR & SBF Replay launcher |
| **Hardware Bridges** | `trisat_hw/` | Arduino / C++, Python 3, PyUSB, libusb | SparkFun u-blox ZED-F9P + ESP32 hybrid bridge (BLE NUS + WiFi UDP), Geneq SXblue USB daemon, u-blox USB-CDC bridge |
| **Android** | `trisat_android/` | Kotlin, Jetpack Compose, Android GNSS HAL APIs | Raw `TYPE_GAL_I` I/NAV 240-bit CRC-24Q/OSNMA decoding, **Assisted OSNMA** client (GST drift monitor & 2s bitwise check), AGC levels, A-GNSS cold-start reset |
| **SDR Engine** | `trisat_sdr/` | Python 3, NumPy/SciPy, Dual-HackRF (`10 MHz CLKOUT/CLKIN`) | SDR simulator (`E1B/C`, `E5a`, `E5b`, `E5 AltBOC`, `E6B/C HAS`, `GPS L1`), **OSNMA v1.1**, **HAS-UA (`PPPSD`/`DIA`/`NTCM-G`)** & Web UI (`:8085`) |

---

## 🛰️ Key Features

* 🌌 **Anti-Flicker Open Sky Polar Skyplot:** Interactive 360° celestial skyplot with constellation color coding (Galileo Cyan, GPS Amber, GLONASS Red, BeiDou Green) and a persistent 25-second tracking cache that eliminates flickering.
* 📊 **Satellite C/N0 Signal Strength Bar Chart:** Real-time vertical SNR bar chart displaying carrier-to-noise ratio in dB-Hz color-coded by signal quality (Green $\ge 35$ dB-Hz, Yellow $\ge 25$ dB-Hz, Red $< 25$ dB-Hz) with constellation PRN badges (`E02`, `G10`, `R80`, `B28`).
* 📡 **Three Mutually Exclusive Operating Modes:**
  * **Mode 0 (`🛰️ Internal + TLE`):** Uses iPhone / Mac internal CoreLocation coordinates with live CelesTrak SGP4 24-hour Keplerian orbit propagation ($\pm 12\text{h}$ slider). Raw RF telemetry and threat analysis views enter a clean Standby state.
  * **Mode 1 (`📡 Bluetooth`):** Direct wireless connection to the physical **TRISAT ESP32 Hybrid Bridge** via Nordic UART Service (NUS). Streams real-time NMEA sentences (`$GNGGA`, `$GAGSV`, `$GPGSV`) and paced Galileo I/NAV subframes (`$GALINAV`) without requiring Apple MFi licensing.
  * **Mode 2 (`🌐 UDP Telemetry / Simulator`):** In-process Swift telemetry generator and high-performance UDP receiver (`Network.framework` port 9090) for HackRF SDR streams or standalone testing.
* 🔒 **Galileo I/NAV & Live OSNMA Subframe Decoder:** Bit-exact Galileo OS SIS ICD v2.1 decoding (`WT 0..6, 16, 17..20, 22, 44`), `CRC-24Q` validation (`0x1864CFB`), 40-bit OSNMA (`8b HKROOT` + `32b MACK`) extraction, and 30-second subframe reassembly with EUSPA public XML (`MerkleTree.xml` / `PublicKey.xml`) cryptographic verification.
* 🌐 **Assisted OSNMA & 2-Second Bitwise Anti-Spoofing:** Built-in, open-source HTTP JSON Reference Server (`:8095` in `trisat_win`) and native clients across Windows, macOS, iOS, and Android providing independent network-to-GST clock synchronization (anti-time-spoofing guard), fast cold-start cryptographic bootstrap (`DSM-KROOT` / `Merkle Root` / `Public Key`), and 2-second 40-bit OSNMA bitwise cross-checking for immediate spoofing and meaconing detection.
* 🛡️ **Interactive Threat Analysis & Anti-Spoofing:** Dynamic RF health scoring (0–100%) combining real OSNMA cryptographic/bitwise alerts and GST time-drift monitoring with interactive attack simulation drills for broadband Jamming, coherent Spoofing, Meaconing (replay delay), and OSNMA MAC tampering.
* 🗺️ **Embedded Geospatial Mini Map:** Live interactive MapKit view displaying real-time coordinate position marked `GNSS LIVE`.

---

## ⚡ Hardware Bridges & Receivers (`trisat_hw`)

TRISAT supports both professional sub-meter receivers and multi-frequency RTK sensor pods:

### 1️⃣ SparkFun u-blox ZED-F9P + ESP32 DevKit (Hybrid BLE & WiFi)
Located in [`trisat_hw/esp32/esp32_hybrid_bridge/`](trisat_hw/esp32/esp32_hybrid_bridge/esp32_hybrid_bridge.ino):
* **Wiring:** u-blox TX $\to$ ESP32 RX2 (GPIO 19), u-blox RX $\leftarrow$ ESP32 TX2 (GPIO 18) at 115200 bps.
* **BLE Stream:** Advertises as `TRISAT-GNSS-BLE` using Nordic UART Service (`6E400001-B5A3-F393-E0A9-E50E24DCCA9E`). Paces Galileo I/NAV subframes to 1 frame per 1200 ms to guarantee zero BLE packet drops on iOS.
* **WiFi UDP Broadcast:** Creates access point `TRISAT-GNSS` (password: `trisat4ever`) and broadcasts all NMEA sentences to UDP port `9090`.
* **Flashing:** Open sketch in Arduino IDE, select board **ESP32 Dev Module**, port `/dev/cu.usbserial-*`, and upload.

### 2️⃣ u-blox ZED-F9P Native USB Bridge
Located in [`trisat_hw/zed_f9p/`](trisat_hw/zed_f9p/):
* Direct USB-C connection to Mac `/dev/cu.usbmodem*`.
* Run daemon: `python3 trisat_hw/zed_f9p/f9p_bridge.py` to bridge NMEA and `UBX-RXM-SFRBX` binary packets to UDP `127.0.0.1:9090`.

### 3️⃣ Geneq SXblue USB FTDI Bridge
Located in [`trisat_hw/sxblue/`](trisat_hw/sxblue/):
* Interfaces with Geneq SXblue FTDI USB chip (`VID: 0x0403, PID: 0xb5a0`) at 19200 bps.
* One-time EEPROM configuration: `sudo python3 trisat_hw/sxblue/sxblue_config.py`.
* Continuous reader daemon: `sudo ./trisat_hw/sxblue/run_bridge.sh`.

---

## 📖 Documentation & Interactive Colab Notebooks

* **User Manual:** For complete usage instructions, technical architecture, and OSNMA/HAS testing workflows, please refer to [docs/USER_MANUAL.md](docs/USER_MANUAL.md).
* **Web Server & SDR Dashboard Guide:** For instructions on running the real-time SDR web cockpit (`http://127.0.0.1:8085`) and REST API, please refer to [docs/WEB_SERVER_GUIDE.md](docs/WEB_SERVER_GUIDE.md).
* **Interactive Google Colab Notebooks:** For executable demonstrations of Galileo E1-B I/NAV v2.2 decoding, OSNMA Annex A cryptographic verification, and E6 HAS-UA precise point positioning, see [notebooks/README.md](notebooks/README.md).
* **TRISAT-SDR Engine Documentation:** For Dual-HackRF SDR configuration, front-end clock sync, and GNSS baseband engines, see [trisat_sdr/README.md](trisat_sdr/README.md).

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

### Windows App (`trisat_win`)
```powershell
dotnet test trisat_win\TrisatWin.sln
dotnet run --project trisat_win\Trisat.Win\Trisat.Win.csproj
```

### Linux App (`trisat_linux`)
```bash
dotnet test trisat_linux/TrisatLinux.sln
dotnet run --project trisat_linux/Trisat.Linux/Trisat.Linux.csproj
# Optional: build self-contained binary + Ubuntu .desktop launcher
./trisat_linux/scripts/install-desktop.sh
```
