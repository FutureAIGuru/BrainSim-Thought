import SwiftUI

// SwiftUI ports of the smaller desktop module dialogs. Each reads its module's state
// (UKSNative/Host/ModuleApis.cs) and calls its operations; the layout is all here.

// MARK: - ModuleActionDlg

struct ActionState: Decodable {
    let lastContext: String?
    let lastAction: String?
}

struct ActionDialog: View {
    @State private var module: ModuleLink<ActionState>
    init(label: String) { _module = State(initialValue: ModuleLink(label)) }

    var body: some View {
        ModuleForm(module: module) { s in
            Section {
                TextRow("Last Context", s.lastContext ?? "(none)")
                TextRow("Last Action", s.lastAction ?? "(none)")
            }
        }
    }
}

// MARK: - ModuleAgentsDlg

struct AgentsState: Decodable {
    let agents: [String]
    let status: String?
}

struct AgentsDialog: View {
    @State private var module: ModuleLink<AgentsState>
    init(label: String) { _module = State(initialValue: ModuleLink(label)) }

    var body: some View {
        ModuleForm(module: module) { s in
            Section {
                if !s.agents.isEmpty {
                    ControlRow {
                        ForEach(s.agents, id: \.self) { agent in
                            WideButton(agent) { module.call("run", ["agent": agent]) }
                        }
                    }
                }
                LogView(title: "Status", text: s.status ?? "")
            }
        }
    }
}

// MARK: - ModuleAddCountsDlg, ModuleAttributeBubbleDlg, ModuleBalanceTreeDlg, ModuleClassCreateDlg, ModuleRemoveRedundancyDlg

struct LogState: Decodable {
    let log: String?
}

struct RunOnceAgentDialog: View {
    @State private var module: ModuleLink<LogState>
    init(label: String) { _module = State(initialValue: ModuleLink(label)) }

    var body: some View {
        ModuleForm(module: module) { s in
            Section {
                WideButton("Run Once", prominent: true) { module.call("run") }
                LogView(title: "Messages", text: s.log ?? "")
            }
        }
    }
}

// MARK: - ModuleWellBeingDlg

struct WellBeingState: Decodable {
    let value: Float
}

struct WellBeingDialog: View {
    @State private var module: ModuleLink<WellBeingState>
    init(label: String) { _module = State(initialValue: ModuleLink(label)) }

    var body: some View {
        ModuleForm(module: module) { s in
            Section {
                SliderRow(title: "Well-being", value: Double(s.value), range: -1...1) { module.call("set", ["value": $0]) }
                ControlRow {
                    WideButton("−") { module.call("decrease") }
                    WideButton("+") { module.call("increase") }
                }
            }
        }
    }
}

// MARK: - ModuleAttentionDlg

struct AttentionState: Decodable {
    let azimuth: Double
    let elevation: Double
    let focus: String?
}

struct AttentionDialog: View {
    @State private var module: ModuleLink<AttentionState>
    init(label: String) { _module = State(initialValue: ModuleLink(label)) }

    var body: some View {
        ModuleForm(module: module) { s in
            Section {
                SliderRow(title: "Horizontal", value: s.azimuth, range: -180...180, step: 1) {
                    module.call("setCenter", ["azimuth": $0])
                }
                SliderRow(title: "Vertical", value: s.elevation, range: -90...90, step: 1) {
                    module.call("setCenter", ["elevation": $0])
                }
                TextRow("Current Focus", s.focus ?? "(none)")
            }
        }
    }
}

// MARK: - ModuleSoundOutDlg

struct SoundOutState: Decodable {
    let cadence: Int
    let pitchOffset: Int
}

struct SoundOutDialog: View {
    @State private var module: ModuleLink<SoundOutState>
    init(label: String) { _module = State(initialValue: ModuleLink(label)) }

    private static let cadences = ["50", "75", "100", "125", "150", "200"]
    private static let offsets = ["-12", "-6", "0", "6", "12"]

    var body: some View {
        ModuleForm(module: module) { s in
            Section {
                WideButton("Play Sound", prominent: true) { module.call("play") }
                MenuPicker(title: "Cadence", selection: String(s.cadence), options: Self.cadences) {
                    if let v = Int($0) { module.call("setCadence", ["value": v]) }
                }
                MenuPicker(title: "Pitch Offset", selection: String(s.pitchOffset), options: Self.offsets) {
                    if let v = Int($0) { module.call("setPitchOffset", ["value": v]) }
                }
            }
        }
    }
}

// MARK: - ModuleSoundInDlg

struct SoundInState: Decodable {
    let phrases: [String]
}

struct SoundInDialog: View {
    @State private var module: ModuleLink<SoundInState>
    @State private var phrase = ""
    init(label: String) { _module = State(initialValue: ModuleLink(label)) }

    var body: some View {
        ModuleForm(module: module) { s in
            Section {
                FieldRow(placeholder: "Musical phrase to play", text: $phrase,
                         suggestions: completions(of: phrase, in: s.phrases), submit: play)
                WideButton("Play Phrase") { play() }
                    .disabled(phrase.trimmingCharacters(in: .whitespaces).isEmpty)
                PianoKeyboard(low: 60, high: 76) { note, down in
                    module.call(down ? "noteDown" : "noteUp", ["note": note])
                }
            }
        }
    }

    private func play() {
        guard !phrase.trimmingCharacters(in: .whitespaces).isEmpty else { return }
        module.call("playPhrase", ["phrase": phrase])
    }
}

// MARK: - ModuleStressTestDlg

struct EmptyState: Decodable {}

struct StressTestDialog: View {
    @State private var module: ModuleLink<EmptyState>
    @State private var count = ""
    @State private var output = ""
    init(label: String) { _module = State(initialValue: ModuleLink(label)) }

    var body: some View {
        ModuleForm(module: module, note: "Used to stress test by adding toy examples. Enter the number of examples to add.",
                   status: { _ in nil }) { _ in
            Section {
                FieldRow(placeholder: "Count", text: $count, numeric: true, submit: add)
                WideButton("Add Items", prominent: true) { add() }
                LogView(title: "Output", text: output)
            }
        }
    }

    private func add() {
        guard let n = Int(count.trimmingCharacters(in: .whitespaces)) else {
            output = "Error! You must provide an integer to run."
            return
        }
        if module.call("add", ["count": n]), let result = module.message { output = result; module.clearMessage() }
    }
}

// MARK: - ModuleLearnMelodyDlg

struct LearnMelodyDialog: View {
    @State private var module: ModuleLink<EmptyState>
    @State private var path = ""
    init(label: String) { _module = State(initialValue: ModuleLink(label)) }

    var body: some View {
        ModuleForm(module: module) { _ in
            Section {
                FileRow(title: "Melody File", path: path) { path = $0 }
                WideButton("Load") { module.call("load", ["path": path]) }
                    .disabled(path.isEmpty)
            }
        }
    }
}

// MARK: - ModuleEmptyDlg

struct EmptyDialog: View {
    @State private var module: ModuleLink<EmptyState>
    @State private var text = ""
    init(label: String) { _module = State(initialValue: ModuleLink(label)) }

    var body: some View {
        ModuleForm(module: module, note: "Template module: copy it to start a new one.") { _ in
            Section {
                FieldRow(placeholder: "The Label", text: $text)
            }
        }
    }
}
