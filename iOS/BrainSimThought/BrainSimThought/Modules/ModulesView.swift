import SwiftUI

/// The desktop main window's module side: the active-module list (with the context menu's
/// enable/initialize/remove), "add module", and the engine that fires them every cycle.
struct ModulesView: View {
    @Environment(ModuleEngine.self) private var engine
    @State private var error: String?
    @State private var showSettings = false
    @State private var showAdd = false
    @State private var path: [String] = []

    var body: some View {
        @Bindable var engine = engine
        NavigationStack(path: $path) {
            List {
                Section {
                    Toggle(isOn: $engine.running) {
                        Label("Engine", systemImage: engine.running ? "play.circle.fill" : "pause.circle")
                    }
                    Picker("Cycle", selection: $engine.intervalMs) {
                        Text("10 ms").tag(10)
                        Text("50 ms").tag(50)
                        Text("100 ms").tag(100)
                        Text("500 ms").tag(500)
                    }
                } footer: {
                    Text("\(engine.ticks) cycles. Every enabled module fires once per cycle, as on the desktop.")
                }

                Section("Active Modules") {
                    ForEach(engine.active) { module in
                        NavigationLink(value: module.label) {
                            ModuleRow(module: module)
                        }
                        .swipeActions {
                            Button("Remove", role: .destructive) { run { try engine.remove(module.label) } }
                        }
                        .contextMenu {
                            Button(module.enabled ? "Disable" : "Enable", systemImage: module.enabled ? "pause" : "play") {
                                engine.setEnabled(module.label, !module.enabled)
                            }
                            Button("Initialize", systemImage: "arrow.counterclockwise") { run { try engine.initialize(module.label) } }
                            Button("Remove", systemImage: "trash", role: .destructive) { run { try engine.remove(module.label) } }
                        }
                    }
                }
            }
            .navigationTitle("Modules")
            .navigationDestination(for: String.self) { ModuleDetailView(label: $0) }
            .toolbar {
                ToolbarItem(placement: .topBarLeading) {
                    Button("Settings", systemImage: "gearshape") { showSettings = true }
                }
                ToolbarItem(placement: .topBarTrailing) {
                    Button("Add Module", systemImage: "plus") { showAdd = true }
                }
            }
            .sheet(isPresented: $showSettings) { ModuleSettingsView() }
            .sheet(isPresented: $showAdd) {
                AddModuleSheet { type in
                    showAdd = false
                    run { path.append(try engine.activate(type)) }
                }
            }
            .alert("Modules", isPresented: .constant(error != nil), presenting: error) { _ in
                Button("OK") { error = nil }
            } message: { Text($0) }
        }
    }

    private func run(_ action: () throws -> Void) {
        do { try action() } catch { self.error = error.localizedDescription }
    }
}

private struct ModuleRow: View {
    let module: ModuleInfo

    var body: some View {
        HStack {
            Image(systemName: icon)
                .foregroundStyle(module.enabled ? Color.accentColor : .secondary)
                .frame(width: 28)
            VStack(alignment: .leading) {
                Text(module.displayType)
                Text(module.label).font(.caption).foregroundStyle(.secondary)
            }
            Spacer()
            if !module.enabled {
                Text("Disabled").font(.caption).foregroundStyle(.secondary)
            }
        }
    }

    private var icon: String {
        switch module.type {
        case "ModuleUKS", "ModuleShowGraph": "point.3.connected.trianglepath.dotted"
        case "ModuleUKSQuery": "questionmark.bubble"
        case "ModuleUKSStatement": "plus.bubble"
        case "ModuleMentalModel", "ModuleAddToMentalModel": "globe"
        case "ModuleAttention", "ModuleAttentionS": "scope"
        case "ModuleVision", "ModuleVisualInput": "eye"
        case "ModuleObjectRecognizer": "camera.viewfinder"
        case "ModuleSoundIn", "ModuleSoundOut", "ModuleLearnMelody": "music.note"
        case "ModuleText", "ModuleTextIn", "ModuleTextNEW", "ModuleWord", "ModuleSequenceSegmenter": "text.bubble"
        case "ModuleGPTInfo", "ModuleOnlineInfo": "network"
        case "ModuleAlgorithm", "ModuleSequenceEditor": "list.number"
        case "ModuleWellBeing": "heart"
        case "ModuleAction": "bolt"
        default: "cpu"
        }
    }
}

/// The desktop's module type list ("AvailableModule").
private struct AddModuleSheet: View {
    @Environment(ModuleEngine.self) private var engine
    @Environment(\.dismiss) private var dismiss
    let add: (String) -> Void
    @State private var search = ""

    var body: some View {
        NavigationStack {
            List(engine.available.filter { search.isEmpty || ModuleInfo.display($0).localizedCaseInsensitiveContains(search) },
                 id: \.self) { type in
                Button {
                    add(type)
                } label: {
                    HStack {
                        Text(ModuleInfo.display(type))
                        Spacer()
                        let count = engine.active.filter { $0.type == type }.count
                        if count > 0 { Text("\(count) active").font(.caption).foregroundStyle(.secondary) }
                    }
                }
                .foregroundStyle(.primary)
            }
            .searchable(text: $search, placement: .navigationBarDrawer(displayMode: .always))
            .navigationTitle("Add Module")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("Cancel") { dismiss() } }
            }
        }
    }
}

/// app.config settings used by the modules.
struct ModuleSettingsView: View {
    @Environment(\.dismiss) private var dismiss
    @State private var apiKey = Keychain.read(ModuleEngine.apiKeySetting) ?? ""

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    SecureField("sk-…", text: $apiKey)
                        .textInputAutocapitalization(.never)
                        .autocorrectionDisabled()
                } header: {
                    Text("OpenAI API Key")
                } footer: {
                    Text("Used by the GPT Info and Online Info (ChatGPT) modules, like APIKey in the desktop app.config. Stored in the Keychain.")
                }
            }
            .navigationTitle("Settings")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .confirmationAction) {
                    Button("Done") {
                        let key = apiKey.trimmingCharacters(in: .whitespacesAndNewlines)
                        Keychain.write(ModuleEngine.apiKeySetting, key)
                        ModuleEngine.setSetting(ModuleEngine.apiKeySetting, key)
                        dismiss()
                    }
                }
            }
        }
    }
}

extension View {
    /// Shows module MessageBox-style notices one at a time.
    func moduleNotices(_ engine: ModuleEngine) -> some View {
        modifier(ModuleNoticeModifier(engine: engine))
    }
}

private struct ModuleNoticeModifier: ViewModifier {
    @Bindable var engine: ModuleEngine

    func body(content: Content) -> some View {
        let notice = engine.notices.first
        content.alert(notice?.title.isEmpty == false ? notice!.title : "Brain Simulator",
                      isPresented: Binding(get: { notice != nil }, set: { if !$0, !engine.notices.isEmpty { engine.notices.removeFirst() } }),
                      presenting: notice) { _ in
            Button("OK") {}
        } message: { Text($0.text) }
    }
}
