import SwiftUI

// The Object Recognizer's dialog (new on iOS; the module has no desktop dialog): take or
// choose a picture, see what it was matched to, teach or correct it, and manage what is
// known. The module reports its last look as data (UKSNative/Host/ObjectRecognizerApi.cs).

struct ColorShare: Decodable, Hashable {
    let name: String
    let fraction: Float
}

struct RecognizerMatch: Decodable, Hashable {
    let label: String
    let score: Float
}

struct KnownObject: Decodable, Hashable {
    let label: String
    let color: String?
    let views: Int
    let anonymous: Bool
}

struct RecognizerObservation: Decodable {
    let boundaryCount: Int
    let aspect: Float
    let colors: [ColorShare]
}

struct ObjectRecognizerState: Decodable {
    let imagePath: String?
    let status: String?
    let looking: Bool
    let pictureRevision: Int
    let observation: RecognizerObservation?
    let matches: [RecognizerMatch]
    let object: String?
    let recognized: Bool
    let objectIsAnonymous: Bool
    let guess: String?
    let known: [KnownObject]
    let learnUnknownObjects: Bool
    let recognizeThreshold: Float
}

struct RecognizerPicture: Decodable {
    let width: Int
    let height: Int
    let pixels: String?
    let boundary: [[Float]]
    let box: [Int]?
}

struct ObjectRecognizerDialog: View {
    @State private var module: ModuleLink<ObjectRecognizerState>
    @State private var picture: RecognizerPicture?
    @State private var name = ""
    /// The known object tapped in the list.
    @State private var selected: String?
    init(label: String) { _module = State(initialValue: ModuleLink(label)) }

    var body: some View {
        ModuleForm(module: module, status: { s in s.looking ? "Looking…" : (module.message ?? s.status) }) { s in
            RecognizerPictureView(picture: picture)
                .pictureSection()
            Section {
                CameraControl(title: "Look at an Object") { look($0) }
                FileRow(title: "Or use an image", path: s.imagePath, kind: .image) { look($0) }
            }
            if let obs = s.observation {
                Section("What it saw") {
                    TextRow("Edges", "\(obs.boundaryCount) points, \(Self.shape(obs.aspect)) outline")
                    TextRow("Colours", obs.colors.filter { $0.fraction >= 0.1 }.sorted { $0.fraction > $1.fraction }
                        .map { "\($0.name) \(Self.percent($0.fraction))" }.joined(separator: ", "))
                    if !s.matches.isEmpty {
                        let matches = Array(s.matches.prefix(5))
                        ItemList(title: "Closest known objects", items: matches.map {
                            ListItem(text: $0.label, detail: Self.percent($0.score, spaced: true),
                                     fraction: min(1, max(0, Double($0.score))), selected: $0.label == s.object)
                        }) { i in
                            // tapping a close match teaches that name
                            module.call("teach", ["name": matches[i].label])
                        }
                    }
                }
                Section("Teach") {
                    if let guess = s.guess {
                        WideButton("Yes, it's \(guess)", prominent: true) { module.call("confirm") }
                    }
                    ControlRow {
                        FieldRow(placeholder: s.object != nil && !s.objectIsAnonymous ? "It's actually…" : "What is it?", text: $name,
                                 suggestions: completions(of: name, in: s.known.map(\.label)), submit: teach)
                        WideButton("Teach") { teach() }
                            .disabled(name.trimmingCharacters(in: .whitespaces).isEmpty)
                    }
                }
            }
            Section("Known objects") {
                ItemList(title: nil, items: s.known.map {
                    ListItem(text: $0.label, detail: [$0.color, $0.views == 1 ? "1 view" : "\($0.views) views"].compactMap { $0 }.joined(separator: " · "),
                             selected: $0.label == selected)
                }) { i in
                    selected = s.known[i].label == selected ? nil : s.known[i].label
                }
                if let selected, s.known.contains(where: { $0.label == selected }) {
                    WideButton("Forget \(selected)", role: .destructive) {
                        module.call("forget", ["object": selected])
                        self.selected = nil
                    }
                }
            }
            Section("Settings") {
                ToggleChip(title: "Remember unknown objects", isOn: Binding(get: { s.learnUnknownObjects },
                                                                           set: { module.call("setLearnUnknown", ["value": $0]) }))
                SliderRow(title: "Recognize at", value: Double(s.recognizeThreshold), range: 0.5...0.99, step: 0.01) {
                    module.call("setRecognizeThreshold", ["value": $0])
                }
            }
        }
        .task(id: module.state?.pictureRevision) {
            // the picture is large, so it is only fetched when a new look changed it
            guard module.state != nil else { return }
            picture = module.query(RecognizerPicture.self, "picture")
        }
    }

    private func look(_ path: String) {
        selected = nil
        name = ""
        module.call("look", ["path": path])
    }

    private func teach() {
        let n = name.trimmingCharacters(in: .whitespaces)
        guard !n.isEmpty else { return }
        if module.call("teach", ["name": n]) { name = "" }
    }

    private static func shape(_ aspect: Float) -> String { aspect > 1.25 ? "wide" : aspect < 0.8 ? "tall" : "square" }

    /// C#'s "P0": "45%", or "45 %" in the invariant culture the match scores used.
    static func percent(_ v: Float, spaced: Bool = false) -> String {
        "\(Int((Double(v) * 100).rounded()))" + (spaced ? " %" : "%")
    }
}

/// The picture the recognizer looked at, with its edge points and the object's box.
private struct RecognizerPictureView: View {
    let picture: RecognizerPicture?

    private static let edge = Color(hex: 0xFF2D55), box = Color(hex: 0xFFD60A)

    var body: some View {
        if let picture, picture.width > 0, picture.height > 0 {
            let image = rgbImage(base64: picture.pixels, width: picture.width, height: picture.height)
            let width = CGFloat(picture.width), height = CGFloat(picture.height)
            Canvas { ctx, size in
                let s = min(size.width / width, size.height / height)
                ctx.fill(Path(CGRect(origin: .zero, size: size)), with: .color(Color(white: 0.95)))
                if let image {
                    // one canvas unit per pixel, drawn blocky like the desktop's pixel rects
                    ctx.draw(Image(decorative: image, scale: 1).interpolation(.none).resizable(),
                             in: CGRect(x: 0, y: 0, width: width * s, height: height * s))
                }
                var edges = Path()
                for p in picture.boundary where p.count == 2 {
                    edges.addRect(CGRect(x: (CGFloat(p[0]) - 0.3) * s, y: (CGFloat(p[1]) - 0.3) * s, width: 0.6 * s, height: 0.6 * s))
                }
                ctx.fill(edges, with: .color(Self.edge))
                if let b = picture.box, b.count == 4 {
                    let r = CGRect(x: CGFloat(b[0]) * s, y: CGFloat(b[1]) * s,
                                   width: CGFloat(b[2] - b[0] + 1) * s, height: CGFloat(b[3] - b[1] + 1) * s)
                    ctx.stroke(Path(r), with: .color(Self.box), lineWidth: 2)
                }
            }
            .aspectRatio(width / height, contentMode: .fit)
        } else if picture != nil {
            // nothing to draw yet; a 0×0 canvas has no aspect ratio
            Label("No image yet", systemImage: "photo").foregroundStyle(.secondary)
                .frame(maxWidth: .infinity, minHeight: 120)
        } else {
            ProgressView().frame(maxWidth: .infinity, minHeight: 200)
        }
    }
}
