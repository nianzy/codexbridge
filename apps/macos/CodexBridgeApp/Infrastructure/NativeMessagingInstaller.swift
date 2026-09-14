import AppKit
import ApplicationServices
import CryptoKit
import Foundation

enum NativeMessagingInstallError: LocalizedError {
    case missingHelper
    case missingExtension
    case browserUnavailable(String)
    case defaultBrowserUnavailable

    var errorDescription: String? {
        switch self {
        case .missingHelper: "Codex Bridge 安装包中缺少浏览器连接组件，请重新安装 App。"
        case .missingExtension: "Codex Bridge 安装包中缺少浏览器扩展文件。"
        case let .browserUnavailable(name): "没有找到\(name)，请先安装后再继续。"
        case .defaultBrowserUnavailable: "无法使用系统默认浏览器打开 ChatGPT。"
        }
    }
}

enum SupportedExtensionBrowser: String, CaseIterable, Identifiable, Sendable {
    case chrome
    case edge

    var id: String { rawValue }

    var displayName: String {
        switch self {
        case .chrome: "Google Chrome"
        case .edge: "Microsoft Edge"
        }
    }

    var shortName: String {
        switch self {
        case .chrome: "Chrome"
        case .edge: "Edge"
        }
    }

    var bundleIdentifier: String {
        switch self {
        case .chrome: "com.google.Chrome"
        case .edge: "com.microsoft.edgemac"
        }
    }

    var extensionsPage: String {
        switch self {
        case .chrome: "chrome://extensions/"
        case .edge: "edge://extensions/"
        }
    }

    var applicationSupportComponents: [String] {
        switch self {
        case .chrome: ["Google", "Chrome"]
        case .edge: ["Microsoft Edge"]
        }
    }
}

enum BrowserExtensionInstallation: Equatable, Sendable {
    case missing
    case disabled(browser: SupportedExtensionBrowser, profile: String)
    case enabled(browser: SupportedExtensionBrowser, profile: String)
}

struct NativeMessagingInstaller: Sendable {
    static let extensionID = "pnpgopcjhgfmnkefmdnoebmcbnnhnhee"

    func install() throws -> [URL] {
        let helperURL = Bundle.main.bundleURL
            .appendingPathComponent("Contents", isDirectory: true)
            .appendingPathComponent("Helpers", isDirectory: true)
            .appendingPathComponent("CodexBridgeNativeHost")
        guard FileManager.default.isExecutableFile(atPath: helperURL.path) else {
            throw NativeMessagingInstallError.missingHelper
        }

        let applicationSupport = try FileManager.default.url(
            for: .applicationSupportDirectory,
            in: .userDomainMask,
            appropriateFor: nil,
            create: true
        )
        let manifest: [String: Any] = [
            "name": "app.codexbridge.nativehost",
            "description": "Codex Bridge Native Messaging Host",
            "path": helperURL.path,
            "type": "stdio",
            "allowed_origins": ["chrome-extension://\(Self.extensionID)/"],
        ]
        let legacyManifest: [String: Any] = [
            "name": "app.sidely.nativehost",
            "description": "Codex Bridge Native Messaging Host compatibility entry",
            "path": helperURL.path,
            "type": "stdio",
            "allowed_origins": ["chrome-extension://\(Self.extensionID)/"],
        ]
        let data = try JSONSerialization.data(withJSONObject: manifest, options: [.prettyPrinted, .sortedKeys])
        let legacyData = try JSONSerialization.data(withJSONObject: legacyManifest, options: [.prettyPrinted, .sortedKeys])
        return try SupportedExtensionBrowser.allCases.map { browser in
            let directory = browser.applicationSupportComponents.reduce(applicationSupport) { partial, component in
                partial.appendingPathComponent(component, isDirectory: true)
            }
            .appendingPathComponent("NativeMessagingHosts", isDirectory: true)
            try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
            let manifestURL = directory.appendingPathComponent("app.codexbridge.nativehost.json")
            try data.write(to: manifestURL, options: .atomic)
            let legacyManifestURL = directory.appendingPathComponent("app.sidely.nativehost.json")
            try legacyData.write(to: legacyManifestURL, options: .atomic)
            return manifestURL
        }
    }

    func extensionDirectory() throws -> URL {
        guard let directory = Bundle.main.resourceURL?.appendingPathComponent("extension", isDirectory: true),
              FileManager.default.fileExists(atPath: directory.appendingPathComponent("manifest.json").path) else {
            throw NativeMessagingInstallError.missingExtension
        }
        return directory
    }

    /// Chromium 浏览器的“加载已解压的扩展程序”需要普通文件夹，不能直接选择 App 包内资源。
    func prepareExtensionDirectory() throws -> URL {
        let source = try extensionDirectory()
        let applicationSupport = try FileManager.default.url(
            for: .applicationSupportDirectory,
            in: .userDomainMask,
            appropriateFor: nil,
            create: true
        )
        let legacyDestination = applicationSupport
            .appendingPathComponent("Sidely", isDirectory: true)
            .appendingPathComponent("ChromeExtension", isDirectory: true)
        let support = applicationSupport.appendingPathComponent("Codex Bridge", isDirectory: true)
        try FileManager.default.createDirectory(at: support, withIntermediateDirectories: true)
        // 已加载的 unpacked extension 绑定原路径；首次改名时原地更新，避免浏览器失去扩展。
        let destination = FileManager.default.fileExists(atPath: legacyDestination.path)
            ? legacyDestination
            : support.appendingPathComponent("ChromeExtension", isDirectory: true)
        if FileManager.default.fileExists(atPath: destination.path) {
            try FileManager.default.removeItem(at: destination)
        }
        try FileManager.default.copyItem(at: source, to: destination)
        return destination
    }

    func preparedExtensionDirectory() -> URL? {
        guard let support = try? FileManager.default.url(
            for: .applicationSupportDirectory,
            in: .userDomainMask,
            appropriateFor: nil,
            create: false
        ) else { return nil }
        let directory = support
            .appendingPathComponent("Codex Bridge", isDirectory: true)
            .appendingPathComponent("ChromeExtension", isDirectory: true)
        if FileManager.default.fileExists(atPath: directory.appendingPathComponent("manifest.json").path) {
            return directory
        }
        let legacyDirectory = support
            .appendingPathComponent("Sidely", isDirectory: true)
            .appendingPathComponent("ChromeExtension", isDirectory: true)
        return FileManager.default.fileExists(atPath: legacyDirectory.appendingPathComponent("manifest.json").path)
            ? legacyDirectory
            : nil
    }

    func hasCurrentNativeHostManifest() -> Bool {
        let helperURL = Bundle.main.bundleURL
            .appendingPathComponent("Contents", isDirectory: true)
            .appendingPathComponent("Helpers", isDirectory: true)
            .appendingPathComponent("CodexBridgeNativeHost")
        guard let support = try? FileManager.default.url(
                for: .applicationSupportDirectory,
                in: .userDomainMask,
                appropriateFor: nil,
                create: false
              ) else { return false }
        guard FileManager.default.isExecutableFile(atPath: helperURL.path) else { return false }
        return SupportedExtensionBrowser.allCases.contains { browser in
            let root = browser.applicationSupportComponents.reduce(support) { partial, component in
                partial.appendingPathComponent(component, isDirectory: true)
            }
            let manifestURL = root
                .appendingPathComponent("NativeMessagingHosts", isDirectory: true)
                .appendingPathComponent("app.codexbridge.nativehost.json")
            guard let data = try? Data(contentsOf: manifestURL),
                  let object = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else { return false }
            return object["path"] as? String == helperURL.path
        }
    }

    func browserExtensionInstallation() -> BrowserExtensionInstallation {
        guard let support = try? FileManager.default.url(
            for: .applicationSupportDirectory,
            in: .userDomainMask,
            appropriateFor: nil,
            create: false
        ) else { return .missing }
        let roots = SupportedExtensionBrowser.allCases.map { browser in
            let root = browser.applicationSupportComponents.reduce(support) { partial, component in
                partial.appendingPathComponent(component, isDirectory: true)
            }
            return (browser, root)
        }
        return Self.browserExtensionInstallation(browserRoots: roots)
    }

    static func browserExtensionInstallation(
        browserRoots: [(SupportedExtensionBrowser, URL)]
    ) -> BrowserExtensionInstallation {
        var disabled: BrowserExtensionInstallation?
        for (browser, browserRoot) in browserRoots {
            guard let profiles = try? FileManager.default.contentsOfDirectory(
                at: browserRoot,
                includingPropertiesForKeys: [.isDirectoryKey],
                options: [.skipsHiddenFiles]
            ) else { continue }
            for profile in profiles.sorted(by: { $0.lastPathComponent < $1.lastPathComponent }) {
                for fileName in ["Preferences", "Secure Preferences"] {
                    let preferencesURL = profile.appendingPathComponent(fileName)
                    guard let data = try? Data(contentsOf: preferencesURL, options: [.mappedIfSafe]),
                          let root = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
                          let extensions = root["extensions"] as? [String: Any],
                          let settings = extensions["settings"] as? [String: Any],
                          let entry = settings[extensionID] as? [String: Any] else { continue }
                    let state = (entry["state"] as? NSNumber)?.intValue
                    let disableReasons = entry["disable_reasons"] as? [Any] ?? []
                    if state != 0, disableReasons.isEmpty {
                        return .enabled(browser: browser, profile: profile.lastPathComponent)
                    }
                    disabled = .disabled(browser: browser, profile: profile.lastPathComponent)
                }
            }
        }
        return disabled ?? .missing
    }

    @MainActor
    func isBrowserInstalled(_ browser: SupportedExtensionBrowser) -> Bool {
        NSWorkspace.shared.urlForApplication(withBundleIdentifier: browser.bundleIdentifier) != nil
    }

    @MainActor
    func openExtensionsPage(in browser: SupportedExtensionBrowser) async throws {
        guard let browserURL = NSWorkspace.shared.urlForApplication(withBundleIdentifier: browser.bundleIdentifier) else {
            throw NativeMessagingInstallError.browserUnavailable(browser.displayName)
        }
        let configuration = NSWorkspace.OpenConfiguration()
        configuration.arguments = [browser.extensionsPage]
        _ = try await NSWorkspace.shared.openApplication(at: browserURL, configuration: configuration)
    }

    @MainActor
    func openChatGPTWeb() async throws {
        guard let url = URL(string: "https://chatgpt.com/"), NSWorkspace.shared.open(url) else {
            throw NativeMessagingInstallError.defaultBrowserUnavailable
        }
    }
}

enum NativeCaptureInbox {
    static func pendingCaptureURLs() throws -> [URL] {
        var directories = [try inboxDirectory()]
        if let legacyDirectory = try legacyInboxDirectory() {
            directories.append(legacyDirectory)
        }
        return directories.flatMap { directory in
            (try? FileManager.default.contentsOfDirectory(
                at: directory,
                includingPropertiesForKeys: [.creationDateKey],
                options: [.skipsHiddenFiles]
            )) ?? []
        }
        .filter { $0.pathExtension == "json" }
        .sorted { $0.lastPathComponent < $1.lastPathComponent }
    }

    static func quarantine(_ url: URL) throws {
        let directory = try supportDirectory().appendingPathComponent("InvalidCaptures", isDirectory: true).ensuringDirectory()
        let target = directory.appendingPathComponent(url.lastPathComponent)
        if FileManager.default.fileExists(atPath: target.path) {
            try FileManager.default.removeItem(at: target)
        }
        try FileManager.default.moveItem(at: url, to: target)
    }

    private static func inboxDirectory() throws -> URL {
        try supportDirectory().appendingPathComponent("CaptureInbox", isDirectory: true).ensuringDirectory()
    }

    private static func legacyInboxDirectory() throws -> URL? {
        let base = try FileManager.default.url(
            for: .applicationSupportDirectory,
            in: .userDomainMask,
            appropriateFor: nil,
            create: true
        )
        let directory = base
            .appendingPathComponent("Sidely", isDirectory: true)
            .appendingPathComponent("CaptureInbox", isDirectory: true)
        return FileManager.default.fileExists(atPath: directory.path) ? directory : nil
    }

    private static func supportDirectory() throws -> URL {
        let base = try FileManager.default.url(
            for: .applicationSupportDirectory,
            in: .userDomainMask,
            appropriateFor: nil,
            create: true
        )
        return try base.appendingPathComponent("Codex Bridge", isDirectory: true).ensuringDirectory()
    }
}

private extension URL {
    func ensuringDirectory() throws -> URL {
        try FileManager.default.createDirectory(at: self, withIntermediateDirectories: true)
        return self
    }
}

enum ChatGPTAppCaptureError: LocalizedError {
    case permissionRequired
    case appNotRunning
    case windowUnavailable
    case noVisibleTurns

    var errorDescription: String? {
        switch self {
        case .permissionRequired: "需要开启辅助功能权限才能保存 ChatGPT 中已打开的对话。"
        case .appNotRunning: "未找到正在运行的 ChatGPT App。"
        case .windowUnavailable: "无法读取 ChatGPT 窗口。请先打开需要保存的对话。"
        case .noVisibleTurns: "没有识别到完整的对话内容。请改用手动粘贴导入。"
        }
    }
}

struct ChatGPTAppCaptureService: Sendable {
    func captureVisibleConversation(projectName: String?) async throws -> CapturedConversation {
        try await Task.detached(priority: .userInitiated) {
            try capture(projectName: projectName)
        }.value
    }

    private func capture(projectName: String?) throws -> CapturedConversation {
        guard AXIsProcessTrusted() else { throw ChatGPTAppCaptureError.permissionRequired }
        let identifiers = Set(["com.openai.chat", "com.openai.chatgpt", "com.openai.codex"])
        guard let app = NSWorkspace.shared.runningApplications.first(where: {
            $0.bundleIdentifier.map(identifiers.contains) == true &&
            ($0.localizedName?.localizedCaseInsensitiveContains("ChatGPT") == true || $0.bundleURL?.lastPathComponent == "ChatGPT.app")
        }) else { throw ChatGPTAppCaptureError.appNotRunning }

        let application = AXUIElementCreateApplication(app.processIdentifier)
        AXUIElementSetMessagingTimeout(application, 2)
        guard let window = copyElementAttribute(application, kAXFocusedWindowAttribute as String)
            ?? copyElementsAttribute(application, kAXWindowsAttribute as String).first else {
            throw ChatGPTAppCaptureError.windowUnavailable
        }
        let title = copyStringAttribute(window, kAXTitleAttribute as String) ?? "ChatGPT App 对话"
        var visited = 0
        var textBytes = 0
        var fragments: [String] = []
        collectText(
            from: window,
            visited: &visited,
            textBytes: &textBytes,
            fragments: &fragments
        )
        let turns = ChatGPTTranscriptParser().parse(fragments)
        guard !turns.isEmpty else { throw ChatGPTAppCaptureError.noVisibleTurns }

        let canonical = turns.flatMap { [$0.user.text, $0.assistant?.text ?? ""] }.joined(separator: "\u{1E}")
        let hash = SHA256.hash(data: Data(canonical.utf8)).map { String(format: "%02x", $0) }.joined()
        return CapturedConversation(
            id: CapturedConversation.stableID(for: "chatgpt-app-\(hash)"),
            sourceKind: .chatGPTApp,
            sourceURL: nil,
            sourceConversationID: nil,
            title: title,
            projectName: projectName?.trimmingCharacters(in: .whitespacesAndNewlines).nilIfEmpty,
            projectPath: nil,
            capturedAt: .now,
            captureScope: "accessibility-visible-window",
            freshness: .captured,
            contentHash: "chatgpt-app-\(hash)",
            turns: turns,
            warnings: ["仅包含 ChatGPT 当前窗口中可见的文字。"],
            codexThreadID: nil
        )
    }

    private func collectText(
        from element: AXUIElement,
        visited: inout Int,
        textBytes: inout Int,
        fragments: inout [String]
    ) {
        guard visited < 6_000, textBytes < CaptureValidator.maximumPayloadBytes else { return }
        visited += 1

        let role = copyStringAttribute(element, kAXRoleAttribute as String) ?? ""
        if [kAXButtonRole as String, kAXTextAreaRole as String, kAXTextFieldRole as String,
            kAXToolbarRole as String, kAXMenuRole as String, kAXOutlineRole as String].contains(role) { return }
        let children = copyElementsAttribute(element, kAXChildrenAttribute as String)
        let description = copyStringAttribute(element, kAXDescriptionAttribute as String) ?? ""
        let roleLabels = ["you said", "你说", "用户说", "chatgpt said", "chatgpt 说", "assistant said"]
        let normalized = description.lowercased().trimmingCharacters(in: CharacterSet(charactersIn: ":： "))
        if roleLabels.contains(normalized) { fragments.append(description) }
        if role == kAXStaticTextRole as String || children.isEmpty {
            if let text = copyStringAttribute(element, kAXValueAttribute as String)
                ?? copyStringAttribute(element, kAXTitleAttribute as String), !text.isEmpty,
                fragments.last != text {
                fragments.append(text)
                textBytes += text.utf8.count
            }
        }
        for child in children {
            collectText(from: child, visited: &visited, textBytes: &textBytes, fragments: &fragments)
        }
    }

    /// 通过桌面端内置的新会话链接打开 ChatGPT 并预填草稿；不触发发送。
    func fillNewConversation(with text: String) async throws -> ChatGPTFillResult {
        guard !text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
            throw ChatGPTComposerError.emptyDraft
        }
        guard let url = ChatGPTDraftDeepLink.make(prompt: text) else {
            throw ChatGPTComposerError.newConversationUnavailable
        }
        let opened = await MainActor.run { NSWorkspace.shared.open(url) }
        guard opened else { throw ChatGPTComposerError.newConversationUnavailable }
        // 桌面端会把 deep link 放入启动队列，重启后也会在窗口就绪时新建 ChatGPT 会话并预填。
        return .inserted
    }

    /// 通过 Codex 官方新任务链接打开工作目录并预填草稿；不触发发送或执行。
    func fillCodexDraft(workspacePath: String, text: String) async throws -> ChatGPTFillResult {
        guard !text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
            throw ChatGPTComposerError.emptyDraft
        }
        guard let url = CodexDraftDeepLink.make(workspacePath: workspacePath, prompt: text) else {
            throw ChatGPTComposerError.newConversationUnavailable
        }
        let opened = await MainActor.run { NSWorkspace.shared.open(url) }
        guard opened else { throw ChatGPTComposerError.newConversationUnavailable }
        // Codex 会把 deep link 放入自身启动队列；冷启动时也会在窗口就绪后创建新任务并预填。
        return .inserted
    }

    private func fillComposer(
        _ text: String,
        app: NSRunningApplication,
        application: AXUIElement,
        window: AXUIElement,
        preferredComposer: AXUIElement? = nil,
        requiresEmptyComposer: Bool = false
    ) async throws -> ChatGPTFillResult {
            var composer: AXUIElement? = preferredComposer
            for _ in 0..<120 {
                if composer != nil { break }
                guard let currentWindow = copyElementAttribute(application, kAXFocusedWindowAttribute as String) else {
                    try await Task.sleep(for: .milliseconds(100))
                    continue
                }
                let candidates = composerCandidates(in: currentWindow)
                let focused = copyElementAttribute(application, kAXFocusedUIElementAttribute as String)
                composer = candidates.first(where: { candidate in
                    focused.map { sameElementOrDescendant($0, of: candidate) } == true
                }) ?? candidates.max(by: { composerScore($0) < composerScore($1) })
                if composer != nil { break }
                try await Task.sleep(for: .milliseconds(100))
            }
            guard let composer else { throw ChatGPTComposerError.missing }
            guard let current = copyElementAttribute(application, kAXFocusedWindowAttribute as String), CFEqual(current, window) else { throw ChatGPTComposerError.missing }
            if requiresEmptyComposer {
                let value = copyStringAttribute(composer, kAXValueAttribute as String)
                let placeholder = copyStringAttribute(composer, kAXPlaceholderValueAttribute as String)
                guard ChatGPTComposerValue.hasNoDraft(value: value, placeholder: placeholder) else {
                    throw ChatGPTComposerError.occupied
                }
            }
            // 目标页面已经由 CodexBridge 明确核对。全选再粘贴可兼容把占位内容暴露为 AXValue 的编辑器。
            app.activate()
            for _ in 0..<20 {
                if NSWorkspace.shared.frontmostApplication?.processIdentifier == app.processIdentifier { break }
                try await Task.sleep(for: .milliseconds(100))
            }
            AXUIElementPerformAction(window, kAXRaiseAction as CFString)
            let focusResult = AXUIElementSetAttributeValue(composer, kAXFocusedAttribute as CFString, kCFBooleanTrue)
            if focusResult != .success {
                _ = AXUIElementPerformAction(composer, kAXPressAction as CFString)
            }
            try await Task.sleep(for: .milliseconds(200))
            guard let focused = copyElementAttribute(application, kAXFocusedUIElementAttribute as String),
                  sameElementOrDescendant(focused, of: composer) else {
                throw ChatGPTComposerError.missing
            }
            guard NSWorkspace.shared.frontmostApplication?.processIdentifier == app.processIdentifier,
                  let currentWindow = copyElementAttribute(application, kAXFocusedWindowAttribute as String),
                  CFEqual(currentWindow, window) else {
                throw ChatGPTComposerError.missing
            }
            await MainActor.run {
                NSPasteboard.general.clearContents()
                NSPasteboard.general.setString(text, forType: .string)
            }
            try postCommandKey(0, to: app.processIdentifier) // Command-A
            try await Task.sleep(for: .milliseconds(80))
            try postCommandKey(9, to: app.processIdentifier) // Command-V
            for _ in 0..<30 {
                try await Task.sleep(for: .milliseconds(100))
                if ChatGPTComposerValue.matchesDraft(
                    copyStringAttribute(composer, kAXValueAttribute as String),
                    expected: text
                ) { return .verified }
            }
            // ChatGPT 的网页编辑器有时已经完成粘贴，但不再通过 AXValue 回传富文本内容。
            // 前台 App、目标窗口和输入焦点均已核对，此时交给用户在发送前做最后确认。
            return .inserted
    }

    func openNewConversation() async throws {
        guard let url = ChatGPTDraftDeepLink.make(prompt: nil) else {
            throw ChatGPTComposerError.newConversationUnavailable
        }
        let opened = await MainActor.run { NSWorkspace.shared.open(url) }
        guard opened else { throw ChatGPTComposerError.newConversationUnavailable }
    }

    @MainActor private func launchChatGPTIfNeeded() async throws {
        let identifiers = ["com.openai.chat", "com.openai.chatgpt", "com.openai.codex"]
        if runningChatGPTApplication() != nil { return }
        guard let url = identifiers.lazy.compactMap({ NSWorkspace.shared.urlForApplication(withBundleIdentifier: $0) }).first else {
            throw ChatGPTAppCaptureError.appNotRunning
        }
        _ = try await NSWorkspace.shared.openApplication(at: url, configuration: .init())
        for _ in 0..<120 {
            if runningChatGPTApplication() != nil { return }
            try await Task.sleep(for: .milliseconds(100))
        }
        throw ChatGPTAppCaptureError.appNotRunning
    }

    /// 只调用明确标注“新建聊天”的系统菜单或按钮，失败时不退回当前会话。
    private func newConversationWindow() async throws -> (NSRunningApplication, AXUIElement, AXUIElement) {
        guard AXIsProcessTrusted() else { throw ChatGPTAppCaptureError.permissionRequired }
        guard let app = runningChatGPTApplication() else { throw ChatGPTAppCaptureError.appNotRunning }
        app.activate()
        let application = AXUIElementCreateApplication(app.processIdentifier)
        AXUIElementSetMessagingTimeout(application, 2)
        for attempt in 0..<120 {
            if NSWorkspace.shared.frontmostApplication?.processIdentifier == app.processIdentifier { break }
            if attempt.isMultiple(of: 10) { app.activate() }
            try await Task.sleep(for: .milliseconds(100))
        }
        guard NSWorkspace.shared.frontmostApplication?.processIdentifier == app.processIdentifier else {
            throw ChatGPTComposerError.newConversationUnavailable
        }
        func findAction(_ element: AXUIElement, depth: Int = 0) -> AXUIElement? {
            var visited = 0
            func visit(_ element: AXUIElement, depth: Int) -> AXUIElement? {
                guard visited < 6_000, depth < 60 else { return nil }
                visited += 1
                let role = copyStringAttribute(element, kAXRoleAttribute as String) ?? ""
                if [kAXMenuItemRole as String, kAXButtonRole as String].contains(role),
                   [kAXTitleAttribute, kAXDescriptionAttribute, kAXHelpAttribute].contains(where: {
                       ChatGPTNewConversationAction.matches(copyStringAttribute(element, $0 as String) ?? "")
                   }) {
                    var enabled: CFTypeRef?
                    if AXUIElementCopyAttributeValue(element, kAXEnabledAttribute as CFString, &enabled) == .success,
                       (enabled as? Bool) == false { return nil }
                    return element
                }
                for child in copyElementsAttribute(element, kAXChildrenAttribute as String) {
                    if let result = visit(child, depth: depth + 1) { return result }
                }
                return nil
            }
            return visit(element, depth: depth)
        }
        var action: AXUIElement?
        for _ in 0..<120 where action == nil {
            if let menu = copyElementAttribute(application, kAXMenuBarAttribute as String) {
                action = findAction(menu)
            }
            if action == nil,
               let window = copyElementAttribute(application, kAXFocusedWindowAttribute as String)
                ?? copyElementsAttribute(application, kAXWindowsAttribute as String).first {
                action = findAction(window)
            }
            if action == nil { try await Task.sleep(for: .milliseconds(100)) }
        }
        guard let action, AXUIElementPerformAction(action, kAXPressAction as CFString) == .success else {
            throw ChatGPTComposerError.newConversationUnavailable
        }
        // 新页面可能复用原窗口；这里只等待新建动作落地，编辑框由填写流程继续轮询。
        try await Task.sleep(for: .milliseconds(700))
        guard let window = copyElementAttribute(application, kAXFocusedWindowAttribute as String)
            ?? copyElementsAttribute(application, kAXWindowsAttribute as String).first else {
            throw ChatGPTAppCaptureError.windowUnavailable
        }
        return (app, application, window)
    }

    private func runningChatGPTApplication() -> NSRunningApplication? {
        let identifiers = Set(["com.openai.chat", "com.openai.chatgpt", "com.openai.codex"])
        return NSWorkspace.shared.runningApplications.first(where: {
            identifiers.contains($0.bundleIdentifier ?? "") && $0.activationPolicy == .regular
        })
    }

    private func composerCandidates(in window: AXUIElement) -> [AXUIElement] {
        var result: [AXUIElement] = []
        var visited = 0
        func visit(_ element: AXUIElement, depth: Int) {
            guard visited < 6_000, depth < 80 else { return }
            visited += 1
            let role = copyStringAttribute(element, kAXRoleAttribute as String) ?? ""
            if role == kAXTextAreaRole as String {
                var enabled: CFTypeRef?
                let isDisabled = AXUIElementCopyAttributeValue(
                    element,
                    kAXEnabledAttribute as CFString,
                    &enabled
                ) == .success && (enabled as? Bool) == false
                if !isDisabled { result.append(element) }
                return
            }
            for child in copyElementsAttribute(element, kAXChildrenAttribute as String) {
                visit(child, depth: depth + 1)
            }
        }
        visit(window, depth: 0)
        return result
    }

    private func composerScore(_ composer: AXUIElement) -> Int {
        let labels = [kAXPlaceholderValueAttribute, kAXDescriptionAttribute, kAXHelpAttribute, kAXTitleAttribute]
            .compactMap { copyStringAttribute(composer, $0 as String) }
            .joined(separator: " ")
            .lowercased()
        let markers = ["message", "chatgpt", "codex", "ask", "prompt", "消息", "询问", "输入"]
        return markers.reduce(0) { $0 + (labels.contains($1) ? 1 : 0) }
    }

    private func sameElementOrDescendant(_ element: AXUIElement, of ancestor: AXUIElement) -> Bool {
        var current: AXUIElement? = element
        for _ in 0..<12 {
            guard let candidate = current else { return false }
            if CFEqual(candidate, ancestor) { return true }
            current = copyElementAttribute(candidate, kAXParentAttribute as String)
        }
        return false
    }

    private func postCommandKey(_ keyCode: CGKeyCode, to processID: pid_t) throws {
        guard let down = CGEvent(keyboardEventSource: nil, virtualKey: keyCode, keyDown: true),
              let up = CGEvent(keyboardEventSource: nil, virtualKey: keyCode, keyDown: false) else {
            throw ChatGPTComposerError.writeFailed
        }
        down.flags = .maskCommand
        up.flags = .maskCommand
        down.postToPid(processID)
        up.postToPid(processID)
    }

    private func copyStringAttribute(_ element: AXUIElement, _ attribute: String) -> String? {
        var value: CFTypeRef?
        guard AXUIElementCopyAttributeValue(element, attribute as CFString, &value) == .success else { return nil }
        return value as? String
    }

    private func copyElementAttribute(_ element: AXUIElement, _ attribute: String) -> AXUIElement? {
        var value: CFTypeRef?
        guard AXUIElementCopyAttributeValue(element, attribute as CFString, &value) == .success,
              let value,
              CFGetTypeID(value) == AXUIElementGetTypeID() else { return nil }
        return unsafeDowncast(value, to: AXUIElement.self)
    }

    private func copyElementsAttribute(_ element: AXUIElement, _ attribute: String) -> [AXUIElement] {
        var value: CFTypeRef?
        guard AXUIElementCopyAttributeValue(element, attribute as CFString, &value) == .success else { return [] }
        return value as? [AXUIElement] ?? []
    }
}

enum ChatGPTFillResult: Sendable {
    case verified
    case inserted
}

enum CodexDraftDeepLink {
    static func make(workspacePath: String, prompt: String) -> URL? {
        guard workspacePath.hasPrefix("/"), !prompt.isEmpty else { return nil }
        var components = URLComponents()
        components.scheme = "codex"
        components.host = "threads"
        components.path = "/new"
        components.queryItems = [
            URLQueryItem(name: "mode", value: "work"),
            URLQueryItem(name: "path", value: workspacePath),
            URLQueryItem(name: "prompt", value: prompt),
        ]
        return components.url
    }
}

enum CodexTaskDeepLink {
    static func make(threadID: String) -> URL? {
        let normalizedThreadID = threadID.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !normalizedThreadID.isEmpty else { return nil }
        var components = URLComponents()
        components.scheme = "codex"
        components.host = "threads"
        components.path = "/\(normalizedThreadID)"
        return components.url
    }
}

enum ChatGPTDraftDeepLink {
    static func make(prompt: String?) -> URL? {
        var components = URLComponents()
        components.scheme = "codex"
        components.host = "threads"
        components.path = "/new"
        components.queryItems = [URLQueryItem(name: "mode", value: "chat")]
        if let prompt, !prompt.isEmpty {
            components.queryItems?.append(URLQueryItem(name: "prompt", value: prompt))
        }
        return components.url
    }
}

private extension String {
    var nilIfEmpty: String? { isEmpty ? nil : self }
}

/// 限定新建聊天操作，避免误触新建项目、临时聊天或发送按钮。
enum ChatGPTNewConversationAction {
    static func matches(_ title: String) -> Bool {
        let normalized = title.trimmingCharacters(in: .whitespacesAndNewlines)
            .replacingOccurrences(of: "…", with: "").lowercased()
        return ["new chat", "new conversation", "新聊天", "新建聊天", "新对话", "新建对话", "新建会话"].contains(normalized)
    }
}

enum ChatGPTComposerValue {
    static func hasNoDraft(value: String?, placeholder: String?) -> Bool {
        let value = value?.trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
        let placeholder = placeholder?.trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
        let knownPlaceholders = [
            "ask anything", "message chatgpt", "message codex", "send a message",
            "询问任何问题", "给 chatgpt 发消息", "给 codex 发消息", "发送消息",
        ]
        return value.isEmpty
            || (!placeholder.isEmpty && value == placeholder)
            || knownPlaceholders.contains(value.lowercased())
    }

    static func matchesDraft(_ value: String?, expected: String) -> Bool {
        func normalized(_ text: String) -> String {
            text.replacingOccurrences(of: "\r\n", with: "\n")
                .replacingOccurrences(of: "\r", with: "\n")
        }
        return value.map(normalized) == normalized(expected)
    }
}
