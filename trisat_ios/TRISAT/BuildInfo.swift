import Foundation

struct BuildInfo {
    static var version: String { "1.1.0" }
    static var buildNumber: String { "2" }

    static var buildDateString: String {
        let formatter = DateFormatter()
        formatter.dateStyle = .medium
        formatter.timeStyle = .short
        return formatter.string(from: Date())
    }
}
