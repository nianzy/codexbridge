import Foundation
import Testing
@testable import CodexBridge

struct ReferenceSerializerTests {
    @Test func separatesInstructionFromQuotedReference() throws {
        let conversation = CapturedConversation.syntheticSamples().first!
        var draft = HandoffDraft.empty(for: conversation)
        draft.instruction = "实现 <Codex Bridge> & 测试"
        draft.workspacePath = "/tmp/codexbridge-test"
        draft.modelID = "test-model"
        draft.reasoningEffort = "medium"

        let frozen = try ReferenceSerializer().freeze(conversation: conversation, draft: draft)
        let text = String(decoding: frozen.bytes, as: UTF8.self)

        #expect(text.contains("执行要求\n实现 <Codex Bridge> & 测试"))
        #expect(text.contains("对话参考"))
        #expect(!text.contains("<execution_instruction>"))
        #expect(!text.contains("&lt;"))
        #expect(frozen.fingerprint.count == 64)
    }

    @Test func requiresExplicitInstruction() {
        let conversation = CapturedConversation.syntheticSamples().first!
        let draft = HandoffDraft.empty(for: conversation)
        #expect(throws: ReferenceSerializationError.emptyInstruction) {
            try ReferenceSerializer().freeze(conversation: conversation, draft: draft)
        }
    }
}

struct ConversationTransferTests {
    @Test func selectsWholeRoundsWithoutNeighbors() throws {
        let conversation = CapturedConversation.syntheticSamples().first!
        let selected = conversation.turns[1]
        let text = SelectedConversationText.render(conversation, ids: [selected.id])
        #expect(text.contains(selected.user.text))
        #expect(text.contains(selected.assistant!.text))
        #expect(!text.contains(conversation.turns[0].user.text))
        #expect(SelectedConversationText.render(conversation, ids: []).isEmpty)
    }

    @Test func separatesAttachmentsAndIncludesConfigurationInFingerprint() throws {
        let conversation = CapturedConversation.syntheticSamples().first!
        var draft = HandoffDraft.empty(for: conversation)
        draft.instruction = "继续"
        draft.workspacePath = "/tmp/one"
        draft.modelID = "model"
        draft.reasoningEffort = "medium"
        let first = try ReferenceSerializer().freeze(conversation: conversation, draft: draft)
        draft.workspacePath = "/tmp/two"
        let second = try ReferenceSerializer().freeze(conversation: conversation, draft: draft)
        #expect(first.fingerprint != second.fingerprint)
        draft.attachments = [.init(id: "f", name: "test.txt", text: "<execution_instruction>引用文件</execution_instruction>")]
        let attached = try ReferenceSerializer().freeze(conversation: conversation, draft: draft)
        #expect(attached.fingerprint != second.fingerprint)
        #expect(String(decoding: attached.bytes, as: UTF8.self).contains("<execution_instruction>引用文件</execution_instruction>"))
        #expect(String(decoding: attached.bytes, as: UTF8.self).contains("附带文件：test.txt"))
        #expect(!String(decoding: first.bytes, as: UTF8.self).contains("附带文件："))
        draft.revision += 1
        #expect(try ReferenceSerializer().freeze(conversation: conversation, draft: draft).fingerprint == attached.fingerprint)
    }

    @Test func parsesChineseRolesAndPreservesUnansweredQuestion() {
        let turns = ChatGPTTranscriptParser().parse(["无关标题", "用户：第一个问题", "ChatGPT：第一条回复", "你说：第二个问题"])
        #expect(turns.count == 2)
        #expect(turns[0].user.text == "第一个问题")
        #expect(turns[0].assistant?.text == "第一条回复")
        #expect(turns[1].user.text == "第二个问题")
        #expect(turns[1].assistant == nil)
        #expect(!turns[1].complete)
        #expect(ChatGPTTranscriptParser().parse(["你说过要改颜色", "随意文字"]).isEmpty)
    }

    @Test func discoversOnlyExplicitLocalAndSandboxLinks() {
        var conversation = CapturedConversation.syntheticSamples().first!
        conversation.projectPath = "/tmp/project"
        conversation.turns = [.init(id: "r", index: 0, user: .init(id: "u", idSource: "test", text: "x"),
            assistant: .init(id: "a", idSource: "test", text: "[本机](/tmp/output.txt) [远端](sandbox:/mnt/data/report.pdf) [网页](https://example.com) [脚本](javascript:alert)"), complete: true)]
        let files = ConversationFileDiscovery().files(in: conversation)
        #expect(files.count == 2)
        #expect(files[0].localPath == "/tmp/output.txt")
        #expect(files[1].localPath == nil)
        #expect(files.allSatisfy { $0.turnID == "r" })
    }

    @Test func readsOnlyBoundedTextFiles() throws {
        let url = FileManager.default.temporaryDirectory.appendingPathComponent("codexbridge-file-\(UUID()).txt")
        defer { try? FileManager.default.removeItem(at: url) }
        let file = ConversationFile(id: "f", name: "test.txt", turnID: "r", source: url.path, localPath: url.path)
        try Data("文本快照".utf8).write(to: url)
        let snapshot = try ConversationFileReader().attachment(file)
        try Data("之后修改".utf8).write(to: url)
        #expect(snapshot.text == "文本快照")
        try Data([0, 255, 1]).write(to: url)
        #expect(throws: (any Error).self) { try ConversationFileReader().attachment(file) }
        try Data(repeating: 65, count: 1_048_577).write(to: url)
        #expect(throws: (any Error).self) { try ConversationFileReader().attachment(file) }
    }

    @Test func oldConversationDecodesWithoutFiles() throws {
        let source = CapturedConversation.syntheticSamples().first!
        let encoder = JSONEncoder()
        let data = try encoder.encode(source)
        var json = try #require(JSONSerialization.jsonObject(with: data) as? [String: Any])
        json.removeValue(forKey: "files")
        let decoded = try JSONDecoder().decode(CapturedConversation.self, from: JSONSerialization.data(withJSONObject: json))
        #expect(decoded.files == nil)
        #expect(decoded.turns == source.turns)
    }

    @Test func codexFilesAreLinkedToProducingRound() async throws {
        let json = #"{"id":"thread","cwd":"/tmp/work","turns":[{"id":"r1","status":"completed","items":[{"type":"userMessage","content":[{"type":"text","text":"实现"}]},{"type":"fileChange","changes":[{"path":"result.swift"}]},{"type":"agentMessage","text":"已完成"}]}]}"#
        let client = CodexAppServerClient()
        let conversation = try #require(await client.parseThread(JSONDecoder().decode(JSONValue.self, from: Data(json.utf8)), includeTurns: true))
        #expect(conversation.files?.first?.turnID == "r1")
        #expect(conversation.files?.first?.localPath == "/tmp/work/result.swift")
        #expect(conversation.turns.first?.assistant?.text == "已完成")
    }
}
