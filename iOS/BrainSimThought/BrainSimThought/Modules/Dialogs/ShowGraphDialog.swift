import SwiftUI

// ModuleShowGraphDlg: every link among the descendants of a root Thought, laid out as a
// graph. The module supplies the edges (the "graph" query); the layout is the app's.

struct ShowGraphState: Decodable {
    let root: String?
}

struct ShowGraphDialog: View {
    @State private var module: ModuleLink<ShowGraphState>
    @State private var layout = ForceLayout()
    @State private var selected: String?
    /// The root typed here, or nil to follow the Knowledge tree's root.
    @State private var root: String?
    init(label: String) { _module = State(initialValue: ModuleLink(label)) }

    var body: some View {
        ModuleForm(module: module, pollInterval: .seconds(2)) { s in
            let typed = Binding(get: { root ?? s.root ?? "Object" }, set: { root = $0 })
            GraphView(layout: layout, selected: $selected) { selected = $0 }
                .frame(height: 420)
                .pictureSection()
            Section {
                FieldRow(placeholder: "Root", text: typed) { load(typed.wrappedValue, force: true) }
                WideButton("Refresh") { load(typed.wrappedValue, force: true) }
            }
            // the desktop only redraws on Refresh; here also when the edges change
            .task(id: s.root) {
                load(typed.wrappedValue, force: true)
                while !Task.isCancelled {
                    try? await Task.sleep(for: .seconds(2))
                    load(typed.wrappedValue, force: false)
                }
            }
        }
    }

    private func load(_ root: String, force: Bool) {
        let r = root.trimmingCharacters(in: .whitespaces)
        guard let graph = module.query(UKSGraph.self, "graph", r.isEmpty ? [:] : ["root": r]) else { return }
        if force || graph.edges.count != layout.edges.count || graph.nodes.count != layout.nodes.count {
            layout.load(graph)
        }
    }
}
