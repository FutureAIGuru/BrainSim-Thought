import SwiftUI
import UniformTypeIdentifiers

// Pieces the module dialogs share.

/// A read-only multi-line output box that follows its end, like the desktop's ScrollToEnd().
struct LogView: View {
    let title: String?
    let text: String

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            if let title { Text(title).font(.caption).foregroundStyle(.secondary) }
            ScrollViewReader { proxy in
                ScrollView {
                    Text(text.isEmpty ? " " : text)
                        .font(.caption.monospaced())
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .textSelection(.enabled)
                    Color.clear.frame(height: 1).id("end")
                }
                .frame(minHeight: 60, maxHeight: 220)
                .onChange(of: text) { proxy.scrollTo("end", anchor: .bottom) }
            }
        }
    }
}

/// Completions under a text field (the desktop's inline autocomplete): tap one to use it.
struct SuggestionChips: View {
    let suggestions: [String]
    let pick: (String) -> Void

    var body: some View {
        if !suggestions.isEmpty {
            ScrollView(.horizontal, showsIndicators: false) {
                HStack {
                    ForEach(suggestions, id: \.self) { s in
                        Button(s) { pick(s) }
                            .buttonStyle(.bordered)
                            .controlSize(.small)
                    }
                }
            }
        }
    }
}

/// Labels from `candidates` starting with `prefix` (case-insensitive), for autocomplete.
func completions(of prefix: String, in candidates: [String], max: Int = 6) -> [String] {
    let p = prefix.trimmingCharacters(in: .whitespaces)
    guard !p.isEmpty else { return [] }
    return candidates
        .filter { $0.lowercased().hasPrefix(p.lowercased()) && $0.caseInsensitiveCompare(p) != .orderedSame }
        .sorted { $0.localizedCaseInsensitiveCompare($1) == .orderedAscending }
        .prefix(max)
        .map { $0 }
}

/// A row with a confidence bar drawn behind it (0…1).
struct ConfidenceRow: View {
    let text: String
    var detail: String? = nil
    var fraction: Double? = nil
    var selected = false

    var body: some View {
        HStack {
            Text(text).font(.callout)
            Spacer(minLength: 0)
            if let detail { Text(detail).font(.caption).foregroundStyle(.secondary) }
        }
        .padding(.vertical, 4).padding(.horizontal, 6)
        .background(alignment: .leading) {
            GeometryReader { geo in
                if let fraction {
                    Rectangle().fill(Color.green.opacity(0.3)).frame(width: geo.size.width * CGFloat(min(1, max(0, fraction))))
                }
            }
        }
        .background(selected ? Color.accentColor.opacity(0.18) : .clear, in: RoundedRectangle(cornerRadius: 6))
        .contentShape(Rectangle())
    }
}

/// The desktop's Browse… buttons: pick a bundled corpus or image, a file from Documents, or import one.
struct FileRow: View {
    enum Kind { case text, image }

    let title: String
    let path: String?
    var kind: Kind = .text
    let choose: (String) -> Void

    @State private var picking = false
    @State private var importing = false

    var body: some View {
        Button {
            picking = true
        } label: {
            LabeledContent(title) {
                Text(path.flatMap { $0.isEmpty ? nil : URL(fileURLWithPath: $0).lastPathComponent } ?? "Choose…")
                    .lineLimit(1)
            }
        }
        .sheet(isPresented: $picking) {
            NavigationStack {
                List {
                    ForEach(groups, id: \.title) { group in
                        Section(group.title) {
                            ForEach(group.files, id: \.self) { url in
                                Button(url.lastPathComponent) {
                                    choose(url.path(percentEncoded: false))
                                    picking = false
                                }
                            }
                        }
                    }
                }
                .navigationTitle(title)
                .navigationBarTitleDisplayMode(.inline)
                .toolbar {
                    ToolbarItem(placement: .cancellationAction) { Button("Cancel") { picking = false } }
                    ToolbarItem(placement: .primaryAction) { Button("Import…", systemImage: "square.and.arrow.down") { importing = true } }
                }
                .fileImporter(isPresented: $importing, allowedContentTypes: kind == .image ? [.image] : [.plainText, .text]) { result in
                    guard let picked = try? result.get(), picked.startAccessingSecurityScopedResource() else { return }
                    defer { picked.stopAccessingSecurityScopedResource() }
                    let local = URL.documentsDirectory.appending(path: picked.lastPathComponent)
                    try? FileManager.default.removeItem(at: local)
                    if (try? FileManager.default.copyItem(at: picked, to: local)) != nil {
                        choose(local.path(percentEncoded: false))
                        picking = false
                    }
                }
            }
        }
    }

    private var groups: [(title: String, files: [URL])] {
        let exts: Set<String> = kind == .image ? ["png", "jpg", "jpeg", "gif", "bmp", "webp"] : ["txt"]
        let content = HostBridge.contentRoot
        let bundled = kind == .image
            ? [("Grounded Images", content.appending(path: "UKSContent/GroundedDogs/Images"))]
            : [("Word Files", content.appending(path: "WordFIles")), ("UKS Content", content.appending(path: "UKSContent"))]
        var result = bundled.map { (title: $0.0, files: Self.files(in: $0.1, exts)) }
        result.append((title: "Documents", files: Self.files(in: .documentsDirectory, exts)))
        return result.filter { !$0.files.isEmpty }
    }

    private static func files(in folder: URL, _ exts: Set<String>) -> [URL] {
        guard let e = FileManager.default.enumerator(at: folder, includingPropertiesForKeys: nil) else { return [] }
        return e.compactMap { $0 as? URL }
            .filter { exts.contains($0.pathExtension.lowercased()) }
            .sorted { $0.lastPathComponent.localizedStandardCompare($1.lastPathComponent) == .orderedAscending }
    }
}

/// ModuleSoundInDlg plays notes from the computer keyboard (Z S X D C …); here, piano keys.
struct PianoKeyboard: View {
    let low: Int
    let high: Int
    let note: (Int, Bool) -> Void
    @State private var held: Set<Int> = []

    private static let blackOffsets: Set<Int> = [1, 3, 6, 8, 10]

    var body: some View {
        let whites = (low...high).filter { !Self.blackOffsets.contains($0 % 12) }
        GeometryReader { geo in
            let w = geo.size.width / CGFloat(whites.count)
            ZStack(alignment: .topLeading) {
                ForEach(Array(whites.enumerated()), id: \.element) { i, n in
                    key(n, black: false)
                        .frame(width: w - 1, height: geo.size.height)
                        .offset(x: CGFloat(i) * w)
                }
                ForEach(Array(whites.enumerated()), id: \.element) { i, n in
                    if n + 1 <= high, Self.blackOffsets.contains((n + 1) % 12) {
                        key(n + 1, black: true)
                            .frame(width: w * 0.6, height: geo.size.height * 0.6)
                            .offset(x: CGFloat(i + 1) * w - w * 0.3)
                    }
                }
            }
        }
        .frame(height: 120)
    }

    private func key(_ n: Int, black: Bool) -> some View {
        RoundedRectangle(cornerRadius: 4)
            .fill(held.contains(n) ? Color.accentColor : (black ? Color.primary : Color(.systemBackground)))
            .overlay(RoundedRectangle(cornerRadius: 4).stroke(Color.secondary.opacity(0.5)))
            .gesture(DragGesture(minimumDistance: 0)
                .onChanged { _ in if held.insert(n).inserted { note(n, true) } }
                .onEnded { _ in held.remove(n); note(n, false) })
            .accessibilityLabel("Note \(n)")
    }
}

/// A full-width bordered button, for the rows of buttons the desktop dialogs have.
struct WideButton: View {
    let title: String
    var prominent = false
    var role: ButtonRole? = nil
    let action: () -> Void

    init(_ title: String, prominent: Bool = false, role: ButtonRole? = nil, action: @escaping () -> Void) {
        self.title = title; self.prominent = prominent; self.role = role; self.action = action
    }

    var body: some View {
        if prominent {
            Button(role: role, action: action) { Text(title).frame(maxWidth: .infinity) }.buttonStyle(.borderedProminent)
        } else {
            Button(role: role, action: action) { Text(title).frame(maxWidth: .infinity) }.buttonStyle(.bordered)
        }
    }
}

/// Width×height RGB bytes (base64, row by row) as an image, for the modules' pixel arrays.
func rgbImage(base64: String?, width: Int, height: Int) -> CGImage? {
    guard let base64, width > 0, height > 0, let rgb = Data(base64Encoded: base64), rgb.count == width * height * 3,
          let provider = CGDataProvider(data: rgb as CFData) else { return nil }
    return CGImage(width: width, height: height, bitsPerComponent: 8, bitsPerPixel: 24, bytesPerRow: width * 3,
                   space: CGColorSpaceCreateDeviceRGB(), bitmapInfo: CGBitmapInfo(rawValue: CGImageAlphaInfo.none.rawValue),
                   provider: provider, decode: nil, shouldInterpolate: false, intent: .defaultIntent)
}

// MARK: - Controls styled like the old generic dialog renderer

/// A one-line text box, with the desktop's inline autocomplete as chips under it.
struct FieldRow: View {
    let placeholder: String
    @Binding var text: String
    var suggestions: [String] = []
    var numeric = false
    var submit: () -> Void = {}

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            TextField(placeholder, text: $text)
                .keyboardType(numeric ? .numbersAndPunctuation : .default)
                .submitLabel(.return)
                .onSubmit(submit)
            if !suggestions.isEmpty {
                ScrollView(.horizontal, showsIndicators: false) {
                    HStack {
                        ForEach(suggestions, id: \.self) { s in
                            Button(s) { text = s }
                                .buttonStyle(.bordered)
                                .controlSize(.small)
                        }
                    }
                }
            }
        }
    }
}

/// A multi-line text box with its label above it.
struct EditorRow: View {
    let title: String
    @Binding var text: String

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            Text(title).font(.caption).foregroundStyle(.secondary)
            TextEditor(text: $text)
                .font(.body.monospaced())
                .frame(minHeight: 220)
        }
    }
}

/// A read-only value with its label.
struct TextRow: View {
    let title: String
    let value: String

    init(_ title: String, _ value: String) { self.title = title; self.value = value }

    var body: some View {
        LabeledContent(title, value: value)
            .textSelection(.enabled)
    }
}

/// A slider with its label and value above it. While it is dragged it shows (and sends)
/// the finger's value rather than the module's.
struct SliderRow: View {
    let title: String
    let value: Double
    let range: ClosedRange<Double>
    var step: Double = 0
    let set: (Double) -> Void
    @State private var dragging: Double?

    var body: some View {
        let current = dragging ?? value
        VStack(alignment: .leading) {
            LabeledContent(title, value: current.formatted(.number.precision(.fractionLength(step >= 1 ? 0 : 2))))
            Slider(value: Binding(get: { current }, set: { dragging = $0; set($0) }),
                   in: range.lowerBound...max(range.upperBound, range.lowerBound + 0.0001)) { editing in
                if !editing { dragging = nil }
            }
        }
    }
}

/// A menu of choices ("—" for an empty one).
struct MenuPicker: View {
    let title: String
    let selection: String
    let options: [String]
    let choose: (String) -> Void

    var body: some View {
        Picker(title, selection: Binding(get: { selection }, set: choose)) {
            ForEach(options, id: \.self) { Text($0.isEmpty ? "—" : $0).tag($0) }
        }
        .pickerStyle(.menu)
    }
}

/// An on/off switch drawn as a small toggle button (the desktop's check boxes).
struct ToggleChip: View {
    let title: String
    @Binding var isOn: Bool

    var body: some View {
        Toggle(title, isOn: $isOn)
            .toggleStyle(.button)
            .font(.caption)
    }
}

/// Controls laid out side by side, each taking an equal share of the width.
struct ControlRow<Content: View>: View {
    @ViewBuilder let content: Content

    var body: some View {
        HStack(spacing: 10) {
            Group { content }.frame(maxWidth: .infinity)
        }
    }
}

/// A row of a list control.
struct ListItem: Hashable {
    let text: String
    var detail: String? = nil
    /// 0…1 bar drawn behind the row (confidence).
    var fraction: Double? = nil
    var selected = false
}

/// A scrolling list of tappable rows (the desktop's list boxes), scrolled to the selected one.
struct ItemList: View {
    let title: String?
    let items: [ListItem]
    var tap: (Int) -> Void = { _ in }

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            if let title { Text(title).font(.caption).foregroundStyle(.secondary) }
            if items.isEmpty {
                Text("None").foregroundStyle(.tertiary)
            }
            ScrollViewReader { proxy in
                ScrollView {
                    VStack(alignment: .leading, spacing: 2) {
                        ForEach(Array(items.enumerated()), id: \.offset) { i, item in
                            Button { tap(i) } label: {
                                ConfidenceRow(text: item.text, detail: item.detail, fraction: item.fraction, selected: item.selected).id(i)
                            }
                            .buttonStyle(.plain)
                        }
                    }
                }
                .frame(maxHeight: 240)
                .onAppear { scroll(proxy) }
                .onChange(of: items) { scroll(proxy) }
            }
        }
    }

    private func scroll(_ proxy: ScrollViewProxy) {
        if let i = items.firstIndex(where: \.selected) { proxy.scrollTo(i, anchor: .center) }
    }
}

extension View {
    /// The section a dialog's picture (sheet, canvas, graph) sits in.
    func pictureSection() -> some View {
        Section { self }
            .listRowInsets(EdgeInsets(top: 8, leading: 8, bottom: 8, trailing: 8))
    }
}
