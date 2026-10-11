# Project Plan

TRISAT - an Android application focused on testing GNSS (Global Navigation Satellite System) trustability options, authenticated location/earth intelligence, satellite signal verification, anti-spoofing/anti-jamming analysis, raw GNSS measurements monitoring, and location trust scoring. Standard branding based on provided TRISAT logos with blue/teal/light green color palette.

## Project Brief

# TRISAT - Project Brief (MVP)

## Features
1. **GNSS Raw Measurement & Signal Monitor**: Real-time tracking of satellite constellation telemetry (GPS, Galileo, GLONASS, BeiDou), including Carrier-to-Noise ratio ($C/N_0$), carrier frequencies, satellite positions, and raw clock measurement streams.
2. **Anti-Spoofing & Anti-Jamming Analysis Engine**: Detection of location spoofing and RF jamming threats by evaluating signal power anomalies, AGC (Automatic Gain Control) fluctuations, clock drift, and satellite geometry (DOP).
3. **Location Trust Scoring System**: Computation of an dynamic, real-time Location Trust Score (0–100%) based on multi-constellation verification, signal authenticity checks, and location sanity metrics.
4. **Trustability Dashboard & Visualizations**: Interactive UI presenting real-time trust status badges, active satellite health details, threat warnings, and verified earth intelligence telemetry using TRISAT's blue/teal/light green brand theme.

---

## High-Level Tech Stack
- **Language**: Kotlin
- **UI Framework**: Jetpack Compose with Material 3 (customized with TRISAT blue/teal/light green color scheme)
- **Navigation**: Jetpack Navigation 3 (state-driven navigation)
- **Adaptive Strategy**: Compose Material Adaptive library (`ListDetailPaneScaffold` and adaptive navigation layouts for phones, foldables, and tablets)
- **Concurrency & Event Streams**: Kotlin Coroutines & `StateFlow` / `SharedFlow` for non-blocking real-time streaming of raw GNSS measurement callbacks
- **Core Platform APIs**: Android GNSS & Location APIs (`LocationManager`, `GnssStatus.Callback`, `GnssMeasurementsEvent.Callback`)

## Implementation Steps
**Total Duration:** 28h 17m 22s

### Task_1_SetupThemeAndNavigation: Configure TRISAT branding theme with custom blue/teal/light green Material 3 color scheme, set up state-driven navigation, and build adaptive layout container.
- **Status:** COMPLETED
- **Updates:** TRISAT Material 3 theme configured with deep blue/teal/emerald green brand colors. Adaptive navigation implemented supporting navigation rail (wide screens) and bottom navigation bar (mobile). Main destinations (Dashboard, Signal Monitor, Threat Analysis, Settings) wired in Jetpack Compose. Build verified successfully.
- **Acceptance Criteria:**
  - App builds successfully with TRISAT Material 3 color palette
  - Navigation framework between Dashboard, Signal Monitor, Threat Analysis, and Settings is operational
- **Duration:** 14m 57s

### Task_2_GnssMonitorEngine: Implement Android Location and GNSS APIs (LocationManager, GnssStatus.Callback, GnssMeasurementsEvent.Callback) streaming telemetry, carrier-to-noise ratio (C/N0), frequencies, satellite geometry, and clock data via Kotlin Coroutines StateFlow.
- **Status:** COMPLETED
- **Updates:** `LocationGnssManager`, `GnssViewModel`, `MockGnssGenerator`, data models, and permission handling implemented and verified. Unit tests in `GnssEngineTest.kt` passing (5/5). Build verified cleanly with `./gradlew testDebugUnitTest assembleDebug`.
- **Acceptance Criteria:**
  - GNSS status and raw measurement listeners integrated with Flow streams
  - Satellite constellation telemetry, C/N0, and clock data tracked in real-time
  - Runtime permissions for Location/Fine Location handled gracefully
- **Duration:** 27h 49m 58s

### Task_3_AntiSpoofingAndTrustScoring: Develop detection logic for RF jamming and location spoofing (evaluating power anomalies, AGC fluctuations, clock drift, DOP geometry) and compute real-time Location Trust Score (0-100%).
- **Status:** COMPLETED
- **Updates:** `TrustScoringEngine` implemented with detection logic for C/N0 spoofing anomalies, AGC jamming suppression, clock drift anomalies, constellation diversity, DOP calculations, and OSNMA verification. Dynamic Location Trust Score (0-100%) and structured `TrustReport` created. Unit tests in `TrustScoringEngineTest.kt` passing (12/12). Build verified with `./gradlew testDebugUnitTest assembleDebug`.
- **Acceptance Criteria:**
  - Anti-spoofing and anti-jamming threat detection engine operational
  - Dynamic Location Trust Score (0-100%) calculated from multi-constellation and signal metrics
- **Duration:** 7m 22s

### Task_4_DashboardAndUIScreens: Build Jetpack Compose UI screens (Trustability Dashboard, GNSS Raw Signal Monitor, Threat Visualizations) with real-time status badges, threat warnings, and adaptive layouts (ListDetailPaneScaffold).
- **Status:** COMPLETED
- **Updates:** Jetpack Compose UI screens (Dashboard with gauge meter and simulation scenario switcher, Signal Monitor with multi-band C/N0 charts, Threat Analysis with live threat logs, DOP cards, OSNMA state, and Settings screen) built with TRISAT branding. Adaptive layout support verified. Build and unit tests verified cleanly (`./gradlew testDebugUnitTest assembleDebug`).
- **Acceptance Criteria:**
  - Interactive UI presenting trust score badges, active satellite health details, and threat alerts
  - Adaptive layouts functioning seamlessly across phones, foldables, and tablets
- **Duration:** 5m 5s

### Task_5_RunAndVerify: Run and verify the complete TRISAT app build and functionality. Instruct critic_agent to verify application stability (no crashes), confirm alignment with user requirements, and report critical UI issues.
- **Status:** IN_PROGRESS
- **Acceptance Criteria:**
  - build pass
  - app does not crash
  - make sure all existing tests pass
  - All TRISAT project brief requirements and UI stability verified
- **StartTime:** 2026-10-11 18:10:45 CEST

