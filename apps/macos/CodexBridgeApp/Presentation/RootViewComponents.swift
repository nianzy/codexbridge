import SwiftUI

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

struct ContinueCodexDialog: View {
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

struct CodexInteractionDialog: View {
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
