import SwiftUI

/// Interactive node-link view of the UKS.
/// - Drag a node to move it (it stays pinned where dropped); drag the background to pan.
/// - Pinch to zoom. Tap a node to select it; double-tap to re-center the graph on it.
struct GraphView: View {
    @Environment(UKSStore.self) private var store
    var layout: ForceLayout
    @Binding var selected: String?
    /// Double-tap on a node.
    var onFocus: (String) -> Void

    @State private var scale: CGFloat = 1
    @State private var baseScale: CGFloat = 1
    @State private var offset: CGSize = .zero
    @State private var baseOffset: CGSize = .zero
    @State private var gesture: DragMode?
    @State private var lastTap: (id: String, time: Date)?

    private enum DragMode {
        case node(Int, grabOffset: CGSize)
        case pan
    }

    var body: some View {
        GeometryReader { geo in
            let center = CGPoint(x: geo.size.width / 2, y: geo.size.height / 2)
            // Read everything the drawing depends on here so observation re-renders the canvas.
            let nodes = layout.nodes
            let _ = (scale, offset, selected, layout.edges.count)
            Canvas { ctx, _ in
                draw(in: &ctx, center: center, nodes: nodes)
            }
            .contentShape(Rectangle())
            .gesture(drag(center: center))
            .simultaneousGesture(zoom)
            .onAppear { layout.onSettled = { fit(in: geo.size) } }
            .onChange(of: geo.size) { _, size in fit(in: size) }
            .overlay(alignment: .bottomTrailing) {
                zoomControls(size: geo.size)
            }
        }
        .background(Color(.systemGroupedBackground))
        .accessibilityElement(children: .contain)
        .accessibilityLabel("Knowledge graph with \(layout.nodes.count) Thoughts")
    }

    // MARK: - Coordinates

    private func toScreen(_ p: CGPoint, _ center: CGPoint) -> CGPoint {
        CGPoint(x: center.x + offset.width + p.x * scale, y: center.y + offset.height + p.y * scale)
    }

    private func toGraph(_ p: CGPoint, _ center: CGPoint) -> CGPoint {
        CGPoint(x: (p.x - center.x - offset.width) / scale, y: (p.y - center.y - offset.height) / scale)
    }

    private func hitTest(_ p: CGPoint, _ center: CGPoint) -> Int? {
        let g = toGraph(p, center)
        let slop = 10 / scale // generous touch target
        var best: (Int, CGFloat)?
        for (i, n) in layout.nodes.enumerated() {
            let d = hypot(n.position.x - g.x, n.position.y - g.y)
            if d <= n.radius + slop, d < (best?.1 ?? .infinity) { best = (i, d) }
        }
        return best?.0
    }

    // MARK: - Drawing

    private func draw(in ctx: inout GraphicsContext, center: CGPoint, nodes: [ForceLayout.Node]) {
        let selectedIndex = selected.flatMap { layout.index(of: $0) }
        let neighborhood = selectedIndex.map { layout.adjacency[$0].union([$0]) }
        let showEdgeLabels = scale > 0.55

        func isDimmed(_ i: Int) -> Bool { neighborhood.map { !$0.contains(i) } ?? false }

        // Edges
        for e in layout.edges {
            let a = nodes[e.from], b = nodes[e.to]
            let pa = toScreen(a.position, center), pb = toScreen(b.position, center)
            let dx = pb.x - pa.x, dy = pb.y - pa.y
            let len = max(hypot(dx, dy), 0.001)
            let ux = dx / len, uy = dy / len
            // Stop at node rims so arrowheads stay visible
            let start = CGPoint(x: pa.x + ux * a.radius * scale, y: pa.y + uy * a.radius * scale)
            let end = CGPoint(x: pb.x - ux * b.radius * scale, y: pb.y - uy * b.radius * scale)
            guard hypot(end.x - start.x, end.y - start.y) > 4 else { continue }

            let dim = isDimmed(e.from) || isDimmed(e.to)
            let color: Color = e.info.isA ? .secondary : .accentColor
            let opacity = dim ? 0.12 : (e.info.isA ? 0.55 : 0.85)

            var line = Path()
            line.move(to: start)
            line.addLine(to: end)
            ctx.stroke(line, with: .color(color.opacity(opacity)),
                       style: StrokeStyle(lineWidth: e.info.isA ? 1 : 1.5, dash: e.info.isA ? [4, 3] : []))

            // Arrowhead
            let size: CGFloat = 7
            var head = Path()
            head.move(to: end)
            head.addLine(to: CGPoint(x: end.x - ux * size - uy * size * 0.5, y: end.y - uy * size + ux * size * 0.5))
            head.addLine(to: CGPoint(x: end.x - ux * size + uy * size * 0.5, y: end.y - uy * size - ux * size * 0.5))
            head.closeSubpath()
            ctx.fill(head, with: .color(color.opacity(opacity)))

            if showEdgeLabels && !e.info.isA && !dim {
                let mid = CGPoint(x: (start.x + end.x) / 2, y: (start.y + end.y) / 2)
                let text = ctx.resolve(Text(e.info.linkType).font(.caption2).foregroundStyle(Color.accentColor))
                let sz = text.measure(in: CGSize(width: 200, height: 40))
                let bg = CGRect(x: mid.x - sz.width / 2 - 3, y: mid.y - sz.height / 2 - 1, width: sz.width + 6, height: sz.height + 2)
                ctx.fill(Path(roundedRect: bg, cornerRadius: 4), with: .color(Color(.systemGroupedBackground).opacity(0.9)))
                ctx.draw(text, at: mid)
            }
        }

        // Nodes
        for (i, n) in nodes.enumerated() {
            let p = toScreen(n.position, center)
            let r = n.radius * scale
            let rect = CGRect(x: p.x - r, y: p.y - r, width: r * 2, height: r * 2)
            let dim = isDimmed(i)
            let isRoot = n.id == layout.root
            let fill: Color = isRoot ? .accentColor : (n.info.isLinkType ? .purple : (n.info.isLink ? .orange : .teal))

            ctx.fill(Path(ellipseIn: rect), with: .color(fill.opacity(dim ? 0.15 : (isRoot ? 0.9 : 0.35))))
            ctx.stroke(Path(ellipseIn: rect), with: .color(fill.opacity(dim ? 0.2 : 1)), lineWidth: 1.5)
            if i == selectedIndex {
                ctx.stroke(Path(ellipseIn: rect.insetBy(dx: -4, dy: -4)), with: .color(.orange), lineWidth: 2.5)
            }
            if n.pinned {
                let pin = CGRect(x: rect.maxX - 5, y: rect.minY - 1, width: 6, height: 6)
                ctx.fill(Path(ellipseIn: pin), with: .color(.orange.opacity(dim ? 0.3 : 1)))
            }

            let label = ctx.resolve(
                Text(n.info.label)
                    .font(isRoot || i == selectedIndex ? .caption.bold() : .caption)
                    .foregroundStyle(Color.primary.opacity(dim ? 0.3 : 1))
            )
            ctx.draw(label, at: CGPoint(x: p.x, y: rect.maxY + 3), anchor: .top)
        }
    }

    // MARK: - Gestures

    private func drag(center: CGPoint) -> some Gesture {
        DragGesture(minimumDistance: 0)
            .onChanged { value in
                if gesture == nil {
                    if let i = hitTest(value.startLocation, center) {
                        let g = toGraph(value.startLocation, center)
                        let p = layout.nodes[i].position
                        gesture = .node(i, grabOffset: CGSize(width: p.x - g.x, height: p.y - g.y))
                    } else {
                        gesture = .pan
                    }
                }
                let moved = hypot(value.translation.width, value.translation.height) > 4
                switch gesture {
                case let .node(i, grab):
                    guard moved else { return }
                    layout.beginDrag(i)
                    let g = toGraph(value.location, center)
                    layout.drag(i, to: CGPoint(x: g.x + grab.width, y: g.y + grab.height))
                case .pan:
                    offset = CGSize(width: baseOffset.width + value.translation.width,
                                    height: baseOffset.height + value.translation.height)
                case nil: break
                }
            }
            .onEnded { value in
                let isTap = hypot(value.translation.width, value.translation.height) <= 4
                switch gesture {
                case let .node(i, _):
                    layout.endDrag(i)
                    if isTap { tapped(layout.nodes[i].id) }
                case .pan:
                    baseOffset = offset
                    if isTap { selected = nil }
                case nil: break
                }
                gesture = nil
            }
    }

    private var zoom: some Gesture {
        MagnifyGesture()
            .onChanged { value in
                scale = min(max(baseScale * value.magnification, 0.15), 4)
            }
            .onEnded { _ in baseScale = scale }
    }

    private func tapped(_ id: String) {
        let now = Date()
        if let last = lastTap, last.id == id, now.timeIntervalSince(last.time) < 0.35 {
            lastTap = nil
            onFocus(id)
        } else {
            lastTap = (id, now)
            selected = id
        }
    }

    // MARK: - Zoom helpers

    private func zoomControls(size: CGSize) -> some View {
        VStack(spacing: 0) {
            zoomButton("Zoom In", "plus") { setScale(scale * 1.4) }
            Divider()
            zoomButton("Zoom Out", "minus") { setScale(scale / 1.4) }
            Divider()
            zoomButton("Fit to Screen", "arrow.up.left.and.down.right.magnifyingglass") { fit(in: size) }
        }
        .frame(width: 44)
        .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 12))
        .padding(12)
    }

    private func zoomButton(_ title: String, _ icon: String, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            Image(systemName: icon)
                .font(.body.weight(.medium))
                .frame(width: 44, height: 44)
                .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .foregroundStyle(.tint)
        .accessibilityLabel(title)
    }

    private func setScale(_ s: CGFloat) {
        withAnimation(.snappy) {
            scale = min(max(s, 0.15), 4)
            baseScale = scale
        }
    }

    /// Scales and centers so every node is visible.
    func fit(in size: CGSize) {
        guard let b = layout.bounds(), size.width > 0, size.height > 0 else { return }
        let margin: CGFloat = 40
        let s = min((size.width - margin * 2) / max(b.width, 1), (size.height - margin * 2) / max(b.height, 1))
        withAnimation(.snappy) {
            scale = min(max(s, 0.15), 1.6)
            baseScale = scale
            offset = CGSize(width: -b.midX * scale, height: -b.midY * scale)
            baseOffset = offset
        }
    }
}
