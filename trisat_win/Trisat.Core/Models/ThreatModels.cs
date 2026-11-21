namespace Trisat.Core.Models;

/// <summary>
/// Reception and verification status for live Galileo I/NAV OSNMA telemetry.
/// Preserves strict data provenance: received pages are never marked verified without cryptographic or bitwise validation.
/// </summary>
public enum OsnmaReceptionStatus
{
    NotAvailable,
    Unverified,
    BitwiseVerified,
    Authenticated,
    TamperDetected
}

/// <summary>
/// Result of the standalone EUSPA Annex A cryptographic self-test vector (separate from live session status).
/// </summary>
public sealed record OsnmaSelfTestResult(
    bool Passed,
    bool DsmPkrPaddingPassed,
    bool EcdsaP256KrootPassed,
    bool DsmKrootPaddingPassed,
    bool TeslaChainPassed,
    bool MacSeqPassed,
    bool Adkd4Tag2Passed,
    string Summary,
    DateTimeOffset ExecutedAtUtc);

/// <summary>
/// Interactive threat simulation switches (for diagnostic/training drills, clearly flagged as simulated).
/// </summary>
public sealed record ThreatSimulationFlags(
    bool Jamming = false,
    bool Spoofing = false,
    bool Meaconing = false,
    bool MacTamper = false)
{
    public bool AnyActive => Jamming || Spoofing || Meaconing || MacTamper;
}

/// <summary>
/// Aggregated real-time threat evaluation displayed on the Threat Analysis dashboard.
/// </summary>
public sealed record ThreatAssessment(
    int ThreatScore,
    string ThreatLevelText,
    string ThreatSeverity,
    OsnmaReceptionStatus OsnmaStatus,
    string OsnmaStatusText,
    string OsnmaDetails,
    string JammingStatusText,
    bool IsJammingRisk,
    string JammingDetails,
    string SpoofingStatusText,
    bool IsSpoofingRisk,
    string SpoofingDetails,
    string DgpsStatusText,
    bool IsDgpsActive,
    string DgpsDetails,
    bool HasActiveSimulation);
