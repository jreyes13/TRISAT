# TRISAT Windows Native Desktop Cockpit (`trisat_win`)

Native **Windows 10 / 11** desktop application for **TRISAT** built with **.NET 8, WPF, and Windows Runtime (WinRT)** — providing full native access to Windows hardware (`COM*` serial ports, WinRT Bluetooth Low Energy, Windows Location API, UDP/TCP telemetry sockets) and integrated launchers for **TRISAT-SDR** and **Septentrio SBF Replay**.

---

## Architecture

```text
trisat_win/
├── TrisatWin.sln              # Visual Studio 2022+ / dotnet CLI solution
├── Trisat.Core/               # .NET 8 core library (NMEA, UBX-RXM-SFRBX, JSON, TLE/SGP4, DOP, OSNMA Annex A, UDP/TCP)
├── Trisat.Win/                # .NET 8 WPF + WinRT Windows 10/11 desktop app (COM, BLE, Geolocation, SkyPlot, C/N0, MiniMap)
└── Trisat.Core.Tests/         # xUnit automated test suite
```

## Capabilities

* **Direct USB Serial (`COM*`)**:
  * **u-blox ZED-F9P**: Native mixed ASCII NMEA + binary `UBX-RXM-SFRBX` framing and Galileo I/NAV extraction right inside the app (no Python bridge required), plus one-click **Configure ZED-F9P** and **5-step Factory Reset**, and optional LAN UDP `:9090` relay for iOS/Android devices.
  * **Geneq SXblue (FTDI VCP)**: Connects at `19200 bps` with one-click `$JASC`/`$JBIN,94`/`$JBIN,95`/`$JSAVE` wake-up commands.
  * **ESP32 USB Monitor**: Direct `115200 bps` serial telemetry streaming.
* **Native Windows Bluetooth LE (WinRT)**:
  * Connects wirelessly to the **ESP32 Hybrid Bridge (`TRISAT-GNSS-BLE`)** via Nordic UART Service (`6E400001-B5A3-F393-E0A9-E50E24DCCA9E`) and to the **TRISAT BLE GATT Service** (`00001835-...`).
* **Network Telemetry Bridges (UDP & TCP)**:
  * Built-in async UDP listener on `:9090` (compatible with ESP32 Wi-Fi broadcast, `f9p_bridge.py`, `sxblue_reader.py`, and `trisat_sdr`) and TCP client on `127.0.0.1:5006`.
* **Windows Location API + CelesTrak TLE SGP4**:
  * Mode 0 (`Windows Location + TLE`), Mode 1 (`External Hardware`), and Mode 2 (`TLE Orbit Simulation` with ±12 h scrubbing and reference sites including Fuentecén).
* **Bit-Exact Galileo I/NAV Parser & Independent GST Clock**:
  * Decodes u-blox ZED-F9P 228-bit `UBX-RXM-SFRBX` and full 240-bit nominal Galileo E1-B I/NAV pages per Galileo OS SIS ICD v2.1: validates `CRC-24Q` (`0x1864CFB`), extracts the 40-bit OSNMA field (`8b HKROOT` + `32b MACK`), `SAR`, `SSP`, and decodes `GST` (`WN`/`TOW`) from `WT 0`, `WT 5`, and `WT 6`.
  * Built-in `GstClockService` performs independent network time synchronization via SNTP (RFC 4330 UDP `:123`) with HTTPS `Date` header fallback, flagging **Time-Spoofing / Meaconing** when `|Δt(RX - Net)| > 5 s` and enforcing the 30-second OSNMA loose time-synchronization window before accepting TESLA keys.
* **Live OSNMA Verifier & EUSPA Annex A Cryptographic Self-Test**:
  * Loads official public EUSPA GSC XML files (`MerkleTree.xml` and `PublicKey.xml`) and reassembles 30-second OSNMA subframes (`120b HKROOT` + `480b MACK`) to verify `DSM-KROOT` (ECDSA P-256 + SHA-256 `P_DK` padding), TESLA key chains, and HMAC-SHA-256 tags in real time.
  * Preserves strict data provenance (`NO I/NAV DATA`, `UNVERIFIED`, `BITWISE VERIFIED (2s)`, `AUTHENTICATED (LIVE/ASSISTED)`, `OSNMA TAMPER / BITWISE MISMATCH`).
  * Includes a standalone 6-stage **EUSPA Annex A** cryptographic self-test (SHA-256 `P_DP`/`P_DK`, ECDSA P-256 `DSM-KROOT`, TESLA `K14→K0` chain, `MACSEQ 0xB08`, `ADKD=4 Tag2`).
* **Assisted OSNMA (Open-Source HTTP JSON Reference Server & Client)**:
  * Zero-database, in-memory 60-second sliding-window Reference Server (`http://127.0.0.1:8095` by default) allowing a Windows PC with a u-blox ZED-F9P to serve TRISAT clients on Windows, macOS, iOS, and Android:
    * `GET /api/osnma/status` — service health, active GST, NMA header, cached epochs
    * `GET /api/osnma/gst` — independent reference Galileo System Time (GST)
    * `GET /api/osnma/assist` — fast cold-start bootstrap bundle (Merkle Root, Public Key, verified `DSM-KROOT`)
    * `GET /api/osnma/reference[?gst={totalSeconds}]` — 40-bit OSNMA snapshot across Galileo `E01–E36`
    * `GET /api/osnma/satellites` — per-satellite OSNMA availability and authentication status
    * `POST /api/osnma/bitwise-check` — 2-second 40-bit OSNMA bitwise anti-spoofing cross-check
    * `POST /api/osnma/ingest` — reference page ingestion for external hardware bridges
* **Integrated TRISAT-SDR & Septentrio SBF Replay Panel**:
  * Launch and monitor `trisat_sdr/main.py` (Dual-HackRF E1/E6/E5 bands + `:8085` Web Cockpit) and `trisat_sdr.replay_sbf` (with optional `MerkleTree.xml`, `PublicKey.xml`, and `OSNMAlib` path) directly from the **SDR & SBF Tools** tab.

---

## Prerequisites (Windows 10/11)

1. Install **.NET 8 SDK** (or Visual Studio 2022 with the *.NET desktop development* workload):
   ```powershell
   winget install Microsoft.DotNet.SDK.8
   ```
2. *(Optional, for SDR & SBF Replay tab)* Python 3.12+ on `PATH`.

---

## Build, Run & Package on Windows

```powershell
# Run automated core tests
dotnet test trisat_win\TrisatWin.sln

# Run the native Windows app directly
dotnet run --project trisat_win\Trisat.Win\Trisat.Win.csproj

# Publish a standalone Windows x64 executable (.exe)
dotnet publish trisat_win\Trisat.Win\Trisat.Win.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o trisat_win\publish
```

You can also open [`TrisatWin.sln`](TrisatWin.sln) directly in **Visual Studio 2022** and press **F5**.
