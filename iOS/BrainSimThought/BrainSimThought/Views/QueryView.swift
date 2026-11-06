import SwiftUI

/// Queries the UKS, like the desktop Query dialog: attributes of a Thought (with
/// inheritance, conflicts and conditionals), Why?/Why not?, and search by attributes.
struct QueryView: View {
    @Environment(UKSStore.self) private var store

    enum Mode: String, CaseIterable { case attributes = "Attributes", find = "Find by attributes" }

    @State private var mode = Mode.attributes

    // Attribute query
    @State private var source = ""
    @State private var linkType = ""
    @State private var target = ""
    @State private var filter = ""
    @State private var result: UKSQueryResult?
    @State private var explanation: String?

    // Search by attributes
    @State private var attributes = ""
    @State private var ancestor = ""
    @State private var matches: [UKSScored]?

    @State private var error: String?

    var body: some View {
        NavigationStack {
            Form {
                Picker("Mode", selection: $mode) {
                    ForEach(Mode.allCases, id: \.self) { Text($0.rawValue) }
                }
                .pickerStyle(.segmented)
                .listRowBackground(Color.clear)

                switch mode {
                case .attributes: attributeQuery
                case .find: attributeSearch
                }

                if let error {
                    Section { Label(error, systemImage: "exclamationmark.triangle").foregroundStyle(.red) }
                }
            }
            .autocorrectionDisabled()
            .textInputAutocapitalization(.never)
            .scrollDismissesKeyboard(.interactively)
            .navigationTitle("Query")
            .navigationDestination(for: String.self) { ThoughtDetailView(label: $0) }
            .onChange(of: mode) { error = nil }
        }
    }

    // MARK: - Attributes of a Thought

    @ViewBuilder private var attributeQuery: some View {
        Section {
            TextField("Source", text: $source)
            TextField("Link type", text: $linkType)
            TextField("Target", text: $target)
            TextField("Filter (ancestor labels)", text: $filter)
        } footer: {
            Text("Inherited links are included: if dogs have fur, so does Fido. Link type and Target narrow the results to those descendants. Use is-a with an empty source for children.")
        }
        .onSubmit(runQuery)

        HStack {
            Button("Query", systemImage: "magnifyingglass", action: runQuery)
                .disabled(source.isBlank && target.isBlank)
            Spacer()
            Button("Why?", systemImage: "questionmark.circle", action: runWhy)
                .disabled(source.isBlank)
        }
        .buttonStyle(.borderless)

        if let explanation {
            Section("Explanation") { Text(explanation) }
        }

        if let result {
            if !result.thoughts.isEmpty {
                Section("Thoughts (\(result.thoughts.count))") {
                    ForEach(result.thoughts, id: \.self) { ThoughtRow(label: $0) }
                }
            }
            Section("Links (\(result.links.count))") {
                if result.links.isEmpty && result.thoughts.isEmpty {
                    Text("No results").foregroundStyle(.secondary)
                }
                ForEach(result.links) { LinkRow(link: $0) }
            }
        }
    }

    private func runQuery() {
        do {
            result = try store.query(source: source, linkType: linkType, target: target, filter: filter)
            explanation = nil
            error = nil
        } catch {
            self.error = error.localizedDescription
            result = nil
        }
    }

    private func runWhy() {
        do {
            explanation = try store.why(source: source, linkType: linkType, target: target)
            error = nil
        } catch {
            self.error = error.localizedDescription
        }
    }

    // MARK: - Search by attributes

    @ViewBuilder private var attributeSearch: some View {
        Section {
            TextField("has,fur\ncan,bark\nis,brown,0.7", text: $attributes, axis: .vertical)
                .font(.body.monospaced())
                .lineLimit(3...8)
            TextField("Within (ancestor, default Thought)", text: $ancestor)
        } footer: {
            Text("One “link type,target[,weight]” per line. Finds the Thoughts whose links best match.")
        }
        Button("Find", systemImage: "magnifyingglass", action: runSearch)
            .disabled(attributes.isBlank)

        if let matches {
            Section("Matches (\(matches.count))") {
                if matches.isEmpty { Text("No results").foregroundStyle(.secondary) }
                ForEach(matches) { m in
                    NavigationLink(value: m.label) {
                        LabeledContent(m.label) {
                            Text(m.confidence, format: .number.precision(.fractionLength(2)))
                                .monospacedDigit()
                        }
                    }
                }
            }
        }
    }

    private func runSearch() {
        do {
            matches = try store.searchByAttributes(attributes, ancestor: ancestor)
            error = nil
        } catch {
            self.error = error.localizedDescription
            matches = nil
        }
    }
}
