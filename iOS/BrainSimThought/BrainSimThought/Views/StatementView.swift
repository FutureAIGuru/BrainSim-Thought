import SwiftUI

/// Adds knowledge, like the desktop Statement dialog and the UKS text-file format.
struct StatementView: View {
    @Environment(UKSStore.self) private var store

    enum Mode: String, CaseIterable { case statement = "Statement", text = "Text line" }

    @State private var mode = Mode.statement
    @State private var from = ""
    @State private var linkType = ""
    @State private var to = ""
    @State private var setConfidence = false
    @State private var confidence: Float = 0.9
    @State private var line = ""
    @State private var log: [(ok: Bool, text: String)] = []
    @FocusState private var focus: Field?

    enum Field { case from, linkType, to, line }

    var body: some View {
        NavigationStack {
            Form {
                Picker("Mode", selection: $mode) {
                    ForEach(Mode.allCases, id: \.self) { Text($0.rawValue) }
                }
                .pickerStyle(.segmented)
                .listRowBackground(Color.clear)

                switch mode {
                case .statement:
                    Section {
                        TextField("From (e.g. Fido, big brown dog)", text: $from)
                            .focused($focus, equals: .from)
                            .submitLabel(.next)
                            .onSubmit { focus = .linkType }
                        TextField("Link type (e.g. is-a, has 4, can, IF)", text: $linkType)
                            .focused($focus, equals: .linkType)
                            .submitLabel(.next)
                            .onSubmit { focus = .to }
                        TextField("To (e.g. dog, legs)", text: $to)
                            .focused($focus, equals: .to)
                            .submitLabel(.done)
                            .onSubmit(addStatement)
                    } footer: {
                        Text("Multi-word phrases become attributed subclasses and plurals are singularized: “dogs have 4 legs” adds dog → has → leg.4. Three words in From or To make a nested Link: “Fido play outside” IF “weather is sunny”. Start To with ^ for a sequence: ^c a t.")
                    }
                    Section {
                        Toggle("Set confidence", isOn: $setConfidence)
                        if setConfidence {
                            LabeledContent("Confidence") {
                                Slider(value: $confidence, in: 0.05...1)
                                Text(confidence, format: .number.precision(.fractionLength(2)))
                                    .monospacedDigit()
                            }
                        }
                    }
                    Button("Add Statement", systemImage: "plus.circle.fill", action: addStatement)
                        .disabled(from.isBlank || linkType.isBlank)
                case .text:
                    Section {
                        TextField("[Fido->is-a->dog] 1.00", text: $line, axis: .vertical)
                            .focused($focus, equals: .line)
                            .font(.body.monospaced())
                            .lineLimit(2...5)
                    } footer: {
                        Text("UKS text format: optional label, then [From->LinkType->To] and an optional weight. Links can nest: R1 [[Fido->play.?->outside]->IF->[weather->is.?->sunny]].")
                    }
                    Button("Add", systemImage: "plus.circle.fill", action: addLine)
                        .disabled(line.isBlank)
                }

                if !log.isEmpty {
                    Section("Recent") {
                        ForEach(Array(log.enumerated().reversed()), id: \.offset) { _, entry in
                            Label(entry.text, systemImage: entry.ok ? "checkmark.circle" : "exclamationmark.triangle")
                                .foregroundStyle(entry.ok ? Color.primary : Color.red)
                                .font(.callout)
                        }
                    }
                }
            }
            .autocorrectionDisabled()
            .textInputAutocapitalization(.never)
            .scrollDismissesKeyboard(.interactively)
            .navigationTitle("Add Knowledge")
        }
    }

    private func addStatement() {
        guard !from.isBlank, !linkType.isBlank else { return }
        do {
            let result = try store.addStatement(from: from, linkType: linkType, to: to,
                                                weight: setConfidence ? confidence : 1)
            record(true, result.link?.text ?? result.message ?? "Added")
            from = ""; linkType = ""; to = ""
            focus = .from
        } catch {
            record(false, error.localizedDescription)
        }
    }

    private func addLine() {
        do {
            let result = try store.addLine(line)
            record(true, result.link?.text ?? result.message ?? line)
            line = ""
        } catch {
            record(false, error.localizedDescription)
        }
    }

    private func record(_ ok: Bool, _ text: String) {
        log.append((ok, text))
        if log.count > 20 { log.removeFirst() }
    }
}

extension String {
    var isBlank: Bool { trimmingCharacters(in: .whitespaces).isEmpty }
}
