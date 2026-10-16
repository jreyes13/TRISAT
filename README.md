# TRISAT - Authenticated Earth Intelligence

TRISAT is an advanced native Android application designed for **Galileo OSNMA (Open Service Navigation Message Authentication) testing**, multi-constellation GNSS signal analysis, anti-spoofing diagnostic evaluation, and spatial coordinate verification.

![TRISAT Banner](app/src/main/res/drawable/trisat_logo.png)

## Key Features
* 🛰️ **Open Sky Polar Skyplot:** Interactive 360° skyplot with constellation color coding (Galileo Primary, GPS, GLONASS, BeiDou) and custom satellite shapes ($\bigcirc$ Tracking, $\square$ Used in Fix, $\bigodot/\boxdot$ Galileo I/NAV Active).
* 🔒 **Galileo I/NAV & OSNMA Receiver Test:** Direct extraction and inspection of raw `TYPE_GAL_I` navigation frames.
* 📍 **Galileo-Only Authenticated Position:** Calculates position convergence time and 3D fix accuracy exclusively from authenticated Galileo satellites.
* 📊 **Multi-Constellation Solution Matrix:** Side-by-side comparison of position, 2D/1D error, and sat count for Galileo OS-NMA, Galileo-Only, GPS-Only, GLONASS-Only, and BeiDou-Only.
* 📈 **Position History & Error Statistics:** RMS 2D horizontal, 1D vertical, and 3D position error metrics across logged epochs.
* 🔄 **Cold-Start A-GNSS Purge:** Issues native Android HAL commands to flush cached A-GPS/A-GNSS ephemeris data.
* 🗺️ **Geospatial Navigator Exporter:** Copy coordinates to clipboard or launch positions directly in Google Maps.

## Documentation
For complete usage instructions, technical architecture, and OSNMA testing workflows, please refer to the [USER_MANUAL.md](USER_MANUAL.md).

## How to Build

```bash
# Assemble Debug APK
./gradlew assembleDebug
```
Output APK location: `app/build/outputs/apk/debug/app-debug.apk`
