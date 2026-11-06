import AVFoundation
import Foundation
import ImageIO
import Security
import UKSNative

/// Services the desktop modules get from Windows (files next to the exe, NAudio MIDI,
/// System.Drawing images, app.config) supplied by iOS instead.
enum HostBridge {
    /// Bundled copy of BrainSimulator/UKSContent, WordFIles and Resources (see the
    /// "Copy desktop content" build phase). Relative paths in the modules resolve here.
    static var contentRoot: URL { Bundle.main.bundleURL.appending(path: "Content") }

    private static var installed = false

    static func install() {
        guard !installed else { return }
        installed = true
        uks_set_content_root(contentRoot.path(percentEncoded: false))
        uks_set_midi_callback { message in MidiPlayer.shared.send(message) }
        uks_set_image_decoder(decodeImage)
        if let key = Keychain.read(ModuleEngine.apiKeySetting) {
            ModuleEngine.setSetting(ModuleEngine.apiKeySetting, key)
        }
    }
}

/// System.Drawing.Bitmap(path) for ModuleVision: RGBA8 pixels via ImageIO. A free function so
/// it can be passed to C as a function pointer.
private func decodeImage(_ path: UnsafePointer<CChar>?, _ rgba: UnsafeMutablePointer<UInt8>?, _ capacity: Int32,
                         _ width: UnsafeMutablePointer<Int32>?, _ height: UnsafeMutablePointer<Int32>?) -> Int32 {
    guard let path, let width, let height else { return 0 }
    let url = URL(fileURLWithPath: String(cString: path))
    guard let source = CGImageSourceCreateWithURL(url as CFURL, nil),
          let image = CGImageSourceCreateImageAtIndex(source, 0, nil) else { return 0 }
    width.pointee = Int32(image.width)
    height.pointee = Int32(image.height)
    guard let rgba else { return 1 }
    guard Int(capacity) >= image.width * image.height * 4,
          let context = CGContext(data: rgba, width: image.width, height: image.height, bitsPerComponent: 8,
                                  bytesPerRow: image.width * 4, space: CGColorSpaceCreateDeviceRGB(),
                                  bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else { return 0 }
    context.draw(image, in: CGRect(x: 0, y: 0, width: image.width, height: image.height))
    return 1
}

/// NAudio's MidiOut: short messages from ModuleSoundIn/SoundOut played on a General MIDI sampler.
final class MidiPlayer: @unchecked Sendable {
    static let shared = MidiPlayer()

    private let engine = AVAudioEngine()
    private let sampler = AVAudioUnitSampler()
    private let queue = DispatchQueue(label: "BrainSim MIDI")
    private var started = false

    private init() {
        engine.attach(sampler)
        engine.connect(sampler, to: engine.mainMixerNode, format: nil)
    }

    /// status | data1 << 8 | data2 << 16, as NAudio packs them.
    func send(_ message: Int32) {
        queue.async { [self] in
            startIfNeeded()
            let status = UInt8(message & 0xFF), d1 = UInt8((message >> 8) & 0x7F), d2 = UInt8((message >> 16) & 0x7F)
            let channel = status & 0x0F
            switch status & 0xF0 {
            case 0x90 where d2 > 0: sampler.startNote(d1, withVelocity: d2, onChannel: channel)
            case 0x80, 0x90: sampler.stopNote(d1, onChannel: channel)
            case 0xC0: sampler.sendProgramChange(d1, onChannel: channel)
            case 0xB0: sampler.sendController(d1, withValue: d2, onChannel: channel)
            default: break
            }
        }
    }

    private func startIfNeeded() {
        guard !started else { return }
        started = true
        try? AVAudioSession.sharedInstance().setCategory(.playback, options: .mixWithOthers)
        try? AVAudioSession.sharedInstance().setActive(true)
        try? engine.start()
    }
}

/// Stores app.config-style secrets (the OpenAI API key) in the Keychain.
enum Keychain {
    private static let service = "org.futureaisociety.BrainSimThought"

    static func read(_ account: String) -> String? {
        var item: CFTypeRef?
        let query: [String: Any] = [kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: service,
                                    kSecAttrAccount as String: account, kSecReturnData as String: true]
        guard SecItemCopyMatching(query as CFDictionary, &item) == errSecSuccess, let data = item as? Data else { return nil }
        return String(data: data, encoding: .utf8)
    }

    static func write(_ account: String, _ value: String) {
        let query: [String: Any] = [kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: service,
                                    kSecAttrAccount as String: account]
        SecItemDelete(query as CFDictionary)
        guard !value.isEmpty else { return }
        var add = query
        add[kSecValueData as String] = Data(value.utf8)
        SecItemAdd(add as CFDictionary, nil)
    }
}
