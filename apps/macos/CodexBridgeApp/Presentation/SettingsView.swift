import SwiftUI

struct SettingsView: View {
    @Bindable var model: AppModel
    @Bindable var accessibility: AccessibilityPermissionController
    @AppStorage("codexbridge.appearance") private var appearance = "system"
    @State private var selection: SettingsSection = .general

    var body: some View {
        HStack(spacing: 0) {
            VStack(alignment: .leading, spacing: 18) {
                BrandMark(condensed: true)
                    .padding(.top, 10)

                VStack(spacing: 5) {
                    ForEach(SettingsSection.allCases) { section in
                        SettingsNavigationButton(
                            section: section,
                            selected: selection == section,
                            action: { selection = section }
                        )
                    }
                }

                Spacer()

            }
            .padding(12)
            .frame(width: 184)
            .background(CodexBridgePalette.sidebar)

            Divider().overlay(CodexBridgePalette.border)

            Group {
                switch selection {
                case .general:
                    GeneralSettingsPane(appearance: $appearance)
                case .sources:
                    SourcesView(model: model, accessibility: accessibility, embedded: true)
                case .privacy:
                    LocalDataSettingsPane(model: model)
                case .about:
                    AboutSettingsPane(model: model)
                }
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)
            .background(CodexBridgePalette.canvas)
        }
        .overlay {
            if let error = model.errorMessage {
                ZStack {
                    Color.black.opacity(0.22).ignoresSafeArea()
                    CodexBridgeErrorDialog(message: error, dismiss: model.dismissError)
                }
            }
        }
        .preferredColorScheme(preferredColorScheme)
    }

    private var preferredColorScheme: ColorScheme? {
        switch appearance {
        case "light": .light
        case "dark": .dark
        default: nil
        }
    }
}

private enum SettingsSection: String, CaseIterable, Identifiable {
    case general
    case sources
    case privacy
    case about

    var id: String { rawValue }

    var title: String {
        switch self {
        case .general: "通用"
        case .sources: "来源与权限"
        case .privacy: "本地数据"
        case .about: "关于"
        }
    }

    var symbol: String {
        switch self {
        case .general: "switch.2"
        case .sources: "point.3.connected.trianglepath.dotted"
        case .privacy: "externaldrive"
        case .about: "info.circle"
        }
    }
}

private struct SettingsNavigationButton: View {
    let section: SettingsSection
    let selected: Bool
    let action: () -> Void
    @State private var isHovering = false

    var body: some View {
        Button(action: action) {
            HStack(spacing: 9) {
                Image(systemName: section.symbol)
                    .foregroundStyle(selected ? CodexBridgePalette.accent : CodexBridgePalette.secondaryText)
                    .frame(width: 18)
                Text(section.title)
                    .font(.system(size: 12.5, weight: selected ? .semibold : .medium))
                Spacer()
            }
            .padding(.horizontal, 10)
            .frame(height: 34)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .background(
            selected ? CodexBridgePalette.surface : (isHovering ? CodexBridgePalette.subtleFill : .clear),
            in: RoundedRectangle(cornerRadius: CodexBridgeRadius.small, style: .continuous)
        )
        .overlay {
            RoundedRectangle(cornerRadius: CodexBridgeRadius.small, style: .continuous)
                .stroke(selected ? CodexBridgePalette.border : .clear, lineWidth: 1)
        }
        .onHover { isHovering = $0 }
    }
}

private struct GeneralSettingsPane: View {
    @Binding var appearance: String

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 24) {
                SettingsHeading(
                    title: "通用",
                    detail: "调整 Codex Bridge 的显示外观。"
                )

                VStack(alignment: .leading, spacing: 13) {
                    CodexBridgeSectionHeader(title: "外观", detail: "浅色与深色都使用高对比度语义色")
                    HStack(spacing: 11) {
                        AppearanceChoice(
                            title: "跟随系统",
                            symbol: "circle.lefthalf.filled",
                            selected: appearance == "system",
                            action: { appearance = "system" }
                        )
                        AppearanceChoice(
                            title: "浅色",
                            symbol: "sun.max.fill",
                            selected: appearance == "light",
                            action: { appearance = "light" }
                        )
                        AppearanceChoice(
                            title: "深色",
                            symbol: "moon.fill",
                            selected: appearance == "dark",
                            action: { appearance = "dark" }
                        )
                    }
                }

            }
            .padding(26)
            .frame(maxWidth: 680, alignment: .leading)
        }
    }
}

private struct AppearanceChoice: View {
    let title: String
    let symbol: String
    let selected: Bool
    let action: () -> Void
    @State private var isHovering = false

    var body: some View {
        Button(action: action) {
            VStack(spacing: 9) {
                Image(systemName: symbol)
                    .font(.system(size: 17, weight: .semibold))
                    .foregroundStyle(selected ? CodexBridgePalette.accent : CodexBridgePalette.secondaryText)
                Text(title).font(.subheadline.weight(.medium))
            }
            .frame(maxWidth: .infinity, minHeight: 74)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .background(
            selected ? CodexBridgePalette.accent.opacity(0.08) : (isHovering ? CodexBridgePalette.subtleFill : CodexBridgePalette.surface),
            in: RoundedRectangle(cornerRadius: CodexBridgeRadius.medium, style: .continuous)
        )
        .overlay {
            RoundedRectangle(cornerRadius: CodexBridgeRadius.medium)
                .stroke(selected ? CodexBridgePalette.accent.opacity(0.5) : CodexBridgePalette.border, lineWidth: selected ? 1.4 : 1)
        }
        .onHover { isHovering = $0 }
        .accessibilityValue(selected ? "已选择" : "")
    }
}

struct SourcesView: View {
    @Bindable var model: AppModel
    @Bindable var accessibility: AccessibilityPermissionController
    var embedded = false
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        VStack(spacing: 0) {
            if !embedded {
                HStack {
                    SettingsHeading(
                        title: "来源与权限",
                        detail: "Codex Bridge 只读取你明确连接或导入的内容。"
                    )
                    Spacer()
                    Button {
                        dismiss()
                    } label: {
                        Image(systemName: "xmark")
                    }
                    .buttonStyle(CodexBridgeIconButtonStyle())
                    .keyboardShortcut(.cancelAction)
                    .help("关闭")
                }
                .padding(.horizontal, 24)
                .frame(height: 82)
                .background(CodexBridgePalette.surface)
                Divider().overlay(CodexBridgePalette.border)
            }

            ScrollView {
                VStack(alignment: .leading, spacing: 20) {
                    if embedded {
                        SettingsHeading(
                            title: "来源与权限",
                            detail: "管理 ChatGPT 与 Codex 的连接和权限。"
                        )
                    }

                    VStack(alignment: .leading, spacing: 11) {
                        CodexBridgeSectionHeader(title: "对话来源", detail: "保存的 ChatGPT 对话会出现在左侧 Chat 列表")
                        SourceConnectionCard(
                            icon: "terminal",
                            tint: CodexBridgePalette.codex,
                            title: "Codex",
                            state: model.codexState,
                            detail: "连接本机 Codex，查看任务内容，并创建或继续任务。",
                            statusDetail: publicStatusText(for: model.codexState),
                            actionTitle: "重新检查",
                            action: { Task { await model.refreshCodexConnection() } }
                        )
                        SourceConnectionCard(
                            icon: "globe",
                            tint: CodexBridgePalette.chatGPT,
                            title: "ChatGPT 网页版",
                            state: model.chatGPTWebState,
                            detail: "在 ChatGPT 网页点击 Codex Bridge 扩展，选择需要的对话内容并保存到本机。",
                            statusDetail: model.browserExtensionReloadRequired
                                ? "新版扩展文件已经准备好，请在浏览器扩展管理页点击“重新加载”。"
                                : model.isBrowserExtensionEnabled
                                ? "扩展已就绪。保存后，对话会出现在左侧 Chat 列表。"
                                : "完成一次设置后，即可从网页保存对话到 Codex Bridge。",
                            actionTitle: model.browserExtensionReloadRequired
                                ? "完成更新…"
                                : (model.isBrowserExtensionEnabled ? "查看连接" : "设置…"),
                            action: { model.prepareBrowserConnection() }
                        )
                        ChatGPTAppSourceCard(model: model, accessibility: accessibility)
                    }
                }
                .padding(embedded ? 26 : 24)
                .frame(maxWidth: 700, alignment: .leading)
                .frame(maxWidth: .infinity)
            }
        }
        .background(CodexBridgePalette.canvas)
        .overlay {
            if let error = model.errorMessage {
                ZStack {
                    Color.black.opacity(0.22).ignoresSafeArea()
                    CodexBridgeErrorDialog(message: error, dismiss: model.dismissError)
                }
            }
        }
        .onAppear {
            accessibility.refresh()
            model.refreshBrowserConnectionState()
        }
        .onReceive(NotificationCenter.default.publisher(for: NSApplication.didBecomeActiveNotification)) { _ in
            accessibility.refresh()
            model.refreshBrowserConnectionState()
        }
        .sheet(isPresented: $model.isBrowserSetupPresented) {
            BrowserConnectionSetupView(model: model)
                .frame(width: 620, height: 610)
        }
        .sheet(isPresented: $model.isManualCapturePresented) {
            ManualChatGPTCaptureView(model: model)
                .frame(width: 640, height: 520)
        }
    }

    private func publicStatusText(for state: SourceConnectionState) -> String {
        switch state {
        case .checking:
            "正在检查连接。"
        case .ready:
            "等待完成连接设置。"
        case .connected:
            "连接正常。"
        case .experimental:
            "当前连接可用。"
        case .unavailable:
            "暂时无法连接，请重新检查。"
        }
    }
}

private struct ChatGPTAppSourceCard: View {
    @Bindable var model: AppModel
    @Bindable var accessibility: AccessibilityPermissionController
    @State private var showsPermissionRecovery = false

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            HStack(alignment: .top, spacing: 13) {
                Image(systemName: "bubble.left.and.bubble.right")
                    .font(.system(size: 16, weight: .semibold))
                    .foregroundStyle(CodexBridgePalette.chatGPT)
                    .frame(width: 41, height: 41)
                    .background(
                        CodexBridgePalette.chatGPT.opacity(0.1),
                        in: RoundedRectangle(cornerRadius: CodexBridgeRadius.medium)
                    )

                VStack(alignment: .leading, spacing: 6) {
                    HStack(spacing: 8) {
                        Text("ChatGPT App")
                            .font(.headline)
                        CodexBridgeStatusBadge(
                            text: accessibility.isTrusted ? "权限已开启" : "需要授权",
                            color: accessibility.isTrusted ? CodexBridgePalette.success : CodexBridgePalette.warning,
                            symbol: accessibility.isTrusted ? "checkmark.shield.fill" : "lock.shield"
                        )
                    }

                    Text(permissionDetail)
                        .font(.caption)
                        .foregroundStyle(CodexBridgePalette.secondaryText)
                        .fixedSize(horizontal: false, vertical: true)

                    Label("仅在你主动保存时读取已打开的对话", systemImage: "hand.raised")
                        .font(.caption2)
                        .foregroundStyle(CodexBridgePalette.tertiaryText)
                }

                Spacer(minLength: 12)

                HStack(spacing: 7) {
                    if accessibility.isTrusted {
                        Button(model.isBusy ? "正在保存…" : "保存已打开的对话") {
                            Task { await model.captureCurrentChatGPTAppWindow() }
                        }
                        .buttonStyle(CodexBridgePrimaryButtonStyle(compact: true))
                        .disabled(model.isBusy)
                    } else {
                        Button("打开系统设置…") {
                            showsPermissionRecovery = false
                            accessibility.requestAccess()
                        }
                        .buttonStyle(CodexBridgePrimaryButtonStyle(compact: true))
                    }

                    Menu {
                        Button("检查权限") {
                            accessibility.refresh()
                            showsPermissionRecovery = !accessibility.isTrusted
                        }
                        Button("手动粘贴导入…") {
                            model.isManualCapturePresented = true
                        }
                        if !accessibility.isTrusted {
                            Divider()
                            Button("在访达中显示 Codex Bridge") {
                                NSWorkspace.shared.activateFileViewerSelecting([Bundle.main.bundleURL])
                            }
                        }
                    } label: {
                        Image(systemName: "ellipsis")
                            .accessibilityLabel("更多 ChatGPT App 操作")
                    }
                    .menuStyle(.button)
                    .buttonStyle(CodexBridgeIconButtonStyle())
                    .help("更多")
                }
            }

            if showsPermissionRecovery && !accessibility.isTrusted {
                Divider()
                    .overlay(CodexBridgePalette.border)
                    .padding(.vertical, 13)

                HStack(alignment: .top, spacing: 9) {
                    Image(systemName: "info.circle")
                        .foregroundStyle(CodexBridgePalette.warning)
                        .padding(.top, 1)
                    Text("仍未检测到权限。若系统设置中已经开启 Codex Bridge，请移除旧条目，重新添加此 App 后再启动 Codex Bridge。")
                        .font(.caption)
                        .foregroundStyle(CodexBridgePalette.secondaryText)
                        .fixedSize(horizontal: false, vertical: true)
                }
                .accessibilityElement(children: .combine)
            }
        }
        .padding(15)
        .codexBridgeSurface()
        .onChange(of: accessibility.isTrusted) { _, isTrusted in
            if isTrusted {
                showsPermissionRecovery = false
            }
        }
    }

    private var permissionDetail: String {
        if accessibility.isTrusted {
            "可以保存已打开的对话，并在交接前预览内容。"
        } else {
            "开启辅助功能权限后，可以保存 ChatGPT App 中已打开的对话。"
        }
    }
}

private struct BrowserConnectionSetupView: View {
    @Bindable var model: AppModel
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        VStack(spacing: 0) {
            HStack(spacing: 14) {
                Image(systemName: "puzzlepiece.extension.fill")
                    .font(.system(size: 20, weight: .semibold))
                    .foregroundStyle(CodexBridgePalette.chatGPT)
                    .frame(width: 46, height: 46)
                    .background(CodexBridgePalette.chatGPT.opacity(0.12), in: RoundedRectangle(cornerRadius: CodexBridgeRadius.medium))
                VStack(alignment: .leading, spacing: 3) {
                    Text("连接 ChatGPT 网页版")
                        .font(.title2.bold())
                    Text("在 Chrome 或 Microsoft Edge 中加载一次扩展，之后即可选择需要的对话内容并保存到 Codex Bridge。")
                        .font(.caption)
                        .foregroundStyle(CodexBridgePalette.secondaryText)
                }
                Spacer()
                Button {
                    dismiss()
                } label: {
                    Image(systemName: "xmark")
                }
                .buttonStyle(CodexBridgeIconButtonStyle())
                .keyboardShortcut(.cancelAction)
            }
            .padding(22)
            .background(CodexBridgePalette.surface)

            Divider().overlay(CodexBridgePalette.border)

            ScrollView {
                VStack(alignment: .leading, spacing: 14) {
                    connectionStatus

                    browserManagementStep

                    if model.browserExtensionReloadRequired {
                        setupStep(
                            number: 2,
                            title: "重新加载 Codex Bridge 扩展",
                            detail: "扩展文件已自动更新。在扩展管理页找到 Codex Bridge，点击卡片上的“重新加载”按钮。",
                            actionTitle: "我已重新加载",
                            action: model.markBrowserExtensionReloaded
                        )
                    } else {
                        setupStep(
                            number: 2,
                            title: "加载 Codex Bridge 扩展",
                            detail: "开启“开发者模式”，点击“加载已解压的扩展程序”。在文件选择窗口按 ⇧⌘G，再粘贴下面的完整路径。",
                            actionTitle: "在访达中显示",
                            action: model.revealPreparedBrowserExtension
                        )
                    }

                    if let url = model.preparedBrowserExtensionURL {
                        VStack(alignment: .leading, spacing: 11) {
                            HStack(spacing: 10) {
                                Image(systemName: "folder.fill")
                                    .foregroundStyle(CodexBridgePalette.chatGPT)
                                Text(url.path)
                                    .font(.system(.caption, design: .monospaced))
                                    .lineLimit(2)
                                    .textSelection(.enabled)
                                Spacer(minLength: 8)
                                Button("复制路径", action: model.copyPreparedBrowserExtensionPath)
                                    .buttonStyle(CodexBridgeSecondaryButtonStyle(compact: true))
                            }

                            Divider().overlay(CodexBridgePalette.border)

                            Label {
                                Text("资源库（Library）文件夹默认隐藏。也可以在访达中按住 Option 打开“前往”菜单，再选择“资源库”。")
                                    .fixedSize(horizontal: false, vertical: true)
                            } icon: {
                                Image(systemName: "info.circle")
                            }
                            .font(.caption)
                            .foregroundStyle(CodexBridgePalette.secondaryText)
                        }
                        .padding(13)
                        .background(CodexBridgePalette.surface, in: RoundedRectangle(cornerRadius: CodexBridgeRadius.medium))
                        .overlay {
                            RoundedRectangle(cornerRadius: CodexBridgeRadius.medium)
                                .stroke(CodexBridgePalette.border, lineWidth: 1)
                        }
                    }

                    setupStep(
                        number: 3,
                        title: "保存 ChatGPT 对话",
                        detail: "使用系统默认浏览器打开 ChatGPT，点击浏览器工具栏中的 Codex Bridge，选择需要的内容并保存。内容会出现在 Codex Bridge 的 Chat 会话列表。",
                        actionTitle: "打开 ChatGPT",
                        action: { Task { await model.openChatGPTWeb() } }
                    )
                }
                .padding(22)
            }

            Divider().overlay(CodexBridgePalette.border)

            HStack {
                Text(footerStatus)
                    .font(.caption)
                    .foregroundStyle(CodexBridgePalette.secondaryText)
                Spacer()
                Button(model.browserExtensionReloadRequired ? "我已重新加载" : "重新检查") {
                    if model.browserExtensionReloadRequired {
                        model.markBrowserExtensionReloaded()
                    } else {
                        model.refreshBrowserConnectionState()
                    }
                }
                    .buttonStyle(CodexBridgeSecondaryButtonStyle(compact: true))
                Button(model.isBrowserExtensionEnabled ? "完成" : "稍后完成") { dismiss() }
                    .buttonStyle(CodexBridgePrimaryButtonStyle(compact: true))
            }
            .padding(18)
            .background(CodexBridgePalette.surface)
        }
        .background(CodexBridgePalette.canvas)
        .onAppear { model.refreshBrowserConnectionState() }
    }

    private var browserManagementStep: some View {
        HStack(alignment: .top, spacing: 13) {
            Text("1")
                .font(.caption.bold())
                .foregroundStyle(.white)
                .frame(width: 25, height: 25)
                .background(CodexBridgePalette.chatGPT, in: Circle())
            VStack(alignment: .leading, spacing: 5) {
                Text(model.browserExtensionReloadRequired ? "打开扩展管理页" : "打开浏览器扩展管理页")
                    .font(.headline)
                Text(model.browserExtensionReloadRequired
                    ? "选择安装了扩展的浏览器，然后在 Codex Bridge 扩展卡片中点击“重新加载”。"
                    : "选择你准备使用的浏览器，Codex Bridge 会打开对应的扩展管理页。")
                    .font(.caption)
                    .foregroundStyle(CodexBridgePalette.secondaryText)
                    .fixedSize(horizontal: false, vertical: true)
            }
            Spacer(minLength: 10)
            HStack(spacing: 8) {
                ForEach(SupportedExtensionBrowser.allCases) { browser in
                    Button(browser.shortName) {
                        Task { await model.openExtensionsPage(in: browser) }
                    }
                    .buttonStyle(CodexBridgeSecondaryButtonStyle(compact: true))
                    .disabled(!model.isBrowserInstalled(browser))
                    .help(
                        model.isBrowserInstalled(browser)
                            ? "在 \(browser.displayName) 中打开扩展管理页"
                            : "未安装 \(browser.displayName)"
                    )
                }
            }
        }
        .padding(15)
        .codexBridgeSurface()
    }

    @ViewBuilder
    private var connectionStatus: some View {
        HStack(spacing: 12) {
            Image(systemName: model.browserExtensionReloadRequired ? "arrow.clockwise.circle.fill" : (model.isBrowserExtensionEnabled ? "checkmark.circle.fill" : "clock.badge.checkmark"))
                .font(.system(size: 22, weight: .semibold))
                .foregroundStyle(model.browserExtensionReloadRequired ? CodexBridgePalette.warning : (model.isBrowserExtensionEnabled ? CodexBridgePalette.success : CodexBridgePalette.accent))
            VStack(alignment: .leading, spacing: 3) {
                Text(model.browserExtensionReloadRequired ? "扩展更新等待重新加载" : (model.isBrowserExtensionEnabled ? "浏览器扩展已加载" : "扩展文件已准备好"))
                    .font(.headline)
                Text(model.chatGPTWebState.label)
                    .font(.caption)
                    .foregroundStyle(CodexBridgePalette.secondaryText)
            }
            Spacer()
        }
        .padding(15)
        .background(
            (model.browserExtensionReloadRequired ? CodexBridgePalette.warning : (model.isBrowserExtensionEnabled ? CodexBridgePalette.success : CodexBridgePalette.accent)).opacity(0.09),
            in: RoundedRectangle(cornerRadius: CodexBridgeRadius.large)
        )
    }

    private var footerStatus: String {
        if model.browserExtensionReloadRequired {
            return "重新加载后，新版扩展会立即生效"
        }
        return model.isBrowserExtensionEnabled ? "扩展已经加载，可以开始使用" : "加载完成后，请重新检查连接"
    }

    private func setupStep(
        number: Int,
        title: String,
        detail: String,
        actionTitle: String,
        action: @escaping () -> Void
    ) -> some View {
        HStack(alignment: .top, spacing: 13) {
            Text("\(number)")
                .font(.caption.bold())
                .foregroundStyle(.white)
                .frame(width: 25, height: 25)
                .background(CodexBridgePalette.chatGPT, in: Circle())
            VStack(alignment: .leading, spacing: 5) {
                Text(title).font(.headline)
                Text(detail)
                    .font(.caption)
                    .foregroundStyle(CodexBridgePalette.secondaryText)
                    .fixedSize(horizontal: false, vertical: true)
            }
            Spacer(minLength: 10)
            Button(actionTitle, action: action)
                .buttonStyle(CodexBridgeSecondaryButtonStyle(compact: true))
        }
        .padding(15)
        .codexBridgeSurface()
    }
}

private struct SourceConnectionCard: View {
    let icon: String
    let tint: Color
    let title: String
    let state: SourceConnectionState
    let detail: String
    let statusDetail: String
    let actionTitle: String?
    let action: (() -> Void)?

    var body: some View {
        HStack(alignment: .top, spacing: 13) {
            Image(systemName: icon)
                .font(.system(size: 16, weight: .semibold))
                .foregroundStyle(tint)
                .frame(width: 41, height: 41)
                .background(tint.opacity(0.1), in: RoundedRectangle(cornerRadius: CodexBridgeRadius.medium))
            VStack(alignment: .leading, spacing: 6) {
                HStack(spacing: 8) {
                    Text(title).font(.headline)
                    ConnectionBadge(state: state)
                }
                Text(detail)
                    .font(.caption)
                    .foregroundStyle(CodexBridgePalette.secondaryText)
                    .fixedSize(horizontal: false, vertical: true)
                Text(statusDetail)
                    .font(.caption2)
                    .foregroundStyle(CodexBridgePalette.tertiaryText)
                    .lineLimit(2)
            }
            Spacer(minLength: 8)
            if let actionTitle, let action {
                Button(actionTitle, action: action)
                    .buttonStyle(CodexBridgeSecondaryButtonStyle(compact: true))
            }
        }
        .padding(15)
        .codexBridgeSurface()
    }
}

private struct ConnectionBadge: View {
    let state: SourceConnectionState

    var body: some View {
        CodexBridgeStatusBadge(text: label, color: color)
    }

    private var label: String {
        switch state {
        case .checking: "检查中"
        case .ready: "待完成"
        case .connected: "已连接"
        case .experimental: "可用"
        case .unavailable: "不可用"
        }
    }

    private var color: Color {
        switch state {
        case .checking: CodexBridgePalette.warning
        case .ready: CodexBridgePalette.accent
        case .connected: CodexBridgePalette.success
        case .experimental: CodexBridgePalette.accent
        case .unavailable: CodexBridgePalette.danger
        }
    }
}

private struct SettingsHeading: View {
    let title: String
    let detail: String

    var body: some View {
        VStack(alignment: .leading, spacing: 5) {
            Text(title)
                .font(.system(size: 24, weight: .bold))
                .tracking(-0.35)
            Text(detail)
                .font(.subheadline)
                .foregroundStyle(CodexBridgePalette.secondaryText)
        }
    }
}

private struct AboutSettingsPane: View {
    @Bindable var model: AppModel

    var body: some View {
        VStack(alignment: .leading, spacing: 24) {
            SettingsHeading(
                title: "关于 Codex Bridge",
                detail: "让 ChatGPT 的讨论和 Codex 的执行自然衔接。"
            )

            VStack(alignment: .leading, spacing: 18) {
                HStack(alignment: .center, spacing: 16) {
                    BrandMark()
                    Spacer(minLength: 16)
                    CodexBridgeStatusBadge(
                        text: CodexBridgeRelease.displayVersion,
                        color: CodexBridgePalette.accent
                    )
                }

                Divider()
                    .overlay(CodexBridgePalette.border)

                Text("Codex Bridge 帮助你在 ChatGPT 与 Codex 之间预览、确认并交接对话内容。")
                    .font(.system(size: 14, weight: .medium))
                    .foregroundStyle(CodexBridgePalette.primaryText)
                    .lineSpacing(3)
                    .frame(maxWidth: 520, alignment: .leading)

                CodexBridgeStatusBadge(text: "原生 macOS", color: CodexBridgePalette.accent, symbol: "apple.logo")

                Text("Beta 版本面向早期用户，连接能力可能随 ChatGPT 或 Codex 更新而调整。")
                    .font(.caption)
                    .foregroundStyle(CodexBridgePalette.secondaryText)
            }
            .padding(22)
            .frame(maxWidth: .infinity, alignment: .leading)
            .codexBridgeSurface(radius: CodexBridgeRadius.large, elevated: true)

            updateCard

            Label(
                "Codex Bridge 只处理你选择的内容；发送和权限操作始终由你确认。",
                systemImage: "lock.shield.fill"
            )
            .font(.caption)
            .foregroundStyle(CodexBridgePalette.secondaryText)

            Spacer()
        }
        .padding(28)
    }

    private var updateCard: some View {
        HStack(spacing: 13) {
            Image(systemName: updateSymbol)
                .font(.system(size: 16, weight: .semibold))
                .foregroundStyle(updateColor)
                .frame(width: 40, height: 40)
                .background(updateColor.opacity(0.1), in: RoundedRectangle(cornerRadius: CodexBridgeRadius.medium))

            VStack(alignment: .leading, spacing: 4) {
                Text(updateTitle)
                    .font(.headline)
                Text(updateDetail)
                    .font(.caption)
                    .foregroundStyle(CodexBridgePalette.secondaryText)
            }

            Spacer(minLength: 12)

            switch model.appUpdateState {
            case .checking:
                ProgressView().controlSize(.small)
            case let .available(release):
                if release.asset != nil {
                    Button("发布说明", action: model.openAvailableUpdate)
                        .buttonStyle(CodexBridgeSecondaryButtonStyle(compact: true))
                    Button("下载并安装") { Task { await model.installAvailableUpdate() } }
                        .buttonStyle(CodexBridgePrimaryButtonStyle(compact: true))
                } else {
                    Button("查看更新", action: model.openAvailableUpdate)
                        .buttonStyle(CodexBridgePrimaryButtonStyle(compact: true))
                }
            case .downloading, .installing:
                ProgressView().controlSize(.small)
            case let .failed(_, release?):
                Button("发布页面", action: model.openAvailableUpdate)
                    .buttonStyle(CodexBridgeSecondaryButtonStyle(compact: true))
                if release.asset != nil {
                    Button("重试安装") { Task { await model.installAvailableUpdate() } }
                        .buttonStyle(CodexBridgePrimaryButtonStyle(compact: true))
                }
            default:
                Button("检查更新") { Task { await model.checkForUpdates() } }
                    .buttonStyle(CodexBridgeSecondaryButtonStyle(compact: true))
            }
        }
        .padding(16)
        .codexBridgeSurface()
    }

    private var updateTitle: String {
        switch model.appUpdateState {
        case .idle: "自动检查更新"
        case .checking: "正在检查更新"
        case .upToDate: "已是最新版本"
        case let .available(release): "Codex Bridge \(release.version) 可用"
        case let .downloading(release): "正在下载 Codex Bridge \(release.version)"
        case let .installing(release): "正在安装 Codex Bridge \(release.version)"
        case .failed: "更新未完成"
        }
    }

    private var updateDetail: String {
        switch model.appUpdateState {
        case .idle: "每天自动检查一次 GitHub Releases，也可以立即检查。"
        case .checking: "正在连接 GitHub Releases。"
        case .upToDate: "当前版本是 \(CodexBridgeRelease.displayVersion)。"
        case let .available(release):
            release.asset == nil ? release.title : "\(release.title)；安装前会校验文件摘要、版本和代码签名。"
        case .downloading: "正在从 GitHub Releases 下载并校验更新文件。"
        case .installing: "验证完成，Codex Bridge 即将退出、完成替换并重新打开。"
        case let .failed(message, _): message
        }
    }

    private var updateSymbol: String {
        switch model.appUpdateState {
        case .available, .downloading, .installing: "arrow.down.circle.fill"
        case .upToDate: "checkmark.circle.fill"
        case .failed: "exclamationmark.circle.fill"
        default: "arrow.triangle.2.circlepath"
        }
    }

    private var updateColor: Color {
        switch model.appUpdateState {
        case .available, .downloading, .installing: CodexBridgePalette.accent
        case .upToDate: CodexBridgePalette.success
        case .failed: CodexBridgePalette.warning
        default: CodexBridgePalette.secondaryText
        }
    }
}

private struct LocalDataSettingsPane: View {
    @Bindable var model: AppModel
    @State private var confirmsClear = false

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 24) {
                SettingsHeading(
                    title: "本地数据",
                    detail: "查看 Codex Bridge 保存范围，并管理只属于 Codex Bridge 的数据。"
                )

                VStack(alignment: .leading, spacing: 0) {
                    dataRow("已保存的对话", value: "\(model.conversations.count)", symbol: "bubble.left.and.text.bubble.right")
                    Divider().padding(.leading, 46)
                    dataRow("Codex 任务记录", value: "\(model.operations.count)", symbol: "terminal")
                    Divider().padding(.leading, 46)
                    dataRow("交接记录", value: "\(model.links.count + model.chatGPTDrafts.count)", symbol: "link")
                }
                .codexBridgeSurface()

                VStack(alignment: .leading, spacing: 12) {
                    Text("数据与隐私").font(.headline)
                    Label("所有数据默认保存在这台 Mac 上。", systemImage: "internaldrive")
                    Label("只保存对话中可见的内容，不保存模型的内部思考过程。", systemImage: "lock.shield")
                    Label("清除本地数据不会删除 ChatGPT、Codex 中的会话或项目文件。", systemImage: "checkmark.shield")
                }
                .font(.subheadline)
                .foregroundStyle(CodexBridgePalette.secondaryText)
                .padding(18)
                .frame(maxWidth: .infinity, alignment: .leading)
                .codexBridgeSurface()

                HStack {
                    VStack(alignment: .leading, spacing: 4) {
                        Text("诊断信息").font(.headline)
                        Text("只导出数量与连接状态，不包含对话内容、文件路径、网页地址或任务标识。")
                            .font(.caption)
                            .foregroundStyle(CodexBridgePalette.secondaryText)
                    }
                    Spacer()
                    Button("导出…") { Task { await model.exportDiagnostics() } }
                        .buttonStyle(CodexBridgeSecondaryButtonStyle())
                }
                .padding(18)
                .codexBridgeSurface()

                HStack {
                    VStack(alignment: .leading, spacing: 4) {
                        Text("清除 Codex Bridge 本地数据").font(.headline)
                        Text("移除已保存的对话、草稿与交接记录；不会修改任何原始会话或项目。")
                            .font(.caption)
                            .foregroundStyle(CodexBridgePalette.secondaryText)
                    }
                    Spacer()
                    Button("清除…", role: .destructive) { confirmsClear = true }
                        .buttonStyle(CodexBridgeSecondaryButtonStyle())
                }
                .padding(18)
                .background(CodexBridgePalette.danger.opacity(0.055))
                .overlay { RoundedRectangle(cornerRadius: CodexBridgeRadius.medium).stroke(CodexBridgePalette.danger.opacity(0.24)) }
            }
            .padding(26)
            .frame(maxWidth: 680, alignment: .leading)
        }
        .alert("清除 Codex Bridge 本地数据？", isPresented: $confirmsClear) {
            Button("取消", role: .cancel) {}
            Button("清除", role: .destructive) { Task { await model.clearLocalData() } }
        } message: {
            Text("此操作不会删除 ChatGPT 会话、Codex 任务或工作目录中的文件。")
        }
    }

    private func dataRow(_ title: String, value: String, symbol: String) -> some View {
        HStack(spacing: 12) {
            Image(systemName: symbol)
                .foregroundStyle(CodexBridgePalette.accent)
                .frame(width: 30)
            Text(title).font(.subheadline.weight(.medium))
            Spacer()
            Text(value).font(.system(.body, design: .monospaced).weight(.semibold))
        }
        .padding(.horizontal, 16)
        .frame(height: 48)
    }
}
