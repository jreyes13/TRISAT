# TRISAT Linux Native Desktop Cockpit (`trisat_linux`)

Native **Ubuntu / Linux** desktop application for **TRISAT** built with **.NET 8, Avalonia UI 11 (X11 / Wayland), Linux TTY Serial, BlueZ D-Bus, and `gpsd` / GeoClue2** — sharing the bit-exact GNSS/OSNMA/Assisted-OSNMA core (`Trisat.Core`) with `trisat_win` and providing full native access to Linux hardware and integrated launchers for **TRISAT-SDR** and **Septentrio SBF Replay**.

---

## Architecture

```text
trisat_linux/
├── TrisatLinux.sln                  # .NET 8 solution (Trisat.Core + Trisat.Linux + Tests)
├── Trisat.Linux/                    # .NET 8 Avalonia 11 native Linux cockpit
│   ├── Controls/                    # Polar SkyPlot, C/N0 Bar Chart, OSM MiniMap, AdaptiveScrollViewer
│   ├── Services/                    # LinuxSerialService (/dev/tty*), LinuxBleService (BlueZ D-Bus), LinuxLocationService (gpsd + GeoClue2)
│   ├── Themes/                      # DarkAerospaceTheme.axaml
│   └── ViewModels/                  # 5-tab MainViewModel
├── Trisat.Linux.Tests/              # xUnit tests for Linux hardware services & ViewModel
└── scripts/
    ├── install-desktop.sh           # Publishes self-contained Linux binary + Ubuntu .desktop launcher
    └── 99-trisat-gnss.rules         # udev rules for ZED-F9P, SXblue, ESP32 & HackRF
```

## Capabilities

* **Direct Linux USB Serial (`/dev/ttyACM*`, `/dev/ttyUSB*`, `/dev/rfcomm*`)**:
  * Automatic hardware detection via Linux `sysfs` (`idVendor`/`idProduct`/`manufacturer`/`product`) and `/dev/serial/by-id/*`.
  * **u-blox ZED-F9P** (`VID 1546`): Mixed ASCII NMEA + binary `UBX-RXM-SFRBX` Galileo I/NAV framing inside the app, one-click **Configure ZED-F9P**, **5-step Factory Reset**, and optional LAN UDP `:9090` relay.
  * **Geneq SXblue (FTDI `0403:b5a0`)**: Auto-selects `19200 bps` with one-click `$JASC`/`$JBIN,94`/`$JBIN,95`/`$JSAVE` wake-up.
  * **ESP32 UART (CP210x / CH340 / Espressif)**: Direct `115200 bps` streaming.
* **Native Linux Bluetooth LE (BlueZ D-Bus `org.bluez`)**:
  * Scans and connects wirelessly via system D-Bus to the **ESP32 Hybrid Bridge (`TRISAT-GNSS-BLE`)** over Nordic UART Service (`6E400001-B5A3-F393-E0A9-E50E24DCCA9E`) and **TRISAT BLE GATT** (`00001835-...`).
* **Native Linux Location (`gpsd` + GeoClue2) & CelesTrak TLE SGP4**:
  * Mode 0 (`Linux Location + TLE`) connects automatically to local **`gpsd` (`127.0.0.1:2947`)** or falls back to Ubuntu's **GeoClue2** D-Bus location service.
  * Mode 1 (`External Hardware`) and Mode 2 (`TLE Orbit Simulation` ±12 h scrubbing).
* **Bit-Exact Galileo I/NAV, Live OSNMA, Independent SNTP/GST Clock & Assisted OSNMA (`:8095`)**:
  * Shares `Trisat.Core` (`CRC-24Q`, 40-bit OSNMA, EUSPA `MerkleTree.xml`/`PublicKey.xml` verifier, 6-stage **EUSPA Annex A** self-test, SNTP/HTTPS anti-time-spoofing clock, and zero-database Assisted OSNMA HTTP JSON Reference Server & Client).
* **Integrated TRISAT-SDR & Septentrio SBF Replay Panel**:
  * Auto-detects `python3` (or repo `.venv/bin/python3`) to launch and monitor `trisat_sdr/main.py` (`:8085`) and `trisat_sdr.replay_sbf` (`:8090`).

---

## Prerequisites on Ubuntu (22.04 / 24.04 / 26.04)

1. Install **.NET SDK** (`.NET 10` on Ubuntu 26.04+ or `.NET 8` on Ubuntu 22.04/24.04 — `trisat_linux` supports both automatically):
   ```bash
   sudo apt update
   # Ubuntu 26.04 (Resolute) and newer:
   sudo apt install -y dotnet-sdk-10.0

   # Ubuntu 22.04 / 24.04:
   # sudo apt install -y dotnet-sdk-8.0
   ```
2. *(Recommended for USB GNSS receivers without `sudo`)* Add your user to `dialout` and install the TRISAT udev rules:
   ```bash
   sudo usermod -aG dialout "$USER"
   sudo cp trisat_linux/scripts/99-trisat-gnss.rules /etc/udev/rules.d/
   sudo udevadm control --reload-rules && sudo udevadm trigger
   ```
   *(Log out and back in once for the `dialout` group membership to take effect.)*

---

## Build, Test, Run & Install on Ubuntu

```bash
# Run all automated tests (Trisat.Core.Tests + Trisat.Linux.Tests)
dotnet test trisat_linux/TrisatLinux.sln

# Run the native Linux app directly
dotnet run --project trisat_linux/Trisat.Linux/Trisat.Linux.csproj

# Or publish & install as a native Ubuntu desktop app (~/.local/bin/trisat + Application Menu icon)
chmod +x trisat_linux/scripts/install-desktop.sh
./trisat_linux/scripts/install-desktop.sh
```
