import SwiftUI

@main
struct CwiIosCarplayApp: App {
    init() {
        RuntimeConfigLoader.applyIfPresent()
    }
    var body: some Scene {
        WindowGroup {
            ContentView()
        }
    }
}


