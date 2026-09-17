import AppKit
import CryptoKit
import Foundation
import Observation

@MainActor
@Observable
final class AppModel {
    var conversations: [CapturedConversation] = [] { didSet { fileCache.removeAll() } }
    var operations: [ExecutionOperation] = []
    var links: [SourceTaskLink] = []
    var chatGPTDrafts: [ChatGPTDraft] = []
    var selectedConversationID: UUID? {
        didSet { if oldValue != selectedConversationID { selectedTurnIDs = [] } }
    }
    var selectedTurnIDs: Set<String> = []
    var transferID = UUID()
    var transferConversation: CapturedConversation?
    var transferAttachments: [TransferAttachment] = []
    var attachmentLoadingIDs: Set<String> = []
    var chatGPTInstruction = ""
    var chatGPTReference = ""

    var selectedScope: LibraryScope = .projects
    var selectedProject: String?
    var searchText = ""
    var activeDraft: HandoffDraft?
    var handoffStep = 0
    var isHandoffPresented = false
    var isSourcesPresented = false
    var isImportPresented = false
    var isChatGPTDraftPresented = false
    var isContinuePresented = false
    var isManualCapturePresented = false
    var isBrowserSetupPresented = false
    var isBusy = false
    var loadingConversationID: UUID?
    var conversationDetailErrors: [UUID: String] = [:]
    var statusMessage: String?
    var errorMessage: String?
    var models: [CodexModelOption] = []
    var codexState: SourceConnectionState = .checking
    var chatGPTWebState: SourceConnectionState = .ready("尚未设置浏览器扩展")
    var chatGPTAppState: SourceConnectionState = .ready("需要授权")
    var browserExtensionInstallation: BrowserExtensionInstallation = .missing
    var preparedBrowserExtensionURL: URL?
    var browserExtensionReloadRequired = false
    var preparedBrowserExtensionVersion: String?
    var appUpdateState: AppUpdateState = .idle
    var lastSubmission: ExecutionOperation?
    var activeChatGPTDraft: ChatGPTDraft?
    var queuedCodexInteractions: [CodexInteractionRequest] = []
    var pendingCodexInteraction: CodexInteractionRequest?
    var interactionAnswers: [String: String] = [:]
    var continuePrompt = ""
    var manualCaptureTitle = ""
    var manualCaptureText = ""
    var manualCaptureProject = ""

    @ObservationIgnored private let repository: any ConversationRepository
    @ObservationIgnored private let codexClient: any CodexClient
    @ObservationIgnored private let orchestrator: ExecutionOrchestrator
    @ObservationIgnored private let captureValidator = CaptureValidator()
    @ObservationIgnored private let nativeMessagingInstaller = NativeMessagingInstaller()
    @ObservationIgnored private let appUpdateChecker: any AppUpdateChecking
    @ObservationIgnored private let chatGPTAppCaptureService = ChatGPTAppCaptureService()
    @ObservationIgnored private var statusDismissTask: Task<Void, Never>?
    @ObservationIgnored private var detailTasks: [String: Task<CapturedConversation, Error>] = [:]
    @ObservationIgnored private var detailVersions: [String: Int] = [:]
    @ObservationIgnored private var dirtyThreads: Set<String> = []
    @ObservationIgnored private var fileCache: [UUID: [ConversationFile]] = [:]
    @ObservationIgnored private var refreshInProgress = false
    @ObservationIgnored private var conversationRefreshInProgress = false
    @ObservationIgnored private var hasCompletedBootstrap = false
    @ObservationIgnored private var backgroundDetailTasks: [String: Task<Void, Never>] = [:]
    @ObservationIgnored private var codexEventTask: Task<Void, Never>?

    init(
        repository: any ConversationRepository,
        codexClient: any CodexClient,
        appUpdateChecker: any AppUpdateChecking = GitHubReleaseUpdateChecker()
    ) {
        self.repository = repository
        self.codexClient = codexClient
        self.appUpdateChecker = appUpdateChecker
        self.orchestrator = ExecutionOrchestrator(repository: repository, codexClient: codexClient)
    }

    static func live() -> AppModel {
        let fallback = FileManager.default.temporaryDirectory.appendingPathComponent("codex-bridge.sqlite")
        let url = (try? SQLiteConversationRepository.liveDatabaseURL()) ?? fallback
        let repository = SQLiteConversationRepository(databaseURL: url)
        let client = CodexAppServerClient()
        return AppModel(repository: repository, codexClient: client)
    }

    var selectedConversation: CapturedConversation? {
        conversations.first { $0.id == selectedConversationID }
    }

    var existingWorkspacePaths: [String] {
        let recent = UserDefaults.standard.stringArray(forKey: "codexbridge.recent-workspaces") ?? []
        let discovered = conversations.compactMap(\.projectPath)
        var seen: Set<String> = []
        return (recent + discovered).filter { path in
            var isDirectory = ObjCBool(false)
            return seen.insert(path).inserted
                && FileManager.default.fileExists(atPath: path, isDirectory: &isDirectory)
                && isDirectory.boolValue
        }
    }

    var visibleConversations: [CapturedConversation] {
        conversations.filter { conversation in
            let matchesScope: Bool
            switch selectedScope {
            case .projects: matchesScope = selectedProject == nil || conversation.projectName == selectedProject
            case .chatGPT: matchesScope = conversation.sourceKind == .chatGPTWeb || conversation.sourceKind == .chatGPTApp
            case .codex: matchesScope = conversation.sourceKind == .codex
            case .handoffs:
                matchesScope = links.contains { $0.sourceConversationID == conversation.id }
                    || chatGPTDrafts.contains { $0.sourceConversationID == conversation.id }
            }
            guard matchesScope else { return false }
            guard !searchText.isEmpty else { return true }
            return conversation.title.localizedCaseInsensitiveContains(searchText)
                || (conversation.projectName?.localizedCaseInsensitiveContains(searchText) ?? false)
                || conversation.turns.contains {
                    $0.user.text.localizedCaseInsensitiveContains(searchText)
                    || ($0.assistant?.text.localizedCaseInsensitiveContains(searchText) ?? false)
                }
        }
    }

    var visibleProjectGroups: [ConversationProjectGroup] {
        ConversationProjectGroup.grouped(visibleConversations)
    }

    func bootstrap() async {
        isBusy = true
        defer { isBusy = false }
        do {
            try await repository.prepare()
            try await drainCaptureInbox()
            conversations = try await repository.listConversations()
            operations = try await repository.listOperations()
            links = try await repository.listLinks()
            chatGPTDrafts = try await repository.listChatGPTDrafts()
            refreshCapturedSourceStates()
            if selectedConversationID == nil { selectedConversationID = conversations.first?.id }
            hasCompletedBootstrap = true
        } catch {
            errorMessage = error.localizedDescription
        }
        startCodexEventMonitor()
        isBusy = false
        async let connection: Void = refreshCodexConnection()
        await loadSelectedConversationDetails()
        await connection
    }

    func reloadConversations(reportErrors: Bool = true) async {
        do {
            try await drainCaptureInbox()
            conversations = try await repository.listConversations()
            refreshCapturedSourceStates()
            if selectedConversationID == nil { selectedConversationID = conversations.first?.id }
        } catch {
            if reportErrors { errorMessage = error.localizedDescription }
        }
    }

    func refreshAllConversations(showConfirmation: Bool = true) async {
        guard !conversationRefreshInProgress else { return }
        conversationRefreshInProgress = true
        defer { conversationRefreshInProgress = false }

        await reloadConversations(reportErrors: showConfirmation)
        await refreshCodexConnection(indicateProgress: showConfirmation)
        refreshBrowserConnectionState()
        if showConfirmation { showStatus("会话已刷新") }
    }

    func refreshAutomaticallyIfReady() async {
        guard hasCompletedBootstrap else { return }
        await refreshAllConversations(showConfirmation: false)
    }

    func refreshCodexConnection(indicateProgress: Bool = true) async {
        guard !refreshInProgress else { return }
        refreshInProgress = true
        defer { refreshInProgress = false }
        if indicateProgress { codexState = .checking }
        do {
            let probe = try await codexClient.probe()
            models = probe.models
            codexState = .connected("\(probe.accountLabel) · \(probe.version)")
            await synchronizeCodexThreads()
        } catch {
            codexState = .unavailable(error.localizedDescription)
        }
    }

    private func synchronizeCodexThreads() async {
        do {
            let summaries = try await codexClient.listThreads(limit: 100)
            let activeThreadIDs = Set(summaries.compactMap(\.codexThreadID))
            let missingCodexConversations = conversations.filter { conversation in
                conversation.sourceKind == .codex
                    && conversation.codexThreadID.map { !activeThreadIDs.contains($0) } == true
            }
            for conversation in missingCodexConversations {
                try await removeCodexConversation(conversation)
            }

            var changed: [CapturedConversation] = []
            for summary in summaries {
                if var cached = conversations.first(where: { $0.id == summary.id }), cached.captureScope == "app-server-thread-read" {
                    if let threadID = summary.codexThreadID,
                       (summary.updatedAt ?? summary.capturedAt) > (cached.updatedAt ?? cached.capturedAt) {
                        dirtyThreads.insert(threadID)
                        detailVersions[threadID, default: 0] += 1
                    }
                    cached.title = summary.title
                    cached.projectName = summary.projectName
                    cached.projectPath = summary.projectPath
                    cached.runtimeStatus = summary.runtimeStatus
                    if cached != conversations.first(where: { $0.id == cached.id }) { changed.append(cached); replaceConversation(cached) }
                } else if summary != conversations.first(where: { $0.id == summary.id }) {
                    changed.append(summary)
                    replaceConversation(summary)
                }
            }
            // 列表先显示，再保存；不重新解码数据库中的所有正文。
            for conversation in changed { try await repository.saveConversation(conversation) }
            if selectedConversationID == nil { selectedConversationID = conversations.first?.id }
            if let id = selectedConversation?.codexThreadID, dirtyThreads.contains(id) { scheduleDetailRefresh(id) }
            codexState = .connected("Codex 已连接，可以查看任务")
        } catch {
            codexState = .connected("Codex 已连接 · 历史摘要暂不可用")
        }
    }

    func loadSelectedConversationDetails(force: Bool = false) async {
        guard let conversation = selectedConversation,
              conversation.sourceKind == .codex,
              let threadID = conversation.codexThreadID else { return }
        let loaded = conversation.captureScope == "app-server-thread-read"
        guard force || !loaded || dirtyThreads.contains(threadID) else { return }
        loadingConversationID = conversation.id
        conversationDetailErrors.removeValue(forKey: conversation.id)
        defer { if loadingConversationID == conversation.id { loadingConversationID = nil } }
        do { _ = try await readConversationDetails(threadID) }
        catch {
            if Task.isCancelled { return }
            conversationDetailErrors[conversation.id] = error.localizedDescription
            if force { errorMessage = error.localizedDescription }
        }
    }

    private func readConversationDetails(_ threadID: String) async throws -> CapturedConversation {
        if let pending = detailTasks[threadID] { return try await pending.value }
        let client = codexClient
        let version = detailVersions[threadID, default: 0]
        let task = Task { @MainActor in
            let loaded = try await client.readThread(id: threadID)
            let detailed = self.preservingFileLocations(loaded)
            self.replaceConversation(detailed)
            if self.detailVersions[threadID, default: 0] == version { self.dirtyThreads.remove(threadID) }
            try await self.repository.saveConversation(detailed)
            return detailed
        }
        detailTasks[threadID] = task
        defer { detailTasks.removeValue(forKey: threadID) }
        return try await task.value
    }

    private func scheduleDetailRefresh(_ threadID: String) {
        dirtyThreads.insert(threadID)
        detailVersions[threadID, default: 0] += 1
        guard backgroundDetailTasks[threadID] == nil else { return }
        backgroundDetailTasks[threadID] = Task { @MainActor [weak self] in
            defer { self?.backgroundDetailTasks.removeValue(forKey: threadID) }
            // 合并同一轮结束时接连到达的通知，不阻塞审批事件。
            try? await Task.sleep(for: .milliseconds(200))
            guard !Task.isCancelled, let self,
                  self.selectedConversation?.codexThreadID == threadID else { return }
            repeat {
                do { _ = try await self.readConversationDetails(threadID) }
                catch { break }
            } while self.dirtyThreads.contains(threadID) && self.selectedConversation?.codexThreadID == threadID && !Task.isCancelled
        }
    }

    func beginHandoff() async {
        guard !isBusy, let conversation = selectedConversation else { return }
        do {
            guard !selectedTurnIDs.isEmpty else { return }
            activeChatGPTDraft = nil
            transferID = UUID()
            transferConversation = conversation
            transferAttachments = []
            attachmentLoadingIDs = []
            var draft = try await repository.loadDraft(for: conversation.id) ?? HandoffDraft.empty(for: conversation)
            draft.selectedTurnIDs = conversation.turns.filter { selectedTurnIDs.contains($0.id) }.map(\.id)
            draft.attachments = []
            draft.instruction = ""
            if draft.workspacePath == nil, let projectPath = conversation.projectPath {
                draft.workspacePath = projectPath
            }

            if draft.modelID == nil || (!models.isEmpty && !models.contains(where: { $0.id == draft.modelID })) {
                let model = models.first(where: \.isDefault) ?? models.first
                draft.modelID = model?.id
                draft.reasoningEffort = model?.defaultReasoningEffort
            }
            activeDraft = draft
            handoffStep = 0
            isHandoffPresented = true
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    func updateSelectedModel(_ modelID: String?) {
        activeDraft?.modelID = modelID
        if let model = models.first(where: { $0.id == modelID }) {
            activeDraft?.reasoningEffort = model.defaultReasoningEffort
        }
    }

    func chooseWorkspace() {
        let panel = NSOpenPanel()
        panel.title = "选择 Codex 工作目录"
        panel.prompt = "使用此目录"
        panel.canChooseFiles = false
        panel.canChooseDirectories = true
        panel.allowsMultipleSelection = false
        panel.canCreateDirectories = true
        if panel.runModal() == .OK {
            if let path = panel.url?.path { selectWorkspace(path) }
        }
    }

    func selectWorkspace(_ path: String) {
        activeDraft?.workspacePath = path
        rememberWorkspace(path)
    }

    func createWorkspace() {
        let panel = NSSavePanel()
        panel.title = "新建 Codex 项目"
        panel.prompt = "创建并使用"
        panel.nameFieldLabel = "项目名称"
        panel.nameFieldStringValue = "新项目"
        panel.canCreateDirectories = true
        panel.directoryURL = activeDraft?.workspacePath
            .map { URL(fileURLWithPath: $0).deletingLastPathComponent() }
            ?? FileManager.default.urls(for: .documentDirectory, in: .userDomainMask).first
        guard panel.runModal() == .OK, let url = panel.url else { return }
        do {
            var isDirectory = ObjCBool(false)
            if FileManager.default.fileExists(atPath: url.path, isDirectory: &isDirectory) {
                guard isDirectory.boolValue else {
                    throw CocoaError(.fileWriteFileExists)
                }
            } else {
                try FileManager.default.createDirectory(at: url, withIntermediateDirectories: false)
            }
            selectWorkspace(url.path)
        } catch {
            errorMessage = "无法创建项目。请确认所选位置允许写入后重试。"
        }
    }

    private func rememberWorkspace(_ path: String) {
        var paths = UserDefaults.standard.stringArray(forKey: "codexbridge.recent-workspaces") ?? []
        paths.removeAll { $0 == path }
        paths.insert(path, at: 0)
        UserDefaults.standard.set(Array(paths.prefix(10)), forKey: "codexbridge.recent-workspaces")
    }

    func persistDraft() async {
        guard var draft = activeDraft else { return }
        draft.revision += 1
        draft.updatedAt = .now
        activeDraft = draft
        do {
            try await repository.saveDraft(draft)
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    func submitHandoff() async {
        guard !isBusy, let conversation = transferConversation, var draft = activeDraft else { return }
        if draft.instruction.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
            draft.instruction = "请基于以下选中的对话继续完成任务。"
        }
        draft.attachments = transferAttachments
        draft.updatedAt = .now
        activeDraft = draft
        isBusy = true
        defer { isBusy = false }
        do {
            try await repository.saveDraft(draft)
            let frozen = try ReferenceSerializer().freeze(conversation: conversation, draft: draft)
            guard let text = String(data: frozen.bytes, encoding: .utf8) else {
                errorMessage = "无法准备待填入 Codex 的内容。"
                return
            }
            let fillResult = try await chatGPTAppCaptureService.fillCodexDraft(
                workspacePath: frozen.workspacePath,
                text: text
            )
            let operation = try await orchestrator.recordComposerDraft(conversation: conversation, draft: draft)
            lastSubmission = operation
            operations = try await repository.listOperations()
            switch fillResult {
            case .verified:
                showStatus("Codex 新任务已打开，草稿已完整填入")
            case .inserted:
                showStatus("Codex 新任务已打开并填入草稿；请确认后发送")
            }
            isHandoffPresented = false
        } catch {
            operations = (try? await repository.listOperations()) ?? operations
            statusMessage = nil
            errorMessage = error.localizedDescription
        }
    }

    func beginChatGPTDraft() async {
        guard !isBusy, let conversation = selectedConversation, conversation.sourceKind == .codex else { return }
        if conversation.turns.isEmpty { await loadSelectedConversationDetails(force: true) }
        guard let refreshed = selectedConversation, !refreshed.turns.isEmpty else { return }
        guard !selectedTurnIDs.isEmpty else { return }
        activeDraft = nil
        transferID = UUID()
        transferConversation = refreshed
        transferAttachments = []
        attachmentLoadingIDs = []
        chatGPTInstruction = ""
        chatGPTReference = SelectedConversationText.render(refreshed, ids: selectedTurnIDs)
        activeChatGPTDraft = ChatGPTDraft(id: UUID(), sourceConversationID: refreshed.id,
            selectedTurnIDs: refreshed.turns.filter { selectedTurnIDs.contains($0.id) }.map(\.id),
            content: chatGPTReference, createdAt: .now, updatedAt: .now)
        isChatGPTDraftPresented = true
    }

    func saveChatGPTDraft() async {
        guard var draft = activeChatGPTDraft else { return }
        draft.updatedAt = .now
        activeChatGPTDraft = draft
        do {
            try await repository.saveChatGPTDraft(draft)
            chatGPTDrafts = try await repository.listChatGPTDrafts()
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    func copyDraftAndOpenChatGPT() async {
        guard let draft = activeChatGPTDraft,
              !draft.content.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else { return }
        await saveChatGPTDraft()
        NSPasteboard.general.clearContents()
        NSPasteboard.general.setString(draft.content, forType: .string)
        do { try await chatGPTAppCaptureService.openNewConversation() }
        catch { errorMessage = error.localizedDescription; return }
        isChatGPTDraftPresented = false
        showStatus("草稿已复制，并打开 ChatGPT 新会话；粘贴后可自行发送")
    }

    func beginContinueCodex() {
        continuePrompt = ""
        isContinuePresented = true
    }

    func continueCodexTask() async {
        guard let conversation = selectedConversation,
              let threadID = conversation.codexThreadID else { return }
        let prompt = continuePrompt.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !prompt.isEmpty else { return }
        isBusy = true
        defer { isBusy = false }
        do {
            let submission = try await codexClient.continueThread(
                id: threadID,
                prompt: prompt,
                modelID: conversation.modelID,
                reasoningEffort: nil
            )
            updateConversationState(threadID: threadID, turnID: submission.turnID, state: .running)
            continuePrompt = ""
            isContinuePresented = false
            showStatus("Codex 已继续执行")
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    func forkSelectedCodexTask() async {
        guard let threadID = selectedConversation?.codexThreadID else { return }
        isBusy = true
        defer { isBusy = false }
        do {
            let conversation = try await codexClient.forkThread(id: threadID)
            try await repository.saveConversation(conversation)
            replaceConversation(conversation)
            selectedConversationID = conversation.id
            showStatus("已创建 Codex 任务副本")
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    func interruptSelectedCodexTask() async {
        guard let conversation = selectedConversation,
              let threadID = conversation.codexThreadID,
              let turnID = conversation.activeTurnID else { return }
        do {
            try await codexClient.interrupt(threadID: threadID, turnID: turnID)
            updateConversationState(threadID: threadID, turnID: turnID, state: .interrupted)
            showStatus("正在停止 Codex 任务")
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    func respondToCodexInteraction(accepted: Bool) async {
        guard let request = pendingCodexInteraction else { return }
        let answers = request.questions.reduce(into: [String: [String]]()) { result, question in
            let text = interactionAnswers[question.id]?.trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
            if !text.isEmpty { result[question.id] = [text] }
        }
        do {
            try await codexClient.respond(to: request.id, accepted: accepted, answers: answers)
            pendingCodexInteraction = queuedCodexInteractions.isEmpty ? nil : queuedCodexInteractions.removeFirst()
            interactionAnswers = [:]
            showStatus(accepted ? "已提交给 Codex" : "已拒绝本次请求")
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    func importCapture(from url: URL) async {
        do {
            let conversation = try await Task.detached {
                let data = try Data(contentsOf: url, options: [.mappedIfSafe])
                return try CaptureValidator().validate(data: data)
            }.value
            try await repository.saveConversation(conversation)
            conversations = try await repository.listConversations()
            refreshCapturedSourceStates()
            selectedConversationID = conversation.id
            showStatus("ChatGPT 对话已导入")
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    func saveManualChatGPTCapture() async {
        let text = manualCaptureText.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !text.isEmpty else { return }
        let title = manualCaptureTitle.trimmingCharacters(in: .whitespacesAndNewlines)
        let hash = SHA256.hash(data: Data(text.utf8)).map { String(format: "%02x", $0) }.joined()
        let turns = ChatGPTTranscriptParser().parse(text.components(separatedBy: .newlines))
        guard !turns.isEmpty else {
            errorMessage = "请保留角色标题，例如「用户：问题」和「ChatGPT：回复」，以便准确区分每轮问答。"
            return
        }
        let conversation = CapturedConversation(
            id: CapturedConversation.stableID(for: "chatgpt-app-manual-\(hash)"),
            sourceKind: .chatGPTApp,
            sourceURL: nil,
            sourceConversationID: nil,
            title: title.isEmpty ? String(text.prefix(40)) : title,
            projectName: manualCaptureProject.trimmingCharacters(in: .whitespacesAndNewlines).nilIfEmpty,
            projectPath: nil,
            capturedAt: .now,
            captureScope: "manual-current-conversation",
            freshness: .captured,
            contentHash: "chatgpt-app-manual-\(hash)",
            turns: turns,
            warnings: ["由你手动确认并保存。"],
            codexThreadID: nil
        )
        do {
            try await repository.saveConversation(conversation)
            conversations = try await repository.listConversations()
            refreshCapturedSourceStates()
            selectedConversationID = conversation.id
            isManualCapturePresented = false
            manualCaptureTitle = ""
            manualCaptureText = ""
            manualCaptureProject = ""
            showStatus("ChatGPT 对话已保存")
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    func captureCurrentChatGPTAppWindow() async {
        isBusy = true
        defer { isBusy = false }
        do {
            let conversation = try await chatGPTAppCaptureService.captureVisibleConversation(
                projectName: manualCaptureProject
            )
            try await repository.saveConversation(conversation)
            conversations = try await repository.listConversations()
            selectedConversationID = conversation.id
            chatGPTAppState = .connected("已保存打开的对话")
            showStatus("ChatGPT 对话已保存")
        } catch {
            showStatus(error.localizedDescription)
            isManualCapturePresented = true
        }
    }

    func exportDiagnostics() async {
        let panel = NSSavePanel()
        panel.title = "导出 Codex Bridge 诊断信息"
        panel.nameFieldStringValue = "Codex Bridge 诊断信息.json"
        panel.allowedContentTypes = [.json]
        guard panel.runModal() == .OK, let url = panel.url else { return }
        let sourceCounts = Dictionary(grouping: conversations, by: { $0.sourceKind.rawValue }).mapValues(\.count)
        let operationCounts = Dictionary(grouping: operations, by: { $0.state.rawValue }).mapValues(\.count)
        let payload: [String: Any] = [
            "schemaVersion": 1,
            "generatedAt": ISO8601DateFormatter().string(from: .now),
            "appVersion": Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "unknown",
            "conversationCounts": sourceCounts,
            "operationCounts": operationCounts,
            "handoffLinkCount": links.count,
            "chatGPTDraftCount": chatGPTDrafts.count,
            "codexConnection": codexState.label,
            "privacy": "不包含对话内容、文件路径、网页地址或真实任务标识",
        ]
        do {
            let data = try JSONSerialization.data(withJSONObject: payload, options: [.prettyPrinted, .sortedKeys])
            try data.write(to: url, options: .atomic)
            showStatus("诊断信息已导出")
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    func clearLocalData() async {
        do {
            try await repository.clearLocalData()
            conversations = []
            operations = []
            links = []
            chatGPTDrafts = []
            selectedConversationID = nil
            activeDraft = nil
            activeChatGPTDraft = nil
            showStatus("Codex Bridge 本地数据已清除")
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    func chooseCaptureFile() {
        let panel = NSOpenPanel()
        panel.title = "导入 ChatGPT 对话文件"
        panel.prompt = "导入"
        panel.allowedContentTypes = [.json]
        panel.allowsMultipleSelection = false
        guard panel.runModal() == .OK, let url = panel.url else { return }
        Task { await importCapture(from: url) }
    }

    func prepareBrowserConnection() {
        do {
            _ = try nativeMessagingInstaller.install()
            let prepared = try nativeMessagingInstaller.prepareExtensionDirectory()
            preparedBrowserExtensionURL = prepared.url
            preparedBrowserExtensionVersion = prepared.version
            if prepared.replacedExistingInstallation {
                requireBrowserExtensionReload(for: prepared.version)
            } else {
                restoreBrowserExtensionReloadState(for: prepared.version)
            }
            refreshBrowserConnectionState()
            isBrowserSetupPresented = true
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    func refreshBrowserConnectionState() {
        preparedBrowserExtensionURL = preparedBrowserExtensionURL
            ?? nativeMessagingInstaller.preparedExtensionDirectory()
        browserExtensionInstallation = nativeMessagingInstaller.browserExtensionInstallation()
        if browserExtensionReloadRequired {
            let browserName: String = switch browserExtensionInstallation {
            case let .enabled(browser, _), let .disabled(browser, _): browser.shortName
            case .missing: "浏览器"
            }
            chatGPTWebState = .ready("扩展已更新，请在 \(browserName) 扩展管理页重新加载")
            return
        }
        switch browserExtensionInstallation {
        case let .enabled(browser, _):
            chatGPTWebState = .connected("\(browser.shortName) 扩展已加载，可以从网页保存对话")
        case let .disabled(browser, _):
            chatGPTWebState = .unavailable("扩展已关闭，请在 \(browser.shortName) 扩展管理中重新开启")
        case .missing:
            chatGPTWebState = nativeMessagingInstaller.hasCurrentNativeHostManifest()
                ? .ready("Codex Bridge 已准备好，请在 Chrome 或 Edge 中加载扩展")
                : .ready("尚未设置浏览器扩展")
        }
    }

    func markBrowserExtensionReloaded() {
        UserDefaults.standard.removeObject(forKey: Self.browserExtensionReloadVersionKey)
        browserExtensionReloadRequired = false
        refreshBrowserConnectionState()
        showStatus("扩展更新已确认")
    }

    func checkForUpdatesAutomatically() async {
        guard isAutomaticUpdateCheckDue else { return }
        await checkForUpdates(showFailure: false)
    }

    func checkForUpdates() async {
        await checkForUpdates(showFailure: true)
    }

    func openAvailableUpdate() {
        guard case let .available(release) = appUpdateState else { return }
        NSWorkspace.shared.open(release.pageURL)
    }

    var isBrowserExtensionEnabled: Bool {
        if case .enabled = browserExtensionInstallation { return true }
        return false
    }

    func isBrowserInstalled(_ browser: SupportedExtensionBrowser) -> Bool {
        nativeMessagingInstaller.isBrowserInstalled(browser)
    }

    func openExtensionsPage(in browser: SupportedExtensionBrowser) async {
        do {
            try await nativeMessagingInstaller.openExtensionsPage(in: browser)
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    func revealPreparedBrowserExtension() {
        guard let url = preparedBrowserExtensionURL else {
            errorMessage = "浏览器扩展文件尚未准备好。"
            return
        }
        NSWorkspace.shared.activateFileViewerSelecting([url])
    }

    func copyPreparedBrowserExtensionPath() {
        guard let url = preparedBrowserExtensionURL else {
            errorMessage = "浏览器扩展文件尚未准备好。"
            return
        }
        NSPasteboard.general.clearContents()
        NSPasteboard.general.setString(url.path, forType: .string)
        showStatus("扩展文件夹路径已复制")
    }

    func openChatGPTWeb() async {
        do {
            try await nativeMessagingInstaller.openChatGPTWeb()
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    func synchronizePreparedBrowserExtension() {
        guard nativeMessagingInstaller.preparedExtensionDirectory() != nil else { return }
        do {
            _ = try nativeMessagingInstaller.install()
            let prepared = try nativeMessagingInstaller.prepareExtensionDirectory()
            preparedBrowserExtensionURL = prepared.url
            preparedBrowserExtensionVersion = prepared.version
            if prepared.replacedExistingInstallation {
                requireBrowserExtensionReload(for: prepared.version)
                showStatus("浏览器扩展已更新，请在扩展管理页重新加载")
            } else {
                restoreBrowserExtensionReloadState(for: prepared.version)
            }
        } catch {
            // 启动时保持安静；用户打开“来源与权限”后仍可重新准备连接。
        }
    }

    private func requireBrowserExtensionReload(for version: String) {
        UserDefaults.standard.set(version, forKey: Self.browserExtensionReloadVersionKey)
        browserExtensionReloadRequired = true
    }

    private func restoreBrowserExtensionReloadState(for version: String) {
        let pendingVersion = UserDefaults.standard.string(forKey: Self.browserExtensionReloadVersionKey)
        browserExtensionReloadRequired = pendingVersion == version
    }

    private static let browserExtensionReloadVersionKey = "codexbridge.browser-extension-reload-version"

    private func checkForUpdates(showFailure: Bool) async {
        guard appUpdateState != .checking else { return }
        appUpdateState = .checking
        do {
            let release = try await appUpdateChecker.latestRelease(newerThan: CodexBridgeRelease.version)
            UserDefaults.standard.set(Date().timeIntervalSince1970, forKey: Self.lastUpdateCheckKey)
            if let release {
                appUpdateState = .available(release)
                showStatus("发现 Codex Bridge \(release.version)")
            } else {
                appUpdateState = .upToDate
                if showFailure { showStatus("Codex Bridge 已是最新版本") }
            }
        } catch {
            appUpdateState = showFailure
                ? .failed(error.localizedDescription)
                : .idle
        }
    }

    private var isAutomaticUpdateCheckDue: Bool {
        let timestamp = UserDefaults.standard.double(forKey: Self.lastUpdateCheckKey)
        guard timestamp > 0 else { return true }
        return Date().timeIntervalSince1970 - timestamp >= 24 * 60 * 60
    }

    private static let lastUpdateCheckKey = "codexbridge.last-update-check"

    func openCodex() {
        if let url = URL(string: "codex://") {
            NSWorkspace.shared.open(url)
        } else if let appURL = NSWorkspace.shared.urlForApplication(withBundleIdentifier: "com.openai.codex") {
            NSWorkspace.shared.openApplication(at: appURL, configuration: .init())
        }
    }

    func openSelectedCodexTask() {
        guard let threadID = selectedConversation?.codexThreadID,
              let url = CodexTaskDeepLink.make(threadID: threadID) else {
            errorMessage = "无法打开这项 Codex 任务。"
            return
        }
        guard NSWorkspace.shared.open(url) else {
            errorMessage = "无法打开 ChatGPT，请确认 App 已安装后重试。"
            return
        }
        showStatus("已在 ChatGPT 中打开任务")
    }

    func copyLastThreadID() {
        guard let threadID = lastSubmission?.threadID else { return }
        NSPasteboard.general.clearContents()
        NSPasteboard.general.setString(threadID, forType: .string)
        showStatus("Codex 任务标识已复制")
    }

    func dismissError() {
        errorMessage = nil
    }

    private func startCodexEventMonitor() {
        guard codexEventTask == nil else { return }
        codexEventTask = Task { @MainActor [weak self] in
            guard let self else { return }
            let events = await codexClient.runtimeEvents()
            for await event in events {
                if Task.isCancelled { break }
                await handleCodexEvent(event)
            }
        }
    }

    private func refreshCapturedSourceStates() {
        refreshBrowserConnectionState()
        if conversations.contains(where: { $0.sourceKind == .chatGPTApp }) {
            chatGPTAppState = .connected("已保存打开的对话")
        }
    }

    private func handleCodexEvent(_ event: CodexRuntimeEvent) async {
        switch event {
        case let .threadChanged(threadID):
            scheduleDetailRefresh(threadID)
        case let .threadArchived(threadID):
            if let conversation = conversations.first(where: { $0.codexThreadID == threadID }) {
                try? await removeCodexConversation(conversation)
            }
        case .threadUnarchived:
            await synchronizeCodexThreads()
        case let .stateChanged(threadID, turnID, state):
            updateConversationState(threadID: threadID, turnID: turnID, state: state)
            for index in operations.indices where operations[index].threadID == threadID {
                operations[index].state = state
                operations[index].turnID = turnID ?? operations[index].turnID
                operations[index].updatedAt = .now
                try? await repository.saveOperation(operations[index])
            }
        case let .interaction(request):
            if pendingCodexInteraction == nil {
                pendingCodexInteraction = request
                interactionAnswers = [:]
            } else if pendingCodexInteraction?.id != request.id && !queuedCodexInteractions.contains(where: { $0.id == request.id }) {
                queuedCodexInteractions.append(request)
            }
            updateConversationState(
                threadID: request.threadID,
                turnID: request.turnID,
                state: request.kind == .userInput ? .waitingForInput : .waitingForApproval
            )
        case let .disconnected(message):
            codexState = .unavailable(message)
        }
    }

    private func removeCodexConversation(_ conversation: CapturedConversation) async throws {
        guard conversation.sourceKind == .codex else { return }
        if let threadID = conversation.codexThreadID {
            detailTasks.removeValue(forKey: threadID)?.cancel()
            backgroundDetailTasks.removeValue(forKey: threadID)?.cancel()
            dirtyThreads.remove(threadID)
            detailVersions.removeValue(forKey: threadID)
        }
        conversationDetailErrors.removeValue(forKey: conversation.id)
        try await repository.removeConversation(id: conversation.id)
        conversations.removeAll { $0.id == conversation.id }
        if selectedConversationID == conversation.id {
            selectedConversationID = conversations.first?.id
        }
    }

    private func updateConversationState(threadID: String, turnID: String?, state: ExecutionState) {
        guard let index = conversations.firstIndex(where: { $0.codexThreadID == threadID }) else { return }
        conversations[index].activeTurnID = turnID
        conversations[index].runtimeStatus = switch state {
        case .running, .accepted, .submitting: .running
        case .waitingForApproval: .waitingForApproval
        case .waitingForInput: .waitingForInput
        case .completed: .completed
        case .interrupted, .interrupting: .interrupted
        case .failed, .uncertain: .failed
        default: .idle
        }
        conversations[index].updatedAt = .now
        let updated = conversations[index]
        Task { try? await repository.saveConversation(updated) }
    }

    private func replaceConversation(_ conversation: CapturedConversation) {
        if selectedConversationID == conversation.id { selectedTurnIDs.formIntersection(conversation.turns.map(\.id)) }
        if let index = conversations.firstIndex(where: { $0.id == conversation.id || $0.contentHash == conversation.contentHash }) {
            conversations[index] = conversation
        } else {
            conversations.insert(conversation, at: 0)
        }
    }

    private func drainCaptureInbox() async throws {
        for url in try NativeCaptureInbox.pendingCaptureURLs() {
            do {
                let data = try Data(contentsOf: url, options: [.mappedIfSafe])
                let conversation = try captureValidator.validate(data: data)
                try await repository.saveConversation(conversation)
                try FileManager.default.removeItem(at: url)
            } catch {
                try? NativeCaptureInbox.quarantine(url)
            }
        }
    }

    private func showStatus(_ message: String) {
        statusDismissTask?.cancel()
        statusMessage = message
        statusDismissTask = Task { @MainActor [weak self] in
            try? await Task.sleep(for: .seconds(3.2))
            guard !Task.isCancelled else { return }
            self?.statusMessage = nil
        }
    }
}

private extension String {
    var nilIfEmpty: String? { isEmpty ? nil : self }
}

// MARK: - 轮次与明确选择的文件
extension AppModel {
    var allTurnsSelected: Bool {
        guard let conversation = selectedConversation, !conversation.turns.isEmpty else { return false }
        return Set(conversation.turns.map(\.id)).isSubset(of: selectedTurnIDs)
    }
    var conversationFiles: [ConversationFile] {
        guard let conversation = selectedConversation else { return [] }
        if let cached = fileCache[conversation.id] { return cached }
        let files = ConversationFileDiscovery().files(in: conversation)
        fileCache[conversation.id] = files
        return files
    }
    var availableTransferFiles: [ConversationFile] {
        guard let conversation = transferConversation else { return [] }
        let ids = Set(activeDraft?.selectedTurnIDs ?? activeChatGPTDraft?.selectedTurnIDs ?? [])
        return ConversationFileDiscovery().files(in: conversation).filter { ids.contains($0.turnID) }
    }
    private func preservingFileLocations(_ incoming: CapturedConversation) -> CapturedConversation {
        guard let existing = conversations.first(where: { $0.id == incoming.id }) else { return incoming }
        let old = ConversationFileDiscovery().files(in: existing)
        var result = incoming
        result.files = ConversationFileDiscovery().files(in: incoming).map { file in
            var file = file
            if let path = old.first(where: { $0.id == file.id })?.localPath { file.localPath = path }
            return file
        }
        return result
    }
    func toggleTurn(_ id: String) {
        if selectedTurnIDs.contains(id) { selectedTurnIDs.remove(id) } else { selectedTurnIDs.insert(id) }
    }
    func selectTurnRange(from anchorID: String, through targetID: String) {
        guard let turns = selectedConversation?.turns,
              let anchorIndex = turns.firstIndex(where: { $0.id == anchorID }),
              let targetIndex = turns.firstIndex(where: { $0.id == targetID }) else {
            toggleTurn(targetID)
            return
        }
        let bounds = min(anchorIndex, targetIndex)...max(anchorIndex, targetIndex)
        selectedTurnIDs.formUnion(turns[bounds].map(\.id))
    }
    func selectRecentTurns(_ count: Int) {
        let turns = selectedConversation?.turns ?? []
        selectedTurnIDs = Set(turns.suffix(max(0, count)).map(\.id))
    }
    func selectCompleteTurns() {
        selectedTurnIDs = Set(selectedConversation?.turns.filter(\.complete).map(\.id) ?? [])
    }
    func clearTurnSelection() {
        selectedTurnIDs.removeAll()
    }
    func selectAllTurns() {
        selectedTurnIDs = allTurnsSelected ? [] : Set(selectedConversation?.turns.map(\.id) ?? [])
    }
    func locateFile(_ file: ConversationFile) async {
        guard var conversation = selectedConversation else { return }
        let panel = NSOpenPanel()
        panel.title = "选择 \(file.name) 的本机副本"
        panel.canChooseDirectories = false
        guard panel.runModal() == .OK, let url = panel.url else { return }
        var files = ConversationFileDiscovery().files(in: conversation)
        guard let index = files.firstIndex(where: { $0.id == file.id }) else { return }
        files[index].localPath = url.path
        conversation.files = files
        do { try await repository.saveConversation(conversation); replaceConversation(conversation) }
        catch { errorMessage = error.localizedDescription }
    }
    func toggleAttachment(_ file: ConversationFile) async {
        if transferAttachments.contains(where: { $0.id == file.id }) {
            transferAttachments.removeAll { $0.id == file.id }
        } else {
            guard !attachmentLoadingIDs.contains(file.id) else { return }
            attachmentLoadingIDs.insert(file.id)
            defer { attachmentLoadingIDs.remove(file.id) }
            let snapshotID = transferID
            do {
                let attachment = try await Task.detached { try ConversationFileReader().attachment(file) }.value
                guard transferID == snapshotID else { return }
                guard transferAttachments.reduce(0, { $0 + $1.text.utf8.count }) + attachment.text.utf8.count <= 2_097_152 else {
                    throw ConversationFileError.tooLarge
                }
                transferAttachments.append(attachment)
            } catch { errorMessage = error.localizedDescription }
        }
        activeDraft?.attachments = transferAttachments
        rebuildChatGPTDraft()
    }
    func rebuildChatGPTDraft() {
        let instruction = chatGPTInstruction.trimmingCharacters(in: .whitespacesAndNewlines)
        activeChatGPTDraft?.content = (instruction.isEmpty ? "" : instruction + "\n\n") + chatGPTReference
            + transferAttachments.map { "\n\n附带文件：\($0.name)\n\($0.text)" }.joined()
    }
    func fillChatGPTDraft() async {
        guard !isBusy, let draft = activeChatGPTDraft else { return }
        isBusy = true
        defer { isBusy = false }
        do {
            try await repository.saveChatGPTDraft(draft)
            let result = try await chatGPTAppCaptureService.fillNewConversation(with: draft.content)
            isChatGPTDraftPresented = false
            switch result {
            case .verified:
                showStatus("草稿已完整填入 ChatGPT，请检查后自行发送")
            case .inserted:
                showStatus("草稿已填入 ChatGPT，请检查后自行发送")
            }
        } catch { errorMessage = error.localizedDescription }
    }
}
