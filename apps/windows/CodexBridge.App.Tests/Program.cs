using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;
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
    ("workspace cancel restores ComboBox selection", WorkspaceCancelRestoresSelectionAsync),
    ("workspace cancel preserves current state", WorkspaceCancelPreservesStateAsync),
    ("workspace failure restores UI selection", WorkspaceFailureRestoresSelectionAsync),
    ("workspace success commits UI selection", WorkspaceSuccessCommitsSelectionAsync),
    ("workspace selection restore does not re-enter switch", WorkspaceRestoreDoesNotReenterAsync),
    ("context pack renders all sections in order", ContextPackRendersAsync),
    ("context pack keys distinguish same titles", ContextPackKeysAreDistinctAsync),
    ("context pack empty render is safe", ContextPackEmptyRenderAsync),
    ("context pack size boundaries classify correctly", ContextPackSizeClassificationAsync),
    ("context pack character estimate matches items", ContextPackCharacterEstimateAsync),
    ("theme shared styles remain outside color dictionaries", ThemeSharedStylesAreSeparatedAsync),
    ("light and dark theme keys are symmetric", ThemeColorKeysAreSymmetricAsync),
    ("handoff templates render ordered sections", HandoffTemplatesRenderAsync),
    ("handoff embeds context and git state", HandoffContextAndGitRenderAsync),
    ("handoff keeps normal user task", HandoffNormalTaskAsync),
    ("handoff removes attachment wrapper and path", HandoffAttachmentWrapperAsync),
    ("handoff preserves request after wrapper", HandoffWrapperRequestAsync),
    ("handoff rejects wrapper-only and giant task", HandoffUnsafeTaskAsync),
    ("handoff request marker wins over attachment title", HandoffRequestMarkerWinsAsync),
    ("handoff empty request marker never falls back", HandoffEmptyMarkerAsync),
    ("handoff accepts markdown request marker", HandoffMarkdownMarkerAsync),
    ("handoff uses final request marker", HandoffFinalMarkerAsync),
    ("handoff request marker is case insensitive", HandoffCaseInsensitiveMarkerAsync),
    ("handoff filters attachment path after marker", HandoffMarkerPathFilterAsync),
    ("handoff generate clears stale task", HandoffGenerateClearsStaleTaskAsync),
    ("handoff generate replaces stale task", HandoffGenerateReplacesTaskAsync),
    ("handoff generate does not use empty selected attachments", HandoffGenerateEmptySelectionAsync),
    ("handoff second generate clears first task", HandoffGenerateTwiceAsync),
    ("clear selection clears every selected turn", ClearSelectionClearsSelectedTurnsAsync),
    ("clear selection is safe when nothing is selected", ClearSelectionWithNothingSelectedAsync),
    ("clipboard copy succeeds on first attempt", ClipboardCopySucceedsImmediatelyAsync),
    ("clipboard copy retries transient contention", ClipboardCopyRetriesTransientContentionAsync),
    ("clipboard copy stops after maximum attempts", ClipboardCopyStopsAtMaximumAttemptsAsync),
    ("clipboard copy does not retry other COM errors", ClipboardCopyDoesNotRetryOtherComErrorsAsync),
    ("clipboard copy preserves text", ClipboardCopyPreservesTextAsync),
    ("clipboard busy then success stops immediately", ClipboardBusyThenSuccessStopsImmediatelyAsync),
    ("clipboard success clears stale exception", ClipboardSuccessClearsStaleExceptionAsync),
    ("clipboard side effect reports success", ClipboardSideEffectReportsSuccessAsync),
    ("clipboard side effect verification polls through busy reads", ClipboardSideEffectVerificationPollsAsync),
    ("clipboard verification match prevents another write", ClipboardVerificationMatchPreventsAnotherWriteAsync),
    ("clipboard verification miss allows the next write", ClipboardVerificationMissAllowsNextWriteAsync),
    ("clipboard all busy attempts remain failure", ClipboardAllBusyAttemptsRemainFailureAsync),
    ("handoff copy success replaces stale failure", HandoffCopySuccessReplacesStaleFailureAsync),
    ("handoff copy failure reports current reason", HandoffCopyFailureReportsCurrentReasonAsync),
    ("handoff copy keeps latest status", HandoffCopyKeepsLatestStatusAsync),
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

static async Task WorkspaceCancelRestoresSelectionAsync()
{
    var previousOverride = Environment.GetEnvironmentVariable("CODEX_BRIDGE_WORKSPACES_PATH");
    try
    {
        await using var scope = await TestScope.CreateAsync();
        Environment.SetEnvironmentVariable("CODEX_BRIDGE_WORKSPACES_PATH", Path.Combine(scope.Root, "workspaces.json"));
        await using var importer = new CaptureInboxImporter(scope.Repository, inboxDirectory: scope.Inbox);
        await using var viewModel = new MainWindowViewModel(scope.Repository, importer, FakeCodexClient.Success(), confirmWorkspaceSwitch: () => MessageBoxResult.Cancel);
        var current = new WorkspaceItem("current", scope.Root, DateTimeOffset.UtcNow);
        var targetPath = Path.Combine(scope.Root, "target");
        Directory.CreateDirectory(targetPath);
        var target = new WorkspaceItem("target", targetPath, DateTimeOffset.UtcNow);
        viewModel.Workspaces.Add(current);
        viewModel.Workspaces.Add(target);
        Assert(viewModel.TrySwitchWorkspace(current, requireConfirmation: false), "initial workspace setup failed");
        viewModel.ContextItems.Add(new ContextPackItem("Note", "keep", "test", "content", "keep", 0, "keep"));

        viewModel.WorkspaceSelection = target;

        Assert(viewModel.WorkspaceSelection?.Path == current.Path, "cancel did not restore ComboBox selection");
        Assert(viewModel.SelectedWorkspace?.Path == current.Path, "cancel changed committed workspace");
    }
    finally
    {
        Environment.SetEnvironmentVariable("CODEX_BRIDGE_WORKSPACES_PATH", previousOverride);
    }
}

static async Task WorkspaceCancelPreservesStateAsync()
{
    var previousOverride = Environment.GetEnvironmentVariable("CODEX_BRIDGE_WORKSPACES_PATH");
    try
    {
        await using var scope = await TestScope.CreateAsync();
        Environment.SetEnvironmentVariable("CODEX_BRIDGE_WORKSPACES_PATH", Path.Combine(scope.Root, "workspaces.json"));
        await using var importer = new CaptureInboxImporter(scope.Repository, inboxDirectory: scope.Inbox);
        await using var viewModel = new MainWindowViewModel(scope.Repository, importer, FakeCodexClient.Success(), confirmWorkspaceSwitch: () => MessageBoxResult.Cancel);
        var current = new WorkspaceItem("current", scope.Root, DateTimeOffset.UtcNow);
        var targetPath = Path.Combine(scope.Root, "target");
        Directory.CreateDirectory(targetPath);
        var target = new WorkspaceItem("target", targetPath, DateTimeOffset.UtcNow);
        viewModel.Workspaces.Add(current);
        viewModel.Workspaces.Add(target);
        Assert(viewModel.TrySwitchWorkspace(current, requireConfirmation: false), "initial workspace setup failed");
        viewModel.ContextItems.Add(new ContextPackItem("Note", "keep", "test", "content", "keep", 0, "keep"));
        viewModel.HandoffTask = "keep handoff";
        var projectRootPath = viewModel.ProjectRootItems.Single().FullPath;
        var projectFileCount = viewModel.ProjectFileCount;

        viewModel.WorkspaceSelection = target;

        Assert(viewModel.ContextItems.Count == 1, "cancel cleared context");
        Assert(viewModel.HandoffTask == "keep handoff", "cancel cleared handoff");
        Assert(viewModel.ProjectRootItems.Single().FullPath == projectRootPath, "cancel changed Project Explorer");
        Assert(viewModel.ProjectFileCount == projectFileCount, "cancel changed project file state");
    }
    finally
    {
        Environment.SetEnvironmentVariable("CODEX_BRIDGE_WORKSPACES_PATH", previousOverride);
    }
}

static async Task WorkspaceFailureRestoresSelectionAsync()
{
    var previousOverride = Environment.GetEnvironmentVariable("CODEX_BRIDGE_WORKSPACES_PATH");
    try
    {
        await using var scope = await TestScope.CreateAsync();
        Environment.SetEnvironmentVariable("CODEX_BRIDGE_WORKSPACES_PATH", Path.Combine(scope.Root, "workspaces.json"));
        await using var importer = new CaptureInboxImporter(scope.Repository, inboxDirectory: scope.Inbox);
        await using var viewModel = new MainWindowViewModel(scope.Repository, importer, FakeCodexClient.Success(), confirmWorkspaceSwitch: () => MessageBoxResult.OK);
        var current = new WorkspaceItem("current", scope.Root, DateTimeOffset.UtcNow);
        var missing = new WorkspaceItem("missing", Path.Combine(scope.Root, "missing"), DateTimeOffset.UtcNow);
        viewModel.Workspaces.Add(current);
        viewModel.Workspaces.Add(missing);
        Assert(viewModel.TrySwitchWorkspace(current, requireConfirmation: false), "initial workspace setup failed");
        viewModel.ContextItems.Add(new ContextPackItem("Note", "keep", "test", "content", "keep", 0, "keep"));

        viewModel.WorkspaceSelection = missing;

        Assert(viewModel.WorkspaceSelection?.Path == current.Path, "failed switch did not restore ComboBox selection");
        Assert(viewModel.SelectedWorkspace?.Path == current.Path, "failed switch changed committed workspace");
        Assert(viewModel.ContextItems.Count == 1, "failed switch cleared context");
    }
    finally
    {
        Environment.SetEnvironmentVariable("CODEX_BRIDGE_WORKSPACES_PATH", previousOverride);
    }
}

static async Task WorkspaceSuccessCommitsSelectionAsync()
{
    var previousOverride = Environment.GetEnvironmentVariable("CODEX_BRIDGE_WORKSPACES_PATH");
    try
    {
        await using var scope = await TestScope.CreateAsync();
        Environment.SetEnvironmentVariable("CODEX_BRIDGE_WORKSPACES_PATH", Path.Combine(scope.Root, "workspaces.json"));
        await using var importer = new CaptureInboxImporter(scope.Repository, inboxDirectory: scope.Inbox);
        await using var viewModel = new MainWindowViewModel(scope.Repository, importer, FakeCodexClient.Success(), confirmWorkspaceSwitch: () => MessageBoxResult.OK);
        var current = new WorkspaceItem("current", scope.Root, DateTimeOffset.UtcNow);
        var targetPath = Path.Combine(scope.Root, "target");
        Directory.CreateDirectory(targetPath);
        var target = new WorkspaceItem("target", targetPath, DateTimeOffset.UtcNow);
        viewModel.Workspaces.Add(current);
        viewModel.Workspaces.Add(target);
        Assert(viewModel.TrySwitchWorkspace(current, requireConfirmation: false), "initial workspace setup failed");
        viewModel.ContextItems.Add(new ContextPackItem("Note", "clear", "test", "content", "clear", 0, "clear"));
        viewModel.HandoffTask = "clear handoff";

        viewModel.WorkspaceSelection = target;

        Assert(viewModel.WorkspaceSelection?.Path == targetPath, "successful switch did not commit ComboBox selection");
        Assert(viewModel.SelectedWorkspace?.Path == targetPath, "successful switch did not commit workspace");
        Assert(viewModel.ContextItems.Count == 0, "successful switch did not clear context");
        Assert(string.IsNullOrEmpty(viewModel.HandoffTask), "successful switch did not clear handoff");
    }
    finally
    {
        Environment.SetEnvironmentVariable("CODEX_BRIDGE_WORKSPACES_PATH", previousOverride);
    }
}

static async Task WorkspaceRestoreDoesNotReenterAsync()
{
    var previousOverride = Environment.GetEnvironmentVariable("CODEX_BRIDGE_WORKSPACES_PATH");
    try
    {
        await using var scope = await TestScope.CreateAsync();
        Environment.SetEnvironmentVariable("CODEX_BRIDGE_WORKSPACES_PATH", Path.Combine(scope.Root, "workspaces.json"));
        await using var importer = new CaptureInboxImporter(scope.Repository, inboxDirectory: scope.Inbox);
        var confirmationCount = 0;
        await using var viewModel = new MainWindowViewModel(scope.Repository, importer, FakeCodexClient.Success(), confirmWorkspaceSwitch: () =>
        {
            confirmationCount++;
            return MessageBoxResult.Cancel;
        });
        var current = new WorkspaceItem("current", scope.Root, DateTimeOffset.UtcNow);
        var targetPath = Path.Combine(scope.Root, "target");
        Directory.CreateDirectory(targetPath);
        var target = new WorkspaceItem("target", targetPath, DateTimeOffset.UtcNow);
        viewModel.Workspaces.Add(current);
        viewModel.Workspaces.Add(target);
        Assert(viewModel.TrySwitchWorkspace(current, requireConfirmation: false), "initial workspace setup failed");
        viewModel.ContextItems.Add(new ContextPackItem("Note", "keep", "test", "content", "keep", 0, "keep"));

        viewModel.WorkspaceSelection = target;

        Assert(confirmationCount == 1, "selection restore re-entered workspace switch");
        Assert(viewModel.WorkspaceSelection?.Path == current.Path, "selection restore did not keep current workspace");
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

static Task ThemeSharedStylesAreSeparatedAsync()
{
    var colors = LoadThemeDocument("Colors.xaml");
    var styles = LoadThemeDocument("Styles.xaml");
    var colorKeys = ResourceKeys(colors);
    var styleKeys = ResourceKeys(styles);
    foreach (var key in new[] { "PrimaryButtonStyle", "SecondaryButtonStyle", "SessionListItemStyle", "ToolbarButtonStyle", "DisabledButtonStyle" })
    {
        Assert(styleKeys.Contains(key), $"shared style missing from Styles.xaml: {key}");
        Assert(!colorKeys.Contains(key), $"shared style leaked into Colors.xaml: {key}");
    }

    return Task.CompletedTask;
}

static Task ThemeColorKeysAreSymmetricAsync()
{
    var lightKeys = ResourceKeys(LoadThemeDocument("Colors.xaml"));
    var darkKeys = ResourceKeys(LoadThemeDocument("DarkColors.xaml"));
    Assert(lightKeys.SetEquals(darkKeys), "Light and Dark color dictionaries do not expose the same keys");
    return Task.CompletedTask;
}

static Task HandoffTemplatesRenderAsync()
{
    foreach (var template in new[] { "Continue Task", "Debug Issue", "Review Changes", "Plan Next Step" })
    {
        var text = HandoffService.Render(new HandoffInput("Codex", template, "Fixture", "task", "state", "constraints", "next", "workspace", "C:\\workspace", "main", [], [], 0, false, true, false, false));
        Assert(text.StartsWith("# Handoff", StringComparison.Ordinal), $"{template} missing header");
        Assert(text.Contains("Target: Codex", StringComparison.Ordinal), $"{template} missing target");
        Assert(text.IndexOf("## Current State", StringComparison.Ordinal) > 0 && text.Contains("## Constraints", StringComparison.Ordinal) || text.Contains("## Open Questions", StringComparison.Ordinal), $"{template} section order mismatch");
    }
    return Task.CompletedTask;
}

static Task HandoffContextAndGitRenderAsync()
{
    var text = HandoffService.Render(new HandoffInput("ChatGPT", "Continue Task", "Fixture", "task", "state", "constraints", "next", "workspace", "C:\\workspace", "main", [new GitChangedFile("M", "Program.cs")], [new ContextPackItem("ProjectFile", "README.md", "Workspace", "content", "README.md", 0, "key")], 4, true, true, true, true));
    Assert(text.Contains("Target: ChatGPT", StringComparison.Ordinal), "handoff target missing");
    Assert(text.Contains("## Important Context", StringComparison.Ordinal) && !text.Contains("# Context Pack", StringComparison.Ordinal), "context was wrapped with duplicate pack heading");
    Assert(text.Contains("Branch: main", StringComparison.Ordinal) && text.Contains("- M Program.cs", StringComparison.Ordinal), "git state missing");
    Assert(text.Contains("Files: 4", StringComparison.Ordinal), "project file summary missing");
    return Task.CompletedTask;
}

static Task HandoffNormalTaskAsync()
{
    Assert(HandoffService.NormalizeUserTask("Please diagnose the startup crash.") == "Please diagnose the startup crash.", "normal task changed");
    return Task.CompletedTask;
}

static Task HandoffAttachmentWrapperAsync()
{
    var input = "# Files pasted by the user:\n## attachment: C:\\fixture\\.codex\\attachments\\abc\\file.txt\nPasted text contains the user's request.";
    var result = HandoffService.NormalizeUserTask(input);
    Assert(!result.Contains("Files pasted", StringComparison.OrdinalIgnoreCase), "wrapper remained");
    Assert(!result.Contains(".codex\\attachments", StringComparison.OrdinalIgnoreCase), "attachment path remained");
    return Task.CompletedTask;
}

static Task HandoffWrapperRequestAsync()
{
    var input = "# Files pasted by the user:\nC:\\fixture\\.codex\\attachments\\abc\\file.txt\n## My request:\nPlease diagnose the project crash.";
    Assert(HandoffService.NormalizeUserTask(input) == "Please diagnose the project crash.", "real request after wrapper was lost");
    return Task.CompletedTask;
}

static Task HandoffUnsafeTaskAsync()
{
    var wrapperOnly = "# Files pasted by the user:\nC:\\fixture\\.codex\\attachments\\abc\\file.txt\nPasted text contains the user's request.";
    Assert(HandoffService.NormalizeUserTask(wrapperOnly) == string.Empty, "wrapper-only task was not empty");
    Assert(HandoffService.NormalizeUserTask(new string('x', 8001)) == string.Empty, "giant user turn was promoted to task");
    return Task.CompletedTask;
}

static Task HandoffRequestMarkerWinsAsync()
{
    var input = "Files pasted by the user:\nOLD ATTACHMENT TITLE\nMy request:\nREAL REQUEST";
    Assert(HandoffService.NormalizeUserTask(input) == "REAL REQUEST", "attachment title won over request marker");
    return Task.CompletedTask;
}

static Task HandoffEmptyMarkerAsync()
{
    var input = "Files pasted by the user:\nOLD ATTACHMENT TITLE\nMy request:\n";
    Assert(HandoffService.NormalizeUserTask(input) == string.Empty, "empty request marker fell back to attachment title");
    return Task.CompletedTask;
}

static Task HandoffMarkdownMarkerAsync()
{
    var input = "# Files pasted by the user:\n# Old heading\n## My request:\nFix current bug";
    Assert(HandoffService.NormalizeUserTask(input) == "Fix current bug", "markdown request marker was not recognized");
    return Task.CompletedTask;
}

static Task HandoffFinalMarkerAsync()
{
    var input = "My request:\nold request\nwrapper\nMy request:\nnew request";
    Assert(HandoffService.NormalizeUserTask(input) == "new request", "final request marker was not used");
    return Task.CompletedTask;
}

static Task HandoffCaseInsensitiveMarkerAsync()
{
    Assert(HandoffService.NormalizeUserTask("MY REQUEST:\nFix this") == "Fix this", "case-insensitive request marker failed");
    return Task.CompletedTask;
}

static Task HandoffMarkerPathFilterAsync()
{
    var input = "My request:\nC:\\fixture\\.codex\\attachments\\a.txt\nFix this";
    var result = HandoffService.NormalizeUserTask(input);
    Assert(result == "Fix this" && !result.Contains("attachments", StringComparison.OrdinalIgnoreCase), "attachment path after marker remained");
    return Task.CompletedTask;
}

static async Task HandoffGenerateClearsStaleTaskAsync()
{
    await using var scope = await TestScope.CreateAsync();
    await using var importer = new CaptureInboxImporter(scope.Repository, inboxDirectory: scope.Inbox);
    await using var viewModel = new MainWindowViewModel(scope.Repository, importer, FakeCodexClient.Success());
    viewModel.HandoffTask = "OLD TASK";
    viewModel.Turns.Add(HandoffTurn("Files pasted by the user:\nold attachment\nMy request:\n"));
    viewModel.GenerateHandoffCommand.Execute(null);
    Assert(viewModel.HandoffTask == string.Empty, "generate retained stale task");
}

static async Task HandoffGenerateReplacesTaskAsync()
{
    await using var scope = await TestScope.CreateAsync();
    await using var importer = new CaptureInboxImporter(scope.Repository, inboxDirectory: scope.Inbox);
    await using var viewModel = new MainWindowViewModel(scope.Repository, importer, FakeCodexClient.Success());
    viewModel.HandoffTask = "OLD TASK";
    viewModel.Turns.Add(HandoffTurn("My request:\nNEW TASK"));
    viewModel.GenerateHandoffCommand.Execute(null);
    Assert(viewModel.HandoffTask == "NEW TASK", "generate did not replace stale task");
}

static async Task HandoffGenerateEmptySelectionAsync()
{
    await using var scope = await TestScope.CreateAsync();
    await using var importer = new CaptureInboxImporter(scope.Repository, inboxDirectory: scope.Inbox);
    await using var viewModel = new MainWindowViewModel(scope.Repository, importer, FakeCodexClient.Success());
    viewModel.HandoffTask = "OLD TASK";
    viewModel.Turns.Add(HandoffTurn("My request:\n"));
    viewModel.Turns.Add(HandoffTurn("Files pasted by the user:\nold attachment"));
    viewModel.GenerateHandoffCommand.Execute(null);
    Assert(viewModel.HandoffTask == string.Empty, "empty selected attachments became task fallback");
}

static async Task HandoffGenerateTwiceAsync()
{
    await using var scope = await TestScope.CreateAsync();
    await using var importer = new CaptureInboxImporter(scope.Repository, inboxDirectory: scope.Inbox);
    await using var viewModel = new MainWindowViewModel(scope.Repository, importer, FakeCodexClient.Success());
    viewModel.Turns.Add(HandoffTurn("My request:\nFIRST"));
    viewModel.GenerateHandoffCommand.Execute(null);
    Assert(viewModel.HandoffTask == "FIRST", "first generate failed");
    viewModel.Turns.Clear();
    viewModel.Turns.Add(HandoffTurn("My request:\n"));
    viewModel.GenerateHandoffCommand.Execute(null);
    Assert(viewModel.HandoffTask == string.Empty, "second generate retained first task");
}

static async Task ClearSelectionClearsSelectedTurnsAsync()
{
    await using var scope = await TestScope.CreateAsync();
    await scope.Repository.SaveAsync(new CaptureValidator().Validate(CaptureJson.Serialize(SelectionPayload(3))));
    await using var importer = new CaptureInboxImporter(scope.Repository, inboxDirectory: scope.Inbox);
    await using var viewModel = new MainWindowViewModel(scope.Repository, importer, FakeCodexClient.Success());
    await viewModel.RefreshAsync();
    viewModel.Turns[2].IsSelected = false;
    Assert(viewModel.Turns.Select(turn => turn.IsSelected).SequenceEqual([true, true, false]), "selection fixture was not loaded");
    var draftBefore = viewModel.DraftText;
    Assert(!string.IsNullOrEmpty(draftBefore), "selection fixture did not produce a draft");
    viewModel.HandoffTask = "KEEP HANDOFF";

    viewModel.ClearSelectionCommand.Execute(null);

    Assert(viewModel.Turns.All(turn => !turn.IsSelected), "clear selection left selected turns");
    Assert(viewModel.DraftText == draftBefore, "clear selection changed the draft");
    Assert(viewModel.HandoffTask == "KEEP HANDOFF", "clear selection changed the handoff");
}

static async Task ClearSelectionWithNothingSelectedAsync()
{
    await using var scope = await TestScope.CreateAsync();
    await scope.Repository.SaveAsync(new CaptureValidator().Validate(CaptureJson.Serialize(SelectionPayload(2))));
    await using var importer = new CaptureInboxImporter(scope.Repository, inboxDirectory: scope.Inbox);
    await using var viewModel = new MainWindowViewModel(scope.Repository, importer, FakeCodexClient.Success());
    await viewModel.RefreshAsync();
    foreach (var turn in viewModel.Turns)
    {
        turn.IsSelected = false;
    }

    viewModel.ClearSelectionCommand.Execute(null);

    Assert(viewModel.Turns.All(turn => !turn.IsSelected), "clear selection changed an empty selection");
}

static TurnRowViewModel HandoffTurn(string userText)
{
    var fixture = LoadFixture().Turns[0];
    return new TurnRowViewModel(fixture with { User = fixture.User with { Text = userText } }, true);
}

static CapturePayload SelectionPayload(int turnCount)
{
    var fixture = LoadFixture();
    var seed = fixture.Turns[0];
    var turns = Enumerable.Range(0, turnCount).Select(index => seed with
    {
        Id = $"turn-{index}",
        Index = index,
        User = seed.User with { Id = $"user-{index}" },
        Assistant = seed.Assistant is null ? null : seed.Assistant with { Id = $"assistant-{index}" },
    }).ToList();
    return fixture with
    {
        Turns = turns,
        Selection = fixture.Selection with
        {
            SelectedTurnIds = Enumerable.Range(0, turnCount).Select(index => $"turn-{index}").ToList(),
        },
    };
}

static async Task ClipboardCopySucceedsImmediatelyAsync()
{
    var calls = 0;
    var service = new ClipboardService(_ => calls++, _ => Task.CompletedTask);

    var result = await service.CopyTextAsync("text");

    Assert(result.Succeeded, "first clipboard write did not succeed");
    Assert(calls == 1, $"clipboard writer was called {calls} times");
}

static async Task ClipboardCopyRetriesTransientContentionAsync()
{
    var calls = 0;
    var delays = 0;
    var service = new ClipboardService(
        _ =>
        {
            calls++;
            if (calls < 3) throw ClipboardBusyException();
        },
        _ => { delays++; return Task.CompletedTask; });

    var result = await service.CopyTextAsync("text");

    Assert(result.Succeeded, "clipboard retry did not eventually succeed");
    Assert(calls == 3, $"clipboard writer was called {calls} times instead of 3");
    Assert(delays == 10, $"clipboard verification/retry delayed {delays} times instead of 10");
}

static async Task ClipboardCopyStopsAtMaximumAttemptsAsync()
{
    var calls = 0;
    var service = new ClipboardService(
        _ => { calls++; throw ClipboardBusyException(); },
        _ => Task.CompletedTask);

    var result = await service.CopyTextAsync("text");

    Assert(!result.Succeeded, "clipboard copy unexpectedly succeeded");
    Assert(calls == 5, $"clipboard writer was called {calls} times instead of 5");
    Assert(result.Error is COMException { HResult: ClipboardService.ClipboardCantOpenHResult }, "clipboard contention error was not returned");
}

static async Task ClipboardCopyDoesNotRetryOtherComErrorsAsync()
{
    var calls = 0;
    var service = new ClipboardService(
        _ => { calls++; throw new COMException("other failure", unchecked((int)0x80004005)); },
        _ => Task.CompletedTask);

    var result = await service.CopyTextAsync("text");

    Assert(!result.Succeeded, "unrelated COM error unexpectedly succeeded");
    Assert(calls == 1, $"unrelated COM error was retried {calls} times");
}

static async Task ClipboardCopyPreservesTextAsync()
{
    const string expected = "第一行\r\n```csharp\r\nConsole.WriteLine(\"原样\");\r\n```";
    string? actual = null;
    var service = new ClipboardService(text => actual = text, _ => Task.CompletedTask);

    var result = await service.CopyTextAsync(expected);

    Assert(result.Succeeded, "clipboard copy failed");
    Assert(actual == expected, "clipboard service modified the copied text");
}

static async Task ClipboardBusyThenSuccessStopsImmediatelyAsync()
{
    var calls = 0;
    var service = new ClipboardService(
        _ =>
        {
            calls++;
            if (calls == 1) throw ClipboardBusyException();
        },
        _ => Task.CompletedTask);

    var result = await service.CopyTextAsync("text");

    Assert(result.Succeeded, "busy then success was not reported as success");
    Assert(calls == 2, $"successful retry continued to attempt {calls} writes");
}

static async Task ClipboardSuccessClearsStaleExceptionAsync()
{
    var calls = 0;
    var service = new ClipboardService(
        _ =>
        {
            calls++;
            if (calls == 1) throw ClipboardBusyException();
        },
        _ => Task.CompletedTask);

    var result = await service.CopyTextAsync("text");

    Assert(result.Succeeded, "retry success was not reported as success");
    Assert(result.Error is null, "retry success retained the previous exception");
}

static async Task ClipboardSideEffectReportsSuccessAsync()
{
    const string expected = "handoff body";
    string? writtenText = null;
    var calls = 0;
    var service = new ClipboardService(
        text =>
        {
            calls++;
            writtenText = text;
            throw ClipboardBusyException();
        },
        _ => Task.CompletedTask,
        () => writtenText);

    var result = await service.CopyTextAsync(expected);

    Assert(writtenText == expected, "writer did not receive the complete input");
    Assert(result.Succeeded, "a successful clipboard side effect was reported as failure");
    Assert(result.Error is null, "side-effect success retained an exception");
    Assert(calls == 1, $"side-effect success retried {calls} times");
}

static async Task ClipboardSideEffectVerificationPollsAsync()
{
    const string expected = "handoff body";
    string? writtenText = null;
    var reads = 0;
    var service = new ClipboardService(
        text =>
        {
            writtenText = text;
            throw ClipboardBusyException();
        },
        _ => Task.CompletedTask,
        () =>
        {
            reads++;
            if (reads < 3) throw ClipboardBusyException();
            return writtenText;
        });

    var result = await service.CopyTextAsync(expected);

    Assert(result.Succeeded && result.Error is null, "polling did not convert the verified side effect to success");
    Assert(reads == 3, $"verification stopped after {reads} reads instead of the third match");
}

static async Task ClipboardVerificationMatchPreventsAnotherWriteAsync()
{
    var writes = 0;
    var service = new ClipboardService(
        _ =>
        {
            writes++;
            throw ClipboardBusyException();
        },
        _ => Task.CompletedTask,
        () => "expected");

    var result = await service.CopyTextAsync("expected");

    Assert(result.Succeeded && result.Error is null, "verification match was not successful");
    Assert(writes == 1, $"verification match allowed {writes} write attempts");
}

static async Task ClipboardVerificationMissAllowsNextWriteAsync()
{
    var writes = 0;
    var service = new ClipboardService(
        _ =>
        {
            writes++;
            if (writes == 1) throw ClipboardBusyException();
        },
        _ => Task.CompletedTask,
        () => "CLIPTEST-OLD");

    var result = await service.CopyTextAsync("expected");

    Assert(result.Succeeded && result.Error is null, "next write did not recover after verification miss");
    Assert(writes == 2, $"verification miss made {writes} write attempts");
}

static async Task ClipboardAllBusyAttemptsRemainFailureAsync()
{
    var writes = 0;
    var service = new ClipboardService(
        _ =>
        {
            writes++;
            throw ClipboardBusyException();
        },
        _ => Task.CompletedTask,
        () => throw ClipboardBusyException());

    var result = await service.CopyTextAsync("expected");

    Assert(!result.Succeeded, "all busy attempts unexpectedly succeeded");
    Assert(writes == 5, $"all busy attempts made {writes} writes instead of 5");
}

static COMException ClipboardBusyException() => new("clipboard busy", ClipboardService.ClipboardCantOpenHResult);

static async Task HandoffCopySuccessReplacesStaleFailureAsync()
{
    var calls = 0;
    var service = new ClipboardService(_ =>
    {
        calls++;
        if (calls == 1) throw new InvalidOperationException("old failure");
    }, _ => Task.CompletedTask);
    await using var scope = await TestScope.CreateAsync();
    await using var importer = new CaptureInboxImporter(scope.Repository, inboxDirectory: scope.Inbox);
    await using var viewModel = new MainWindowViewModel(scope.Repository, importer, FakeCodexClient.Success(), clipboardService: service);
    viewModel.HandoffTitle = "Fixture";

    viewModel.CopyHandoffCommand.Execute(null);
    await WaitForStatusAsync(viewModel, "Copy failed: old failure");
    viewModel.CopyHandoffCommand.Execute(null);
    await WaitForStatusAsync(viewModel, "Handoff copied.");

    Assert(viewModel.StatusText == "Handoff copied.", "successful Handoff copy did not replace stale failure");
}

static async Task HandoffCopyFailureReportsCurrentReasonAsync()
{
    var service = new ClipboardService(_ => throw new InvalidOperationException("current failure"), _ => Task.CompletedTask);
    await using var scope = await TestScope.CreateAsync();
    await using var importer = new CaptureInboxImporter(scope.Repository, inboxDirectory: scope.Inbox);
    await using var viewModel = new MainWindowViewModel(scope.Repository, importer, FakeCodexClient.Success(), clipboardService: service);
    viewModel.HandoffTitle = "Fixture";

    viewModel.CopyHandoffCommand.Execute(null);
    await WaitForStatusAsync(viewModel, "Copy failed: current failure");

    Assert(viewModel.StatusText == "Copy failed: current failure", "current Handoff copy failure reason was not shown");
}

static async Task HandoffCopyKeepsLatestStatusAsync()
{
    var calls = 0;
    var service = new ClipboardService(_ =>
    {
        calls++;
        if (calls == 1) throw new InvalidOperationException("stale failure");
    }, _ => Task.CompletedTask);
    await using var scope = await TestScope.CreateAsync();
    await using var importer = new CaptureInboxImporter(scope.Repository, inboxDirectory: scope.Inbox);
    await using var viewModel = new MainWindowViewModel(scope.Repository, importer, FakeCodexClient.Success(), clipboardService: service);
    viewModel.HandoffTitle = "Fixture";

    viewModel.CopyHandoffCommand.Execute(null);
    await WaitForStatusAsync(viewModel, "Copy failed: stale failure");
    viewModel.CopyHandoffCommand.Execute(null);
    await WaitForStatusAsync(viewModel, "Handoff copied.");
    await Task.Delay(100);

    Assert(viewModel.StatusText == "Handoff copied.", "an older Handoff status overwrote the latest success");
}

static async Task WaitForStatusAsync(MainWindowViewModel viewModel, string expected)
{
    for (var attempt = 0; attempt < 100; attempt++)
    {
        if (viewModel.StatusText == expected) return;
        await Task.Delay(10);
    }

    throw new InvalidOperationException($"status did not become '{expected}', actual '{viewModel.StatusText}'");
}

static XDocument LoadThemeDocument(string fileName)
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "apps", "windows", "CodexBridge.App", "Themes")))
    {
        directory = directory.Parent;
    }

    var path = directory is null
        ? throw new InvalidOperationException("repository root not found for theme test")
        : Path.Combine(directory.FullName, "apps", "windows", "CodexBridge.App", "Themes", fileName);
    return XDocument.Load(path);
}

static HashSet<string> ResourceKeys(XDocument document)
{
    XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
    return document.Root?.Elements().Select(element => (string?)element.Attribute(x + "Key"))
        .Where(key => key is not null)
        .Cast<string>()
        .ToHashSet(StringComparer.Ordinal)
        ?? [];
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
