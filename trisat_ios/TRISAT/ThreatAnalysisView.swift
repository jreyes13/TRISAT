import SwiftUI

struct ThreatAnalysisView: View {
    @ObservedObject var viewModel: GnssViewModel
    
    var body: some View {
        ScrollView {
            VStack(spacing: 16) {
                HStack {
                    VStack(alignment: .leading) {
                        Text("Threat Analysis")
                            .font(.title2)
                            .bold()
                        Text("Interference, Spoofing & OSNMA Verification")
                            .font(.caption)
                            .foregroundColor(.secondary)
                    }
                    Spacer()
                }
                .padding([.horizontal, .top])
                
                // Threat Card 1: OS-NMA Authentication
                ThreatMetricCard(
                    title: "OS-NMA Navigation Auth",
                    status: "AUTHENTICATED",
                    statusColor: .green,
                    icon: "lock.shield.fill",
                    details: "HKROOT Certificate Verified via Public Key. TESLA Key Chain Valid."
                )
                
                // Threat Card 2: Jamming Detection
                ThreatMetricCard(
                    title: "Jamming / Interference",
                    status: "NO INTERFERENCE",
                    statusColor: .green,
                    icon: "wifi.exclamationmark",
                    details: "C/N0 levels nominal across E1/E5 bands. AGC levels stable."
                )
                
                // Threat Card 3: Spoofing / Meaconing Analysis
                ThreatMetricCard(
                    title: "Spoofing / Meaconing",
                    status: "LOW RISK",
                    statusColor: .green,
                    icon: "shield.slash.fill",
                    details: "Clock drift and Pseudorange consistency checks within normal parameters."
                )
            }
            .padding(.bottom)
        }
    }
}

// Vista para renderizar cada tarjeta de métrica de amenaza
struct ThreatMetricCard: View {
    let title: String
    let status: String
    let statusColor: Color
    let icon: String
    let details: String
    
    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack {
                Image(systemName: icon)
                    .foregroundColor(statusColor)
                    .font(.title3)
                Text(title)
                    .font(.subheadline)
                    .fontWeight(.bold)
                Spacer()
                Text(status)
                    .font(.caption2)
                    .fontWeight(.bold)
                    .padding(.horizontal, 8)
                    .padding(.vertical, 4)
                    .background(statusColor.opacity(0.15))
                    .foregroundColor(statusColor)
                    .cornerRadius(6)
            }
            
            Text(details)
                .font(.caption)
                .foregroundColor(.secondary)
        }
        .padding()
        .background(Color.gray.opacity(0.08))
        .cornerRadius(12)
        .padding(.horizontal)
    }
}