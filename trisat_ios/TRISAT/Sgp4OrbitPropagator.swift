import Foundation
import CoreLocation

/// Keplerian SGP4 Orbit Propagator for 4-Constellation Open Sky Simulation (Galileo, GPS, GLONASS, BeiDou)
class Sgp4OrbitPropagator {

    /// Calculates Greenwich Mean Sidereal Time (GMST) in radians for a given UTC Date + offset
    private static func calculateGmst(date: Date, offsetHours: Double) -> Double {
        let utcDate = date.addingTimeInterval(offsetHours * 3600.0)
        let julianDate = utcDate.timeIntervalSince1970 / 86400.0 + 2440587.5
        let d = julianDate - 2451545.0
        var gmstDeg = (280.46061837 + 360.98564736629 * d).truncatingRemainder(dividingBy: 360.0)
        if gmstDeg < 0 { gmstDeg += 360.0 }
        return gmstDeg * .pi / 180.0
    }

    /// Propagates orbital TLE elements to calculate Elevation & Azimuth relative to ground observer location
    static func propagateToSkyplot(
        tles: [TleData],
        timeOffsetHours: Double,
        observerLocation: CLLocationCoordinate2D
    ) -> [SatelliteInfo] {
        let catalog = tles.isEmpty ? TleDownloader.generateBuiltInFourConstellationCatalog() : tles

        var satList: [SatelliteInfo] = []
        let deltaMinutes = timeOffsetHours * 60.0
        let gmstRad = calculateGmst(date: Date(), offsetHours: timeOffsetHours)

        let obsLatRad = observerLocation.latitude * .pi / 180.0
        let obsLonRad = observerLocation.longitude * .pi / 180.0

        var perConstCounter: [GnssConstellation: Int] = [:]

        for tle in catalog {
            let meanAnomalyRad = (tle.meanAnomalyDegrees + (tle.meanMotionRevsPerDay * 360.0 * (deltaMinutes / 1440.0))).truncatingRemainder(dividingBy: 360.0) * .pi / 180.0
            let raanRad = (tle.raanDegrees + (deltaMinutes * 0.25)).truncatingRemainder(dividingBy: 360.0) * .pi / 180.0
            let incRad = tle.inclinationDegrees * .pi / 180.0
            let argPerigeeRad = tle.argPerigeeDegrees * .pi / 180.0

            let uRad = meanAnomalyRad + argPerigeeRad
            let satDeclination = asin(sin(incRad) * sin(uRad))
            let satRightAscension = raanRad + atan2(cos(incRad) * sin(uRad), cos(uRad))
            let hourAngle = gmstRad + obsLonRad - satRightAscension

            let sinElev = sin(obsLatRad) * sin(satDeclination) + cos(obsLatRad) * cos(satDeclination) * cos(hourAngle)
            let elevRad = asin(Swift.min(Swift.max(sinElev, -1.0), 1.0))
            let elevDeg = elevRad * 180.0 / .pi

            // Elevation mask: 10° so all 4 constellations have plenty of visible satellites
            guard elevDeg >= 10.0 else { continue }

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

            let nextIdx = (perConstCounter[constKind] ?? 0) + 1
            perConstCounter[constKind] = nextIdx
            let svidNum = Int(tle.svid) ?? nextIdx
            let isUsed = elevDeg > 18.0
            let isGal = constKind == .galileo
            let canonicalId = "\(constKind.rawValue)_\(svidNum)"

            // Natural realistic receiver C/N0 curve: 31 dB-Hz at horizon (10°), peaking up to 48 dB-Hz at zenith
            let cno = Int(31.0 + (elevDeg / 90.0) * 17.5)

            let sat = SatelliteInfo(
                id: canonicalId,
                svid: svidNum,
                elevation: elevDeg,
                azimuth: azimDeg,
                cno: cno,
                isGalileo: isGal,
                constellation: constKind,
                usedInFix: isUsed,
                hasInav: isGal
            )
            satList.append(sat)
        }

        // Group by constellation and select up to 6 highest-elevation satellites per constellation (up to 24 total)
        var finalCappedList: [SatelliteInfo] = []
        for const in GnssConstellation.allCases {
            let constSats = satList.filter { $0.constellation == const }
                .sorted { $0.elevation > $1.elevation }
                .prefix(6)
            finalCappedList.append(contentsOf: constSats)
        }

        return finalCappedList
    }
}
