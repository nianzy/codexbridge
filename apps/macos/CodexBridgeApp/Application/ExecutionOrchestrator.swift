import Foundation

actor ExecutionOrchestrator {
    private let repository: any ConversationRepository
    private let codexClient: any CodexClient
    private var inFlight: Set<String> = []
    private let serializer: ReferenceSerializer

    init(
        repository: any ConversationRepository,
        codexClient: any CodexClient,
        serializer: ReferenceSerializer = ReferenceSerializer()
    ) {
        self.repository = repository
        self.codexClient = codexClient
        self.serializer = serializer
    }

    /// 记录已经在 Codex 新任务编辑器中准备好的草稿。此时用户尚未发送，因此还没有 thread ID。
    func recordComposerDraft(conversation: CapturedConversation, draft: HandoffDraft) async throws -> ExecutionOperation {
        let frozen = try serializer.freeze(conversation: conversation, draft: draft)
        let existing = try await repository.operation(for: frozen.fingerprint)
        let operation = ExecutionOperation(
            id: existing?.id ?? frozen.operationID,
            draftID: frozen.draftID,
            draftRevision: frozen.draftRevision,
            fingerprint: frozen.fingerprint,
            state: .draft,
            threadID: nil,
            turnID: nil,
            errorCode: nil,
            updatedAt: .now
        )
        try await repository.saveOperation(operation)
        return operation
    }

    func prepareDraft(conversation: CapturedConversation, draft: HandoffDraft) async throws -> ExecutionOperation {
        let frozen = try serializer.freeze(conversation: conversation, draft: draft)
        guard inFlight.insert(frozen.fingerprint).inserted else {
            throw CodexAppServerError.invalidResponse("这次转交正在提交，请稍候。")
        }
        defer { inFlight.remove(frozen.fingerprint) }
        let existing = try await repository.operation(for: frozen.fingerprint)
        if let existing,
           existing.state != .failed || existing.threadID != nil {
            return existing
        }

        var operation = ExecutionOperation(
            id: existing?.id ?? frozen.operationID,
            draftID: frozen.draftID,
            draftRevision: frozen.draftRevision,
            fingerprint: frozen.fingerprint,
            state: .submitting,
            threadID: nil,
            turnID: nil,
            errorCode: nil,
            updatedAt: .now
        )
        try await repository.saveOperation(operation)

        do {
            let threadID = try await codexClient.createDraftThread(frozen)
            operation.state = .draft
            operation.threadID = threadID
            operation.turnID = nil
            operation.updatedAt = .now
            try await repository.saveOperation(operation)
            try await repository.saveLink(
                SourceTaskLink(
                    id: UUID(),
                    sourceConversationID: conversation.id,
                    selectedTurnIDs: draft.selectedTurnIDs,
                    threadID: threadID,
                    createdAt: .now
                )
            )
            return operation
        } catch let error as CodexAppServerError {
            switch error {
            case let .partialSubmission(threadID, _):
                operation.threadID = threadID
                operation.state = .uncertain
            case .timeout, .disconnected:
                operation.state = .uncertain
            default:
                operation.state = .failed
            }
            operation.errorCode = String(describing: error)
            operation.updatedAt = .now
            try? await repository.saveOperation(operation)
            throw error
        } catch {
            operation.state = .failed
            operation.errorCode = String(describing: error)
            operation.updatedAt = .now
            try? await repository.saveOperation(operation)
            throw error
        }
    }
}
