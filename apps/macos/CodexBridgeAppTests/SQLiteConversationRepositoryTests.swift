import Foundation
import SQLite3
import Testing
@testable import CodexBridge

struct SQLiteConversationRepositoryTests {
    @Test func migratesLegacyDatabaseIncludingUncheckpointedWrites() async throws {
        let directory = FileManager.default.temporaryDirectory
            .appendingPathComponent("codex-bridge-migration-\(UUID())", isDirectory: true)
        let legacyURL = directory.appendingPathComponent("sidely.sqlite")
        let databaseURL = directory.appendingPathComponent("codex-bridge.sqlite")
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: directory) }

        let legacyRepository = SQLiteConversationRepository(databaseURL: legacyURL)
        try await legacyRepository.prepare()
        let conversation = CapturedConversation.syntheticSamples().first!
        try await legacyRepository.saveConversation(conversation)

        try SQLiteConversationRepository.migrateLegacyDatabaseIfNeeded(from: legacyURL, to: databaseURL)

        let migratedRepository = SQLiteConversationRepository(databaseURL: databaseURL)
        try await migratedRepository.prepare()
        #expect(try await migratedRepository.listConversations().map(\.id) == [conversation.id])
        await legacyRepository.close()
        await migratedRepository.close()
    }

    @Test func persistsConversationDraftOperationAndLink() async throws {
        let url = FileManager.default.temporaryDirectory
            .appendingPathComponent("codexbridge-tests", isDirectory: true)
            .appendingPathComponent("\(UUID().uuidString).sqlite")
        try FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        let repository = SQLiteConversationRepository(databaseURL: url)
        try await repository.prepare()

        let conversation = CapturedConversation.syntheticSamples().first!
        try await repository.saveConversation(conversation)
        var draft = HandoffDraft.empty(for: conversation)
        draft.instruction = "实现"
        try await repository.saveDraft(draft)
        let operation = ExecutionOperation(
            id: UUID(), draftID: draft.id, draftRevision: 1, fingerprint: "fingerprint",
            state: .accepted, threadID: "thread", turnID: "turn", errorCode: nil, updatedAt: .now
        )
        try await repository.saveOperation(operation)
        let link = SourceTaskLink(
            id: UUID(), sourceConversationID: conversation.id, selectedTurnIDs: draft.selectedTurnIDs,
            threadID: "thread", createdAt: .now
        )
        try await repository.saveLink(link)

        #expect(try await repository.listConversations().count == 1)
        #expect(try await repository.loadDraft(for: conversation.id)?.id == draft.id)
        #expect(try await repository.operation(for: "fingerprint")?.state == .accepted)
        #expect(try await repository.listLinks().first?.threadID == "thread")

        let reverse = ChatGPTDraft.make(from: conversation)
        try await repository.saveChatGPTDraft(reverse)
        #expect(try await repository.loadChatGPTDraft(for: conversation.id)?.id == reverse.id)

        try await repository.clearLocalData()
        #expect(try await repository.listConversations().isEmpty)
        #expect(try await repository.listOperations().isEmpty)
        #expect(try await repository.listLinks().isEmpty)
        #expect(try await repository.listChatGPTDrafts().isEmpty)
    }

    @Test func removesOnlyRequestedConversation() async throws {
        let url = FileManager.default.temporaryDirectory
            .appendingPathComponent("codexbridge-tests", isDirectory: true)
            .appendingPathComponent("\(UUID().uuidString).sqlite")
        try FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: url) }
        let repository = SQLiteConversationRepository(databaseURL: url)
        try await repository.prepare()

        let conversations = CapturedConversation.syntheticSamples()
        try await repository.saveConversation(conversations[0])
        try await repository.saveConversation(conversations[1])
        try await repository.removeConversation(id: conversations[0].id)

        #expect(try await repository.listConversations().map(\.id) == [conversations[1].id])
        await repository.close()
    }

    @Test func storesDifferentSessionsWithTheSameContentHash() async throws {
        let url = FileManager.default.temporaryDirectory
            .appendingPathComponent("codexbridge-tests", isDirectory: true)
            .appendingPathComponent("\(UUID().uuidString).sqlite")
        try FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: url) }
        let repository = SQLiteConversationRepository(databaseURL: url)
        try await repository.prepare()

        let sample = CapturedConversation.syntheticSamples()[0]
        let other = CapturedConversation(
            id: UUID(),
            sourceKind: sample.sourceKind,
            sourceURL: sample.sourceURL,
            sourceConversationID: "another-session",
            title: sample.title,
            projectName: sample.projectName,
            projectPath: sample.projectPath,
            capturedAt: sample.capturedAt,
            captureScope: sample.captureScope,
            freshness: sample.freshness,
            contentHash: sample.contentHash,
            turns: sample.turns,
            warnings: sample.warnings,
            codexThreadID: sample.codexThreadID
        )
        try await repository.saveConversation(sample)
        try await repository.saveConversation(other)

        #expect(try await repository.listConversations().count == 2)
        await repository.close()
    }

    @Test func migratesVersion2DatabaseAwayFromUniqueContentHashes() async throws {
        let url = FileManager.default.temporaryDirectory
            .appendingPathComponent("codexbridge-tests", isDirectory: true)
            .appendingPathComponent("\(UUID().uuidString).sqlite")
        try FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: url) }

        var database: OpaquePointer?
        #expect(sqlite3_open(url.path, &database) == SQLITE_OK)
        guard let database else { return }
        let schema = """
            CREATE TABLE conversations (
                id TEXT PRIMARY KEY NOT NULL,
                source_kind TEXT NOT NULL,
                source_id TEXT,
                title TEXT NOT NULL,
                project_name TEXT,
                captured_at REAL NOT NULL,
                content_hash TEXT NOT NULL UNIQUE,
                payload BLOB NOT NULL
            );
            PRAGMA user_version = 2;
            """
        #expect(sqlite3_exec(database, schema, nil, nil, nil) == SQLITE_OK)
        sqlite3_close(database)

        let repository = SQLiteConversationRepository(databaseURL: url)
        try await repository.prepare()
        let sample = CapturedConversation.syntheticSamples()[0]
        let other = CapturedConversation(
            id: UUID(), sourceKind: sample.sourceKind, sourceURL: sample.sourceURL,
            sourceConversationID: "separate-session", title: sample.title,
            projectName: sample.projectName, projectPath: sample.projectPath,
            capturedAt: sample.capturedAt, captureScope: sample.captureScope,
            freshness: sample.freshness, contentHash: sample.contentHash,
            turns: sample.turns, warnings: sample.warnings, codexThreadID: nil
        )
        try await repository.saveConversation(sample)
        try await repository.saveConversation(other)

        #expect(try await repository.listConversations().count == 2)
        await repository.close()
    }
}
