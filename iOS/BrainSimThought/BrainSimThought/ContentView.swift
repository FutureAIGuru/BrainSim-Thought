//
//  ContentView.swift
//  BrainSimThought
//
//  Created by Zayd Kudaimi on 9/21/26.
//

import SwiftUI

struct ContentView: View {
    var body: some View {
        TabView {
            Tab("Knowledge", systemImage: "point.3.connected.trianglepath.dotted") {
                BrowseView()
            }
            Tab("Add", systemImage: "plus.bubble") {
                StatementView()
            }
            Tab("Query", systemImage: "questionmark.bubble") {
                QueryView()
            }
            Tab("Modules", systemImage: "cpu") {
                ModulesView()
            }
            Tab("Files", systemImage: "folder") {
                FilesView()
            }
        }
    }
}

#Preview {
    let store = UKSStore()
    ContentView()
        .environment(store)
        .environment(ModuleEngine(store: store))
}
