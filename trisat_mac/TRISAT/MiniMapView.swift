import SwiftUI
import MapKit

struct MapLocationItem: Identifiable {
    let id = UUID()
    let coordinate: CLLocationCoordinate2D
}

struct MiniMapView: View {
    let coordinate: CLLocationCoordinate2D?

    @State private var region = MKCoordinateRegion(
        center: CLLocationCoordinate2D(latitude: 0, longitude: 0),
        span: MKCoordinateSpan(latitudeDelta: 0.008, longitudeDelta: 0.008)
    )

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack {
                Image(systemName: "map.fill")
                    .foregroundColor(.cyan)
                Text("Geospatial Position Map")
                    .font(.caption)
                    .fontWeight(.bold)
                    .foregroundColor(.secondary)
                Spacer()
                if coordinate != nil {
                    Text("LIVE FIX")
                        .font(.caption2)
                        .fontWeight(.bold)
                        .foregroundColor(.green)
                        .padding(.horizontal, 6)
                        .padding(.vertical, 2)
                        .background(Color.green.opacity(0.15))
                        .cornerRadius(4)
                } else {
                    Text("NO FIX")
                        .font(.caption2)
                        .fontWeight(.bold)
                        .foregroundColor(.orange)
                        .padding(.horizontal, 6)
                        .padding(.vertical, 2)
                        .background(Color.orange.opacity(0.15))
                        .cornerRadius(4)
                }
            }

            if let coord = coordinate {
                Map(coordinateRegion: $region, annotationItems: [MapLocationItem(coordinate: coord)]) { item in
                    MapMarker(coordinate: item.coordinate, tint: .cyan)
                }
                .frame(height: 160)
                .cornerRadius(12)
                .onAppear {
                    region.center = coord
                }
                .onChange(of: coord.latitude) { _ in
                    region.center = coord
                }
            } else {
                ZStack {
                    RoundedRectangle(cornerRadius: 12)
                        .fill(Color.black.opacity(0.25))
                        .frame(height: 160)
                        .overlay(
                            RoundedRectangle(cornerRadius: 12)
                                .strokeBorder(Color.orange.opacity(0.3), style: StrokeStyle(lineWidth: 1, dash: [6]))
                        )

                    VStack(spacing: 8) {
                        Image(systemName: "location.slash.fill")
                            .font(.system(size: 26))
                            .foregroundColor(.orange)

                        Text("Waiting for Location Fix...")
                            .font(.subheadline)
                            .fontWeight(.bold)
                            .foregroundColor(.primary)

                        Text("Position is required to project celestial orbits.\nEnable Location Services in macOS Settings or link a GNSS receiver.")
                            .font(.caption2)
                            .multilineTextAlignment(.center)
                            .foregroundColor(.secondary)
                            .padding(.horizontal, 16)
                    }
                }
            }
        }
        .padding(12)
        .background(Color.gray.opacity(0.1))
        .cornerRadius(14)
    }
}
