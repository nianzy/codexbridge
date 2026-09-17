import Foundation
import Testing
@testable import CodexBridge

struct ExecutionOrchestratorTests {
    @Test func recordsComposerDraftWithoutCreatingBackgroundThread() async throws {
        let repository = MemoryRepository()
        let client = RecordingCodexClient()
        let orchestrator = ExecutionOrchestrator(repository: repository, codexClient: client)
        let conversation = CapturedConversation.syntheticSamples().first!
        var draft = HandoffDraft.empty(for: conversation)
        draft.instruction = "准备草稿"
        draft.workspacePath = "/tmp/codexbridge-test"
        draft.modelID = "test-model"
        draft.reasoningEffort = "medium"

        let operation = try await orchestrator.recordComposerDraft(conversation: conversation, draft: draft)

        #expect(operation.state == .draft)
        #expect(operation.threadID == nil)
        #expect(await client.submissionCount == 0)
        #expect(try await repository.listOperations().count == 1)
    }

    @Test func identicalRevisionIsSubmittedOnlyOnce() async throws {
        let repository = MemoryRepository()
        let client = RecordingCodexClient()
        let orchestrator = ExecutionOrchestrator(repository: repository, codexClient: client)
        let conversation = CapturedConversation.syntheticSamples().first!
        var draft = HandoffDraft.empty(for: conversation)
        draft.instruction = "实现任务"
        draft.workspacePath = "/tmp/codexbridge-test"
        draft.modelID = "test-model"
        draft.reasoningEffort = "medium"

        let first = try await orchestrator.prepareDraft(conversation: conversation, draft: draft)
        let second = try await orchestrator.prepareDraft(conversation: conversation, draft: draft)

        #expect(first.id == second.id)
        #expect(await client.submissionCount == 1)
        #expect(await repository.links.count == 1)
    }
}

private actor RecordingCodexClient: CodexClient {
    var submissionCount = 0

    func probe() async throws -> CodexProbe {
        CodexProbe(accountLabel: "测试", version: "测试", models: [])
    }

    func listThreads(limit: Int) async throws -> [CapturedConversation] { [] }

    func createDraftThread(_ handoff: FrozenHandoff) async throws -> String {
        submissionCount += 1
        return "thread-1"
    }

    func stop() async {}
}

private actor MemoryRepository: ConversationRepository {
    var conversations: [CapturedConversation] = []
    var drafts: [UUID: HandoffDraft] = [:]
    var operations: [String: ExecutionOperation] = [:]
    var links: [SourceTaskLink] = []

    func prepare() async throws {}
    func listConversations() async throws -> [CapturedConversation] { conversations }
    func saveConversation(_ conversation: CapturedConversation) async throws { conversations.append(conversation) }
    func removeConversation(id: UUID) async throws { conversations.removeAll { $0.id == id } }
    func loadDraft(for sourceConversationID: UUID) async throws -> HandoffDraft? { drafts[sourceConversationID] }
    func saveDraft(_ draft: HandoffDraft) async throws { drafts[draft.sourceConversationID] = draft }
    func operation(for fingerprint: String) async throws -> ExecutionOperation? { operations[fingerprint] }
    func saveOperation(_ operation: ExecutionOperation) async throws { operations[operation.fingerprint] = operation }
    func listOperations() async throws -> [ExecutionOperation] { Array(operations.values) }
    func saveLink(_ link: SourceTaskLink) async throws { links.append(link) }
    func listLinks() async throws -> [SourceTaskLink] { links }
    func saveChatGPTDraft(_ draft: ChatGPTDraft) async throws {}
    func loadChatGPTDraft(for sourceConversationID: UUID) async throws -> ChatGPTDraft? { nil }
    func listChatGPTDrafts() async throws -> [ChatGPTDraft] { [] }
    func clearLocalData() async throws {}
}

struct CodexArchiveSynchronizationTests {
    @Test @MainActor func refreshRemovesMissingCodexThreadAndKeepsChatConversation() async throws {
        let repository = MemoryRepository()
        var codexConversation = CapturedConversation.syntheticSamples()[1]
        codexConversation.codexThreadID = "archived-thread"
        let chatConversation = CapturedConversation.syntheticSamples()[0]
        try await repository.saveConversation(codexConversation)
        try await repository.saveConversation(chatConversation)

        let model = AppModel(repository: repository, codexClient: EmptyThreadListClient())
        model.conversations = [codexConversation, chatConversation]
        model.selectedConversationID = codexConversation.id

        await model.refreshCodexConnection()

        #expect(model.conversations.map(\.id) == [chatConversation.id])
        #expect(model.selectedConversationID == chatConversation.id)
        #expect(try await repository.listConversations().map(\.id) == [chatConversation.id])
    }

    @Test @MainActor func automaticRefreshFindsNewThreadAndRemovesItAfterExternalArchive() async throws {
        let repository = MemoryRepository()
        let client = MutableThreadListClient()
        let model = AppModel(repository: repository, codexClient: client)
        await model.bootstrap()

        var newConversation = CapturedConversation.syntheticSamples()[1]
        newConversation.codexThreadID = "external-thread"
        await client.setThreads([newConversation])

        await model.refreshAutomaticallyIfReady()

        #expect(model.conversations.map(\.id) == [newConversation.id])
        #expect(model.statusMessage == nil)

        await client.setThreads([])
        await model.refreshAutomaticallyIfReady()

        #expect(model.conversations.isEmpty)
        #expect(try await repository.listConversations().isEmpty)
    }
}

struct AppUpdateCheckerTests {
    @Test func selectsNewestPublishedReleaseAboveCurrentVersion() throws {
        let data = Data(
            """
            [
              {"tag_name":"v1.1.1-beta.1","name":"Current","html_url":"https://example.com/current","draft":false,"published_at":"2026-09-18T00:00:00Z"},
              {"tag_name":"v1.2.0-beta.1","name":"Codex Bridge 1.2 Beta","html_url":"https://example.com/new","draft":false,"published_at":"2026-09-19T00:00:00Z"},
              {"tag_name":"v2.0.0","name":"Draft","html_url":"https://example.com/draft","draft":true,"published_at":"2026-09-20T00:00:00Z"}
            ]
            """.utf8
        )

        let release = try GitHubReleaseUpdateChecker.newerRelease(in: data, than: "1.1.1")

        #expect(release?.version == "1.2.0")
        #expect(release?.title == "Codex Bridge 1.2 Beta")
        #expect(release?.pageURL.absoluteString == "https://example.com/new")
    }

    @Test func ignoresReleaseWithSameNumericVersion() throws {
        let data = Data(
            """
            [{"tag_name":"v1.1.1-beta.1","name":"Same","html_url":"https://example.com/same","draft":false,"published_at":"2026-09-18T00:00:00Z"}]
            """.utf8
        )

        #expect(try GitHubReleaseUpdateChecker.newerRelease(in: data, than: "1.1.1") == nil)
    }
}

private actor EmptyThreadListClient: CodexClient {
    func probe() async throws -> CodexProbe { .init(accountLabel: "测试", version: "测试", models: []) }
    func listThreads(limit: Int) async throws -> [CapturedConversation] { [] }
    func createDraftThread(_ handoff: FrozenHandoff) async throws -> String { "unused" }
    func stop() async {}
}

private actor MutableThreadListClient: CodexClient {
    private var threads: [CapturedConversation] = []

    func setThreads(_ threads: [CapturedConversation]) {
        self.threads = threads
    }

    func probe() async throws -> CodexProbe { .init(accountLabel: "测试", version: "测试", models: []) }
    func listThreads(limit: Int) async throws -> [CapturedConversation] { Array(threads.prefix(limit)) }
    func createDraftThread(_ handoff: FrozenHandoff) async throws -> String { "unused" }
    func stop() async {}
}

struct TransferModelTests {
    @Test @MainActor func selectionResetsAndDraftContainsOnlySelectedRounds() async throws {
        let model = AppModel(repository: MemoryRepository(), codexClient: RecordingCodexClient())
        let samples = CapturedConversation.syntheticSamples()
        model.conversations = samples
        model.selectedConversationID = samples[0].id
        #expect(model.selectedTurnIDs.isEmpty)
        model.toggleTurn(samples[0].turns[1].id)
        await model.beginHandoff()
        #expect(model.activeDraft?.selectedTurnIDs == [samples[0].turns[1].id])
        #expect(model.transferAttachments.isEmpty)
        model.selectedConversationID = samples[1].id
        #expect(model.selectedTurnIDs.isEmpty)
        #expect(model.transferConversation?.id == samples[0].id)
    }

    @Test @MainActor func rangeAndQuickSelectionChooseExpectedRounds() {
        let model = AppModel(repository: MemoryRepository(), codexClient: RecordingCodexClient())
        var conversation = CapturedConversation.syntheticSamples()[0]
        let originalTurns = conversation.turns
        conversation.turns = [
            originalTurns[0],
            CapturedTurn(
                id: originalTurns[1].id,
                index: originalTurns[1].index,
                user: originalTurns[1].user,
                assistant: originalTurns[1].assistant,
                complete: false
            ),
        ]
        model.conversations = [conversation]
        model.selectedConversationID = conversation.id

        model.selectTurnRange(from: conversation.turns[0].id, through: conversation.turns[1].id)
        #expect(model.selectedTurnIDs == Set(conversation.turns.map(\.id)))

        model.selectRecentTurns(1)
        #expect(model.selectedTurnIDs == [conversation.turns[1].id])

        model.selectCompleteTurns()
        #expect(model.selectedTurnIDs == [conversation.turns[0].id])

        model.clearTurnSelection()
        #expect(model.selectedTurnIDs.isEmpty)
    }

    @Test func partialSubmissionPersistsThreadAndIsNotRetried() async throws {
        let repository = MemoryRepository()
        let client = PartialFailureClient()
        let orchestrator = ExecutionOrchestrator(repository: repository, codexClient: client)
        let conversation = CapturedConversation.syntheticSamples()[0]
        var draft = HandoffDraft.empty(for: conversation)
        draft.instruction = "实现"
        draft.workspacePath = "/tmp/test"
        draft.modelID = "test-model"
        draft.reasoningEffort = "medium"
        do { _ = try await orchestrator.prepareDraft(conversation: conversation, draft: draft); Issue.record("应报告创建结果未确认") } catch { }
        draft.revision += 1
        let existing = try await orchestrator.prepareDraft(conversation: conversation, draft: draft)
        #expect(existing.state == .uncertain)
        #expect(existing.threadID == nil)
        #expect(await client.count == 1)
    }
}

private actor PartialFailureClient: CodexClient {
    var count = 0
    func probe() async throws -> CodexProbe { .init(accountLabel: "test", version: "test", models: []) }
    func listThreads(limit: Int) async throws -> [CapturedConversation] { [] }
    func createDraftThread(_ handoff: FrozenHandoff) async throws -> String {
        count += 1
        throw CodexAppServerError.timeout("thread/start")
    }
    func stop() async {}
}

struct ConversationLoadingTests {
    private func fixture(turns: [CapturedTurn] = []) -> CapturedConversation {
        let source = CapturedConversation.syntheticSamples()[1]
        return CapturedConversation(id: source.id, sourceKind: .codex, sourceURL: nil,
            sourceConversationID: "loading-test", title: "加载测试", projectName: nil, projectPath: nil,
            capturedAt: .now, captureScope: "app-server-thread-read", freshness: .live,
            contentHash: "loading-test", turns: turns, warnings: [], codexThreadID: "loading-test")
    }

    @Test @MainActor func overlappingReadsShareOneRequestAndEmptyResultIsCached() async {
        let detail = fixture()
        var summary = CapturedConversation.syntheticSamples()[1]
        summary.codexThreadID = "loading-test"
        let client = DelayedLoadingClient(detail: detail)
        let model = AppModel(repository: MemoryRepository(), codexClient: client)
        model.conversations = [summary]
        model.selectedConversationID = summary.id
        async let first: Void = model.loadSelectedConversationDetails()
        async let second: Void = model.loadSelectedConversationDetails()
        _ = await (first, second)
        #expect(await client.readCount == 1)
        #expect(model.selectedConversation?.captureScope == "app-server-thread-read")
        await model.loadSelectedConversationDetails()
        #expect(await client.readCount == 1)
        #expect(model.loadingConversationID == nil)
    }

    @Test @MainActor func cachedDetailNeedsNoRequestAndForcedRefreshStillWorks() async {
        let detail = fixture(turns: CapturedConversation.syntheticSamples()[0].turns)
        let client = DelayedLoadingClient(detail: detail)
        let model = AppModel(repository: MemoryRepository(), codexClient: client)
        model.conversations = [detail]
        model.selectedConversationID = detail.id
        let clock = ContinuousClock()
        let start = clock.now
        for _ in 0..<100 { await model.loadSelectedConversationDetails() }
        print("Codex Bridge cached reads x100: \(start.duration(to: clock.now)); requests: \(await client.readCount)")
        #expect(await client.readCount == 0)
        await model.loadSelectedConversationDetails(force: true)
        #expect(await client.readCount == 1)
        #expect(model.selectedConversation?.turns == detail.turns)
    }

    @Test @MainActor func selectionChangeDoesNotShowPreviousRequestError() async {
        let detail = fixture()
        var summary = CapturedConversation.syntheticSamples()[1]
        summary.codexThreadID = "loading-test"
        let model = AppModel(repository: MemoryRepository(), codexClient: DelayedLoadingClient(detail: detail))
        model.conversations = [summary, CapturedConversation.syntheticSamples()[0]]
        model.selectedConversationID = summary.id
        let load = Task { await model.loadSelectedConversationDetails() }
        await Task.yield()
        let chatID = model.conversations[1].id
        model.selectedConversationID = chatID
        await load.value
        #expect(model.selectedConversationID == chatID)
        #expect(model.selectedConversation?.sourceKind != .codex)
    }
}

struct ConversationProjectGroupingTests {
    @Test func groupsByProjectAndKeepsUngroupedAtTheEnd() {
        let base = CapturedConversation.syntheticSamples()[0]
        let now = Date(timeIntervalSince1970: 10_000)
        let older = CapturedConversation(
            id: UUID(), sourceKind: .chatGPTApp, sourceURL: nil, sourceConversationID: "a",
            title: "设计讨论", projectName: "Codex Bridge", projectPath: nil,
            capturedAt: now.addingTimeInterval(-300), captureScope: "test", freshness: .captured,
            contentHash: "a", turns: base.turns, warnings: [], codexThreadID: nil
        )
        let newer = CapturedConversation(
            id: UUID(), sourceKind: .codex, sourceURL: nil, sourceConversationID: "b",
            title: "实现任务", projectName: "Codex Bridge", projectPath: "/tmp/Codex Bridge",
            capturedAt: now, captureScope: "test", freshness: .live,
            contentHash: "b", turns: [], warnings: [], codexThreadID: "b"
        )
        let ungrouped = CapturedConversation(
            id: UUID(), sourceKind: .chatGPTApp, sourceURL: nil, sourceConversationID: "c",
            title: "临时对话", projectName: nil, projectPath: nil,
            capturedAt: now.addingTimeInterval(100), captureScope: "test", freshness: .captured,
            contentHash: "c", turns: [], warnings: [], codexThreadID: nil
        )

        let groups = ConversationProjectGroup.grouped([ungrouped, older, newer])

        #expect(groups.map(\.name) == ["Codex Bridge", "未归类"])
        #expect(groups[0].conversations.map(\.id) == [newer.id, older.id])
        #expect(groups[0].path == "/tmp/Codex Bridge")
        #expect(groups[1].conversations.map(\.id) == [ungrouped.id])
    }

    @Test func usesProjectDirectoryNameWhenOnlyAPathIsAvailable() {
        var conversation = CapturedConversation.syntheticSamples()[1]
        conversation.projectName = nil
        conversation.projectPath = "/Users/test/Workspace/TopicMining"

        let groups = ConversationProjectGroup.grouped([conversation])

        #expect(groups.count == 1)
        #expect(groups[0].name == "TopicMining")
    }
}

private actor DelayedLoadingClient: CodexClient {
    let detail: CapturedConversation
    var readCount = 0
    init(detail: CapturedConversation) { self.detail = detail }
    func probe() async throws -> CodexProbe { .init(accountLabel: "test", version: "test", models: []) }
    func listThreads(limit: Int) async throws -> [CapturedConversation] { [] }
    func readThread(id: String) async throws -> CapturedConversation {
        readCount += 1
        try await Task.sleep(for: .milliseconds(40))
        return detail
    }
    func createDraftThread(_ handoff: FrozenHandoff) async throws -> String { "test" }
    func stop() async {}
}

struct SubmissionRetryTests {
    @Test func rejectedCreationCanRetryWithoutDuplicatingTheOperation() async throws {
        let repository = MemoryRepository()
        let client = RejectOnceClient()
        let orchestrator = ExecutionOrchestrator(repository: repository, codexClient: client)
        let conversation = CapturedConversation.syntheticSamples()[0]
        var draft = HandoffDraft.empty(for: conversation)
        draft.instruction = "测试转交"
        draft.workspacePath = "/tmp/codexbridge-test"
        draft.modelID = "test-model"
        draft.reasoningEffort = "medium"
        do { _ = try await orchestrator.prepareDraft(conversation: conversation, draft: draft); Issue.record("第一次应该失败") } catch { }
        let failed = try await repository.listOperations().first!
        #expect(failed.state == .failed)
        let retried = try await orchestrator.prepareDraft(conversation: conversation, draft: draft)
        #expect(retried.id == failed.id)
        #expect(retried.state == .draft)
        #expect(await client.calls == 2)
        #expect(try await repository.listOperations().count == 1)
    }
}

private actor RejectOnceClient: CodexClient {
    var calls = 0
    func probe() async throws -> CodexProbe { CodexProbe(accountLabel: "测试", version: "测试", models: []) }
    func listThreads(limit: Int) async throws -> [CapturedConversation] { [] }
    func stop() async {}
    func createDraftThread(_ handoff: FrozenHandoff) async throws -> String {
        calls += 1
        if calls == 1 { throw CodexAppServerError.invalidResponse("请求被拒绝") }
        return "new-thread"
    }
}
