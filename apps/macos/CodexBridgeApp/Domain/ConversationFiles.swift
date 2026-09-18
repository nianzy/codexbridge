import Foundation

/// 对话中明确提到的文件。来源地址与用户选择的本机副本分开保存。
struct ConversationFile: Identifiable, Codable, Hashable, Sendable {
    let id: String
    let name: String
    let turnID: String
    let source: String
    var localPath: String?

    var localURL: URL? { localPath.map { URL(fileURLWithPath: $0) } }
}

struct TransferAttachment: Codable, Hashable, Sendable {
    let id: String
    let name: String
    let text: String
}

struct ConversationFileDiscovery: Sendable {
    func files(in conversation: CapturedConversation) -> [ConversationFile] {
        var result = conversation.files ?? []
        // 只提取 Markdown 链接；不扫描工作目录，也不猜测正文中的文件名。
        let pattern = #"\[([^\]]+)\]\(([^\s]+?)(?:\s+\"[^\"]*\")?\)"#
        guard let regex = try? NSRegularExpression(pattern: pattern) else { return result }
        for turn in conversation.turns {
            let text = turn.assistant?.text ?? ""
            let ns = text as NSString
            for match in regex.matches(in: text, range: NSRange(location: 0, length: ns.length)) {
                let source = ns.substring(with: match.range(at: 2)).trimmingCharacters(in: CharacterSet(charactersIn: "<>"))
                let local: String?
                if source.hasPrefix("file://"), let url = URL(string: source), url.host == nil || url.host == "" || url.host == "localhost" {
                    local = url.path
                } else if source.hasPrefix("/") {
                    local = source.removingPercentEncoding ?? source
                } else if !source.contains(":"), !source.hasPrefix("#"), let root = conversation.projectPath {
                    local = URL(fileURLWithPath: root).appendingPathComponent(source.removingPercentEncoding ?? source).standardized.path
                } else { local = nil }
                guard local != nil || source.hasPrefix("sandbox:") else { continue }
                let name = URL(fileURLWithPath: local ?? source).lastPathComponent
                guard !name.isEmpty else { continue }
                if result.contains(where: { $0.turnID == turn.id && $0.source == source }) { continue }
                result.append(.init(id: "\(turn.id):\(source)", name: name, turnID: turn.id, source: source, localPath: local))
            }
        }
        return result
    }
}

enum ConversationFileError: LocalizedError, Equatable {
    case unavailable, missing, unsupported, tooLarge
    var errorDescription: String? {
        switch self {
        case .unavailable: "无法读取这个文件。请先下载文件，或选择本机副本。"
        case .missing: "本机文件已被移动或删除。请重新选择文件。"
        case .unsupported: "此文件不支持作为文本附带。可在文件页预览，并在 ChatGPT 中手动添加原文件。"
        case .tooLarge: "文件超过 1 MB，请选取需要的内容后再转交。"
        }
    }
}

struct ConversationFileReader: Sendable {
    func availableURL(_ file: ConversationFile) throws -> URL {
        guard let url = file.localURL else { throw ConversationFileError.unavailable }
        let values = try? url.resourceValues(forKeys: [.isRegularFileKey])
        guard values?.isRegularFile == true else { throw ConversationFileError.missing }
        return url
    }

    func attachment(_ file: ConversationFile) throws -> TransferAttachment {
        let url = try availableURL(file)
        let values = try url.resourceValues(forKeys: [.isRegularFileKey, .fileSizeKey])
        guard values.isRegularFile == true else { throw ConversationFileError.unsupported }
        guard (values.fileSize ?? Int.max) <= 1_048_576 else { throw ConversationFileError.tooLarge }
        let handle = try FileHandle(forReadingFrom: url)
        defer { try? handle.close() }
        let data = try handle.read(upToCount: 1_048_577) ?? Data()
        guard data.count <= 1_048_576 else { throw ConversationFileError.tooLarge }
        guard let text = String(data: data, encoding: .utf8), !text.contains("\0") else { throw ConversationFileError.unsupported }
        return .init(id: file.id, name: file.name, text: text)
    }
}

struct SelectedConversationText {
    static func render(_ conversation: CapturedConversation, ids: Set<String>) -> String {
        conversation.turns.filter { ids.contains($0.id) }.map {
            "第 \($0.index + 1) 轮\n\n用户：\n\($0.user.text)\n\n\(conversation.sourceKind == .codex ? "Codex" : "ChatGPT")：\n\($0.assistant?.text ?? "（尚无回复）")"
        }.joined(separator: "\n\n---\n\n")
    }
}
