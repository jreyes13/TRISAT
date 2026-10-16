# TRISAT - User Manual & Technical Reference Guide

Welcome to the **TRISAT (Authenticated Earth Intelligence)** application manual. This document explains the architecture, features, GNSS testing workflows, and OSNMA verification tools provided by TRISAT.

---

## 1. Application Overview

TRISAT is a native Android application designed for **Galileo OSNMA (Open Service Navigation Message Authentication) testing**, multi-constellation GNSS signal analysis, anti-spoofing diagnostic evaluation, and spatial coordinate verification.

### Core Capabilities:
* **Real-time Open Sky View (Skyplot):** Polar visualization of tracked satellites categorized by constellation (Galileo Primary, GPS, GLONASS, BeiDou) with custom symbols for tracking ($\bigcirc$), position fix ($\square$), and Galileo I/NAV reception ($\bigodot/\boxdot$).
* **Galileo I/NAV & OSNMA Receiver Test:** Direct extraction and inspection of raw Galileo `TYPE_GAL_I` navigation frames.
* **Galileo-Only Authenticated Position Solution:** Calculates position convergence time and 3D fix accuracy exclusively from authenticated Galileo satellites.
* **Multi-Constellation Solution Matrix:** Side-by-side comparison of position, 2D/1D error, and sat count for Galileo OS-NMA, Galileo-Only, GPS-Only, GLONASS-Only, and BeiDou-Only.
* **Position History & Error Statistics:** Tracks RMS horizontal (2D), vertical (1D), and 3D position errors across logged epochs.
* **Cold-Start A-GNSS Purge:** Issues native Android HAL commands to flush cached A-GPS/A-GNSS ephemeris and almanac data.
* **Geospatial Exporter:** Copy coordinates directly to system clipboard or open position fixes in Google Maps / external navigators.

---

## 2. Screen & Feature Guide

### 📱 Dashboard Tab
1. **Open Sky Satellite View (Skyplot):**
   * Displays satellites in polar coordinates (Azimuth $0^\circ - 360^\circ$, Elevation $0^\circ - 90^\circ$).
   * Interactive filter chips allow toggling individual constellations (Galileo, GPS, GLONASS, BeiDou) on or off.
2. **Position History & Error Statistics:**
   * Displays 2D RMS Horizontal Error, 1D RMS Vertical Height Error, 3D Position Error, and logged sample count.
   * `Remove A-GNSS`: Flushes A-GPS/A-GNSS cache and forces a cold-start re-acquisition.
   * `Clear History`: Resets logged position epochs and error statistics.

---

### 📶 Signal Monitor Tab
1. **GNSS Raw Signal Monitor Header:**
   * Shows active mode status (`REAL HARDWARE` vs `SIMULATION MODE`).
   * Displays total tracked satellites, average $C/N_0$ (dB-Hz), and HDOP.
2. **Galileo I/NAV & OSNMA Receiver Test:**
   * Evaluates if your phone's hardware driver exposes raw `TYPE_GAL_I` I/NAV pages.
   * Displays real-time captured I/NAV frame count and raw hexadecimal payloads (`0x1B0A4F...`).
3. **Galileo-Only Authenticated Position:**
   * Isolates Galileo satellites and displays OS-NMA convergence time (e.g. `12.4 s`) and 3D fix accuracy.
4. **Multi-Constellation Solution Matrix:**
   * Side-by-side comparative table showing position, 2D/1D error, and sat count across all 5 constellation configurations:
     * *Galileo OS-NMA Only*
     * *Galileo Only*
     * *GPS Only*
     * *GLONASS Only*
     * *BeiDou Only*
5. **Authenticated Position & Navigator:**
   * `Copy Coordinates`: Copies current latitude/longitude to Android Clipboard.
   * `Google Maps`: Opens current fix in Google Maps or external navigation apps.
6. **Tracked Satellite Channels List:**
   * Detailed breakdown per satellite: SVID, Constellation, $C/N_0$ progress bar, Elevation/Azimuth, and Carrier Frequency.

---

### 🛡️ Threat Analysis Tab
1. **Trustability Overview Gauge Meter:**
   * Real-time 0–100% Trustability Score with High/Medium/Low trust classification.
2. **Anti-Spoofing Status & Interactive Threat Scenario Switcher:**
   * Interactively test GNSS attack vectors:
     * *Nominal Open Sky*
     * *Spoofing Simulator Attack*
     * *Jamming / AGC Drop*
     * *Clock Drift Anomaly*
     * *OSNMA Key Failure*
3. **DOP Geometry & Anti-Jamming Verification:**
   * Evaluates HDOP, VDOP, and AGC gain levels.
4. **Real-Time Threat Intelligence Event Logs:**
   * Live timeline logging detected signal anomalies and security events.

---

### ⚙️ Settings & Logs Tab
1. **Raw Measurement & RINEX Data Logging:**
   * Toggle raw measurement streaming and export RINEX 3.04 Observation files.
2. **Security & Anti-Spoofing Configuration:**
   * Toggle OSNMA validation mode and adjust anti-spoofing sensitivity threshold.
3. **Emulator & Testing Controls:**
   * Toggle `Force Mock GNSS Stream` (Simulation Mode).
4. **System Information & Build Details:**
   * Displays dynamic APK Build Version (`v1.1.0`), Code (`2`), and Compilation Timestamp.

---

## 3. How to Test OSNMA on a Physical Phone

1. Install the APK on an Android device:
   ```bash
   ./gradlew assembleDebug
   ```
2. Open TRISAT outdoors under clear open sky.
3. Grant location permissions when prompted.
4. Go to **Signal Monitor** $\rightarrow$ **Galileo I/NAV & OSNMA Receiver Test**.
5. **Verification Result:**
   * **Green Badge (`I/NAV CAPABLE (OSNMA OK)`)**: Your phone's GNSS chipset and Android driver deliver raw Galileo `TYPE_GAL_I` navigation frames. The device is fully capable of Galileo OSNMA authentication!
   * **Orange Badge (`NO I/NAV DETECTED`)**: The phone's hardware driver does not expose raw I/NAV pages required for OSNMA.
