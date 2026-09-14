import Foundation
import SQLite3

enum CodexBridgeDatabaseError: LocalizedError {
    case open(String)
    case migrate(String)
    case execute(String)
    case prepare(String)
    case bind(String)
    case decode(String)

    var errorDescription: String? {
        switch self {
        case .open: "无法读取 Codex Bridge 的本地数据。请重新打开 App 后重试。"
        case .migrate: "无法更新旧版本地数据。请保留现有数据并重新打开 App。"
        case .execute, .bind: "无法保存更改，请稍后重试。"
        case .prepare, .decode: "无法读取本地数据。请重新打开 App 后重试。"
        }
    }
}

actor SQLiteConversationRepository: ConversationRepository {
    private let databaseURL: URL
    private var database: OpaquePointer?
    private let encoder = JSONEncoder()
    private let decoder = JSONDecoder()

    init(databaseURL: URL) {
        self.databaseURL = databaseURL
        encoder.outputFormatting = [.sortedKeys, .withoutEscapingSlashes]
    }

    static func liveDatabaseURL() throws -> URL {
        let environment = ProcessInfo.processInfo.environment
        if let override = environment["CODEX_BRIDGE_DATABASE_PATH"] ?? environment["SIDELY_DATABASE_PATH"],
           !override.isEmpty {
            let url = URL(fileURLWithPath: override)
            try FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
            return url
        }
        let base = try FileManager.default.url(
            for: .applicationSupportDirectory,
            in: .userDomainMask,
            appropriateFor: nil,
            create: true
        )
        let directory = base.appendingPathComponent("Codex Bridge", isDirectory: true)
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        let databaseURL = directory.appendingPathComponent("codex-bridge.sqlite")
        let legacyURL = base
            .appendingPathComponent("Sidely", isDirectory: true)
            .appendingPathComponent("sidely.sqlite")
        try migrateLegacyDatabaseIfNeeded(from: legacyURL, to: databaseURL)
        return databaseURL
    }

    static func migrateLegacyDatabaseIfNeeded(from legacyURL: URL, to databaseURL: URL) throws {
        let fileManager = FileManager.default
        guard !fileManager.fileExists(atPath: databaseURL.path),
              fileManager.fileExists(atPath: legacyURL.path) else { return }

        try fileManager.createDirectory(at: databaseURL.deletingLastPathComponent(), withIntermediateDirectories: true)
        var source: OpaquePointer?
        var destination: OpaquePointer?
        guard sqlite3_open_v2(legacyURL.path, &source, SQLITE_OPEN_READONLY | SQLITE_OPEN_FULLMUTEX, nil) == SQLITE_OK,
              let source else {
            sqlite3_close(source)
            throw CodexBridgeDatabaseError.migrate("无法读取旧数据库")
        }
        defer { sqlite3_close(source) }

        guard sqlite3_open_v2(databaseURL.path, &destination, SQLITE_OPEN_CREATE | SQLITE_OPEN_READWRITE | SQLITE_OPEN_FULLMUTEX, nil) == SQLITE_OK,
              let destination else {
            sqlite3_close(destination)
            throw CodexBridgeDatabaseError.migrate("无法创建新数据库")
        }
        defer { sqlite3_close(destination) }
        sqlite3_busy_timeout(source, 3_000)
        sqlite3_busy_timeout(destination, 3_000)

        guard let backup = sqlite3_backup_init(destination, "main", source, "main") else {
            let message = String(cString: sqlite3_errmsg(destination))
            try? fileManager.removeItem(at: databaseURL)
            throw CodexBridgeDatabaseError.migrate(message)
        }
        var result = sqlite3_backup_step(backup, -1)
        var retryCount = 0
        while (result == SQLITE_BUSY || result == SQLITE_LOCKED), retryCount < 60 {
            sqlite3_sleep(50)
            retryCount += 1
            result = sqlite3_backup_step(backup, -1)
        }
        let finishResult = sqlite3_backup_finish(backup)
        guard result == SQLITE_DONE, finishResult == SQLITE_OK else {
            let message = String(cString: sqlite3_errmsg(destination))
            try? fileManager.removeItem(at: databaseURL)
            throw CodexBridgeDatabaseError.migrate(message)
        }
    }

    func prepare() async throws {
        guard database == nil else { return }
        var handle: OpaquePointer?
        let result = sqlite3_open_v2(
            databaseURL.path,
            &handle,
            SQLITE_OPEN_CREATE | SQLITE_OPEN_READWRITE | SQLITE_OPEN_FULLMUTEX,
            nil
        )
        guard result == SQLITE_OK, let handle else {
            let message = handle.map { String(cString: sqlite3_errmsg($0)) } ?? "未知错误"
            sqlite3_close(handle)
            throw CodexBridgeDatabaseError.open(message)
        }
        database = handle

        try execute("PRAGMA foreign_keys = ON")
        try execute("PRAGMA journal_mode = WAL")
        try execute("PRAGMA busy_timeout = 3000")
        try execute("""
            CREATE TABLE IF NOT EXISTS conversations (
                id TEXT PRIMARY KEY NOT NULL,
                source_kind TEXT NOT NULL,
                source_id TEXT,
                title TEXT NOT NULL,
                project_name TEXT,
                captured_at REAL NOT NULL,
                content_hash TEXT NOT NULL UNIQUE,
                payload BLOB NOT NULL
            )
            """)
        try execute("CREATE INDEX IF NOT EXISTS conversations_captured_at ON conversations(captured_at DESC)")
        try execute("CREATE INDEX IF NOT EXISTS conversations_source_kind ON conversations(source_kind)")
        try execute("CREATE INDEX IF NOT EXISTS conversations_project_name ON conversations(project_name)")
        try execute("""
            CREATE TABLE IF NOT EXISTS drafts (
                id TEXT PRIMARY KEY NOT NULL,
                source_conversation_id TEXT NOT NULL UNIQUE,
                updated_at REAL NOT NULL,
                payload BLOB NOT NULL
            )
            """)
        try execute("""
            CREATE TABLE IF NOT EXISTS operations (
                id TEXT PRIMARY KEY NOT NULL,
                draft_id TEXT NOT NULL,
                fingerprint TEXT NOT NULL UNIQUE,
                updated_at REAL NOT NULL,
                payload BLOB NOT NULL
            )
            """)
        try execute("CREATE INDEX IF NOT EXISTS operations_updated_at ON operations(updated_at DESC)")
        try execute("""
            CREATE TABLE IF NOT EXISTS source_task_links (
                id TEXT PRIMARY KEY NOT NULL,
                source_conversation_id TEXT NOT NULL,
                thread_id TEXT NOT NULL UNIQUE,
                created_at REAL NOT NULL,
                payload BLOB NOT NULL
            )
            """)
        try execute("PRAGMA user_version = 1")
        try execute("""
            CREATE TABLE IF NOT EXISTS chatgpt_drafts (
                id TEXT PRIMARY KEY NOT NULL,
                source_conversation_id TEXT NOT NULL UNIQUE,
                updated_at REAL NOT NULL,
                payload BLOB NOT NULL
            )
            """)
        try execute("PRAGMA user_version = 2")
    }

    func close() {
        guard let database else { return }
        sqlite3_close_v2(database)
        self.database = nil
    }

    func listConversations() async throws -> [CapturedConversation] {
        try ensurePrepared()
        return try readPayloads(
            sql: "SELECT payload FROM conversations ORDER BY captured_at DESC",
            as: CapturedConversation.self
        )
    }

    func saveConversation(_ conversation: CapturedConversation) async throws {
        try ensurePrepared()
        let data = try encoder.encode(conversation)
        try write(
            sql: """
                INSERT INTO conversations
                (id, source_kind, source_id, title, project_name, captured_at, content_hash, payload)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?)
                ON CONFLICT DO UPDATE SET
                    content_hash = excluded.content_hash,
                    title = excluded.title,
                    project_name = excluded.project_name,
                    captured_at = excluded.captured_at,
                    payload = excluded.payload
                """,
            values: [
                .text(conversation.id.uuidString),
                .text(conversation.sourceKind.rawValue),
                .optionalText(conversation.sourceConversationID),
                .text(conversation.title),
                .optionalText(conversation.projectName),
                .double(conversation.capturedAt.timeIntervalSince1970),
                .text(conversation.contentHash),
                .blob(data),
            ]
        )
    }

    func loadDraft(for sourceConversationID: UUID) async throws -> HandoffDraft? {
        try ensurePrepared()
        let rows = try readPayloads(
            sql: "SELECT payload FROM drafts WHERE source_conversation_id = ? LIMIT 1",
            values: [.text(sourceConversationID.uuidString)],
            as: HandoffDraft.self
        )
        return rows.first
    }

    func saveDraft(_ draft: HandoffDraft) async throws {
        try ensurePrepared()
        let data = try encoder.encode(draft)
        try write(
            sql: """
                INSERT INTO drafts (id, source_conversation_id, updated_at, payload)
                VALUES (?, ?, ?, ?)
                ON CONFLICT(source_conversation_id) DO UPDATE SET
                    id = excluded.id,
                    updated_at = excluded.updated_at,
                    payload = excluded.payload
                """,
            values: [
                .text(draft.id.uuidString),
                .text(draft.sourceConversationID.uuidString),
                .double(draft.updatedAt.timeIntervalSince1970),
                .blob(data),
            ]
        )
    }

    func operation(for fingerprint: String) async throws -> ExecutionOperation? {
        try ensurePrepared()
        let rows = try readPayloads(
            sql: "SELECT payload FROM operations WHERE fingerprint = ? LIMIT 1",
            values: [.text(fingerprint)],
            as: ExecutionOperation.self
        )
        return rows.first
    }

    func saveOperation(_ operation: ExecutionOperation) async throws {
        try ensurePrepared()
        let data = try encoder.encode(operation)
        try write(
            sql: """
                INSERT INTO operations (id, draft_id, fingerprint, updated_at, payload)
                VALUES (?, ?, ?, ?, ?)
                ON CONFLICT(id) DO UPDATE SET
                    updated_at = excluded.updated_at,
                    payload = excluded.payload
                """,
            values: [
                .text(operation.id.uuidString),
                .text(operation.draftID.uuidString),
                .text(operation.fingerprint),
                .double(operation.updatedAt.timeIntervalSince1970),
                .blob(data),
            ]
        )
    }

    func listOperations() async throws -> [ExecutionOperation] {
        try ensurePrepared()
        return try readPayloads(
            sql: "SELECT payload FROM operations ORDER BY updated_at DESC",
            as: ExecutionOperation.self
        )
    }

    func saveLink(_ link: SourceTaskLink) async throws {
        try ensurePrepared()
        let data = try encoder.encode(link)
        try write(
            sql: """
                INSERT INTO source_task_links (id, source_conversation_id, thread_id, created_at, payload)
                VALUES (?, ?, ?, ?, ?)
                ON CONFLICT(thread_id) DO UPDATE SET payload = excluded.payload
                """,
            values: [
                .text(link.id.uuidString),
                .text(link.sourceConversationID.uuidString),
                .text(link.threadID),
                .double(link.createdAt.timeIntervalSince1970),
                .blob(data),
            ]
        )
    }

    func listLinks() async throws -> [SourceTaskLink] {
        try ensurePrepared()
        return try readPayloads(
            sql: "SELECT payload FROM source_task_links ORDER BY created_at DESC",
            as: SourceTaskLink.self
        )
    }

    func saveChatGPTDraft(_ draft: ChatGPTDraft) async throws {
        try ensurePrepared()
        let data = try encoder.encode(draft)
        try write(
            sql: """
                INSERT INTO chatgpt_drafts (id, source_conversation_id, updated_at, payload)
                VALUES (?, ?, ?, ?)
                ON CONFLICT(source_conversation_id) DO UPDATE SET
                    id = excluded.id,
                    updated_at = excluded.updated_at,
                    payload = excluded.payload
                """,
            values: [
                .text(draft.id.uuidString),
                .text(draft.sourceConversationID.uuidString),
                .double(draft.updatedAt.timeIntervalSince1970),
                .blob(data),
            ]
        )
    }

    func loadChatGPTDraft(for sourceConversationID: UUID) async throws -> ChatGPTDraft? {
        try ensurePrepared()
        return try readPayloads(
            sql: "SELECT payload FROM chatgpt_drafts WHERE source_conversation_id = ? LIMIT 1",
            values: [.text(sourceConversationID.uuidString)],
            as: ChatGPTDraft.self
        ).first
    }

    func listChatGPTDrafts() async throws -> [ChatGPTDraft] {
        try ensurePrepared()
        return try readPayloads(
            sql: "SELECT payload FROM chatgpt_drafts ORDER BY updated_at DESC",
            as: ChatGPTDraft.self
        )
    }

    func clearLocalData() async throws {
        try ensurePrepared()
        try execute("BEGIN IMMEDIATE")
        do {
            try execute("DELETE FROM chatgpt_drafts")
            try execute("DELETE FROM source_task_links")
            try execute("DELETE FROM operations")
            try execute("DELETE FROM drafts")
            try execute("DELETE FROM conversations")
            try execute("COMMIT")
        } catch {
            try? execute("ROLLBACK")
            throw error
        }
    }

    private enum SQLValue {
        case text(String)
        case optionalText(String?)
        case double(Double)
        case blob(Data)
    }

    private func ensurePrepared() throws {
        guard database != nil else { throw CodexBridgeDatabaseError.open("数据库尚未初始化") }
    }

    private func execute(_ sql: String) throws {
        guard let database else { throw CodexBridgeDatabaseError.open("数据库尚未初始化") }
        var error: UnsafeMutablePointer<CChar>?
        guard sqlite3_exec(database, sql, nil, nil, &error) == SQLITE_OK else {
            let message = error.map { String(cString: $0) } ?? String(cString: sqlite3_errmsg(database))
            sqlite3_free(error)
            throw CodexBridgeDatabaseError.execute(message)
        }
    }

    private func write(sql: String, values: [SQLValue]) throws {
        guard let database else { throw CodexBridgeDatabaseError.open("数据库尚未初始化") }
        var statement: OpaquePointer?
        guard sqlite3_prepare_v2(database, sql, -1, &statement, nil) == SQLITE_OK, let statement else {
            throw CodexBridgeDatabaseError.prepare(String(cString: sqlite3_errmsg(database)))
        }
        defer { sqlite3_finalize(statement) }
        try bind(values, to: statement)
        guard sqlite3_step(statement) == SQLITE_DONE else {
            throw CodexBridgeDatabaseError.execute(String(cString: sqlite3_errmsg(database)))
        }
    }

    private func readPayloads<T: Decodable>(
        sql: String,
        values: [SQLValue] = [],
        as type: T.Type
    ) throws -> [T] {
        guard let database else { throw CodexBridgeDatabaseError.open("数据库尚未初始化") }
        var statement: OpaquePointer?
        guard sqlite3_prepare_v2(database, sql, -1, &statement, nil) == SQLITE_OK, let statement else {
            throw CodexBridgeDatabaseError.prepare(String(cString: sqlite3_errmsg(database)))
        }
        defer { sqlite3_finalize(statement) }
        try bind(values, to: statement)

        var result: [T] = []
        while sqlite3_step(statement) == SQLITE_ROW {
            guard let bytes = sqlite3_column_blob(statement, 0) else { continue }
            let count = Int(sqlite3_column_bytes(statement, 0))
            let data = Data(bytes: bytes, count: count)
            do {
                result.append(try decoder.decode(type, from: data))
            } catch {
                throw CodexBridgeDatabaseError.decode(error.localizedDescription)
            }
        }
        return result
    }

    private func bind(_ values: [SQLValue], to statement: OpaquePointer) throws {
        let transient = unsafeBitCast(-1, to: sqlite3_destructor_type.self)
        for (offset, value) in values.enumerated() {
            let index = Int32(offset + 1)
            let result: Int32
            switch value {
            case let .text(text):
                result = sqlite3_bind_text(statement, index, text, -1, transient)
            case let .optionalText(text):
                if let text {
                    result = sqlite3_bind_text(statement, index, text, -1, transient)
                } else {
                    result = sqlite3_bind_null(statement, index)
                }
            case let .double(value):
                result = sqlite3_bind_double(statement, index, value)
            case let .blob(data):
                result = data.withUnsafeBytes { bytes in
                    sqlite3_bind_blob(statement, index, bytes.baseAddress, Int32(data.count), transient)
                }
            }
            guard result == SQLITE_OK else {
                throw CodexBridgeDatabaseError.bind("参数 \(offset + 1) 写入失败")
            }
        }
    }
}
