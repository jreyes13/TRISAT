import SwiftUI

@main
struct TRISATApp: App {
    var body: some Scene {
        WindowGroup {
            MainTabView()
                #if os(macOS)
                .frame(minWidth: 1080, idealWidth: 1240, minHeight: 720, idealHeight: 800)
                #endif
        }
        #if os(macOS)
        .defaultSize(width: 1240, height: 800)
        #endif
    }
}
