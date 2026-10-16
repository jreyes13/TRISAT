import SwiftUI

struct SignalProcessingView: View {
    @ObservedObject var viewModel: GnssViewModel
    
    var body: some View {
        NavigationView {
            VStack(alignment: .leading, spacing: 12) {
                // Header Info
                HStack {
                    VStack(alignment: .leading) {
                        Text("Galileo I/NAV Pages")
                            .font(.title2)
                            .bold()
                        Text("Live Subframe Buffer & Demodulation")
                            .font(.caption)
                            .foregroundColor(.secondary)
                    }
                    Spacer()
                    Text("Total: \(viewModel.messages.count)")
                        .font(.subheadline)
                        .fontWeight(.bold)
                        .padding(8)
                        .background(Color.blue.opacity(0.1))
                        .cornerRadius(8)
                }
                .padding(.horizontal)
                
                Divider()
                
                // Live Stream List
                ScrollView {
                    LazyVStack(spacing: 8) {
                        ForEach(viewModel.messages.reversed()) { msg in
                            INavMessageCard(message: msg)
                        }
                    }
                    .padding(.horizontal)
                }
            }
            .navigationBarHidden(true)
        }
    }
}