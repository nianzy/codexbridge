import CryptoKit
import Foundation

enum ConversationSourceKind: String, Codable, CaseIterable, Sendable {
    case chatGPTWeb = "chatgpt-web"
    case chatGPTApp = "chatgpt-app"
    case codex

    var displayName: String {
        switch self {
        case .chatGPTWeb: "ChatGPT 网页版"
        case .chatGPTApp: "ChatGPT App"
        case .codex: "Codex"
        }
    }

    var symbolName: String {
        switch self {
        case .chatGPTWeb: "globe"
        case .chatGPTApp: "bubble.left.and.bubble.right"
        case .codex: "terminal"
        }
    }
}

enum ConversationFreshness: String, Codable, Sendable {
    case live
    case captured
    case possiblyStale
    case offline

    var displayName: String {
        switch self {
        case .live: "实时"
        case .captured: "已保存"
        case .possiblyStale: "可能不是最新"
        case .offline: "本机副本"
        }
    }
}

struct CapturedMessage: Codable, Hashable, Sendable {
    let id: String
    let idSource: String
    let text: String
}

struct CapturedTurn: Identifiable, Codable, Hashable, Sendable {
    let id: String
    let index: Int
    let user: CapturedMessage
    let assistant: CapturedMessage?
    let complete: Bool

    var characterCount: Int {
        user.text.count + (assistant?.text.count ?? 0)
    }
}

struct CapturedConversation: Identifiable, Codable, Hashable, Sendable {
    let id: UUID
    let sourceKind: ConversationSourceKind
    let sourceURL: URL?
    let sourceConversationID: String?
    var title: String
    var projectName: String?
    var projectPath: String?
    let capturedAt: Date
    let captureScope: String
    let freshness: ConversationFreshness
    let contentHash: String
    var turns: [CapturedTurn]
    var warnings: [String]
    var codexThreadID: String?
    var runtimeStatus: ConversationRuntimeStatus? = nil
    var activeTurnID: String? = nil
    var updatedAt: Date? = nil
    var modelID: String? = nil
    var files: [ConversationFile]? = nil

    var subtitle: String {
        let project = projectName ?? "未归类"
        return "\(sourceKind.displayName) · \(project) · \(turns.count) 轮"
    }

    static func syntheticSamples(now: Date = .now) -> [CapturedConversation] {
        let discussionTurns = [
            CapturedTurn(
                id: "sample-turn-1",
                index: 0,
                user: .init(id: "sample-u-1", idSource: "synthetic", text: "Codex Bridge 如何在 ChatGPT 和 Codex 之间保持上下文？"),
                assistant: .init(id: "sample-a-1", idSource: "synthetic", text: "交接前可以预览并确认内容，同时保留来源关系。"),
                complete: true
            ),
            CapturedTurn(
                id: "sample-turn-2",
                index: 1,
                user: .init(id: "sample-u-2", idSource: "synthetic", text: "交接时只保留我选中的对话轮次。"),
                assistant: .init(id: "sample-a-2", idSource: "synthetic", text: "可以在交接前快速多选轮次，并预览最终内容。"),
                complete: true
            ),
        ]

        return [
            CapturedConversation(
                id: UUID(uuidString: "9E451E01-941F-4C0B-B1BD-1ED2D70424A1")!,
                sourceKind: .chatGPTApp,
                sourceURL: nil,
                sourceConversationID: "synthetic-chatgpt-app",
                title: "macOS App 与 Codex 协作方案",
                projectName: "codexbridge",
                projectPath: nil,
                capturedAt: now,
                captureScope: "foreground-window-only",
                freshness: .captured,
                contentHash: "synthetic-chatgpt-app-v1",
                turns: discussionTurns,
                warnings: ["示例会话，仅用于展示界面。"],
                codexThreadID: nil
            ),
            CapturedConversation(
                id: UUID(uuidString: "32ED62CC-7961-4A4E-9E0C-9D382A0B4F22")!,
                sourceKind: .codex,
                sourceURL: nil,
                sourceConversationID: "synthetic-codex-thread",
                title: "Codex Bridge UI 原型",
                projectName: "codexbridge",
                projectPath: nil,
                capturedAt: now.addingTimeInterval(-420),
                captureScope: "app-server",
                freshness: .live,
                contentHash: "synthetic-codex-v1",
                turns: [],
                warnings: [],
                codexThreadID: nil
            ),
        ]
    }

    static func stableID(for value: String) -> UUID {
        let bytes = Array(SHA256.hash(data: Data(value.utf8)).prefix(16))
        let tuple: uuid_t = (
            bytes[0], bytes[1], bytes[2], bytes[3], bytes[4], bytes[5], bytes[6], bytes[7],
            bytes[8], bytes[9], bytes[10], bytes[11], bytes[12], bytes[13], bytes[14], bytes[15]
        )
        return UUID(uuid: tuple)
    }
}

struct ConversationProjectGroup: Identifiable, Hashable, Sendable {
    let id: String
    let name: String
    let path: String?
    let conversations: [CapturedConversation]

    var latestActivity: Date {
        conversations.map { $0.updatedAt ?? $0.capturedAt }.max() ?? .distantPast
    }

    static func grouped(_ conversations: [CapturedConversation]) -> [ConversationProjectGroup] {
        let unassignedName = "未归类"
        func projectName(for conversation: CapturedConversation) -> String {
            let explicitName = conversation.projectName?.trimmingCharacters(in: .whitespacesAndNewlines)
            if let explicitName, !explicitName.isEmpty { return explicitName }
            if let path = conversation.projectPath?.trimmingCharacters(in: .whitespacesAndNewlines), !path.isEmpty {
                return URL(fileURLWithPath: path).lastPathComponent
            }
            return unassignedName
        }
        let grouped = Dictionary(grouping: conversations) { projectName(for: $0).lowercased() }

        return grouped.map { key, conversations in
            let name = conversations.first.map(projectName(for:)) ?? unassignedName
            return ConversationProjectGroup(
                id: key,
                name: name,
                path: conversations.compactMap(\.projectPath).first,
                conversations: conversations.sorted {
                    ($0.updatedAt ?? $0.capturedAt) > ($1.updatedAt ?? $1.capturedAt)
                }
            )
        }
        .sorted { lhs, rhs in
            if lhs.name == unassignedName { return false }
            if rhs.name == unassignedName { return true }
            if lhs.latestActivity != rhs.latestActivity { return lhs.latestActivity > rhs.latestActivity }
            return lhs.name.localizedStandardCompare(rhs.name) == .orderedAscending
        }
    }
}

enum ConversationRuntimeStatus: String, Codable, Hashable, Sendable {
    case unavailable
    case idle
    case running
    case waitingForApproval
    case waitingForInput
    case completed
    case interrupted
    case failed

    var displayName: String {
        switch self {
        case .unavailable: "暂无状态"
        case .idle: "空闲"
        case .running: "运行中"
        case .waitingForApproval: "等待审批"
        case .waitingForInput: "等待补充"
        case .completed: "已完成"
        case .interrupted: "已停止"
        case .failed: "失败"
        }
    }
}

struct CapturePayload: Codable, Sendable {
    struct Source: Codable, Sendable {
        let kind: String
        let url: URL
        let conversationId: String?
        let title: String
    }

    struct Capture: Codable, Sendable {
        let capturedAt: Date
        let scope: String
        let attachmentCount: Int
        let complete: Bool
    }

    struct Selection: Codable, Sendable {
        let selectedTurnIds: [String]
    }

    let schemaVersion: Int
    let source: Source
    let capture: Capture
    let selection: Selection
    let turns: [CapturedTurn]
    let warnings: [String]
}

struct HandoffDraft: Identifiable, Codable, Equatable, Sendable {
    let id: UUID
    let sourceConversationID: UUID
    var selectedTurnIDs: [String]
    var instruction: String
    var workspacePath: String?
    var modelID: String?
    var reasoningEffort: String?
    var attachments: [TransferAttachment]? = nil
    var revision: Int
    var updatedAt: Date

    static func empty(for conversation: CapturedConversation) -> HandoffDraft {
        HandoffDraft(
            id: UUID(),
            sourceConversationID: conversation.id,
            selectedTurnIDs: conversation.turns.map(\.id),
            instruction: "",
            workspacePath: conversation.projectPath,
            modelID: nil,
            reasoningEffort: nil,
            revision: 1,
            updatedAt: .now
        )
    }
}

struct CodexModelOption: Identifiable, Codable, Hashable, Sendable {
    let id: String
    let displayName: String
    let description: String
    let isDefault: Bool
    let defaultReasoningEffort: String
    let supportedReasoningEfforts: [String]
}

enum ExecutionState: String, Codable, CaseIterable, Sendable {
    case draft
    case confirming
    case submitting
    case accepted
    case running
    case waitingForApproval
    case waitingForInput
    case completed
    case interrupting
    case interrupted
    case uncertain
    case failed

    var displayName: String {
        switch self {
        case .draft: "草稿"
        case .confirming: "等待确认"
        case .submitting: "正在提交"
        case .accepted: "任务已建立"
        case .running: "执行中"
        case .waitingForApproval: "等待审批"
        case .waitingForInput: "等待补充"
        case .completed: "已完成"
        case .interrupting: "正在停止"
        case .interrupted: "已停止"
        case .uncertain: "结果待确认"
        case .failed: "失败"
        }
    }
}

struct ExecutionOperation: Identifiable, Codable, Equatable, Sendable {
    let id: UUID
    let draftID: UUID
    let draftRevision: Int
    let fingerprint: String
    var state: ExecutionState
    var threadID: String?
    var turnID: String?
    var errorCode: String?
    var updatedAt: Date
}

struct SourceTaskLink: Identifiable, Codable, Hashable, Sendable {
    let id: UUID
    let sourceConversationID: UUID
    let selectedTurnIDs: [String]
    let threadID: String
    let createdAt: Date
}

enum HandoffDirection: String, Codable, Sendable {
    case chatGPTToCodex
    case codexToChatGPT
}

struct ChatGPTDraft: Identifiable, Codable, Hashable, Sendable {
    let id: UUID
    let sourceConversationID: UUID
    var selectedTurnIDs: [String]
    var content: String
    var createdAt: Date
    var updatedAt: Date

    static func make(from conversation: CapturedConversation) -> ChatGPTDraft {
        let selected = conversation.turns.filter { $0.assistant?.text.isEmpty == false }
        let text = SelectedConversationText.render(conversation, ids: Set(selected.map(\.id)))
        return ChatGPTDraft(
            id: UUID(),
            sourceConversationID: conversation.id,
            selectedTurnIDs: selected.map(\.id),
            content: text,
            createdAt: .now,
            updatedAt: .now
        )
    }
}

enum CodexInteractionKind: String, Sendable {
    case commandApproval
    case fileChangeApproval
    case permissionsApproval
    case userInput
}

struct CodexInputQuestion: Identifiable, Hashable, Sendable {
    let id: String
    let header: String
    let question: String
    let options: [String]
    let isSecret: Bool
}

struct CodexInteractionRequest: Identifiable, Hashable, Sendable {
    let id: String
    let kind: CodexInteractionKind
    let threadID: String
    let turnID: String
    let title: String
    let detail: String
    let questions: [CodexInputQuestion]
}

enum CodexRuntimeEvent: Sendable {
    case threadChanged(threadID: String)
    case threadArchived(threadID: String)
    case threadUnarchived(threadID: String)
    case stateChanged(threadID: String, turnID: String?, state: ExecutionState)
    case interaction(CodexInteractionRequest)
    case disconnected(String)
}

enum LibraryScope: String, CaseIterable, Identifiable, Hashable, Sendable {
    case projects
    case chatGPT
    case codex
    case handoffs

    var id: String { rawValue }

    var displayName: String {
        switch self {
        case .projects: "按项目"
        case .chatGPT: "ChatGPT"
        case .codex: "Codex"
        case .handoffs: "交接记录"
        }
    }

    var symbolName: String {
        switch self {
        case .projects: "folder"
        case .chatGPT: "bubble.left.and.bubble.right"
        case .codex: "terminal"
        case .handoffs: "link"
        }
    }
}

enum SourceConnectionState: Equatable, Sendable {
    case checking
    case ready(String)
    case connected(String)
    case experimental(String)
    case unavailable(String)

    var label: String {
        switch self {
        case .checking: "正在检查"
        case let .ready(value), let .connected(value), let .experimental(value), let .unavailable(value): value
        }
    }
}
