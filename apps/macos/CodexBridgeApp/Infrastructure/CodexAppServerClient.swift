import Foundation

enum CodexAppServerError: LocalizedError, Equatable {
    case executableNotFound
    case launchFailed(String)
    case disconnected
    case timeout(String)
    case invalidResponse(String)
    case partialSubmission(threadID: String, message: String)
    case server(code: Int, message: String)

    var errorDescription: String? {
        switch self {
        case .executableNotFound: "未找到 Codex。请先安装或打开 Codex App。"
        case .launchFailed: "无法连接 Codex。请重新打开 Codex 后重试。"
        case .disconnected: "与 Codex 的连接已中断。"
        case .timeout: "Codex 响应超时，请稍后重试。"
        case .invalidResponse: "暂时无法读取 Codex 返回的内容，请稍后重试。"
        case .partialSubmission: "Codex 任务可能已经创建，但状态尚未确认。请先在 Codex 中检查，避免重复创建。"
        case .server: "Codex 暂时无法完成请求，请稍后重试。"
        }
    }
}

actor CodexAppServerClient: CodexClient {
    private struct PendingRequest {
        let method: String
        let continuation: CheckedContinuation<JSONValue, Error>
    }

    private var process: Process?
    private var inputHandle: FileHandle?
    private var outputHandle: FileHandle?
    private var errorHandle: FileHandle?
    private var outputBuffer = Data()
    private var outputConsumer: Task<Void, Never>?
    private var outputContinuation: AsyncStream<Data>.Continuation?
    private var versionTask: Task<String, Never>?
    private var pending: [Int: PendingRequest] = [:]
    private struct PendingInteraction {
        let wireID: JSONValue
        let kind: CodexInteractionKind
        let requestedPermissions: JSONValue?
    }

    private var pendingInteractions: [String: PendingInteraction] = [:]
    private var eventContinuations: [UUID: AsyncStream<CodexRuntimeEvent>.Continuation] = [:]
    private var nextRequestID = 1
    private var initializationTask: Task<Void, Error>?
    private var initialized = false
    private let requestTimeout: Duration
    private let executableURL: URL?

    init(executableURL: URL? = nil, requestTimeout: Duration = .seconds(15)) {
        self.executableURL = executableURL ?? Self.discoverExecutable()
        self.requestTimeout = requestTimeout
    }

    func probe() async throws -> CodexProbe {
        try await startIfNeeded()
        let account = try await request(
            method: "account/read",
            params: .object(["refreshToken": .bool(false)])
        )
        let accountLabel = parseAccountLabel(account)
        let models = try await loadModels()
        if versionTask == nil {
            let url = executableURL
            versionTask = Task.detached { Self.readVersion(executableURL: url) }
        }
        let version = await versionTask!.value
        return CodexProbe(
            accountLabel: accountLabel,
            version: version,
            models: models
        )
    }

    func listThreads(limit: Int = 50) async throws -> [CapturedConversation] {
        try await startIfNeeded()
        var cursor: String?
        var seen: Set<String> = []
        var result: [CapturedConversation] = []
        repeat {
            var params: [String: JSONValue] = [
                "limit": .number(Double(max(1, min(limit, 100)))),
                "sortKey": .string("updated_at"),
                "sortDirection": .string("desc"),
                "useStateDbOnly": .bool(true),
                "archived": .bool(false),
            ]
            if let cursor { params["cursor"] = .string(cursor) }
            let response = try await request(method: "thread/list", params: .object(params))
            result += response["data"]?.arrayValue?.compactMap { parseThread($0) } ?? []
            cursor = response["nextCursor"]?.stringValue
            if let cursor, !seen.insert(cursor).inserted { break }
            try Task.checkCancellation()
        } while cursor != nil
        var ids: Set<UUID> = []
        return result.filter { ids.insert($0.id).inserted }
    }

    func recentThreadSnapshot(limit: Int) async throws -> CodexThreadSnapshot {
        try await startIfNeeded()
        let pageLimit = max(1, min(limit, 100))
        let response = try await request(
            method: "thread/list",
            params: .object([
                "limit": .number(Double(pageLimit)),
                "sortKey": .string("updated_at"),
                "sortDirection": .string("desc"),
                "useStateDbOnly": .bool(true),
                "archived": .bool(false),
            ])
        )
        var ids: Set<UUID> = []
        let threads = (response["data"]?.arrayValue?.compactMap { parseThread($0) } ?? [])
            .filter { ids.insert($0.id).inserted }
        return CodexThreadSnapshot(
            threads: threads,
            isComplete: response["nextCursor"]?.stringValue == nil
        )
    }

    func readThread(id: String) async throws -> CapturedConversation {
        try await startIfNeeded()
        let response = try await request(
            method: "thread/read",
            params: .object(["threadId": .string(id), "includeTurns": .bool(true)])
        )
        guard let thread = response["thread"], let conversation = parseThread(thread, includeTurns: true) else {
            throw CodexAppServerError.invalidResponse("thread/read 缺少有效任务")
        }
        return conversation
    }

    func continueThread(
        id: String,
        prompt: String,
        modelID: String?,
        reasoningEffort: String?
    ) async throws -> CodexSubmission {
        try await startIfNeeded()
        _ = try await request(
            method: "thread/resume",
            params: .object(["threadId": .string(id), "excludeTurns": .bool(true)])
        )
        var params: [String: JSONValue] = [
            "threadId": .string(id),
            "input": .array([.object(["type": .string("text"), "text": .string(prompt)])]),
        ]
        if let modelID, !modelID.isEmpty { params["model"] = .string(modelID) }
        if let reasoningEffort, !reasoningEffort.isEmpty { params["effort"] = .string(reasoningEffort) }
        let response = try await request(method: "turn/start", params: .object(params))
        guard let turnID = response["turn"]?["id"]?.stringValue else {
            throw CodexAppServerError.invalidResponse("turn/start 缺少 turn.id")
        }
        return CodexSubmission(threadID: id, turnID: turnID)
    }

    func forkThread(id: String) async throws -> CapturedConversation {
        try await startIfNeeded()
        let response = try await request(
            method: "thread/fork",
            params: .object(["threadId": .string(id), "threadSource": .string("codexbridge")])
        )
        guard let thread = response["thread"],
              let threadID = thread["id"]?.stringValue else {
            throw CodexAppServerError.invalidResponse("thread/fork 缺少 thread.id")
        }
        return try await readThread(id: threadID)
    }

    func interrupt(threadID: String, turnID: String) async throws {
        try await startIfNeeded()
        _ = try await request(
            method: "turn/interrupt",
            params: .object(["threadId": .string(threadID), "turnId": .string(turnID)])
        )
    }

    func runtimeEvents() async -> AsyncStream<CodexRuntimeEvent> {
        let token = UUID()
        return AsyncStream { continuation in
            eventContinuations[token] = continuation
            continuation.onTermination = { [weak self] _ in
                Task { await self?.removeEventContinuation(token) }
            }
        }
    }

    func respond(to requestID: String, accepted: Bool, answers: [String: [String]]) async throws {
        guard let interaction = pendingInteractions[requestID], let inputHandle else {
            throw CodexAppServerError.invalidResponse("该交互请求已失效")
        }
        let result: JSONValue
        switch interaction.kind {
        case .commandApproval, .fileChangeApproval:
            result = .object(["decision": .string(accepted ? "accept" : "decline")])
        case .permissionsApproval:
            result = .object([
                "permissions": accepted ? (interaction.requestedPermissions ?? .object([:])) : .object([:]),
                "scope": .string("turn"),
            ])
        case .userInput:
            let encoded = answers.mapValues { values in
                JSONValue.object(["answers": .array(values.map(JSONValue.string))])
            }
            result = .object(["answers": .object(encoded)])
        }
        try write(
            .object(["jsonrpc": .string("2.0"), "id": interaction.wireID, "result": result]),
            to: inputHandle
        )
        pendingInteractions.removeValue(forKey: requestID)
    }

    func createDraftThread(_ handoff: FrozenHandoff) async throws -> String {
        try await startIfNeeded()
        let thread = try await request(
            method: "thread/start",
            params: .object([
                "cwd": .string(handoff.workspacePath),
                "model": .string(handoff.modelID),
                "config": .object([
                    "model_reasoning_effort": .string(handoff.reasoningEffort),
                ]),
                "approvalPolicy": .string("on-request"),
                "sandbox": .string("workspace-write"),
                "ephemeral": .bool(false),
                "threadSource": .string("codexbridge"),
            ]),
            timeout: .seconds(90)
        )
        guard let threadID = thread["thread"]?["id"]?.stringValue else {
            throw CodexAppServerError.invalidResponse("thread/start 缺少 thread.id")
        }
        return threadID
    }

    func stop() async {
        let error = CodexAppServerError.disconnected
        for request in pending.values {
            request.continuation.resume(throwing: error)
        }
        pending.removeAll()
        pendingInteractions.removeAll()
        outputContinuation?.finish()
        outputContinuation = nil
        outputConsumer?.cancel()
        outputConsumer = nil
        outputBuffer.removeAll(keepingCapacity: false)
        outputHandle?.readabilityHandler = nil
        errorHandle?.readabilityHandler = nil
        outputHandle = nil
        errorHandle = nil
        try? inputHandle?.close()
        inputHandle = nil
        initialized = false

        if let process {
            process.terminationHandler = nil
            if process.isRunning { process.terminate() }
        }
        process = nil
    }

    private func startIfNeeded() async throws {
        if let process, process.isRunning, initialized { return }
        if let initializationTask { return try await initializationTask.value }
        let task = Task { try await self.launchAndInitialize() }
        initializationTask = task
        defer { initializationTask = nil }
        do { try await task.value }
        catch { await stop(); throw error }
    }

    private func launchAndInitialize() async throws {
        guard let executableURL else { throw CodexAppServerError.executableNotFound }

        let process = Process()
        let input = Pipe()
        let output = Pipe()
        let errorOutput = Pipe()
        process.executableURL = executableURL
        process.arguments = ["app-server", "--stdio"]
        process.standardInput = input
        process.standardOutput = output
        process.standardError = errorOutput
        process.terminationHandler = { [weak self] _ in
            Task { await self?.serverDidTerminate() }
        }
        let (chunks, continuation) = AsyncStream<Data>.makeStream()
        outputContinuation = continuation
        outputConsumer = Task { [weak self] in
            for await data in chunks {
                guard !Task.isCancelled else { break }
                await self?.receive(data)
            }
        }
        output.fileHandleForReading.readabilityHandler = { handle in
            let data = handle.availableData
            if data.isEmpty { continuation.finish() }
            else { continuation.yield(data) }
        }
        errorOutput.fileHandleForReading.readabilityHandler = { handle in
            _ = handle.availableData
        }

        do {
            try process.run()
        } catch {
            output.fileHandleForReading.readabilityHandler = nil
            errorOutput.fileHandleForReading.readabilityHandler = nil
            throw CodexAppServerError.launchFailed(error.localizedDescription)
        }

        self.process = process
        inputHandle = input.fileHandleForWriting
        outputHandle = output.fileHandleForReading
        errorHandle = errorOutput.fileHandleForReading
        let result = try await request(
            method: "initialize",
            params: .object([
                "clientInfo": .object([
                    "name": .string("codexbridge"),
                    "title": .string("Codex Bridge"),
                    "version": .string("1.1.0"),
                ]),
                "capabilities": .object(["experimentalApi": .bool(false)]),
            ])
        )
        _ = result
        try sendNotification(method: "initialized", params: .object([:]))
        initialized = true
    }

    private func loadModels() async throws -> [CodexModelOption] {
        var cursor: String?
        var models: [CodexModelOption] = []
        repeat {
            var params: [String: JSONValue] = [
                "includeHidden": .bool(false),
                "limit": .number(100),
            ]
            if let cursor { params["cursor"] = .string(cursor) }
            let page = try await request(method: "model/list", params: .object(params))
            for value in page["data"]?.arrayValue ?? [] {
                guard let id = value["model"]?.stringValue ?? value["id"]?.stringValue else { continue }
                let effortValues = value["supportedReasoningEfforts"]?.arrayValue?.compactMap {
                    $0["reasoningEffort"]?.stringValue
                } ?? []
                let defaultEffort = value["defaultReasoningEffort"]?.stringValue ?? effortValues.first ?? "medium"
                models.append(
                    CodexModelOption(
                        id: id,
                        displayName: value["displayName"]?.stringValue ?? id,
                        description: value["description"]?.stringValue ?? "",
                        isDefault: value["isDefault"]?.boolValue ?? false,
                        defaultReasoningEffort: defaultEffort,
                        supportedReasoningEfforts: effortValues.isEmpty ? [defaultEffort] : effortValues
                    )
                )
            }
            cursor = page["nextCursor"]?.stringValue
        } while cursor != nil
        return models
    }

    private func parseAccountLabel(_ response: JSONValue) -> String {
        guard let account = response["account"] else { return "Codex 已连接" }
        if let email = account["email"]?.stringValue, !email.isEmpty { return email }
        switch account["type"]?.stringValue {
        case "apiKey": return "API Key"
        case "amazonBedrock": return "Amazon Bedrock"
        default: return "Codex 已连接"
        }
    }

    func parseThread(_ value: JSONValue, includeTurns: Bool = false) -> CapturedConversation? {
        guard let threadID = value["id"]?.stringValue else { return nil }
        let workspacePath = value["cwd"]?.stringValue
        let preview = value["preview"]?.stringValue?.trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
        let name = value["name"]?.stringValue?.trimmingCharacters(in: .whitespacesAndNewlines)
        let title = name?.isEmpty == false ? name! : (preview.isEmpty ? "Codex 任务" : String(preview.prefix(80)))
        let timestamp = value["recencyAt"]?.numberValue
            ?? value["createdAt"]?.numberValue
            ?? Date.now.timeIntervalSince1970
        let turns = includeTurns ? parseTurns(value["turns"]?.arrayValue ?? []) : []
        let state = parseRuntimeStatus(value["status"])
        let activeTurnID = (value["turns"]?.arrayValue ?? []).last(where: {
            $0["status"]?.stringValue == "inProgress"
        })?["id"]?.stringValue
        return CapturedConversation(
            id: CapturedConversation.stableID(for: "codex-thread-\(threadID)"),
            sourceKind: .codex,
            sourceURL: nil,
            sourceConversationID: threadID,
            title: title,
            projectName: workspacePath.map { URL(fileURLWithPath: $0).lastPathComponent },
            projectPath: workspacePath,
            capturedAt: Date(timeIntervalSince1970: timestamp),
            captureScope: includeTurns ? "app-server-thread-read" : "app-server-thread-list",
            freshness: .live,
            contentHash: "codex-thread-\(threadID)",
            turns: turns,
            warnings: includeTurns ? [] : ["打开任务后会自动加载完整对话。"],
            codexThreadID: threadID,
            runtimeStatus: state,
            activeTurnID: activeTurnID,
            updatedAt: value["updatedAt"]?.numberValue.map(Date.init(timeIntervalSince1970:)),
            modelID: value["model"]?.stringValue,
            files: includeTurns ? parseFiles(value["turns"]?.arrayValue ?? [], workspace: workspacePath) : nil
        )
    }

    private func parseFiles(_ turns: [JSONValue], workspace: String?) -> [ConversationFile] {
        var result: [ConversationFile] = []
        for turn in turns {
            guard let turnID = turn["id"]?.stringValue else { continue }
            for item in turn["items"]?.arrayValue ?? [] where item["type"]?.stringValue == "fileChange" {
                for change in item["changes"]?.arrayValue ?? [] {
                    guard let path = change["path"]?.stringValue else { continue }
                    let resolved = path.hasPrefix("/") ? path : workspace.map { URL(fileURLWithPath: $0).appendingPathComponent(path).standardized.path }
                    guard !result.contains(where: { $0.turnID == turnID && $0.source == path }) else { continue }
                    result.append(.init(id: "\(turnID):\(path)", name: URL(fileURLWithPath: path).lastPathComponent,
                        turnID: turnID, source: path, localPath: resolved))
                }
            }
        }
        return result
    }

    private func parseTurns(_ values: [JSONValue]) -> [CapturedTurn] {
        values.enumerated().compactMap { index, value in
            let items = value["items"]?.arrayValue ?? []
            let userText = items
                .filter { $0["type"]?.stringValue == "userMessage" }
                .flatMap { $0["content"]?.arrayValue ?? [] }
                .compactMap { input -> String? in
                    guard input["type"]?.stringValue == "text" else { return nil }
                    return input["text"]?.stringValue
                }
                .joined(separator: "\n")
                .trimmingCharacters(in: .whitespacesAndNewlines)
            guard !userText.isEmpty else { return nil }

            // reasoning 是独立 item；这里只读取用户可见的 agentMessage。
            let assistantText = items
                .filter { $0["type"]?.stringValue == "agentMessage" }
                .compactMap { $0["text"]?.stringValue }
                .filter { !$0.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty }
                .joined(separator: "\n\n")
            let turnID = value["id"]?.stringValue ?? "codex-turn-\(index)"
            let status = value["status"]?.stringValue ?? "completed"
            return CapturedTurn(
                id: turnID,
                index: index,
                user: .init(id: "\(turnID)-user", idSource: "app-server", text: userText),
                assistant: assistantText.isEmpty ? nil : .init(
                    id: "\(turnID)-assistant",
                    idSource: "app-server",
                    text: assistantText
                ),
                complete: status == "completed"
            )
        }
    }

    private func parseRuntimeStatus(_ value: JSONValue?) -> ConversationRuntimeStatus {
        guard let type = value?["type"]?.stringValue else { return .unavailable }
        switch type {
        case "idle": return .idle
        case "systemError": return .failed
        case "active":
            let flags = value?["activeFlags"]?.arrayValue?.compactMap(\.stringValue) ?? []
            if flags.contains("waitingOnApproval") { return .waitingForApproval }
            if flags.contains("waitingOnUserInput") { return .waitingForInput }
            return .running
        default: return .unavailable
        }
    }

    private func request(method: String, params: JSONValue, timeout: Duration? = nil) async throws -> JSONValue {
        guard process?.isRunning == true, let inputHandle else {
            throw CodexAppServerError.disconnected
        }
        let id = nextRequestID
        nextRequestID += 1
        let message: JSONValue = .object([
            "jsonrpc": .string("2.0"),
            "id": .number(Double(id)),
            "method": .string(method),
            "params": params,
        ])

        return try await withCheckedThrowingContinuation { continuation in
            pending[id] = PendingRequest(method: method, continuation: continuation)
            do {
                try write(message, to: inputHandle)
            } catch {
                pending.removeValue(forKey: id)
                continuation.resume(throwing: error)
                return
            }
            Task { [requestTimeout] in
                try? await Task.sleep(for: timeout ?? requestTimeout)
                self.timeoutRequest(id)
            }
        }
    }

    private func timeoutRequest(_ id: Int) {
        guard let request = pending.removeValue(forKey: id) else { return }
        request.continuation.resume(throwing: CodexAppServerError.timeout(request.method))
    }

    private func sendNotification(method: String, params: JSONValue) throws {
        guard let inputHandle else { throw CodexAppServerError.disconnected }
        try write(
            .object([
                "jsonrpc": .string("2.0"),
                "method": .string(method),
                "params": params,
            ]),
            to: inputHandle
        )
    }

    private func receive(_ data: Data) {
        // 旧的未完成行已扫描过，只搜索新收到的字节，避免大响应的平方级重复扫描。
        var searchStart = outputBuffer.endIndex
        outputBuffer.append(data)
        while let newline = outputBuffer[searchStart...].firstIndex(of: 0x0A) {
            var line = outputBuffer[..<newline]
            outputBuffer.removeSubrange(...newline)
            searchStart = outputBuffer.startIndex
            if line.last == 0x0D { line = line.dropLast() }
            guard !line.isEmpty,
                  let value = try? JSONDecoder().decode(JSONValue.self, from: Data(line)) else { continue }
            handle(value)
        }
    }

    private func handle(_ message: JSONValue) {
        if let method = message["method"]?.stringValue {
            if let id = message["id"] {
                if let interaction = parseInteraction(id: id, method: method, params: message["params"]) {
                    pendingInteractions[interaction.id] = PendingInteraction(
                        wireID: id,
                        kind: interaction.kind,
                        requestedPermissions: message["params"]?["permissions"]
                    )
                    emit(.interaction(interaction))
                } else {
                    try? sendSafeDecline(for: id)
                }
            } else {
                handleNotification(method: method, params: message["params"])
            }
            return
        }

        if let idValue = message["id"], let id = integerID(idValue), let pendingRequest = pending.removeValue(forKey: id) {
            if let error = message["error"] {
                let code = Int(error["code"]?.numberValue ?? -1)
                let text = error["message"]?.stringValue ?? "未知错误"
                pendingRequest.continuation.resume(throwing: CodexAppServerError.server(code: code, message: text))
            } else if let result = message["result"] {
                pendingRequest.continuation.resume(returning: result)
            } else {
                pendingRequest.continuation.resume(throwing: CodexAppServerError.invalidResponse(pendingRequest.method))
            }
            return
        }
    }

    private func parseInteraction(id: JSONValue, method: String, params: JSONValue?) -> CodexInteractionRequest? {
        guard let params,
              let threadID = params["threadId"]?.stringValue,
              let turnID = params["turnId"]?.stringValue else { return nil }
        let requestID = stringID(id)
        switch method {
        case "item/commandExecution/requestApproval":
            let command = params["command"]?.stringValue ?? "Codex 请求运行一条命令"
            let reason = params["reason"]?.stringValue ?? params["cwd"]?.stringValue ?? "请确认是否允许本次执行。"
            return .init(id: requestID, kind: .commandApproval, threadID: threadID, turnID: turnID, title: "允许运行命令？", detail: "\(command)\n\n\(reason)", questions: [])
        case "item/fileChange/requestApproval":
            let reason = params["reason"]?.stringValue ?? params["grantRoot"]?.stringValue ?? "Codex 请求修改工作目录中的文件。"
            return .init(id: requestID, kind: .fileChangeApproval, threadID: threadID, turnID: turnID, title: "允许修改文件？", detail: reason, questions: [])
        case "item/permissions/requestApproval":
            let reason = params["reason"]?.stringValue ?? params["cwd"]?.stringValue ?? "Codex 请求额外权限。"
            return .init(id: requestID, kind: .permissionsApproval, threadID: threadID, turnID: turnID, title: "Codex 请求额外权限", detail: reason, questions: [])
        case "item/tool/requestUserInput":
            let questions = params["questions"]?.arrayValue?.compactMap { value -> CodexInputQuestion? in
                guard let questionID = value["id"]?.stringValue,
                      let question = value["question"]?.stringValue else { return nil }
                let options = value["options"]?.arrayValue?.compactMap { $0["label"]?.stringValue } ?? []
                return .init(
                    id: questionID,
                    header: value["header"]?.stringValue ?? "补充信息",
                    question: question,
                    options: options,
                    isSecret: value["isSecret"]?.boolValue ?? false
                )
            } ?? []
            return .init(id: requestID, kind: .userInput, threadID: threadID, turnID: turnID, title: "Codex 需要补充信息", detail: "回答后任务会继续执行。", questions: questions)
        default:
            return nil
        }
    }

    private func handleNotification(method: String, params: JSONValue?) {
        guard let params, let threadID = params["threadId"]?.stringValue else { return }
        switch method {
        case "thread/archived":
            emit(.threadArchived(threadID: threadID))
        case "thread/unarchived":
            emit(.threadUnarchived(threadID: threadID))
        case "thread/status/changed":
            let status = parseRuntimeStatus(params["status"])
            emit(.stateChanged(threadID: threadID, turnID: nil, state: executionState(for: status)))
        case "turn/started":
            emit(.stateChanged(threadID: threadID, turnID: params["turn"]?["id"]?.stringValue, state: .running))
        case "turn/completed":
            let turn = params["turn"]
            let status = turn?["status"]?.stringValue
            let state: ExecutionState = switch status {
            case "interrupted": .interrupted
            case "failed": .failed
            default: .completed
            }
            emit(.stateChanged(threadID: threadID, turnID: turn?["id"]?.stringValue, state: state))
            emit(.threadChanged(threadID: threadID))
        default:
            break
        }
    }

    private func executionState(for status: ConversationRuntimeStatus) -> ExecutionState {
        switch status {
        case .running: .running
        case .waitingForApproval: .waitingForApproval
        case .waitingForInput: .waitingForInput
        case .completed, .idle: .completed
        case .interrupted: .interrupted
        case .failed, .unavailable: .failed
        }
    }

    private func emit(_ event: CodexRuntimeEvent) {
        for continuation in eventContinuations.values { continuation.yield(event) }
    }

    private func removeEventContinuation(_ token: UUID) {
        eventContinuations.removeValue(forKey: token)
    }

    private func sendSafeDecline(for id: JSONValue) throws {
        guard let inputHandle else { throw CodexAppServerError.disconnected }
        try write(
            .object([
                "jsonrpc": .string("2.0"),
                "id": id,
                "result": .object(["decision": .string("decline")]),
            ]),
            to: inputHandle
        )
    }

    private func write(_ message: JSONValue, to handle: FileHandle) throws {
        var data = try JSONEncoder().encode(message)
        data.append(0x0A)
        try handle.write(contentsOf: data)
    }

    private func integerID(_ value: JSONValue) -> Int? {
        switch value {
        case let .number(number): Int(number)
        case let .string(string): Int(string)
        default: nil
        }
    }

    private func stringID(_ value: JSONValue) -> String {
        switch value {
        case let .number(number): String(Int(number))
        case let .string(string): string
        default: UUID().uuidString
        }
    }

    private func serverDidTerminate() {
        guard process != nil else { return }
        let requests = pending.values
        pending.removeAll()
        for request in requests {
            request.continuation.resume(throwing: CodexAppServerError.disconnected)
        }
        process = nil
        outputContinuation?.finish()
        outputContinuation = nil
        outputConsumer?.cancel()
        outputConsumer = nil
        outputBuffer.removeAll(keepingCapacity: false)
        outputHandle?.readabilityHandler = nil
        errorHandle?.readabilityHandler = nil
        outputHandle = nil
        errorHandle = nil
        try? inputHandle?.close()
        inputHandle = nil
        initialized = false
        pendingInteractions.removeAll()
        emit(.disconnected("Codex 服务已断开"))
    }

    private nonisolated static func readVersion(executableURL: URL?) -> String {
        guard let executableURL else { return "未知版本" }
        let process = Process()
        let output = Pipe()
        process.executableURL = executableURL
        process.arguments = ["--version"]
        process.standardOutput = output
        process.standardError = FileHandle.nullDevice
        do {
            try process.run()
            process.waitUntilExit()
            let data = output.fileHandleForReading.readDataToEndOfFile()
            let value = String(data: data, encoding: .utf8)?.trimmingCharacters(in: .whitespacesAndNewlines)
            return value?.isEmpty == false ? value! : "未知版本"
        } catch {
            return "未知版本"
        }
    }

    private static func discoverExecutable() -> URL? {
        let processEnvironment = ProcessInfo.processInfo.environment
        let environment = processEnvironment["CODEX_BRIDGE_CODEX_PATH"] ?? processEnvironment["SIDELY_CODEX_PATH"]
        let candidates = [
            environment,
            "/Applications/ChatGPT.app/Contents/Resources/codex",
            "/Applications/Codex.app/Contents/Resources/codex",
            "/opt/homebrew/bin/codex",
            "/usr/local/bin/codex",
        ].compactMap { $0 }
        return candidates
            .map(URL.init(fileURLWithPath:))
            .first { FileManager.default.isExecutableFile(atPath: $0.path) }
    }
}
