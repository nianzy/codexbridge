import Foundation

/// 只接受明确的角色标题；不把正文中的「你说…」猜成消息边界。
struct ChatGPTTranscriptParser: Sendable {
    func parse(_ fragments: [String]) -> [CapturedTurn] {
        enum Role { case user, assistant }
        var role: Role?
        var body: [String] = []
        var messages: [(Role, String)] = []
        func flush() {
            if let role {
                let text = body.joined(separator: "\n").trimmingCharacters(in: .whitespacesAndNewlines)
                if !text.isEmpty { messages.append((role, text)) }
            }
            body = []
        }
        let users = ["you said", "你说", "用户说", "user", "用户"]
        let assistants = ["chatgpt said", "chatgpt 说", "assistant said", "chatgpt", "assistant", "助手"]
        for fragment in fragments {
            let value = fragment.trimmingCharacters(in: .whitespacesAndNewlines)
            var next: Role?
            var remainder = ""
            for (markers, candidate) in [(users, Role.user), (assistants, Role.assistant)] {
                for marker in markers {
                    if value.lowercased() == marker { next = candidate }
                    for colon in [":", "："] where value.lowercased().hasPrefix(marker + colon) {
                        next = candidate
                        remainder = String(value.dropFirst(marker.count + 1)).trimmingCharacters(in: .whitespacesAndNewlines)
                    }
                }
            }
            if let next { flush(); role = next; if !remainder.isEmpty { body.append(remainder) } }
            else if role != nil, !value.isEmpty { body.append(value) }
        }
        flush()
        var turns: [CapturedTurn] = []
        var pending: String?
        func append(_ user: String, _ reply: String?) {
            let index = turns.count
            let id = CapturedConversation.stableID(for: "chatgpt-round:\(index):\(user)").uuidString
            turns.append(.init(id: id, index: index, user: .init(id: id + "-user", idSource: "accessibility", text: user),
                assistant: reply.map { .init(id: id + "-assistant", idSource: "accessibility", text: $0) }, complete: reply != nil))
        }
        for (role, text) in messages {
            switch role {
            case .user:
                if let pending { append(pending, nil) }
                pending = text
            case .assistant:
                guard let user = pending else { continue }
                append(user, text); pending = nil
            }
        }
        if let pending { append(pending, nil) }
        return turns
    }
}

enum ChatGPTComposerError: LocalizedError {
    case missing, occupied, writeFailed, emptyDraft, newConversationUnavailable
    var errorDescription: String? {
        switch self {
        case .missing: "没有找到可填写的位置。请确认已经打开新的 ChatGPT 会话或 Codex 任务。"
        case .occupied: "输入框中已有内容。为避免覆盖，请先处理现有草稿。"
        case .writeFailed: "无法确认内容已经完整填入。Codex Bridge 没有发送任何内容。"
        case .newConversationUnavailable: "无法确认新会话或新任务已经打开，内容尚未填入。"
        case .emptyDraft: "草稿内容为空。"
        }
    }
}
