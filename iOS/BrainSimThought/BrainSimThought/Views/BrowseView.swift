import SwiftUI

/// Knowledge tab: an interactive graph of the UKS, with the tree list as an alternative.
struct BrowseView: View {
    enum Mode: String, CaseIterable { case graph = "Graph", list = "List" }

    @Environment(UKSStore.self) private var store
    @State private var path: [String] = []
    @State private var searchText = ""
    @State private var mode = Mode.graph

    // Graph state
    @State private var layout = ForceLayout()
    @State private var focus: String? = "Thought"
    @State private var depth = 2
    @State private var selected: String?
    @State private var graphError: String?

    var body: some View {
        NavigationStack(path: $path) {
            Group {
                switch mode {
                case .graph: graphContent
                case .list: listContent
                }
            }
            .searchable(text: $searchText, prompt: "Find a Thought")
            .autocorrectionDisabled()
            .textInputAutocapitalization(.never)
            .navigationTitle(mode == .graph ? (focus ?? "Everything") : "Knowledge")
            .navigationBarTitleDisplayMode(.inline)
            .navigationDestination(for: String.self) { ThoughtDetailView(label: $0) }
            .toolbar {
                ToolbarItem(placement: .principal) {
                    Picker("View", selection: $mode) {
                        ForEach(Mode.allCases, id: \.self) { Text($0.rawValue) }
                    }
                    .pickerStyle(.segmented)
                    .frame(width: 180)
                }
                if mode == .graph {
                    ToolbarItem(placement: .topBarTrailing) { graphMenu }
                }
            }
        }
        .onAppear(perform: reloadGraph)
        .onChange(of: store.revision) { reloadGraph() }
        .onChange(of: focus) { reloadGraph() }
        .onChange(of: depth) { reloadGraph() }
    }

    // MARK: - Graph

    @ViewBuilder private var graphContent: some View {
        if !searchText.isEmpty {
            searchResults { label in
                focus = label
                selected = label
                searchText = ""
            }
        } else {
            GraphView(layout: layout, selected: $selected) { label in
                focus = label
                selected = label
            }
            .overlay(alignment: .top) {
                if let graphError {
                    Label(graphError, systemImage: "exclamationmark.triangle")
                        .font(.footnote)
                        .padding(8)
                        .background(.regularMaterial, in: Capsule())
                        .padding(.top, 8)
                } else if layout.truncated {
                    Text("Showing the first \(layout.nodes.count) Thoughts. Focus on one to see more.")
                        .font(.footnote)
                        .padding(.horizontal, 12).padding(.vertical, 6)
                        .background(.regularMaterial, in: Capsule())
                        .padding(.top, 8)
                }
            }
            .overlay(alignment: .bottomLeading) {
                if let selected, let thought = store.thought(selected) {
                    inspector(thought)
                        .transition(.move(edge: .bottom).combined(with: .opacity))
                }
            }
            .animation(.snappy, value: selected)
        }
    }

    private var graphMenu: some View {
        Menu("Graph Options", systemImage: "ellipsis.circle") {
            Picker("Depth", selection: $depth) {
                Text("1 hop").tag(1)
                Text("2 hops").tag(2)
                Text("3 hops").tag(3)
            }
            Section {
                Button("Focus on Thought", systemImage: "scope") { focus = "Thought" }
                Button("Show Everything", systemImage: "circle.hexagongrid") { focus = nil }
                Button("Re-layout", systemImage: "arrow.triangle.2.circlepath") { layout.relayout() }
            }
        }
    }

    private func inspector(_ thought: UKSThought) -> some View {
        let index = layout.index(of: thought.label)
        let pinned = index.map { layout.nodes[$0].pinned } ?? false
        return VStack(alignment: .leading, spacing: 8) {
            HStack(alignment: .firstTextBaseline) {
                Text(thought.label).font(.headline)
                Spacer()
                Button("Close", systemImage: "xmark.circle.fill") { selected = nil }
                    .labelStyle(.iconOnly)
                    .foregroundStyle(.secondary)
            }
            if !thought.parents.isEmpty {
                Text("is-a \(thought.parents.joined(separator: ", "))")
                    .font(.subheadline)
                    .foregroundStyle(.secondary)
                    .lineLimit(1)
            }
            Text("\(thought.children.count) children · \(thought.linksTo.count) links · \(thought.linksFrom.count) references")
                .font(.caption)
                .foregroundStyle(.secondary)
            HStack {
                if thought.label != focus {
                    Button("Focus", systemImage: "scope") { focus = thought.label }
                }
                NavigationLink(value: thought.label) {
                    Label("Details", systemImage: "info.circle")
                }
                if pinned, let index {
                    Button("Unpin", systemImage: "pin.slash") { layout.unpin(index) }
                }
            }
            .buttonStyle(.bordered)
            .controlSize(.small)
        }
        .padding()
        .frame(maxWidth: 360, alignment: .leading)
        .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 16))
        .padding(12)
        .padding(.trailing, 56) // leave the zoom controls uncovered
    }

    private func reloadGraph() {
        do {
            // Fall back to the top of the tree if the focused Thought was deleted.
            if let f = focus, store.thought(f) == nil { focus = "Thought"; return }
            layout.load(try store.graph(root: focus, depth: depth, maxNodes: focus == nil ? 200 : 150))
            if let s = selected, layout.index(of: s) == nil { selected = nil }
            graphError = nil
        } catch {
            graphError = error.localizedDescription
        }
    }

    // MARK: - List

    private var listContent: some View {
        List {
            if searchText.isEmpty {
                Section {
                    ForEach(store.roots(), id: \.self) { ThoughtRow(label: $0) }
                } header: {
                    Text("Roots")
                } footer: {
                    Text("\(store.thoughtCount) Thoughts in the UKS")
                }
            } else {
                let hits = store.search(searchText)
                if hits.isEmpty {
                    ContentUnavailableView.search(text: searchText)
                }
                ForEach(hits, id: \.self) { ThoughtRow(label: $0) }
            }
        }
        .id("\(store.revision)-\(store.liveRevision)")
    }

    /// Search hits that act on the graph instead of navigating.
    private func searchResults(_ choose: @escaping (String) -> Void) -> some View {
        List {
            let hits = store.search(searchText)
            if hits.isEmpty {
                ContentUnavailableView.search(text: searchText)
            }
            ForEach(hits, id: \.self) { label in
                Button {
                    choose(label)
                } label: {
                    Label(label, systemImage: "scope")
                }
            }
        }
    }
}

struct ThoughtRow: View {
    @Environment(UKSStore.self) private var store
    let label: String

    var body: some View {
        NavigationLink(value: label) {
            let info = store.thought(label)
            HStack {
                Text(label)
                Spacer()
                if let n = info?.children.count, n > 0 {
                    Text("\(n)")
                        .font(.caption.monospacedDigit())
                        .foregroundStyle(.secondary)
                }
            }
        }
    }
}

struct ThoughtDetailView: View {
    @Environment(UKSStore.self) private var store
    @Environment(\.dismiss) private var dismiss
    let label: String
    @State private var error: String?
    @State private var confirmDelete = false
    @State private var renaming = false
    @State private var newLabel = ""

    var body: some View {
        Group {
            if let thought = store.thought(label) {
                List {
                    if thought.isLink || thought.isSequence || thought.value != nil {
                        Section("Thought") {
                            if thought.isLink { LabeledContent("Link", value: thought.text) }
                            if thought.isSequence { Label("Sequence element", systemImage: "list.number") }
                            if let value = thought.value { LabeledContent("Value", value: value) }
                        }
                    }
                    if !thought.parents.isEmpty {
                        Section("Is a") {
                            ForEach(thought.parents, id: \.self) { parent in
                                ThoughtRow(label: parent)
                                    .swipeActions {
                                        Button("Unlink", role: .destructive) { run { try store.removeParent(parent, from: label) } }
                                    }
                            }
                        }
                    }
                    if !thought.linksTo.isEmpty {
                        Section("Links") {
                            ForEach(thought.linksTo) { l in
                                LinkRow(link: l, highlight: .to)
                                    .swipeActions {
                                        Button("Delete", role: .destructive) { run { try store.deleteLink(l, from: label) } }
                                    }
                            }
                        }
                    }
                    if !thought.linksFrom.isEmpty {
                        Section("Referenced by") {
                            ForEach(thought.linksFrom) { LinkRow(link: $0, highlight: .from) }
                        }
                    }
                    if !thought.children.isEmpty {
                        Section("Children (\(thought.children.count))") {
                            ForEach(thought.children, id: \.self) { ThoughtRow(label: $0) }
                        }
                    }
                }
                .id("\(store.revision)-\(store.liveRevision)")
            } else {
                ContentUnavailableView("No Thought labeled “\(label)”", systemImage: "questionmark.circle")
            }
        }
        .navigationTitle(label)
        .toolbar {
            Menu("Actions", systemImage: "ellipsis.circle") {
                Button("Rename", systemImage: "pencil") { newLabel = label; renaming = true }
                Button("Fire", systemImage: "bolt") { run { try store.fire(label) } }
                Button("Delete", systemImage: "trash", role: .destructive) { confirmDelete = true }
            }
        }
        .alert("Rename “\(label)”", isPresented: $renaming) {
            TextField("New label", text: $newLabel)
                .textInputAutocapitalization(.never)
                .autocorrectionDisabled()
            Button("Rename") {
                run {
                    try store.rename(label, to: newLabel)
                    dismiss()   // the old label no longer resolves
                }
            }
            Button("Cancel", role: .cancel) {}
        }
        .confirmationDialog("Delete “\(label)”?", isPresented: $confirmDelete, titleVisibility: .visible) {
            Button("Delete Thought", role: .destructive) {
                run { try store.deleteThought(label) }
                dismiss()
            }
            Button("Delete with Descendants", role: .destructive) {
                run { try store.deleteThought(label, withChildren: true) }
                dismiss()
            }
        } message: {
            Text("Deleting only this Thought moves its children up to its parents.")
        }
        .alert("UKS", isPresented: .constant(error != nil), presenting: error) { _ in
            Button("OK") { error = nil }
        } message: { Text($0) }
    }

    private func run(_ action: () throws -> Void) {
        do { try action() } catch { self.error = error.localizedDescription }
    }
}

/// One Link; the far end navigates when it is a labeled Thought, so the graph can be walked.
struct LinkRow: View {
    enum End { case from, to }
    let link: UKSLink
    var highlight: End = .to

    var body: some View {
        let far = highlight == .to ? link.toLabel : link.fromLabel
        let content = VStack(alignment: .leading, spacing: 4) {
            HStack(spacing: 6) {
                Text(link.from)
                Image(systemName: "arrow.right").font(.caption2).foregroundStyle(.tertiary)
                Text(link.linkType).foregroundStyle(.tint)
                if !link.to.isEmpty {
                    Image(systemName: "arrow.right").font(.caption2).foregroundStyle(.tertiary)
                    Text(link.to)
                }
            }
            .font(.body)
            HStack(spacing: 8) {
                Text(link.weight, format: .number.precision(.fractionLength(2)))
                    .font(.caption.monospacedDigit())
                if link.isConditional {
                    Text("conditional").font(.caption).foregroundStyle(.orange)
                }
                if link.inheritanceDepth > 0 {
                    Text("inherited from \(link.inheritedFrom ?? "ancestor")").font(.caption)
                }
                if !link.label.isEmpty {
                    Text(link.label).font(.caption.monospaced())
                }
                if let ttl = link.timeToLive {
                    Label(Duration.seconds(ttl).formatted(.time(pattern: .minuteSecond)), systemImage: "timer")
                        .font(.caption)
                }
            }
            .foregroundStyle(.secondary)
        }
        if let far {
            NavigationLink(value: far) { content }
        } else {
            content
        }
    }
}
