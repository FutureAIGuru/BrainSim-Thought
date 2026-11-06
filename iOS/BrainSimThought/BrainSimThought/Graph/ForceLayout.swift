import Foundation
import CoreGraphics
import Observation

/// A small force-directed layout (d3-force style): nodes repel each other, edges act as
/// springs, and a weak pull keeps the graph centered. The simulation "cools" (alpha decays)
/// and stops on its own; dragging a node reheats it so neighbors follow.
@MainActor
@Observable
final class ForceLayout {
    struct Node: Identifiable {
        let id: String
        var info: UKSGraph.Node
        var position: CGPoint
        var velocity = CGVector.zero
        /// Pinned nodes stay where the user put them.
        var pinned = false
        var radius: CGFloat { 11 + min(CGFloat(info.childCount), 25) * 0.5 }
    }

    struct Edge: Identifiable {
        let id: Int
        let from: Int
        let to: Int
        let info: UKSGraph.Edge
    }

    private(set) var nodes: [Node] = []
    private(set) var edges: [Edge] = []
    private(set) var root: String?
    private(set) var truncated = false
    private var indexByID: [String: Int] = [:]

    /// Neighbors per node index, for highlighting.
    private(set) var adjacency: [Set<Int>] = []

    // Simulation state
    private var alpha: Double = 0
    private var alphaTarget: Double = 0
    private let alphaMin = 0.002
    private let alphaDecay = 0.025
    private let velocityDecay = 0.55
    private var loop: Task<Void, Never>?

    /// Called once each time a freshly loaded graph has mostly settled (e.g. to zoom-to-fit).
    var onSettled: (() -> Void)?
    private var awaitingSettle = false

    var isRunning: Bool { loop != nil }

    // MARK: - Loading

    func load(_ graph: UKSGraph) {
        let previous = Dictionary(uniqueKeysWithValues: nodes.map { ($0.id, $0) })
        root = graph.root
        truncated = graph.truncated

        var newNodes: [Node] = []
        for (i, info) in graph.nodes.enumerated() {
            if var existing = previous[info.id] {
                existing.info = info
                newNodes.append(existing)
            } else {
                newNodes.append(Node(id: info.id, info: info, position: Self.seedPosition(i)))
            }
        }
        nodes = newNodes
        indexByID = Dictionary(uniqueKeysWithValues: nodes.enumerated().map { ($1.id, $0) })

        edges = graph.edges.enumerated().compactMap { i, e in
            guard let a = indexByID[e.source], let b = indexByID[e.target], a != b else { return nil }
            return Edge(id: i, from: a, to: b, info: e)
        }
        adjacency = Array(repeating: [], count: nodes.count)
        for e in edges {
            adjacency[e.from].insert(e.to)
            adjacency[e.to].insert(e.from)
        }

        // Place brand-new nodes next to an already-placed neighbor so they unfold outward.
        for i in nodes.indices where previous[nodes[i].id] == nil {
            if let n = adjacency[i].first(where: { previous[nodes[$0].id] != nil }) {
                let p = nodes[n].position
                nodes[i].position = CGPoint(x: p.x + .random(in: -30...30), y: p.y + .random(in: -30...30))
            }
        }
        // Keep the focus Thought at the middle of the canvas.
        if let r = root, let i = indexByID[r], previous[r] == nil {
            nodes[i].position = .zero
        }

        // Keep a pending fit request if a reload arrives before the previous load settled.
        awaitingSettle = awaitingSettle || previous.isEmpty || graph.nodes.contains { previous[$0.id] == nil }
        reheat(1)
    }

    /// Phyllotaxis spiral: an even, deterministic starting spread.
    private static func seedPosition(_ i: Int) -> CGPoint {
        let r = 22 * sqrt(Double(i) + 0.5)
        let a = Double(i) * .pi * (3 - sqrt(5))
        return CGPoint(x: r * cos(a), y: r * sin(a))
    }

    func index(of id: String) -> Int? { indexByID[id] }

    // MARK: - Interaction

    func beginDrag(_ i: Int) {
        nodes[i].pinned = true
        alphaTarget = 0.3
        reheat(max(alpha, 0.3))
    }

    func drag(_ i: Int, to p: CGPoint) {
        nodes[i].position = p
        nodes[i].velocity = .zero
    }

    func endDrag(_ i: Int) {
        alphaTarget = 0 // node stays pinned where it was dropped
    }

    func unpin(_ i: Int) {
        nodes[i].pinned = false
        reheat(0.3)
    }

    /// Unpins everything and scatters slightly so the layout is recomputed from scratch.
    func relayout() {
        for i in nodes.indices {
            nodes[i].pinned = false
            nodes[i].position = Self.seedPosition(i)
            nodes[i].velocity = .zero
        }
        if let r = root, let i = indexByID[r] { nodes[i].position = .zero }
        awaitingSettle = true
        reheat(1)
    }

    func bounds() -> CGRect? {
        guard let first = nodes.first else { return nil }
        var rect = CGRect(origin: first.position, size: .zero)
        for n in nodes { rect = rect.union(CGRect(origin: n.position, size: .zero).insetBy(dx: -n.radius, dy: -n.radius)) }
        return rect
    }

    // MARK: - Simulation

    private func reheat(_ to: Double) {
        alpha = max(alpha, to)
        guard loop == nil else { return }
        loop = Task { [weak self] in
            while let self, !Task.isCancelled {
                self.tick()
                if self.alpha < self.alphaMin && self.alphaTarget == 0 { break }
                try? await Task.sleep(for: .milliseconds(16))
            }
            self?.loop = nil
        }
    }

    private func tick() {
        alpha += (alphaTarget - alpha) * alphaDecay
        let n = nodes.count
        guard n > 0 else { return }
        var pos = nodes.map(\.position)
        var vel = nodes.map(\.velocity)

        // Repulsion (many-body), O(n²) is fine at a few hundred nodes.
        let charge = 900.0 * alpha
        for i in 0..<n {
            for j in (i + 1)..<n {
                var dx = pos[j].x - pos[i].x, dy = pos[j].y - pos[i].y
                if dx == 0 && dy == 0 { dx = .random(in: -1...1); dy = .random(in: -1...1) }
                let d2 = max(dx * dx + dy * dy, 25)
                if d2 > 250_000 { continue } // ignore beyond 500pt
                let f = charge / d2
                vel[i].dx -= dx * f; vel[i].dy -= dy * f
                vel[j].dx += dx * f; vel[j].dy += dy * f
            }
        }

        // Springs along edges; is-a links are shorter so taxonomies cluster.
        for e in edges {
            let dx = pos[e.to].x + vel[e.to].dx - pos[e.from].x - vel[e.from].dx
            let dy = pos[e.to].y + vel[e.to].dy - pos[e.from].y - vel[e.from].dy
            let l = max(sqrt(dx * dx + dy * dy), 1)
            let rest: CGFloat = e.info.isA ? 70 : 120
            let k = (l - rest) / l * alpha * 0.35
            vel[e.to].dx -= dx * k * 0.5; vel[e.to].dy -= dy * k * 0.5
            vel[e.from].dx += dx * k * 0.5; vel[e.from].dy += dy * k * 0.5
        }

        // Weak gravity toward the origin keeps disconnected pieces on screen.
        for i in 0..<n {
            vel[i].dx -= pos[i].x * 0.02 * alpha
            vel[i].dy -= pos[i].y * 0.02 * alpha
        }

        for i in 0..<n {
            if nodes[i].pinned {
                vel[i] = .zero
                continue
            }
            vel[i].dx *= velocityDecay
            vel[i].dy *= velocityDecay
            pos[i].x += vel[i].dx
            pos[i].y += vel[i].dy
        }
        for i in 0..<n {
            nodes[i].position = pos[i]
            nodes[i].velocity = vel[i]
        }

        if awaitingSettle && alpha < 0.08 {
            awaitingSettle = false
            onSettled?()
        }
    }
}
