import CryptoKit
import Foundation

struct FrozenHandoff: Equatable, Sendable {
    let operationID: UUID
    let draftID: UUID
    let draftRevision: Int
    let bytes: Data
    let fingerprint: String
    let workspacePath: String
    let modelID: String
    let reasoningEffort: String
}

enum ReferenceSerializationError: LocalizedError, Equatable {
    case emptyInstruction
    case missingWorkspace
    case missingModel
    case missingReasoningEffort
    case noSelectedTurns

    var errorDescription: String? {
        switch self {
        case .emptyInstruction: "请填写明确的执行指令。"
        case .missingWorkspace: "请选择 Codex 工作目录。"
        case .missingModel: "请选择当前可用的 Codex 模型。"
        case .missingReasoningEffort: "请选择该模型支持的思考强度。"
        case .noSelectedTurns: "至少选择一轮讨论参考。"
        }
    }
}

struct ReferenceSerializer: Sendable {
    func freeze(conversation: CapturedConversation, draft: HandoffDraft, operationID: UUID = UUID()) throws -> FrozenHandoff {
        let instruction = draft.instruction.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !instruction.isEmpty else { throw ReferenceSerializationError.emptyInstruction }
        guard let workspacePath = draft.workspacePath, !workspacePath.isEmpty else { throw ReferenceSerializationError.missingWorkspace }
        guard let modelID = draft.modelID, !modelID.isEmpty else { throw ReferenceSerializationError.missingModel }
        guard let reasoningEffort = draft.reasoningEffort, !reasoningEffort.isEmpty else { throw ReferenceSerializationError.missingReasoningEffort }

        let selected = conversation.turns.filter { draft.selectedTurnIDs.contains($0.id) }
        guard !selected.isEmpty else { throw ReferenceSerializationError.noSelectedTurns }

        var lines = [
            "执行要求",
            instruction,
            "",
            "对话参考",
            "以下是从 \(sourceName(conversation.sourceKind)) 选择的 \(selected.count) 轮对话，仅作为任务背景。",
        ]

        for turn in selected {
            lines.append("")
            lines.append("第 \(turn.index + 1) 轮")
            lines.append("用户：")
            lines.append(turn.user.text)
            if let assistant = turn.assistant {
                lines.append("")
                lines.append("助手：")
                lines.append(assistant.text)
            } else {
                lines.append("")
                lines.append("助手：暂无回复")
            }
        }

        for attachment in draft.attachments ?? [] {
            lines.append("")
            lines.append("附带文件：\(attachment.name)")
            lines.append(attachment.text)
        }
        let bytes = Data(lines.joined(separator: "\n").utf8)
        let fingerprintInput = Data("\(draft.id.uuidString):\(workspacePath):\(modelID):\(reasoningEffort):".utf8) + bytes
        let fingerprint = SHA256.hash(data: fingerprintInput).map { String(format: "%02x", $0) }.joined()

        return FrozenHandoff(
            operationID: operationID,
            draftID: draft.id,
            draftRevision: draft.revision,
            bytes: bytes,
            fingerprint: fingerprint,
            workspacePath: workspacePath,
            modelID: modelID,
            reasoningEffort: reasoningEffort
        )
    }

    private func sourceName(_ source: ConversationSourceKind) -> String {
        switch source {
        case .chatGPTWeb: "ChatGPT 网页版"
        case .chatGPTApp: "ChatGPT App"
        case .codex: "Codex"
        }
    }
}
