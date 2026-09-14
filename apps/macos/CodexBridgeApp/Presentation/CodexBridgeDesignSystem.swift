import AppKit
import SwiftUI

enum CodexBridgePalette {
    // Mirrors the final `codexbridge-flat-business-refinement` tokens in the approved prototype.
    static let accent = dynamic(light: NSColor(hex: 0x24476B), dark: NSColor(hex: 0x6F95BA))
    static let accentStrong = dynamic(light: NSColor(hex: 0x24476B), dark: NSColor(hex: 0x7DA0C2))
    static let canvas = dynamic(light: NSColor(hex: 0xF4F5F7), dark: NSColor(hex: 0x181B1F))
    static let sidebar = dynamic(light: NSColor(hex: 0xF0F2F5), dark: NSColor(hex: 0x1B1F24))
    static let surface = dynamic(light: NSColor(hex: 0xFFFFFF), dark: NSColor(hex: 0x202429))
    static let raisedSurface = dynamic(light: NSColor(hex: 0xF7F8FA), dark: NSColor(hex: 0x252A30))
    static let subtleFill = dynamic(light: NSColor(hex: 0xEEF1F4), dark: NSColor(hex: 0x292F36))
    static let selectedFill = dynamic(light: NSColor(hex: 0xE9EFF5), dark: NSColor(hex: 0x263340))
    static let border = dynamic(light: NSColor(hex: 0xDDE2E8), dark: NSColor(hex: 0x323841))
    static let edge = dynamic(light: NSColor(hex: 0xD9DEE5), dark: NSColor(hex: 0x343A43))
    static let desktop = dynamic(light: NSColor(hex: 0xE2E5E9), dark: NSColor(hex: 0x111417))
    static let primaryText = dynamic(light: NSColor(hex: 0x17202A), dark: NSColor(hex: 0xEEF1F4))
    static let secondaryText = dynamic(light: NSColor(hex: 0x697586), dark: NSColor(hex: 0x9AA4AF))
    static let tertiaryText = dynamic(light: NSColor(hex: 0x87919E), dark: NSColor(hex: 0x7F8994))
    static let onPrimary = dynamic(light: NSColor(hex: 0xFFFFFF), dark: NSColor(hex: 0x0F151B))
    static let focus = dynamic(light: NSColor(hex: 0x476B8F), dark: NSColor(hex: 0x7DA0C2))
    static let success = dynamic(light: NSColor(hex: 0x3A7456), dark: NSColor(hex: 0x72A487))
    static let warning = dynamic(light: NSColor(hex: 0x8B692D), dark: NSColor(hex: 0xC2A064))
    static let danger = dynamic(light: NSColor(hex: 0xA24D4D), dark: NSColor(hex: 0xD17B7B))
    static let info = dynamic(light: NSColor(hex: 0x496B89), dark: NSColor(hex: 0x86A9C9))
    static let chatGPT = dynamic(light: NSColor(hex: 0x4B6177), dark: NSColor(hex: 0x8CA1B5))
    static let codex = dynamic(light: NSColor(hex: 0x315A7A), dark: NSColor(hex: 0x7CA3C4))

    static let brandGradient = LinearGradient(
        colors: [accent, accent],
        startPoint: .topLeading,
        endPoint: .bottomTrailing
    )

    private static func dynamic(light: NSColor, dark: NSColor) -> Color {
        Color(nsColor: NSColor(name: nil) { appearance in
            appearance.bestMatch(from: [.darkAqua, .aqua]) == .darkAqua ? dark : light
        })
    }
}

enum CodexBridgeSpacing {
    static let xSmall: CGFloat = 4
    static let small: CGFloat = 8
    static let medium: CGFloat = 12
    static let large: CGFloat = 16
    static let xLarge: CGFloat = 24
}

enum CodexBridgeRadius {
    static let small: CGFloat = 6
    static let medium: CGFloat = 7
    static let large: CGFloat = 8
}

struct CodexBridgeSurfaceModifier: ViewModifier {
    var radius: CGFloat
    var elevated: Bool
    var selected: Bool

    func body(content: Content) -> some View {
        content
            .background(elevated ? CodexBridgePalette.surface : CodexBridgePalette.raisedSurface)
            .clipShape(RoundedRectangle(cornerRadius: radius, style: .continuous))
            .overlay {
                RoundedRectangle(cornerRadius: radius, style: .continuous)
                    .stroke(selected ? CodexBridgePalette.accent : CodexBridgePalette.border, lineWidth: 1)
            }
            .shadow(color: elevated ? Color.black.opacity(0.08) : .clear, radius: elevated ? 16 : 0, y: elevated ? 8 : 0)
    }
}

extension View {
    func codexBridgeSurface(
        radius: CGFloat = CodexBridgeRadius.medium,
        elevated: Bool = false,
        selected: Bool = false
    ) -> some View {
        modifier(CodexBridgeSurfaceModifier(radius: radius, elevated: elevated, selected: selected))
    }
}

struct CodexBridgePrimaryButtonStyle: ButtonStyle {
    var compact = false
    var controlHeight: CGFloat? = nil

    func makeBody(configuration: Configuration) -> some View {
        CodexBridgePrimaryButtonBody(
            configuration: configuration,
            compact: compact,
            controlHeight: controlHeight
        )
    }
}

private struct CodexBridgePrimaryButtonBody: View {
    let configuration: ButtonStyle.Configuration
    let compact: Bool
    let controlHeight: CGFloat?
    @Environment(\.isEnabled) private var isEnabled
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @State private var isHovering = false

    var body: some View {
        configuration.label
            .font(.system(size: compact ? 12 : 13, weight: .semibold))
            .foregroundStyle(CodexBridgePalette.onPrimary.opacity(isEnabled ? 1 : 0.62))
            .padding(.horizontal, compact ? 12 : 16)
            .frame(height: controlHeight ?? (compact ? 32 : 36))
            .background {
                RoundedRectangle(cornerRadius: CodexBridgeRadius.small, style: .continuous)
                    .fill(CodexBridgePalette.accent)
                    .opacity(isEnabled ? (configuration.isPressed ? 0.78 : 1) : 0.48)
            }
            .overlay {
                RoundedRectangle(cornerRadius: CodexBridgeRadius.small, style: .continuous)
                    .stroke(isHovering && isEnabled ? CodexBridgePalette.focus : CodexBridgePalette.accent, lineWidth: 1)
            }
            .animation(reduceMotion ? nil : .easeOut(duration: 0.16), value: configuration.isPressed)
            .animation(reduceMotion ? nil : .easeOut(duration: 0.18), value: isHovering)
            .onHover { isHovering = $0 }
    }
}

struct CodexBridgeSecondaryButtonStyle: ButtonStyle {
    var compact = false
    var controlHeight: CGFloat? = nil

    func makeBody(configuration: Configuration) -> some View {
        CodexBridgeSecondaryButtonBody(
            configuration: configuration,
            compact: compact,
            controlHeight: controlHeight
        )
    }
}

private struct CodexBridgeSecondaryButtonBody: View {
    let configuration: ButtonStyle.Configuration
    let compact: Bool
    let controlHeight: CGFloat?
    @Environment(\.isEnabled) private var isEnabled
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @State private var isHovering = false

    var body: some View {
        configuration.label
            .font(.system(size: compact ? 12 : 13, weight: .medium))
            .foregroundStyle(isEnabled ? CodexBridgePalette.primaryText : CodexBridgePalette.tertiaryText)
            .padding(.horizontal, compact ? 11 : 14)
            .frame(height: controlHeight ?? (compact ? 32 : 36))
            .background(
                isHovering && isEnabled ? CodexBridgePalette.raisedSurface : CodexBridgePalette.surface,
                in: RoundedRectangle(cornerRadius: CodexBridgeRadius.small, style: .continuous)
            )
            .overlay {
                RoundedRectangle(cornerRadius: CodexBridgeRadius.small, style: .continuous)
                    .stroke(CodexBridgePalette.border, lineWidth: 1)
            }
            .opacity(configuration.isPressed ? 0.72 : 1)
            .animation(reduceMotion ? nil : .easeOut(duration: 0.16), value: isHovering)
            .animation(reduceMotion ? nil : .easeOut(duration: 0.12), value: configuration.isPressed)
            .onHover { isHovering = $0 }
    }
}

struct CodexBridgeIconButtonStyle: ButtonStyle {
    var prominent = false

    func makeBody(configuration: Configuration) -> some View {
        CodexBridgeIconButtonBody(configuration: configuration, prominent: prominent)
    }
}

private struct CodexBridgeIconButtonBody: View {
    let configuration: ButtonStyle.Configuration
    let prominent: Bool
    @Environment(\.isEnabled) private var isEnabled
    @State private var isHovering = false

    var body: some View {
        configuration.label
            .font(.system(size: 13, weight: .semibold))
            .foregroundStyle(prominent ? CodexBridgePalette.onPrimary : CodexBridgePalette.primaryText)
            .frame(width: 36, height: 36)
            .background(
                prominent
                    ? AnyShapeStyle(CodexBridgePalette.brandGradient)
                    : AnyShapeStyle(isHovering ? CodexBridgePalette.subtleFill : CodexBridgePalette.surface),
                in: RoundedRectangle(cornerRadius: CodexBridgeRadius.small, style: .continuous)
            )
            .overlay {
                RoundedRectangle(cornerRadius: CodexBridgeRadius.small, style: .continuous)
                    .stroke(prominent ? CodexBridgePalette.accent : CodexBridgePalette.border, lineWidth: 1)
            }
            .opacity(isEnabled ? (configuration.isPressed ? 0.72 : 1) : 0.45)
            .onHover { isHovering = $0 }
    }
}

struct CodexBridgeStatusBadge: View {
    let text: String
    let color: Color
    var symbol: String?

    var body: some View {
        HStack(spacing: 5) {
            if let symbol {
                Image(systemName: symbol)
            } else {
                Circle().fill(color).frame(width: 6, height: 6)
            }
            Text(text).lineLimit(1)
        }
        .font(.system(size: 10.5, weight: .semibold))
        .foregroundStyle(color)
        .padding(.horizontal, 8)
        .frame(height: 23)
        .background(CodexBridgePalette.raisedSurface, in: RoundedRectangle(cornerRadius: 5))
        .overlay { RoundedRectangle(cornerRadius: 5).stroke(CodexBridgePalette.border, lineWidth: 1) }
    }
}

struct CodexBridgeSectionHeader: View {
    let title: String
    var detail: String?
    var actionTitle: String?
    var action: (() -> Void)?

    var body: some View {
        HStack(alignment: .firstTextBaseline) {
            VStack(alignment: .leading, spacing: 3) {
                Text(title).font(.system(size: 13, weight: .semibold))
                if let detail {
                    Text(detail).font(.caption).foregroundStyle(CodexBridgePalette.secondaryText)
                }
            }
            Spacer()
            if let actionTitle, let action {
                Button(actionTitle, action: action)
                    .buttonStyle(CodexBridgeSecondaryButtonStyle(compact: true))
            }
        }
    }
}

struct CodexBridgeEmptyState: View {
    let title: String
    let message: String
    let symbol: String
    var actionTitle: String?
    var action: (() -> Void)?

    var body: some View {
        VStack(spacing: 13) {
            ZStack {
                RoundedRectangle(cornerRadius: CodexBridgeRadius.medium).fill(CodexBridgePalette.raisedSurface)
                Image(systemName: symbol)
                    .font(.system(size: 26, weight: .medium))
                    .foregroundStyle(CodexBridgePalette.accent)
            }
            .frame(width: 58, height: 58)
            Text(title).font(.headline)
            Text(message)
                .font(.subheadline)
                .foregroundStyle(CodexBridgePalette.secondaryText)
                .multilineTextAlignment(.center)
                .frame(maxWidth: 320)
            if let actionTitle, let action {
                Button(actionTitle, action: action)
                    .buttonStyle(CodexBridgeSecondaryButtonStyle())
                    .padding(.top, 2)
            }
        }
        .padding(30)
        .accessibilityElement(children: .combine)
    }
}

struct CodexBridgeLoadingView: View {
    var title = "正在载入"

    var body: some View {
        HStack(spacing: 10) {
            ProgressView().controlSize(.small)
            Text(title).font(.subheadline.weight(.medium))
        }
        .padding(.horizontal, 14)
        .frame(height: 40)
        .background(CodexBridgePalette.surface, in: RoundedRectangle(cornerRadius: CodexBridgeRadius.medium))
        .overlay { RoundedRectangle(cornerRadius: CodexBridgeRadius.medium).stroke(CodexBridgePalette.border, lineWidth: 1) }
        .accessibilityLabel(title)
    }
}

struct CodexBridgeErrorDialog: View {
    let message: String
    let dismiss: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 18) {
            HStack(alignment: .top, spacing: 13) {
                Image(systemName: "exclamationmark.triangle.fill")
                    .font(.system(size: 21))
                    .foregroundStyle(CodexBridgePalette.danger)
                    .frame(width: 42, height: 42)
                    .background(CodexBridgePalette.danger.opacity(0.1), in: RoundedRectangle(cornerRadius: CodexBridgeRadius.medium))
                VStack(alignment: .leading, spacing: 5) {
                    Text("无法完成操作").font(.headline)
                    Text(message)
                        .font(.subheadline)
                        .foregroundStyle(CodexBridgePalette.secondaryText)
                        .fixedSize(horizontal: false, vertical: true)
                }
            }
            HStack {
                Spacer()
                if message == ChatGPTAppCaptureError.permissionRequired.errorDescription {
                    Button("打开系统设置") {
                        if let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility") {
                            NSWorkspace.shared.open(url)
                        }
                    }
                    .buttonStyle(CodexBridgeSecondaryButtonStyle(compact: true))
                    Button("在访达中显示") {
                        NSWorkspace.shared.activateFileViewerSelecting([Bundle.main.bundleURL])
                    }
                    .buttonStyle(CodexBridgeSecondaryButtonStyle(compact: true))
                }
                Button("知道了", action: dismiss)
                    .buttonStyle(CodexBridgePrimaryButtonStyle(compact: true))
                    .keyboardShortcut(.defaultAction)
            }
        }
        .padding(20)
        .frame(width: message == ChatGPTAppCaptureError.permissionRequired.errorDescription ? 520 : 390)
        .codexBridgeSurface(radius: CodexBridgeRadius.large, elevated: true)
        .accessibilityElement(children: .contain)
        .accessibilityAddTraits(.isModal)
    }
}

struct CodexBridgeToast: View {
    let message: String

    var body: some View {
        HStack(spacing: 9) {
            Image(systemName: "checkmark.circle.fill")
                .foregroundStyle(CodexBridgePalette.success)
            Text(message).font(.subheadline.weight(.medium))
        }
        .padding(.horizontal, 15)
        .frame(minHeight: 40)
        .foregroundStyle(CodexBridgePalette.surface)
        .background(CodexBridgePalette.primaryText, in: RoundedRectangle(cornerRadius: CodexBridgeRadius.medium))
        .accessibilityLabel(message)
    }
}

private extension NSColor {
    convenience init(hex: UInt32) {
        self.init(
            calibratedRed: CGFloat((hex >> 16) & 0xFF) / 255,
            green: CGFloat((hex >> 8) & 0xFF) / 255,
            blue: CGFloat(hex & 0xFF) / 255,
            alpha: 1
        )
    }
}
