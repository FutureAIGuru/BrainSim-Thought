import Foundation
import Observation
import UKSNative

/// The desktop MainWindow's module side: active modules, the engine timer that fires
/// them, and each module's logic interface (UKSNative/Host/ModuleApi.cs: its state as data,
/// and named operations). The modules are the original C# classes; their dialogs are the
/// SwiftUI views in Dialogs/.
@MainActor
@Observable
final class ModuleEngine {
    private(set) var active: [ModuleInfo] = []
    private(set) var available: [String] = []
    private(set) var ticks: Int64 = 0
    /// Alerts raised by modules (MessageBox.Show on the desktop).
    var notices: [ModuleNotice] = []

    /// Engine period in milliseconds; the desktop timer runs every 10 ms.
    var intervalMs: Int = 10 {
        didSet { if running { uks_engine_run(Int32(intervalMs)) } }
    }
    var running = false {
        didSet { uks_engine_run(running ? Int32(intervalMs) : 0) }
    }

    private let store: UKSStore
    private var poll: Task<Void, Never>?

    init(store: UKSStore) {
        self.store = store
        HostBridge.install()
        available = (try? call([String].self) { uks_modules_available() }) ?? []
        refresh()
        running = true
        poll = Task { [weak self] in
            while !Task.isCancelled {
                try? await Task.sleep(for: .milliseconds(250))
                self?.pulse()
            }
        }
    }

    // MARK: - Bridging

    private static let decoder = JSONDecoder()

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

    /// Picks up engine progress, module notices and module list changes.
    private func pulse() {
        let t = uks_engine_ticks()
        if t != ticks {
            ticks = t
            store.engineAdvanced()
        }
        if let new = try? call([ModuleNotice].self, { uks_notices() }), !new.isEmpty {
            notices.append(contentsOf: new)
        }
        refresh()
    }

    func refresh() {
        // nil when the UKS is busy: keep the list we have
        guard let list = try? call([ModuleInfo].self, { uks_modules_active() }) else { return }
        if list != active { active = list }
    }

    // MARK: - Module list

    @discardableResult
    func activate(_ type: String) throws -> String {
        let label = try call(UKSOpResult.self) { uks_module_activate(type) }.message ?? type
        refresh()
        store.modulesChanged()
        return label
    }

    func remove(_ label: String) throws {
        try call(UKSOpResult.self) { uks_module_remove(label) }
        refresh()
        store.modulesChanged()
    }

    func setEnabled(_ label: String, _ enabled: Bool) {
        _ = try? call(UKSOpResult.self) { uks_module_set_enabled(label, enabled ? 1 : 0) }
        refresh()
    }

    func initialize(_ label: String) throws {
        try call(UKSOpResult.self) { uks_module_initialize(label) }
    }

    // MARK: - Module APIs

    /// A read-only question to a module ("state", or another its API answers). Throws a
    /// busy error while the UKS is in the middle of long work.
    func query<T: Decodable>(_ type: T.Type, _ label: String, _ op: String = "state", _ args: [String: Any] = [:]) throws -> T {
        let json = Self.encode(args)
        return try call(T.self) { uks_module_query(label, op, json) }
    }

    /// Performs a module operation; returns the message it reports, if any.
    @discardableResult
    func call(_ label: String, _ op: String, _ args: [String: Any] = [:]) throws -> String? {
        let json = Self.encode(args)
        let result = try call(ModuleCallResult.self) { uks_module_call(label, op, json) }
        store.modulesChanged()
        return result.message
    }

    /// Performs a module operation that answers with data (e.g. the Text module's answer).
    func call<T: Decodable>(_ type: T.Type, _ label: String, _ op: String, _ args: [String: Any] = [:]) throws -> T {
        let json = Self.encode(args)
        let result = try call(T.self) { uks_module_call(label, op, json) }
        store.modulesChanged()
        return result
    }

    private static func encode(_ args: [String: Any]) -> String {
        guard !args.isEmpty, let data = try? JSONSerialization.data(withJSONObject: args) else { return "" }
        return String(decoding: data, as: UTF8.self)
    }

    // MARK: - Settings

    static let apiKeySetting = "APIKey"

    /// app.config appSettings on the desktop (e.g. the OpenAI key for GPT modules).
    static func setSetting(_ key: String, _ value: String) {
        uks_set_setting(key, value)
    }
}
