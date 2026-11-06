import SwiftUI

// ModuleVisionDlg: the module's pixel and boundary arrays, the patches and corners that
// fired, and its buttons. The module reports them as data (UKSNative/Host/VisionApi.cs);
// what is drawn, in which colours, and which overlays are on, is decided here.

struct WeightedPoint: Decodable, Hashable {
    let x: Int
    let y: Int
    let weight: Float
    let maxWeight: Float
}

struct VisionPatch: Decodable, Hashable {
    let label: String
    let x: Int
    let y: Int
    let weight: Float
    let firedAgo: Double
    let boundary: [WeightedPoint]
    let collinear: [WeightedPoint]
    let orientation: Double?
    let orientationX: Int?
    let orientationY: Int?
}

struct VisionCorner: Decodable, Hashable {
    let label: String
    let x: Int
    let y: Int
    let weight: Float
}

struct VisionState: Decodable {
    let imagePath: String?
    let width: Int
    let height: Int
    let pixels: String?
    let boundaryWidth: Int
    let boundaryHeight: Int
    let boundary: String?
    let patchSize: Int
    let outlines: Int
    let patches: [VisionPatch]
    let corners: [VisionCorner]
    let busy: Bool
    let status: String?
}

/// The desktop dialog's check boxes, with its defaults.
struct VisionOverlays: Equatable {
    var pixels = false
    var boundaries = true
    var strokes = false
    var centerPoints = false
    var patches = true
    var corners = false
}

struct VisionDialog: View {
    @State private var module: ModuleLink<VisionState>
    @State private var overlays = VisionOverlays()
    /// What was tapped on the canvas (the desktop's right-click status line).
    @State private var selected: String?
    init(label: String) { _module = State(initialValue: ModuleLink(label)) }

    var body: some View {
        ModuleForm(module: module, status: { s in selected ?? s.status ?? module.message }) { s in
            VisionCanvas(state: s, overlays: overlays) { tag in
                selected = tag
                module.call("inspect", ["thought": String(tag.split(separator: " ").first ?? "")])
            }
            .pictureSection()
            Section {
                FileRow(title: "Image", path: s.imagePath, kind: .image) { module.call("setImage", ["path": $0]) }
                if s.width > 0 {
                    TextRow("Image", "\(s.width)x\(s.height)   Outlines: \(s.outlines)")
                }
                ControlRow {
                    ToggleChip(title: "Pixel Array", isOn: $overlays.pixels)
                    ToggleChip(title: "Boundaries", isOn: $overlays.boundaries)
                    ToggleChip(title: "Strokes", isOn: $overlays.strokes)
                }
                ControlRow {
                    ToggleChip(title: "CenterPts", isOn: $overlays.centerPoints)
                    ToggleChip(title: "Patches", isOn: $overlays.patches)
                    ToggleChip(title: "Corners", isOn: $overlays.corners)
                }
                ControlRow {
                    WideButton("Test") { module.call("test") }
                    // the desktop's "100" button runs 1000 patterns
                    WideButton("100") { module.call("testMany", ["count": 1000]) }
                    WideButton("Show") { module.call("show") }
                }
                ControlRow {
                    WideButton("Init") { module.call("init") }
                    WideButton("Clear") { module.call("clear") }
                    WideButton("Refresh") { module.call("refresh") }
                }
            }
        }
    }
}

/// The desktop canvas: everything is a rectangle or line in pixel-grid units, drawn in
/// order; a tap shows the topmost tagged one.
struct VisionCanvas: View {
    let state: VisionState
    let overlays: VisionOverlays
    let tap: (String) -> Void

    struct Shape {
        var line = false
        var x = 0.0, y = 0.0, w = 0.0, h = 0.0, x2 = 0.0, y2 = 0.0
        var fill: Color?
        var stroke: Color?
        var lineWidth = 0.0
        var tag: String?
    }

    // desktop: scale = canvas / rows, pixelSize = scale - 2
    private let pixelSize = 0.8
    private static let gray = Color(hex: 0x808080), yellow = Color(hex: 0xFFFF00), pink = Color(hex: 0xFFC0CB)
    private static let green = Color(hex: 0x008000), red = Color(hex: 0xFF0000)

    var body: some View {
        if state.width == 0 || state.height == 0 {
            // nothing to draw yet (no image loaded); a 0×0 canvas has no aspect ratio
            Label("No image yet", systemImage: "photo").foregroundStyle(.secondary)
                .frame(maxWidth: .infinity, minHeight: 120)
        } else {
            let shapes = self.shapes()
            let width = Double(state.width + 1), height = Double(state.height + 1)
            GeometryReader { geo in
                let s = min(geo.size.width / width, geo.size.height / height)
                Canvas { ctx, _ in
                    ctx.fill(Path(CGRect(origin: .zero, size: geo.size)), with: .color(Color(white: 0.95)))
                    for shape in shapes {
                        if shape.line {
                            var p = Path()
                            p.move(to: CGPoint(x: shape.x * s, y: shape.y * s))
                            p.addLine(to: CGPoint(x: shape.x2 * s, y: shape.y2 * s))
                            ctx.stroke(p, with: .color(shape.stroke ?? .black), lineWidth: shape.lineWidth)
                        } else {
                            let r = CGRect(x: shape.x * s, y: shape.y * s, width: shape.w * s, height: shape.h * s)
                            if let fill = shape.fill { ctx.fill(Path(r), with: .color(fill)) }
                            if let stroke = shape.stroke { ctx.stroke(Path(r), with: .color(stroke), lineWidth: shape.lineWidth) }
                        }
                    }
                }
                .contentShape(Rectangle())
                .onTapGesture { location in
                    let p = CGPoint(x: location.x / s, y: location.y / s)
                    if let hit = shapes.last(where: {
                        $0.tag != nil && CGRect(x: $0.x, y: $0.y, width: $0.w, height: $0.h).insetBy(dx: -0.2, dy: -0.2).contains(p)
                    }), let tag = hit.tag {
                        tap(tag)
                    }
                }
            }
            .aspectRatio(width / height, contentMode: .fit)
            .overlay {
                if shapes.isEmpty {
                    Text("Choose an image, then Init and Refresh.").font(.callout).foregroundStyle(.secondary)
                }
            }
        }
    }

    /// ModuleVisionDlg's Draw(), shape for shape.
    private func shapes() -> [Shape] {
        var out: [Shape] = []
        let ps = pixelSize
        func rect(_ x: Double, _ y: Double, _ w: Double, _ h: Double, fill: Color? = nil, stroke: Color? = nil,
                  lw: Double = 0, _ tag: String?) {
            out.append(Shape(x: x, y: y, w: w, h: h, fill: fill, stroke: stroke, lineWidth: lw, tag: tag))
        }

        if overlays.pixels, let data = state.pixels.flatMap({ Data(base64Encoded: $0) }), data.count == state.width * state.height * 3 {
            let rgb = [UInt8](data)
            for x in 0..<state.width {
                for y in 0..<state.height {
                    let i = (y * state.width + x) * 3
                    let r = rgb[i], g = rgb[i + 1], b = rgb[i + 2]
                    let lum = (Double(max(r, g, b)) + Double(min(r, g, b))) / 2 / 255
                    rect(Double(x) + ps / 2, Double(y) + ps / 2, ps, ps,
                         fill: Color(red: Double(r) / 255, green: Double(g) / 255, blue: Double(b) / 255), "(\(x),\(y)) \(Self.f2(lum))")
                }
            }
        }

        let bw = state.boundaryWidth, bh = state.boundaryHeight
        let bits = Array(state.boundary?.utf8 ?? "".utf8)
        guard bw > 0, bh > 0, bits.count == bw * bh else { return out }

        if overlays.boundaries {
            for x in 0..<bw {
                for y in 0..<bh where bits[y * bw + x] == UInt8(ascii: "1") {
                    rect(Double(x) + ps / 2, Double(y) + ps / 2, ps, ps, fill: Self.gray, "(\(x),\(y)) ")
                }
            }
        }

        if overlays.centerPoints, bw > 4, bh > 4 {
            for x in 2..<(bw - 2) {
                for y in 2..<(bh - 2) {
                    rect(Double(x) + 0.75, Double(y) + 0.75, ps * 0.5, ps * 0.5, stroke: Self.yellow, lw: 2,
                         String(format: "patch_%02d_%02d_0", x, y))
                }
            }
            for p in state.patches {
                rect(Double(p.x) + 0.75, Double(p.y) + 0.75, ps * 0.5, ps * 0.5, stroke: Self.pink, lw: 2, "\(p.label) c:\(Self.weight(p.weight))")
            }
        }

        if overlays.patches {
            // the desktop draws the patches that fired in the last three seconds
            for p in state.patches where p.firedAgo <= 3 {
                let size = Double(state.patchSize)
                rect(Double(p.x) + 0.65 - size / 2, Double(p.y) + 0.65 - size / 2, ps * (size - 0.3), ps * (size - 0.3),
                     stroke: Self.pink, lw: 2, "\(p.label) c:\(Self.weight(p.weight))")
                for b in p.boundary {
                    rect(Double(b.x) + 3 * ps / 4, Double(b.y) + 3 * ps / 4, ps / 2, ps / 2, fill: Self.rainbow(b.weight),
                         "(\(b.x),\(b.y)) w: \(Self.f2(b.weight))  max: \(Self.f2(b.maxWeight))")
                }
                if overlays.corners {
                    for c in p.collinear {
                        rect(Double(c.x) + 3 * ps / 4, Double(c.y) + 3 * ps / 4, ps / 2, ps / 2, fill: Self.red,
                             "(\(c.x),\(c.y)) w: \(Self.f2(c.weight))  max: \(Self.f2(c.maxWeight))")
                    }
                }
                if overlays.strokes, let theta = p.orientation, let cx = p.orientationX, let cy = p.orientationY {
                    let l = 2.0, off = 3 * ps / 4
                    out.append(Shape(line: true,
                                     x: Double(cx) - cos(theta) * l + off, y: Double(cy) - sin(theta) * l + off,
                                     x2: Double(cx) + cos(theta) * l + off, y2: Double(cy) + sin(theta) * l + off,
                                     stroke: .black, lineWidth: 1.5))
                }
            }
        }

        if overlays.corners {
            for c in state.corners {
                rect(Double(c.x) + 0.875, Double(c.y) + 0.875, ps * 0.25, ps * 0.25, stroke: Self.green, lw: 2, "\(c.label) c:\(Self.weight(c.weight))")
            }
        }
        return out
    }

    private static func f2(_ v: some BinaryFloatingPoint) -> String { String(format: "%.2f", Double(v)) }

    /// C#'s default float formatting, as in "c:" + t.Weight.
    private static func weight(_ v: Float) -> String { v == v.rounded() ? String(Int(v)) : "\(v)" }

    /// RainbowColorFromValue (desktop dialog): −1 black … 0 blue … +1 red, white beyond.
    static func rainbow(_ value: Float) -> Color {
        var v = Int(1023 * value)
        if v < -1022 { return .black }
        if v >= 1023 { return .white }
        if v == 0 { return .blue }
        func rgb(_ r: Int, _ g: Int, _ b: Int) -> Color { Color(red: Double(r) / 255, green: Double(g) / 255, blue: Double(b) / 255) }
        if v < 0 {
            let g = (1024 - abs(v) / 2 + 512) / 4
            return rgb(g, g, g)
        }
        v = 1023 - v
        if v < 256 { return rgb(255, v, 0) }
        if v < 512 { v -= 256; return rgb(255 - v, 255, 0) }
        if v < 768 { v -= 512; return rgb(0, 255, v) }
        v -= 768
        return rgb(0, 255 - v, 255)
    }
}

extension Color {
    /// 0xRRGGBB
    init(hex: UInt32) {
        self.init(red: Double((hex >> 16) & 0xFF) / 255, green: Double((hex >> 8) & 0xFF) / 255, blue: Double(hex & 0xFF) / 255)
    }
}
