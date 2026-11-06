import Foundation

/// Mirrors UKSNative's ModuleInfoDto: one active module (desktop MainWindow's module list).
struct ModuleInfo: Decodable, Hashable, Identifiable {
    let label: String
    let type: String
    let enabled: Bool
    let revision: Int
    var id: String { label }

    /// "ModuleTextNEW" → "Text NEW"
    var displayType: String { ModuleInfo.display(type) }

    static func display(_ type: String) -> String {
        let name = Array(type.hasPrefix("Module") ? String(type.dropFirst(6)) : type)
        var out = ""
        for i in name.indices {
            let ch = name[i]
            if i > 0, ch.isUppercase {
                let prev = name[i - 1], next = i + 1 < name.count ? name[i + 1] : " "
                // "AddCounts" → "Add Counts", "UKSQuery" → "UKS Query"
                if prev.isLowercase || (prev.isUppercase && next.isLowercase) { out.append(" ") }
            }
            out.append(ch)
        }
        return out
    }
}

struct ModuleNotice: Decodable, Hashable {
    let title: String
    let text: String
}

/// What a module operation reports back (UKSNative's MessageDto), if anything.
struct ModuleCallResult: Decodable {
    let message: String?
}
