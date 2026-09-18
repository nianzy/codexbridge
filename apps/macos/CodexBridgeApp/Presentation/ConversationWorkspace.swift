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
    @State private var isRefreshingConversations = false
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
            pruneStoredConversationIDs()
            ensureVisibleSelection()
        }
        .onChange(of: model.conversations.map(\.id)) { _, _ in
            pruneStoredConversationIDs()
            ensureVisibleSelection()
        }
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

                    Button {
                        refreshConversations()
                    } label: {
                        HStack(spacing: 6) {
                            if isRefreshingConversations {
                                ProgressView()
                                    .controlSize(.small)
                                    .frame(width: 12, height: 12)
                            } else {
                                Image(systemName: "arrow.clockwise")
                                    .font(.system(size: 11, weight: .semibold))
                            }
                            Text("刷新")
                        }
                        .font(.system(size: 10.5, weight: .semibold))
                        .foregroundStyle(CodexBridgePalette.secondaryText)
                        .padding(.horizontal, 10)
                        .frame(height: 32)
                        .background(CodexBridgePalette.raisedSurface, in: RoundedRectangle(cornerRadius: CodexBridgeRadius.small))
                        .overlay(RoundedRectangle(cornerRadius: CodexBridgeRadius.small).stroke(CodexBridgePalette.border, lineWidth: 1))
                    }
                    .buttonStyle(.plain)
                    .disabled(isRefreshingConversations)
                    .help("重新读取已保存的 ChatGPT 对话和 Codex 会话")

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
                            ConversationFileRow(
                                file: file,
                                isAvailable: model.availableLocalURL(for: file) != nil
                            ) {
                                revealTurnID = file.turnID
                                proxy.scrollTo(file.turnID, anchor: .top)
                            } preview: {
                                if let url = model.previewFile(file) { previewURL = url }
                            } reveal: {
                                model.revealFileInFinder(file)
                            } choose: {
                                Task { await model.chooseLocalFile(file) }
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

    private func refreshConversations() {
        guard !isRefreshingConversations else { return }
        isRefreshingConversations = true
        Task { @MainActor in
            await model.refreshAllConversations()
            isRefreshingConversations = false
            ensureVisibleSelection()
        }
    }

    private func pruneStoredConversationIDs() {
        let available = Set(model.conversations.map { $0.id.uuidString })
        pinnedStore = pinnedIDs.intersection(available).sorted().joined(separator: ",")
        archivedStore = archivedIDs.intersection(available).sorted().joined(separator: ",")
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
