import SwiftUI

struct HandoffEditorView: View {
    @Bindable var model: AppModel
    @Environment(\.dismiss) private var dismiss
    @State private var showsPayload = false
    @State private var expandedSelector: TransferSelector?

    private var selectedModel: CodexModelOption? {
        model.models.first { $0.id == model.activeDraft?.modelID }
    }

    private var effortOptions: [String] {
        selectedModel?.supportedReasoningEfforts ?? []
    }

    private var frozen: FrozenHandoff? {
        guard let conversation = model.transferConversation, var draft = model.activeDraft else { return nil }
        if draft.instruction.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
            draft.instruction = "请基于以下选中的对话继续完成任务。"
        }
        draft.attachments = model.transferAttachments
        return try? ReferenceSerializer().freeze(conversation: conversation, draft: draft)
    }
    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            TransferHeading(
                title: "交给 Codex",
                subtitle: "\(model.activeDraft?.selectedTurnIDs.count ?? 0) 轮对话 · 新建任务并填入草稿，由你发送"
            )
            ScrollViewReader { proxy in
                ScrollView {
                    VStack(alignment: .leading, spacing: 18) {
                        TransferSection(title: "任务要求", symbol: "text.alignleft") {
                            TextField("例如：根据这些讨论，实现第一版", text: Binding(get: { model.activeDraft?.instruction ?? "" }, set: { model.activeDraft?.instruction = $0 }), axis: .vertical)
                                .lineLimit(3...5)
                                .textFieldStyle(.plain)
                                .padding(14)
                                .codexBridgeSurface()
                        }

                    TransferSection(title: "工作目录", symbol: "folder") {
                        workspaceSelectionDropdown
                    }
                    .id(TransferSelector.workspace)

                    TransferSection(title: "模型设置", symbol: "slider.horizontal.3") {
                        TransferSelectionDropdown(
                            title: "模型",
                            symbol: "cpu",
                            value: selectedModel?.displayName ?? "选择模型",
                            detail: selectedModel.map(modelDescription) ?? "选择本次任务使用的模型",
                            groupTitle: "可用模型",
                            options: model.models.map { ($0.id, $0.displayName, modelDescription($0)) },
                            selection: model.activeDraft?.modelID,
                            isExpanded: expandedSelector == .model,
                            onToggle: { toggleSelector(.model) },
                            action: {
                                model.updateSelectedModel($0)
                                closeSelector()
                            }
                        )

                        TransferSelectionDropdown(
                            title: "思考强度",
                            symbol: "brain.head.profile",
                            value: effortDisplayName(model.activeDraft?.reasoningEffort),
                            detail: "影响推理深度与响应速度",
                            groupTitle: "思考强度",
                            options: effortOptions.map { ($0, effortDisplayName($0), effortDescription($0)) },
                            selection: model.activeDraft?.reasoningEffort,
                            isExpanded: expandedSelector == .reasoning,
                            onToggle: { toggleSelector(.reasoning) },
                            action: {
                                model.activeDraft?.reasoningEffort = $0
                                closeSelector()
                            }
                        )
                        .id(TransferSelector.reasoning)
                    }
                    .id(TransferSelector.model)

                    if model.models.isEmpty {
                        HStack {
                            Text("请先连接 Codex，获取可用模型。").font(.caption).foregroundStyle(.secondary)
                            Button("重新连接") { Task { await model.refreshCodexConnection() } }
                        }
                    }
                    TransferFileChoices(model: model)
                    DisclosureGroup("预览即将填入的内容", isExpanded: $showsPayload) {
                        Text(frozen.map { String(decoding: $0.bytes, as: UTF8.self) } ?? "选择工作目录和模型后显示完整内容。")
                            .font(.body)
                            .lineSpacing(4)
                            .textSelection(.enabled)
                            .frame(maxWidth: .infinity, alignment: .leading)
                            .padding(16)
                            .codexBridgeSurface()
                    }
                    }
                    .padding(26)
                }
                .onChange(of: expandedSelector) { _, selector in
                    guard let selector else { return }
                    Task { @MainActor in
                        try? await Task.sleep(for: .milliseconds(170))
                        withAnimation(.easeInOut(duration: 0.18)) {
                            proxy.scrollTo(selector, anchor: .top)
                        }
                    }
                }
            }
            HStack {
                Label("只填入草稿，不会自动发送", systemImage: "checkmark.shield")
                    .font(.caption)
                    .foregroundStyle(.secondary)
                Spacer()
                Button("取消") { dismiss() }.buttonStyle(CodexBridgeSecondaryButtonStyle())
                Button(model.isBusy ? "正在创建…" : "新建任务并填入") { Task { await model.submitHandoff() } }
                    .buttonStyle(CodexBridgePrimaryButtonStyle())
                    .disabled(frozen == nil || model.isBusy || !model.attachmentLoadingIDs.isEmpty)
            }.padding(22).codexbridgeGlass()
        }.background(CodexBridgePalette.canvas).frame(width: 700, height: 700)
            .interactiveDismissDisabled(model.isBusy).disabled(model.isBusy)
            .overlay { if let error = model.errorMessage { CodexBridgeErrorDialog(message: error, dismiss: model.dismissError) } }
    }

    private var workspaceSelectionDropdown: some View {
        let selectedPath = model.activeDraft?.workspacePath
        return TransferDropdownField(
            symbol: "folder",
            value: selectedPath.map { URL(fileURLWithPath: $0).lastPathComponent } ?? "选择工作目录",
            detail: selectedPath ?? "从已有项目中选择，或使用其他文件夹",
            isPlaceholder: selectedPath == nil,
            isExpanded: expandedSelector == .workspace,
            onToggle: { toggleSelector(.workspace) }
        ) {
            TransferDropdownPanel {
                TransferDropdownGroupTitle("已有项目")

                if model.existingWorkspacePaths.isEmpty {
                    TransferDropdownEmptyState(
                        title: "暂无已有项目",
                        detail: "选择一个文件夹后，它会出现在这里。"
                    )
                } else {
                    ForEach(model.existingWorkspacePaths, id: \.self) { path in
                        TransferDropdownOptionRow(
                            title: URL(fileURLWithPath: path).lastPathComponent,
                            detail: path,
                            symbol: "folder",
                            isSelected: path == selectedPath
                        ) {
                            model.selectWorkspace(path)
                            closeSelector()
                        }
                    }
                }

                TransferDropdownDivider()
                TransferDropdownGroupTitle("其他位置")

                TransferDropdownActionRow(
                    title: "选择其他文件夹…",
                    detail: "选择这台 Mac 上已有的目录",
                    symbol: "folder.badge.plus"
                ) {
                    closeSelector()
                    model.chooseWorkspace()
                }

                TransferDropdownActionRow(
                    title: "新建项目…",
                    detail: "创建新目录并用于本次任务",
                    symbol: "plus"
                ) {
                    closeSelector()
                    model.createWorkspace()
                }
            }
        }
    }

    private func toggleSelector(_ selector: TransferSelector) {
        withAnimation(.easeInOut(duration: 0.16)) {
            expandedSelector = expandedSelector == selector ? nil : selector
        }
    }

    private func closeSelector() {
        withAnimation(.easeInOut(duration: 0.16)) {
            expandedSelector = nil
        }
    }

    private func effortDisplayName(_ effort: String?) -> String {
        switch effort {
        case "none": "关闭"
        case "minimal": "最少"
        case "low": "低"
        case "medium": "中"
        case "high": "高"
        case "xhigh": "很高"
        case "max": "最高"
        case "ultra": "极致"
        case let value?: value
        case nil: "请选择"
        }
    }

    private func modelDescription(_ option: CodexModelOption) -> String {
        option.isDefault ? "Codex 推荐模型" : "可用于本次 Codex 任务"
    }

    private func effortDescription(_ effort: String) -> String {
        switch effort {
        case "none": "直接响应，不进行额外推理"
        case "minimal": "适合简单、明确的修改"
        case "low": "速度优先，适合日常任务"
        case "medium": "速度与推理深度平衡"
        case "high": "适合复杂实现与问题分析"
        case "xhigh": "更深入地规划和验证"
        case "max": "使用最高可用推理强度"
        case "ultra": "用于最复杂、要求最高的任务"
        default: "设置本次任务的推理深度"
        }
    }
}

private enum TransferSelector: Hashable {
    case workspace
    case model
    case reasoning
}

private struct TransferSection<Content: View>: View {
    let title: String
    let symbol: String
    @ViewBuilder let content: Content

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Label(title, systemImage: symbol)
                .font(.system(size: 13, weight: .semibold))
                .foregroundStyle(CodexBridgePalette.primaryText)
            content
        }
        .padding(16)
        .codexbridgeGlass(radius: CodexBridgeRadius.large)
    }
}

private struct TransferSelectionDropdown: View {
    let title: String
    let symbol: String
    let value: String
    let detail: String
    let groupTitle: String
    let options: [(id: String, title: String, detail: String)]
    let selection: String?
    let isExpanded: Bool
    let onToggle: () -> Void
    let action: (String) -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 7) {
            Label(title, systemImage: symbol)
                .font(.caption.weight(.medium))
                .foregroundStyle(CodexBridgePalette.secondaryText)

            TransferDropdownField(
                symbol: symbol,
                value: value,
                detail: detail,
                isPlaceholder: selection == nil,
                isExpanded: isExpanded,
                onToggle: onToggle
            ) {
                TransferDropdownPanel {
                    TransferDropdownGroupTitle(groupTitle)

                    if options.isEmpty {
                        TransferDropdownEmptyState(
                            title: "暂无可用选项",
                            detail: "请重新连接 Codex 后再试。"
                        )
                    } else {
                        ForEach(options, id: \.id) { option in
                            TransferDropdownOptionRow(
                                title: option.title,
                                detail: option.detail,
                                symbol: symbol,
                                isSelected: option.id == selection
                            ) {
                                action(option.id)
                            }
                        }
                    }
                }
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }
}

private struct TransferDropdownField<Content: View>: View {
    let symbol: String
    let value: String
    let detail: String
    let isPlaceholder: Bool
    let isExpanded: Bool
    let onToggle: () -> Void
    @ViewBuilder let content: Content

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            Button(action: onToggle) {
                TransferDropdownTrigger(
                    symbol: symbol,
                    value: value,
                    detail: detail,
                    isPlaceholder: isPlaceholder,
                    isExpanded: isExpanded
                )
            }
            .buttonStyle(.plain)
            .accessibilityLabel(value)
            .accessibilityHint(isExpanded ? "收起选项" : "展开选项")
            .accessibilityValue(isExpanded ? "已展开" : "已收起")

            if isExpanded {
                content
                    .transition(.opacity.combined(with: .move(edge: .top)))
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }
}

private struct TransferDropdownPanel<Content: View>: View {
    @ViewBuilder let content: Content

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 2) {
                content
            }
            .frame(maxWidth: .infinity, alignment: .leading)
        }
        .frame(maxHeight: 310)
        .padding(8)
        .background(CodexBridgePalette.surface, in: RoundedRectangle(cornerRadius: 14, style: .continuous))
        .overlay {
            RoundedRectangle(cornerRadius: 14, style: .continuous)
                .stroke(CodexBridgePalette.focus, lineWidth: 1.5)
        }
        .shadow(color: .black.opacity(0.14), radius: 18, y: 8)
    }
}

private struct TransferDropdownGroupTitle: View {
    let title: String

    init(_ title: String) {
        self.title = title
    }

    var body: some View {
        Text(title)
            .font(.system(size: 11, weight: .medium))
            .foregroundStyle(CodexBridgePalette.secondaryText)
            .padding(.horizontal, 10)
            .padding(.top, 8)
            .padding(.bottom, 5)
    }
}

private struct TransferDropdownDivider: View {
    var body: some View {
        Rectangle()
            .fill(CodexBridgePalette.border)
            .frame(height: 1)
            .padding(.horizontal, 4)
            .padding(.vertical, 7)
    }
}

private struct TransferDropdownEmptyState: View {
    let title: String
    let detail: String

    var body: some View {
        VStack(alignment: .leading, spacing: 3) {
            Text(title)
                .font(.system(size: 13, weight: .medium))
                .foregroundStyle(CodexBridgePalette.primaryText)
            Text(detail)
                .font(.caption)
                .foregroundStyle(CodexBridgePalette.secondaryText)
        }
        .padding(.horizontal, 10)
        .padding(.vertical, 12)
    }
}

private struct TransferDropdownOptionRow: View {
    let title: String
    let detail: String
    let symbol: String
    let isSelected: Bool
    let action: () -> Void
    @State private var isHovering = false

    var body: some View {
        Button(action: action) {
            HStack(spacing: 12) {
                Image(systemName: symbol)
                    .font(.system(size: 15, weight: .regular))
                    .foregroundStyle(CodexBridgePalette.secondaryText)
                    .frame(width: 18)

                VStack(alignment: .leading, spacing: 2) {
                    Text(title)
                        .font(.system(size: 14, weight: isSelected ? .semibold : .medium))
                        .foregroundStyle(CodexBridgePalette.primaryText)
                        .lineLimit(1)
                    Text(detail)
                        .font(.system(size: 11))
                        .foregroundStyle(CodexBridgePalette.secondaryText)
                        .lineLimit(1)
                        .truncationMode(.middle)
                }

                Spacer(minLength: 10)

                Image(systemName: "checkmark")
                    .font(.system(size: 12, weight: .semibold))
                    .foregroundStyle(CodexBridgePalette.primaryText)
                    .opacity(isSelected ? 1 : 0)
            }
            .padding(.horizontal, 10)
            .frame(maxWidth: .infinity, minHeight: 58, alignment: .leading)
            .background(
                rowBackground,
                in: RoundedRectangle(cornerRadius: 10, style: .continuous)
            )
            .contentShape(RoundedRectangle(cornerRadius: 10, style: .continuous))
        }
        .buttonStyle(.plain)
        .onHover { isHovering = $0 }
        .accessibilityLabel(title)
        .accessibilityHint(detail)
        .accessibilityValue(isSelected ? "已选择" : "")
    }

    private var rowBackground: Color {
        if isSelected { return CodexBridgePalette.success.opacity(0.11) }
        if isHovering { return CodexBridgePalette.subtleFill }
        return .clear
    }
}

private struct TransferDropdownActionRow: View {
    let title: String
    let detail: String
    let symbol: String
    let action: () -> Void
    @State private var isHovering = false

    var body: some View {
        Button(action: action) {
            HStack(spacing: 12) {
                Image(systemName: symbol)
                    .font(.system(size: 15, weight: .regular))
                    .foregroundStyle(CodexBridgePalette.secondaryText)
                    .frame(width: 18)
                VStack(alignment: .leading, spacing: 2) {
                    Text(title)
                        .font(.system(size: 14, weight: .medium))
                        .foregroundStyle(CodexBridgePalette.primaryText)
                    Text(detail)
                        .font(.system(size: 11))
                        .foregroundStyle(CodexBridgePalette.secondaryText)
                }
                Spacer(minLength: 10)
                Image(systemName: "chevron.right")
                    .font(.system(size: 10, weight: .semibold))
                    .foregroundStyle(CodexBridgePalette.tertiaryText)
            }
            .padding(.horizontal, 10)
            .frame(maxWidth: .infinity, minHeight: 54, alignment: .leading)
            .background(
                isHovering ? CodexBridgePalette.subtleFill : .clear,
                in: RoundedRectangle(cornerRadius: 10, style: .continuous)
            )
            .contentShape(RoundedRectangle(cornerRadius: 10, style: .continuous))
        }
        .buttonStyle(.plain)
        .onHover { isHovering = $0 }
        .accessibilityLabel(title)
        .accessibilityHint(detail)
    }
}

private struct TransferDropdownTrigger: View {
    let symbol: String
    let value: String
    var detail: String?
    var isPlaceholder = false
    var isExpanded = false

    @Environment(\.isEnabled) private var isEnabled
    @State private var isHovering = false

    var body: some View {
        HStack(spacing: 10) {
            Image(systemName: symbol)
                .font(.system(size: 15, weight: .medium))
                .foregroundStyle(CodexBridgePalette.secondaryText)
                .frame(width: 18)

            VStack(alignment: .leading, spacing: 2) {
                Text(value)
                    .font(.system(size: 13, weight: .medium))
                    .foregroundStyle(isPlaceholder ? CodexBridgePalette.secondaryText : CodexBridgePalette.primaryText)
                    .lineLimit(1)

                if let detail {
                    Text(detail)
                        .font(.caption2)
                        .foregroundStyle(CodexBridgePalette.secondaryText)
                        .lineLimit(1)
                        .truncationMode(.middle)
                }
            }

            Spacer(minLength: 8)

            Image(systemName: isExpanded ? "chevron.up" : "chevron.down")
                .font(.system(size: 10, weight: .semibold))
                .foregroundStyle(CodexBridgePalette.tertiaryText)
        }
        .padding(.horizontal, 12)
        .frame(maxWidth: .infinity, minHeight: detail == nil ? 44 : 52, alignment: .leading)
        .background(
            isHovering && isEnabled ? CodexBridgePalette.subtleFill : CodexBridgePalette.canvas,
            in: RoundedRectangle(cornerRadius: 11, style: .continuous)
        )
        .overlay {
            RoundedRectangle(cornerRadius: 11, style: .continuous)
                .stroke(
                    isExpanded ? CodexBridgePalette.focus : (isHovering && isEnabled ? CodexBridgePalette.tertiaryText : CodexBridgePalette.border),
                    lineWidth: isExpanded ? 1.5 : 1
                )
        }
        .shadow(color: isExpanded ? CodexBridgePalette.focus.opacity(0.16) : .clear, radius: 0, x: 0, y: 0)
        .contentShape(RoundedRectangle(cornerRadius: 11, style: .continuous))
        .opacity(isEnabled ? 1 : 0.38)
        .onHover { isHovering = $0 }
    }
}

struct SimpleChatGPTDraftEditor: View {
    @Bindable var model: AppModel
    @Environment(\.dismiss) private var dismiss
    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            TransferHeading(title: "交给 ChatGPT", subtitle: "\(model.activeChatGPTDraft?.selectedTurnIDs.count ?? 0) 轮对话 · 先填入草稿，再由你发送")
            ScrollView {
                VStack(alignment: .leading, spacing: 20) {
                    TextField("补充说明（可选）", text: $model.chatGPTInstruction, axis: .vertical)
                        .lineLimit(2...4).textFieldStyle(.plain).padding(14).codexBridgeSurface()
                        .onChange(of: model.chatGPTInstruction) { _, _ in model.rebuildChatGPTDraft() }
                    TransferFileChoices(model: model)
                    Text("即将填入的内容").font(.callout.weight(.medium))
                    Text(model.activeChatGPTDraft?.content ?? "")
                        .font(.body).lineSpacing(5).textSelection(.enabled)
                        .frame(maxWidth: .infinity, alignment: .leading).padding(18).codexBridgeSurface()
                    Text("Codex Bridge 会自动新建 ChatGPT 会话，再填入所选内容。你检查后自行发送。")
                        .font(.caption).foregroundStyle(.secondary)
                }.padding(26)
            }
            HStack {
                Button("取消") { dismiss() }.buttonStyle(CodexBridgeSecondaryButtonStyle())
                Spacer()
                Button("复制并新建") { Task { await model.copyDraftAndOpenChatGPT() } }.buttonStyle(CodexBridgeSecondaryButtonStyle())
                Button(model.isBusy ? "正在填写…" : "新建会话并填入") { Task { await model.fillChatGPTDraft() } }.buttonStyle(CodexBridgePrimaryButtonStyle())
            }.padding(22).codexbridgeGlass()
        }.background(CodexBridgePalette.canvas).frame(width: 680, height: 650)
            .disabled(model.isBusy || !model.attachmentLoadingIDs.isEmpty).interactiveDismissDisabled(model.isBusy)
            .overlay { if let error = model.errorMessage { CodexBridgeErrorDialog(message: error, dismiss: model.dismissError) } }
    }
}

struct TransferHeading: View {
    let title: String
    let subtitle: String
    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text(title).font(.title2.weight(.semibold))
            Text(subtitle).font(.callout).foregroundStyle(.secondary)
        }.frame(maxWidth: .infinity, alignment: .leading).padding(26).codexbridgeGlass()
    }
}

struct TransferFileChoices: View {
    @Bindable var model: AppModel
    var body: some View {
        if !model.availableTransferFiles.isEmpty {
            VStack(alignment: .leading, spacing: 12) {
                Text("附带文件（可选）").font(.callout.weight(.medium))
                Text("仅附带你勾选的文本内容，每个文件最多 1 MB。图片等原文件请在 ChatGPT 或 Codex 中添加。")
                    .font(.caption).foregroundStyle(.secondary)
                ForEach(model.availableTransferFiles) { file in
                    Toggle(isOn: Binding(get: { model.transferAttachments.contains { $0.id == file.id } }, set: { _ in Task { await model.toggleAttachment(file) } })) {
                        HStack {
                            Text(file.name)
                            if file.localPath == nil { Text("需先下载").foregroundStyle(.secondary) }
                            if model.attachmentLoadingIDs.contains(file.id) { ProgressView().controlSize(.small) }
                        }
                    }.toggleStyle(.checkbox).disabled(file.localPath == nil || model.attachmentLoadingIDs.contains(file.id))
                }
            }
        }
    }
}
