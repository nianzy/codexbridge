import AppKit
import SwiftUI

enum ConversationCollectionScope {
    case all
    case pinned
    case archived
}

enum ConversationTypeFilter {
    case all
    case chat
    case codex
}

enum ConversationSortMode: String, CaseIterable, Identifiable {
    case recent
    case oldest
    case title

    var id: String { rawValue }

    var title: String {
        switch self {
        case .recent: "最近更新优先"
        case .oldest: "最早更新优先"
        case .title: "按标题排序"
        }
    }

    var shortTitle: String {
        switch self {
        case .recent: "最近更新"
        case .oldest: "最早更新"
        case .title: "标题"
        }
    }

    var symbol: String {
        switch self {
        case .recent: "arrow.down"
        case .oldest: "arrow.up"
        case .title: "textformat"
        }
    }
}

struct ConversationListRow: View {
    let conversation: CapturedConversation
    let selected: Bool
    let pinned: Bool
    let action: () -> Void
    @State private var hovering = false

    var body: some View {
        Button(action: action) {
            HStack(alignment: .top, spacing: 11) {
                ConversationTypeIcon(kind: conversation.sourceKind, size: 38)

                VStack(alignment: .leading, spacing: 5) {
                    HStack(alignment: .firstTextBaseline, spacing: 8) {
                        Text(conversation.title)
                            .font(.system(size: 14, weight: .semibold))
                            .foregroundStyle(CodexBridgePalette.primaryText)
                            .lineLimit(1)
                        Spacer(minLength: 5)
                        Text(conversation.updatedAt ?? conversation.capturedAt, style: .relative)
                            .font(.system(size: 9))
                            .foregroundStyle(CodexBridgePalette.secondaryText)
                            .lineLimit(1)
                    }

                    HStack(spacing: 5) {
                        Text(conversation.sourceKind == .codex ? "Codex" : "Chat")
                        Text("·")
                        Text(projectName)
                            .lineLimit(1)
                        if pinned {
                            Image(systemName: "pin.fill")
                                .font(.system(size: 8))
                        }
                        ConversationRowState(conversation: conversation)
                    }
                    .font(.system(size: 9.5))
                    .foregroundStyle(CodexBridgePalette.secondaryText)
                }
            }
            .padding(.horizontal, 13)
            .padding(.vertical, 13)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(selected ? CodexBridgePalette.selectedFill : (hovering ? CodexBridgePalette.raisedSurface : CodexBridgePalette.surface))
            .overlay(alignment: .leading) {
                Rectangle().fill(selected ? CodexBridgePalette.accent : Color.clear).frame(width: 3)
            }
            .overlay(alignment: .bottom) {
                Rectangle().fill(CodexBridgePalette.border).frame(height: 1)
            }
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .onHover { hovering = $0 }
        .accessibilityLabel("\(conversation.sourceKind == .codex ? "Codex" : "Chat")，\(conversation.title)")
        .accessibilityAddTraits(selected ? .isSelected : [])
    }

    private var projectName: String {
        if let name = conversation.projectName, !name.isEmpty { return name }
        if let path = conversation.projectPath, !path.isEmpty { return URL(fileURLWithPath: path).lastPathComponent }
        return "未归类"
    }
}

struct ConversationTypeIcon: View {
    let kind: ConversationSourceKind
    var size: CGFloat = 34

    var body: some View {
        Image(systemName: kind == .codex ? "terminal" : "bubble.left.and.text.bubble.right")
            .font(.system(size: size * 0.42, weight: .medium))
            .foregroundStyle(kind == .codex ? CodexBridgePalette.codex : CodexBridgePalette.chatGPT)
            .frame(width: size, height: size)
            .background(CodexBridgePalette.raisedSurface, in: iconShape)
            .overlay(iconShape.stroke(CodexBridgePalette.border, lineWidth: 1))
    }

    private var iconShape: AnyShape {
        if kind == .codex {
            AnyShape(RoundedRectangle(cornerRadius: CodexBridgeRadius.small))
        } else {
            AnyShape(Circle())
        }
    }
}

struct ConversationKindBadge: View {
    let kind: ConversationSourceKind

    var body: some View {
        Label(kind == .codex ? "Codex" : "Chat", systemImage: kind == .codex ? "terminal" : "bubble.left.and.text.bubble.right")
            .font(.system(size: 9.5, weight: .semibold))
            .foregroundStyle(kind == .codex ? CodexBridgePalette.codex : CodexBridgePalette.chatGPT)
            .padding(.horizontal, 7)
            .frame(height: 23)
            .background(CodexBridgePalette.raisedSurface, in: RoundedRectangle(cornerRadius: 5))
            .overlay(RoundedRectangle(cornerRadius: 5).stroke(CodexBridgePalette.border, lineWidth: 1))
    }
}

struct ConversationStateBadge: View {
    let conversation: CapturedConversation

    var body: some View {
        Label(state.title, systemImage: state.symbol)
            .font(.system(size: 9.5, weight: .semibold))
            .foregroundStyle(state.color)
            .padding(.horizontal, 7)
            .frame(height: 23)
            .background(CodexBridgePalette.raisedSurface, in: RoundedRectangle(cornerRadius: 5))
            .overlay(RoundedRectangle(cornerRadius: 5).stroke(CodexBridgePalette.border, lineWidth: 1))
    }

    private var state: ConversationVisualState { ConversationVisualState(conversation: conversation) }
}

struct ConversationRowState: View {
    let conversation: CapturedConversation

    var body: some View {
        let state = ConversationVisualState(conversation: conversation)
        HStack(spacing: 4) {
            Image(systemName: state.symbol).font(.system(size: 8))
            Text(state.title)
        }
        .foregroundStyle(state.color)
        .lineLimit(1)
    }
}

struct ConversationVisualState {
    let title: String
    let symbol: String
    let color: Color

    init(conversation: CapturedConversation) {
        guard conversation.sourceKind == .codex else {
            title = "可继续"
            symbol = "bubble.left"
            color = CodexBridgePalette.chatGPT
            return
        }
        switch conversation.runtimeStatus ?? .unavailable {
        case .running:
            title = "运行中"; symbol = "play.fill"; color = CodexBridgePalette.info
        case .waitingForApproval, .waitingForInput:
            title = "待确认"; symbol = "clock"; color = CodexBridgePalette.warning
        case .completed, .idle:
            title = "已完成"; symbol = "checkmark"; color = CodexBridgePalette.success
        case .failed:
            title = "失败"; symbol = "xmark.circle"; color = CodexBridgePalette.danger
        case .interrupted:
            title = "已停止"; symbol = "stop"; color = CodexBridgePalette.secondaryText
        case .unavailable:
            title = "暂无状态"; symbol = "circle.dashed"; color = CodexBridgePalette.secondaryText
        }
    }
}

struct ConversationSummaryCard: View {
    let conversation: CapturedConversation
    let fileCount: Int

    var body: some View {
        HStack(alignment: .top, spacing: 12) {
            ConversationTypeIcon(kind: conversation.sourceKind, size: 38)
            VStack(alignment: .leading, spacing: 4) {
                Text(conversation.sourceKind == .codex ? "任务概览" : "对话概览")
                    .font(.system(size: 9, weight: .bold))
                    .tracking(0.7)
                    .foregroundStyle(CodexBridgePalette.secondaryText)
                Text(summaryTitle)
                    .font(.system(size: 12, weight: .semibold))
                    .foregroundStyle(CodexBridgePalette.primaryText)
                Text(summary)
                    .font(.system(size: 10.5))
                    .foregroundStyle(CodexBridgePalette.secondaryText)
                    .fixedSize(horizontal: false, vertical: true)
            }
            Spacer()
        }
        .padding(14)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(CodexBridgePalette.surface, in: RoundedRectangle(cornerRadius: CodexBridgeRadius.large))
        .overlay(RoundedRectangle(cornerRadius: CodexBridgeRadius.large).stroke(CodexBridgePalette.border, lineWidth: 1))
    }

    private var summaryTitle: String {
        conversation.sourceKind == .codex
            ? (conversation.runtimeStatus?.displayName ?? "暂无状态")
            : conversation.freshness.displayName
    }

    private var summary: String {
        let turns = "\(conversation.turns.count) 轮对话"
        let files = fileCount == 0 ? "暂无相关文件" : "\(fileCount) 个相关文件"
        return conversation.sourceKind == .codex
            ? "可以查看任务内容，并选择需要的对话交给 ChatGPT。\(turns) · \(files)。"
            : "内容保存在本机，可以选择需要的对话交给 Codex。\(turns) · \(files)。"
    }
}

struct RoundSelectionDivider: View {
    let index: Int
    let selected: Bool
    let complete: Bool
    let action: (Bool) -> Void

    var body: some View {
        HStack(spacing: 13) {
            Rectangle().fill(CodexBridgePalette.border).frame(height: 1)
            Button {
                action(NSEvent.modifierFlags.contains(.shift))
            } label: {
                HStack(spacing: 8) {
                    CodexBridgeSelectionCheckbox(selected: selected, size: 22)
                    Text("第 \(index + 1) 轮")
                    Text(complete ? "完整" : "未完成")
                        .foregroundStyle(CodexBridgePalette.secondaryText)
                }
                .font(.system(size: 11.5, weight: .semibold))
                .padding(.horizontal, 5)
                .frame(height: 44)
                .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            Rectangle().fill(CodexBridgePalette.border).frame(height: 1)
        }
        .padding(.vertical, 5)
        .accessibilityLabel("第 \(index + 1) 轮，\(selected ? "已选择" : "未选择")")
        .accessibilityAddTraits(selected ? .isSelected : [])
    }
}

struct TurnSelectionToolbar: View {
    let selectedCount: Int
    let canSelectFive: Bool
    let selectRecent: (Int) -> Void
    let selectComplete: () -> Void
    let clear: () -> Void

    var body: some View {
        HStack(spacing: 10) {
            Image(systemName: "checkmark.square")
                .font(.system(size: 12, weight: .semibold))
                .foregroundStyle(CodexBridgePalette.accent)

            VStack(alignment: .leading, spacing: 2) {
                Text(selectedCount == 0 ? "选择要交接的对话" : "已选择 \(selectedCount) 轮")
                    .font(.system(size: 11, weight: .semibold))
                    .foregroundStyle(CodexBridgePalette.primaryText)
                Text("按住 Shift 点击可连续选择")
                    .font(.system(size: 9.5))
                    .foregroundStyle(CodexBridgePalette.secondaryText)
            }

            Spacer(minLength: 8)

            Menu {
                Button("最近 3 轮") { selectRecent(3) }
                if canSelectFive {
                    Button("最近 5 轮") { selectRecent(5) }
                }
                Button("所有已完成对话") { selectComplete() }
                Divider()
                Button("清空选择", action: clear)
                    .disabled(selectedCount == 0)
            } label: {
                HStack(spacing: 6) {
                    Image(systemName: "checklist")
                    Text("快速选择")
                    Image(systemName: "chevron.down")
                        .font(.system(size: 8, weight: .bold))
                }
                .font(.system(size: 10.5, weight: .semibold))
                .foregroundStyle(CodexBridgePalette.secondaryText)
                .padding(.horizontal, 10)
                .frame(height: 32)
                .background(CodexBridgePalette.surface, in: RoundedRectangle(cornerRadius: CodexBridgeRadius.small))
                .overlay(RoundedRectangle(cornerRadius: CodexBridgeRadius.small).stroke(CodexBridgePalette.border, lineWidth: 1))
            }
            .menuStyle(.borderlessButton)
            .menuIndicator(.hidden)
            .fixedSize()
        }
        .padding(.horizontal, 12)
        .frame(minHeight: 44)
        .background(CodexBridgePalette.raisedSurface, in: RoundedRectangle(cornerRadius: CodexBridgeRadius.medium))
        .overlay(RoundedRectangle(cornerRadius: CodexBridgeRadius.medium).stroke(CodexBridgePalette.border, lineWidth: 1))
    }
}

struct CodexBridgeColumnResizeHandle: View {
    @Binding var value: CGFloat
    let displayedWidth: CGFloat
    let allowedRange: ClosedRange<CGFloat>
    let label: String
    let persist: (CGFloat) -> Void

    @State private var dragStartWidth: CGFloat?
    @State private var isHovering = false
    @State private var isDragging = false

    var body: some View {
        ZStack {
            CodexBridgePalette.surface
            Rectangle()
                .fill(isHovering || isDragging ? CodexBridgePalette.accent : CodexBridgePalette.border)
                .frame(width: isHovering || isDragging ? 2 : 1)
        }
        .contentShape(Rectangle())
        .gesture(
            DragGesture(minimumDistance: 1, coordinateSpace: .global)
                .onChanged { gesture in
                    let start = dragStartWidth ?? displayedWidth
                    if dragStartWidth == nil {
                        dragStartWidth = displayedWidth
                        isDragging = true
                    }
                    let nextWidth = clamp(start + gesture.translation.width)
                    guard abs(nextWidth - value) >= 0.25 else { return }
                    var transaction = Transaction()
                    transaction.disablesAnimations = true
                    withTransaction(transaction) {
                        value = nextWidth
                    }
                }
                .onEnded { gesture in
                    let finalWidth = clamp((dragStartWidth ?? displayedWidth) + gesture.translation.width)
                    value = finalWidth
                    persist(finalWidth)
                    dragStartWidth = nil
                    isDragging = false
                    if !isHovering {
                        NSCursor.arrow.set()
                    }
                }
        )
        .onHover { hovering in
            isHovering = hovering
            if hovering {
                NSCursor.resizeLeftRight.set()
            } else if !isDragging {
                NSCursor.arrow.set()
            }
        }
        .accessibilityElement()
        .accessibilityLabel(label)
        .accessibilityValue("\(Int(displayedWidth)) 点")
        .accessibilityAdjustableAction { direction in
            switch direction {
            case .increment:
                let nextWidth = clamp(displayedWidth + 20)
                value = nextWidth
                persist(nextWidth)
            case .decrement:
                let nextWidth = clamp(displayedWidth - 20)
                value = nextWidth
                persist(nextWidth)
            @unknown default:
                break
            }
        }
    }

    private func clamp(_ proposed: CGFloat) -> CGFloat {
        min(max(proposed, allowedRange.lowerBound), allowedRange.upperBound)
    }
}

struct CodexBridgeSelectionCheckbox: View {
    let selected: Bool
    var size: CGFloat = 20

    var body: some View {
        ZStack {
            RoundedRectangle(cornerRadius: 5, style: .continuous)
                .fill(selected ? CodexBridgePalette.accent : CodexBridgePalette.surface)
            RoundedRectangle(cornerRadius: 5, style: .continuous)
                .stroke(selected ? CodexBridgePalette.accent : CodexBridgePalette.secondaryText, lineWidth: 1.5)
            if selected {
                Image(systemName: "checkmark")
                    .font(.system(size: size * 0.52, weight: .bold))
                    .foregroundStyle(CodexBridgePalette.onPrimary)
            }
        }
        .frame(width: size, height: size)
    }
}

enum MessageKind { case user, chat, codex }

struct ConversationMessageRow<Content: View>: View {
    let role: String
    let kind: MessageKind
    let timestamp: String?
    @ViewBuilder let content: Content

    var body: some View {
        if kind == .user {
            HStack(alignment: .top, spacing: 10) {
                Spacer(minLength: 54)
                messageStack(alignment: .trailing)
                avatar
            }
            .padding(.bottom, 16)
        } else {
            HStack(alignment: .top, spacing: 10) {
                avatar
                messageStack(alignment: .leading)
                Spacer(minLength: 54)
            }
            .padding(.bottom, 16)
        }
    }

    private var avatar: some View {
        Group {
            switch kind {
            case .user: Text("你")
            case .chat: Image(systemName: "bubble.left.and.text.bubble.right")
            case .codex: Image(systemName: "terminal")
            }
        }
        .font(.system(size: 10, weight: .bold))
        .foregroundStyle(avatarColor)
        .frame(width: 32, height: 32)
        .background(avatarBackground, in: kind == .codex ? AnyShape(RoundedRectangle(cornerRadius: CodexBridgeRadius.medium)) : AnyShape(Circle()))
        .overlay {
            (kind == .codex ? AnyShape(RoundedRectangle(cornerRadius: CodexBridgeRadius.medium)) : AnyShape(Circle()))
                .stroke(CodexBridgePalette.border, lineWidth: 1)
        }
    }

    private func messageStack(alignment: HorizontalAlignment) -> some View {
        VStack(alignment: alignment, spacing: 6) {
            HStack(spacing: 6) {
                if kind == .user, let timestamp {
                    Text(timestamp).foregroundStyle(CodexBridgePalette.secondaryText)
                }
                Text(role)
                    .font(.system(size: 10.5, weight: .semibold))
                    .foregroundStyle(CodexBridgePalette.primaryText)
                if kind != .user {
                    Text(kind == .codex ? "Codex" : "助手")
                        .font(.system(size: 8.5))
                        .foregroundStyle(CodexBridgePalette.secondaryText)
                        .padding(.horizontal, 5)
                        .padding(.vertical, 2)
                        .background(CodexBridgePalette.subtleFill, in: RoundedRectangle(cornerRadius: 4))
                    if let timestamp {
                        Spacer()
                        Text(timestamp).foregroundStyle(CodexBridgePalette.secondaryText)
                    }
                }
            }
            .font(.system(size: 9))

            content
                .padding(.horizontal, 13)
                .padding(.vertical, 11)
                .frame(maxWidth: 680, alignment: .leading)
                .background(bubbleBackground, in: bubbleShape)
                .overlay(bubbleShape.stroke(bubbleBorder, lineWidth: 1))
        }
        .frame(maxWidth: 680, alignment: kind == .user ? .trailing : .leading)
    }

    private var bubbleShape: RoundedRectangle {
        RoundedRectangle(cornerRadius: CodexBridgeRadius.large)
    }

    private var avatarColor: Color {
        switch kind {
        case .user: CodexBridgePalette.accent
        case .chat: CodexBridgePalette.chatGPT
        case .codex: CodexBridgePalette.codex
        }
    }

    private var avatarBackground: Color {
        kind == .user ? CodexBridgePalette.selectedFill : CodexBridgePalette.raisedSurface
    }

    private var bubbleBackground: Color {
        kind == .user ? CodexBridgePalette.selectedFill : CodexBridgePalette.surface
    }

    private var bubbleBorder: Color {
        kind == .user ? CodexBridgePalette.accent.opacity(0.18) : CodexBridgePalette.border
    }
}

struct ThreadSectionDivider: View {
    let title: String

    var body: some View {
        HStack(spacing: 11) {
            Rectangle().fill(CodexBridgePalette.border).frame(height: 1)
            Text(title)
                .font(.system(size: 9, weight: .semibold))
                .foregroundStyle(CodexBridgePalette.secondaryText)
            Rectangle().fill(CodexBridgePalette.border).frame(height: 1)
        }
        .padding(.vertical, 14)
    }
}

struct ConversationFileRow: View {
    let file: ConversationFile
    let isAvailable: Bool
    let showTurn: () -> Void
    let preview: () -> Void
    let reveal: () -> Void
    let choose: () -> Void

    var body: some View {
        HStack(spacing: 11) {
            Image(systemName: "doc")
                .font(.system(size: 15, weight: .medium))
                .foregroundStyle(CodexBridgePalette.codex)
                .frame(width: 34, height: 34)
                .background(CodexBridgePalette.raisedSurface, in: RoundedRectangle(cornerRadius: CodexBridgeRadius.small))
                .overlay(RoundedRectangle(cornerRadius: CodexBridgeRadius.small).stroke(CodexBridgePalette.border, lineWidth: 1))
            VStack(alignment: .leading, spacing: 3) {
                Text(file.name)
                    .font(.system(size: 11.5, weight: .semibold))
                    .lineLimit(1)
                Button("查看相关对话", action: showTurn)
                    .buttonStyle(.plain)
                    .font(.system(size: 9.5))
                    .foregroundStyle(CodexBridgePalette.secondaryText)
            }
            Spacer()
            if isAvailable {
                Button("预览", action: preview)
                    .buttonStyle(CodexBridgeSecondaryButtonStyle(compact: true))
                Button("在 Finder 中显示", action: reveal)
                    .buttonStyle(CodexBridgeSecondaryButtonStyle(compact: true))
                Menu {
                    Button("重新选择本机文件…", systemImage: "arrow.triangle.2.circlepath") { choose() }
                } label: {
                    Image(systemName: "ellipsis")
                }
                .menuStyle(.borderlessButton)
                .fixedSize()
                .help("更多文件操作")
            } else {
                Button("选择本机文件…", action: choose)
                    .buttonStyle(CodexBridgeSecondaryButtonStyle(compact: true))
            }
        }
        .padding(12)
        .background(CodexBridgePalette.surface, in: RoundedRectangle(cornerRadius: CodexBridgeRadius.large))
        .overlay(RoundedRectangle(cornerRadius: CodexBridgeRadius.large).stroke(CodexBridgePalette.border, lineWidth: 1))
        .padding(.bottom, 8)
    }
}

struct CodexBridgeInlineNotice: View {
    let title: String
    let message: String
    let color: Color
    let symbol: String

    var body: some View {
        HStack(alignment: .top, spacing: 11) {
            Image(systemName: symbol)
                .foregroundStyle(color)
                .padding(.top, 2)
            VStack(alignment: .leading, spacing: 4) {
                Text(title).font(.system(size: 11, weight: .semibold))
                Text(message)
                    .font(.system(size: 10))
                    .foregroundStyle(CodexBridgePalette.secondaryText)
                    .fixedSize(horizontal: false, vertical: true)
            }
            Spacer()
        }
        .padding(12)
        .background(color.opacity(0.055), in: RoundedRectangle(cornerRadius: CodexBridgeRadius.medium))
        .overlay(RoundedRectangle(cornerRadius: CodexBridgeRadius.medium).stroke(color.opacity(0.3), lineWidth: 1))
    }
}

struct DetailEmptyState: View {
    let symbol: String
    let title: String
    let message: String
    var actionTitle: String?
    var action: (() -> Void)?

    var body: some View {
        VStack(spacing: 9) {
            Image(systemName: symbol)
                .font(.system(size: 25, weight: .regular))
                .foregroundStyle(CodexBridgePalette.secondaryText)
            Text(title)
                .font(.system(size: 13, weight: .semibold))
                .foregroundStyle(CodexBridgePalette.primaryText)
            Text(message)
                .font(.system(size: 10.5))
                .foregroundStyle(CodexBridgePalette.secondaryText)
                .multilineTextAlignment(.center)
                .frame(maxWidth: 340)
            if let actionTitle, let action {
                Button(actionTitle, action: action)
                    .buttonStyle(CodexBridgeSecondaryButtonStyle(compact: true))
                    .padding(.top, 3)
            }
        }
        .padding(30)
        .frame(maxWidth: .infinity, maxHeight: .infinity)
    }
}

struct ArchiveConversationDialog: View {
    let conversation: CapturedConversation
    let cancel: () -> Void
    let confirm: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            Image(systemName: "archivebox")
                .font(.system(size: 24, weight: .medium))
                .foregroundStyle(CodexBridgePalette.warning)
            Text("仅在 Codex Bridge 中归档？")
                .font(.system(size: 17, weight: .bold))
            Text("「\(conversation.title)」将从全部会话和项目列表中隐藏，并移至已归档。不会影响 Codex 或 ChatGPT 中的原会话；消息、文件和引用仍会保留。")
                .font(.system(size: 12))
                .foregroundStyle(CodexBridgePalette.secondaryText)
                .fixedSize(horizontal: false, vertical: true)
            HStack {
                Spacer()
                Button("取消", action: cancel)
                    .buttonStyle(CodexBridgeSecondaryButtonStyle())
                Button("仅在此处归档", action: confirm)
                    .buttonStyle(CodexBridgePrimaryButtonStyle())
                    .keyboardShortcut(.defaultAction)
            }
            .padding(.top, 4)
        }
        .padding(20)
        .frame(width: 410)
        .background(CodexBridgePalette.surface, in: RoundedRectangle(cornerRadius: CodexBridgeRadius.large))
        .overlay(RoundedRectangle(cornerRadius: CodexBridgeRadius.large).stroke(CodexBridgePalette.border, lineWidth: 1))
        .shadow(color: .black.opacity(0.18), radius: 28, y: 14)
    }
}

struct ArchiveToast: View {
    let undo: () -> Void

    var body: some View {
        HStack(spacing: 10) {
            Image(systemName: "archivebox")
            Text("已在 Codex Bridge 中归档")
                .font(.system(size: 11.5, weight: .medium))
            Button("撤销", action: undo)
                .buttonStyle(.plain)
                .font(.system(size: 11, weight: .semibold))
                .padding(.horizontal, 8)
                .frame(height: 28)
                .background(CodexBridgePalette.surface.opacity(0.12), in: RoundedRectangle(cornerRadius: 5))
        }
        .foregroundStyle(CodexBridgePalette.surface)
        .padding(.horizontal, 14)
        .frame(height: 44)
        .background(CodexBridgePalette.primaryText, in: RoundedRectangle(cornerRadius: CodexBridgeRadius.medium))
        .shadow(color: .black.opacity(0.18), radius: 20, y: 9)
    }
}

extension View {
    /// Compatibility surface for existing sheets; the approved flat prototype removes blur materials.
    func codexbridgeGlass(radius: CGFloat = 0) -> some View {
        background(CodexBridgePalette.surface, in: RoundedRectangle(cornerRadius: radius))
            .overlay(RoundedRectangle(cornerRadius: radius).stroke(CodexBridgePalette.border, lineWidth: 1))
    }
}

struct CodexBridgeUserMessage: View, Equatable {
    let text: String

    private var request: String {
        guard text.hasPrefix("# Files mentioned by the user:"),
              let range = text.range(of: "## My request:\n") else { return text }
        return String(text[range.upperBound...])
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 9) {
            ConversationMarkdown(text: request)
            if request != text {
                DisclosureGroup("附件与原始消息") {
                    Text(text)
                        .font(.system(size: 10.5))
                        .foregroundStyle(CodexBridgePalette.secondaryText)
                        .textSelection(.enabled)
                }
                .font(.system(size: 10.5))
                .foregroundStyle(CodexBridgePalette.secondaryText)
            }
        }
    }
}

/// 保持段落换行；代码段使用等宽字体，不执行 HTML 或脚本。
struct ConversationMarkdown: View, Equatable {
    let text: String

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            ForEach(Array(text.components(separatedBy: "```").enumerated()), id: \.offset) { index, part in
                if index % 2 == 1 {
                    ScrollView(.horizontal) {
                        Text(part.trimmingCharacters(in: .newlines))
                            .font(.system(size: 11, design: .monospaced))
                            .textSelection(.enabled)
                            .padding(11)
                    }
                    .background(CodexBridgePalette.raisedSurface, in: RoundedRectangle(cornerRadius: CodexBridgeRadius.small))
                    .overlay(RoundedRectangle(cornerRadius: CodexBridgeRadius.small).stroke(CodexBridgePalette.border, lineWidth: 1))
                } else {
                    Text(
                        (try? AttributedString(
                            markdown: part,
                            options: .init(interpretedSyntax: .inlineOnlyPreservingWhitespace)
                        )) ?? AttributedString(part)
                    )
                    .font(.system(size: 12))
                    .lineSpacing(5)
                    .foregroundStyle(CodexBridgePalette.primaryText)
                    .textSelection(.enabled)
                }
            }
        }
    }
}
