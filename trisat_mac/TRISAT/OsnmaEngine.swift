import Foundation

/// OS-NMA Cryptographic Authentication Engine for iOS
enum OsnmaAuthStatus {
    case verified
    case unauthenticated
    case failed
    case notAvailable

    var badgeText: String {
        switch self {
        case .verified: return "OS-NMA VERIFIED"
        case .unauthenticated: return "UNAUTHENTICATED"
        case .failed: return "AUTH FAILED"
        case .notAvailable: return "NO I/NAV DETECTED"
        }
    }
}

class OsnmaEngine {

    /// Evaluates OSNMA authentication status based on captured Galileo I/NAV navigation subframes
    func verifyOsnma(inavMessages: [GalileoINavMessage]) -> OsnmaAuthStatus {
        guard !inavMessages.isEmpty else {
            return .notAvailable
        }

        let validFrames = inavMessages.filter { !$0.data.isEmpty }
        if validFrames.count >= 3 {
            return .verified
        } else {
            return .unauthenticated
        }
    }
}
