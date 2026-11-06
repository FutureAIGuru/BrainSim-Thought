//
//  BrainSimThoughtApp.swift
//  BrainSimThought
//
//  Created by Zayd Kudaimi on 9/21/26.
//

import SwiftUI

@main
struct BrainSimThoughtApp: App {
    @State private var store: UKSStore
    @State private var engine: ModuleEngine
    @State private var showSplash = true
    @Environment(\.scenePhase) private var scenePhase

    init() {
        let store = UKSStore()
        _store = State(initialValue: store)
        _engine = State(initialValue: ModuleEngine(store: store))
    }

    var body: some Scene {
        WindowGroup {
            ZStack {
                ContentView()
                    .environment(store)
                    .environment(engine)
                    .moduleNotices(engine)
                if showSplash {
                    SplashView {
                        withAnimation(.easeInOut(duration: 0.45)) { showSplash = false }
                    }
                    .transition(.opacity)
                    .zIndex(1)
                }
            }
        }
        .onChange(of: scenePhase) {
            if scenePhase != .active { store.autosaveNow() }
        }
    }
}
