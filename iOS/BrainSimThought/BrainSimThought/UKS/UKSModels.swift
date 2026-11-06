import Foundation

/// Mirrors UKSNative's LinkDto. From/LinkType/To are display strings (a nested Link
/// renders as `[a → b → c]`); the *Label fields are set only for plain, labeled Thoughts.
struct UKSLink: Decodable, Hashable, Identifiable {
    let label: String
    let from: String
    let linkType: String
    let to: String
    let fromLabel: String?
    let linkTypeLabel: String?
    let toLabel: String?
    let weight: Float
    let inheritanceDepth: Int
    let inheritedFrom: String?
    let isConditional: Bool
    let text: String
    /// Seconds until the link is forgotten; nil when permanent.
    let timeToLive: Double?

    var id: String { "\(label)|\(text)|\(inheritanceDepth)" }
}

/// Mirrors UKSNative's ThoughtDto.
struct UKSThought: Decodable, Hashable {
    let label: String
    let value: String?
    let weight: Float
    let isLink: Bool
    let isSequence: Bool
    let text: String
    let parents: [String]
    let children: [String]
    let linksTo: [UKSLink]
    let linksFrom: [UKSLink]
}

/// Mirrors UKSNative's GraphDto: a neighborhood of the UKS for the graph view.
struct UKSGraph: Decodable {
    struct Node: Decodable, Hashable {
        let id: String
        let label: String
        let depth: Int
        let childCount: Int
        let isLinkType: Bool
        let isLink: Bool
    }
    struct Edge: Decodable, Hashable {
        let source: String
        let target: String
        let linkType: String
        let isA: Bool
        let weight: Float
    }
    let root: String?
    let nodes: [Node]
    let edges: [Edge]
    let truncated: Bool
}

struct UKSQueryResult: Decodable {
    var thoughts: [String] = []
    var links: [UKSLink] = []
}

struct UKSScored: Decodable, Hashable, Identifiable {
    let label: String
    let confidence: Float
    var id: String { label }
}

/// Generic {ok, error, message, link} result for mutating calls.
struct UKSOpResult: Decodable {
    let ok: Bool
    let error: String?
    let message: String?
    let link: UKSLink?

    /// How UKSNative serializes a failed call (ResultDto with Ok first).
    static let failurePrefix = Data(#"{"ok":false"#.utf8)
}

struct UKSError: LocalizedError {
    let message: String
    var errorDescription: String? { message }

    /// The UKS was busy with long module work; polls return this instead of waiting for it.
    var isBusy: Bool { message == "busy" }
}
