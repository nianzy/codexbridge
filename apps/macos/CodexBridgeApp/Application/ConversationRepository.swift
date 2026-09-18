import Foundation

protocol ConversationRepository: Sendable {
    func prepare() async throws
    func listConversations() async throws -> [CapturedConversation]
    func saveConversation(_ conversation: CapturedConversation) async throws
    func removeConversation(id: UUID) async throws
    func loadDraft(for sourceConversationID: UUID) async throws -> HandoffDraft?
    func saveDraft(_ draft: HandoffDraft) async throws
    func operation(for fingerprint: String) async throws -> ExecutionOperation?
    func saveOperation(_ operation: ExecutionOperation) async throws
    func listOperations() async throws -> [ExecutionOperation]
    func saveLink(_ link: SourceTaskLink) async throws
    func listLinks() async throws -> [SourceTaskLink]
    func saveChatGPTDraft(_ draft: ChatGPTDraft) async throws
    func loadChatGPTDraft(for sourceConversationID: UUID) async throws -> ChatGPTDraft?
    func listChatGPTDrafts() async throws -> [ChatGPTDraft]
    func clearLocalData() async throws
}

protocol CodexClient: Sendable {
    func probe() async throws -> CodexProbe
    func listThreads(limit: Int) async throws -> [CapturedConversation]
    func recentThreadSnapshot(limit: Int) async throws -> CodexThreadSnapshot
    func createDraftThread(_ handoff: FrozenHandoff) async throws -> String
    func readThread(id: String) async throws -> CapturedConversation
    func continueThread(id: String, prompt: String, modelID: String?, reasoningEffort: String?) async throws -> CodexSubmission
    func forkThread(id: String) async throws -> CapturedConversation
    func interrupt(threadID: String, turnID: String) async throws
    func runtimeEvents() async -> AsyncStream<CodexRuntimeEvent>
    func respond(to requestID: String, accepted: Bool, answers: [String: [String]]) async throws
    func stop() async
}

private struct UnsupportedCodexClientCapability: LocalizedError {
    let message: String
    var errorDescription: String? { message }
}

extension CodexClient {
    func recentThreadSnapshot(limit: Int) async throws -> CodexThreadSnapshot {
        let threads = try await listThreads(limit: limit)
        return CodexThreadSnapshot(threads: threads, isComplete: threads.count < limit)
    }

    func readThread(id: String) async throws -> CapturedConversation {
        throw UnsupportedCodexClientCapability(message: "当前 Codex 客户端不支持读取任务")
    }

    func continueThread(id: String, prompt: String, modelID: String?, reasoningEffort: String?) async throws -> CodexSubmission {
        throw UnsupportedCodexClientCapability(message: "当前 Codex 客户端不支持继续任务")
    }

    func forkThread(id: String) async throws -> CapturedConversation {
        throw UnsupportedCodexClientCapability(message: "当前 Codex 版本不支持创建任务副本")
    }

    func interrupt(threadID: String, turnID: String) async throws {
        throw UnsupportedCodexClientCapability(message: "当前 Codex 客户端不支持停止任务")
    }

    func runtimeEvents() async -> AsyncStream<CodexRuntimeEvent> {
        AsyncStream { $0.finish() }
    }

    func respond(to requestID: String, accepted: Bool, answers: [String: [String]]) async throws {
        throw UnsupportedCodexClientCapability(message: "当前 Codex 客户端不支持交互响应")
    }
}

struct CodexThreadSnapshot: Sendable {
    let threads: [CapturedConversation]
    let isComplete: Bool
}

struct CodexProbe: Sendable {
    let accountLabel: String
    let version: String
    let models: [CodexModelOption]
}

struct CodexSubmission: Sendable {
    let threadID: String
    let turnID: String
}
