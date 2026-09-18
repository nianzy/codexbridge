import CryptoKit
import Foundation

enum CaptureValidationError: LocalizedError, Equatable {
    case unsupportedVersion(Int)
    case unsupportedSource(String)
    case invalidHost
    case emptyConversation
    case oversized(Int)
    case turnOrder
    case duplicateMessageID(String)
    case invalidSelection
    case inconsistentTurn(String)

    var errorDescription: String? {
        switch self {
        case .unsupportedVersion: "这个对话文件由其他版本生成，请更新 Codex Bridge 后重试。"
        case .unsupportedSource, .invalidHost: "只支持导入由 Codex Bridge 浏览器扩展保存的 ChatGPT 对话。"
        case .emptyConversation: "这个文件中没有可导入的对话内容。"
        case .oversized: "对话内容超过 2 MB。请减少选择的内容后重试。"
        case .turnOrder, .duplicateMessageID, .invalidSelection, .inconsistentTurn:
            "对话文件内容不完整，请回到 ChatGPT 网页重新选择并保存。"
        }
    }
}

struct CaptureValidator: Sendable {
    static let maximumPayloadBytes = 2 * 1_024 * 1_024

    func validate(data: Data) throws -> CapturedConversation {
        guard data.count <= Self.maximumPayloadBytes else {
            throw CaptureValidationError.oversized(data.count)
        }

        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .iso8601
        let payload = try decoder.decode(CapturePayload.self, from: data)
        return try validate(payload)
    }

    func validate(_ payload: CapturePayload) throws -> CapturedConversation {
        guard payload.schemaVersion == 1 else {
            throw CaptureValidationError.unsupportedVersion(payload.schemaVersion)
        }
        guard payload.source.kind == ConversationSourceKind.chatGPTWeb.rawValue else {
            throw CaptureValidationError.unsupportedSource(payload.source.kind)
        }
        guard payload.source.url.host?.lowercased() == "chatgpt.com" else {
            throw CaptureValidationError.invalidHost
        }
        guard !payload.turns.isEmpty else {
            throw CaptureValidationError.emptyConversation
        }
        guard payload.turns.map(\.index) == payload.turns.map(\.index).sorted(),
              Set(payload.turns.map(\.index)).count == payload.turns.count else {
            throw CaptureValidationError.turnOrder
        }

        var messageIDs = Set<String>()
        for turn in payload.turns {
            for message in [turn.user, turn.assistant].compactMap({ $0 }) {
                guard messageIDs.insert(message.id).inserted else {
                    throw CaptureValidationError.duplicateMessageID(message.id)
                }
            }
            guard !turn.user.text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
                throw CaptureValidationError.inconsistentTurn(turn.id)
            }
            if turn.complete, turn.assistant == nil {
                throw CaptureValidationError.inconsistentTurn(turn.id)
            }
        }

        let availableIDs = payload.turns.map(\.id)
        guard payload.selection.selectedTurnIds == availableIDs else {
            throw CaptureValidationError.invalidSelection
        }

        let encoder = JSONEncoder()
        encoder.outputFormatting = [.sortedKeys, .withoutEscapingSlashes]
        let canonicalData = try encoder.encode(payload.turns)
        let hash = SHA256.hash(data: canonicalData).map { String(format: "%02x", $0) }.joined()

        var warnings = payload.warnings
        if payload.capture.attachmentCount > 0 {
            warnings.append("检测到 \(payload.capture.attachmentCount) 个附件。附件不会随对话一起保存，请在需要时从原会话添加。")
        }
        if !payload.capture.complete {
            warnings.append("部分较早的消息可能未包含。请在 ChatGPT 中向上滚动后重新保存。")
        }

        let sourceConversationID = payload.source.conversationId?
            .trimmingCharacters(in: .whitespacesAndNewlines)
            .nilIfEmpty
        let identitySeed = sourceConversationID.map { "chatgpt-web-\($0)" }
            ?? "chatgpt-web-content-\(hash)"

        return CapturedConversation(
            id: CapturedConversation.stableID(for: identitySeed),
            sourceKind: .chatGPTWeb,
            sourceURL: payload.source.url,
            sourceConversationID: sourceConversationID,
            title: payload.source.title,
            projectName: nil,
            projectPath: nil,
            capturedAt: payload.capture.capturedAt,
            captureScope: payload.capture.scope,
            freshness: .captured,
            contentHash: hash,
            turns: payload.turns,
            warnings: warnings,
            codexThreadID: nil
        )
    }
}

private extension String {
    var nilIfEmpty: String? { isEmpty ? nil : self }
}
