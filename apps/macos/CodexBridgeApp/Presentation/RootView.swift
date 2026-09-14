import AppKit
import SwiftUI

struct RootView: View {
    @Bindable var model: AppModel
    @Bindable var accessibility: AccessibilityPermissionController
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
        ConversationWorkspace(model: model)
            .frame(minWidth: 960, minHeight: 620)
            .sheet(isPresented: $model.isHandoffPresented) {
                HandoffEditorView(model: model)
            }
            .sheet(isPresented: $model.isManualCapturePresented) {
                ManualChatGPTCaptureView(model: model).frame(width: 620, height: 540)
            }
            .sheet(isPresented: $model.isSourcesPresented) {
                SourcesView(model: model, accessibility: accessibility)
                    .frame(width: 690, height: 610)
            }
            .sheet(isPresented: $model.isChatGPTDraftPresented) {
                SimpleChatGPTDraftEditor(model: model)
                    .frame(minWidth: 760, minHeight: 580)
            }
            .sheet(isPresented: $model.isContinuePresented) {
                ContinueCodexDialog(model: model)
                    .frame(width: 540, height: 300)
            }
            .sheet(item: $model.pendingCodexInteraction) { request in
                CodexInteractionDialog(model: model, request: request)
                    .frame(width: 560, height: request.kind == .userInput ? 430 : 390)
                    .interactiveDismissDisabled()
            }
            .overlay {
                if let error = model.errorMessage {
                    ZStack {
                        Color.black.opacity(0.22).ignoresSafeArea()
                        CodexBridgeErrorDialog(message: error, dismiss: model.dismissError)
                    }
                    .transition(.opacity)
                    .zIndex(20)
                }
            }
            .overlay(alignment: .bottom) {
                if let message = model.statusMessage {
                    CodexBridgeToast(message: message)
                        .padding(.bottom, 18)
                        .transition(.move(edge: .bottom).combined(with: .opacity))
                        .zIndex(10)
                }
            }
            .animation(reduceMotion ? nil : .easeOut(duration: 0.2), value: model.statusMessage)
            .animation(reduceMotion ? nil : .easeOut(duration: 0.18), value: model.errorMessage)
            .onReceive(DistributedNotificationCenter.default().publisher(for: Notification.Name("app.codexbridge.captureImported"))) { _ in
                Task { await model.reloadConversations() }
            }
            .task(id: model.selectedConversationID) {
                await model.loadSelectedConversationDetails()
            }
    }
}

private struct ConversationListView: View {
    @Bindable var model: AppModel
    var onConversationSelected: ((UUID) -> Void)?
    @State private var filter = ConversationListFilter.all

    init(model: AppModel, onConversationSelected: ((UUID) -> Void)? = nil) {
        self.model = model
        self.onConversationSelected = onConversationSelected
    }

    var body: some View {
        VStack(spacing: 0) {
            HStack(alignment: .firstTextBaseline, spacing: 8) {
                Text(model.selectedProject ?? model.selectedScope.displayName)
                    .font(.system(size: 18, weight: .bold))
                Text("\(filteredConversations.count) 段对话")
                    .font(.system(size: 10.5, weight: .medium))
                    .foregroundStyle(CodexBridgePalette.secondaryText)
                Spacer()
                Button {
                    Task {
                        await model.reloadConversations()
                        if model.selectedScope == .codex { await model.refreshCodexConnection() }
                    }
                } label: {
                    Image(systemName: "arrow.clockwise")
                }
                .buttonStyle(CodexBridgeIconButtonStyle())
                .help("刷新对话")
            }
            .padding(.horizontal, 18)
            .padding(.top, 15)

            HStack(spacing: 19) {
                ForEach(ConversationListFilter.allCases) { option in
                    ConversationListFilterButton(
                        title: option.title,
                        selected: filter == option,
                        action: { filter = option }
                    )
                }
                Spacer()
            }
            .padding(.horizontal, 18)
            .padding(.top, 5)

            Divider().overlay(CodexBridgePalette.border)

            if filteredConversations.isEmpty {
                CodexBridgeEmptyState(
                    title: model.searchText.isEmpty && filter == .all ? "这里还没有对话" : "没有匹配结果",
                    message: model.searchText.isEmpty && filter == .all
                        ? "连接来源或切换资料库，Codex Bridge 会把相关对话整理在一起。"
                        : "调整状态筛选、关键词或项目后重试。",
                    symbol: model.searchText.isEmpty && filter == .all ? "tray" : "line.3.horizontal.decrease.circle",
                    actionTitle: model.searchText.isEmpty && filter == .all ? "查看来源" : nil,
                    action: model.searchText.isEmpty && filter == .all ? { model.isSourcesPresented = true } : nil
                )
                .frame(maxWidth: .infinity, maxHeight: .infinity)
            } else {
                ScrollView {
                    LazyVStack(spacing: 0) {
                        ForEach(filteredConversations) { conversation in
                            ConversationRow(
                                conversation: conversation,
                                selected: model.selectedConversationID == conversation.id
                            ) {
                                model.selectedConversationID = conversation.id
                                onConversationSelected?(conversation.id)
                            }
                        }
                    }
                }
                .overlay {
                    if model.isBusy && model.conversations.isEmpty {
                        CodexBridgeLoadingView(title: "正在读取对话")
                    }
                }
            }
        }
        .background(CodexBridgePalette.canvas)
    }

    private var filteredConversations: [CapturedConversation] {
        model.visibleConversations.filter { conversation in
            switch filter {
            case .all:
                true
            case .needsAttention:
                conversation.runtimeStatus == .waitingForApproval || conversation.runtimeStatus == .waitingForInput
            case .completed:
                conversation.runtimeStatus == .completed
            }
        }
    }
}

private enum ConversationListFilter: String, CaseIterable, Identifiable {
    case all
    case needsAttention
    case completed

    var id: String { rawValue }

    var title: String {
        switch self {
        case .all: "全部"
        case .needsAttention: "等待你"
        case .completed: "已完成"
        }
    }
}

private struct ConversationListFilterButton: View {
    let title: String
    let selected: Bool
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            Text(title)
                .font(.system(size: 11, weight: selected ? .semibold : .regular))
                .foregroundStyle(selected ? CodexBridgePalette.primaryText : CodexBridgePalette.secondaryText)
                .padding(.vertical, 10)
                .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .overlay(alignment: .bottom) {
            Rectangle()
                .fill(selected ? CodexBridgePalette.accent : .clear)
                .frame(height: 2)
        }
        .accessibilityValue(selected ? "已选择" : "")
    }
}

private struct ConversationRow: View {
    let conversation: CapturedConversation
    let selected: Bool
    let action: () -> Void
    @State private var isHovering = false

    var body: some View {
        Button(action: action) {
            HStack(alignment: .top, spacing: 10) {
                SourceIcon(kind: conversation.sourceKind, size: 30, selected: selected)
                VStack(alignment: .leading, spacing: 5) {
                    HStack(alignment: .firstTextBaseline, spacing: 6) {
                        Text(conversation.title)
                            .font(.system(size: 12.5, weight: .semibold))
                            .foregroundStyle(CodexBridgePalette.primaryText)
                            .lineLimit(1)
                            .multilineTextAlignment(.leading)
                        Spacer(minLength: 3)
                        Text(conversation.capturedAt, style: .relative)
                            .font(.system(size: 9, design: .monospaced))
                            .foregroundStyle(CodexBridgePalette.tertiaryText)
                    }

                    Text(previewText)
                        .font(.system(size: 10.5))
                        .foregroundStyle(CodexBridgePalette.secondaryText)
                        .lineLimit(1)

                    HStack(spacing: 6) {
                        Text(conversation.sourceKind.displayName)
                        Text("·")
                        Text(conversation.projectName ?? "未归类")
                        Spacer(minLength: 4)
                        Circle().fill(stateColor).frame(width: 6, height: 6)
                        Text(stateText)
                    }
                    .font(.system(size: 9.5))
                    .foregroundStyle(CodexBridgePalette.tertiaryText)

                    if !conversation.warnings.isEmpty {
                        Image(systemName: "exclamationmark.triangle.fill")
                            .font(.system(size: 9))
                            .foregroundStyle(CodexBridgePalette.warning)
                    }
                }
            }
            .padding(.horizontal, 14)
            .padding(.vertical, 12)
            .frame(maxWidth: .infinity, minHeight: 82, alignment: .leading)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .background(selected ? CodexBridgePalette.surface : (isHovering ? CodexBridgePalette.subtleFill.opacity(0.7) : .clear))
        .overlay(alignment: .leading) {
            Rectangle().fill(selected ? CodexBridgePalette.accent : .clear).frame(width: 3)
        }
        .overlay(alignment: .bottom) { Rectangle().fill(CodexBridgePalette.border.opacity(0.7)).frame(height: 1) }
        .onHover { isHovering = $0 }
        .accessibilityValue(selected ? "已选择" : "")
    }

    private var previewText: String {
        if let latest = conversation.turns.last {
            return latest.assistant?.text ?? latest.user.text
        }
        if conversation.sourceKind == .codex {
            return conversation.runtimeStatus == .running ? "Codex 正在执行当前任务" : "尚无可展示的回复"
        }
        return "已保存的会话内容"
    }

    private var stateText: String {
        conversation.runtimeStatus?.displayName ?? conversation.freshness.displayName
    }

    private var stateColor: Color {
        switch conversation.runtimeStatus {
        case .running?, .completed?, .idle?:
            CodexBridgePalette.success
        case .waitingForApproval?, .waitingForInput?:
            CodexBridgePalette.warning
        case .failed?:
            CodexBridgePalette.danger
        case .interrupted?, .unavailable?:
            CodexBridgePalette.tertiaryText
        case nil:
            CodexBridgePalette.tertiaryText
        }
    }
}

private struct ConversationDetailView: View {
    let conversation: CapturedConversation
    @Bindable var model: AppModel
    var compact = false

    var body: some View {
        VStack(spacing: 0) {
            detailHeader
            Divider().overlay(CodexBridgePalette.border)

            ScrollView {
                VStack(alignment: .leading, spacing: 0) {
                    if !conversation.warnings.isEmpty {
                        InlineWarning(warnings: conversation.warnings)
                            .padding(.top, 14)
                    }

                    if conversation.turns.isEmpty {
                        CodexBridgeEmptyState(
                            title: emptyStateTitle,
                            message: emptyStateMessage,
                            symbol: model.conversationDetailErrors[conversation.id] == nil ? "terminal" : "exclamationmark.triangle"
                        )
                        .frame(maxWidth: .infinity, minHeight: 280)
                    } else {
                        DetailSectionTitle(title: "对话内容", symbol: "text.bubble")
                        VStack(spacing: 0) {
                            Divider().overlay(CodexBridgePalette.border)
                            ForEach(conversation.turns) { turn in
                                TranscriptMessageRow(
                                    role: "你 · \(turn.index + 1)",
                                    text: turn.user.text
                                )
                                Divider().overlay(CodexBridgePalette.border)
                                if let assistant = turn.assistant {
                                    TranscriptMessageRow(
                                        role: conversation.sourceKind == .codex ? "Codex" : "ChatGPT",
                                        text: assistant.text
                                    )
                                    Divider().overlay(CodexBridgePalette.border)
                                }
                            }
                        }
                    }

                    if let relation = relatedConversation {
                        DetailSectionTitle(title: "交接记录", symbol: "link")
                        HandoffRelationRow(from: conversation, to: relation)
                    }
                }
                .padding(.horizontal, compact ? 16 : 22)
                .padding(.bottom, 20)
                .frame(maxWidth: 820, alignment: .leading)
                .frame(maxWidth: .infinity)
            }

            if conversation.sourceKind == .codex {
                Divider().overlay(CodexBridgePalette.border)
                CodexInlineComposer(conversation: conversation, model: model, compact: compact)
            }
        }
        .background(CodexBridgePalette.canvas)
    }

    private var detailHeader: some View {
        VStack(alignment: .leading, spacing: 11) {
            HStack(spacing: 8) {
                Image(systemName: conversation.sourceKind.symbolName)
                    .font(.system(size: 12, weight: .semibold))
                    .foregroundStyle(CodexBridgePalette.secondaryText)
                Text(conversation.sourceKind.displayName)
                    .font(.system(size: 11, weight: .medium))
                    .foregroundStyle(CodexBridgePalette.secondaryText)
                Text("·")
                    .foregroundStyle(CodexBridgePalette.tertiaryText)
                if let runtime = conversation.runtimeStatus {
                    RuntimeIndicator(text: runtime.displayName, color: runtimeColor(runtime))
                } else {
                    Text(conversation.freshness.displayName)
                        .font(.system(size: 11))
                        .foregroundStyle(CodexBridgePalette.secondaryText)
                }
                Spacer(minLength: 12)
                detailActions
            }

            Text(conversation.title)
                .font(.system(size: compact ? 20 : 24, weight: .bold))
                .tracking(-0.4)
                .lineLimit(1)
                .minimumScaleFactor(0.78)
                .textSelection(.enabled)

            HStack(spacing: 7) {
                Text(conversation.projectName ?? "未归类")
                Text("·")
                Text("\(conversation.turns.count) 轮")
                Text("·")
                Text(conversation.capturedAt.formatted(date: .abbreviated, time: .shortened))
                if let modelID = conversation.modelID {
                    Text("·")
                    Text(modelID)
                }
            }
            .font(.system(size: 10.5))
            .foregroundStyle(CodexBridgePalette.secondaryText)
            .lineLimit(1)
        }
        .padding(.horizontal, compact ? 16 : 22)
        .padding(.vertical, compact ? 15 : 18)
        .background(CodexBridgePalette.surface)
    }

    @ViewBuilder
    private var detailActions: some View {
        if compact {
            Menu {
                if conversation.sourceKind == .codex {
                    Button("在 ChatGPT 中打开") { model.openSelectedCodexTask() }
                    Button("创建任务副本") { Task { await model.forkSelectedCodexTask() } }
                    Button("刷新内容") { Task { await model.loadSelectedConversationDetails(force: true) } }
                    if isActiveCodexTask {
                        Button("停止任务", role: .destructive) { Task { await model.interruptSelectedCodexTask() } }
                    }
                    Divider()
                    Button("转到 ChatGPT") { Task { await model.beginChatGPTDraft() } }
                        .disabled(conversation.turns.isEmpty)
                } else {
                    if let sourceURL = conversation.sourceURL {
                        Button("打开原会话") { NSWorkspace.shared.open(sourceURL) }
                    }
                    Button("交给 Codex") { Task { await model.beginHandoff() } }
                        .disabled(conversation.turns.isEmpty || model.models.isEmpty)
                }
            } label: {
                Image(systemName: "ellipsis")
            }
            .menuStyle(.borderlessButton)
            .frame(width: 30)
        } else {
            if conversation.sourceKind == .codex {
                Button("在 ChatGPT 中打开") { model.openSelectedCodexTask() }
                    .buttonStyle(CodexBridgeSecondaryButtonStyle(compact: true))
                Menu {
                    Button("创建任务副本") { Task { await model.forkSelectedCodexTask() } }
                    Button("刷新内容") { Task { await model.loadSelectedConversationDetails(force: true) } }
                    if isActiveCodexTask {
                        Divider()
                        Button("停止任务", role: .destructive) { Task { await model.interruptSelectedCodexTask() } }
                    }
                    Divider()
                    Button("转到 ChatGPT") { Task { await model.beginChatGPTDraft() } }
                        .disabled(conversation.turns.isEmpty)
                } label: {
                    Image(systemName: "ellipsis")
                        .frame(width: 28, height: 26)
                }
                .menuStyle(.borderlessButton)
            } else {
                if let sourceURL = conversation.sourceURL {
                    Button("打开原会话") { NSWorkspace.shared.open(sourceURL) }
                        .buttonStyle(CodexBridgeSecondaryButtonStyle(compact: true))
                }
                Button {
                    Task { await model.beginHandoff() }
                } label: {
                    Label("交给 Codex", systemImage: "arrow.right")
                }
                .buttonStyle(CodexBridgePrimaryButtonStyle(compact: true))
                .disabled(conversation.turns.isEmpty || model.models.isEmpty)
                .keyboardShortcut("h", modifiers: [.command, .shift])
            }
        }
    }

    private var emptyStateTitle: String {
        if model.loadingConversationID == conversation.id { return "正在读取对话内容" }
        if model.conversationDetailErrors[conversation.id] != nil { return "暂时无法读取对话内容" }
        return "暂无可展示的对话内容"
    }

    private var emptyStateMessage: String {
        if let error = model.conversationDetailErrors[conversation.id] { return error }
        if model.loadingConversationID == conversation.id {
            return "Codex Bridge 正在读取 Codex 中的用户消息和回复。"
        }
        return "这项任务还没有可展示的消息；你可以稍后刷新内容。"
    }

    private func runtimeColor(_ runtime: ConversationRuntimeStatus) -> Color {
        switch runtime {
        case .running, .waitingForApproval, .waitingForInput: CodexBridgePalette.warning
        case .completed, .idle: CodexBridgePalette.success
        case .failed: CodexBridgePalette.danger
        case .interrupted, .unavailable: CodexBridgePalette.secondaryText
        }
    }

    private var isActiveCodexTask: Bool {
        conversation.runtimeStatus == .running
            || conversation.runtimeStatus == .waitingForApproval
            || conversation.runtimeStatus == .waitingForInput
    }

    private var relatedConversation: CapturedConversation? {
        if conversation.sourceKind == .codex, let threadID = conversation.codexThreadID,
           let link = model.links.first(where: { $0.threadID == threadID }) {
            return model.conversations.first(where: { $0.id == link.sourceConversationID })
        }
        if let link = model.links.first(where: { $0.sourceConversationID == conversation.id }) {
            return model.conversations.first(where: { $0.codexThreadID == link.threadID })
        }
        return nil
    }
}

private struct RuntimeIndicator: View {
    let text: String
    let color: Color

    var body: some View {
        HStack(spacing: 5) {
            Circle().fill(color).frame(width: 6, height: 6)
            Text(text)
        }
        .font(.system(size: 11, weight: .medium))
        .foregroundStyle(CodexBridgePalette.secondaryText)
    }
}

private struct DetailSectionTitle: View {
    let title: String
    let symbol: String

    var body: some View {
        Label(title, systemImage: symbol)
            .font(.system(size: 11, weight: .semibold))
            .foregroundStyle(CodexBridgePalette.secondaryText)
            .padding(.top, 17)
            .padding(.bottom, 9)
    }
}

private struct TranscriptMessageRow: View {
    let role: String
    let text: String

    var body: some View {
        HStack(alignment: .top, spacing: 16) {
            Text(role)
                .font(.system(size: 10, weight: .semibold, design: .monospaced))
                .foregroundStyle(CodexBridgePalette.tertiaryText)
                .frame(width: 72, alignment: .leading)
            Text(text)
                .font(.system(size: 13))
                .foregroundStyle(CodexBridgePalette.primaryText)
                .lineSpacing(3)
                .textSelection(.enabled)
                .frame(maxWidth: .infinity, alignment: .leading)
        }
        .padding(.vertical, 14)
    }
}

private struct InlineWarning: View {
    let warnings: [String]

    var body: some View {
        HStack(alignment: .top, spacing: 9) {
            Image(systemName: "exclamationmark.triangle")
                .foregroundStyle(CodexBridgePalette.warning)
            VStack(alignment: .leading, spacing: 3) {
                Text("内容提示")
                    .font(.system(size: 11, weight: .semibold))
                ForEach(warnings, id: \.self) { warning in
                    Text(warning)
                        .font(.system(size: 10.5))
                        .foregroundStyle(CodexBridgePalette.secondaryText)
                }
            }
        }
        .padding(.vertical, 11)
        .frame(maxWidth: .infinity, alignment: .leading)
        .overlay(alignment: .top) { Divider().overlay(CodexBridgePalette.border) }
        .overlay(alignment: .bottom) { Divider().overlay(CodexBridgePalette.border) }
    }
}

private struct HandoffRelationRow: View {
    let from: CapturedConversation
    let to: CapturedConversation

    var body: some View {
        HStack(spacing: 12) {
            relationNode(label: from.sourceKind.displayName, title: from.title)
            Image(systemName: "arrow.right")
                .font(.system(size: 11, weight: .semibold))
                .foregroundStyle(CodexBridgePalette.accent)
            relationNode(label: to.sourceKind.displayName, title: to.title)
        }
        .padding(.vertical, 12)
        .overlay(alignment: .top) { Divider().overlay(CodexBridgePalette.border) }
        .overlay(alignment: .bottom) { Divider().overlay(CodexBridgePalette.border) }
    }

    private func relationNode(label: String, title: String) -> some View {
        VStack(alignment: .leading, spacing: 4) {
            Text(label)
                .font(.system(size: 9.5))
                .foregroundStyle(CodexBridgePalette.tertiaryText)
            Text(title)
                .font(.system(size: 11, weight: .semibold))
                .lineLimit(1)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }
}

private struct CodexInlineComposer: View {
    let conversation: CapturedConversation
    @Bindable var model: AppModel
    let compact: Bool

    var body: some View {
        HStack(alignment: .bottom, spacing: 10) {
            ZStack(alignment: .topLeading) {
                if model.continuePrompt.isEmpty {
                    Text("继续当前 Codex 任务…")
                        .font(.system(size: 12))
                        .foregroundStyle(CodexBridgePalette.tertiaryText)
                        .padding(.horizontal, 10)
                        .padding(.vertical, 11)
                        .allowsHitTesting(false)
                }
                TextEditor(text: $model.continuePrompt)
                    .font(.system(size: 12))
                    .scrollContentBackground(.hidden)
                    .padding(5)
                    .frame(minHeight: compact ? 48 : 54, maxHeight: 86)
            }
            .background(CodexBridgePalette.canvas)
            .overlay {
                RoundedRectangle(cornerRadius: CodexBridgeRadius.medium)
                    .stroke(CodexBridgePalette.border, lineWidth: 1)
            }

            Button {
                Task { await model.continueCodexTask() }
            } label: {
                if model.isBusy {
                    ProgressView().controlSize(.small).tint(.white)
                } else {
                    Label("发送", systemImage: "arrow.right")
                }
            }
            .buttonStyle(CodexBridgePrimaryButtonStyle(compact: true))
            .disabled(
                conversation.codexThreadID == nil
                    || model.continuePrompt.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
                    || model.isBusy
            )
            .keyboardShortcut(.return, modifiers: .command)
            .help(conversation.codexThreadID == nil ? "示例或离线任务无法继续" : "发送到当前 Codex 任务")
        }
        .padding(.horizontal, compact ? 12 : 16)
        .padding(.vertical, 12)
        .background(CodexBridgePalette.surface)
    }
}

private struct MetadataLabel: View {
    let text: String
    let symbol: String

    var body: some View {
        Label(text, systemImage: symbol)
            .font(.caption)
            .foregroundStyle(CodexBridgePalette.secondaryText)
            .lineLimit(1)
    }
}

private struct TurnCard: View {
    let turn: CapturedTurn
    let sourceKind: ConversationSourceKind

    var body: some View {
        VStack(alignment: .leading, spacing: 15) {
            HStack {
                Text("第 \(turn.index + 1) 轮")
                    .font(.caption.weight(.semibold))
                    .foregroundStyle(CodexBridgePalette.secondaryText)
                Spacer()
                Text("\(turn.characterCount) 字")
                    .font(.caption2.monospacedDigit())
                    .foregroundStyle(CodexBridgePalette.tertiaryText)
            }
            MessageBlock(role: "你", symbol: "person.fill", text: turn.user.text, tint: CodexBridgePalette.accent)
            if let assistant = turn.assistant {
                Divider().overlay(CodexBridgePalette.border)
                MessageBlock(
                    role: sourceKind == .codex ? "Codex" : "ChatGPT",
                    symbol: sourceKind == .codex ? "terminal" : "sparkles",
                    text: assistant.text,
                    tint: sourceKind == .codex ? CodexBridgePalette.codex : CodexBridgePalette.chatGPT
                )
            }
        }
        .padding(16)
        .codexBridgeSurface()
    }
}

private struct MessageBlock: View {
    let role: String
    let symbol: String
    let text: String
    let tint: Color

    var body: some View {
        HStack(alignment: .top, spacing: 11) {
            Image(systemName: symbol)
                .font(.system(size: 11, weight: .semibold))
                .foregroundStyle(tint)
                .frame(width: 27, height: 27)
                .background(tint.opacity(0.1), in: RoundedRectangle(cornerRadius: CodexBridgeRadius.small))
            VStack(alignment: .leading, spacing: 6) {
                Text(role)
                    .font(.caption.weight(.semibold))
                    .foregroundStyle(CodexBridgePalette.secondaryText)
                Text(text)
                    .font(.body)
                    .foregroundStyle(CodexBridgePalette.primaryText)
                    .textSelection(.enabled)
                    .lineSpacing(2)
            }
        }
    }
}

private struct WarningCard: View {
    let warnings: [String]

    var body: some View {
        HStack(alignment: .top, spacing: 11) {
            Image(systemName: "info.circle.fill")
                .foregroundStyle(CodexBridgePalette.warning)
            VStack(alignment: .leading, spacing: 5) {
                Text("内容提示").font(.subheadline.weight(.semibold))
                ForEach(warnings, id: \.self) {
                    Text($0).font(.caption).foregroundStyle(CodexBridgePalette.secondaryText)
                }
            }
        }
        .padding(14)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(CodexBridgePalette.warning.opacity(0.08), in: RoundedRectangle(cornerRadius: CodexBridgeRadius.medium))
        .overlay {
            RoundedRectangle(cornerRadius: CodexBridgeRadius.medium)
                .stroke(CodexBridgePalette.warning.opacity(0.2), lineWidth: 1)
        }
    }
}

struct BrandMark: View {
    var compact = false
    var condensed = false

    var body: some View {
        HStack(spacing: condensed ? 10 : 9) {
            ZStack {
                RoundedRectangle(cornerRadius: CodexBridgeRadius.small, style: .continuous)
                    .fill(CodexBridgePalette.brandGradient)
                Image(systemName: "sidebar.right")
                    .font(.system(size: compact ? 12 : (condensed ? 14 : 18), weight: .bold))
                    .foregroundStyle(.white)
            }
            .frame(
                width: compact ? 27 : (condensed ? 34 : 42),
                height: compact ? 27 : (condensed ? 34 : 42)
            )
            .overlay {
                RoundedRectangle(cornerRadius: CodexBridgeRadius.small, style: .continuous)
                    .stroke(.white.opacity(0.16), lineWidth: 1)
            }
            if !compact {
                VStack(alignment: .leading, spacing: condensed ? 0 : 1) {
                    if condensed {
                        Text("Codex Bridge")
                            .font(.system(size: 16, weight: .semibold))
                            .lineLimit(1)
                            .minimumScaleFactor(0.9)
                        Text("\(CodexBridgeRelease.channel) · ChatGPT × Codex")
                            .font(.system(size: 9.5, weight: .medium))
                            .foregroundStyle(CodexBridgePalette.secondaryText)
                            .lineLimit(1)
                            .minimumScaleFactor(0.8)
                    } else {
                        HStack(spacing: 6) {
                            Text("Codex Bridge")
                                .font(.title2.bold())
                                .lineLimit(1)
                            CodexBridgeStatusBadge(
                                text: CodexBridgeRelease.channel.uppercased(),
                                color: CodexBridgePalette.accent
                            )
                        }
                        Text("ChatGPT × Codex")
                            .font(.caption2.weight(.medium))
                            .foregroundStyle(CodexBridgePalette.secondaryText)
                            .lineLimit(1)
                    }
                }
            }
        }
        .accessibilityElement(children: .combine)
        .accessibilityLabel("Codex Bridge \(CodexBridgeRelease.channel)")
    }
}

struct SourceIcon: View {
    let kind: ConversationSourceKind
    var size: CGFloat = 28
    var selected = false

    var body: some View {
        let color = selected ? CodexBridgePalette.accent : CodexBridgePalette.secondaryText
        Image(systemName: kind.symbolName)
            .font(.system(size: size * 0.4, weight: .semibold))
            .foregroundStyle(color)
            .frame(width: size, height: size)
            .background(selected ? CodexBridgePalette.accent.opacity(0.1) : CodexBridgePalette.surface, in: RoundedRectangle(cornerRadius: CodexBridgeRadius.medium, style: .continuous))
            .overlay {
                RoundedRectangle(cornerRadius: CodexBridgeRadius.medium, style: .continuous)
                    .stroke(selected ? CodexBridgePalette.accent.opacity(0.28) : CodexBridgePalette.border, lineWidth: 1)
            }
    }
}

private struct ContinueCodexDialog: View {
    @Bindable var model: AppModel
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            HStack {
                VStack(alignment: .leading, spacing: 3) {
                    Text("继续 Codex 任务").font(.title3.weight(.semibold))
                    Text(model.selectedConversation?.title ?? "")
                        .font(.caption)
                        .foregroundStyle(CodexBridgePalette.secondaryText)
                        .lineLimit(1)
                }
                Spacer()
                Button { dismiss() } label: { Image(systemName: "xmark") }
                    .buttonStyle(CodexBridgeIconButtonStyle())
            }
            TextEditor(text: $model.continuePrompt)
                .font(.body)
                .scrollContentBackground(.hidden)
                .padding(10)
                .background(CodexBridgePalette.surface)
                .overlay { RoundedRectangle(cornerRadius: CodexBridgeRadius.medium).stroke(CodexBridgePalette.border) }
            HStack {
                Text("将作为新一轮用户消息发送到同一 Codex 任务。")
                    .font(.caption)
                    .foregroundStyle(CodexBridgePalette.secondaryText)
                Spacer()
                Button("取消") { dismiss() }.buttonStyle(CodexBridgeSecondaryButtonStyle())
                Button("继续执行") { Task { await model.continueCodexTask() } }
                    .buttonStyle(CodexBridgePrimaryButtonStyle())
                    .disabled(model.continuePrompt.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
            }
        }
        .padding(22)
        .background(CodexBridgePalette.canvas)
    }
}

struct ManualChatGPTCaptureView: View {
    @Bindable var model: AppModel
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        VStack(spacing: 0) {
            HStack {
                VStack(alignment: .leading, spacing: 3) {
                    Text("保存 ChatGPT App 对话").font(.title3.weight(.semibold))
                    Text("请保留「用户：」与「ChatGPT：」角色标题，自动拆分为轮次。")
                        .font(.caption)
                        .foregroundStyle(CodexBridgePalette.secondaryText)
                }
                Spacer()
                Button { dismiss() } label: { Image(systemName: "xmark") }
                    .buttonStyle(CodexBridgeIconButtonStyle())
            }
            .padding(.horizontal, 22)
            .frame(height: 76)
            .background(CodexBridgePalette.surface)
            Divider().overlay(CodexBridgePalette.border)

            VStack(alignment: .leading, spacing: 14) {
                TextField("对话标题", text: $model.manualCaptureTitle)
                    .textFieldStyle(.roundedBorder)
                TextField("项目名称（可选）", text: $model.manualCaptureProject)
                    .textFieldStyle(.roundedBorder)
                TextEditor(text: $model.manualCaptureText)
                    .font(.body)
                    .scrollContentBackground(.hidden)
                    .padding(10)
                    .background(CodexBridgePalette.surface)
                    .overlay { RoundedRectangle(cornerRadius: CodexBridgeRadius.medium).stroke(CodexBridgePalette.border) }
                Label("内容只保存在这台 Mac 上。", systemImage: "lock")
                    .font(.caption)
                    .foregroundStyle(CodexBridgePalette.secondaryText)
            }
            .padding(22)

            Divider().overlay(CodexBridgePalette.border)
            HStack {
                Button("取消") { dismiss() }.buttonStyle(CodexBridgeSecondaryButtonStyle())
                Spacer()
                Button("保存到 Codex Bridge") { Task { await model.saveManualChatGPTCapture() } }
                    .buttonStyle(CodexBridgePrimaryButtonStyle())
                    .disabled(model.manualCaptureText.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
            }
            .padding(.horizontal, 22)
            .frame(height: 68)
            .background(CodexBridgePalette.surface)
        }
        .background(CodexBridgePalette.canvas)
    }
}

private struct CodexInteractionDialog: View {
    @Bindable var model: AppModel
    let request: CodexInteractionRequest

    var body: some View {
        VStack(spacing: 0) {
            HStack(spacing: 12) {
                Image(systemName: dialogSymbol)
                    .font(.system(size: 15, weight: .semibold))
                    .foregroundStyle(dialogColor)
                    .frame(width: 32, height: 32)
                    .background(dialogColor.opacity(0.09))
                    .overlay { Rectangle().stroke(dialogColor.opacity(0.25), lineWidth: 1) }
                VStack(alignment: .leading, spacing: 4) {
                    Text(dialogEyebrow.uppercased())
                        .font(.system(size: 9, weight: .bold))
                        .tracking(0.8)
                        .foregroundStyle(dialogColor)
                    Text(request.title)
                        .font(.system(size: 17, weight: .bold))
                        .lineLimit(1)
                }
                Spacer()
            }
            .padding(.horizontal, 20)
            .frame(height: 68)
            .background(CodexBridgePalette.surface)

            Divider().overlay(CodexBridgePalette.border)

            ScrollView {
                VStack(alignment: .leading, spacing: 18) {
                    if !request.detail.isEmpty {
                        VStack(alignment: .leading, spacing: 7) {
                            Text(detailTitle)
                                .font(.system(size: 10, weight: .semibold))
                                .foregroundStyle(CodexBridgePalette.secondaryText)
                            Text(request.detail)
                                .font(.system(size: request.kind == .userInput ? 12 : 11.5, design: request.kind == .userInput ? .default : .monospaced))
                                .lineSpacing(3)
                                .frame(maxWidth: .infinity, alignment: .leading)
                                .padding(12)
                                .background(CodexBridgePalette.raisedSurface)
                                .overlay { Rectangle().stroke(CodexBridgePalette.border, lineWidth: 1) }
                                .textSelection(.enabled)
                        }
                    }

                    if request.kind == .userInput {
                        VStack(spacing: 0) {
                            ForEach(Array(request.questions.enumerated()), id: \.element.id) { index, question in
                                VStack(alignment: .leading, spacing: 8) {
                                    HStack(alignment: .firstTextBaseline) {
                                        Text(question.header.uppercased())
                                            .font(.system(size: 9, weight: .bold))
                                            .tracking(0.7)
                                            .foregroundStyle(CodexBridgePalette.accent)
                                        Spacer()
                                        Text("\(index + 1) / \(request.questions.count)")
                                            .font(.system(size: 9, design: .monospaced))
                                            .foregroundStyle(CodexBridgePalette.tertiaryText)
                                    }
                                    Text(question.question)
                                        .font(.system(size: 12, weight: .medium))
                                    answerField(question)
                                }
                                .padding(.vertical, 13)

                                if index < request.questions.count - 1 {
                                    Divider().overlay(CodexBridgePalette.border)
                                }
                            }
                        }
                    } else {
                        HStack(alignment: .top, spacing: 9) {
                            Image(systemName: "shield.lefthalf.filled")
                                .foregroundStyle(CodexBridgePalette.accent)
                            VStack(alignment: .leading, spacing: 3) {
                                Text("仅本次授权")
                                    .font(.system(size: 11, weight: .semibold))
                                Text("决定只对当前请求生效，不会更改全局权限，也不会建立永久放行规则。")
                                    .font(.system(size: 10.5))
                                    .foregroundStyle(CodexBridgePalette.secondaryText)
                            }
                        }
                        .padding(.top, 1)
                    }
                }
                .padding(20)
            }

            Divider().overlay(CodexBridgePalette.border)

            HStack(spacing: 10) {
                Button(request.kind == .userInput ? "取消" : "拒绝") {
                    Task { await model.respondToCodexInteraction(accepted: false) }
                }
                .buttonStyle(CodexBridgeSecondaryButtonStyle())
                .keyboardShortcut(.cancelAction)

                if request.kind != .userInput {
                    Text("Codex 将停在当前请求处")
                        .font(.system(size: 10))
                        .foregroundStyle(CodexBridgePalette.secondaryText)
                }
                Spacer()
                Button(request.kind == .userInput ? "提交回答" : "只允许本次") {
                    Task { await model.respondToCodexInteraction(accepted: true) }
                }
                .buttonStyle(CodexBridgePrimaryButtonStyle())
                .disabled(request.kind == .userInput && !hasAllAnswers)
                .keyboardShortcut(.defaultAction)
            }
            .padding(.horizontal, 20)
            .frame(height: 64)
            .background(CodexBridgePalette.surface)
        }
        .background(CodexBridgePalette.canvas)
    }

    @ViewBuilder
    private func answerField(_ question: CodexInputQuestion) -> some View {
        if question.isSecret {
            SecureField("输入回答", text: answerBinding(question.id))
                .textFieldStyle(.roundedBorder)
        } else if !question.options.isEmpty {
            Picker("回答", selection: answerBinding(question.id)) {
                Text("请选择").tag("")
                ForEach(question.options, id: \.self) { Text($0).tag($0) }
            }
            .labelsHidden()
            .frame(maxWidth: .infinity, alignment: .leading)
        } else {
            TextField("输入回答", text: answerBinding(question.id))
                .textFieldStyle(.roundedBorder)
        }
    }

    private var dialogEyebrow: String {
        request.kind == .userInput ? "Codex 需要你的回答" : "Codex 请求授权"
    }

    private var detailTitle: String {
        switch request.kind {
        case .commandApproval: "准备运行的命令"
        case .fileChangeApproval: "准备修改的文件"
        case .permissionsApproval: "请求的权限范围"
        case .userInput: "补充说明"
        }
    }

    private var dialogSymbol: String {
        switch request.kind {
        case .commandApproval: "terminal"
        case .fileChangeApproval: "doc.badge.gearshape"
        case .permissionsApproval: "hand.raised.fill"
        case .userInput: "questionmark.bubble"
        }
    }

    private var dialogColor: Color {
        request.kind == .userInput ? CodexBridgePalette.accent : CodexBridgePalette.warning
    }

    private var hasAllAnswers: Bool {
        request.questions.allSatisfy {
            !(model.interactionAnswers[$0.id] ?? "").trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
        }
    }

    private func answerBinding(_ id: String) -> Binding<String> {
        Binding(get: { model.interactionAnswers[id] ?? "" }, set: { model.interactionAnswers[id] = $0 })
    }
}
