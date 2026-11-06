import SwiftUI

// SwiftUI ports of the desktop's text-entry module dialogs. What is typed, histories and the
// editor's find/replace live here; the modules do the work (UKSNative/Host/ModuleApis.cs).

// MARK: - ModuleTextDlg (ModuleText, ModuleTextIn, ModuleTextNEW)

struct TextState: Decodable {
    let status: String?
    let busy: Bool
}

struct TextAnswer: Decodable {
    let answer: String?
    let status: String?
    let interpretation: String?
    let thought: String?
}

struct KnownAttribute: Decodable, Hashable {
    let relation: String
    let target: String
    let confidence: Double
}

struct TextDialog: View {
    @State private var module: ModuleLink<TextState>
    @State private var input = ""
    @State private var output = ""
    @State private var history: [String] = []
    /// How recent inputs were understood, newest first.
    @State private var parsed: [(entry: String, input: String, thought: String?)] = []
    @State private var aboutThought = ""
    @State private var attributes: [KnownAttribute] = []
    @State private var corpus = ""
    init(label: String) { _module = State(initialValue: ModuleLink(label)) }

    var body: some View {
        ModuleForm(module: module, status: { s in s.busy ? "Loading phrases..." : module.message ?? s.status }) { _ in
            Section {
                FieldRow(placeholder: "Say or ask something, then Return", text: $input, submit: submit)
                ControlRow {
                    WideButton("Submit", prominent: true) { submit() }
                        .disabled(input.trimmingCharacters(in: .whitespaces).isEmpty)
                    if !history.isEmpty {
                        MenuPicker(title: "History", selection: "", options: [""] + history.reversed()) { if !$0.isEmpty { input = $0 } }
                    }
                }
                LogView(title: "Answer / Status", text: output)
                if !parsed.isEmpty {
                    ItemList(title: "Interpretations",
                             items: parsed.enumerated().map { ListItem(text: $1.entry, selected: $0 == 0) }) { i in
                        input = parsed[i].input
                        if let t = parsed[i].thought { aboutThought = t }
                    }
                }
                FieldRow(placeholder: "Show known attributes of…", text: $aboutThought)
                ItemList(title: "Known attributes",
                         items: attributes.map { ListItem(text: "\($0.relation) \($0.target)", fraction: $0.confidence) })
                FileRow(title: "Corpus", path: corpus) { corpus = $0; module.call("cancelLoad") }
                WideButton("Load") { module.clearMessage(); module.call("load", ["path": corpus]) }
                    .disabled(corpus.isEmpty)
            }
        }
        .onChange(of: aboutThought) { lookUp() }
    }

    private func submit() {
        let phrase = input.trimmingCharacters(in: .whitespaces)
        guard !phrase.isEmpty else { return }
        history.removeAll { $0 == phrase }
        history.append(phrase)
        guard let answer = module.perform(TextAnswer.self, "submit", ["text": phrase]) else { return }
        output = (answer.answer?.trimmingCharacters(in: .whitespaces).isEmpty == false ? answer.answer : answer.status) ?? ""
        if let entry = answer.interpretation, parsed.first?.entry != entry {
            parsed.insert((entry, phrase, answer.thought), at: 0)
            if parsed.count > 6 { parsed.removeLast(parsed.count - 6) }
        }
        module.show(answer.status)
        lookUp()
    }

    private func lookUp() {
        attributes = module.query([KnownAttribute].self, "attributes", ["thought": aboutThought]) ?? []
    }
}

// MARK: - ModuleWordDlg

struct StatusState: Decodable {
    let status: String?
    let busy: Bool
}

struct WordDialog: View {
    @State private var module: ModuleLink<StatusState>
    @State private var word = ""
    @State private var suggestion: [String] = []
    @State private var file = ""
    init(label: String) { _module = State(initialValue: ModuleLink(label)) }

    var body: some View {
        ModuleForm(module: module, status: { s in s.busy ? "Loading words..." : module.message ?? s.status }) { _ in
            Section("Add Word Spelling") {
                FieldRow(placeholder: "Word", text: $word, suggestions: suggestion, submit: add)
                WideButton("Add", prominent: true) { add() }
                    .disabled(word.trimmingCharacters(in: .whitespaces).isEmpty)
            }
            Section("Load from File") {
                FileRow(title: "File", path: file) { file = $0 }
                WideButton("Load") { module.clearMessage(); module.call("load", ["path": file]) }
                    .disabled(file.isEmpty)
            }
        }
        .onChange(of: word) {
            // the module hears each letter as it is typed
            module.call("typed", ["text": word])
            suggestion = module.query([String].self, "suggest", ["prefix": word]) ?? []
        }
    }

    private func add() { module.call("add", ["word": word]) }
}

// MARK: - ModuleOnlineInfoDlg

struct OutputState: Decodable {
    let output: String?
}

struct OnlineInfoDialog: View {
    @State private var module: ModuleLink<OutputState>
    @State private var source = "ChatGPT"
    @State private var link = "is-a"
    @State private var input = ""
    init(label: String) { _module = State(initialValue: ModuleLink(label)) }

    private static let sources = ["ChatGPT", "CSKG", "ConceptNet", "WikiData", "Wiktionary", "Free Dictionary",
                                  "Webster's Elementary", "Kid's Definition", "Oxford Word List"]

    var body: some View {
        ModuleForm(module: module, note: "ChatGPT needs an OpenAI API key in Settings.") { s in
            Section {
                ControlRow {
                    MenuPicker(title: "Source", selection: source, options: Self.sources) { source = $0 }
                    MenuPicker(title: "Link", selection: link, options: ["is-a", "hasa"]) { link = $0 }
                }
                FieldRow(placeholder: "Word or question, then Return", text: $input, submit: lookUp)
                ControlRow {
                    WideButton("Look Up", prominent: true) { lookUp() }
                        .disabled(input.isEmpty)
                    WideButton("Clear") { module.call("clear") }
                }
                LogView(title: "Output", text: s.output ?? "")
            }
        }
    }

    private func lookUp() { module.call("lookup", ["source": source, "link": link, "text": input]) }
}

// MARK: - ModuleGPTInfoDlg

struct GPTInfoState: Decodable {
    let output: String?
    let tokensUsed: Int
    let unknownCount: Int
}

struct GPTInfoDialog: View {
    @State private var module: ModuleLink<GPTInfoState>
    @State private var request = ""
    @State private var wordFile = ""
    init(label: String) { _module = State(initialValue: ModuleLink(label)) }

    var body: some View {
        ModuleForm(module: module, note: "Needs an OpenAI API key in Settings. Return requests info; Parents requests parents only.",
                   status: { s in "\(s.tokensUsed) tokens used.  \(s.unknownCount) unknown Thoughts." }) { s in
            Section {
                FieldRow(placeholder: "GPT Request", text: $request) { module.call("info", ["text": request]) }
                ControlRow {
                    WideButton("Info", prominent: true) { module.call("info", ["text": request]) }
                        .disabled(request.isEmpty)
                    WideButton("Parents") { module.call("parents", ["text": request]) }
                        .disabled(request.isEmpty)
                }
                LogView(title: "Output", text: s.output ?? "")
                ControlRow {
                    WideButton("Re-Parse") { module.call("reparse", ["text": request]) }
                    WideButton("Handle Unknowns") { module.call("unknowns") }
                }
                ControlRow {
                    WideButton("Verify All Parents") { module.call("verify") }
                    WideButton("Add Clauses To All") { module.call("clauses") }
                }
                ControlRow {
                    WideButton("Solve Ambiguity") { module.call("ambiguity") }
                    WideButton("Remove Duplicates") { module.call("duplicates") }
                }
                FileRow(title: "Word File", path: wordFile) { wordFile = $0 }
                ControlRow {
                    WideButton("Load Word File") { module.call("loadWords", ["path": wordFile]) }
                        .disabled(wordFile.isEmpty)
                    WideButton("Ambiguity File") { module.call("loadAmbiguity", ["path": wordFile]) }
                        .disabled(wordFile.isEmpty)
                }
                WideButton("Clear", role: .destructive) { module.call("clear") }
            }
        }
    }
}

// MARK: - ModuleSequenceSegmenterDlg

struct JobState: Decodable {
    let status: String?
    let busy: Bool
    let trace: String?
}

struct SequenceSegmenterDialog: View {
    @State private var module: ModuleLink<JobState>
    @State private var line = ""
    @State private var corpus = ""
    init(label: String) { _module = State(initialValue: ModuleLink(label)) }

    var body: some View {
        ModuleForm(module: module, status: { s in s.busy ? "Loading sequences..." : module.message ?? s.status }) { s in
            Section {
                FieldRow(placeholder: "Unsegmented line (or \"exp\")", text: $line, submit: submit)
                WideButton("Submit", prominent: true) { submit() }
                    .disabled(line.isEmpty)
                FileRow(title: "Corpus", path: corpus) { corpus = $0; module.call("cancelLoad") }
                WideButton("Load") { module.clearMessage(); module.call("load", ["path": corpus]) }
                    .disabled(corpus.isEmpty)
                LogView(title: "Trace", text: s.trace ?? "")
            }
        }
    }

    private func submit() {
        // "exp" runs the desktop's vocabulary experiment
        if line == "exp" { module.call("experiment") } else { module.call("submit", ["line": line]) }
    }
}

// MARK: - ModuleAlgorithmDlg

struct AlgorithmState: Decodable {
    let lastAction: String?
    let tasks: [String]
    let canStep: Bool
}

struct AlgorithmDialog: View {
    @State private var module: ModuleLink<AlgorithmState>
    @State private var task = ""
    @State private var param1 = ""
    @State private var param2 = ""
    init(label: String) { _module = State(initialValue: ModuleLink(label)) }

    var body: some View {
        ModuleForm(module: module, status: { s in
            if let action = s.lastAction, !action.isEmpty { return action }
            return module.message
        }) { s in
            Section {
                FieldRow(placeholder: "Task", text: $task, suggestions: completions(of: task, in: s.tasks))
                ControlRow {
                    FieldRow(placeholder: "Param 1", text: $param1)
                    FieldRow(placeholder: "Param 2", text: $param2) { run("execute") }
                }
                ControlRow {
                    WideButton("Execute", prominent: true) { run("execute") }
                    WideButton("Start") { run("start") }
                    WideButton("Single Step") { module.call("step") }
                        .disabled(!s.canStep)
                }
            }
        }
    }

    private func run(_ op: String) { module.call(op, ["task": task, "param1": param1, "param2": param2]) }
}

// MARK: - ModuleSequenceEditorDlg

struct SequenceEditorState: Decodable {
    let tasks: [String]
    let executing: String?
}

struct TaskText: Decodable {
    let content: String
    let message: String?
}

struct SequenceEditorDialog: View {
    @State private var module: ModuleLink<SequenceEditorState>
    @State private var task = ""
    @State private var content = ""
    @State private var search = ""
    @State private var replacement = ""
    @State private var undoContent: String?
    init(label: String) { _module = State(initialValue: ModuleLink(label)) }

    var body: some View {
        ModuleForm(module: module) { s in
            Section {
                FieldRow(placeholder: "Task name, then Return to load", text: $task,
                         suggestions: completions(of: task, in: s.tasks), submit: load)
                ControlRow {
                    WideButton("Load") { load() }
                        .disabled(task.trimmingCharacters(in: .whitespaces).isEmpty)
                    WideButton("Save", prominent: true) { module.call("save", ["task": task, "content": content]) }
                    WideButton("Undo") { undo() }
                        .disabled(undoContent == nil)
                }
                if let executing = s.executing { TextRow("Executing", executing) }
                EditorRow(title: "Content", text: $content)
                ControlRow {
                    FieldRow(placeholder: "Search", text: $search)
                    FieldRow(placeholder: "Replace", text: $replacement)
                }
                WideButton("Replace All") { replaceAll() }
            }
        }
    }

    private func load() {
        guard let text = module.perform(TaskText.self, "load", ["task": task]) else { content = ""; return }
        content = text.content
        module.show(text.message)
    }

    /// Replaces every occurrence of the search text (ignoring case); undoable once.
    private func replaceAll() {
        let find = search.trimmingCharacters(in: .whitespaces), with = replacement.trimmingCharacters(in: .whitespaces)
        guard !find.isEmpty else { module.show("Search pattern is empty"); return }
        guard !with.isEmpty else { module.show("Replace pattern is empty"); return }
        guard !content.isEmpty else { module.show("No content to replace"); return }
        var count = 0
        var result = ""
        var rest = content[...]
        while let range = rest.range(of: find, options: .caseInsensitive) {
            result += rest[..<range.lowerBound] + with
            rest = rest[range.upperBound...]
            count += 1
        }
        undoContent = content
        content = result + rest
        module.show("Replaced \(count) occurrence(s) of '\(find)' with '\(with)'")
    }

    private func undo() {
        guard let previous = undoContent else { module.show("Nothing to undo"); return }
        content = previous
        undoContent = nil
        module.show("Undo successful")
    }
}

// MARK: - ModuleAddToMentalModelDlg

struct AddToMentalModelState: Decodable {
    let history: [String]
}

struct AddToMentalModelDialog: View {
    @State private var module: ModuleLink<AddToMentalModelState>
    @State private var thought = ""
    @State private var suggestions: [String] = []
    @State private var azimuth = "0"
    @State private var elevation = "0"
    @State private var distance = "1"
    init(label: String) { _module = State(initialValue: ModuleLink(label)) }

    var body: some View {
        ModuleForm(module: module) { s in
            Section {
                FieldRow(placeholder: "Thought", text: $thought, suggestions: suggestions)
                if !s.history.isEmpty {
                    MenuPicker(title: "Recent", selection: "", options: [""] + s.history) { if !$0.isEmpty { thought = $0 } }
                }
                ControlRow {
                    FieldRow(placeholder: "Az°", text: $azimuth, numeric: true)
                    FieldRow(placeholder: "Elev°", text: $elevation, numeric: true)
                    FieldRow(placeholder: "Dist", text: $distance, numeric: true)
                }
                WideButton("OK", prominent: true) {
                    module.call("add", ["thought": thought, "azimuth": Double(azimuth) ?? 0,
                                        "elevation": Double(elevation) ?? 0, "distance": Double(distance) ?? 1])
                }
                .disabled(thought.trimmingCharacters(in: .whitespaces).isEmpty)
            }
        }
        .onChange(of: thought) { suggestions = module.query([String].self, "complete", ["prefix": thought]) ?? [] }
    }
}
