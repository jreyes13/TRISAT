using System.Globalization;
using Trisat.Core.Models;
using Trisat.Core.Services;

namespace Trisat.Core.Engines;

/// <summary>
/// Evaluates real-time RF jamming, spoofing/meaconing risk, independent GST clock drift,
/// DGPS integrity, and Galileo OSNMA live cryptographic / Bitwise verification status,
/// with optional diagnostic threat simulation drills clearly labeled as simulated.
/// </summary>
public sealed class ThreatAnalyzer
{
    private readonly OsnmaEngine _osnmaEngine = new();

    public ThreatAssessment Evaluate(
        IReadOnlyList<SatelliteInfo> satellites,
        IReadOnlyList<GalileoINavMessage> inavMessages,
        string gstRmsError,
        string dgpsStationId,
        string dgpsAge,
        ThreatSimulationFlags simulation)
        => Evaluate(satellites, inavMessages, gstRmsError, dgpsStationId, dgpsAge, simulation, null, null, null);

    public ThreatAssessment Evaluate(
        IReadOnlyList<SatelliteInfo> satellites,
        IReadOnlyList<GalileoINavMessage> inavMessages,
        string gstRmsError,
        string dgpsStationId,
        string dgpsAge,
        ThreatSimulationFlags simulation,
        OsnmaLiveSummary? liveOsnma,
        GstClockStatus? gstClock,
        BitwiseSessionStats? bitwiseStats)
    {
        int avgCno = satellites.Count == 0
            ? 0
            : (int)Math.Round(satellites.Average(s => s.Cno));

        if (simulation.Jamming && avgCno > 0)
            avgCno = Math.Max(12, avgCno - 22);

        double baseScore;
        if (satellites.Count == 0)
            baseScore = 35.0;
        else if (avgCno < 20)
            baseScore = 85.0;
        else if (avgCno < 30)
            baseScore = 45.0;
        else
            baseScore = 5.0;

        bool realOsnmaTamper = liveOsnma?.Status == OsnmaReceptionStatus.TamperDetected
                               || bitwiseStats?.HasMismatch == true;
        bool realTimeSpoof = gstClock?.IsTimeSpoofingSuspected == true;

        if (realOsnmaTamper) baseScore += 40.0;
        if (realTimeSpoof) baseScore += 35.0;

        if (simulation.Jamming) baseScore = Math.Max(baseScore, 80.0);
        if (simulation.Spoofing) baseScore += 30.0;
        if (simulation.Meaconing) baseScore += 20.0;
        if (simulation.MacTamper) baseScore += 35.0;

        int score = (int)Math.Clamp(Math.Round(baseScore), 0, 100);
        string levelText;
        string severity;
        if (score > 70)
        {
            levelText = simulation.AnyActive && !realOsnmaTamper && !realTimeSpoof
                ? "CRITICAL THREAT (SIM)"
                : "CRITICAL THREAT";
            severity = "Critical";
        }
        else if (score > 30)
        {
            levelText = simulation.AnyActive && !realOsnmaTamper && !realTimeSpoof
                ? "MODERATE RISK (SIM)"
                : "MODERATE RISK";
            severity = "Warning";
        }
        else
        {
            levelText = "NOMINAL / SAFE";
            severity = "Nominal";
        }

        // Card 1: OSNMA Live Provenance & Verification
        var osnmaStatus = liveOsnma?.Status ?? _osnmaEngine.GetReceptionStatus(inavMessages);
        if (bitwiseStats?.HasMismatch == true)
            osnmaStatus = OsnmaReceptionStatus.TamperDetected;

        string osnmaStatusText;
        string osnmaDetails;
        if (simulation.MacTamper)
        {
            osnmaStatusText = "SIMULATED MAC TAMPER";
            osnmaDetails = "Simulated drill active: OSNMA HMAC-SHA-256 tag mismatch injected for diagnostic testing.";
        }
        else if (osnmaStatus == OsnmaReceptionStatus.TamperDetected)
        {
            osnmaStatusText = bitwiseStats?.HasMismatch == true
                ? "BITWISE MISMATCH ALERT"
                : "OSNMA TAMPER DETECTED";
            osnmaDetails = bitwiseStats?.LastResult is { Matched: false, HasReference: true } mismatch
                ? mismatch.Reason
                : (liveOsnma?.DetailText ?? "Cryptographic OSNMA tag or TESLA key verification failed.");
        }
        else if (osnmaStatus == OsnmaReceptionStatus.Authenticated && liveOsnma is not null)
        {
            osnmaStatusText = liveOsnma.IsAssistedBootstrap
                ? "AUTHENTICATED (ASSISTED)"
                : "AUTHENTICATED (LIVE)";
            var nmaTag = liveOsnma.NmaHeader is not null ? $" | NMA: {liveOsnma.NmaHeader.Summary}" : string.Empty;
            osnmaDetails = liveOsnma.DetailText + nmaTag;
        }
        else if (osnmaStatus == OsnmaReceptionStatus.BitwiseVerified && liveOsnma is not null)
        {
            osnmaStatusText = "BITWISE VERIFIED (2s)";
            osnmaDetails = liveOsnma.DetailText;
        }
        else if (osnmaStatus == OsnmaReceptionStatus.NotAvailable)
        {
            osnmaStatusText = "NO I/NAV DATA";
            osnmaDetails = "Awaiting Galileo I/NAV telemetry from USB serial, BLE, or UDP bridge.";
        }
        else
        {
            osnmaStatusText = "I/NAV RECEIVED · UNVERIFIED";
            osnmaDetails = liveOsnma is not null
                ? liveOsnma.DetailText
                : $"{inavMessages.Count} navigation frames captured. Load EUSPA XML keys or connect Assisted OSNMA for live verification.";
        }

        // Card 2: Jamming & RF Interference
        bool isJamming = simulation.Jamming || (satellites.Count > 0 && avgCno < 25);
        string jammingStatus = simulation.Jamming
            ? "JAMMING (SIMULATED)"
            : (isJamming ? "JAMMING RISK" : "NOMINAL");
        string jammingDetails =
            $"Average C/N0 SNR: {avgCno} dB-Hz across {satellites.Count} visible satellites. " +
            (isJamming
                ? "Elevated noise floor / depressed carrier-to-noise ratio detected."
                : "Signal-to-noise levels within nominal operational margins.");

        // Card 3: Spoofing / Meaconing (now backed by real GST Time-Sync + Bitwise Cross-Check!)
        bool isSpoofing = simulation.Spoofing || simulation.Meaconing || realTimeSpoof || realOsnmaTamper;
        string spoofingStatus;
        string spoofingDetails;
        if (realOsnmaTamper)
        {
            spoofingStatus = "SPOOFING DETECTED (OSNMA)";
            spoofingDetails = $"40-bit OSNMA mismatch or cryptographic failure detected. Pseudorange Residuals ($GPGST): {gstRmsError}.";
        }
        else if (realTimeSpoof && gstClock?.DriftSeconds is double drift)
        {
            spoofingStatus = "TIME-SPOOFING / MEACONING";
            spoofingDetails = string.Create(
                CultureInfo.InvariantCulture,
                $"Receiver GST deviates by {drift:+0.0;-0.0;0.0}s from independent {gstClock.ClockSource} reference (threshold ±{GstClockService.StrictSpoofingDriftThresholdSeconds:F0}s). TESLA guard active.");
        }
        else if (simulation.Spoofing)
        {
            spoofingStatus = "SPOOFING (SIMULATED)";
            spoofingDetails = $"Pseudorange Residuals ($GPGST): {gstRmsError}. Simulated coherent signal takeover anomaly active.";
        }
        else if (simulation.Meaconing)
        {
            spoofingStatus = "MEACONING (SIMULATED)";
            spoofingDetails = $"Pseudorange Residuals ($GPGST): {gstRmsError}. Simulated replay delay anomaly active.";
        }
        else
        {
            spoofingStatus = "LOW RISK";
            string gstInfo = gstClock is not null
                ? $" | {gstClock.Summary}"
                : string.Empty;
            string bwInfo = bitwiseStats is { TotalChecked: > 0 }
                ? $" | Bitwise OK: {bitwiseStats.MatchedCount}/{bitwiseStats.TotalChecked}"
                : string.Empty;
            spoofingDetails = $"Pseudorange Residuals ($GPGST): {gstRmsError}{gstInfo}{bwInfo}.";
        }

        // Card 4: Differential DGPS / RTK Integrity
        bool isDgps = !string.IsNullOrWhiteSpace(dgpsStationId) && dgpsStationId != "N/A";
        string dgpsStatus = isDgps ? "CORRECTIONS ACTIVE" : "AUTONOMOUS";
        string dgpsDetails = $"DGPS Station ID: {dgpsStationId} | Correction Age: {dgpsAge}.";

        return new ThreatAssessment(
            ThreatScore: score,
            ThreatLevelText: levelText,
            ThreatSeverity: severity,
            OsnmaStatus: osnmaStatus,
            OsnmaStatusText: osnmaStatusText,
            OsnmaDetails: osnmaDetails,
            JammingStatusText: jammingStatus,
            IsJammingRisk: isJamming,
            JammingDetails: jammingDetails,
            SpoofingStatusText: spoofingStatus,
            IsSpoofingRisk: isSpoofing,
            SpoofingDetails: spoofingDetails,
            DgpsStatusText: dgpsStatus,
            IsDgpsActive: isDgps,
            DgpsDetails: dgpsDetails,
            HasActiveSimulation: simulation.AnyActive);
    }
}
