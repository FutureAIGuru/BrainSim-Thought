import PhotosUI
import SwiftUI
import UIKit

/// A dialog's camera button: take a picture (or pick a photo when there is no camera,
/// as in the Simulator), save it as a grounded image, and hand the module its path.
///
/// Photos go to Documents/ObjectCamera/Images so modules can ground Thoughts in them
/// (GroundedImageResolver looks for images in <root>/Images). The object is assumed to be
/// in the middle of the frame, so the photo is cropped to the centre square and scaled
/// down; the vision pipeline only looks at 50×50 pixels anyway.
struct CameraControl: View {
    var title = "Take Picture"
    let captured: (String) -> Void

    @State private var showingCamera = false
    @State private var pickedItem: PhotosPickerItem?
    @State private var failure: String?

    private let hasCamera = UIImagePickerController.isSourceTypeAvailable(.camera)

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack(spacing: 10) {
                if hasCamera {
                    Button {
                        showingCamera = true
                    } label: {
                        Label(title, systemImage: "camera.fill")
                            .frame(maxWidth: .infinity)
                    }
                    .buttonStyle(.borderedProminent)
                }
                PhotosPicker(selection: $pickedItem, matching: .images) {
                    Label(hasCamera ? "Photo" : "Choose Photo", systemImage: "photo.on.rectangle")
                        .frame(maxWidth: hasCamera ? nil : .infinity)
                }
                .buttonStyle(.bordered)
            }
            if !hasCamera {
                Text("No camera here — choose a photo instead.").font(.caption).foregroundStyle(.secondary)
            }
            if let failure {
                Text(failure).font(.caption).foregroundStyle(.red)
            }
        }
        .fullScreenCover(isPresented: $showingCamera) {
            CameraPicker { image in
                showingCamera = false
                if let image { save(image) }
            }
            .ignoresSafeArea()
        }
        .onChange(of: pickedItem) {
            guard let item = pickedItem else { return }
            pickedItem = nil
            Task {
                if let data = try? await item.loadTransferable(type: Data.self), let image = UIImage(data: data) {
                    save(image)
                } else {
                    failure = "Couldn't read that photo."
                }
            }
        }
    }

    /// Cropping, scaling and JPEG-encoding a full-size photo takes a while, so it runs off
    /// the main thread.
    private func save(_ image: UIImage) {
        Task {
            do {
                let url = try await Task.detached(priority: .userInitiated) { try Self.store(image) }.value
                failure = nil
                captured(url.path(percentEncoded: false))
            } catch {
                failure = error.localizedDescription
            }
        }
    }

    nonisolated static let imagesFolder = URL.documentsDirectory.appending(path: "ObjectCamera/Images")

    /// Centre square, at most `side` pixels, orientation applied, as JPEG.
    nonisolated static func store(_ image: UIImage, side: CGFloat = 480) throws -> URL {
        let size = image.size
        let crop = min(size.width, size.height)
        let scale = min(1, side / crop)
        let target = CGSize(width: crop * scale, height: crop * scale)
        let format = UIGraphicsImageRendererFormat()
        format.scale = 1
        let square = UIGraphicsImageRenderer(size: target, format: format).image { _ in
            let origin = CGPoint(x: -(size.width - crop) / 2 * scale, y: -(size.height - crop) / 2 * scale)
            image.draw(in: CGRect(origin: origin, size: CGSize(width: size.width * scale, height: size.height * scale)))
        }
        guard let data = square.jpegData(compressionQuality: 0.9) else {
            throw CocoaError(.fileWriteUnknown)
        }
        try FileManager.default.createDirectory(at: imagesFolder, withIntermediateDirectories: true)
        let stamp = Date.now.formatted(.iso8601.year().month().day().time(includingFractionalSeconds: false))
            .replacingOccurrences(of: ":", with: "")
        // the time alone is only unique to the second; each file is an Appearance's photo
        let suffix = UUID().uuidString.prefix(4).lowercased()
        let url = imagesFolder.appending(path: "cam-\(stamp)-\(suffix).jpg")
        try data.write(to: url, options: .atomic)
        return url
    }
}

/// UIImagePickerController in camera mode.
private struct CameraPicker: UIViewControllerRepresentable {
    let done: (UIImage?) -> Void

    func makeUIViewController(context: Context) -> UIImagePickerController {
        let picker = UIImagePickerController()
        picker.sourceType = .camera
        picker.cameraCaptureMode = .photo
        picker.delegate = context.coordinator
        return picker
    }

    func updateUIViewController(_ controller: UIImagePickerController, context: Context) {}

    func makeCoordinator() -> Coordinator { Coordinator(done: done) }

    final class Coordinator: NSObject, UIImagePickerControllerDelegate, UINavigationControllerDelegate {
        let done: (UIImage?) -> Void
        init(done: @escaping (UIImage?) -> Void) { self.done = done }

        func imagePickerController(_ picker: UIImagePickerController,
                                   didFinishPickingMediaWithInfo info: [UIImagePickerController.InfoKey: Any]) {
            done(info[.originalImage] as? UIImage)
        }

        func imagePickerControllerDidCancel(_ picker: UIImagePickerController) { done(nil) }
    }
}
