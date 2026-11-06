import SwiftUI

/// An active module's screen: its title and enabled switch, and its dialog (the SwiftUI port
/// of its desktop *Dlg.xaml, in Dialogs/), which talks to the module's logic interface.
struct ModuleDetailView: View {
    @Environment(ModuleEngine.self) private var engine
    let label: String

    private var info: ModuleInfo? { engine.active.first { $0.label == label } }

    var body: some View {
        Group {
            if let info {
                ModuleDialog(type: info.type, label: label)
            } else {
                ContentUnavailableView("Module unavailable", systemImage: "exclamationmark.triangle",
                                       description: Text("\(label) is no longer active."))
            }
        }
        .navigationTitle(info.map { ModuleInfo.display($0.type) } ?? label)
        .navigationBarTitleDisplayMode(.inline)
        .toolbar {
            if let info {
                Toggle("Enabled", isOn: Binding(get: { info.enabled }, set: { engine.setEnabled(label, $0) }))
                    .toggleStyle(.switch)
                    .labelsHidden()
            }
        }
    }
}

/// The dialog for a module type.
struct ModuleDialog: View {
    let type: String
    let label: String

    var body: some View {
        switch type {
        case "ModuleText", "ModuleTextIn", "ModuleTextNEW": TextDialog(label: label)
        case "ModuleAction": ActionDialog(label: label)
        case "ModuleAgents": AgentsDialog(label: label)
        case "ModuleAddCounts", "ModuleAttributeBubble", "ModuleBalanceTree", "ModuleClassCreate", "ModuleRemoveRedundancy":
            RunOnceAgentDialog(label: label)
        case "ModuleWellBeing": WellBeingDialog(label: label)
        case "ModuleAttention": AttentionDialog(label: label)
        case "ModuleSoundOut": SoundOutDialog(label: label)
        case "ModuleSoundIn": SoundInDialog(label: label)
        case "ModuleStressTest": StressTestDialog(label: label)
        case "ModuleLearnMelody": LearnMelodyDialog(label: label)
        case "ModuleSequenceSegmenter": SequenceSegmenterDialog(label: label)
        case "ModuleAddToMentalModel": AddToMentalModelDialog(label: label)
        case "ModuleOnlineInfo": OnlineInfoDialog(label: label)
        case "ModuleVisualInput": VisualInputDialog(label: label)
        case "ModuleWord": WordDialog(label: label)
        case "ModuleAlgorithm": AlgorithmDialog(label: label)
        case "ModuleGPTInfo": GPTInfoDialog(label: label)
        case "ModuleSequenceEditor": SequenceEditorDialog(label: label)
        case "ModuleMentalModel": MentalModelDialog(label: label)
        case "ModuleShowGraph": ShowGraphDialog(label: label)
        case "ModuleVision": VisionDialog(label: label)
        case "ModuleObjectRecognizer": ObjectRecognizerDialog(label: label)
        case "ModuleEmpty": EmptyDialog(label: label)
        case "ModuleUKS": NoteDialog(note: "The UKS tree is the Knowledge tab.")
        case "ModuleUKSStatement": NoteDialog(note: "Statements are added in the Add tab.")
        case "ModuleUKSQuery": NoteDialog(note: "Queries run in the Query tab.")
        case "ModuleAttentionS": NoteDialog(note: "This module has no dialog; it runs each engine cycle.")
        default: NoteDialog(note: "This module has no dialog.")
        }
    }
}

/// For modules with nothing to show or do.
struct NoteDialog: View {
    let note: String

    var body: some View {
        List {
            Section { Text(note).font(.callout).foregroundStyle(.secondary) }
        }
    }
}
