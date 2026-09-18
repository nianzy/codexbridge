import AppKit
import ApplicationServices
import CryptoKit
import Foundation

enum ChatGPTAppCaptureError: LocalizedError {
    case permissionRequired
    case appNotRunning
    case windowUnavailable
    case noVisibleTurns

    var errorDescription: String? {
        switch self {
        case .permissionRequired: "需要开启辅助功能权限，才能读取已打开的对话或将草稿填入 Codex。"
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

    /// Deep Link 只负责打开空白会话，正文通过辅助功能写入编辑框；不触发发送。
    func fillNewConversation(with text: String) async throws -> ChatGPTFillResult {
        guard !text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
            throw ChatGPTComposerError.emptyDraft
        }
        guard let url = ChatGPTDraftDeepLink.make() else {
            throw ChatGPTComposerError.newConversationUnavailable
        }
        return try await openComposerAndFill(url: url, text: text)
    }

    /// Deep Link 只传递工作目录，正文通过辅助功能写入编辑框；不触发发送或执行。
    func fillCodexDraft(workspacePath: String, text: String) async throws -> ChatGPTFillResult {
        guard !text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
            throw ChatGPTComposerError.emptyDraft
        }
        guard let url = CodexDraftDeepLink.make(workspacePath: workspacePath) else {
            throw ChatGPTComposerError.newConversationUnavailable
        }
        return try await openComposerAndFill(url: url, text: text)
    }

    private func openComposerAndFill(url: URL, text: String) async throws -> ChatGPTFillResult {
        guard AXIsProcessTrusted() else { throw ChatGPTAppCaptureError.permissionRequired }
        let opened = await MainActor.run { NSWorkspace.shared.open(url) }
        guard opened else { throw ChatGPTComposerError.newConversationUnavailable }

        for attempt in 0..<120 {
            if let app = runningCodexApplication() {
                if attempt.isMultiple(of: 10) { app.activate() }
                if NSWorkspace.shared.frontmostApplication?.processIdentifier == app.processIdentifier {
                    let application = AXUIElementCreateApplication(app.processIdentifier)
                    AXUIElementSetMessagingTimeout(application, 2)
                    try await Task.sleep(for: .milliseconds(700))
                    if let window = copyElementAttribute(application, kAXFocusedWindowAttribute as String)
                        ?? copyElementsAttribute(application, kAXWindowsAttribute as String).first {
                        return try await fillComposer(
                            text,
                            app: app,
                            application: application,
                            window: window,
                            requiresEmptyComposer: true
                        )
                    }
                }
            }
            try await Task.sleep(for: .milliseconds(100))
        }
        throw ChatGPTComposerError.newConversationUnavailable
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
        guard let current = copyElementAttribute(application, kAXFocusedWindowAttribute as String),
              CFEqual(current, window) else {
            throw ChatGPTComposerError.missing
        }
        if requiresEmptyComposer {
            let value = copyStringAttribute(composer, kAXValueAttribute as String)
            let placeholder = copyStringAttribute(composer, kAXPlaceholderValueAttribute as String)
            guard ChatGPTComposerValue.hasNoDraft(value: value, placeholder: placeholder) else {
                throw ChatGPTComposerError.occupied
            }
        }
        // 先直接写入 AXValue，避免正文进入 URL 或剪贴板；富文本编辑器不支持时再使用受控粘贴。
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
        let directWrite = AXUIElementSetAttributeValue(composer, kAXValueAttribute as CFString, text as CFString)
        if directWrite == .success {
            for _ in 0..<10 {
                try await Task.sleep(for: .milliseconds(100))
                if ChatGPTComposerValue.matchesDraft(
                    copyStringAttribute(composer, kAXValueAttribute as String),
                    expected: text
                ) { return .verified }
            }
            let value = copyStringAttribute(composer, kAXValueAttribute as String)
            let placeholder = copyStringAttribute(composer, kAXPlaceholderValueAttribute as String)
            if !ChatGPTComposerValue.hasNoDraft(value: value, placeholder: placeholder) {
                return .inserted
            }
        }
        let pasteboardSnapshot = await MainActor.run { PasteboardSnapshot.installing(text) }
        do {
            try postCommandKey(0, to: app.processIdentifier) // Command-A
            try await Task.sleep(for: .milliseconds(80))
            try postCommandKey(9, to: app.processIdentifier) // Command-V
            try await Task.sleep(for: .milliseconds(180))
        } catch {
            await MainActor.run { pasteboardSnapshot.restoreIfUnchanged() }
            throw error
        }
        await MainActor.run { pasteboardSnapshot.restoreIfUnchanged() }
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
        guard let url = ChatGPTDraftDeepLink.make() else {
            throw ChatGPTComposerError.newConversationUnavailable
        }
        let opened = await MainActor.run { NSWorkspace.shared.open(url) }
        guard opened else { throw ChatGPTComposerError.newConversationUnavailable }
    }

    private func runningChatGPTApplication() -> NSRunningApplication? {
        let identifiers = Set(["com.openai.chat", "com.openai.chatgpt", "com.openai.codex"])
        return NSWorkspace.shared.runningApplications.first(where: {
            identifiers.contains($0.bundleIdentifier ?? "") && $0.activationPolicy == .regular
        })
    }

    private func runningCodexApplication() -> NSRunningApplication? {
        NSWorkspace.shared.runningApplications.first {
            $0.bundleIdentifier == "com.openai.codex" && $0.activationPolicy == .regular
        }
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

private struct PasteboardSnapshot: Sendable {
    private struct Item: Sendable {
        let values: [String: Data]
    }

    private let items: [Item]
    private let installedChangeCount: Int

    @MainActor
    static func installing(_ text: String) -> PasteboardSnapshot {
        let pasteboard = NSPasteboard.general
        let items = (pasteboard.pasteboardItems ?? []).map { item in
            Item(values: Dictionary(uniqueKeysWithValues: item.types.compactMap { type in
                item.data(forType: type).map { (type.rawValue, $0) }
            }))
        }
        pasteboard.clearContents()
        pasteboard.setString(text, forType: .string)
        return PasteboardSnapshot(items: items, installedChangeCount: pasteboard.changeCount)
    }

    @MainActor
    func restoreIfUnchanged() {
        let pasteboard = NSPasteboard.general
        guard pasteboard.changeCount == installedChangeCount else { return }
        pasteboard.clearContents()
        guard !items.isEmpty else { return }
        let restoredItems = items.map { item in
            let restored = NSPasteboardItem()
            for (rawType, data) in item.values {
                restored.setData(data, forType: NSPasteboard.PasteboardType(rawType))
            }
            return restored
        }
        pasteboard.writeObjects(restoredItems)
    }
}

enum CodexDraftDeepLink {
    static func make(workspacePath: String) -> URL? {
        guard workspacePath.hasPrefix("/") else { return nil }
        var components = URLComponents()
        components.scheme = "codex"
        components.host = "threads"
        components.path = "/new"
        components.queryItems = [
            URLQueryItem(name: "mode", value: "work"),
            URLQueryItem(name: "path", value: workspacePath),
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
    static func make() -> URL? {
        var components = URLComponents()
        components.scheme = "codex"
        components.host = "threads"
        components.path = "/new"
        components.queryItems = [URLQueryItem(name: "mode", value: "chat")]
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
