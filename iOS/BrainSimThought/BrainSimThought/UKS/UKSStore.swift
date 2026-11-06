import Foundation
import Observation
import UIKit
import UKSNative

/// Swift face of the UKS. The knowledge store itself is the original Brain Simulator Thought
/// C# UKS library, compiled with NativeAOT into libUKSNative.a and linked into this binary.
/// Calls are cheap for normal-sized stores, so they run synchronously on the main actor.
@MainActor
@Observable
final class UKSStore {
    /// Bumped after every mutation so views can refresh.
    private(set) var revision = 0
    /// Bumped (at most every second) while the module engine changes the UKS, for views
    /// that should follow it live without re-laying out on every edit.
    private(set) var liveRevision = 0
    private var lastLive = Date.distantPast
    private(set) var thoughtCount = 0
    var status: String?

    init() {
        HostBridge.install()
        thoughtCount = Int(uks_initialize(0))
        restoreAutosave()
    }

    // MARK: - Bridging helpers

    private static let decoder = JSONDecoder()

    /// Calls a C export returning an owned UTF-8 string and decodes it.
    @discardableResult
    private func call<T: Decodable>(_ type: T.Type, _ body: () -> UnsafeMutablePointer<CChar>?) throws -> T {
        guard let ptr = body() else { throw UKSError(message: "UKS returned nothing") }
        defer { uks_free(ptr) }
        let data = Data(bytes: ptr, count: strlen(ptr))
        // Failures are serialized as {"ok":false,…}; only those are worth decoding twice
        // (payloads such as canvases can be large).
        if data.starts(with: UKSOpResult.failurePrefix),
           let failure = try? Self.decoder.decode(UKSOpResult.self, from: data), let err = failure.error {
            throw UKSError(message: err)
        }
        return try Self.decoder.decode(T.self, from: data)
    }

    @discardableResult
    private func mutate(_ body: () -> UnsafeMutablePointer<CChar>?) throws -> UKSOpResult {
        let result = try call(UKSOpResult.self, body)
        changed()
        return result
    }

    private func changed() {
        thoughtCount = Int(uks_thought_count())
        revision += 1
        scheduleAutosave()
    }

    /// Module actions can change anything in the UKS.
    func modulesChanged() { changed() }

    /// Called when the engine has fired modules since the last check.
    func engineAdvanced() {
        guard Date.now.timeIntervalSince(lastLive) >= 1 else { return }
        lastLive = .now
        thoughtCount = Int(uks_thought_count())
        liveRevision += 1
        scheduleAutosave(restart: false)
    }

    // MARK: - Statements

    /// `weight` below 1 sets the confidence of a newly created Link.
    @discardableResult
    func addStatement(from: String, linkType: String, to: String, weight: Float = 1) throws -> UKSOpResult {
        try mutate { uks_add_statement(from, linkType, to, weight) }
    }

    /// One line of the UKS text format, e.g. `[Fido->is-a->dog] 1.00`.
    @discardableResult
    func addLine(_ line: String) throws -> UKSOpResult {
        try mutate { uks_parse_line(line) }
    }

    /// Desktop UKS dialog context menu: rename, fire, and "Delete Child" (unlink from a parent).
    func rename(_ label: String, to newLabel: String) throws {
        try mutate { uks_rename_thought(label, newLabel) }
    }

    func fire(_ label: String) throws {
        try mutate { uks_fire_thought(label) }
    }

    func removeParent(_ parent: String, from child: String) throws {
        try mutate { uks_remove_parent(child, parent) }
    }

    func deleteThought(_ label: String, withChildren: Bool = false) throws {
        try mutate { uks_delete_thought(label, withChildren ? 1 : 0) }
    }

    /// Removes a Link that goes out of the Thought labeled `owner`.
    func deleteLink(_ link: UKSLink, from owner: String) throws {
        try mutate { uks_delete_link(owner, link.text) }
    }

    // MARK: - Browsing & query

    func thought(_ label: String) -> UKSThought? {
        (try? call(UKSThought?.self) { uks_get_thought(label) }) ?? nil
    }

    /// Thoughts within `depth` hops of `root` (nil = whole UKS), capped at `maxNodes`.
    func graph(root: String?, depth: Int, maxNodes: Int = 150) throws -> UKSGraph {
        try call(UKSGraph.self) { uks_graph(root ?? "", Int32(depth), Int32(maxNodes)) }
    }

    func roots() -> [String] {
        (try? call([String].self) { uks_roots() }) ?? []
    }

    func search(_ text: String, limit: Int = 100) -> [String] {
        (try? call([String].self) { uks_search(text, Int32(limit)) }) ?? []
    }

    func query(source: String, linkType: String, target: String, filter: String) throws -> UKSQueryResult {
        try call(UKSQueryResult.self) { uks_query(source, linkType, target, filter) }
    }

    /// Why? / Why not? for the latest query of `source`.
    func why(source: String, linkType: String, target: String) throws -> String {
        try call(UKSOpResult.self) { uks_why(source, linkType, target) }.message ?? ""
    }

    /// Thoughts under `ancestor` matching `linkType,target[,weight]` lines, best first.
    func searchByAttributes(_ attributes: String, ancestor: String) throws -> [UKSScored] {
        try call([UKSScored].self) { uks_search_by_attributes(attributes, ancestor) }
    }

    // MARK: - Files

    static var documents: URL { URL.documentsDirectory }
    private static var autosaveURL: URL { documents.appending(path: "Autosave.xml") }
    private var suspendAutosave = false

    func reset() {
        thoughtCount = Int(uks_initialize(1))
        changed()
    }

    func saveXML(to url: URL) throws {
        try call(UKSOpResult.self) { uks_save_xml(url.path(percentEncoded: false)) }
    }

    func loadXML(from url: URL) throws {
        try mutate { uks_load_xml(url.path(percentEncoded: false)) }
    }

    func importText(from url: URL) throws {
        try mutate { uks_import_text(url.path(percentEncoded: false)) }
    }

    func exportText(root: String, to url: URL) throws {
        try call(UKSOpResult.self) { uks_export_text(root, url.path(percentEncoded: false)) }
    }

    /// Bundled sample knowledge files from the desktop app (UKSContent).
    var sampleFiles: [URL] {
        let folder = HostBridge.contentRoot.appending(path: "UKSContent")
        let files = (try? FileManager.default.contentsOfDirectory(at: folder, includingPropertiesForKeys: nil)) ?? []
        return files.filter { ["xml", "txt"].contains($0.pathExtension.lowercased()) }
            .sorted { $0.lastPathComponent.localizedStandardCompare($1.lastPathComponent) == .orderedAscending }
    }

    private var autosaveTask: Task<Void, Never>?

    /// Saves a few seconds after the last change rather than after every single edit.
    /// `restart: false` keeps an already pending save, so a running engine still saves regularly.
    private func scheduleAutosave(restart: Bool = true) {
        guard !suspendAutosave else { return }
        if !restart, autosaveTask != nil { return }
        autosaveTask?.cancel()
        autosaveTask = Task { [weak self] in
            try? await Task.sleep(for: .seconds(restart ? 3 : 30))
            guard !Task.isCancelled else { return }
            self?.autosaveNow()
        }
    }

    private var saving = false
    private var saveAgain = false

    /// Saves on a background thread. The UKS is only locked while it is snapshotted; writing
    /// the XML (most of the time) doesn't hold up the engine or the UI.
    func autosaveNow() {
        guard !suspendAutosave else { return }
        autosaveTask?.cancel()
        autosaveTask = nil
        if saving { saveAgain = true; return }
        saving = true
        let path = Self.autosaveURL.path(percentEncoded: false)
        // let a save started as the app goes to the background finish
        let background = UIApplication.shared.beginBackgroundTask(withName: "Autosave")
        Task.detached(priority: .utility) {
            if let result = uks_save_xml(path) { uks_free(result) }
            await MainActor.run { [weak self] in
                UIApplication.shared.endBackgroundTask(background)
                guard let self else { return }
                saving = false
                if saveAgain {
                    saveAgain = false
                    autosaveNow()
                }
            }
        }
    }

    private func restoreAutosave() {
        guard FileManager.default.fileExists(atPath: Self.autosaveURL.path(percentEncoded: false)) else { return }
        suspendAutosave = true
        defer { suspendAutosave = false }
        try? loadXML(from: Self.autosaveURL)
    }
}
