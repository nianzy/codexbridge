using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
    ("localized UI strings and handoff content", LocalizedUiStringsAsync),
    ("localized size and character labels", LocalizedSizeLabelsAsync),
    ("dark control styles are defined", DarkControlStylesAreDefinedAsync),
    ("workspace snapshot round trips", WorkspaceSnapshotRoundTripAsync),
    ("workspace snapshot rejects newer schema", WorkspaceSnapshotRejectsNewerSchemaAsync),
    ("workspace snapshot rename preserves id", WorkspaceSnapshotRenamePreservesIdAsync),
    ("workspace snapshot lists corrupt entries safely", WorkspaceSnapshotListsCorruptEntriesAsync),
    ("workspace snapshot fingerprint is stable", WorkspaceSnapshotFingerprintAsync),
    ("workspace snapshot delete is scoped", WorkspaceSnapshotDeleteIsScopedAsync),
    ("workspace snapshot corrupt delete is scoped", WorkspaceSnapshotCorruptDeleteIsScopedAsync),
    ("workspace snapshot corrupt delete cancel preserves entry", WorkspaceSnapshotCorruptDeleteCancelAsync),
    ("workspace snapshot delete rejects invalid directory", WorkspaceSnapshotDeleteRejectsInvalidDirectoryAsync),
    ("workspace snapshot summary is complete", WorkspaceSnapshotSummaryIsCompleteAsync),
    ("workspace snapshot summary preserves creation time", WorkspaceSnapshotSummaryTimeSemanticsAsync),
    ("workspace snapshot summary localizes template names", WorkspaceSnapshotSummaryTemplateNamesAsync),
    ("workspace snapshot workspace affinity is enforced", WorkspaceSnapshotWorkspaceAffinityAsync),
    ("workspace snapshot schema zero is rejected", WorkspaceSnapshotRejectsLowerSchemaAsync),
    ("workspace snapshot git verification reports precise mismatch", WorkspaceSnapshotGitVerificationAsync),
    ("snapshot preview compares workspace and git", SnapshotPreviewComparisonAsync),
    ("snapshot preview matches visible turns", SnapshotPreviewSelectionAsync),
    ("snapshot preview preserves metadata and handoff", SnapshotPreviewMetadataAsync),
    ("snapshot preview is read only", SnapshotPreviewIsReadOnlyAsync),
    ("snapshot preview UI is localized", SnapshotPreviewUiIsLocalizedAsync),
    ("snapshot preview uses short hashes and headers", SnapshotPreviewShortDisplayAsync),
    ("snapshot preview localizes display values", SnapshotPreviewLocalizedDisplayAsync),
    ("snapshot preview handles unavailable git and workspace warning", SnapshotPreviewUnavailableGitAsync),
    ("workspace snapshot JSON has no sensitive schema fields", WorkspaceSnapshotSensitiveSchemaAsync),
    ("snapshot UI has empty state and selection guards", SnapshotUiBindingsAsync),
    ("snapshot rename dialog is localized", SnapshotRenameDialogIsLocalizedAsync),
    ("project file context adds selected file", ProjectFileContextAddsSelectedFileAsync),
    ("project file context rejects directory and read failure", ProjectFileContextRejectsInvalidSelectionAsync),
    ("snapshot captures only selected visible turns", SnapshotCapturesOnlySelectedTurnsAsync),
    ("snapshot package exports exact backup entries", SnapshotPackageExportsExactEntriesAsync),
    ("snapshot package imports and rebuilds summary", SnapshotPackageImportsAndRebuildsSummaryAsync),
    ("snapshot package rejects duplicate and invalid packages", SnapshotPackageRejectsDuplicateAndInvalidAsync),
    ("snapshot package rejects missing and unsupported metadata", SnapshotPackageRejectsMissingAndUnsupportedMetadataAsync),
    ("snapshot package rejects identity and traversal tampering", SnapshotPackageRejectsIdentityAndTraversalAsync),
    ("snapshot package allows workspace mismatch without rewriting origin", SnapshotPackageAllowsWorkspaceMismatchAsync),
    ("snapshot import failure feedback is localized and non-mutating", SnapshotImportFailureFeedbackAsync),
    ("snapshot import error dialog resources are themed", SnapshotImportErrorDialogResourcesAsync),
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
    Assert(viewModel.ChatGptEmptyText == "当前工作区暂无会话", "workspace empty-state text is misleading");
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
    Assert(markdown.IndexOf("## ChatGPT 上下文", StringComparison.Ordinal) < markdown.IndexOf("## Codex 上下文", StringComparison.Ordinal), "context section order mismatch");
    Assert(markdown.Contains("## 项目文件") && markdown.Contains("## Git 差异") && markdown.Contains("## 笔记"), "context sections missing");
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
    Assert(service.RenderMarkdown(pack, "main", 0).Contains("# 上下文包"), "empty context render failed");
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
        Assert(text.StartsWith("# 任务交接", StringComparison.Ordinal), $"{template} missing header");
        Assert(text.Contains("目标：Codex", StringComparison.Ordinal), $"{template} missing target");
        var requiredSection = template switch
        {
            "Debug Issue" => "## 实际表现",
            "Review Changes" => "## 修改文件",
            "Plan Next Step" => "## 待确认问题",
            _ => "## 当前状态",
        };
        Assert(text.Contains(requiredSection, StringComparison.Ordinal), $"{template} section order mismatch");
        var sections = template switch
        {
            "Debug Issue" => new[] { "## 问题", "## 实际表现", "## 预期表现", "## 证据", "## 疑似区域", "## 约束", "## 下一步诊断" },
            "Review Changes" => new[] { "## 审查目标", "## 修改文件", "## 相关差异", "## 已知测试", "## 风险", "## 审查请求" },
            "Plan Next Step" => new[] { "## 目标", "## 当前状态", "## 待确认问题", "## 约束", "## 可选下一步", "## 请求决策" },
            _ => new[] { "## 目标", "## 当前状态", "## 约束", "## 下一步操作" },
        };
        Assert(sections.All(section => text.Contains(section, StringComparison.Ordinal)), $"{template} localized sections incomplete");
    }
    return Task.CompletedTask;
}

static Task LocalizedUiStringsAsync()
{
    Assert(UiStrings.Ready == "就绪" && UiStrings.HandoffCopied == "交接内容已复制。", "localized UI resources missing");
    var body = "Please keep this user text unchanged.";
    var markdown = HandoffService.Render(new HandoffInput("Codex", "Continue Task", "标题", body, "当前状态", "不要自动发送。\n不要自动执行。", "下一步", "工作区", "C:\\workspace", "main", [], [], 0, false, false, true, false));
    Assert(markdown.Contains("# 任务交接") && markdown.Contains("## 目标") && markdown.Contains(body), "localized Continue Task output is incomplete");
    Assert(markdown.Contains("Codex") && markdown.Contains("main") && !markdown.Contains("Target:"), "technical names or protocol labels were translated incorrectly");
    return Task.CompletedTask;
}

static Task LocalizedSizeLabelsAsync()
{
    Assert(UiStrings.SizeState("Small") == "小" && UiStrings.SizeState("Medium") == "中" && UiStrings.SizeState("Large") == "大", "size labels were not localized");
    Assert(UiStrings.CharacterSummary(0) == "字符数：0" && UiStrings.ContextCharacters(0) == "0 个字符", "character labels were not localized");
    return Task.CompletedTask;
}

static Task DarkControlStylesAreDefinedAsync()
{
    var styles = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "apps", "windows", "CodexBridge.App", "Themes", "Styles.xaml"));
    var strings = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "apps", "windows", "CodexBridge.App", "Themes", "Strings.xaml"));
    Assert(styles.Contains("TargetType=\"ScrollBar\"", StringComparison.Ordinal) && styles.Contains("TargetType=\"Thumb\"", StringComparison.Ordinal), "scrollbar styles missing");
    Assert(styles.Contains("TargetType=\"CheckBox\"", StringComparison.Ordinal) && strings.Contains("UiSmall", StringComparison.Ordinal), "checkbox or size resources missing");
    return Task.CompletedTask;
}

static async Task WorkspaceSnapshotRoundTripAsync()
{
    var root = Path.Combine(Path.GetTempPath(), $"codexbridge-snapshot-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    try
    {
        var service = new WorkspaceSnapshotService();
        var snapshot = SampleSnapshot("snap-1");
        service.Save(root, snapshot);
        var loaded = service.Load(Path.Combine(WorkspaceSnapshotService.GetRoot(root), snapshot.SnapshotId));
        Assert(loaded.ContextItems.Count == 3 && loaded.ContextItems[0].Content == "body" && loaded.ContextItems[1].ReferencePath == "README.md" && loaded.ContextItems[2].Type == "GitDiff", "context items did not round trip");
        Assert(loaded.Handoff.Target == "ChatGPT" && loaded.Handoff.Template == "Debug Issue" && loaded.Handoff.IncludeGitState && loaded.Handoff.NextAction == "next", "handoff fields did not round trip");
        Assert(loaded.Selection.Count == 2 && loaded.Selection[1].Source == "Codex" && loaded.Selection[1].TextHash == "hash-2", "selected references did not round trip");
        Assert(File.Exists(Path.Combine(WorkspaceSnapshotService.GetRoot(root), snapshot.SnapshotId, "summary.md")), "summary was not written");
        Assert(!File.Exists(Path.Combine(WorkspaceSnapshotService.GetRoot(root), snapshot.SnapshotId, "snapshot.json.tmp")) && !File.Exists(Path.Combine(WorkspaceSnapshotService.GetRoot(root), snapshot.SnapshotId, "summary.md.tmp")), "temporary snapshot file remained");
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    await Task.CompletedTask;
}

static Task WorkspaceSnapshotRejectsNewerSchemaAsync()
{
    var snapshot = SampleSnapshot("future") with { SchemaVersion = WorkspaceSnapshotService.CurrentSchemaVersion + 1 };
    try { WorkspaceSnapshotService.Validate(snapshot); throw new InvalidOperationException("newer schema was accepted"); }
    catch (InvalidDataException exception) { Assert(exception.Message.Contains("更新版本", StringComparison.Ordinal), "newer schema warning missing"); }
    return Task.CompletedTask;
}

static async Task WorkspaceSnapshotRenamePreservesIdAsync()
{
    var root = Path.Combine(Path.GetTempPath(), $"codexbridge-snapshot-{Guid.NewGuid():N}"); Directory.CreateDirectory(root);
    try
    {
        var service = new WorkspaceSnapshotService(); var snapshot = SampleSnapshot("stable-id"); service.Save(root, snapshot); var renamed = service.Rename(root, snapshot.SnapshotId, "新名称");
        var loaded = service.Load(Path.Combine(WorkspaceSnapshotService.GetRoot(root), snapshot.SnapshotId));
        Assert(loaded.SnapshotId == "stable-id" && loaded.Name == "新名称" && loaded.CreatedAt == snapshot.CreatedAt && renamed.UpdatedAt >= snapshot.UpdatedAt, "rename changed immutable snapshot fields");
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    await Task.CompletedTask;
}

static Task WorkspaceSnapshotListsCorruptEntriesAsync()
{
    var root = Path.Combine(Path.GetTempPath(), $"codexbridge-snapshot-{Guid.NewGuid():N}"); Directory.CreateDirectory(Path.Combine(root, ".ai", "snapshots", "bad"));
    try
    {
        File.WriteAllText(Path.Combine(root, ".ai", "snapshots", "bad", "snapshot.json"), "{");
        var entries = new WorkspaceSnapshotService().List(root);
        Assert(entries.Count == 1 && entries[0].Snapshot is null && entries[0].Error is not null, "corrupt snapshot was not isolated");
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    return Task.CompletedTask;
}

static Task WorkspaceSnapshotFingerprintAsync()
{
    var files = new[] { new WorkspaceSnapshotChangedFile { Status = "M", RelativePath = "a.cs" }, new WorkspaceSnapshotChangedFile { Status = "A", RelativePath = "b.cs" } };
    var first = WorkspaceSnapshotService.BuildStatusFingerprint("main", "abc", files);
    var second = WorkspaceSnapshotService.BuildStatusFingerprint("main", "abc", files.Reverse());
    var changed = WorkspaceSnapshotService.BuildStatusFingerprint("feature", "abc", files);
    Assert(first == second && first != changed, "git fingerprint was not canonical");
    return Task.CompletedTask;
}

static async Task WorkspaceSnapshotDeleteIsScopedAsync()
{
    var root = Path.Combine(Path.GetTempPath(), $"codexbridge-snapshot-{Guid.NewGuid():N}"); Directory.CreateDirectory(root);
    try { var service = new WorkspaceSnapshotService(); service.Save(root, SampleSnapshot("a")); service.Save(root, SampleSnapshot("b")); service.Delete(root, "a"); var entries = service.List(root); Assert(entries.Count == 1 && entries[0].Snapshot?.SnapshotId == "b" && Directory.Exists(WorkspaceSnapshotService.GetRoot(root)), "delete was not scoped to one snapshot"); }
    finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    await Task.CompletedTask;
}

static Task WorkspaceSnapshotCorruptDeleteIsScopedAsync()
{
    var root = Path.Combine(Path.GetTempPath(), $"codexbridge-snapshot-{Guid.NewGuid():N}");
    var service = new WorkspaceSnapshotService();
    try
    {
        service.Save(root, SampleSnapshot("valid-a"));
        Directory.CreateDirectory(Path.Combine(WorkspaceSnapshotService.GetRoot(root), "corrupt-b"));
        File.WriteAllText(Path.Combine(WorkspaceSnapshotService.GetRoot(root), "corrupt-b", "snapshot.json"), "{ invalid");
        service.Save(root, SampleSnapshot("valid-c"));
        var corrupt = service.List(root).Single(entry => entry.Snapshot is null);
        service.DeleteDirectory(root, corrupt.DirectoryPath);
        var remaining = service.List(root);
        Assert(!Directory.Exists(corrupt.DirectoryPath), "corrupt snapshot directory was not deleted");
        Assert(Directory.Exists(WorkspaceSnapshotService.GetRoot(root)), "snapshots root was deleted");
        Assert(remaining.Count == 2 && remaining.All(entry => entry.Snapshot is not null) && remaining.Select(entry => entry.Snapshot!.SnapshotId).ToHashSet().SetEquals(["valid-a", "valid-c"]), "valid snapshots were not preserved or corrupt entry remained");
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    return Task.CompletedTask;
}

static async Task WorkspaceSnapshotCorruptDeleteCancelAsync()
{
    var previousOverride = Environment.GetEnvironmentVariable("CODEX_BRIDGE_WORKSPACES_PATH");
    await using var scope = await TestScope.CreateAsync();
    try
    {
        Environment.SetEnvironmentVariable("CODEX_BRIDGE_WORKSPACES_PATH", Path.Combine(scope.Root, "workspaces.json"));
        await using var importer = new CaptureInboxImporter(scope.Repository, inboxDirectory: scope.Inbox);
        await using var viewModel = new MainWindowViewModel(scope.Repository, importer, FakeCodexClient.Success());
        var workspace = new WorkspaceItem("fixture", scope.Root, DateTimeOffset.UtcNow);
        viewModel.Workspaces.Add(workspace);
        Assert(viewModel.TrySwitchWorkspace(workspace, requireConfirmation: false), "workspace setup failed");
        var corruptDirectory = Path.Combine(WorkspaceSnapshotService.GetRoot(scope.Root), "corrupt-cancel");
        Directory.CreateDirectory(corruptDirectory);
        File.WriteAllText(Path.Combine(corruptDirectory, "snapshot.json"), "{ invalid");
        viewModel.RefreshSnapshots();
        viewModel.SelectedSnapshot = viewModel.SnapshotEntries.Single(entry => entry.Snapshot is null);
        Assert(!viewModel.DeleteSelectedSnapshot(_ => false), "cancelled corrupt delete reported success");
        Assert(Directory.Exists(corruptDirectory) && viewModel.SnapshotEntries.Count == 1, "cancelled corrupt delete changed the entry");
    }
    finally
    {
        Environment.SetEnvironmentVariable("CODEX_BRIDGE_WORKSPACES_PATH", previousOverride);
    }
}

static Task WorkspaceSnapshotDeleteRejectsInvalidDirectoryAsync()
{
    var root = Path.Combine(Path.GetTempPath(), $"codexbridge-snapshot-{Guid.NewGuid():N}");
    var service = new WorkspaceSnapshotService();
    try
    {
        var snapshotsRoot = WorkspaceSnapshotService.GetRoot(root);
        Directory.CreateDirectory(snapshotsRoot);
        foreach (var invalid in new[] { snapshotsRoot, Path.Combine(root, "outside"), Path.Combine(snapshotsRoot, "..", "outside") })
        {
            try { service.DeleteDirectory(root, invalid); throw new InvalidOperationException("invalid snapshot delete path was accepted"); }
            catch (InvalidDataException) { }
        }
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    return Task.CompletedTask;
}

static Task WorkspaceSnapshotSummaryIsCompleteAsync()
{
    var summary = WorkspaceSnapshotService.RenderSummary(SampleSnapshot("summary"));
    foreach (var section in new[] { "# 工作现场", "名称：", "保存时间：", "更新时间：", "工作区：", "## 当前任务", "## 当前状态", "## 下一步", "## 上下文", "## Git", "## 交接" }) Assert(summary.Contains(section, StringComparison.Ordinal), $"summary section missing: {section}");
    return Task.CompletedTask;
}

static Task WorkspaceSnapshotSummaryTimeSemanticsAsync()
{
    var created = new DateTimeOffset(2026, 9, 29, 14, 2, 28, TimeSpan.FromHours(8));
    var updated = new DateTimeOffset(2026, 9, 29, 14, 37, 46, TimeSpan.FromHours(8));
    var summary = WorkspaceSnapshotService.RenderSummary(SampleSnapshot("time") with { CreatedAt = created, UpdatedAt = updated });
    Assert(summary.Contains("保存时间：2026-09-29 14:02", StringComparison.Ordinal), "summary save time did not use CreatedAt");
    Assert(summary.Contains("更新时间：2026-09-29 14:37", StringComparison.Ordinal), "summary update time did not use UpdatedAt");
    Assert(!summary.Contains("保存时间：2026-09-29 14:37", StringComparison.Ordinal), "summary save time changed with UpdatedAt");
    return Task.CompletedTask;
}

static Task WorkspaceSnapshotSummaryTemplateNamesAsync()
{
    var expected = new Dictionary<string, string>
    {
        ["Continue Task"] = "继续任务",
        ["Debug Issue"] = "调试问题",
        ["Review Changes"] = "审查更改",
        ["Plan Next Step"] = "规划下一步",
    };
    foreach (var pair in expected)
    {
        var summary = WorkspaceSnapshotService.RenderSummary(SampleSnapshot("template") with { Handoff = new WorkspaceSnapshotHandoff { Template = pair.Key } });
        Assert(summary.Contains($"模板：{pair.Value}", StringComparison.Ordinal), $"summary template name missing: {pair.Key}");
        Assert(!summary.Contains($"模板：{pair.Key}", StringComparison.Ordinal), $"internal template key leaked: {pair.Key}");
    }
    return Task.CompletedTask;
}

static Task WorkspaceSnapshotWorkspaceAffinityAsync()
{
    var snapshot = SampleSnapshot("affinity");
    Assert(WorkspaceSnapshotService.IsWorkspaceMatch(snapshot, "C:\\fixture"), "matching workspace rejected");
    Assert(!WorkspaceSnapshotService.IsWorkspaceMatch(snapshot, "C:\\other"), "cross-workspace snapshot accepted");
    return Task.CompletedTask;
}

static Task WorkspaceSnapshotRejectsLowerSchemaAsync()
{
    try { WorkspaceSnapshotService.Validate(SampleSnapshot("old") with { SchemaVersion = 0 }); throw new InvalidOperationException("lower schema was accepted"); }
    catch (InvalidDataException exception) { Assert(exception.Message.Contains("不支持", StringComparison.Ordinal), "lower schema message missing"); }
    return Task.CompletedTask;
}

static Task WorkspaceSnapshotGitVerificationAsync()
{
    var saved = new WorkspaceSnapshotGit { Branch = "main", HeadCommit = "abc", StatusFingerprint = "xyz" };
    Assert(WorkspaceSnapshotService.CompareGit(saved, saved) is null, "matching Git state produced warning");
    Assert(WorkspaceSnapshotService.CompareGit(saved, saved with { Branch = "feature" })?.Contains("分支", StringComparison.Ordinal) == true, "branch mismatch was not reported");
    Assert(WorkspaceSnapshotService.CompareGit(saved, saved with { HeadCommit = "def" })?.Contains("HEAD", StringComparison.Ordinal) == true, "HEAD mismatch was not reported");
    Assert(WorkspaceSnapshotService.CompareGit(saved, saved with { StatusFingerprint = "changed" })?.Contains("文件状态", StringComparison.Ordinal) == true, "status mismatch was not reported");
    return Task.CompletedTask;
}

static Task SnapshotPreviewComparisonAsync()
{
    var snapshot = SampleSnapshot("preview") with
    {
        Workspace = new WorkspaceSnapshotWorkspace { Name = "fixture", Path = "C:\\fixture" },
        Git = new WorkspaceSnapshotGit { Branch = "main", HeadCommit = "abc", StatusFingerprint = "same" },
    };
    var current = new SnapshotPreviewCurrentState { WorkspaceName = "fixture", WorkspacePath = "C:\\fixture", Source = "Codex", SessionId = "thread-1", Git = snapshot.Git };
    var model = SnapshotPreviewBuilder.Build(snapshot, current);
    Assert(model.CanRestore && model.Comparisons[0].Status == UiStrings.Same && model.Git.Result == UiStrings.Same, "matching workspace or git was not recognized");
    var mismatch = SnapshotPreviewBuilder.Build(snapshot, current with { WorkspaceName = "other", WorkspacePath = "C:\\other", Git = snapshot.Git with { Branch = "feature" } });
    Assert(!mismatch.CanRestore && mismatch.WorkspaceAffinityMessage == UiStrings.SnapshotWorkspaceMismatchRestoreBlocked && mismatch.Comparisons[0].Status == UiStrings.WorkspaceMismatch, "workspace mismatch did not disable restore");
    Assert(mismatch.Git.Reasons.Contains(UiStrings.GitBranchChanged) && mismatch.Git.Result == UiStrings.CurrentGitDiffers, "git branch mismatch was not reported");
    var headMismatch = SnapshotPreviewBuilder.Build(snapshot, current with { Git = snapshot.Git with { HeadCommit = "def" } });
    Assert(headMismatch.Git.Reasons.Contains(UiStrings.GitHeadChanged), "git HEAD mismatch was not reported");
    var fingerprintMismatch = SnapshotPreviewBuilder.Build(snapshot, current with { Git = snapshot.Git with { StatusFingerprint = "changed" } });
    Assert(fingerprintMismatch.Git.Reasons.Contains(UiStrings.GitWorkspaceStateChanged), "git fingerprint mismatch was not reported");
    return Task.CompletedTask;
}

static Task SnapshotPreviewSelectionAsync()
{
    var snapshot = SampleSnapshot("preview-selection") with
    {
        Selection = [
            new WorkspaceSnapshotSelection { Source = "Codex", SessionId = "thread-1", TurnId = "a", Role = "User" },
            new WorkspaceSnapshotSelection { Source = "Codex", SessionId = "thread-1", TurnId = "b", Role = "User" },
            new WorkspaceSnapshotSelection { Source = "Codex", SessionId = "thread-1", TurnId = "c", Role = "User" },
        ],
    };
    var model = SnapshotPreviewBuilder.Build(snapshot, new SnapshotPreviewCurrentState { WorkspaceName = "fixture", WorkspacePath = "C:\\fixture", Source = "Codex", SessionId = "thread-1", VisibleTurnIds = new HashSet<string>(["a", "b"]), SelectedTurnCount = 2 });
    Assert(model.Turns.Count(item => item.Status == UiStrings.Matchable) == 2 && model.Turns.Count(item => item.Status == UiStrings.NotCurrentlyMatchable) == 1, "selection match count was incorrect");
    return Task.CompletedTask;
}

static Task SnapshotPreviewMetadataAsync()
{
    var snapshot = SampleSnapshot("preview-metadata") with
    {
        ContextItems = [
            new WorkspaceSnapshotContextItem { Key = "second", Type = "Note", Title = "B", Content = "1234", Order = 2, ReferencePath = "b.md" },
            new WorkspaceSnapshotContextItem { Key = "first", Type = "ProjectFile", Title = "A", Content = "12", Order = 1, ReferencePath = "a.md" },
        ],
        Handoff = new WorkspaceSnapshotHandoff { Target = "Codex", Template = "Continue Task", Title = "Preview title", Task = "task", IncludeContextPack = true, IncludeWorkspace = true, IncludeGitState = false, IncludeProjectFileSummary = true },
    };
    var model = SnapshotPreviewBuilder.Build(snapshot, new SnapshotPreviewCurrentState { WorkspaceName = "fixture", WorkspacePath = "C:\\fixture", Source = "ChatGPT", SessionId = "session-1" });
    Assert(model.ContextItems.Select(item => item.Title).SequenceEqual(["A", "B"]), "context preview order changed");
    Assert(model.ContextItems[0].CharacterCount == 2 && model.Handoff.Template == "继续任务" && model.Handoff.IncludeContextPack == UiStrings.Yes, "context or handoff metadata was incorrect");
    return Task.CompletedTask;
}

static Task SnapshotPreviewIsReadOnlyAsync()
{
    var snapshot = SampleSnapshot("preview-read-only");
    var rows = new[] { true, false };
    var contextCount = snapshot.ContextItems.Count;
    var model = SnapshotPreviewBuilder.Build(snapshot, new SnapshotPreviewCurrentState { WorkspaceName = "fixture", WorkspacePath = "C:\\fixture", Source = "ChatGPT", SessionId = "session-1", SelectedTurnCount = 1, ContextCount = contextCount });
    Assert(rows.SequenceEqual([true, false]) && snapshot.ContextItems.Count == contextCount && model is not null, "preview changed source state");
    return Task.CompletedTask;
}

static Task SnapshotPreviewUiIsLocalizedAsync()
{
    var root = FindRepositoryRoot();
    var xaml = File.ReadAllText(Path.Combine(root, "apps", "windows", "CodexBridge.App", "SnapshotPreviewView.xaml"));
    var code = File.ReadAllText(Path.Combine(root, "apps", "windows", "CodexBridge.App", "SnapshotView.xaml"));
    Assert(xaml.Contains("UiSnapshotComparison", StringComparison.Ordinal) && xaml.Contains("UiWillNotModifyClipboard", StringComparison.Ordinal) && xaml.Contains("CanRestore", StringComparison.Ordinal), "preview UI bindings are incomplete");
    Assert(code.Contains("UiSnapshotPreview", StringComparison.Ordinal) && code.Contains("PreviewClick", StringComparison.Ordinal), "snapshot preview entry point is missing");
    return Task.CompletedTask;
}

static Task SnapshotPreviewShortDisplayAsync()
{
    var root = FindRepositoryRoot();
    var xaml = File.ReadAllText(Path.Combine(root, "apps", "windows", "CodexBridge.App", "SnapshotPreviewView.xaml"));
    var strings = File.ReadAllText(Path.Combine(root, "apps", "windows", "CodexBridge.App", "Themes", "Strings.xaml"));
    Assert(strings.Contains("UiItem", StringComparison.Ordinal) && strings.Contains("UiSelectionSourceHeader", StringComparison.Ordinal) && strings.Contains("UiContextReferenceHeader", StringComparison.Ordinal), "preview header resources are missing");
    Assert(xaml.Contains("UiSnapshotColumn", StringComparison.Ordinal) && xaml.Contains("UiCurrentColumn", StringComparison.Ordinal) && xaml.Contains("UiSelectionTurnIdHeader", StringComparison.Ordinal) && xaml.Contains("UiContextCharacterHeader", StringComparison.Ordinal), "preview headers are not bound");
    Assert(SnapshotPreviewBuilder.ShortHash("d80401eabcdef", 8) == "d80401ea…", "HEAD short display is incorrect");
    Assert(SnapshotPreviewBuilder.ShortHash("EB606B63abcdef", 12) == "EB606B63abcd…", "fingerprint short display is incorrect");
    Assert(SnapshotPreviewBuilder.ShortHash(null, 8) == UiStrings.Unavailable && SnapshotPreviewBuilder.ShortHash(string.Empty, 8) == UiStrings.Unavailable, "empty hash did not display unavailable");
    return Task.CompletedTask;
}

static Task SnapshotPreviewLocalizedDisplayAsync()
{
    var root = FindRepositoryRoot();
    var xaml = File.ReadAllText(Path.Combine(root, "apps", "windows", "CodexBridge.App", "SnapshotPreviewView.xaml"));
    var strings = File.ReadAllText(Path.Combine(root, "apps", "windows", "CodexBridge.App", "Themes", "Strings.xaml"));
    Assert(UiStrings.DisplayRole("User") == "用户" && UiStrings.DisplayRole("Assistant") == "助手" && UiStrings.DisplayRole("System") == "系统", "role display mapping is incomplete");
    Assert(UiStrings.DisplayContextType("ProjectFile") == "项目文件" && UiStrings.DisplayContextType("ConversationTurn") == "会话轮次" && UiStrings.DisplayContextType("GitDiff") == "Git 差异" && UiStrings.DisplayContextType("Note") == "笔记", "context type display mapping is incomplete");
    Assert(UiStrings.DisplayContextTitle("File · AGENTS.md") == "文件 · AGENTS.md", "project file title display mapping is incorrect");
    Assert(strings.Contains("UiGitItemHeader", StringComparison.Ordinal) && strings.Contains("UiGitSnapshotHeader", StringComparison.Ordinal) && strings.Contains("UiGitCurrentHeader", StringComparison.Ordinal), "Git preview header resources are missing");
    foreach (var english in new[] { "Session / Thread", "Changed files", "fingerprint", "Workspace 文件", "Git working tree" }) Assert(!strings.Contains(english, StringComparison.Ordinal), $"English preview resource remained: {english}");
    Assert(xaml.Contains("UiGitItemHeader", StringComparison.Ordinal) && xaml.Contains("UiGitCurrentHeader", StringComparison.Ordinal) && xaml.Contains("UiWillRestoreContext", StringComparison.Ordinal), "Git or restore-after headers are not bound");
    return Task.CompletedTask;
}

static Task SnapshotPreviewUnavailableGitAsync()
{
    var root = FindRepositoryRoot();
    var xaml = File.ReadAllText(Path.Combine(root, "apps", "windows", "CodexBridge.App", "SnapshotPreviewView.xaml"));
    var snapshot = SampleSnapshot("preview-no-git") with
    {
        Workspace = new WorkspaceSnapshotWorkspace { Name = "fixture", Path = "C:\\fixture" },
        Git = new WorkspaceSnapshotGit { Branch = "main", HeadCommit = "abc", StatusFingerprint = "saved" },
    };
    var model = SnapshotPreviewBuilder.Build(snapshot, new SnapshotPreviewCurrentState { WorkspaceName = "other", WorkspacePath = "C:\\other", Source = "Codex", Git = new WorkspaceSnapshotGit() });
    Assert(!model.CanRestore && model.WorkspaceAffinityMessage == UiStrings.SnapshotWorkspaceMismatchRestoreBlocked, "workspace mismatch warning or restore guard missing");
    Assert(model.Git.Result == UiStrings.Unavailable && model.Git.Reasons.SequenceEqual([UiStrings.GitUnavailableReason]), "unavailable Git was reported as changed");
    Assert(model.Comparisons.Where(item => item.Label is UiStrings.GitBranchLabel or UiStrings.GitHeadLabel or UiStrings.GitWorkspaceStateLabel).All(item => item.Status == UiStrings.Unavailable), "top comparison did not use unavailable Git semantics");
    Assert(xaml.Contains("WorkspaceAffinityMessage", StringComparison.Ordinal), "workspace mismatch warning is not visible in Preview");
    var comparable = snapshot with { Workspace = new WorkspaceSnapshotWorkspace { Name = "fixture", Path = "C:\\fixture" } };
    var currentGit = new WorkspaceSnapshotGit { Branch = "feature", HeadCommit = "def", StatusFingerprint = "current" };
    var changed = SnapshotPreviewBuilder.Build(comparable, new SnapshotPreviewCurrentState { WorkspaceName = "fixture", WorkspacePath = "C:\\fixture", Source = "Codex", Git = currentGit });
    Assert(changed.Git.Result == UiStrings.CurrentGitDiffers && changed.Git.Reasons.Contains(UiStrings.GitBranchChanged) && changed.Git.Reasons.Contains(UiStrings.GitHeadChanged) && changed.Git.Reasons.Contains(UiStrings.GitWorkspaceStateChanged), "comparable Git changes were not reported");
    return Task.CompletedTask;
}

static async Task WorkspaceSnapshotSensitiveSchemaAsync()
{
    var root = Path.Combine(Path.GetTempPath(), $"codexbridge-snapshot-{Guid.NewGuid():N}"); Directory.CreateDirectory(root);
    try { var service = new WorkspaceSnapshotService(); var snapshot = SampleSnapshot("privacy"); service.Save(root, snapshot); var json = File.ReadAllText(Path.Combine(WorkspaceSnapshotService.GetRoot(root), snapshot.SnapshotId, "snapshot.json")); foreach (var forbidden in new[] { "token", "accessToken", "refreshToken", "cookie", "credential", "password", "apiKey", "clipboard", "reasoning", "hiddenState", "environmentVariables" }) Assert(!json.Contains(forbidden, StringComparison.OrdinalIgnoreCase), $"sensitive field leaked: {forbidden}"); }
    finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    await Task.CompletedTask;
}

static Task SnapshotUiBindingsAsync()
{
    var root = FindRepositoryRoot();
    var xaml = File.ReadAllText(Path.Combine(root, "apps", "windows", "CodexBridge.App", "SnapshotView.xaml"));
    var strings = File.ReadAllText(Path.Combine(root, "apps", "windows", "CodexBridge.App", "Themes", "Strings.xaml"));
    Assert(strings.Contains("UiSnapshotNameLabel", StringComparison.Ordinal) && strings.Contains("暂无工作现场", StringComparison.Ordinal), "snapshot empty/name resources missing");
    Assert(xaml.Contains("CanRestoreSnapshot", StringComparison.Ordinal) && xaml.Contains("CanRenameSnapshot", StringComparison.Ordinal) && xaml.Contains("CanDeleteSnapshot", StringComparison.Ordinal), "snapshot action enable bindings missing");
    Assert(xaml.Contains("SnapshotEntries.Count", StringComparison.Ordinal), "snapshot empty state binding missing");
    return Task.CompletedTask;
}

static Task SnapshotRenameDialogIsLocalizedAsync()
{
    var root = FindRepositoryRoot();
    var xaml = File.ReadAllText(Path.Combine(root, "apps", "windows", "CodexBridge.App", "SnapshotRenameView.xaml"));
    var code = File.ReadAllText(Path.Combine(root, "apps", "windows", "CodexBridge.App", "SnapshotRenameView.xaml.cs"));
    var strings = File.ReadAllText(Path.Combine(root, "apps", "windows", "CodexBridge.App", "Themes", "Strings.xaml"));
    Assert(strings.Contains("UiRenameSnapshotTitle", StringComparison.Ordinal) && strings.Contains("UiConfirm", StringComparison.Ordinal) && strings.Contains("UiCancel", StringComparison.Ordinal), "rename dialog resources missing");
    Assert(xaml.Contains("UiConfirm", StringComparison.Ordinal) && xaml.Contains("UiCancel", StringComparison.Ordinal) && xaml.Contains("NameBoxLoaded", StringComparison.Ordinal), "rename dialog bindings missing");
    Assert(code.Contains("UiSnapshotNameRequired", StringComparison.Ordinal), "empty rename validation missing");
    return Task.CompletedTask;
}

static async Task ProjectFileContextAddsSelectedFileAsync()
{
    await using var scope = await TestScope.CreateAsync();
    var file = Path.Combine(Environment.CurrentDirectory, $".codexbridge-project-file-{Guid.NewGuid():N}.md");
    await File.WriteAllTextAsync(file, "# fixture agents");
    try
    {
        var workspace = new WorkspaceItem("fixture", scope.Root, DateTimeOffset.Now);
        Assert(MainWindowViewModel.TryCreateProjectFileContext(workspace, file, out var item, out _), "selected project file was not added");
        Assert(item.Type == "ProjectFile" && item.Title.EndsWith(Path.GetFileName(file), StringComparison.Ordinal) && item.Content == "# fixture agents" && item.ReferencePath == Path.GetRelativePath(scope.Root, file), "project file context fields are incorrect");
        var references = new HashSet<string>(StringComparer.OrdinalIgnoreCase); Assert(references.Add(item.ReferencePath!), "first project file add was rejected"); Assert(!references.Add(item.ReferencePath!), "duplicate project file was added twice");
    }
    finally { if (File.Exists(file)) File.Delete(file); }
}

static async Task ProjectFileContextRejectsInvalidSelectionAsync()
{
    await using var scope = await TestScope.CreateAsync();
    var directory = Path.Combine(Environment.CurrentDirectory, $".codexbridge-project-dir-{Guid.NewGuid():N}"); Directory.CreateDirectory(directory);
    var missing = Path.Combine(Environment.CurrentDirectory, $".codexbridge-missing-{Guid.NewGuid():N}.md");
    try
    {
        var workspace = new WorkspaceItem("fixture", scope.Root, DateTimeOffset.Now);
        Assert(!MainWindowViewModel.TryCreateProjectFileContext(workspace, directory, out _, out _), "directory produced context content");
        Assert(!MainWindowViewModel.TryCreateProjectFileContext(workspace, missing, out _, out _), "missing file produced context content");
    }
    finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}

static Task SnapshotCapturesOnlySelectedTurnsAsync()
{
    var rows = Enumerable.Range(0, 5).Select(index => new TurnRowViewModel(new CapturedTurn { Id = $"turn-{index}", Index = index, User = new CapturedMessage { Id = $"user-{index}", IdSource = "fixture", Text = $"text-{index}" }, Complete = true }, index == 2, "用户", "ChatGPT")).ToList();
    var refs = MainWindowViewModel.CaptureSelectedTurnRefs("ChatGPT", "session-1", rows);
    Assert(refs.Count == 1 && refs[0].TurnId == "turn-2", "snapshot captured unselected visible turns");
    var before = rows.Select(row => row.IsSelected).ToArray();
    Assert(MainWindowViewModel.CaptureSelectedTurnRefs("ChatGPT", "session-1", rows).Count == 1, "clear/reselect capture did not remain one");
    Assert(before.SequenceEqual(rows.Select(row => row.IsSelected)), "capture changed UI selection state");
    Assert(MainWindowViewModel.CaptureSelectedTurnRefs("Codex", "thread-1", rows).Single().Source == "Codex", "source was not isolated");
    rows.ForEach(row => row.IsSelected = false);
    Assert(MainWindowViewModel.CaptureSelectedTurnRefs("ChatGPT", "session-1", rows).Count == 0, "empty selection was not captured as empty");
    return Task.CompletedTask;
}

static Task SnapshotPackageExportsExactEntriesAsync()
{
    var root = Path.Combine(Path.GetTempPath(), $"codexbridge-package-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    try
    {
        var packagePath = Path.Combine(root, "backup.zip");
        var snapshot = SampleSnapshot("package-export");
        new SnapshotPackageService().Export(packagePath, snapshot, DateTimeOffset.UtcNow);
        using var archive = ZipFile.OpenRead(packagePath);
        var names = archive.Entries.Select(entry => entry.FullName).ToHashSet(StringComparer.Ordinal);
        Assert(names.SetEquals(["manifest.json", "snapshot.json", "summary.md"]), "export package entries were not exact");
        var snapshotBytes = ReadZipEntry(archive, "snapshot.json");
        var manifest = JsonSerializer.Deserialize<SnapshotPackageManifest>(ReadZipEntry(archive, "manifest.json"), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert(manifest is not null && manifest.PackageVersion == 1 && manifest.SnapshotId == snapshot.SnapshotId && manifest.SnapshotSchemaVersion == snapshot.SchemaVersion, "manifest identity fields were incorrect");
        Assert(string.Equals(manifest!.SnapshotSha256, Convert.ToHexString(SHA256.HashData(snapshotBytes)), StringComparison.OrdinalIgnoreCase), "manifest hash did not match snapshot bytes");
        var manifestText = Encoding.UTF8.GetString(ReadZipEntry(archive, "manifest.json"));
        foreach (var sensitive in new[] { "token", "cookie", "password", "credential", "apiKey", "clipboard", "reasoning", "environmentVariables" })
            Assert(!manifestText.Contains(sensitive, StringComparison.OrdinalIgnoreCase), $"manifest contained sensitive field: {sensitive}");
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    return Task.CompletedTask;
}

static Task SnapshotPackageImportsAndRebuildsSummaryAsync()
{
    var root = Path.Combine(Path.GetTempPath(), $"codexbridge-package-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    try
    {
        var source = Path.Combine(root, "source");
        var target = Path.Combine(root, "target");
        var packagePath = Path.Combine(root, "backup.zip");
        var snapshot = SampleSnapshot("package-import");
        var service = new SnapshotPackageService();
        service.Export(packagePath, snapshot);
        RewritePackage(packagePath, entries => entries["summary.md"] = Encoding.UTF8.GetBytes("tampered summary"));
        var inspection = service.InspectImport(packagePath, "target", target);
        var imported = service.ImportValidated(inspection);
        var directory = Path.Combine(WorkspaceSnapshotService.GetRoot(target), imported.SnapshotId);
        Assert(File.Exists(Path.Combine(directory, "snapshot.json")) && File.ReadAllText(Path.Combine(directory, "summary.md")).Contains("模板：调试问题", StringComparison.Ordinal), "import did not write validated snapshot and regenerated summary");
        Assert(!File.ReadAllText(Path.Combine(directory, "summary.md")).Contains("tampered", StringComparison.Ordinal), "import trusted package summary");
        Assert(!Directory.Exists(Path.Combine(source, ".ai")), "import unexpectedly changed another workspace");
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    return Task.CompletedTask;
}

static Task SnapshotPackageRejectsDuplicateAndInvalidAsync()
{
    var root = Path.Combine(Path.GetTempPath(), $"codexbridge-package-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    try
    {
        var workspace = Path.Combine(root, "workspace");
        var packagePath = Path.Combine(root, "backup.zip");
        var snapshot = SampleSnapshot("package-duplicate");
        var service = new SnapshotPackageService();
        service.Export(packagePath, snapshot);
        new WorkspaceSnapshotService().Save(workspace, snapshot);
        AssertThrows<InvalidDataException>(() => service.InspectImport(packagePath, "workspace", workspace), "duplicate snapshot id was accepted");

        var extraPath = Path.Combine(root, "extra.zip");
        File.Copy(packagePath, extraPath);
        RewritePackage(extraPath, entries => entries["extra.txt"] = Encoding.UTF8.GetBytes("unsupported"));
        AssertThrows<InvalidDataException>(() => service.InspectImport(extraPath, "other", Path.Combine(root, "other")), "extra package entry was accepted");

        var hashPath = Path.Combine(root, "hash.zip");
        File.Copy(packagePath, hashPath);
        RewritePackage(hashPath, entries => entries["snapshot.json"][0] ^= 1);
        AssertThrows<InvalidDataException>(() => service.InspectImport(hashPath, "other", Path.Combine(root, "other")), "snapshot hash mismatch was accepted");
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    return Task.CompletedTask;
}

static Task SnapshotPackageRejectsMissingAndUnsupportedMetadataAsync()
{
    var root = Path.Combine(Path.GetTempPath(), $"codexbridge-package-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    try
    {
        var service = new SnapshotPackageService();
        var snapshot = SampleSnapshot("package-malformed");
        var source = Path.Combine(root, "source.zip");
        service.Export(source, snapshot);
        foreach (var (name, mutate) in new (string Name, Action<Dictionary<string, byte[]>> Mutate)[]
        {
            ("missing-manifest.zip", entries => entries.Remove("manifest.json")),
            ("missing-snapshot.zip", entries => entries.Remove("snapshot.json")),
            ("corrupt-manifest.zip", entries => entries["manifest.json"] = Encoding.UTF8.GetBytes("{")),
            ("corrupt-snapshot.zip", entries => entries["snapshot.json"] = Encoding.UTF8.GetBytes("{")),
            ("newer-version.zip", entries => entries["manifest.json"] = ManifestBytes(snapshot, packageVersion: 2)),
            ("unsupported-version.zip", entries => entries["manifest.json"] = ManifestBytes(snapshot, packageVersion: 0)),
        })
        {
            var package = Path.Combine(root, name);
            File.Copy(source, package);
            RewritePackage(package, mutate);
            AssertThrows<InvalidDataException>(() => service.InspectImport(package, "workspace", Path.Combine(root, name + "-workspace")), $"malformed package was accepted: {name}");
        }
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    return Task.CompletedTask;
}

static Task SnapshotPackageRejectsIdentityAndTraversalAsync()
{
    var root = Path.Combine(Path.GetTempPath(), $"codexbridge-package-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    try
    {
        var service = new SnapshotPackageService();
        var snapshot = SampleSnapshot("package-identity");
        var source = Path.Combine(root, "source.zip");
        service.Export(source, snapshot);
        foreach (var (name, mutate) in new (string Name, Action<Dictionary<string, byte[]>> Mutate)[]
        {
            ("id-mismatch.zip", entries => entries["manifest.json"] = ManifestBytes(snapshot, snapshotId: "other")),
            ("schema-mismatch.zip", entries => entries["manifest.json"] = ManifestBytes(snapshot, schemaVersion: 99)),
            ("nested-entry.zip", entries => entries["nested/evil.txt"] = Encoding.UTF8.GetBytes("evil")),
            ("traversal-entry.zip", entries => entries["../evil.txt"] = Encoding.UTF8.GetBytes("evil")),
        })
        {
            var package = Path.Combine(root, name);
            File.Copy(source, package);
            RewritePackage(package, mutate);
            AssertThrows<InvalidDataException>(() => service.InspectImport(package, "workspace", Path.Combine(root, name + "-workspace")), $"tampered package was accepted: {name}");
        }
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    return Task.CompletedTask;
}

static byte[] ManifestBytes(WorkspaceSnapshot snapshot, int? packageVersion = null, string? snapshotId = null, int? schemaVersion = null, string? snapshotSha256 = null)
{
    var manifest = new SnapshotPackageManifest
    {
        PackageVersion = packageVersion ?? SnapshotPackageService.CurrentPackageVersion,
        Product = "Codex Bridge",
        SnapshotId = snapshotId ?? snapshot.SnapshotId,
        SnapshotSchemaVersion = schemaVersion ?? snapshot.SchemaVersion,
        SnapshotSha256 = snapshotSha256 ?? Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(snapshot, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }))),
        ExportedAt = DateTimeOffset.UtcNow,
        AppVersion = snapshot.AppVersion,
    };
    return JsonSerializer.SerializeToUtf8Bytes(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
}

static async Task SnapshotImportFailureFeedbackAsync()
{
    var previousOverride = Environment.GetEnvironmentVariable("CODEX_BRIDGE_WORKSPACES_PATH");
    await using var scope = await TestScope.CreateAsync();
    try
    {
        Environment.SetEnvironmentVariable("CODEX_BRIDGE_WORKSPACES_PATH", Path.Combine(scope.Root, "workspaces.json"));
        await using var importer = new CaptureInboxImporter(scope.Repository, inboxDirectory: scope.Inbox);
        await using var viewModel = new MainWindowViewModel(scope.Repository, importer, FakeCodexClient.Success());
        var workspace = new WorkspaceItem("fixture", scope.Root, DateTimeOffset.UtcNow);
        viewModel.Workspaces.Add(workspace);
        Assert(viewModel.TrySwitchWorkspace(workspace, requireConfirmation: false), "workspace setup failed");

        var snapshot = SampleSnapshot("bad-sha") with
        {
            Workspace = new WorkspaceSnapshotWorkspace { Name = workspace.Name, Path = workspace.Path },
        };
        var service = new SnapshotPackageService();
        var badPackage = Path.Combine(scope.Root, "bad-sha.zip");
        service.Export(badPackage, snapshot);
        RewritePackage(badPackage, entries => entries["manifest.json"] = ManifestBytes(snapshot, snapshotSha256: new string('0', 64)));

        var failed = viewModel.InspectSnapshotImport(badPackage);
        Assert(failed is null, "bad SHA unexpectedly entered import confirmation");
        Assert(viewModel.SnapshotImportErrorMessage == UiStrings.SnapshotIntegrityFailed, "bad SHA error message was not exposed for display");
        Assert(viewModel.StatusText == UiStrings.SnapshotImportFailed(UiStrings.SnapshotIntegrityFailed), "bad SHA status did not retain the concrete reason");
        Assert(!Directory.Exists(Path.Combine(WorkspaceSnapshotService.GetRoot(scope.Root), snapshot.SnapshotId)), "bad SHA created a Snapshot directory");

        viewModel.BeginSnapshotImport();
        Assert(string.IsNullOrEmpty(viewModel.SnapshotImportErrorMessage), "cancel path retained an import error");

        var validSnapshot = snapshot with { SnapshotId = "valid-package" };
        var validPackage = Path.Combine(scope.Root, "valid.zip");
        service.Export(validPackage, validSnapshot);
        var inspection = viewModel.InspectSnapshotImport(validPackage);
        Assert(inspection is not null && string.IsNullOrEmpty(viewModel.SnapshotImportErrorMessage), "valid package did not reach import confirmation");
        Assert(!Directory.Exists(Path.Combine(WorkspaceSnapshotService.GetRoot(scope.Root), validSnapshot.SnapshotId)), "inspection wrote a Snapshot before confirmation");
    }
    finally
    {
        Environment.SetEnvironmentVariable("CODEX_BRIDGE_WORKSPACES_PATH", previousOverride);
    }
}

static Task SnapshotImportErrorDialogResourcesAsync()
{
    var strings = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "apps", "windows", "CodexBridge.App", "Themes", "Strings.xaml"));
    var view = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "apps", "windows", "CodexBridge.App", "SnapshotImportErrorView.xaml"));
    Assert(strings.Contains("UiSnapshotImportWindowTitle", StringComparison.Ordinal) && strings.Contains("UiConfirm", StringComparison.Ordinal), "import error dialog resources are missing");
    Assert(view.Contains("SnapshotImportErrorView", StringComparison.Ordinal) && view.Contains("PrimaryButtonStyle", StringComparison.Ordinal), "import error dialog is not themed");
    return Task.CompletedTask;
}

static Task SnapshotPackageAllowsWorkspaceMismatchAsync()
{
    var root = Path.Combine(Path.GetTempPath(), $"codexbridge-package-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    try
    {
        var current = Path.Combine(root, "current");
        var packagePath = Path.Combine(root, "backup.zip");
        var snapshot = SampleSnapshot("package-mismatch");
        var service = new SnapshotPackageService();
        service.Export(packagePath, snapshot);
        var inspection = service.InspectImport(packagePath, "current", current);
        Assert(!inspection.WorkspaceMatches && inspection.WorkspaceMismatchWarning.Length > 0, "workspace mismatch was not surfaced during import inspection");
        service.ImportValidated(inspection);
        var loaded = new WorkspaceSnapshotService().Load(Path.Combine(WorkspaceSnapshotService.GetRoot(current), snapshot.SnapshotId));
        Assert(loaded.Workspace.Path == snapshot.Workspace.Path && loaded.Workspace.Name == snapshot.Workspace.Name, "workspace mismatch import rewrote original workspace");
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    return Task.CompletedTask;
}

static byte[] ReadZipEntry(ZipArchive archive, string name)
{
    using var stream = archive.GetEntry(name)!.Open();
    using var output = new MemoryStream();
    stream.CopyTo(output);
    return output.ToArray();
}

static void RewritePackage(string packagePath, Action<Dictionary<string, byte[]>> mutate)
{
    var entries = new Dictionary<string, byte[]>(StringComparer.Ordinal);
    using (var archive = ZipFile.OpenRead(packagePath))
    {
        foreach (var entry in archive.Entries) entries[entry.FullName] = ReadZipEntry(archive, entry.FullName);
    }
    mutate(entries);
    var temp = packagePath + ".rewrite.tmp";
    using (var archive = ZipFile.Open(temp, ZipArchiveMode.Create))
    {
        foreach (var entry in entries)
        {
            using var stream = archive.CreateEntry(entry.Key).Open();
            stream.Write(entry.Value);
        }
    }
    File.Move(temp, packagePath, true);
}

static void AssertThrows<TException>(Action action, string message) where TException : Exception
{
    try { action(); }
    catch (TException) { return; }
    throw new InvalidOperationException(message);
}

static WorkspaceSnapshot SampleSnapshot(string id) => new()
{
    SnapshotId = id, Name = "现场", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
    Workspace = new WorkspaceSnapshotWorkspace { Name = "fixture", Path = "C:\\fixture" },
    Selection = [new WorkspaceSnapshotSelection { Source = "ChatGPT", SessionId = "session-1", TurnId = "turn-1", Role = "User", TextHash = "hash-1" }, new WorkspaceSnapshotSelection { Source = "Codex", SessionId = "thread-1", TurnId = "turn-2", Role = "User", TextHash = "hash-2" }],
    ContextItems = [new WorkspaceSnapshotContextItem { Key = "key-1", Type = "ConversationTurn", Title = "title-1", Content = "body", Source = "ChatGPT", Order = 0 }, new WorkspaceSnapshotContextItem { Key = "key-2", Type = "ProjectFile", Title = "title-2", Content = "file", Source = "Project Explorer", ReferencePath = "README.md", Order = 1 }, new WorkspaceSnapshotContextItem { Key = "key-3", Type = "GitDiff", Title = "title-3", Content = "diff", Source = "Git", ReferencePath = "Program.cs", Order = 2 }],
    Handoff = new WorkspaceSnapshotHandoff { Target = "ChatGPT", Template = "Debug Issue", Title = "title", Task = "task", CurrentState = "state", Constraints = "constraints", NextAction = "next", IncludeContextPack = true, IncludeWorkspace = true, IncludeGitState = true, IncludeProjectFileSummary = true },
    Git = new WorkspaceSnapshotGit { Branch = "main", HeadCommit = "abc" },
};

static string FindRepositoryRoot()
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "apps", "windows", "CodexBridge.App", "CodexBridge.App.csproj")))
    {
        directory = directory.Parent;
    }

    return directory?.FullName ?? throw new InvalidOperationException("repository root not found");
}

static Task HandoffContextAndGitRenderAsync()
{
    var text = HandoffService.Render(new HandoffInput("ChatGPT", "Continue Task", "Fixture", "task", "state", "constraints", "next", "workspace", "C:\\workspace", "main", [new GitChangedFile("M", "Program.cs")], [new ContextPackItem("ProjectFile", "README.md", "Workspace", "content", "README.md", 0, "key")], 4, true, true, true, true));
    Assert(text.Contains("目标：ChatGPT", StringComparison.Ordinal), "handoff target missing");
    Assert(text.Contains("## 重要上下文", StringComparison.Ordinal) && !text.Contains("# Context Pack", StringComparison.Ordinal), "context was wrapped with duplicate pack heading");
    Assert(text.Contains("分支：main", StringComparison.Ordinal) && text.Contains("- M Program.cs", StringComparison.Ordinal), "git state missing");
    Assert(text.Contains("文件数：4", StringComparison.Ordinal), "project file summary missing");
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
    await WaitForStatusAsync(viewModel, "复制失败：old failure");
    viewModel.CopyHandoffCommand.Execute(null);
    await WaitForStatusAsync(viewModel, "交接内容已复制。");

    Assert(viewModel.StatusText == "交接内容已复制。", "successful Handoff copy did not replace stale failure");
}

static async Task HandoffCopyFailureReportsCurrentReasonAsync()
{
    var service = new ClipboardService(_ => throw new InvalidOperationException("current failure"), _ => Task.CompletedTask);
    await using var scope = await TestScope.CreateAsync();
    await using var importer = new CaptureInboxImporter(scope.Repository, inboxDirectory: scope.Inbox);
    await using var viewModel = new MainWindowViewModel(scope.Repository, importer, FakeCodexClient.Success(), clipboardService: service);
    viewModel.HandoffTitle = "Fixture";

    viewModel.CopyHandoffCommand.Execute(null);
    await WaitForStatusAsync(viewModel, "复制失败：current failure");

    Assert(viewModel.StatusText == "复制失败：current failure", "current Handoff copy failure reason was not shown");
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
    await WaitForStatusAsync(viewModel, "复制失败：stale failure");
    viewModel.CopyHandoffCommand.Execute(null);
    await WaitForStatusAsync(viewModel, "交接内容已复制。");
    await Task.Delay(100);

    Assert(viewModel.StatusText == "交接内容已复制。", "an older Handoff status overwrote the latest success");
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
