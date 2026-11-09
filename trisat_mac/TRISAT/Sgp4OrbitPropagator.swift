import Foundation
import CoreLocation

/// Simplified Keplerian SGP4 Orbit Propagator for Open Sky Simulation
class Sgp4OrbitPropagator {

    // Official Galileo GSAT to PRN Mapping (EUSPA)
    private static let gsatToPrn: [String: String] = [
        "GSAT0101": "E11", "GSAT0102": "E12", "GSAT0103": "E19", "GSAT0104": "E20",
        "GSAT0201": "E18", "GSAT0202": "E14", "GSAT0203": "E26", "GSAT0204": "E22",
        "GSAT0205": "E24", "GSAT0206": "E30", "GSAT0207": "E07", "GSAT0208": "E08",
        "GSAT0209": "E09", "GSAT0210": "E01", "GSAT0211": "E02", "GSAT0212": "E03",
        "GSAT0213": "E04", "GSAT0214": "E05", "GSAT0215": "E21", "GSAT0216": "E25",
        "GSAT0217": "E27", "GSAT0218": "E31", "GSAT0219": "E36", "GSAT0220": "E13",
        "GSAT0221": "E15", "GSAT0222": "E33", "GSAT0223": "E34", "GSAT0224": "E10",
        "GSAT0225": "E29", "GSAT0226": "E23", "GSAT0227": "E06", "GSAT0228": "E16",
        "GSAT0229": "E17"
    ]

    // Galileo OSNMA capable satellites (~85% of active fleet)
    private static let osnmaPrnNumbers: Set<Int> = [
        1, 2, 3, 4, 5, 7, 8, 9, 11, 12, 13, 15, 18, 19, 21, 24, 25, 26, 27, 29, 30, 31, 33, 34, 36
    ]

    /// Calculates Greenwich Mean Sidereal Time (GMST) in radians for a given UTC Date + offset
    private static func calculateGmst(date: Date, offsetHours: Double) -> Double {
        let utcDate = date.addingTimeInterval(offsetHours * 3600.0)
        let julianDate = utcDate.timeIntervalSince1970 / 86400.0 + 2440587.5
        let d = julianDate - 2451545.0
        var gmstDeg = (280.46061837 + 360.98564736629 * d).truncatingRemainder(dividingBy: 360.0)
        if gmstDeg < 0 { gmstDeg += 360.0 }
        return gmstDeg * .pi / 180.0
    }

    /// Resolves actual PRN number from TLE metadata (e.g. GSAT name, PRN regex)
    private static func resolvePrn(name: String, constellation: GnssConstellation, fallbackIndex: Int) -> Int {
        switch constellation {
        case .galileo:
            // Match GSAT0xxx
            if let regex = try? NSRegularExpression(pattern: "GSAT\\d{4}"),
               let match = regex.firstMatch(in: name, range: NSRange(name.startIndex..., in: name)),
               let range = Range(match.range, in: name) {
                let gsatCode = String(name[range])
                if let prnCode = gsatToPrn[gsatCode],
                   let prnNum = Int(prnCode.replacingOccurrences(of: "E", with: "")) {
                    return prnNum
                }
            }
            // Fallback: check if PRN \d+ is present
            if let regex = try? NSRegularExpression(pattern: "PRN\\s*(\\d+)", options: .caseInsensitive),
               let match = regex.firstMatch(in: name, range: NSRange(name.startIndex..., in: name)),
               match.numberOfRanges > 1,
               let range = Range(match.range(at: 1), in: name),
               let num = Int(name[range]) {
                return num
            }
            return (fallbackIndex % 36) + 1

        case .gps:
            // Match PRN \d+
            if let regex = try? NSRegularExpression(pattern: "PRN\\s*(\\d+)", options: .caseInsensitive),
               let match = regex.firstMatch(in: name, range: NSRange(name.startIndex..., in: name)),
               match.numberOfRanges > 1,
               let range = Range(match.range(at: 1), in: name),
               let num = Int(name[range]) {
                return num
            }
            return (fallbackIndex % 32) + 1

        case .glonass:
            // Match (xxx) slot or 7xx
            if let regex = try? NSRegularExpression(pattern: "\\((\\d+)\\)"),
               let match = regex.firstMatch(in: name, range: NSRange(name.startIndex..., in: name)),
               match.numberOfRanges > 1,
               let range = Range(match.range(at: 1), in: name),
               let slot = Int(name[range]) {
                return slot > 700 ? (slot - 700) : slot
            }
            return (fallbackIndex % 24) + 1

        case .beidou:
            // Match (C\d+) or (B\d+) or PRN \d+
            if let regex = try? NSRegularExpression(pattern: "\\([CB]?(\\d+)\\)", options: .caseInsensitive),
               let match = regex.firstMatch(in: name, range: NSRange(name.startIndex..., in: name)),
               match.numberOfRanges > 1,
               let range = Range(match.range(at: 1), in: name),
               let num = Int(name[range]) {
                return num
            }
            return (fallbackIndex % 46) + 1
        }
    }

    /// Propagates orbital TLE elements to calculate Elevation & Azimuth relative to ground observer location
    static func propagateToSkyplot(
        tles: [TleData],
        timeOffsetHours: Double,
        observerLocation: CLLocationCoordinate2D?
    ) -> [SatelliteInfo] {
        guard let observerLocation = observerLocation, !tles.isEmpty else {
            return []
        }

        var satList: [SatelliteInfo] = []
        let deltaMinutes = timeOffsetHours * 60.0
        let gmstRad = calculateGmst(date: Date(), offsetHours: timeOffsetHours)

        let obsLatRad = observerLocation.latitude * .pi / 180.0
        let obsLonRad = observerLocation.longitude * .pi / 180.0

        for (index, tle) in tles.enumerated() {
            let meanAnomalyRad = (tle.meanAnomalyDegrees + (tle.meanMotionRevsPerDay * 360.0 * (deltaMinutes / 1440.0))).truncatingRemainder(dividingBy: 360.0) * .pi / 180.0
            let raanRad = (tle.raanDegrees + (deltaMinutes * 0.25)).truncatingRemainder(dividingBy: 360.0) * .pi / 180.0
            let incRad = tle.inclinationDegrees * .pi / 180.0
            let argPerigeeRad = tle.argPerigeeDegrees * .pi / 180.0

            // Argument of latitude u
            let uRad = meanAnomalyRad + argPerigeeRad

            // Sub-satellite declination and Greenwich right ascension
            let satDeclination = asin(sin(incRad) * sin(uRad))
            let satRightAscension = raanRad + atan2(cos(incRad) * sin(uRad), cos(uRad))

            // Hour Angle relative to observer: HA = GMST + obsLon - RightAscension
            let hourAngle = gmstRad + obsLonRad - satRightAscension

            // Topocentric Elevation angle
            let sinElev = sin(obsLatRad) * sin(satDeclination) + cos(obsLatRad) * cos(satDeclination) * cos(hourAngle)
            let elevRad = asin(Swift.min(Swift.max(sinElev, -1.0), 1.0))
            let elevDeg = elevRad * 180.0 / .pi

            // Operational Elevation Cutoff: 10.0°
            guard elevDeg >= 10.0 else { continue }

            // Azimuth calculation
            let cosHA = cos(hourAngle)
            let sinHA = sin(hourAngle)
            let azimRad = atan2(-sinHA, cos(obsLatRad) * tan(satDeclination) - sin(obsLatRad) * cosHA)
            let azimDeg = (azimRad * 180.0 / .pi + 360.0).truncatingRemainder(dividingBy: 360.0)

            let constKind: GnssConstellation = {
                switch tle.constellation.uppercased() {
                case "GALILEO": return .galileo
                case "GPS": return .gps
                case "GLONASS": return .glonass
                case "BEIDOU": return .beidou
                default: return .galileo
                }
            }()

            let svidNum = resolvePrn(name: tle.name, constellation: constKind, fallbackIndex: index)
            let isGal = constKind == .galileo
            let hasInav = isGal && osnmaPrnNumbers.contains(svidNum)
            let uniqueId = "\(constKind.rawValue)_\(svidNum)"

            let sat = SatelliteInfo(
                id: uniqueId,
                svid: svidNum,
                elevation: elevDeg,
                azimuth: azimDeg,
                cno: Int(28.0 + (elevDeg / 90.0) * 22.0),
                isGalileo: isGal,
                constellation: constKind,
                usedInFix: elevDeg >= 15.0,
                hasInav: hasInav
            )
            satList.append(sat)
        }

        // Return all visible satellites sorted by constellation and PRN (no artificial cap)
        return satList.sorted {
            if $0.constellation != $1.constellation {
                return $0.constellation.rawValue < $1.constellation.rawValue
            }
            return $0.svid < $1.svid
        }
    }
}
