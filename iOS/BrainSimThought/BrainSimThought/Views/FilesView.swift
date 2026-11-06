import SwiftUI
import UniformTypeIdentifiers

/// Load/save UKS XML (same format as the desktop app) and import/export the text format.
struct FilesView: View {
    @Environment(UKSStore.self) private var store

    @State private var savedFiles: [URL] = []
    @State private var saveName = "MyKnowledge"
    @State private var importing = false
    @State private var confirmReset = false
    @State private var message: (ok: Bool, text: String)?
    @State private var exportURL: URL?

    var body: some View {
        NavigationStack {
            List {
                if let message {
                    Label(message.text, systemImage: message.ok ? "checkmark.circle" : "exclamationmark.triangle")
                        .foregroundStyle(message.ok ? Color.primary : Color.red)
                }

                Section {
                    HStack {
                        TextField("File name", text: $saveName)
                            .autocorrectionDisabled()
                            .textInputAutocapitalization(.never)
                        Button("Save") { save() }
                            .disabled(saveName.isBlank)
                    }
                    Button("Export as Text", systemImage: "text.alignleft") { exportText() }
                        .disabled(saveName.isBlank)
                    if let exportURL {
                        ShareLink(item: exportURL) { Label("Share \(exportURL.lastPathComponent)", systemImage: "square.and.arrow.up") }
                    }
                } header: {
                    Text("Save")
                } footer: {
                    Text("XML files are interchangeable with the desktop app. Text export writes everything under Thought in the [From->LinkType->To] format.")
                }

                Section("Your files") {
                    if savedFiles.isEmpty {
                        Text("No saved files yet").foregroundStyle(.secondary)
                    }
                    ForEach(savedFiles, id: \.self) { url in
                        fileRow(url)
                    }
                    .onDelete { offsets in
                        for i in offsets { try? FileManager.default.removeItem(at: savedFiles[i]) }
                        refresh()
                    }
                }

                Section("Samples from Brain Simulator Thought") {
                    ForEach(store.sampleFiles, id: \.self) { fileRow($0) }
                }

                Section {
                    Button("Open XML or Import Text File…", systemImage: "square.and.arrow.down") { importing = true }
                    Button("Reset to Initial Structure", systemImage: "arrow.counterclockwise", role: .destructive) {
                        confirmReset = true
                    }
                } footer: {
                    Text("\(store.thoughtCount) Thoughts. Changes are autosaved and restored on launch.")
                }
            }
            .navigationTitle("Files")
            .onAppear(perform: refresh)
            .fileImporter(isPresented: $importing, allowedContentTypes: [.xml, .plainText, .text]) { result in
                importFile(result)
            }
            .confirmationDialog("Clear the UKS back to its initial structure?", isPresented: $confirmReset, titleVisibility: .visible) {
                Button("Reset", role: .destructive) {
                    store.reset()
                    message = (true, "Reset: \(store.thoughtCount) Thoughts")
                }
            }
        }
    }

    private func fileRow(_ url: URL) -> some View {
        let isXML = url.pathExtension.lowercased() == "xml"
        return Menu {
            if isXML {
                Button("Open (replaces current)", systemImage: "doc") { load(url) }
            } else {
                Button("Import into current", systemImage: "arrow.triangle.merge") { load(url) }
            }
        } label: {
            Label(url.deletingPathExtension().lastPathComponent, systemImage: isXML ? "doc.text" : "text.alignleft")
        }
    }

    private func refresh() {
        let docs = (try? FileManager.default.contentsOfDirectory(at: UKSStore.documents, includingPropertiesForKeys: nil)) ?? []
        savedFiles = docs.filter { ["xml", "txt"].contains($0.pathExtension) && $0.lastPathComponent != "Autosave.xml" }
            .sorted { $0.lastPathComponent < $1.lastPathComponent }
    }

    private func save() {
        let url = UKSStore.documents.appending(path: saveName.trimmingCharacters(in: .whitespaces) + ".xml")
        do {
            try store.saveXML(to: url)
            exportURL = url
            message = (true, "Saved \(url.lastPathComponent)")
        } catch {
            message = (false, error.localizedDescription)
        }
        refresh()
    }

    private func exportText() {
        let url = UKSStore.documents.appending(path: saveName.trimmingCharacters(in: .whitespaces) + ".txt")
        do {
            try store.exportText(root: "Thought", to: url)
            exportURL = url
            message = (true, "Exported \(url.lastPathComponent)")
        } catch {
            message = (false, error.localizedDescription)
        }
        refresh()
    }

    private func load(_ url: URL) {
        do {
            let isXML = url.pathExtension.lowercased() == "xml"
            if isXML { try store.loadXML(from: url) } else { try store.importText(from: url) }
            message = (true, "\(isXML ? "Opened" : "Imported") \(url.lastPathComponent): \(store.thoughtCount) Thoughts")
        } catch {
            message = (false, error.localizedDescription)
        }
    }

    private func importFile(_ result: Result<URL, Error>) {
        do {
            let picked = try result.get()
            guard picked.startAccessingSecurityScopedResource() else { throw UKSError(message: "Can't read that file") }
            defer { picked.stopAccessingSecurityScopedResource() }
            // Copy into the sandbox so the UKS (which uses plain file APIs) can read it.
            let local = URL.temporaryDirectory.appending(path: picked.lastPathComponent)
            try? FileManager.default.removeItem(at: local)
            try FileManager.default.copyItem(at: picked, to: local)
            if picked.pathExtension.lowercased() == "xml" {
                try store.loadXML(from: local)
            } else {
                try store.importText(from: local)
            }
            message = (true, "Imported \(picked.lastPathComponent): \(store.thoughtCount) Thoughts")
        } catch {
            message = (false, error.localizedDescription)
        }
    }
}
