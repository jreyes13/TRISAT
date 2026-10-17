import Foundation
import CoreLocation

/// Simplified Keplerian SGP4 Orbit Propagator for Open Sky Simulation
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
        observerLocation: CLLocationCoordinate2D = CLLocationCoordinate2D(latitude: 37.7749, longitude: -122.4194)
    ) -> [SatelliteInfo] {
        if tles.isEmpty {
            return generateFallbackModel(timeOffsetHours: timeOffsetHours)
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

            // ELEVATION MASK: Filter satellites below 15° elevation
            guard elevDeg >= 15.0 else { continue }

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

            let svidNum = (index % 32) + 1
            let isUsed = elevDeg > 25.0
            let isGal = constKind == .galileo
            let hasInav = isGal && (svidNum % 2 == 1)
            let uniqueId = "\(constKind.rawValue)_PRN_\(svidNum)_\(index)"

            let sat = SatelliteInfo(
                id: uniqueId,
                svid: svidNum,
                elevation: elevDeg,
                azimuth: azimDeg,
                cno: Int(28.0 + (elevDeg / 90.0) * 22.0),
                isGalileo: isGal,
                constellation: constKind,
                usedInFix: isUsed,
                hasInav: hasInav
            )
            satList.append(sat)
        }

        if satList.isEmpty {
            return generateFallbackModel(timeOffsetHours: timeOffsetHours)
        }

        // Group by constellation and cap at top 7 highest-elevation satellites per constellation
        var finalCappedList: [SatelliteInfo] = []
        for const in GnssConstellation.allCases {
            let constSats = satList.filter { $0.constellation == const }
                .sorted { $0.elevation > $1.elevation }
                .prefix(7)
            finalCappedList.append(contentsOf: constSats)
        }

        return finalCappedList
    }

    private static func generateFallbackModel(timeOffsetHours: Double) -> [SatelliteInfo] {
        let delta = timeOffsetHours * 15.0
        return [
            SatelliteInfo(id: "Galileo_PRN01", svid: 1, elevation: Swift.min(Swift.max(65.0 + delta, 10), 85), azimuth: 45.0, cno: 42, isGalileo: true, constellation: .galileo, usedInFix: true, hasInav: true),
            SatelliteInfo(id: "Galileo_PRN02", svid: 2, elevation: Swift.min(Swift.max(30.0 + delta, 10), 85), azimuth: 120.0, cno: 38, isGalileo: true, constellation: .galileo, usedInFix: true, hasInav: true),
            SatelliteInfo(id: "Galileo_PRN05", svid: 5, elevation: Swift.min(Swift.max(80.0 - delta, 10), 85), azimuth: 210.0, cno: 45, isGalileo: true, constellation: .galileo, usedInFix: true, hasInav: true),
            SatelliteInfo(id: "GPS_PRN12", svid: 12, elevation: Swift.min(Swift.max(45.0 + delta, 10), 85), azimuth: 180.0, cno: 40, isGalileo: false, constellation: .gps, usedInFix: true, hasInav: false),
            SatelliteInfo(id: "GLONASS_PRN08", svid: 8, elevation: Swift.min(Swift.max(25.0 + delta, 10), 85), azimuth: 300.0, cno: 35, isGalileo: false, constellation: .glonass, usedInFix: false, hasInav: false),
            SatelliteInfo(id: "BeiDou_PRN24", svid: 24, elevation: Swift.min(Swift.max(15.0 + delta, 10), 85), azimuth: 75.0, cno: 29, isGalileo: false, constellation: .beidou, usedInFix: false, hasInav: false)
        ]
    }
}
