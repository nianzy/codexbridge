import AppKit
import QuickLook
import SwiftUI

struct ConversationWorkspace: View {
    @Bindable var model: AppModel
    @State private var collectionScope: ConversationCollectionScope = .all
    @State private var typeFilter: ConversationTypeFilter = .all
    @State private var sortMode: ConversationSortMode = .recent
    @FocusState private var searchFocused: Bool
    @State private var revealTurnID: String?
    @State private var previewURL: URL?
    @State private var archiveCandidate: CapturedConversation?
    @State private var undoArchivedID: UUID?
    @State private var selectionAnchorTurnID: String?
    @State private var navigationColumnWidth: CGFloat = 232
    @State private var conversationColumnWidth: CGFloat = 360
    @AppStorage("codexbridge.pinned-conversation-ids") private var pinnedStore = ""
    @AppStorage("codexbridge.archived-conversation-ids") private var archivedStore = ""
    @AppStorage("codexbridge.navigation-column-width") private var savedNavigationColumnWidth = 232.0
    @AppStorage("codexbridge.conversation-column-width") private var savedConversationColumnWidth = 360.0

    var body: some View {
        GeometryReader { geometry in
            let handleWidth: CGFloat = 7
            let navigationMinimum: CGFloat = 190
            let navigationMaximum: CGFloat = 320
            let listMinimum: CGFloat = 300
            let listMaximum: CGFloat = 560
            let detailMinimum: CGFloat = 360
            let handleTotal = handleWidth * 2
            let navigationUpperBound = max(
                navigationMinimum,
                min(navigationMaximum, geometry.size.width - listMinimum - detailMinimum - handleTotal)
            )
            let navigationWidth = min(
                max(navigationColumnWidth, navigationMinimum),
                navigationUpperBound
            )
            let listUpperBound = max(
                listMinimum,
                min(listMaximum, geometry.size.width - navigationWidth - detailMinimum - handleTotal)
            )
            let listWidth = min(max(conversationColumnWidth, listMinimum), listUpperBound)

            HStack(spacing: 0) {
                navigationSidebar
                    .frame(width: navigationWidth)

                CodexBridgeColumnResizeHandle(
                    value: $navigationColumnWidth,
                    displayedWidth: navigationWidth,
                    allowedRange: navigationMinimum...navigationUpperBound,
                    label: "调整侧边栏宽度",
                    persist: { savedNavigationColumnWidth = Double($0) }
                )
                .frame(width: handleWidth)

                conversationList
                    .frame(width: listWidth)

                CodexBridgeColumnResizeHandle(
                    value: $conversationColumnWidth,
                    displayedWidth: listWidth,
                    allowedRange: listMinimum...listUpperBound,
                    label: "调整对话栏宽度",
                    persist: { savedConversationColumnWidth = Double($0) }
                )
                .frame(width: handleWidth)

                conversationDetail
                    .frame(minWidth: 0, maxWidth: .infinity, maxHeight: .infinity)
            }
            .background(CodexBridgePalette.surface)
        }
        .background(CodexBridgePalette.desktop)
        .overlay {
            if let conversation = archiveCandidate {
                Color.black.opacity(0.28)
                    .ignoresSafeArea()
                    .overlay {
                        ArchiveConversationDialog(
                            conversation: conversation,
                            cancel: { archiveCandidate = nil },
                            confirm: { archive(conversation) }
                        )
                    }
            }
        }
        .overlay(alignment: .bottom) {
            if let conversationID = undoArchivedID {
                ArchiveToast {
                    restore(conversationID)
                    undoArchivedID = nil
                }
                .padding(.bottom, 22)
                .transition(.move(edge: .bottom).combined(with: .opacity))
            }
        }
        .onAppear {
            navigationColumnWidth = CGFloat(savedNavigationColumnWidth)
            conversationColumnWidth = CGFloat(savedConversationColumnWidth)
            ensureVisibleSelection()
        }
        .onChange(of: model.conversations.count) { _, _ in ensureVisibleSelection() }
        .onChange(of: model.selectedConversationID) { _, _ in
            revealTurnID = nil
            selectionAnchorTurnID = nil
        }
        .quickLookPreview($previewURL)
    }

    private var navigationSidebar: some View {
        VStack(alignment: .leading, spacing: 0) {
            HStack(spacing: 8) {
                VStack(alignment: .leading, spacing: 1) {
                    Text("Codex Bridge")
                        .font(.system(size: 20, weight: .bold))
                    Text(CodexBridgeRelease.channel.uppercased())
                        .font(.system(size: 9, weight: .semibold))
                        .tracking(0.7)
                        .foregroundStyle(CodexBridgePalette.accent)
                }
                Spacer()
                Image(systemName: "square.stack.3d.up")
                    .font(.system(size: 17, weight: .semibold))
            }
            .foregroundStyle(CodexBridgePalette.primaryText)
            .padding(.horizontal, 17)
            .frame(height: 68)

            VStack(spacing: 4) {
                navigationButton(
                    title: "全部会话",
                    symbol: "bubble.left.and.text.bubble.right",
                    count: activeConversations.count,
                    scope: .all
                )
                navigationButton(
                    title: "已置顶",
                    symbol: "pin",
                    count: pinnedIDs.subtracting(archivedIDs).count,
                    scope: .pinned
                )
                navigationButton(
                    title: "已归档",
                    symbol: "archivebox",
                    count: archivedIDs.count,
                    scope: .archived
                )
            }
            .padding(.horizontal, 10)
            .padding(.top, 8)

            Text("项目")
                .font(.system(size: 10, weight: .semibold))
                .tracking(0.8)
                .foregroundStyle(CodexBridgePalette.secondaryText)
                .padding(.horizontal, 18)
                .padding(.top, 25)
                .padding(.bottom, 8)

            ScrollView {
                LazyVStack(spacing: 2) {
                    ForEach(projectGroups) { group in
                        Button {
                            collectionScope = .all
                            model.selectedProject = group.name
                            model.selectedScope = .projects
                            typeFilter = .all
                            selectFirstVisibleConversation()
                        } label: {
                            HStack(spacing: 10) {
                                Image(systemName: group.name == "未归类" ? "tray" : "folder")
                                    .font(.system(size: 15, weight: .medium))
                                    .frame(width: 20)
                                Text(group.name)
                                    .font(.system(size: 13.5, weight: .medium))
                                    .lineLimit(1)
                                Spacer(minLength: 6)
                                Text("\(group.conversations.count)")
                                    .font(.system(size: 10, weight: .medium).monospacedDigit())
                                    .foregroundStyle(CodexBridgePalette.secondaryText)
                            }
                            .foregroundStyle(CodexBridgePalette.primaryText)
                            .padding(.horizontal, 8)
                            .frame(height: 36)
                            .frame(maxWidth: .infinity)
                            .background(
                                model.selectedProject == group.name && collectionScope == .all
                                    ? CodexBridgePalette.selectedFill
                                    : Color.clear,
                                in: RoundedRectangle(cornerRadius: CodexBridgeRadius.small)
                            )
                            .contentShape(Rectangle())
                        }
                        .buttonStyle(.plain)
                    }
                }
                .padding(.horizontal, 10)
            }

            Spacer(minLength: 12)

            Button { model.isSourcesPresented = true } label: {
                HStack(spacing: 10) {
                    Text("S")
                        .font(.system(size: 10, weight: .bold))
                        .foregroundStyle(CodexBridgePalette.primaryText)
                        .frame(width: 26, height: 26)
                        .background(CodexBridgePalette.surface, in: Circle())
                        .overlay(Circle().stroke(CodexBridgePalette.border, lineWidth: 1))
                    Text("本地工作区")
                    .font(.system(size: 12.5, weight: .medium))
                    Spacer()
                    Text("设置")
                        .font(.system(size: 10))
                        .foregroundStyle(CodexBridgePalette.secondaryText)
                }
                .foregroundStyle(CodexBridgePalette.primaryText)
                .padding(.horizontal, 15)
                .frame(height: 52)
                .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            .overlay(alignment: .top) {
                Rectangle().fill(CodexBridgePalette.border).frame(height: 1)
            }
        }
        .background(CodexBridgePalette.sidebar)
    }

    private func navigationButton(
        title: String,
        symbol: String,
        count: Int,
        scope: ConversationCollectionScope
    ) -> some View {
        let selected = collectionScope == scope && model.selectedProject == nil
        return Button {
            collectionScope = scope
            model.selectedProject = nil
            selectFirstVisibleConversation()
        } label: {
            HStack(spacing: 10) {
                Image(systemName: symbol)
                    .font(.system(size: 16, weight: .medium))
                    .frame(width: 20)
                Text(title)
                    .font(.system(size: 13.5, weight: selected ? .semibold : .medium))
                Spacer(minLength: 6)
                Text("\(count)")
                    .font(.system(size: 10, weight: .medium).monospacedDigit())
            }
            .foregroundStyle(selected ? CodexBridgePalette.primaryText : CodexBridgePalette.secondaryText)
            .padding(.horizontal, 9)
            .frame(height: 38)
            .frame(maxWidth: .infinity)
            .background(selected ? CodexBridgePalette.selectedFill : Color.clear, in: RoundedRectangle(cornerRadius: CodexBridgeRadius.small))
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .accessibilityAddTraits(selected ? .isSelected : [])
    }

    private var conversationList: some View {
        VStack(spacing: 0) {
            VStack(spacing: 11) {
                HStack(spacing: 10) {
                    Text(scopeTitle)
                        .font(.system(size: 18, weight: .bold))
                        .lineLimit(1)
                    Spacer()

                    Menu {
                        ForEach(ConversationSortMode.allCases) { mode in
                            Button {
                                sortMode = mode
                                selectFirstVisibleConversation()
                            } label: {
                                if sortMode == mode {
                                    Label(mode.title, systemImage: "checkmark")
                                } else {
                                    Text(mode.title)
                                }
                            }
                        }
                    } label: {
                        HStack(spacing: 6) {
                            Image(systemName: sortMode.symbol)
                                .font(.system(size: 11, weight: .semibold))
                            Text(sortMode.shortTitle)
                                .lineLimit(1)
                            Image(systemName: "chevron.down")
                                .font(.system(size: 8, weight: .bold))
                        }
                        .font(.system(size: 10.5, weight: .semibold))
                        .foregroundStyle(CodexBridgePalette.secondaryText)
                        .padding(.horizontal, 10)
                        .frame(height: 32)
                        .background(CodexBridgePalette.raisedSurface, in: RoundedRectangle(cornerRadius: CodexBridgeRadius.small))
                        .overlay(RoundedRectangle(cornerRadius: CodexBridgeRadius.small).stroke(CodexBridgePalette.border, lineWidth: 1))
                    }
                    .menuStyle(.borderlessButton)
                    .menuIndicator(.hidden)
                    .fixedSize()
                    .help("调整会话排序")
                }

                HStack(spacing: 8) {
                    Image(systemName: "magnifyingglass")
                        .font(.system(size: 12, weight: .medium))
                        .foregroundStyle(searchFocused ? CodexBridgePalette.focus : CodexBridgePalette.secondaryText)
                    TextField("搜索标题、内容或项目", text: $model.searchText)
                        .textFieldStyle(.plain)
                        .font(.system(size: 11.5))
                        .focused($searchFocused)
                        .onSubmit(selectFirstVisibleConversation)
                    if !model.searchText.isEmpty {
                        Button {
                            model.searchText = ""
                            selectFirstVisibleConversation()
                        } label: {
                            Image(systemName: "xmark.circle.fill")
                                .font(.system(size: 12))
                        }
                        .buttonStyle(.plain)
                        .foregroundStyle(CodexBridgePalette.tertiaryText)
                        .help("清除搜索")
                    }
                    Button {
                        searchFocused = true
                    } label: {
                        Text("⌘K")
                            .font(.system(size: 8.5, weight: .semibold, design: .rounded))
                            .foregroundStyle(CodexBridgePalette.tertiaryText)
                            .padding(.horizontal, 6)
                            .frame(height: 20)
                            .background(CodexBridgePalette.surface, in: RoundedRectangle(cornerRadius: 4))
                            .overlay(RoundedRectangle(cornerRadius: 4).stroke(CodexBridgePalette.border, lineWidth: 1))
                    }
                    .buttonStyle(.plain)
                    .keyboardShortcut("k", modifiers: .command)
                    .help("聚焦搜索")
                }
                .padding(.horizontal, 11)
                .frame(height: 36)
                .background(CodexBridgePalette.canvas, in: RoundedRectangle(cornerRadius: CodexBridgeRadius.small))
                .overlay {
                    RoundedRectangle(cornerRadius: CodexBridgeRadius.small)
                        .stroke(searchFocused ? CodexBridgePalette.focus : CodexBridgePalette.border, lineWidth: searchFocused ? 1.5 : 1)
                }
            }
            .padding(.horizontal, 14)
            .padding(.vertical, 12)
            .background(CodexBridgePalette.surface)
            .overlay(alignment: .bottom) { Rectangle().fill(CodexBridgePalette.border).frame(height: 1) }

            HStack(spacing: 3) {
                typeTab("全部", symbol: "tray.full", filter: .all, count: scopedConversations.count)
                typeTab("Chat", symbol: "bubble.left.and.text.bubble.right", filter: .chat, count: chatCount)
                typeTab("Codex", symbol: "terminal", filter: .codex, count: codexCount)
            }
            .padding(4)
            .background(CodexBridgePalette.raisedSurface, in: RoundedRectangle(cornerRadius: CodexBridgeRadius.medium))
            .overlay(RoundedRectangle(cornerRadius: CodexBridgeRadius.medium).stroke(CodexBridgePalette.border, lineWidth: 1))
            .padding(.horizontal, 14)
            .padding(.vertical, 9)
            .background(CodexBridgePalette.surface)
            .overlay(alignment: .bottom) { Rectangle().fill(CodexBridgePalette.border).frame(height: 1) }

            if filteredConversations.isEmpty {
                VStack(spacing: 8) {
                    Image(systemName: model.searchText.isEmpty ? "bubble.left.and.bubble.right" : "magnifyingglass")
                        .font(.system(size: 23, weight: .regular))
                    Text(model.searchText.isEmpty ? "这里还没有会话" : "没有找到匹配的会话")
                        .font(.system(size: 12, weight: .semibold))
                    Text(model.searchText.isEmpty ? "保存 ChatGPT 对话或连接 Codex 后，会显示在这里。" : "请尝试其他标题或项目。")
                        .font(.system(size: 10.5))
                }
                .foregroundStyle(CodexBridgePalette.secondaryText)
                .frame(maxWidth: .infinity, maxHeight: .infinity)
                .background(CodexBridgePalette.surface)
            } else {
                ScrollView {
                    LazyVStack(spacing: 0) {
                        ForEach(filteredConversations) { conversation in
                            ConversationListRow(
                                conversation: conversation,
                                selected: model.selectedConversationID == conversation.id,
                                pinned: pinnedIDs.contains(conversation.id.uuidString)
                            ) {
                                model.selectedConversationID = conversation.id
                            }
                        }
                    }
                }
                .background(CodexBridgePalette.surface)
            }
        }
    }

    private func typeTab(
        _ title: String,
        symbol: String?,
        filter: ConversationTypeFilter,
        count: Int
    ) -> some View {
        let selected = typeFilter == filter
        return Button {
            typeFilter = filter
            model.selectedScope = switch filter {
            case .all: .projects
            case .chat: .chatGPT
            case .codex: .codex
            }
            selectFirstVisibleConversation()
        } label: {
            HStack(spacing: 6) {
                Image(systemName: symbol ?? "tray.full")
                    .font(.system(size: 11.5, weight: .semibold))
                Text(title)
                Text("\(count)")
                    .font(.system(size: 9, weight: .semibold).monospacedDigit())
                    .foregroundStyle(selected ? CodexBridgePalette.accent : CodexBridgePalette.secondaryText)
                    .monospacedDigit()
            }
            .font(.system(size: 11, weight: .semibold))
            .foregroundStyle(selected ? CodexBridgePalette.accent : CodexBridgePalette.secondaryText)
            .frame(maxWidth: .infinity)
            .frame(height: 34)
            .background(selected ? CodexBridgePalette.surface : Color.clear, in: RoundedRectangle(cornerRadius: CodexBridgeRadius.small))
            .overlay {
                RoundedRectangle(cornerRadius: CodexBridgeRadius.small)
                    .stroke(selected ? CodexBridgePalette.border : Color.clear, lineWidth: 1)
            }
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .accessibilityAddTraits(selected ? .isSelected : [])
    }

    @ViewBuilder
    private var conversationDetail: some View {
        if let conversation = model.selectedConversation {
            VStack(spacing: 0) {
                detailHeader(conversation)

                if let error = model.conversationDetailErrors[conversation.id], conversation.turns.isEmpty {
                    DetailEmptyState(
                        symbol: "exclamationmark.circle",
                        title: "对话内容暂不可用",
                        message: error,
                        actionTitle: "重试"
                    ) {
                        Task { await model.loadSelectedConversationDetails(force: true) }
                    }
                } else if model.loadingConversationID == conversation.id && conversation.turns.isEmpty {
                    CodexBridgeLoadingView(title: "正在读取对话…")
                        .frame(maxWidth: .infinity, maxHeight: .infinity)
                        .background(CodexBridgePalette.canvas)
                } else {
                    detailContent(conversation)
                    transferFooter(conversation)
                }
            }
            .background(CodexBridgePalette.canvas)
        } else {
            DetailEmptyState(
                symbol: "bubble.left.and.bubble.right",
                title: "选择一段会话",
                message: "查看对话与文件，并把需要的内容交给 ChatGPT 或 Codex。",
                actionTitle: "手动导入"
            ) {
                model.isManualCapturePresented = true
            }
            .background(CodexBridgePalette.canvas)
        }
    }

    private func detailHeader(_ conversation: CapturedConversation) -> some View {
        HStack(alignment: .top, spacing: 14) {
            VStack(alignment: .leading, spacing: 6) {
                HStack(spacing: 7) {
                    ConversationKindBadge(kind: conversation.sourceKind)
                    ConversationStateBadge(conversation: conversation)
                }
                Text(conversation.title)
                    .font(.system(size: 18, weight: .bold))
                    .lineLimit(1)
                HStack(spacing: 10) {
                    Label(conversation.projectName ?? "未归类", systemImage: "folder")
                    Text("更新于")
                    Text(conversation.updatedAt ?? conversation.capturedAt, style: .relative)
                    if pinnedIDs.contains(conversation.id.uuidString) {
                        Label("已置顶", systemImage: "pin")
                    }
                }
                .font(.system(size: 10))
                .foregroundStyle(CodexBridgePalette.secondaryText)
            }

            Spacer(minLength: 12)

            HStack(spacing: 7) {
                Button { togglePinned(conversation.id) } label: {
                    Image(systemName: pinnedIDs.contains(conversation.id.uuidString) ? "pin.fill" : "pin")
                }
                .buttonStyle(CodexBridgeIconButtonStyle())
                .help(pinnedIDs.contains(conversation.id.uuidString) ? "取消置顶" : "置顶")

                detailHeaderActionButtons(conversation)
            }
        }
        .padding(.horizontal, 18)
        .padding(.vertical, 15)
        .frame(minHeight: 96)
        .background(CodexBridgePalette.surface)
        .overlay(alignment: .bottom) { Rectangle().fill(CodexBridgePalette.border).frame(height: 1) }
    }

    @ViewBuilder
    private func detailHeaderActionButtons(_ conversation: CapturedConversation) -> some View {
        ViewThatFits(in: .horizontal) {
            HStack(spacing: 7) {
                if conversation.sourceKind == .codex {
                    Button {
                        model.openSelectedCodexTask()
                    } label: {
                        Label("在 ChatGPT 中打开", systemImage: "arrow.up.forward.app")
                    }
                    .buttonStyle(CodexBridgePrimaryButtonStyle(compact: true, controlHeight: 36))
                    .disabled(conversation.codexThreadID == nil)
                    .help("打开对应任务，不填写或发送内容")

                    Button {
                        Task { await model.loadSelectedConversationDetails(force: true) }
                    } label: {
                        Label("刷新内容", systemImage: "arrow.clockwise")
                    }
                    .buttonStyle(CodexBridgeSecondaryButtonStyle(compact: true, controlHeight: 36))

                    if conversation.runtimeStatus == .running {
                        Button {
                            Task { await model.interruptSelectedCodexTask() }
                        } label: {
                            Label("停止执行", systemImage: "stop.circle")
                        }
                        .buttonStyle(CodexBridgeSecondaryButtonStyle(compact: true, controlHeight: 36))
                    }
                }

                archiveButton(conversation, showsTitle: true)
            }

            HStack(spacing: 7) {
                if conversation.sourceKind == .codex {
                    Button { model.openSelectedCodexTask() } label: {
                        Image(systemName: "arrow.up.forward.app")
                    }
                    .buttonStyle(CodexBridgeIconButtonStyle(prominent: true))
                    .disabled(conversation.codexThreadID == nil)
                    .help("在 ChatGPT 中打开对应任务，不填写或发送内容")

                    Button {
                        Task { await model.loadSelectedConversationDetails(force: true) }
                    } label: {
                        Image(systemName: "arrow.clockwise")
                    }
                    .buttonStyle(CodexBridgeIconButtonStyle())
                    .help("刷新内容")

                    if conversation.runtimeStatus == .running {
                        Button {
                            Task { await model.interruptSelectedCodexTask() }
                        } label: {
                            Image(systemName: "stop.circle")
                        }
                        .buttonStyle(CodexBridgeIconButtonStyle())
                        .help("停止执行")
                    }
                }

                archiveButton(conversation, showsTitle: false)
            }
        }
    }

    @ViewBuilder
    private func archiveButton(_ conversation: CapturedConversation, showsTitle: Bool) -> some View {
        let isArchived = archivedIDs.contains(conversation.id.uuidString)
        if showsTitle {
            Button {
                if isArchived {
                    restore(conversation.id)
                } else {
                    archiveCandidate = conversation
                }
            } label: {
                Label(isArchived ? "恢复会话" : "在 Codex Bridge 中归档", systemImage: isArchived ? "arrow.uturn.backward" : "archivebox")
            }
            .buttonStyle(CodexBridgeSecondaryButtonStyle(compact: true, controlHeight: 36))
            .help(isArchived ? "恢复会话" : "仅在 Codex Bridge 中归档，不影响 ChatGPT 或 Codex 中的原会话")
        } else {
            Button {
                if isArchived {
                    restore(conversation.id)
                } else {
                    archiveCandidate = conversation
                }
            } label: {
                Image(systemName: isArchived ? "arrow.uturn.backward" : "archivebox")
            }
            .buttonStyle(CodexBridgeIconButtonStyle())
            .help(isArchived ? "恢复会话" : "仅在 Codex Bridge 中归档，不影响 ChatGPT 或 Codex 中的原会话")
        }
    }

    private func detailContent(_ conversation: CapturedConversation) -> some View {
        ScrollViewReader { proxy in
            ScrollView {
                LazyVStack(alignment: .leading, spacing: 0) {
                    ConversationSummaryCard(
                        conversation: conversation,
                        fileCount: model.conversationFiles.count
                    )
                    .padding(.bottom, 20)

                    if !conversation.warnings.isEmpty {
                        CodexBridgeInlineNotice(
                            title: "内容提示",
                            message: conversation.warnings.joined(separator: "\n"),
                            color: CodexBridgePalette.warning,
                            symbol: "exclamationmark.triangle"
                        )
                        .padding(.bottom, 18)
                    }

                    if conversation.turns.isEmpty {
                        DetailEmptyState(
                            symbol: conversation.sourceKind == .codex ? "terminal" : "bubble.left",
                            title: "还没有对话内容",
                            message: conversation.sourceKind == .codex ? "从 Codex 读取后会显示在这里。" : "请从 ChatGPT 重新保存这段对话。"
                        )
                        .frame(minHeight: 300)
                    } else {
                        TurnSelectionToolbar(
                            selectedCount: model.selectedTurnIDs.count,
                            canSelectFive: conversation.turns.count > 3,
                            selectRecent: { count in
                                model.selectRecentTurns(count)
                                selectionAnchorTurnID = conversation.turns.last?.id
                            },
                            selectComplete: {
                                model.selectCompleteTurns()
                                selectionAnchorTurnID = conversation.turns.last(where: \.complete)?.id
                            },
                            clear: {
                                model.clearTurnSelection()
                                selectionAnchorTurnID = nil
                            }
                        )
                        .padding(.bottom, 12)

                        ForEach(conversation.turns) { turn in
                            RoundSelectionDivider(
                                index: turn.index,
                                selected: model.selectedTurnIDs.contains(turn.id),
                                complete: turn.complete
                            ) { extendsSelection in
                                selectTurn(
                                    turn.id,
                                    in: conversation,
                                    extendingFromAnchor: extendsSelection
                                )
                            }

                            ConversationMessageRow(
                                role: "你",
                                kind: .user,
                                timestamp: nil,
                                content: { CodexBridgeUserMessage(text: turn.user.text).equatable() }
                            )

                            ConversationMessageRow(
                                role: conversation.sourceKind == .codex ? "Codex" : "ChatGPT",
                                kind: conversation.sourceKind == .codex ? .codex : .chat,
                                timestamp: turn.complete ? "已完成" : "尚未完成",
                                content: {
                                    ConversationMarkdown(text: turn.assistant?.text ?? "还没有回复").equatable()
                                }
                            )
                            .padding(.bottom, 8)
                            .id(turn.id)
                        }
                    }

                    if !model.conversationFiles.isEmpty {
                        ThreadSectionDivider(title: "相关文件")
                            .padding(.top, 5)
                        ForEach(model.conversationFiles) { file in
                            ConversationFileRow(file: file) {
                                revealTurnID = file.turnID
                                proxy.scrollTo(file.turnID, anchor: .top)
                            } preview: {
                                if let url = file.localURL { previewURL = url }
                            } locate: {
                                Task { await model.locateFile(file) }
                            }
                        }
                    }
                }
                .padding(.horizontal, 22)
                .padding(.top, 20)
                .padding(.bottom, 28)
                .frame(maxWidth: .infinity, alignment: .leading)
            }
            .background(CodexBridgePalette.canvas)
            .onChange(of: revealTurnID) { _, id in
                if let id { proxy.scrollTo(id, anchor: .top) }
            }
        }
    }

    private func transferFooter(_ conversation: CapturedConversation) -> some View {
        HStack(spacing: 9) {
            HStack(spacing: 10) {
                CodexBridgeSelectionCheckbox(selected: !model.selectedTurnIDs.isEmpty, size: 20)
                VStack(alignment: .leading, spacing: 1) {
                    Text("已选 \(model.selectedTurnIDs.count) 轮")
                        .font(.system(size: 11, weight: .semibold))
                        .foregroundStyle(CodexBridgePalette.primaryText)
                    Text(model.selectedTurnIDs.isEmpty ? "选择要交接的对话" : "只会带上已选内容")
                        .font(.system(size: 9))
                        .foregroundStyle(CodexBridgePalette.secondaryText)
                }
                Spacer(minLength: 4)
                Button(model.allTurnsSelected ? "取消全选" : "全选") {
                    model.selectAllTurns()
                }
                .buttonStyle(.plain)
                .font(.system(size: 10, weight: .medium))
                .foregroundStyle(CodexBridgePalette.secondaryText)
                .disabled(conversation.turns.isEmpty)
            }
            .padding(.horizontal, 11)
            .frame(height: 44)
            .background(CodexBridgePalette.canvas, in: RoundedRectangle(cornerRadius: CodexBridgeRadius.medium))
            .overlay(RoundedRectangle(cornerRadius: CodexBridgeRadius.medium).stroke(CodexBridgePalette.border, lineWidth: 1))

            Button {
                Task {
                    if conversation.sourceKind == .codex {
                        await model.beginChatGPTDraft()
                    } else {
                        await model.beginHandoff()
                    }
                }
            } label: {
                HStack(spacing: 7) {
                    Text(conversation.sourceKind == .codex ? "交给 ChatGPT" : "交给 Codex")
                    Image(systemName: "arrow.up")
                }
            }
            .buttonStyle(CodexBridgePrimaryButtonStyle(controlHeight: 44))
            .disabled(model.selectedTurnIDs.isEmpty || model.isBusy || model.loadingConversationID != nil)
        }
        .padding(.horizontal, 14)
        .padding(.vertical, 12)
        .background(CodexBridgePalette.surface)
        .overlay(alignment: .top) { Rectangle().fill(CodexBridgePalette.border).frame(height: 1) }
    }

    private var pinnedIDs: Set<String> {
        Set(pinnedStore.split(separator: ",").map(String.init))
    }

    private var archivedIDs: Set<String> {
        Set(archivedStore.split(separator: ",").map(String.init))
    }

    private var activeConversations: [CapturedConversation] {
        model.conversations.filter { !archivedIDs.contains($0.id.uuidString) }
    }

    private var projectGroups: [ConversationProjectGroup] {
        ConversationProjectGroup.grouped(activeConversations)
    }

    private var scopedConversations: [CapturedConversation] {
        model.conversations.filter { conversation in
            let identifier = conversation.id.uuidString
            let matchesCollection = switch collectionScope {
            case .all: !archivedIDs.contains(identifier)
            case .pinned: pinnedIDs.contains(identifier) && !archivedIDs.contains(identifier)
            case .archived: archivedIDs.contains(identifier)
            }
            let matchesProject = model.selectedProject == nil || conversationProjectName(conversation) == model.selectedProject
            let matchesSearch = model.searchText.isEmpty
                || conversation.title.localizedCaseInsensitiveContains(model.searchText)
                || conversationProjectName(conversation).localizedCaseInsensitiveContains(model.searchText)
                || conversation.turns.contains {
                    $0.user.text.localizedCaseInsensitiveContains(model.searchText)
                        || ($0.assistant?.text.localizedCaseInsensitiveContains(model.searchText) ?? false)
                }
            return matchesCollection && matchesProject && matchesSearch
        }
        .sorted(by: sortConversations)
    }

    private var filteredConversations: [CapturedConversation] {
        scopedConversations.filter { conversation in
            switch typeFilter {
            case .all: true
            case .chat: conversation.sourceKind != .codex
            case .codex: conversation.sourceKind == .codex
            }
        }
    }

    private var chatCount: Int { scopedConversations.filter { $0.sourceKind != .codex }.count }
    private var codexCount: Int { scopedConversations.filter { $0.sourceKind == .codex }.count }

    private var scopeTitle: String {
        if let project = model.selectedProject { return project }
        return switch collectionScope {
        case .all: "全部会话"
        case .pinned: "已置顶"
        case .archived: "已归档"
        }
    }

    private func conversationProjectName(_ conversation: CapturedConversation) -> String {
        if let name = conversation.projectName?.trimmingCharacters(in: .whitespacesAndNewlines), !name.isEmpty {
            return name
        }
        if let path = conversation.projectPath, !path.isEmpty {
            return URL(fileURLWithPath: path).lastPathComponent
        }
        return "未归类"
    }

    private func sortConversations(_ lhs: CapturedConversation, _ rhs: CapturedConversation) -> Bool {
        switch sortMode {
        case .recent:
            (lhs.updatedAt ?? lhs.capturedAt) > (rhs.updatedAt ?? rhs.capturedAt)
        case .oldest:
            (lhs.updatedAt ?? lhs.capturedAt) < (rhs.updatedAt ?? rhs.capturedAt)
        case .title:
            lhs.title.localizedStandardCompare(rhs.title) == .orderedAscending
        }
    }

    private func selectFirstVisibleConversation() {
        DispatchQueue.main.async {
            if !filteredConversations.contains(where: { $0.id == model.selectedConversationID }) {
                model.selectedConversationID = filteredConversations.first?.id
            }
        }
    }

    private func ensureVisibleSelection() {
        if !filteredConversations.contains(where: { $0.id == model.selectedConversationID }) {
            model.selectedConversationID = filteredConversations.first?.id
        }
    }

    private func togglePinned(_ conversationID: UUID) {
        var values = pinnedIDs
        let value = conversationID.uuidString
        if values.contains(value) { values.remove(value) } else { values.insert(value) }
        pinnedStore = values.sorted().joined(separator: ",")
    }

    private func archive(_ conversation: CapturedConversation) {
        var archived = archivedIDs
        archived.insert(conversation.id.uuidString)
        archivedStore = archived.sorted().joined(separator: ",")

        var pinned = pinnedIDs
        pinned.remove(conversation.id.uuidString)
        pinnedStore = pinned.sorted().joined(separator: ",")

        archiveCandidate = nil
        undoArchivedID = conversation.id
        selectFirstVisibleConversation()
        Task { @MainActor in
            try? await Task.sleep(for: .seconds(5))
            if undoArchivedID == conversation.id { undoArchivedID = nil }
        }
    }

    private func restore(_ conversationID: UUID) {
        var values = archivedIDs
        values.remove(conversationID.uuidString)
        archivedStore = values.sorted().joined(separator: ",")
        collectionScope = .all
        model.selectedProject = nil
        model.selectedConversationID = conversationID
    }

    private func selectTurn(
        _ turnID: String,
        in conversation: CapturedConversation,
        extendingFromAnchor: Bool
    ) {
        if extendingFromAnchor, let anchorID = selectionAnchorTurnID {
            model.selectTurnRange(from: anchorID, through: turnID)
        } else {
            model.toggleTurn(turnID)
        }
        selectionAnchorTurnID = turnID
    }
}

private enum ConversationCollectionScope {
    case all
    case pinned
    case archived
}

private enum ConversationTypeFilter {
    case all
    case chat
    case codex
}

private enum ConversationSortMode: String, CaseIterable, Identifiable {
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

private struct ConversationListRow: View {
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

private struct ConversationTypeIcon: View {
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

private struct ConversationKindBadge: View {
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

private struct ConversationStateBadge: View {
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

private struct ConversationRowState: View {
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

private struct ConversationVisualState {
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

private struct ConversationSummaryCard: View {
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

private struct RoundSelectionDivider: View {
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

private struct TurnSelectionToolbar: View {
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

private struct CodexBridgeColumnResizeHandle: View {
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

private struct CodexBridgeSelectionCheckbox: View {
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

private enum MessageKind { case user, chat, codex }

private struct ConversationMessageRow<Content: View>: View {
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

private struct ThreadSectionDivider: View {
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

private struct ConversationFileRow: View {
    let file: ConversationFile
    let showTurn: () -> Void
    let preview: () -> Void
    let locate: () -> Void

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
            if file.localURL != nil {
                Button("预览", action: preview)
                    .buttonStyle(CodexBridgeSecondaryButtonStyle(compact: true))
            }
            Button(file.localPath == nil ? "选择文件…" : "重新定位…", action: locate)
                .buttonStyle(.plain)
                .font(.system(size: 10, weight: .medium))
                .foregroundStyle(CodexBridgePalette.secondaryText)
        }
        .padding(12)
        .background(CodexBridgePalette.surface, in: RoundedRectangle(cornerRadius: CodexBridgeRadius.large))
        .overlay(RoundedRectangle(cornerRadius: CodexBridgeRadius.large).stroke(CodexBridgePalette.border, lineWidth: 1))
        .padding(.bottom, 8)
    }
}

private struct CodexBridgeInlineNotice: View {
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

private struct DetailEmptyState: View {
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

private struct ArchiveConversationDialog: View {
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

private struct ArchiveToast: View {
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

private struct CodexBridgeUserMessage: View, Equatable {
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
private struct ConversationMarkdown: View, Equatable {
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
