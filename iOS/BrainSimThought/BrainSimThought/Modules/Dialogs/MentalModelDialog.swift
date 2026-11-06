import SwiftUI

// ModuleMentalModelDlg: the spatial sheet of cells (azimuth across, elevation down) with the
// Thoughts placed in them, and the buttons that turn and move the viewpoint. The module
// reports each cell's angles and contents (UKSNative/Host/MentalModelApi.cs); where things
// go on screen, their colours and sizes are decided here.

struct MentalContent: Decodable, Hashable {
    let label: String
    let imagePath: String?
    let attributes: [[String]]?
    let imagined: Bool
    let distance: Double
    let secondsLeft: Double?
}

struct MentalCell: Decodable, Hashable {
    let label: String
    let azimuthFrom: Double
    let azimuthTo: Double
    let inView: Bool
    let isCenter: Bool
    let attended: Bool
    let firedAgo: Double?
    let contents: [MentalContent]?
}

struct MentalRing: Decodable, Hashable {
    let elevationFrom: Double
    let elevationTo: Double
    let cells: [MentalCell]
}

struct MentalModelState: Decodable {
    let attentionCell: String?
    let attentionAzimuth: Double
    let attentionElevation: Double
    let imaginedOpacity: Double
    let cardAttributes: Int?
    let rings: [MentalRing]
}

struct MentalModelDialog: View {
    @State private var module: ModuleLink<MentalModelState>
    init(label: String) { _module = State(initialValue: ModuleLink(label)) }

    /// How far one press turns (degrees) or moves (distance units), as on the desktop.
    private let turnStep = 20.0
    private let moveStep = 5.0

    var body: some View {
        ModuleForm(module: module, status: Self.status) { s in
            MentalModelSheet(state: s) { module.call("attend", ["cell": $0]) }
                .pictureSection()
            Section {
                ControlRow {
                    // turning the head left moves the scene right
                    WideButton("Left") { module.call("turn", ["degrees": turnStep]) }
                    WideButton("Right") { module.call("turn", ["degrees": -turnStep]) }
                    WideButton("Forward") { module.call("move", ["distance": moveStep]) }
                    WideButton("Back") { module.call("move", ["distance": -moveStep]) }
                }
            }
        }
    }

    private static func status(_ s: MentalModelState) -> String? {
        guard s.attentionCell != nil else { return nil }
        return "Attn: Horiz \(degrees(s.attentionAzimuth))° Vert \(degrees(s.attentionElevation))°"
    }

    /// The desktop's "0.#" format.
    static func degrees(_ value: Double) -> String {
        value.formatted(.number.precision(.fractionLength(0...1)).grouping(.never))
    }
}

/// The sheet: one rectangle per cell, coloured by what is in it, and a card for each
/// Thought placed there (its grounded image, or what is known about it).
struct MentalModelSheet: View {
    let state: MentalModelState
    let tapCell: (String) -> Void

    @State private var zoom: CGFloat = 1
    @GestureState private var pinch: CGFloat = 1
    @State private var offset: CGSize = .zero
    @GestureState private var drag: CGSize = .zero

    private static let steelBlue = Color(red: 0.27, green: 0.51, blue: 0.71)
    private static let aliceBlue = Color(red: 0.94, green: 0.97, blue: 1.0)
    private static let darkBlue = Color(red: 0, green: 0, blue: 0.55)

    /// The desktop's cell colours; later rules win.
    private func fill(_ cell: MentalCell) -> Color {
        if cell.attended { return .green }
        if cell.contents != nil { return .yellow }
        if let ago = cell.firedAgo, ago < 1 { return Self.aliceBlue }
        if cell.isCenter { return .pink }
        if cell.inView { return Self.steelBlue }
        return Self.darkBlue
    }

    var body: some View {
        GeometryReader { geo in
            let size = geo.size
            let scale = zoom * pinch
            ZStack(alignment: .topLeading) {
                Canvas { ctx, _ in
                    for ring in state.rings {
                        for cell in ring.cells {
                            let r = rect(cell, ring, size)
                            ctx.fill(Path(r), with: .color(fill(cell)))
                            ctx.stroke(Path(r), with: .color(.gray), lineWidth: 0.5)
                        }
                    }
                }
                ForEach(cards(size), id: \.id) { card in
                    ContentCard(content: card.content, opacity: card.opacity, maxAttributes: state.cardAttributes ?? 3)
                        .scaleEffect(card.scale, anchor: .center)
                        .position(card.center)
                        .zIndex(card.z)
                }
            }
            .frame(width: size.width, height: size.height)
            .scaleEffect(scale)
            .offset(x: offset.width + drag.width, y: offset.height + drag.height)
            .contentShape(Rectangle())
            .onTapGesture { location in
                // undo pan and zoom (around the centre) to find the cell under the finger
                let cx = size.width / 2, cy = size.height / 2
                let p = CGPoint(x: (location.x - cx - offset.width) / scale + cx,
                                y: (location.y - cy - offset.height) / scale + cy)
                if let cell = cell(at: p, size) { tapCell(cell.label) }
            }
            .gesture(MagnifyGesture()
                .updating($pinch) { v, s, _ in s = v.magnification }
                .onEnded { zoom = min(4, max(0.25, zoom * $0.magnification)) })
            .simultaneousGesture(DragGesture(minimumDistance: 8)
                .updating($drag) { v, s, _ in s = v.translation }
                .onEnded { offset.width += $0.translation.width; offset.height += $0.translation.height })
        }
        .aspectRatio(1.6, contentMode: .fit)
        .clipped()
        .overlay(alignment: .bottomTrailing) {
            Button("Reset View", systemImage: "arrow.up.left.and.down.right.magnifyingglass") {
                withAnimation { zoom = 1; offset = .zero }
            }
            .labelStyle(.iconOnly)
            .buttonStyle(.bordered)
            .padding(6)
        }
    }

    /// Azimuth −180…180 runs left to right, elevation 90…−90 top to bottom.
    private func rect(_ cell: MentalCell, _ ring: MentalRing, _ size: CGSize) -> CGRect {
        let x0 = (cell.azimuthFrom / 180 + 1) / 2, x1 = (cell.azimuthTo / 180 + 1) / 2
        let y0 = (90 - ring.elevationTo) / 180, y1 = (90 - ring.elevationFrom) / 180
        return CGRect(x: x0 * size.width, y: y0 * size.height,
                      width: max(0.5, (x1 - x0) * size.width), height: (y1 - y0) * size.height)
    }

    private func cell(at p: CGPoint, _ size: CGSize) -> MentalCell? {
        for ring in state.rings {
            for cell in ring.cells where rect(cell, ring, size).contains(p) { return cell }
        }
        return nil
    }

    private struct PlacedCard {
        let id: String
        let content: MentalContent
        let center: CGPoint
        let scale: CGFloat
        let opacity: Double
        let z: Double
    }

    /// Nearer Thoughts are bigger and drawn on top; imagined ones are faint, and ones about
    /// to be forgotten fade out over their last five seconds.
    private func cards(_ size: CGSize) -> [PlacedCard] {
        var out: [PlacedCard] = []
        for ring in state.rings {
            for cell in ring.cells {
                let r = rect(cell, ring, size)
                // farthest first, so nearer ones stack above them
                let contents = (cell.contents ?? []).enumerated().sorted {
                    $0.element.distance != $1.element.distance ? $0.element.distance > $1.element.distance : $0.offset < $1.offset
                }.map(\.element)
                for (i, content) in contents.enumerated() {
                    let distance = content.distance <= 0 ? 1 : content.distance
                    var opacity = content.imagined ? min(1, max(0, state.imaginedOpacity)) : 1
                    if let left = content.secondsLeft, left < 5 { opacity *= max(0, left / 5) }
                    out.append(PlacedCard(
                        id: "\(cell.label)|\(content.label)|\(i)", content: content,
                        center: CGPoint(x: r.midX + CGFloat(i) * 14, y: r.midY + CGFloat(i) * 8),
                        scale: min(1, max(0.25, 1 / max(1, distance))), opacity: opacity,
                        z: 10 + (100_000 / min(10_000, max(1, distance))).rounded() + Double(i)))
                }
            }
        }
        return out
    }
}

/// A Thought in the mental model: its grounded image, or a card of what is known about it.
private struct ContentCard: View {
    let content: MentalContent
    let opacity: Double
    let maxAttributes: Int

    var body: some View {
        VStack(spacing: 2) {
            if let path = content.imagePath, let image = UIImage(contentsOfFile: path) {
                Image(uiImage: image).resizable().scaledToFill().frame(width: 72, height: 72).clipped()
                Text(title).font(.caption2).lineLimit(1)
            } else {
                Text(title).font(.caption.weight(.semibold)).lineLimit(1)
                Text(attributes).font(.system(size: 9)).lineLimit(4)
            }
        }
        .foregroundStyle(.black)
        .padding(4)
        .frame(maxWidth: content.imagePath == nil ? 150 : 80)
        .background(content.imagePath == nil ? Color(red: 0.98, green: 0.98, blue: 0.82) : .white,
                    in: RoundedRectangle(cornerRadius: 3))
        .overlay(RoundedRectangle(cornerRadius: 3).stroke(content.imagined ? Color.gray : Color(white: 0.4)))
        .opacity(opacity)
        .allowsHitTesting(false)
    }

    private var title: String { content.label + (content.imagined ? " (imagined)" : "") }

    /// "[relation->target] ...", sorted, capped at the module's card size.
    private var attributes: String {
        var seen = Set<String>()
        let all = (content.attributes ?? []).filter { $0.count == 2 }
            .map { "[\($0[0])->\($0[1])]" }
            .filter { seen.insert($0.lowercased()).inserted }
            .sorted { $0.localizedCaseInsensitiveCompare($1) == .orderedAscending }
        if all.isEmpty { return "No known attributes" }
        let take = max(1, maxAttributes)
        let summary = all.prefix(take).joined(separator: " ")
        return all.count > take ? summary + " ..." : summary
    }
}
