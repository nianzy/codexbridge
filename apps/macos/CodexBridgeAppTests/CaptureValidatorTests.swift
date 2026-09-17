import Foundation
import Testing
@testable import CodexBridge

struct CaptureValidatorTests {
    @Test func acceptsRenderedChatGPTSelection() throws {
        let payload = makePayload()
        let conversation = try CaptureValidator().validate(payload)

        #expect(conversation.sourceKind == .chatGPTWeb)
        #expect(conversation.turns.count == 1)
        #expect(conversation.contentHash.count == 64)
        #expect(conversation.warnings.isEmpty)
    }

    @Test func rejectsMismatchedSelection() {
        let payload = makePayload(selectedIDs: ["missing"])
        #expect(throws: CaptureValidationError.invalidSelection) {
            try CaptureValidator().validate(payload)
        }
    }

    @Test func reportsAttachmentBoundary() throws {
        let payload = makePayload(attachmentCount: 2, complete: false)
        let conversation = try CaptureValidator().validate(payload)
        #expect(conversation.warnings.count == 2)
    }

    @Test func identicalCaptureKeepsStableLocalIdentity() throws {
        let validator = CaptureValidator()
        let first = try validator.validate(makePayload())
        let second = try validator.validate(makePayload())
        #expect(first.id == second.id)
        #expect(first.contentHash == second.contentHash)
    }

    private func makePayload(
        selectedIDs: [String] = ["turn-0"],
        attachmentCount: Int = 0,
        complete: Bool = true
    ) -> CapturePayload {
        CapturePayload(
            schemaVersion: 1,
            source: .init(
                kind: "chatgpt-web",
                url: URL(string: "https://chatgpt.com/c/test")!,
                conversationId: "test",
                title: "测试会话"
            ),
            capture: .init(
                capturedAt: Date(timeIntervalSince1970: 0),
                scope: "rendered-current-page",
                attachmentCount: attachmentCount,
                complete: complete
            ),
            selection: .init(selectedTurnIds: selectedIDs),
            turns: [
                .init(
                    id: "turn-0",
                    index: 0,
                    user: .init(id: "user-0", idSource: "dom", text: "需求"),
                    assistant: .init(id: "assistant-0", idSource: "dom", text: "方案"),
                    complete: true
                ),
            ],
            warnings: []
        )
    }
}

struct CodexAppServerClientTests {
    @Test func readsOnlyUserVisibleMessagesAndDropsReasoning() async throws {
        let directory = FileManager.default.temporaryDirectory
            .appendingPathComponent("codexbridge-codex-tests", isDirectory: true)
            .appendingPathComponent(UUID().uuidString, isDirectory: true)
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        let executable = directory.appendingPathComponent("fake-codex")
        let script = #"""
        #!/bin/sh
        while IFS= read -r line; do
          case "$line" in
            *'"method":"initialize"'*)
              printf '%s\n' '{"jsonrpc":"2.0","id":1,"result":{}}'
              ;;
            *thread*read*)
              printf '%s\n' '{"jsonrpc":"2.0","id":2,"result":{"thread":{"id":"thread-1","cwd":"/tmp/project","preview":"实现 Codex Bridge","name":"任务正文测试","createdAt":1,"updatedAt":2,"recencyAt":2,"model":"gpt-test","status":{"type":"idle"},"turns":[{"id":"turn-1","status":"completed","items":[{"id":"u1","type":"userMessage","content":[{"type":"text","text":"公开需求"}]},{"id":"r1","type":"reasoning","content":["绝不能展示的推理"],"summary":["同样不展示"]},{"id":"a1","type":"agentMessage","text":"公开结果"}]}]}}}'
              ;;
          esac
        done
        """#
        try script.write(to: executable, atomically: true, encoding: .utf8)
        try FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: executable.path)

        let client = CodexAppServerClient(executableURL: executable, requestTimeout: .seconds(2))
        let conversation = try await client.readThread(id: "thread-1")
        await client.stop()

        #expect(conversation.turns.count == 1)
        #expect(conversation.turns.first?.user.text == "公开需求")
        #expect(conversation.turns.first?.assistant?.text == "公开结果")
        #expect(!conversation.turns.description.contains("绝不能展示的推理"))
        #expect(conversation.runtimeStatus == .idle)
    }
}

struct LargeConversationTransportTests {
    @Test func reconstructsLargeResponseAcrossPipeChunks() async throws {
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent("codexbridge-chunks-\(UUID())")
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: directory) }
        let text = String(repeating: "正文内容0123456789\n", count: 600_000)
        let response: [String: Any] = ["id": 2, "result": ["thread": ["id": "large", "turns": [["id": "r", "status": "completed", "items": [
            ["type": "userMessage", "content": [["type": "text", "text": "读取大段正文"]]],
            ["type": "agentMessage", "text": text]
        ]]]]]]
        let data = try JSONSerialization.data(withJSONObject: response)
        try (data + Data([10])).write(to: directory.appendingPathComponent("response.json"))
        let executable = directory.appendingPathComponent("fake-codex")
        let script = """
        #!/bin/sh
        while IFS= read -r line; do
          case "$line" in
            *'"method":"initialize"'*) printf '%s\\n' '{"id":1,"result":{}}' ;;
            *thread*read*) cat '\(directory.path)/response.json' ;;
          esac
        done
        """
        try script.write(to: executable, atomically: true, encoding: .utf8)
        try FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: executable.path)
        let client = CodexAppServerClient(executableURL: executable, requestTimeout: .seconds(5))
        let conversation = try await client.readThread(id: "large")
        await client.stop()
        #expect(conversation.turns[0].assistant?.text == text)
    }
}

struct CodexDraftTransportTests {
    @Test func createsConfiguredThreadWithoutStartingTurn() async throws {
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent("codexbridge-submit-\(UUID())")
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: directory) }
        let executable = directory.appendingPathComponent("fake-codex")
        let recorded = directory.appendingPathComponent("thread.json")
        let script = """
        #!/bin/sh
        while IFS= read -r line; do
          case "$line" in
            *'"method":"initialize"'*) printf '%s\n' '{"id":1,"result":{}}' ;;
            *thread*start*)
              printf '%s\n' "$line" > '\(recorded.path)'
              printf '%s\n' '{"id":2,"result":{"thread":{"id":"created-thread"}}}' ;;
          esac
        done
        """
        try script.write(to: executable, atomically: true, encoding: .utf8)
        try FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: executable.path)
        let conversation = CapturedConversation.syntheticSamples()[0]
        var draft = HandoffDraft.empty(for: conversation)
        draft.instruction = "继续实现，保留换行\n与中文"
        draft.workspacePath = "/tmp/codexbridge-transfer"
        draft.modelID = "test-model"
        draft.reasoningEffort = "medium"
        let frozen = try ReferenceSerializer().freeze(conversation: conversation, draft: draft)
        let client = CodexAppServerClient(executableURL: executable)
        let result = try await client.createDraftThread(frozen)
        await client.stop()
        #expect(result == "created-thread")
        let request = try JSONDecoder().decode(JSONValue.self, from: Data(contentsOf: recorded))
        #expect(request["method"]?.stringValue == "thread/start")
        #expect(request["params"]?["cwd"]?.stringValue == "/tmp/codexbridge-transfer")
        #expect(request["params"]?["model"]?.stringValue == "test-model")
        #expect(request["params"]?["config"]?["model_reasoning_effort"]?.stringValue == "medium")
    }
}

struct ChatGPTNewConversationActionTests {
    @Test func chatGPTDraftLinkCreatesNewChatAndPreservesPrompt() throws {
        let prompt = "第一行\n第二行：中文与 ? & ="
        let url = try #require(ChatGPTDraftDeepLink.make(prompt: prompt))
        let components = try #require(URLComponents(url: url, resolvingAgainstBaseURL: false))
        let values = Dictionary(uniqueKeysWithValues: (components.queryItems ?? []).compactMap { item in
            item.value.map { (item.name, $0) }
        })

        #expect(components.scheme == "codex")
        #expect(components.host == "threads")
        #expect(components.path == "/new")
        #expect(values["mode"] == "chat")
        #expect(values["prompt"] == prompt)
        #expect(values["path"] == nil)
    }

    @Test func chatGPTLinkCanOpenAnEmptyNewConversation() throws {
        let url = try #require(ChatGPTDraftDeepLink.make(prompt: nil))
        let components = try #require(URLComponents(url: url, resolvingAgainstBaseURL: false))
        #expect(components.queryItems == [URLQueryItem(name: "mode", value: "chat")])
    }

    @Test func codexDraftLinkCreatesNewWorkTaskAndPreservesPrompt() throws {
        let prompt = "第一行\n第二行：中文与 ? & ="
        let url = try #require(CodexDraftDeepLink.make(
            workspacePath: "/Users/test/Project Folder",
            prompt: prompt
        ))
        let components = try #require(URLComponents(url: url, resolvingAgainstBaseURL: false))
        let values = Dictionary(uniqueKeysWithValues: (components.queryItems ?? []).compactMap { item in
            item.value.map { (item.name, $0) }
        })

        #expect(components.scheme == "codex")
        #expect(components.host == "threads")
        #expect(components.path == "/new")
        #expect(values["mode"] == "work")
        #expect(values["path"] == "/Users/test/Project Folder")
        #expect(values["prompt"] == prompt)
    }

    @Test func codexTaskLinkLocatesExistingTaskWithoutCreatingOrSubmitting() throws {
        let threadID = "019d1234-abcd-7000-8000-123456789abc"
        let url = try #require(CodexTaskDeepLink.make(threadID: "  \(threadID)\n"))
        let components = try #require(URLComponents(url: url, resolvingAgainstBaseURL: false))

        #expect(components.scheme == "codex")
        #expect(components.host == "threads")
        #expect(components.path == "/\(threadID)")
        #expect(components.queryItems == nil)
    }

    @Test func matchesOnlyExplicitNewChatActions() {
        for label in ["New Chat", "New conversation…", "新聊天", "新建对话", " 新建会话 "] {
            #expect(ChatGPTNewConversationAction.matches(label))
        }
        for label in ["Send", "发送", "新建项目", "New task", "Temporary Chat", "删除对话", ""] {
            #expect(!ChatGPTNewConversationAction.matches(label))
        }
    }

    @Test func treatsPlaceholderAsAnEmptyComposer() {
        #expect(ChatGPTComposerValue.hasNoDraft(value: "", placeholder: "Message ChatGPT"))
        #expect(ChatGPTComposerValue.hasNoDraft(value: "Message ChatGPT", placeholder: "Message ChatGPT"))
        #expect(ChatGPTComposerValue.hasNoDraft(value: "询问任何问题", placeholder: "询问任何问题"))
        #expect(ChatGPTComposerValue.hasNoDraft(value: "Ask anything", placeholder: nil))
        #expect(!ChatGPTComposerValue.hasNoDraft(value: "已有草稿", placeholder: "询问任何问题"))
        #expect(!ChatGPTComposerValue.hasNoDraft(value: "继续上一轮任务", placeholder: nil))
    }

    @Test func verifiesPastedDraftAcrossLineEndingStyles() {
        #expect(ChatGPTComposerValue.matchesDraft("第一行\r\n第二行", expected: "第一行\n第二行"))
        #expect(!ChatGPTComposerValue.matchesDraft("第一行", expected: "第一行\n第二行"))
        #expect(!ChatGPTComposerValue.matchesDraft(nil, expected: "内容"))
    }
}

struct BrowserExtensionInstallationTests {
    @Test func updatesPreparedExtensionWhenBundledVersionChanges() throws {
        let root = FileManager.default.temporaryDirectory
            .appendingPathComponent("codexbridge-extension-update-\(UUID())", isDirectory: true)
        let source = root.appendingPathComponent("source", isDirectory: true)
        let support = root.appendingPathComponent("support", isDirectory: true)
        defer { try? FileManager.default.removeItem(at: root) }

        try writeExtension(version: "1.1.0", marker: "old", to: source)
        let installer = NativeMessagingInstaller(extensionSourceURL: source, applicationSupportURL: support)
        let initial = try installer.prepareExtensionDirectory()
        #expect(initial.version == "1.1.0")
        #expect(!initial.replacedExistingInstallation)

        try FileManager.default.removeItem(at: source)
        try writeExtension(version: "1.1.1", marker: "new", to: source)
        let updated = try installer.prepareExtensionDirectory()
        #expect(updated.version == "1.1.1")
        #expect(updated.replacedExistingInstallation)
        #expect(try String(contentsOf: updated.url.appendingPathComponent("marker.txt"), encoding: .utf8) == "new")

        let unchanged = try installer.prepareExtensionDirectory()
        #expect(!unchanged.replacedExistingInstallation)
    }

    @Test func detectsEnabledExtensionAcrossChromeAndEdge() throws {
        let chromeRoot = FileManager.default.temporaryDirectory
            .appendingPathComponent("codexbridge-chrome-state-\(UUID())", isDirectory: true)
        let edgeRoot = FileManager.default.temporaryDirectory
            .appendingPathComponent("codexbridge-edge-state-\(UUID())", isDirectory: true)
        let chromeProfile = chromeRoot.appendingPathComponent("Default", isDirectory: true)
        let edgeProfile = edgeRoot.appendingPathComponent("Profile 1", isDirectory: true)
        try FileManager.default.createDirectory(at: chromeProfile, withIntermediateDirectories: true)
        try FileManager.default.createDirectory(at: edgeProfile, withIntermediateDirectories: true)
        defer {
            try? FileManager.default.removeItem(at: chromeRoot)
            try? FileManager.default.removeItem(at: edgeRoot)
        }

        let roots: [(SupportedExtensionBrowser, URL)] = [
            (.chrome, chromeRoot),
            (.edge, edgeRoot),
        ]
        #expect(NativeMessagingInstaller.browserExtensionInstallation(browserRoots: roots) == .missing)

        try writePreferences(state: 0, disableReasons: [1], to: chromeProfile)
        #expect(
            NativeMessagingInstaller.browserExtensionInstallation(browserRoots: roots)
                == .disabled(browser: .chrome, profile: "Default")
        )

        try writePreferences(state: nil, disableReasons: [], to: edgeProfile)
        #expect(
            NativeMessagingInstaller.browserExtensionInstallation(browserRoots: roots)
                == .enabled(browser: .edge, profile: "Profile 1")
        )
    }

    private func writePreferences(state: Int?, disableReasons: [Int], to profile: URL) throws {
        var extensionSettings: [String: Any] = ["disable_reasons": disableReasons]
        if let state {
            extensionSettings["state"] = state
        }
        let object: [String: Any] = [
            "extensions": [
                "settings": [
                    NativeMessagingInstaller.extensionID: extensionSettings,
                ],
            ],
        ]
        let data = try JSONSerialization.data(withJSONObject: object)
        try data.write(to: profile.appendingPathComponent("Preferences"), options: .atomic)
    }

    private func writeExtension(version: String, marker: String, to directory: URL) throws {
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        let manifest = try JSONSerialization.data(withJSONObject: [
            "manifest_version": 3,
            "name": "Codex Bridge Test",
            "version": version,
        ])
        try manifest.write(to: directory.appendingPathComponent("manifest.json"), options: .atomic)
        try marker.write(to: directory.appendingPathComponent("marker.txt"), atomically: true, encoding: .utf8)
    }
}
