using System.IO;
using CodexBridge.App.Infrastructure;
using CodexBridge.Core.Capture;
using CodexBridge.Core.Conversations;

var tests = new (string Name, Func<Task> Test)[]
{
    ("SQLite saves and loads a conversation", SaveAndLoadConversationAsync),
    ("duplicate capture upserts one conversation", DuplicateCaptureDoesNotDuplicateAsync),
    ("valid inbox capture is imported", ValidInboxCaptureIsImportedAsync),
    ("invalid JSON goes to failed", InvalidJsonGoesToFailedAsync),
    ("duplicate inbox events are safe", DuplicateInboxEventsAreSafeAsync),
    ("turn selection is independent from payload", TurnSelectionIsIndependentAsync),
    ("draft renderer matches the macOS format", DraftRendererMatchesMacFormatAsync),
    ("conversation ordering uses updated time", ConversationOrderingUsesUpdatedTimeAsync),
};

var failures = 0;
foreach (var (name, test) in tests)
{
    try
    {
        await test();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {name}: {exception.Message}");
    }
}

Console.WriteLine($"App tests: {tests.Length - failures} passed, {failures} failed.");
return failures == 0 ? 0 : 1;

static CapturePayload LoadFixture()
{
    var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "synthetic-capture-v1.json");
    return CaptureJson.Deserialize(File.ReadAllBytes(path));
}

static async Task SaveAndLoadConversationAsync()
{
    await using var scope = await TestScope.CreateAsync();
    var validation = new CaptureValidator().Validate(CaptureJson.Serialize(LoadFixture()));
    await scope.Repository.SaveAsync(validation);

    var stored = (await scope.Repository.ListAsync()).Single();
    Assert(stored.Id == validation.ConversationId, "stored ID mismatch");
    Assert(stored.Title == validation.Payload.Source.Title, "stored title mismatch");
    Assert(stored.SourceUrl == validation.Payload.Source.Url, "stored URL mismatch");
    Assert(stored.ContentHash == validation.ContentHash, "stored content hash mismatch");
    Assert(stored.Payload.Turns.Count == validation.Payload.Turns.Count, "stored payload mismatch");
}

static async Task DuplicateCaptureDoesNotDuplicateAsync()
{
    await using var scope = await TestScope.CreateAsync();
    var validation = new CaptureValidator().Validate(CaptureJson.Serialize(LoadFixture()));
    await scope.Repository.SaveAsync(validation);
    await scope.Repository.SaveAsync(validation);

    Assert((await scope.Repository.ListAsync()).Count == 1, "duplicate capture created another row");
}

static async Task ValidInboxCaptureIsImportedAsync()
{
    await using var scope = await TestScope.CreateAsync();
    var source = Path.Combine(scope.Inbox, "capture.json");
    await File.WriteAllBytesAsync(source, CaptureJson.Serialize(LoadFixture()));
    await using var importer = new CaptureInboxImporter(
        scope.Repository,
        inboxDirectory: scope.Inbox,
        stabilityDelay: TimeSpan.FromMilliseconds(1));

    await importer.StartAsync();
    var processedFiles = Directory.GetFiles(importer.ProcessedDirectory, "*.json");
    Assert(processedFiles.Length == 1, "startup scan did not move capture to processed");
    Assert((await scope.Repository.ListAsync()).Count == 1, "valid capture was not stored");
}

static async Task InvalidJsonGoesToFailedAsync()
{
    await using var scope = await TestScope.CreateAsync();
    var source = Path.Combine(scope.Inbox, "broken.json");
    await File.WriteAllTextAsync(source, "{ this is not JSON }");
    await using var importer = new CaptureInboxImporter(
        scope.Repository,
        inboxDirectory: scope.Inbox,
        stabilityDelay: TimeSpan.FromMilliseconds(1));

    var result = await importer.ImportFileAsync(source);
    Assert(result.Status == CaptureImportStatus.Failed, "invalid JSON did not fail");
    Assert(result.DestinationPath is not null && File.Exists(result.DestinationPath), "failed file missing");
    Assert(File.Exists(result.DestinationPath + ".error.txt"), "failure reason missing");
    Assert((await scope.Repository.ListAsync()).Count == 0, "invalid JSON was stored");
}

static async Task DuplicateInboxEventsAreSafeAsync()
{
    await using var scope = await TestScope.CreateAsync();
    var source = Path.Combine(scope.Inbox, "duplicate.json");
    await File.WriteAllBytesAsync(source, CaptureJson.Serialize(LoadFixture()));
    await using var importer = new CaptureInboxImporter(
        scope.Repository,
        inboxDirectory: scope.Inbox,
        stabilityDelay: TimeSpan.FromMilliseconds(1));

    var results = await Task.WhenAll(importer.ImportFileAsync(source), importer.ImportFileAsync(source));
    Assert(results.Count(result => result.Status == CaptureImportStatus.Imported) == 1,
        "duplicate events imported more than once");
    Assert((await scope.Repository.ListAsync()).Count == 1, "duplicate events created another row");
}

static Task TurnSelectionIsIndependentAsync()
{
    var payload = LoadFixture();
    var selection = new TurnSelection(payload);
    Assert(selection.IsSelected("turn-0"), "fixture selection was not used");
    selection.Clear();
    Assert(!selection.IsSelected("turn-0"), "clear did not clear selection");
    Assert(payload.Selection.SelectedTurnIds.Contains("turn-0"), "payload selection was mutated");
    selection.SelectAll();
    Assert(selection.IsSelected("turn-0"), "select all did not select turn");
    return Task.CompletedTask;
}

static Task DraftRendererMatchesMacFormatAsync()
{
    var rendered = SelectedConversationText.Render(LoadFixture(), new[] { "turn-0" });
    var expected = "第 1 轮\n\n用户：\n请梳理 Codex Bridge 的关键流程。\n\nChatGPT：\n先捕获，再整理，最后由用户确认 Handoff。";
    Assert(rendered == expected, "draft format differs from the macOS renderer");
    return Task.CompletedTask;
}

static async Task ConversationOrderingUsesUpdatedTimeAsync()
{
    await using var scope = await TestScope.CreateAsync();
    var clock = new TestTimeProvider(DateTimeOffset.UtcNow);
    var repository = await TestScope.CreateRepositoryAsync(scope.DatabasePath, clock);
    var first = LoadFixture() with
    {
        Source = LoadFixture().Source with { ConversationId = "older" },
    };
    var second = LoadFixture() with
    {
        Source = LoadFixture().Source with { ConversationId = "newer" },
    };
    await repository.SaveAsync(new CaptureValidator().Validate(CaptureJson.Serialize(first)));
    clock.Advance(TimeSpan.FromMinutes(1));
    await repository.SaveAsync(new CaptureValidator().Validate(CaptureJson.Serialize(second)));

    var ordered = await repository.ListAsync();
    Assert(ordered[0].SourceConversationId == "newer", "latest updated conversation is not first");
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

sealed class TestScope : IAsyncDisposable
{
    private TestScope(string root, SqliteConversationRepository repository)
    {
        Root = root;
        Repository = repository;
        Inbox = Path.Combine(root, "CaptureInbox");
        Directory.CreateDirectory(Inbox);
    }

    public string Root { get; }
    public string Inbox { get; }
    public string DatabasePath => Path.Combine(Root, "codex-bridge.sqlite");
    public SqliteConversationRepository Repository { get; }

    public static async Task<TestScope> CreateAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"codexbridge-app-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var repository = new SqliteConversationRepository(Path.Combine(root, "codex-bridge.sqlite"));
        await repository.InitializeAsync();
        return new TestScope(root, repository);
    }

    public static async Task<SqliteConversationRepository> CreateRepositoryAsync(
        string path,
        TimeProvider timeProvider)
    {
        var repository = new SqliteConversationRepository(path, timeProvider);
        await repository.InitializeAsync();
        return repository;
    }

    public ValueTask DisposeAsync()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }

        return ValueTask.CompletedTask;
    }
}

sealed class TestTimeProvider : TimeProvider
{
    private DateTimeOffset utcNow;

    public TestTimeProvider(DateTimeOffset utcNow)
    {
        this.utcNow = utcNow;
    }

    public void Advance(TimeSpan amount) => utcNow += amount;

    public override DateTimeOffset GetUtcNow() => utcNow;
}
