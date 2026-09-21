using System.IO;
using CodexBridge.Core.Capture;
using CodexBridge.Core.Conversations;
using Microsoft.Data.Sqlite;

namespace CodexBridge.App.Infrastructure;

public sealed class SqliteConversationRepository : IConversationRepository
{
    private const string SelectColumns = """
        id, source_kind, source_id, title, source_url, captured_at,
        content_hash, payload, created_at, updated_at
        """;

    private readonly string databasePath;
    private readonly TimeProvider timeProvider;
    private readonly SemaphoreSlim initializeGate = new(1, 1);
    private bool initialized;

    public SqliteConversationRepository(string? databasePath = null, TimeProvider? timeProvider = null)
    {
        this.databasePath = databasePath ?? CodexBridgeWindowsPaths.DatabasePath;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public string DatabasePath => databasePath;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (initialized)
        {
            return;
        }

        await initializeGate.WaitAsync(cancellationToken);
        try
        {
            if (initialized)
            {
                return;
            }

            var directory = Path.GetDirectoryName(databasePath)
                ?? throw new InvalidOperationException("SQLite database path has no parent directory.");
            Directory.CreateDirectory(directory);

            await using var connection = await OpenConnectionAsync(cancellationToken);
            await ExecuteAsync(connection, "PRAGMA foreign_keys = ON", cancellationToken);
            await ExecuteAsync(connection, "PRAGMA journal_mode = WAL", cancellationToken);
            await ExecuteAsync(connection, "PRAGMA busy_timeout = 3000", cancellationToken);
            await ExecuteAsync(
                connection,
                """
                CREATE TABLE IF NOT EXISTS conversations (
                    id TEXT PRIMARY KEY NOT NULL,
                    source_kind TEXT NOT NULL,
                    source_id TEXT,
                    title TEXT NOT NULL,
                    project_name TEXT,
                    source_url TEXT NOT NULL,
                    captured_at REAL NOT NULL,
                    content_hash TEXT NOT NULL,
                    payload BLOB NOT NULL,
                    created_at REAL NOT NULL,
                    updated_at REAL NOT NULL
                );
                CREATE INDEX IF NOT EXISTS conversations_captured_at
                    ON conversations(captured_at DESC);
                CREATE INDEX IF NOT EXISTS conversations_source_kind
                    ON conversations(source_kind);
                CREATE INDEX IF NOT EXISTS conversations_updated_at
                    ON conversations(updated_at DESC);
                PRAGMA user_version = 1;
                """,
                cancellationToken);
            initialized = true;
        }
        finally
        {
            initializeGate.Release();
        }
    }

    public async Task SaveAsync(
        CaptureValidationResult capture,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(capture);
        await InitializeAsync(cancellationToken);

        var payload = capture.Payload;
        var now = ToUnixSeconds(timeProvider.GetUtcNow());
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO conversations
                (id, source_kind, source_id, title, project_name, source_url,
                 captured_at, content_hash, payload, created_at, updated_at)
            VALUES
                ($id, $sourceKind, $sourceId, $title, NULL, $sourceUrl,
                 $capturedAt, $contentHash, $payload, $createdAt, $updatedAt)
            ON CONFLICT(id) DO UPDATE SET
                source_kind = excluded.source_kind,
                source_id = excluded.source_id,
                title = excluded.title,
                source_url = excluded.source_url,
                captured_at = excluded.captured_at,
                content_hash = excluded.content_hash,
                payload = excluded.payload,
                updated_at = excluded.updated_at
            """;
        command.Parameters.AddWithValue("$id", capture.ConversationId.ToString("D").ToUpperInvariant());
        command.Parameters.AddWithValue("$sourceKind", payload.Source.Kind);
        command.Parameters.AddWithValue("$sourceId", (object?)payload.Source.ConversationId ?? DBNull.Value);
        command.Parameters.AddWithValue("$title", payload.Source.Title);
        command.Parameters.AddWithValue("$sourceUrl", payload.Source.Url.AbsoluteUri);
        command.Parameters.AddWithValue("$capturedAt", ToUnixSeconds(payload.Capture.CapturedAt));
        command.Parameters.AddWithValue("$contentHash", capture.ContentHash);
        command.Parameters.Add("$payload", SqliteType.Blob).Value = CaptureJson.Serialize(payload);
        command.Parameters.AddWithValue("$createdAt", now);
        command.Parameters.AddWithValue("$updatedAt", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StoredConversation>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectColumns} FROM conversations ORDER BY updated_at DESC, captured_at DESC, id";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var conversations = new List<StoredConversation>();
        while (await reader.ReadAsync(cancellationToken))
        {
            conversations.Add(ReadConversation(reader));
        }

        return conversations;
    }

    public async Task<StoredConversation?> GetAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectColumns} FROM conversations WHERE id = $id LIMIT 1";
        command.Parameters.AddWithValue("$id", id.ToString("D").ToUpperInvariant());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadConversation(reader) : null;
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = false,
            DefaultTimeout = 3,
        };
        var connection = new SqliteConnection(builder.ToString());
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static StoredConversation ReadConversation(SqliteDataReader reader)
    {
        var payloadBytes = (byte[])reader[7];
        CapturePayload payload;
        try
        {
            payload = CaptureJson.Deserialize(payloadBytes);
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or NotSupportedException)
        {
            throw new InvalidDataException("Stored conversation payload is invalid.", exception);
        }

        return new StoredConversation(
            Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.GetString(3),
            new Uri(reader.GetString(4), UriKind.Absolute),
            FromUnixSeconds(reader.GetDouble(5)),
            reader.GetString(6),
            payload,
            FromUnixSeconds(reader.GetDouble(8)),
            FromUnixSeconds(reader.GetDouble(9)));
    }

    private static double ToUnixSeconds(DateTimeOffset value)
    {
        return value.ToUnixTimeMilliseconds() / 1_000d;
    }

    private static DateTimeOffset FromUnixSeconds(double value)
    {
        return DateTimeOffset.FromUnixTimeMilliseconds(checked((long)Math.Round(value * 1_000d)));
    }
}
