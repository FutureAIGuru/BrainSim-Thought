import Observation
import SwiftUI

/// One module's logic, as its dialog sees it: the state it reports (refreshed while the
/// dialog is on screen) and the operations it performs. The C# side only answers questions
/// and does work; everything shown is decided by the dialog view.
@MainActor
@Observable
final class ModuleLink<State: Decodable> {
    let label: String
    private(set) var state: State?
    /// The last operation's message, or its error.
    private(set) var message: String?
    private(set) var failed = false
    private(set) var unavailable: String?

    @ObservationIgnored private weak var engine: ModuleEngine?

    init(_ label: String) { self.label = label }

    func attach(_ engine: ModuleEngine) { self.engine = engine }

    /// Asks for the state again (keeps the last one while the UKS is busy).
    func reload() {
        guard let engine else { return }
        do {
            state = try engine.query(State.self, label)
            unavailable = nil
        } catch let e as UKSError where e.isBusy {
        } catch {
            if state == nil { unavailable = error.localizedDescription }
        }
    }

    /// Polls the state until cancelled (the dialog went away).
    func poll(every interval: Duration = .milliseconds(300)) async {
        while !Task.isCancelled {
            reload()
            try? await Task.sleep(for: interval)
        }
    }

    /// Performs an operation; shows its message (or error) and refreshes the state.
    @discardableResult
    func call(_ op: String, _ args: [String: Any] = [:]) -> Bool {
        guard let engine else { return false }
        do {
            let reply = try engine.call(label, op, args)
            if let reply { message = reply; failed = false }
            reload()
            return true
        } catch {
            message = error.localizedDescription
            failed = true
            return false
        }
    }

    /// Performs an operation that answers with data; shows its error, if it fails.
    func perform<T: Decodable>(_ type: T.Type, _ op: String, _ args: [String: Any] = [:]) -> T? {
        guard let engine else { return nil }
        do {
            let result = try engine.call(type, label, op, args)
            message = nil; failed = false
            reload()
            return result
        } catch {
            message = error.localizedDescription
            failed = true
            return nil
        }
    }

    /// A read-only question other than the state ("picture", "attributes"…).
    func query<T: Decodable>(_ type: T.Type, _ op: String, _ args: [String: Any] = [:]) -> T? {
        try? engine?.query(type, label, op, args)
    }

    func clearMessage() { message = nil; failed = false }

    /// Shows a status line worked out by the dialog itself.
    func show(_ text: String?) { message = text; failed = false }
}

/// A module dialog's screen, laid out like the generic dialog renderer the app had before:
/// a first section with the dialog's note and status line, then the dialog's own sections.
/// Waits for the first state and keeps it fresh while visible.
struct ModuleForm<State: Decodable, Content: View>: View {
    @Environment(ModuleEngine.self) private var engine
    let module: ModuleLink<State>
    var pollInterval: Duration = .milliseconds(300)
    /// Fixed text under the title (the old dialogs' "note").
    var note: String? = nil
    /// The status line; by default the last operation's message.
    var status: ((State) -> String?)? = nil
    @ViewBuilder let content: (State) -> Content

    var body: some View {
        Group {
            if let state = module.state {
                List {
                    let line = status.map { $0(state) } ?? module.message
                    if note != nil || line != nil {
                        Section {
                            if let note {
                                Text(note).font(.callout).foregroundStyle(.secondary)
                            }
                            if let line, !line.isEmpty {
                                Label(line, systemImage: "info.circle").font(.callout)
                            }
                        }
                    }
                    content(state)
                }
                .scrollDismissesKeyboard(.interactively)
                .autocorrectionDisabled()
                .textInputAutocapitalization(.never)
            } else if let problem = module.unavailable {
                ContentUnavailableView("Module unavailable", systemImage: "exclamationmark.triangle", description: Text(problem))
            } else {
                ProgressView()
            }
        }
        .task(id: module.label) {
            module.attach(engine)
            await module.poll(every: pollInterval)
        }
    }
}
