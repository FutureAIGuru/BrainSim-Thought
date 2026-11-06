import SwiftUI

// ModuleVisualInputDlg: present an observation (or imagine a word) at a distance, and step
// through a lesson. The module keeps the files, lesson and timer (ModuleApis.cs).

struct VisualInputState: Decodable {
    let observationFiles: [String]
    let selectedObservation: String?
    let lessonFiles: [String]
    let selectedLesson: String?
    let stepIntervalSeconds: Double
    let lessonRunning: Bool
    let lessonSteps: [String]
    let nextStep: Int
    let currentWord: String?
    let status: String?
}

struct VisualInputDialog: View {
    @State private var module: ModuleLink<VisualInputState>
    @State private var distance = "1"
    /// The word typed, or nil to show the module's current word.
    @State private var word: String?
    init(label: String) { _module = State(initialValue: ModuleLink(label)) }

    private static let distances = (1...10).map(String.init)
    private static let intervals = [".1", ".25", ".5", "1", "2"]

    var body: some View {
        ModuleForm(module: module, status: { s in module.message ?? s.status }) { s in
            let typed = Binding(get: { word ?? s.currentWord ?? "" }, set: { word = $0 })
            Section("Observation") {
                MenuPicker(title: "Observation", selection: Self.match(s.selectedObservation, in: s.observationFiles),
                           options: s.observationFiles) { file in
                    if module.call("selectObservation", ["file": file]) { present() }
                }
                MenuPicker(title: "Distance", selection: distance, options: Self.distances) { distance = $0 }
                ControlRow {
                    WideButton("Present", prominent: true) { present() }
                    WideButton("Clear") { module.call("clear") }
                }
                FieldRow(placeholder: "Word", text: typed) { imagine(typed.wrappedValue) }
                WideButton("Imagine") { imagine(typed.wrappedValue) }
                    .disabled(typed.wrappedValue.isEmpty)
            }
            Section("Lesson") {
                MenuPicker(title: "Lesson", selection: Self.match(s.selectedLesson, in: s.lessonFiles), options: s.lessonFiles) {
                    module.call("selectLesson", ["file": $0])
                }
                MenuPicker(title: "Seconds", selection: Self.format(s.stepIntervalSeconds), options: Self.intervals) {
                    if let seconds = Double($0), seconds > 0 { module.call("setInterval", ["seconds": seconds]) }
                }
                ControlRow {
                    WideButton("Step") { module.call("step") }
                    WideButton(s.lessonRunning ? "Pause" : "Run", prominent: true) { module.call(s.lessonRunning ? "pause" : "run") }
                    WideButton("Reset") { module.call("reset") }
                }
                let next = min(s.nextStep, max(0, s.lessonSteps.count - 1))
                ItemList(title: "Steps", items: s.lessonSteps.enumerated().map { ListItem(text: $1, selected: $0 == next) })
            }
        }
    }

    private var distanceValue: Double { Double(distance) ?? 1 }

    private func present() { module.call("present", ["distance": distanceValue]) }

    private func imagine(_ text: String) { module.call("imagine", ["word": text, "distance": distanceValue]) }

    /// The selected file as listed (case-insensitively), else the first.
    private static func match(_ selected: String?, in files: [String]) -> String {
        files.first { $0.caseInsensitiveCompare(selected ?? "") == .orderedSame } ?? files.first ?? ""
    }

    /// The interval as one of the menu's choices, if it is one.
    private static func format(_ seconds: Double) -> String {
        intervals.first { abs((Double($0) ?? -1) - seconds) < 0.001 } ?? seconds.formatted(.number.grouping(.never))
    }
}
