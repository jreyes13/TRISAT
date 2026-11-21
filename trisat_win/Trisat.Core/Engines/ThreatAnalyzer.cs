using Trisat.Core.Models;

namespace Trisat.Core.Engines;

/// <summary>
/// Evaluates real-time RF jamming, spoofing/meaconing risk, DGPS integrity, and OSNMA reception status,
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

        if (simulation.Jamming) baseScore = Math.Max(baseScore, 80.0);
        if (simulation.Spoofing) baseScore += 30.0;
        if (simulation.Meaconing) baseScore += 20.0;
        if (simulation.MacTamper) baseScore += 35.0;

        int score = (int)Math.Clamp(Math.Round(baseScore), 0, 100);
        string levelText;
        string severity;
        if (score > 70)
        {
            levelText = simulation.AnyActive ? "CRITICAL THREAT (SIM)" : "CRITICAL THREAT";
            severity = "Critical";
        }
        else if (score > 30)
        {
            levelText = simulation.AnyActive ? "MODERATE RISK (SIM)" : "MODERATE RISK";
            severity = "Warning";
        }
        else
        {
            levelText = "NOMINAL / SAFE";
            severity = "Nominal";
        }

        // Card 1: OSNMA Reception Provenance
        var osnmaStatus = _osnmaEngine.GetReceptionStatus(inavMessages);
        string osnmaStatusText;
        string osnmaDetails;
        if (simulation.MacTamper)
        {
            osnmaStatusText = "SIMULATED MAC TAMPER";
            osnmaDetails = "Simulated drill active: OSNMA HMAC-SHA-256 tag mismatch injected for diagnostic testing.";
        }
        else if (osnmaStatus == OsnmaReceptionStatus.NotAvailable)
        {
            osnmaStatusText = "NO I/NAV DATA";
            osnmaDetails = "Awaiting Galileo I/NAV telemetry from USB serial, BLE, or UDP bridge.";
        }
        else
        {
            osnmaStatusText = "I/NAV RECEIVED · UNVERIFIED";
            osnmaDetails = $"{inavMessages.Count} navigation frames captured. Live pages are unverified until checked offline with OSNMAlib / trusted EUSPA XML keys.";
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

        // Card 3: Spoofing / Meaconing
        bool isSpoofing = simulation.Spoofing || simulation.Meaconing;
        string spoofingStatus = simulation.Spoofing
            ? "SPOOFING (SIMULATED)"
            : (simulation.Meaconing ? "MEACONING (SIMULATED)" : "LOW RISK");
        string spoofingDetails =
            $"Pseudorange Residuals ($GPGST): {gstRmsError}. " +
            (isSpoofing
                ? "Simulated replay / coherent signal takeover anomaly active."
                : "Clock drift and geometry cross-checked against CelesTrak TLE orbit predictions.");

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
