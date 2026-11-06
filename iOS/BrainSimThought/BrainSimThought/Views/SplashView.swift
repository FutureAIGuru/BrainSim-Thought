import SwiftUI

/// Animated splash shown after the static launch screen (LaunchScreen.storyboard).
/// Its first frame matches the launch screen exactly (logo centered on the splash
/// purple), then it reveals the desktop app's splash artwork and titles.
struct SplashView: View {
    var onFinished: () -> Void

    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @State private var revealed = false

    var body: some View {
        ZStack {
            // The desktop splash's network artwork, blurred into a backdrop. It lives in an
            // overlay on a flexible color so its fill size never widens the layout.
            Color("SplashBackground")
                .overlay {
                    Image("SplashArt")
                        .resizable()
                        .scaledToFill()
                        .blur(radius: 24)
                        .opacity(revealed ? 0.55 : 0)
                }
                .clipped()
                .accessibilityHidden(true)

            VStack(spacing: 18) {
                Image("LaunchLogo")
                    .resizable()
                    .scaledToFit()
                    .frame(width: 150, height: 150)
                    .accessibilityHidden(true)

                VStack(spacing: 6) {
                    Text("Brain Simulator Thought")
                        .font(.largeTitle.bold().italic())
                    Text("Technologies That Think™")
                        .font(.headline)
                        .foregroundStyle(.white.opacity(0.85))
                }
                .foregroundStyle(.white)
                .multilineTextAlignment(.center)
                .opacity(revealed ? 1 : 0)
                // Collapsed until revealed so the logo starts exactly where the launch screen put it.
                .frame(height: revealed ? nil : 0)
            }
            .offset(y: revealed && !reduceMotion ? -20 : 0)

            VStack {
                Spacer()
                Text("Future AI Society")
                    .font(.subheadline.weight(.semibold))
                    .tracking(2)
                    .textCase(.uppercase)
                    .foregroundStyle(.white.opacity(0.8))
                    .padding(.bottom, 48)
            }
            .opacity(revealed ? 1 : 0)
        }
        .ignoresSafeArea()
        .accessibilityElement(children: .combine)
        .accessibilityLabel("Brain Simulator Thought, Future AI Society")
        .task {
            withAnimation(.easeOut(duration: reduceMotion ? 0.2 : 0.7)) { revealed = true }
            try? await Task.sleep(for: .seconds(1.6))
            onFinished()
        }
    }
}

#Preview {
    SplashView {}
}
