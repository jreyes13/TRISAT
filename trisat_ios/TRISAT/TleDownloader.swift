import Foundation
import Combine

/// Represents parsed Two-Line Element (TLE) orbital data for a satellite
struct TleData: Identifiable, Codable {
    var id: String { svid }
    let name: String
    let svid: String
    let constellation: String
    let line1: String
    let line2: String

    // Extracted TLE orbital parameters
    let inclinationDegrees: Double
    let raanDegrees: Double
    let eccentricity: Double
    let argPerigeeDegrees: Double
    let meanAnomalyDegrees: Double
    let meanMotionRevsPerDay: Double
}

@MainActor
class TleDownloader: ObservableObject {
    @Published var isDownloading: Bool = false
    @Published var lastDownloadDate: Date?
    @Published var totalTleCount: Int = 0
    @Published var galileoCount: Int = 0
    @Published var gpsCount: Int = 0
    @Published var glonassCount: Int = 0
    @Published var beidouCount: Int = 0
    @Published var downloadStatusText: String = "No TLE orbits downloaded yet."
    @Published var parsedTles: [TleData] = []

    var formattedLastDownloadDate: String {
        guard let date = lastDownloadDate else { return "Never downloaded" }
        let formatter = DateFormatter()
        formatter.dateStyle = .medium
        formatter.timeStyle = .short
        return formatter.string(from: date)
    }

    private let celestrakUrls: [String: String] = [
        "GALILEO": "https://celestrak.org/NORAD/elements/gp.php?GROUP=galileo&FORMAT=tle",
        "GPS": "https://celestrak.org/NORAD/elements/gp.php?GROUP=gps-ops&FORMAT=tle",
        "GLONASS": "https://celestrak.org/NORAD/elements/gp.php?GROUP=glo-ops&FORMAT=tle",
        "BEIDOU": "https://celestrak.org/NORAD/elements/gp.php?GROUP=beidou&FORMAT=tle"
    ]

    init() {
        loadCachedTles()
    }

    func fetchAllTleOrbits() async {
        isDownloading = true
        downloadStatusText = "Downloading TLE orbits from CelesTrak..."
        var allFetched: [TleData] = []

        for (constName, urlString) in celestrakUrls {
            guard let url = URL(string: urlString) else { continue }
            do {
                let (data, _) = try await URLSession.shared.data(from: url)
                if let rawText = String(data: data, encoding: .utf8) {
                    let parsed = parseTleText(rawText, constellation: constName)
                    allFetched.append(contentsOf: parsed)
                }
            } catch {
                print("Failed downloading TLE for \(constName): \(error.localizedDescription)")
            }
        }

        if !allFetched.isEmpty {
            parsedTles = allFetched
            lastDownloadDate = Date()
            totalTleCount = allFetched.count
            galileoCount = allFetched.filter { $0.constellation == "GALILEO" }.count
            gpsCount = allFetched.filter { $0.constellation == "GPS" }.count
            glonassCount = allFetched.filter { $0.constellation == "GLONASS" }.count
            beidouCount = allFetched.filter { $0.constellation == "BEIDOU" }.count
            downloadStatusText = "TLE Orbits Loaded (\(totalTleCount) Satellites)"
            saveCachedTles(allFetched)
        } else {
            downloadStatusText = "Download failed. Using fallback orbital model."
        }

        isDownloading = false
    }

    private func parseTleText(_ text: String, constellation: String) -> [TleData] {
        var results: [TleData] = []
        let lines = text.components(separatedBy: .newlines).map { $0.trimmingCharacters(in: .whitespaces) }.filter { !$0.isEmpty }

        var i = 0
        while i + 2 < lines.count {
            let name = lines[i]
            let l1 = lines[i+1]
            let l2 = lines[i+2]

            if l1.hasPrefix("1 ") && l2.hasPrefix("2 ") {
                let svidStr = l1.count > 6 ? String(l1[l1.index(l1.startIndex, offsetBy: 2)...l1.index(l1.startIndex, offsetBy: 6)]).trimmingCharacters(in: .whitespaces) : "\(i)"

                // Parse inclination from Line 2 (cols 8-16)
                let inc = Double(l2.count > 16 ? String(l2[l2.index(l2.startIndex, offsetBy: 8)...l2.index(l2.startIndex, offsetBy: 15)]).trimmingCharacters(in: .whitespaces) : "55.0") ?? 55.0
                let raan = Double(l2.count > 25 ? String(l2[l2.index(l2.startIndex, offsetBy: 17)...l2.index(l2.startIndex, offsetBy: 24)]).trimmingCharacters(in: .whitespaces) : "0.0") ?? 0.0
                let eccStr = l2.count > 33 ? "0." + String(l2[l2.index(l2.startIndex, offsetBy: 26)...l2.index(l2.startIndex, offsetBy: 32)]).trimmingCharacters(in: .whitespaces) : "0.001"
                let ecc = Double(eccStr) ?? 0.001
                let argPerigee = Double(l2.count > 42 ? String(l2[l2.index(l2.startIndex, offsetBy: 34)...l2.index(l2.startIndex, offsetBy: 41)]).trimmingCharacters(in: .whitespaces) : "0.0") ?? 0.0
                let meanAnomaly = Double(l2.count > 51 ? String(l2[l2.index(l2.startIndex, offsetBy: 43)...l2.index(l2.startIndex, offsetBy: 50)]).trimmingCharacters(in: .whitespaces) : "0.0") ?? 0.0
                let meanMotion = Double(l2.count > 63 ? String(l2[l2.index(l2.startIndex, offsetBy: 52)...l2.index(l2.startIndex, offsetBy: 62)]).trimmingCharacters(in: .whitespaces) : "1.7") ?? 1.7

                let tle = TleData(
                    name: name,
                    svid: svidStr,
                    constellation: constellation,
                    line1: l1,
                    line2: l2,
                    inclinationDegrees: inc,
                    raanDegrees: raan,
                    eccentricity: ecc,
                    argPerigeeDegrees: argPerigee,
                    meanAnomalyDegrees: meanAnomaly,
                    meanMotionRevsPerDay: meanMotion
                )
                results.append(tle)
                i += 3
            } else {
                i += 1
            }
        }
        return results
    }

    private func saveCachedTles(_ tles: [TleData]) {
        if let encoded = try? JSONEncoder().encode(tles) {
            UserDefaults.standard.set(encoded, forKey: "TRISAT_CACHED_TLES")
            UserDefaults.standard.set(Date(), forKey: "TRISAT_TLE_LAST_DATE")
        }
    }

    private func loadCachedTles() {
        if let data = UserDefaults.standard.data(forKey: "TRISAT_CACHED_TLES"),
           let decoded = try? JSONDecoder().decode([TleData].self, from: data) {
            parsedTles = decoded
            totalTleCount = decoded.count
            galileoCount = decoded.filter { $0.constellation == "GALILEO" }.count
            gpsCount = decoded.filter { $0.constellation == "GPS" }.count
            glonassCount = decoded.filter { $0.constellation == "GLONASS" }.count
            beidouCount = decoded.filter { $0.constellation == "BEIDOU" }.count
            lastDownloadDate = UserDefaults.standard.object(forKey: "TRISAT_TLE_LAST_DATE") as? Date
            downloadStatusText = "TLE Orbits Loaded (\(totalTleCount) Satellites)"
        }
    }
}
