using System.IO;
using System.Text;
using System.Windows;
using CodexBridge.App.Infrastructure;
using CodexBridge.App.ViewModels;
using CodexBridge.Core.Capture;
using CodexBridge.Core.Codex;
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
    ("Codex refresh connects and fills UI collection", CodexRefreshConnectsAndFillsCollectionAsync),
    ("Codex refresh preserves exception detail", CodexRefreshPreservesExceptionDetailAsync),
    ("Codex error does not clear ChatGPT conversations", CodexErrorDoesNotClearChatGptConversationsAsync),
    ("faulted Codex refresh recreates client", FaultedCodexRefreshRecreatesClientAsync),
    ("session explorer source and workspace filters", SessionExplorerFiltersAsync),
    ("workspace picker add normalizes and persists paths", WorkspacePickerAddAsync),
    ("workspace switching commits or rolls back atomically", WorkspaceSwitchIsAtomicAsync),
    ("context pack renders all sections in order", ContextPackRendersAsync),
    ("context pack keys distinguish same titles", ContextPackKeysAreDistinctAsync),
    ("context pack empty render is safe", ContextPackEmptyRenderAsync),
    ("context pack size boundaries classify correctly", ContextPackSizeClassificationAsync),
    ("context pack character estimate matches items", ContextPackCharacterEstimateAsync),
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

static async Task CodexRefreshConnectsAndFillsCollectionAsync()
{
    await using var scope = await TestScope.CreateAsync();
    await using var importer = new CaptureInboxImporter(scope.Repository, inboxDirectory: scope.Inbox);
    await using var client = FakeCodexClient.Success();
    await using var viewModel = new MainWindowViewModel(scope.Repository, importer, client);

    await viewModel.RefreshCodexAsync();

    Assert(viewModel.CodexStatusText.StartsWith("Connected", StringComparison.Ordinal), "successful refresh did not connect");
    Assert(viewModel.CodexThreads.Count == 1, "successful refresh did not populate the Codex collection");
    Assert(string.IsNullOrEmpty(viewModel.CodexErrorDetail), "successful refresh retained an error detail");
    Assert(viewModel.StatusText != "Loading…", "successful Codex refresh left the global status loading");
}

static async Task CodexRefreshPreservesExceptionDetailAsync()
{
    await using var scope = await TestScope.CreateAsync();
    await using var importer = new CaptureInboxImporter(scope.Repository, inboxDirectory: scope.Inbox);
    await using var client = FakeCodexClient.Failure(new InvalidOperationException("diagnostic fixture", new IOException("inner fixture")));
    await using var viewModel = new MainWindowViewModel(scope.Repository, importer, client);

    await viewModel.RefreshCodexAsync();

    Assert(viewModel.CodexStatusText == "Error", "failed refresh did not set Error status");
    Assert(viewModel.CodexErrorDetail.Contains("System.InvalidOperationException", StringComparison.Ordinal), "exception type was lost");
    Assert(viewModel.CodexErrorDetail.Contains("diagnostic fixture", StringComparison.Ordinal), "exception message was lost");
    Assert(viewModel.CodexErrorDetail.Contains("inner fixture", StringComparison.Ordinal), "inner exception message was lost");
    Assert(viewModel.StatusText != "Loading…", "failed Codex refresh left the global status loading");
}

static async Task CodexErrorDoesNotClearChatGptConversationsAsync()
{
    await using var scope = await TestScope.CreateAsync();
    await scope.Repository.SaveAsync(new CaptureValidator().Validate(CaptureJson.Serialize(LoadFixture())));
    await using var importer = new CaptureInboxImporter(scope.Repository, inboxDirectory: scope.Inbox);
    await using var client = FakeCodexClient.Failure(new InvalidOperationException("Codex unavailable"));
    await using var viewModel = new MainWindowViewModel(scope.Repository, importer, client);

    await viewModel.RefreshAsync();
    await viewModel.RefreshCodexAsync();

    Assert(viewModel.Conversations.Count == 1, "Codex error changed the ChatGPT conversation collection");
    Assert(viewModel.CodexStatusText == "Error", "Codex error status mismatch");
}

static async Task FaultedCodexRefreshRecreatesClientAsync()
{
    await using var scope = await TestScope.CreateAsync();
    await using var importer = new CaptureInboxImporter(scope.Repository, inboxDirectory: scope.Inbox);
    var oldClient = FakeCodexClient.Failure(new CodexProtocolException("invalid JSON"));
    var replacement = FakeCodexClient.Success();
    var factoryCalls = 0;
    await using var viewModel = new MainWindowViewModel(
        scope.Repository,
        importer,
        oldClient,
        codexClientFactory: () =>
        {
            factoryCalls++;
            return replacement;
        });

    await viewModel.RefreshCodexAsync();
    Assert(viewModel.CodexStatusText == "Error", "first faulted refresh did not fail");
    oldClient.MarkFaulted();
    await viewModel.RefreshCodexAsync();

    Assert(factoryCalls == 1, "faulted refresh did not create exactly one replacement client");
    Assert(viewModel.CodexStatusText.StartsWith("Connected", StringComparison.Ordinal), "replacement client did not reconnect");
    Assert(viewModel.CodexThreads.Count == 1, "replacement client did not load threads");
    Assert(oldClient.WasDisposed, "old faulted client was not disposed");
}

static async Task SessionExplorerFiltersAsync()
{
    await using var scope = await TestScope.CreateAsync();
    Environment.SetEnvironmentVariable("CODEX_BRIDGE_WORKSPACES_PATH", Path.Combine(scope.Root, "session-workspaces.json"));
    await using var importer = new CaptureInboxImporter(scope.Repository, inboxDirectory: scope.Inbox);
    await using var client = FakeCodexClient.Success();
    await using var viewModel = new MainWindowViewModel(scope.Repository, importer, client);
    await scope.Repository.SaveAsync(new CaptureValidator().Validate(CaptureJson.Serialize(LoadFixture())));
    await viewModel.RefreshAsync();
    viewModel.SessionFilter = "ChatGPT";
    Assert(viewModel.ConversationsView.Cast<ConversationListItemViewModel>().Count() == 1, "ChatGPT session was hidden while workspace filter was off");
    viewModel.WorkspaceOnly = true;
    Assert(viewModel.ConversationsView.Cast<ConversationListItemViewModel>().Count() == 0, "unassociated ChatGPT session was not hidden by workspace filter");
    Assert(viewModel.ChatGptEmptyText == "No sessions associated with this workspace", "workspace empty-state text is misleading");
    viewModel.WorkspaceOnly = false;
    var workspace = viewModel.SelectedWorkspace ?? new WorkspaceItem("fixture", Environment.CurrentDirectory, DateTimeOffset.UtcNow);
    var workspacePath = Path.GetFullPath(workspace.Path);
    foreach (var thread in new[]
    {
        new CodexThreadListItemViewModel(new CodexThreadSummary("parent", "Parent", "", Path.GetDirectoryName(workspacePath), null, null, null, false, null)),
        new CodexThreadListItemViewModel(new CodexThreadSummary("root", "Root", "", workspacePath, null, null, null, false, null)),
        new CodexThreadListItemViewModel(new CodexThreadSummary("child", "Child", "", Path.Combine(workspacePath, "apps"), null, null, null, false, null)),
    }) viewModel.CodexThreads.Add(thread);
    viewModel.SessionFilter = "Codex";
    Assert(viewModel.CodexThreadsView.Cast<CodexThreadListItemViewModel>().Count() == 3, "workspace off did not show all Codex threads");
    viewModel.WorkspaceOnly = true;
    Assert(viewModel.CodexThreadsView.Cast<CodexThreadListItemViewModel>().Count() == 2, "workspace filter did not match root and child cwd");
    viewModel.SessionSearchText = "Child";
    Assert(viewModel.CodexThreadsView.Cast<CodexThreadListItemViewModel>().Count() == 1, "search and workspace filters did not combine");
    viewModel.SessionFilter = "ChatGPT";
    Assert(viewModel.CodexThreadsView.Cast<CodexThreadListItemViewModel>().Count() == 0, "ChatGPT source filter leaked Codex threads");
    viewModel.SessionFilter = "Codex";
    viewModel.SessionSearchText = string.Empty;
    viewModel.WorkspaceOnly = false;
    Assert(viewModel.CodexThreadsView.Cast<CodexThreadListItemViewModel>().Count() == 3, "workspace off did not restore all Codex threads");
}

static async Task WorkspacePickerAddAsync()
{
    var previousOverride = Environment.GetEnvironmentVariable("CODEX_BRIDGE_WORKSPACES_PATH");
    try
    {
        await using var scope = await TestScope.CreateAsync();
        var workspaceStorePath = Path.Combine(scope.Root, "workspaces.json");
        Environment.SetEnvironmentVariable("CODEX_BRIDGE_WORKSPACES_PATH", workspaceStorePath);
        await using var importer = new CaptureInboxImporter(scope.Repository, inboxDirectory: scope.Inbox);
        await using var client = FakeCodexClient.Success();
        await using var viewModel = new MainWindowViewModel(scope.Repository, importer, client);
        var addedPath = Path.Combine(scope.Root, "AddedWorkspace");
        Directory.CreateDirectory(addedPath);

        Assert(viewModel.AddWorkspaceFromPath(addedPath + Path.DirectorySeparatorChar), "valid workspace was not added");
        var count = viewModel.Workspaces.Count;
        Assert(!viewModel.AddWorkspaceFromPath(addedPath.ToUpperInvariant()), "duplicate workspace path was added");
        Assert(viewModel.Workspaces.Count == count, "duplicate workspace changed the list");
        Assert(!viewModel.AddWorkspaceFromPath(Path.Combine(scope.Root, "missing")), "missing workspace was added");
        Assert(WorkspaceStore.Load().Any(item => string.Equals(item.Path, Path.GetFullPath(addedPath), StringComparison.OrdinalIgnoreCase)), "workspace was not persisted");
    }
    finally
    {
        Environment.SetEnvironmentVariable("CODEX_BRIDGE_WORKSPACES_PATH", previousOverride);
    }
}

static async Task WorkspaceSwitchIsAtomicAsync()
{
    var previousOverride = Environment.GetEnvironmentVariable("CODEX_BRIDGE_WORKSPACES_PATH");
    try
    {
        await using var scope = await TestScope.CreateAsync();
        Environment.SetEnvironmentVariable("CODEX_BRIDGE_WORKSPACES_PATH", Path.Combine(scope.Root, "workspaces.json"));
        await using var importer = new CaptureInboxImporter(scope.Repository, inboxDirectory: scope.Inbox);
        await using var client = FakeCodexClient.Success();
        var current = new WorkspaceItem("current", scope.Root, DateTimeOffset.UtcNow);
        var targetPath = Path.Combine(scope.Root, "target");
        Directory.CreateDirectory(targetPath);
        var target = new WorkspaceItem("target", targetPath, DateTimeOffset.UtcNow);
        await using var cancelled = new MainWindowViewModel(scope.Repository, importer, client, confirmWorkspaceSwitch: () => MessageBoxResult.Cancel);
        cancelled.Workspaces.Add(current);
        cancelled.Workspaces.Add(target);
        Assert(cancelled.TrySwitchWorkspace(current, requireConfirmation: false), "initial workspace setup failed");
        cancelled.ContextItems.Add(new ContextPackItem("Note", "keep", "test", "content", "keep", 0, "keep"));
        Assert(!cancelled.TrySwitchWorkspace(target), "cancelled workspace switch committed");
        Assert(cancelled.SelectedWorkspace?.Path == current.Path && cancelled.ContextItems.Count == 1, "cancel did not preserve workspace and context");

        await using var committed = new MainWindowViewModel(scope.Repository, importer, FakeCodexClient.Success(), confirmWorkspaceSwitch: () => MessageBoxResult.OK);
        committed.Workspaces.Add(current);
        committed.Workspaces.Add(target);
        Assert(committed.TrySwitchWorkspace(current, requireConfirmation: false), "second initial workspace setup failed");
        committed.ContextItems.Add(new ContextPackItem("Note", "clear", "test", "content", "clear", 0, "clear"));
        Assert(committed.TrySwitchWorkspace(target), "confirmed workspace switch failed");
        Assert(committed.SelectedWorkspace?.Path == target.Path && committed.ContextItems.Count == 0, "successful switch did not commit atomically");

        var missing = new WorkspaceItem("missing", Path.Combine(scope.Root, "missing"), DateTimeOffset.UtcNow);
        committed.ContextItems.Add(new ContextPackItem("Note", "keep", "test", "content", "keep2", 0, "keep2"));
        Assert(!committed.TrySwitchWorkspace(missing), "missing workspace switch succeeded");
        Assert(committed.SelectedWorkspace?.Path == target.Path && committed.ContextItems.Count == 1, "failed switch did not roll back");
    }
    finally
    {
        Environment.SetEnvironmentVariable("CODEX_BRIDGE_WORKSPACES_PATH", previousOverride);
    }
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static Task ContextPackRendersAsync()
{
    var service = new ContextPackService();
    var workspace = new WorkspaceItem("fixture", Path.GetTempPath(), DateTimeOffset.UtcNow);
    var items = new[]
    {
        new ContextPackItem("ChatGPTTurn", "Turn 3", "ChatGPT", "user", "c:3", 0, "1"),
        new ContextPackItem("CodexTurn", "Turn 5", "Codex", "codex", "t:5", 1, "2"),
        new ContextPackItem("ProjectFile", "README.md", "Workspace", "file", "README.md", 2, "3"),
        new ContextPackItem("GitDiff", "README.md", "Git", "diff", "README.md", 3, "4"),
        new ContextPackItem("Note", "ui.md", "Notes", "note", "ui.md", 4, "5"),
    };
    var markdown = service.RenderMarkdown(service.Create("Context Pack", workspace, items), "main", 2);
    Assert(markdown.IndexOf("## ChatGPT Context", StringComparison.Ordinal) < markdown.IndexOf("## Codex Context", StringComparison.Ordinal), "context section order mismatch");
    Assert(markdown.Contains("## Project Files") && markdown.Contains("## Git Diffs") && markdown.Contains("## Notes"), "context sections missing");
    return Task.CompletedTask;
}

static Task ContextPackKeysAreDistinctAsync()
{
    var a = ContextPackService.CreateKey("ProjectFile", "Workspace", "README.md|1", "same");
    var b = ContextPackService.CreateKey("Note", "Notes", "README.md|1", "same");
    Assert(a != b, "different context types were deduplicated");
    return Task.CompletedTask;
}

static Task ContextPackEmptyRenderAsync()
{
    var service = new ContextPackService();
    var pack = service.Create("Context Pack", new WorkspaceItem("fixture", Path.GetTempPath(), DateTimeOffset.UtcNow), []);
    Assert(service.RenderMarkdown(pack, "main", 0).Contains("# Context Pack"), "empty context render failed");
    return Task.CompletedTask;
}

static Task ContextPackSizeClassificationAsync()
{
    Assert(ContextPackService.ClassifySize(19999) == "Small", "19999 boundary");
    Assert(ContextPackService.ClassifySize(20000) == "Medium", "20000 boundary");
    Assert(ContextPackService.ClassifySize(80000) == "Medium", "80000 boundary");
    Assert(ContextPackService.ClassifySize(80001) == "Large", "80001 boundary");
    return Task.CompletedTask;
}

static Task ContextPackCharacterEstimateAsync()
{
    var service = new ContextPackService();
    var pack = service.Create("Context Pack", new WorkspaceItem("fixture", Path.GetTempPath(), DateTimeOffset.UtcNow), [new ContextPackItem("Note", "n", "Notes", "abc", "n", 0, "n")]);
    Assert(pack.EstimatedCharacters == 3, "character estimate mismatch");
    return Task.CompletedTask;
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

sealed class FakeCodexClient : ICodexClient
{
    private readonly Exception? failure;
    private readonly IReadOnlyList<CodexThreadSummary> threads;
    private bool faulted;

    private FakeCodexClient(Exception? failure, IReadOnlyList<CodexThreadSummary> threads)
    {
        this.failure = failure;
        this.threads = threads;
    }

    public CodexConnectionStatus Status => failure is null
        ? CodexConnectionStatus.Connected()
        : CodexConnectionStatus.Error();

    public string? ExecutablePath => "C:\\fixture\\codex.exe";

    public bool IsFaulted => faulted;

    public bool WasDisposed { get; private set; }

    public static FakeCodexClient Success() => new(
        null,
        new[]
        {
            new CodexThreadSummary(
                "thread-1",
                "Fixture thread",
                "Fixture preview",
                "C:\\fixture",
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                false,
                "fixture-model"),
        });

    public static FakeCodexClient Failure(Exception exception)
    {
        return new FakeCodexClient(exception, Array.Empty<CodexThreadSummary>());
    }

    public void MarkFaulted() => faulted = true;

    public Task<CodexProbe> ProbeAsync(CancellationToken cancellationToken = default)
    {
        if (failure is not null)
        {
            return Task.FromException<CodexProbe>(failure);
        }

        return Task.FromResult(new CodexProbe(
            new CodexInitializeResult("fixture", "C:\\fixture\\.codex", "windows", "windows"),
            new CodexAccount(null, null, true),
            Array.Empty<CodexModelOption>()));
    }

    public Task<IReadOnlyList<CodexThreadSummary>> ListThreadsAsync(
        int limit = 100,
        CancellationToken cancellationToken = default) => Task.FromResult(threads);

    public Task<CodexThreadSnapshot> ReadThreadAsync(
        string threadId,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public ValueTask DisposeAsync()
    {
        WasDisposed = true;
        return ValueTask.CompletedTask;
    }
}
