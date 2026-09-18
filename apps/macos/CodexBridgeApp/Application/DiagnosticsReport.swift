import Foundation

struct DiagnosticsReportBuilder: Sendable {
    func data(
        conversations: [CapturedConversation],
        operations: [ExecutionOperation],
        handoffLinkCount: Int,
        chatGPTDraftCount: Int,
        codexState: SourceConnectionState,
        codexVersion: String?,
        appVersion: String,
        generatedAt: Date = .now
    ) throws -> Data {
        let report = DiagnosticsReport(
            schemaVersion: 2,
            generatedAt: ISO8601DateFormatter().string(from: generatedAt),
            appVersion: appVersion,
            conversationCounts: Dictionary(grouping: conversations, by: { $0.sourceKind.rawValue }).mapValues(\.count),
            operationCounts: Dictionary(grouping: operations, by: { $0.state.rawValue }).mapValues(\.count),
            handoffLinkCount: handoffLinkCount,
            chatGPTDraftCount: chatGPTDraftCount,
            codexConnection: codexState.diagnosticCode,
            codexVersion: codexVersion,
            privacy: "不包含对话内容、账户信息、错误原文、文件路径、网页地址或真实任务标识"
        )
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys, .withoutEscapingSlashes]
        return try encoder.encode(report)
    }
}

private struct DiagnosticsReport: Encodable {
    let schemaVersion: Int
    let generatedAt: String
    let appVersion: String
    let conversationCounts: [String: Int]
    let operationCounts: [String: Int]
    let handoffLinkCount: Int
    let chatGPTDraftCount: Int
    let codexConnection: String
    let codexVersion: String?
    let privacy: String
}

private extension SourceConnectionState {
    var diagnosticCode: String {
        switch self {
        case .checking: "checking"
        case .ready: "ready"
        case .connected: "connected"
        case .experimental: "experimental"
        case .unavailable: "unavailable"
        }
    }
}
