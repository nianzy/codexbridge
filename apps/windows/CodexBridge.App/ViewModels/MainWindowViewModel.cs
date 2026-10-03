using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Data;
using System.IO;
using CodexBridge.App.Commands;
using CodexBridge.App.Infrastructure;
using CodexBridge.Core.Capture;
using CodexBridge.Core.Codex;
using CodexBridge.Core.Conversations;
using CodexBridge.App.Controls;
using CodexBridge.App;

namespace CodexBridge.App.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly IConversationRepository repository;
    private readonly CaptureInboxImporter importer;
    private ICodexClient codexClient;
    private readonly Func<ICodexClient>? codexClientFactory;
    private readonly ICodexAppLog codexLog;
    private readonly ICodexAppLog uiLog;
    private readonly Func<MessageBoxResult> confirmWorkspaceSwitch;
    private readonly ClipboardService clipboardService;
    private ConversationListItemViewModel? selectedConversation;
    private CodexThreadListItemViewModel? selectedCodexThread;
    private TurnSelection? chatGptTurnSelection;
    private HashSet<string>? codexSelectedTurnIds;
    private CodexThreadSnapshot? codexSnapshot;
    private CancellationTokenSource? codexThreadLoad;
    private int codexLoadSequence;
    private bool preserveDraftDuringSelectionClear;
    private int sourceIndex;
    private string draftText = string.Empty;
    private string statusText = UiStrings.Ready;
    private string nativeMessagingText = "Not installed";
    private string codexStatusText = "Disconnected";
    private string codexErrorDetail = string.Empty;
    private int codexModelCount;
    private bool initialized;
    private string sessionSearchText = string.Empty;
    private string sessionFilter = "All";
    private bool workspaceOnly;
    private string codexHome = string.Empty;
    private string codexVersion = string.Empty;
    private bool codexDetailsExpanded;
    private string codexExecutable = string.Empty;
    private WorkspaceItem? selectedWorkspace;
    private string newWorkspacePath = string.Empty;
    private string projectFilePreviewTitle = "文件预览";
    private string projectFilePreviewText = "请从项目浏览器中选择支持的文件。";
    private string? selectedProjectFilePath;
    private string newNoteName = string.Empty;
    private string gitBranch = "Git not found";
    private string diffPreviewTitle = "差异预览";
    private string diffPreviewText = "请选择一个变更文件。";
    private bool enableGitIntegration = true;
    private readonly ContextPackService contextPackService = new();
    private string contextPreviewText = string.Empty;
    private string handoffTarget = "Codex";
    private string handoffTemplate = "Continue Task";
    private string handoffTitle = string.Empty;
    private string handoffTask = string.Empty;
    private string handoffCurrentState = string.Empty;
    private string handoffConstraints = "不要自动发送。\n不要自动执行。";
    private string handoffNextAction = string.Empty;
    private string handoffPreviewText = string.Empty;
    private long handoffCopySequence;
    private bool includeHandoffContext = true;
    private bool includeHandoffWorkspace = true;
    private bool includeHandoffGit = true;
    private bool includeHandoffProjectFiles;
    private readonly WorkspaceSnapshotService snapshotService = new();
    private readonly SnapshotPackageService snapshotPackageService;
    private readonly SnapshotAutoIndexService snapshotAutoIndexService = new();
    private WorkspaceSnapshotEntry? selectedSnapshot;
    private string snapshotName = string.Empty;
    private bool autoSaveBeforeWorkspaceSwitch;
    private bool promptSaveBeforeExit;
    private int autoSnapshotRetentionCount = 10;
    private bool workspaceSwitchInProgress;
    private string snapshotOperationErrorMessage = string.Empty;
    private string snapshotImportErrorMessage = string.Empty;

    public MainWindowViewModel(
        IConversationRepository repository,
        CaptureInboxImporter importer,
        ICodexClient codexClient,
        ICodexAppLog? codexLog = null,
        Func<ICodexClient>? codexClientFactory = null,
        ICodexAppLog? uiLog = null,
        Func<MessageBoxResult>? confirmWorkspaceSwitch = null,
        ClipboardService? clipboardService = null)
    {
        this.repository = repository;
        this.importer = importer;
        this.codexClient = codexClient;
        this.codexLog = codexLog ?? NullCodexAppLog.Instance;
        this.uiLog = uiLog ?? NullCodexAppLog.Instance;
        this.clipboardService = clipboardService ?? ClipboardService.Shared;
        snapshotPackageService = new SnapshotPackageService(snapshotService);
        this.confirmWorkspaceSwitch = confirmWorkspaceSwitch ?? (() =>
            WorkspaceSwitchConfirmView.ShowConfirmation(AutoSaveBeforeWorkspaceSwitch)
                ? MessageBoxResult.OK : MessageBoxResult.Cancel);
        this.codexClientFactory = codexClientFactory;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        RefreshActiveCommand = new AsyncRelayCommand(RefreshActiveAsync);
        RefreshCodexCommand = new AsyncRelayCommand(RefreshCodexAsync);
        SelectAllCommand = new RelayCommand(SelectAll);
        ClearSelectionCommand = new RelayCommand(ClearSelection);
        CopyDraftCommand = new AsyncRelayCommand(CopyDraftAsync);
        OpenChatGptCommand = new RelayCommand(OpenChatGpt);
        ExportDraftCommand = new RelayCommand(ExportDraft);
        AddWorkspaceCommand = new AsyncRelayCommand(AddWorkspaceAsync);
        NewNoteCommand = new RelayCommand(NewNote);
        OpenNoteCommand = new RelayCommand(OpenNote);
        DeleteNoteCommand = new RelayCommand(DeleteNote);
        CopyContextCommand = new AsyncRelayCommand(CopyContextAsync);
        ExportContextCommand = new RelayCommand(ExportContext);
        ClearContextCommand = new RelayCommand(ClearContext);
        PreviewContextCommand = new RelayCommand(PreviewContext);
        GenerateHandoffCommand = new RelayCommand(GenerateHandoff);
        PreviewHandoffCommand = new RelayCommand(UpdateHandoffPreview);
        CopyHandoffCommand = new AsyncRelayCommand(CopyHandoffAsync);
        ExportHandoffCommand = new RelayCommand(ExportHandoff);
        UseCurrentSessionCommand = new RelayCommand(UseCurrentSessionForHandoff);
        QuickSaveSnapshotCommand = new AsyncRelayCommand(QuickSaveCurrentSnapshotAsync);
        IgnoreFolders = new ObservableCollection<string>(ProjectContextService.DefaultIgnoreFolders);
        Notes = new ObservableCollection<NoteItem>();
        GitChangedFiles = new ObservableCollection<GitChangedFile>();
        Workspaces = new ObservableCollection<WorkspaceItem>(WorkspaceStore.Load());
        if (Workspaces.Count == 0 && Directory.Exists(Environment.CurrentDirectory))
            Workspaces.Add(new WorkspaceItem(new DirectoryInfo(Environment.CurrentDirectory).Name, Environment.CurrentDirectory, DateTimeOffset.Now));
        importer.Imported += OnImported;
        ConversationsView = CollectionViewSource.GetDefaultView(Conversations);
        CodexThreadsView = CollectionViewSource.GetDefaultView(CodexThreads);
        ConversationsView.Filter = FilterConversation;
        CodexThreadsView.Filter = FilterCodexThread;
        ConversationsView.SortDescriptions.Add(new SortDescription(nameof(ConversationListItemViewModel.UpdatedAt), ListSortDirection.Descending));
        CodexThreadsView.SortDescriptions.Add(new SortDescription(nameof(CodexThreadListItemViewModel.UpdatedAt), ListSortDirection.Descending));
        if (Workspaces.FirstOrDefault() is { } initialWorkspace) CommitWorkspaceSwitch(initialWorkspace);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ConversationListItemViewModel> Conversations { get; } = new();

    public ObservableCollection<CodexThreadListItemViewModel> CodexThreads { get; } = new();

    public ObservableCollection<TurnRowViewModel> Turns { get; } = new();
    public ObservableCollection<WorkspaceItem> Workspaces { get; }
    public ObservableCollection<ProjectTreeItem> ProjectRootItems { get; } = new();
    public ObservableCollection<string> IgnoreFolders { get; }
    public ObservableCollection<NoteItem> Notes { get; }
    public ObservableCollection<ContextPackItem> ContextItems { get; } = new();
    public ObservableCollection<GitChangedFile> GitChangedFiles { get; }
    public ObservableCollection<WorkspaceSnapshotEntry> SnapshotEntries { get; } = new();

    public ICollectionView ConversationsView { get; }

    public ICollectionView CodexThreadsView { get; }

    public ICommand RefreshCommand { get; }

    public ICommand RefreshActiveCommand { get; }

    public ICommand RefreshCodexCommand { get; }

    public ICommand SelectAllCommand { get; }

    public ICommand ClearSelectionCommand { get; }

    public ICommand CopyDraftCommand { get; }

    public ICommand OpenChatGptCommand { get; }
    public ICommand ExportDraftCommand { get; }
    public ICommand AddWorkspaceCommand { get; }
    public ICommand NewNoteCommand { get; }
    public ICommand OpenNoteCommand { get; }
    public ICommand DeleteNoteCommand { get; }
    public ICommand CopyContextCommand { get; }
    public ICommand ExportContextCommand { get; }
    public ICommand ClearContextCommand { get; }
    public ICommand PreviewContextCommand { get; }
    public ICommand GenerateHandoffCommand { get; }
    public ICommand PreviewHandoffCommand { get; }
    public ICommand CopyHandoffCommand { get; }
    public ICommand ExportHandoffCommand { get; }
    public ICommand UseCurrentSessionCommand { get; }
    public ICommand QuickSaveSnapshotCommand { get; }

    public string HandoffTarget { get => handoffTarget; set { if (SetProperty(ref handoffTarget, value)) UpdateHandoffPreview(); } }
    public string HandoffTemplate { get => handoffTemplate; set { if (SetProperty(ref handoffTemplate, value)) UpdateHandoffPreview(); } }
    public string HandoffTitle { get => handoffTitle; set { if (SetProperty(ref handoffTitle, value)) UpdateHandoffPreview(); } }
    public string HandoffTask { get => handoffTask; set { if (SetProperty(ref handoffTask, value)) UpdateHandoffPreview(); } }
    public string HandoffCurrentState { get => handoffCurrentState; set { if (SetProperty(ref handoffCurrentState, value)) UpdateHandoffPreview(); } }
    public string HandoffConstraints { get => handoffConstraints; set { if (SetProperty(ref handoffConstraints, value)) UpdateHandoffPreview(); } }
    public string HandoffNextAction { get => handoffNextAction; set { if (SetProperty(ref handoffNextAction, value)) UpdateHandoffPreview(); } }
    public string HandoffPreviewText { get => handoffPreviewText; private set => SetProperty(ref handoffPreviewText, value); }
    public bool IncludeHandoffContext { get => includeHandoffContext; set { if (SetProperty(ref includeHandoffContext, value)) UpdateHandoffPreview(); } }
    public bool IncludeHandoffWorkspace { get => includeHandoffWorkspace; set { if (SetProperty(ref includeHandoffWorkspace, value)) UpdateHandoffPreview(); } }
    public bool IncludeHandoffGit { get => includeHandoffGit; set { if (SetProperty(ref includeHandoffGit, value)) UpdateHandoffPreview(); } }
    public bool IncludeHandoffProjectFiles { get => includeHandoffProjectFiles; set { if (SetProperty(ref includeHandoffProjectFiles, value)) UpdateHandoffPreview(); } }
    public int HandoffCharacters => HandoffPreviewText.Length;
    public string HandoffCharacterSummary => UiStrings.CharacterSummary(HandoffCharacters);
    public string HandoffSizeState => UiStrings.SizeState(ContextPackService.ClassifySize(HandoffCharacters));

    public int SourceIndex
    {
        get => sourceIndex;
        set
        {
            var normalized = value == 1 ? 1 : 0;
            if (!SetProperty(ref sourceIndex, normalized))
            {
                return;
            }

            OnPropertyChanged(nameof(ActiveSourceLabel));
            OnPropertyChanged(nameof(CurrentSessionTitle));
            OnPropertyChanged(nameof(CurrentSessionUpdatedAt));
            LoadActiveSource();
        }
    }

    public ConversationListItemViewModel? SelectedConversation
    {
        get => selectedConversation;
        set
        {
            if (ReferenceEquals(selectedConversation, value))
            {
                return;
            }

            selectedConversation = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CurrentSessionTitle));
            OnPropertyChanged(nameof(CurrentSessionUpdatedAt));
            if (SourceIndex == 0)
            {
                LoadChatGptTurns(value?.Conversation);
            }
        }
    }

    public CodexThreadListItemViewModel? SelectedCodexThread
    {
        get => selectedCodexThread;
        set
        {
            if (ReferenceEquals(selectedCodexThread, value))
            {
                return;
            }

            selectedCodexThread = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CurrentSessionTitle));
            OnPropertyChanged(nameof(CurrentSessionUpdatedAt));
            if (SourceIndex == 1)
            {
                _ = LoadCodexThreadAsync(value);
            }
        }
    }

    public string DraftText
    {
        get => draftText;
        private set => SetProperty(ref draftText, value);
    }

    public string StatusText
    {
        get => statusText;
        private set => SetProperty(ref statusText, value);
    }

    private void SetUiStatus(string value, string source)
    {
        StatusText = value;
        var state = value.StartsWith("Loading", StringComparison.Ordinal) || value.StartsWith("正在加载", StringComparison.Ordinal) ? "Loading"
            : value.Equals("Ready", StringComparison.Ordinal) || value.Equals(UiStrings.Ready, StringComparison.Ordinal) ? "Ready"
            : value.Contains("失败", StringComparison.Ordinal) || value.StartsWith("Codex 错误", StringComparison.Ordinal) ? "Error"
            : null;
        if (state is not null)
        {
            uiLog.Write($"ui status -> {state}; source={source}");
        }
    }

    public string NativeMessagingText
    {
        get => nativeMessagingText;
        private set => SetProperty(ref nativeMessagingText, value);
    }

    public string CodexStatusText
    {
        get => codexStatusText;
        private set
        {
            if (!SetProperty(ref codexStatusText, value)) return;
            OnPropertyChanged(nameof(CodexStatusShort));
        }
    }

    public string CodexStatusShort => CodexStatusText.StartsWith("Connected", StringComparison.Ordinal)
        ? $"{UiStrings.Connected} · {CodexStatusText[(CodexStatusText.IndexOf('·') + 1)..].Trim()}"
        : CodexStatusText switch
        {
            "Disconnected" => UiStrings.Disconnected,
            "Not found" => UiStrings.GitNotFound,
            "Error" => "错误",
            _ => CodexStatusText,
        };

    public string CodexErrorDetail
    {
        get => codexErrorDetail;
        private set => SetProperty(ref codexErrorDetail, value);
    }

    public int CodexModelCount
    {
        get => codexModelCount;
        private set => SetProperty(ref codexModelCount, value);
    }

    public string SelectedTurnSummary =>
        $"当前选择：{Turns.Count(row => row.IsSelected)} / {Turns.Count} 轮";

    public string ActiveSourceLabel => SourceIndex == 1
        ? (SelectedCodexThread is null ? string.Empty : "Codex")
        : (SelectedConversation is null ? string.Empty : "ChatGPT");
    public string CurrentSessionTitle => SourceIndex == 1 ? SelectedCodexThread?.Title ?? UiStrings.NoSession : SelectedConversation?.Title ?? UiStrings.NoSession;
    public string ChatGptEmptyText => WorkspaceOnly ? "当前工作区暂无会话" : UiStrings.NoSession;
    public string CodexEmptyText => WorkspaceOnly ? "当前工作区暂无会话" : UiStrings.NoSession;
    public string CurrentSessionUpdatedAt => SourceIndex == 1
        ? (SelectedCodexThread?.UpdatedAt == DateTimeOffset.MinValue ? string.Empty : SelectedCodexThread?.UpdatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? string.Empty)
        : SelectedConversation?.UpdatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? string.Empty;

    public string ProjectPath => SelectedWorkspace?.Path ?? Environment.CurrentDirectory;

    public WorkspaceItem? SelectedWorkspace
    {
        get => selectedWorkspace;
        private set
        {
            if (Equals(selectedWorkspace, value)) return;
            selectedWorkspace = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(WorkspaceSelection));
            OnPropertyChanged(nameof(ProjectPath));
        }
    }

    public WorkspaceItem? WorkspaceSelection => SelectedWorkspace;
    public bool IsWorkspaceSwitchInProgress => workspaceSwitchInProgress;

    public string NewWorkspacePath { get => newWorkspacePath; set => SetProperty(ref newWorkspacePath, value); }
    public void ApplyWorkspaceSettings(bool autoSaveBeforeSwitch, bool promptBeforeExit, int retentionCount)
    {
        AutoSaveBeforeWorkspaceSwitch = autoSaveBeforeSwitch;
        PromptSaveBeforeExit = promptBeforeExit;
        AutoSnapshotRetentionCount = retentionCount;
    }
    public string ProjectFilePreviewTitle { get => projectFilePreviewTitle; private set => SetProperty(ref projectFilePreviewTitle, value); }
    public string ProjectFilePreviewText { get => projectFilePreviewText; private set => SetProperty(ref projectFilePreviewText, value); }
    public int ProjectFileCount { get; private set; }
    public string NewNoteName { get => newNoteName; set => SetProperty(ref newNoteName, value); }
    public WorkspaceSnapshotEntry? SelectedSnapshot
    {
        get => selectedSnapshot;
        set
        {
            if (!SetProperty(ref selectedSnapshot, value)) return;
            OnPropertyChanged(nameof(CanRestoreSnapshot));
            OnPropertyChanged(nameof(CanPreviewSnapshot));
            OnPropertyChanged(nameof(CanExportSnapshot));
            OnPropertyChanged(nameof(CanRenameSnapshot));
            OnPropertyChanged(nameof(CanDeleteSnapshot));
        }
    }
    public bool CanRestoreSnapshot => SelectedSnapshot?.Snapshot is not null;
    public bool CanPreviewSnapshot => SelectedSnapshot?.Snapshot is not null;
    public bool CanExportSnapshot => SelectedSnapshot?.Snapshot is not null;
    public bool CanRenameSnapshot => SelectedSnapshot?.Snapshot is not null;
    public bool CanDeleteSnapshot => SelectedSnapshot is not null;
    public string SnapshotName { get => snapshotName; set => SetProperty(ref snapshotName, value); }
    public bool AutoSaveBeforeWorkspaceSwitch { get => autoSaveBeforeWorkspaceSwitch; set => SetProperty(ref autoSaveBeforeWorkspaceSwitch, value); }
    public bool PromptSaveBeforeExit { get => promptSaveBeforeExit; set => SetProperty(ref promptSaveBeforeExit, value); }
    public int AutoSnapshotRetentionCount
    {
        get => autoSnapshotRetentionCount;
        set => SetProperty(ref autoSnapshotRetentionCount, Math.Clamp(value, 3, 50));
    }
    public string SnapshotOperationErrorMessage { get => snapshotOperationErrorMessage; private set => SetProperty(ref snapshotOperationErrorMessage, value); }
    public void SetSnapshotOperationError(string message) => SnapshotOperationErrorMessage = message;
    public string SnapshotImportErrorMessage { get => snapshotImportErrorMessage; private set => SetProperty(ref snapshotImportErrorMessage, value); }
    public NoteItem? SelectedNote { get; set; }
    public string GitBranch
    {
        get => gitBranch;
        private set
        {
            if (!SetProperty(ref gitBranch, value)) return;
            OnPropertyChanged(nameof(GitBranchDisplay));
        }
    }
    public string GitBranchDisplay => GitBranch == "Git not found" ? UiStrings.GitNotFound : GitBranch;
    public int GitModifiedCount => GitChangedFiles.Count(file => file.Status == "M");
    public int GitAddedCount => GitChangedFiles.Count(file => file.Status == "A");
    public int GitDeletedCount => GitChangedFiles.Count(file => file.Status == "D");
    public string DiffPreviewTitle { get => diffPreviewTitle; private set => SetProperty(ref diffPreviewTitle, value); }
    public string DiffPreviewText { get => diffPreviewText; private set => SetProperty(ref diffPreviewText, value); }
    public bool EnableGitIntegration
    {
        get => enableGitIntegration;
        set { if (!SetProperty(ref enableGitIntegration, value)) return; _ = RefreshGitStatusAsync(); }
    }
    public int ContextItemCount => ContextItems.Count;
    public int ContextCharacters => ContextItems.Sum(item => item.Content.Length);
    public string ContextItemSummary => UiStrings.SelectedContextSummary(ContextItemCount);
    public string ContextCharacterSummary => UiStrings.ContextCharacters(ContextCharacters);
    public string ContextSizeState => UiStrings.SizeState(ContextPackService.ClassifySize(ContextCharacters));
    public string ContextPreviewText => contextPreviewText;

    public string SessionSearchText
    {
        get => sessionSearchText;
        set
        {
            if (!SetProperty(ref sessionSearchText, value)) return;
            ConversationsView.Refresh();
            CodexThreadsView.Refresh();
        }
    }

    public string SessionFilter
    {
        get => sessionFilter;
        set
        {
            if (!SetProperty(ref sessionFilter, value)) return;
            ConversationsView.Refresh();
            CodexThreadsView.Refresh();
        }
    }

    public bool WorkspaceOnly
    {
        get => workspaceOnly;
        set
        {
            if (!SetProperty(ref workspaceOnly, value)) return;
            OnPropertyChanged(nameof(ChatGptEmptyText));
            OnPropertyChanged(nameof(CodexEmptyText));
            ConversationsView.Refresh();
            CodexThreadsView.Refresh();
            if (SelectedConversation is not null && !ConversationsView.Contains(SelectedConversation)) SelectedConversation = null;
            if (SelectedCodexThread is not null && !CodexThreadsView.Contains(SelectedCodexThread)) SelectedCodexThread = null;
        }
    }

    public string CodexHome { get => codexHome; private set => SetProperty(ref codexHome, value); }
    public string CodexVersion { get => codexVersion; private set => SetProperty(ref codexVersion, value); }
    public string CodexExecutable { get => codexExecutable; private set => SetProperty(ref codexExecutable, value); }
    public bool CodexDetailsExpanded { get => codexDetailsExpanded; set => SetProperty(ref codexDetailsExpanded, value); }

    public async Task InitializeAsync()
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        NativeMessagingText = NativeMessagingStatusDetector.Detect().DisplayText;
        try
        {
            await importer.StartAsync();
            await RefreshAsync();
            SetUiStatus(UiStrings.Ready, "startup");
        }
        catch (Exception exception)
        {
            SetUiStatus($"ChatGPT 初始化失败：{exception.Message}", "startup");
        }

        _ = RefreshCodexAsync();
    }

    public async Task RefreshAsync()
    {
        SetUiStatus(UiStrings.Loading, "chatgpt");
        try
        {
            var selectedId = selectedConversation?.Conversation.Id;
            var conversations = await repository.ListAsync();
            Conversations.Clear();
            foreach (var conversation in conversations)
            {
                Conversations.Add(new ConversationListItemViewModel(conversation));
            }

            SelectedConversation = Conversations.FirstOrDefault(item => item.Conversation.Id == selectedId && ConversationsView.Contains(item))
                ?? ConversationsView.Cast<ConversationListItemViewModel>().FirstOrDefault();
            if (SourceIndex == 0)
            {
                SetUiStatus(UiStrings.Ready, "chatgpt");
            }
        }
        catch (Exception exception)
        {
            SetUiStatus($"ChatGPT 刷新失败：{exception.Message}", "chatgpt");
        }
        finally
        {
            if (StatusText.StartsWith("Loading", StringComparison.Ordinal) || StatusText.StartsWith("正在加载", StringComparison.Ordinal))
            {
                SetUiStatus(UiStrings.Ready, "chatgpt");
            }
        }
    }

    private Task RefreshActiveAsync() => SourceIndex == 1 ? RefreshCodexAsync() : RefreshAsync();

    public async Task RefreshCodexAsync()
    {
        if (codexClientFactory is not null && codexClient.IsFaulted)
        {
            codexLog.Write("faulted client detected; disposing and recreating client");
            await codexClient.DisposeAsync();
            codexClient = codexClientFactory();
            codexLog.Write("new Codex client created");
        }

        CodexStatusText = "Disconnected";
        SetUiStatus(UiStrings.Loading, "codex");
        CodexErrorDetail = string.Empty;
        CodexExecutable = codexClient.ExecutablePath ?? "未选择";
        codexLog.Write($"refresh start; selected executable={codexClient.ExecutablePath ?? "not selected"}");
        try
        {
            var selectedId = selectedCodexThread?.Thread.Id;
            var probe = await codexClient.ProbeAsync();
            CodexModelCount = probe.Models.Count;
            CodexHome = probe.Initialize.CodexHome ?? "未知";
            CodexVersion = probe.Initialize.UserAgent ?? "未知";
            CodexStatusText = $"Connected · {probe.Account.DisplayLabel}";

            var threads = await codexClient.ListThreadsAsync(100);
            CodexThreads.Clear();
            foreach (var thread in threads)
            {
                CodexThreads.Add(new CodexThreadListItemViewModel(thread));
            }

            SelectedCodexThread = CodexThreads.FirstOrDefault(item => item.Thread.Id == selectedId && CodexThreadsView.Contains(item))
                ?? CodexThreadsView.Cast<CodexThreadListItemViewModel>().FirstOrDefault();
            if (SourceIndex == 1)
            {
                SetUiStatus(UiStrings.Ready, "codex");
                if (SelectedCodexThread is null)
                {
                    ClearTurns();
                }
            }
        }
        catch (CodexExecutableNotFoundException exception)
        {
            CodexStatusText = "Not found";
            CodexModelCount = 0;
            RecordCodexError(exception);
            CodexThreads.Clear();
            if (SourceIndex == 1)
            {
                SetUiStatus("未找到 Codex 可执行文件", "codex");
                ClearTurns();
            }
        }
        catch (CodexDisconnectedException exception)
        {
            CodexStatusText = "Disconnected";
            CodexModelCount = 0;
            RecordCodexError(exception);
            if (SourceIndex == 1)
            {
                SetUiStatus($"Codex 已断开：{exception.Message}", "codex");
            }
        }
        catch (OperationCanceledException)
        {
            CodexStatusText = "Disconnected";
        }
        catch (Exception exception)
        {
            CodexStatusText = "Error";
            CodexModelCount = 0;
            RecordCodexError(exception);
            if (SourceIndex == 1)
            {
                SetUiStatus($"Codex 错误：{exception.Message}", "codex");
            }
        }
        finally
        {
            if (StatusText.StartsWith("Loading", StringComparison.Ordinal) || StatusText.StartsWith("正在加载", StringComparison.Ordinal))
            {
                SetUiStatus(UiStrings.Ready, "codex");
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        importer.Imported -= OnImported;
        codexThreadLoad?.Cancel();
        codexThreadLoad?.Dispose();
        codexThreadLoad = null;
        await importer.DisposeAsync();
        await codexClient.DisposeAsync();
    }

    private void LoadActiveSource()
    {
        if (SourceIndex == 0)
        {
            codexThreadLoad?.Cancel();
            LoadChatGptTurns(SelectedConversation?.Conversation);
            StatusText = $"已加载 {Conversations.Count} 个 ChatGPT 会话";
        }
        else
        {
            _ = LoadCodexThreadAsync(SelectedCodexThread);
            StatusText = $"已加载 {CodexThreads.Count} 个 Codex 会话";
        }
    }

    private void LoadChatGptTurns(StoredConversation? conversation)
    {
        ClearTurnRows();
        codexSnapshot = null;
        codexSelectedTurnIds = null;
        chatGptTurnSelection = conversation is null ? null : new TurnSelection(conversation.Payload);
        if (conversation is not null && chatGptTurnSelection is not null)
        {
            foreach (var turn in conversation.Payload.Turns)
            {
                AddTurnRow(new TurnRowViewModel(
                    turn,
                    chatGptTurnSelection.IsSelected(turn.Id),
                    "用户",
                    "ChatGPT",
                    conversation.CapturedAt));
            }
        }

        UpdateDraft();
        OnPropertyChanged(nameof(SelectedTurnSummary));
    }

    private async Task LoadCodexThreadAsync(CodexThreadListItemViewModel? item)
    {
        var sequence = Interlocked.Increment(ref codexLoadSequence);
        codexThreadLoad?.Cancel();
        codexThreadLoad?.Dispose();
        codexThreadLoad = new CancellationTokenSource();
        if (item is null)
        {
            if (SourceIndex == 1)
            {
                ClearTurns();
            }

            return;
        }

        try
        {
            StatusText = "正在读取 Codex 会话…";
            var loadCancellation = codexThreadLoad.Token;
            var snapshot = await codexClient.ReadThreadAsync(item.Thread.Id, loadCancellation);
            if (sequence != codexLoadSequence || SourceIndex != 1)
            {
                return;
            }

            LoadCodexTurns(snapshot);
            CodexStatusText = CodexStatusText.StartsWith("Connected", StringComparison.Ordinal)
                ? CodexStatusText
                : "Connected";
            StatusText = $"已读取 {snapshot.Turns.Count} 个 Codex 可见轮次";
        }
        catch (OperationCanceledException)
        {
        }
        catch (CodexDisconnectedException exception)
        {
            CodexStatusText = "Disconnected";
            RecordCodexError(exception);
            StatusText = $"Codex 已断开：{exception.Message}";
            ClearTurns();
        }
        catch (Exception exception)
        {
            CodexStatusText = "Error";
            RecordCodexError(exception);
            StatusText = $"Codex thread/read 失败：{exception.Message}";
            ClearTurns();
        }
    }

    private void LoadCodexTurns(CodexThreadSnapshot snapshot)
    {
        ClearTurnRows();
        chatGptTurnSelection = null;
        codexSnapshot = snapshot;
        codexSelectedTurnIds = snapshot.Turns
            .Select(turn => turn.Id)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var turn in snapshot.Turns)
        {
            var captured = new CapturedTurn
            {
                Id = turn.Id,
                Index = turn.Index,
                User = new CapturedMessage
                {
                    Id = turn.UserMessage.Id,
                    IdSource = "app-server",
                    Text = turn.UserMessage.Text,
                },
                Assistant = turn.AgentMessage is null
                    ? null
                    : new CapturedMessage
                    {
                        Id = turn.AgentMessage.Id,
                        IdSource = "app-server",
                        Text = turn.AgentMessage.Text,
                    },
                Complete = string.Equals(turn.Status, "completed", StringComparison.Ordinal),
            };
            AddTurnRow(new TurnRowViewModel(captured, true, "用户", "Codex",
                snapshot.Summary.UpdatedAt ?? snapshot.Summary.RecencyAt ?? snapshot.Summary.CreatedAt));
        }

        UpdateDraft();
        OnPropertyChanged(nameof(SelectedTurnSummary));
    }

    private void OnTurnPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (sender is not TurnRowViewModel row || args.PropertyName != nameof(TurnRowViewModel.IsSelected))
        {
            return;
        }

        if (SourceIndex == 0)
        {
            chatGptTurnSelection?.SetSelected(row.Turn.Id, row.IsSelected);
        }
        else if (codexSelectedTurnIds is not null)
        {
            if (row.IsSelected)
            {
                codexSelectedTurnIds.Add(row.Turn.Id);
            }
            else
            {
                codexSelectedTurnIds.Remove(row.Turn.Id);
            }
        }

        if (!preserveDraftDuringSelectionClear)
        {
            UpdateDraft();
        }

        OnPropertyChanged(nameof(SelectedTurnSummary));
    }

    private void SelectAll()
    {
        if (SourceIndex == 0)
        {
            chatGptTurnSelection?.SelectAll();
        }
        else if (codexSnapshot is not null && codexSelectedTurnIds is not null)
        {
            codexSelectedTurnIds.Clear();
            foreach (var turn in codexSnapshot.Turns)
            {
                codexSelectedTurnIds.Add(turn.Id);
            }
        }

        foreach (var row in Turns)
        {
            row.IsSelected = true;
        }

        UpdateDraft();
        OnPropertyChanged(nameof(SelectedTurnSummary));
    }

    private void ClearSelection()
    {
        if (SourceIndex == 0)
        {
            chatGptTurnSelection?.Clear();
        }
        else
        {
            codexSelectedTurnIds?.Clear();
        }

        preserveDraftDuringSelectionClear = true;
        try
        {
            foreach (var row in Turns)
            {
                row.IsSelected = false;
            }
        }
        finally
        {
            preserveDraftDuringSelectionClear = false;
        }

        OnPropertyChanged(nameof(SelectedTurnSummary));
    }

    private void UpdateDraft()
    {
        if (SourceIndex == 0)
        {
            DraftText = selectedConversation is null || chatGptTurnSelection is null
                ? string.Empty
                : SelectedConversationText.Render(
                    selectedConversation.Conversation.Payload,
                    chatGptTurnSelection.SelectedTurnIds);
            return;
        }

        DraftText = codexSnapshot is null || codexSelectedTurnIds is null
            ? string.Empty
            : CodexDraftRenderer.Render(codexSnapshot, codexSelectedTurnIds);
    }

    private async Task CopyDraftAsync()
    {
        if (string.IsNullOrEmpty(DraftText))
        {
            StatusText = "没有可复制的草稿";
            return;
        }

        var result = await ClipboardService.Shared.CopyTextAsync(DraftText);
        if (result.Succeeded)
        {
            StatusText = "草稿已复制到剪贴板";
            return;
        }

        StatusText = UiStrings.CopyFailed(result.Error?.Message ?? "Clipboard unavailable.");
    }

    private void OpenChatGpt()
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://chatgpt.com/") { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            StatusText = $"无法打开 ChatGPT：{exception.Message}";
        }
    }

    private void OnImported(object? sender, CaptureImportedEventArgs args)
    {
        _ = Application.Current.Dispatcher.InvokeAsync(RefreshAsync);
    }

    private void AddTurnRow(TurnRowViewModel row)
    {
        row.PropertyChanged += OnTurnPropertyChanged;
        Turns.Add(row);
        OnPropertyChanged(nameof(SelectedTurnSummary));
    }

    private bool FilterConversation(object value) => (SessionFilter is "All" or "ChatGPT")
        && !WorkspaceOnly
        && MatchesSearch(value is ConversationListItemViewModel item
            ? $"{item.Title} {item.Preview} {item.Details}" : string.Empty);

    private bool FilterCodexThread(object value) => (SessionFilter is "All" or "Codex") && MatchesSearch(value is CodexThreadListItemViewModel item
        ? $"{item.Title} {item.Preview} {item.Details} {item.Thread.Cwd}" : string.Empty)
        && (!WorkspaceOnly || IsInSelectedWorkspace((CodexThreadListItemViewModel)value));

    private bool IsInSelectedWorkspace(CodexThreadListItemViewModel item)
    {
        if (SelectedWorkspace is null || string.IsNullOrWhiteSpace(SelectedWorkspace.Path) || string.IsNullOrWhiteSpace(item.Thread.Cwd)) return false;
        var workspacePath = NormalizePath(SelectedWorkspace.Path);
        var cwd = NormalizePath(item.Thread.Cwd);
        return string.Equals(cwd, workspacePath, StringComparison.OrdinalIgnoreCase)
            || cwd.StartsWith(workspacePath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private bool MatchesSearch(string text) => string.IsNullOrWhiteSpace(SessionSearchText)
        || text.Contains(SessionSearchText.Trim(), StringComparison.OrdinalIgnoreCase);

    public void RefreshProjectExplorer()
    {
        ProjectRootItems.Clear();
        if (SelectedWorkspace is null || !Directory.Exists(SelectedWorkspace.Path)) return;
        ProjectRootItems.Add(BuildTree(new DirectoryInfo(SelectedWorkspace.Path)));
        ProjectFileCount = new ProjectContextService().Build(SelectedWorkspace, IgnoreFolders).Files.Count;
        OnPropertyChanged(nameof(ProjectFileCount));
        LoadNotes();
    }

    private static ProjectTreeItem BuildTree(DirectoryInfo directory)
    {
        var node = new ProjectTreeItem(directory.Name, directory.FullName, true);
        try
        {
            foreach (var child in directory.EnumerateDirectories().OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (ProjectContextService.DefaultIgnoreFolders.Contains(child.Name, StringComparer.OrdinalIgnoreCase)) continue;
                node.Children.Add(BuildTree(child));
            }
            foreach (var file in directory.EnumerateFiles().OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)) node.Children.Add(new ProjectTreeItem(file.Name, file.FullName, false));
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            // Keep accessible project content visible when one subtree cannot be read.
        }
        return node;
    }

    public void SelectProjectFile(string path)
    {
        selectedProjectFilePath = null;
        try
        {
            var info = new FileInfo(path);
            ProjectFilePreviewTitle = info.Name;
            if (!info.Exists || (info.Attributes & FileAttributes.Directory) != 0) { ProjectFilePreviewText = "无法读取文件"; return; }
            if (info.Length > 512 * 1024) { ProjectFilePreviewText = "文件过大"; return; }
            var allowed = new[] { ".txt", ".md", ".json", ".cs", ".cpp", ".h" };
            if (!allowed.Contains(info.Extension, StringComparer.OrdinalIgnoreCase)) { ProjectFilePreviewText = "不支持的文件类型"; return; }
            ProjectFilePreviewText = File.ReadAllText(path);
            selectedProjectFilePath = info.FullName;
        }
        catch (Exception exception) { ProjectFilePreviewText = $"无法读取文件：{exception.Message}"; }
    }

    public void AddSelectedProjectFileToContext()
    {
        if (string.IsNullOrWhiteSpace(selectedProjectFilePath))
        {
            StatusText = "请先选择可加入上下文的文件。";
            return;
        }

        AddProjectFileToContext(selectedProjectFilePath);
    }

    public void GenerateProjectContext()
    {
        if (SelectedWorkspace is null) return;
        try
        {
            var service = new ProjectContextService();
            var snapshot = service.Build(SelectedWorkspace, IgnoreFolders);
            ProjectFileCount = snapshot.Files.Count; OnPropertyChanged(nameof(ProjectFileCount)); service.Write(SelectedWorkspace, IgnoreFolders);
        }
        catch (Exception exception) { codexLog.Write($"context snapshot exception; type={exception.GetType().FullName}; message={exception.Message}"); }
    }

    public void AddTurnToContext(TurnRowViewModel row)
    {
        if (SelectedWorkspace is null) return;
        var type = SourceIndex == 0 ? "ChatGPTTurn" : "CodexTurn";
        var source = SourceIndex == 0 ? selectedConversation?.Title ?? "ChatGPT" : selectedCodexThread?.Title ?? "Codex";
        var reference = SourceIndex == 0 ? $"{selectedConversation?.Conversation.Id}:{row.Turn.Id}" : $"{selectedCodexThread?.Thread.Id}:{row.Turn.Index}";
        var content = $"User:\n{row.Turn.User.Text}\n\n{(SourceIndex == 0 ? "Assistant" : "Codex")}:\n{row.Turn.Assistant?.Text ?? ""}";
        AddContextItem(new ContextPackItem(type, $"{source} · 第{row.Turn.Index + 1}轮", source, content, reference, ContextItems.Count, ContextPackService.CreateKey(type, source, reference, content)));
    }

    public void AddProjectFileToContext(string path)
    {
        if (SelectedWorkspace is null) return;
        try
        {
            if (!TryCreateProjectFileContext(SelectedWorkspace, path, out var item, out var error)) { StatusText = error; return; }
            if (ContextItems.Any(existing => existing.Type == "ProjectFile" && string.Equals(existing.ReferencePath, item.ReferencePath, StringComparison.OrdinalIgnoreCase))) { StatusText = $"已存在于上下文：{Path.GetFileName(path)}"; return; }
            AddContextItem(item with { Order = ContextItems.Count });
            StatusText = $"已加入上下文：{Path.GetFileName(path)}";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            StatusText = $"无法将文件加入上下文：{exception.Message}";
        }
    }

    public static bool TryCreateProjectFileContext(WorkspaceItem workspace, string path, out ContextPackItem item, out string error)
    {
        item = default!;
        error = string.Empty;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || (info.Attributes & FileAttributes.Directory) != 0) { error = "无法将目录加入上下文"; return false; }
            if (info.Length > 512 * 1024) { error = "文件过大，无法添加到上下文"; return false; }
            var allowed = new[] { ".txt", ".md", ".json", ".cs", ".cpp", ".h" }; if (!allowed.Contains(info.Extension, StringComparer.OrdinalIgnoreCase)) { error = "不支持的文件类型，无法加入上下文"; return false; }
            var relative = Path.GetRelativePath(workspace.Path, info.FullName);
            var content = File.ReadAllText(info.FullName); var key = ContextPackService.CreateKey("ProjectFile", relative, $"{relative}|{info.LastWriteTimeUtc:O}", content);
            item = new ContextPackItem("ProjectFile", $"File · {relative}", "Workspace", content, relative, 0, key);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            error = $"无法将文件加入上下文：{exception.Message}";
            return false;
        }
    }

    public async Task AddGitDiffToContextAsync(GitChangedFile file)
    {
        if (SelectedWorkspace is null) return;
        var content = await new GitService().GetDiffAsync(SelectedWorkspace.Path, file.Path); if (content == "Diff too large") { StatusText = "差异过大，无法添加到上下文"; return; }
        var key = ContextPackService.CreateKey("GitDiff", file.Path, file.Path, content); AddContextItem(new ContextPackItem("GitDiff", $"Diff · {file.Path}", "Git", content, file.Path, ContextItems.Count, key));
    }

    public void AddNoteToContext(NoteItem note)
    {
        if (SelectedWorkspace is null) return;
        var full = Path.GetFullPath(note.Path); var notesRoot = Path.GetFullPath(Path.Combine(SelectedWorkspace.Path, ".ai", "notes")); if (!full.StartsWith(notesRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return;
        var content = File.ReadAllText(full); var key = ContextPackService.CreateKey("Note", note.Name, $"{full}|{File.GetLastWriteTimeUtc(full):O}", content); AddContextItem(new ContextPackItem("Note", $"Note · {note.Name}", "Notes", content, note.Name, ContextItems.Count, key));
    }

    private void AddContextItem(ContextPackItem item)
    {
        if (ContextItems.Any(existing => existing.Key == item.Key)) return;
        ContextItems.Add(item); NotifyContext(); codexLog.Write($"context item added; type={item.Type}; count={ContextItems.Count}; path={item.ReferencePath ?? "(none)"}");
    }

    public void RemoveContextItem(ContextPackItem item) { ContextItems.Remove(item); NotifyContext(); codexLog.Write($"context item removed; type={item.Type}; count={ContextItems.Count}"); }
    private void ClearContext() { ContextItems.Clear(); NotifyContext(); codexLog.Write("context cleared; count=0"); }
    private ContextPack BuildContextPack() => contextPackService.Create("Context Pack", SelectedWorkspace!, ContextItems);
    private string RenderContext() => contextPackService.RenderMarkdown(BuildContextPack(), GitBranch, GitModifiedCount);
    private void NotifyContext() { OnPropertyChanged(nameof(ContextItemCount)); OnPropertyChanged(nameof(ContextCharacters)); OnPropertyChanged(nameof(ContextItemSummary)); OnPropertyChanged(nameof(ContextCharacterSummary)); OnPropertyChanged(nameof(ContextSizeState)); OnPropertyChanged(nameof(ContextPreviewText)); }
    private async Task CopyContextAsync()
    {
        if (SelectedWorkspace is null || ContextItems.Count == 0) return;
        var result = await ClipboardService.Shared.CopyTextAsync(RenderContext());
        if (result.Succeeded) codexLog.Write($"context copied; count={ContextItems.Count}");
    }
    private void ExportContext()
    {
        if (SelectedWorkspace is null || ContextItems.Count == 0) return;
        var directory = Path.Combine(SelectedWorkspace.Path, ".ai", "exports", "context"); Directory.CreateDirectory(directory); var baseName = $"{DateTime.Now:yyyy-MM-dd-HHmm}-context"; var path = Path.Combine(directory, baseName + ".md"); var index = 2; while (File.Exists(path)) path = Path.Combine(directory, $"{baseName}-{index++}.md"); File.WriteAllText(path, RenderContext()); codexLog.Write($"context exported; count={ContextItems.Count}; path=.ai/exports/context/{Path.GetFileName(path)}");
    }
    private void PreviewContext()
    {
        if (SelectedWorkspace is null || ContextItems.Count == 0) return;
        var view = new ContextPreviewView { DataContext = RenderContext() }; var window = new Window { Title = "Context Preview", Width = 900, Height = 700, Owner = Application.Current.MainWindow, Content = view, WindowStartupLocation = WindowStartupLocation.CenterOwner }; window.ShowDialog();
    }

    public void OpenNotesWindow()
    {
        var owner = Application.Current.MainWindow;
        var window = new Window { Title = (string)(Application.Current.FindResource("UiNotesWindowTitle") ?? "Codex Bridge 笔记"), Width = 640, Height = 520, Owner = owner, Content = new NotesView { DataContext = this }, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        window.ShowDialog();
    }

    public void OpenSnapshotsWindow()
    {
        RefreshSnapshots();
        var owner = Application.Current.MainWindow;
        var window = new Window
        {
            Title = (string)(Application.Current.FindResource("UiSnapshotsWindowTitle") ?? "Codex Bridge 工作现场"),
            Width = 720,
            Height = 560,
            Owner = owner,
            Content = new SnapshotView { DataContext = this },
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        window.ShowDialog();
    }

    public void RefreshSnapshots()
    {
        SnapshotEntries.Clear();
        if (SelectedWorkspace is null) return;
        var autoIds = snapshotAutoIndexService.Read(SelectedWorkspace.Path).SnapshotIds;
        foreach (var entry in snapshotService.List(SelectedWorkspace.Path))
        {
            SnapshotEntries.Add(entry with { IsAuto = entry.Snapshot is not null && autoIds.Contains(entry.Snapshot.SnapshotId) });
        }
    }

    public string GetSelectedSnapshotExportFileName() =>
        SelectedSnapshot?.Snapshot is { } snapshot
            ? snapshotPackageService.BuildDefaultFileName(snapshot)
            : "CodexBridge-Snapshot.zip";

    public bool ExportSelectedSnapshot(string packagePath)
    {
        if (SelectedSnapshot?.Snapshot is null || string.IsNullOrWhiteSpace(packagePath)) return false;
        try
        {
            snapshotPackageService.Export(packagePath, SelectedSnapshot.Snapshot);
            StatusText = UiStrings.SnapshotExported;
            return true;
        }
        catch (Exception exception)
        {
            StatusText = UiStrings.SnapshotExportFailed(exception.Message);
            return false;
        }
    }

    public SnapshotPackageInspection? InspectSnapshotImport(string packagePath)
    {
        SnapshotImportErrorMessage = string.Empty;
        if (SelectedWorkspace is null || string.IsNullOrWhiteSpace(packagePath)) return null;
        try
        {
            return snapshotPackageService.InspectImport(packagePath, SelectedWorkspace.Name, SelectedWorkspace.Path);
        }
        catch (Exception exception)
        {
            SnapshotImportErrorMessage = exception.Message;
            StatusText = UiStrings.SnapshotImportFailed(exception.Message);
            return null;
        }
    }

    public void BeginSnapshotImport() => SnapshotImportErrorMessage = string.Empty;

    public bool ImportSnapshotPackage(SnapshotPackageInspection inspection)
    {
        SnapshotImportErrorMessage = string.Empty;
        if (inspection is null || SelectedWorkspace is null) return false;
        try
        {
            var imported = snapshotPackageService.ImportValidated(inspection);
            RefreshSnapshots();
            SelectedSnapshot = SnapshotEntries.FirstOrDefault(entry => entry.Snapshot?.SnapshotId == imported.SnapshotId);
            StatusText = UiStrings.SnapshotImported;
            return true;
        }
        catch (Exception exception)
        {
            SnapshotImportErrorMessage = exception.Message;
            StatusText = UiStrings.SnapshotImportFailed(exception.Message);
            return false;
        }
    }

    public async Task<bool> SaveCurrentSnapshotAsync(string requestedName)
    {
        if (SelectedWorkspace is null) { StatusText = "请先选择工作区。"; return false; }
        var snapshot = await BuildCurrentSnapshotAsync(requestedName);
        snapshotService.Save(SelectedWorkspace.Path, snapshot);
        RefreshSnapshots();
        StatusText = "工作现场已保存。";
        return true;
    }

    public async Task<bool> QuickSaveCurrentSnapshotAsync()
    {
        SnapshotOperationErrorMessage = string.Empty;
        if (SelectedWorkspace is null)
        {
            SnapshotOperationErrorMessage = "请先选择工作区。";
            StatusText = UiStrings.QuickSaveFailed(SnapshotOperationErrorMessage);
            return false;
        }

        try
        {
            var snapshot = await BuildCurrentSnapshotAsync($"快速保存 {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            snapshotService.Save(SelectedWorkspace.Path, snapshot);
            RefreshSnapshots();
            StatusText = UiStrings.QuickSaveSucceeded;
            return true;
        }
        catch (Exception exception)
        {
            SnapshotOperationErrorMessage = exception.Message;
            StatusText = UiStrings.QuickSaveFailed(exception.Message);
            return false;
        }
    }

    public async Task<bool> HasUnsavedSnapshotChangesAsync()
    {
        if (!PromptSaveBeforeExit || SelectedWorkspace is null) return false;
        var current = await BuildCurrentSnapshotAsync("exit-check");
        var latest = snapshotService.List(SelectedWorkspace.Path)
            .Where(entry => entry.Snapshot is not null && WorkspaceSnapshotService.IsWorkspaceMatch(entry.Snapshot, SelectedWorkspace.Path))
            .Select(entry => entry.Snapshot!)
            .OrderByDescending(snapshot => snapshot.CreatedAt)
            .ThenByDescending(snapshot => snapshot.UpdatedAt)
            .FirstOrDefault();
        return latest is null || WorkspaceStateFingerprint.Compute(latest) != WorkspaceStateFingerprint.Compute(current);
    }

    public async Task<bool> SaveExitSnapshotAsync()
    {
        SnapshotOperationErrorMessage = string.Empty;
        if (SelectedWorkspace is null) return true;
        try
        {
            var snapshot = await BuildCurrentSnapshotAsync($"退出前保存 {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            snapshotService.Save(SelectedWorkspace.Path, snapshot);
            RefreshSnapshots();
            StatusText = "工作现场已保存。";
            return true;
        }
        catch (Exception exception)
        {
            SnapshotOperationErrorMessage = exception.Message;
            StatusText = UiStrings.ExitSnapshotSaveFailed;
            return false;
        }
    }

    private async Task<WorkspaceSnapshot> BuildCurrentSnapshotAsync(string requestedName)
    {
        if (SelectedWorkspace is null) throw new InvalidOperationException("请先选择工作区。");
        var now = DateTimeOffset.Now;
        var name = string.IsNullOrWhiteSpace(requestedName)
            ? (string.IsNullOrWhiteSpace(HandoffTitle) ? CurrentSessionTitle : HandoffTitle)
            : requestedName.Trim();
        if (string.IsNullOrWhiteSpace(name) || name == UiStrings.NoSession) name = $"{now:yyyy-MM-dd HH:mm} 工作现场";
        var selections = BuildSnapshotSelections();
        var git = await BuildSnapshotGitAsync();
        return new WorkspaceSnapshot
        {
            SnapshotId = $"{now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..8]}",
            Name = name,
            CreatedAt = now,
            UpdatedAt = now,
            Workspace = new WorkspaceSnapshotWorkspace { Name = SelectedWorkspace.Name, Path = SelectedWorkspace.Path },
            Selection = selections,
            ContextItems = ContextItems.Select(item => new WorkspaceSnapshotContextItem { Key = item.Key, Type = item.Type, Title = item.Title, Content = item.Content, Source = item.Source, ReferencePath = item.ReferencePath, Order = item.Order }).ToList(),
            Handoff = new WorkspaceSnapshotHandoff { Target = HandoffTarget, Template = HandoffTemplate, Title = HandoffTitle, Task = HandoffTask, CurrentState = HandoffCurrentState, Constraints = HandoffConstraints, NextAction = HandoffNextAction, IncludeContextPack = IncludeHandoffContext, IncludeWorkspace = IncludeHandoffWorkspace, IncludeGitState = IncludeHandoffGit, IncludeProjectFileSummary = IncludeHandoffProjectFiles },
            Git = git,
        };
    }

    public async Task<bool> RestoreSelectedSnapshotAsync()
    {
        if (SelectedWorkspace is null || SelectedSnapshot?.Snapshot is null) return false;
        if ((ContextItems.Count > 0 || HandoffPreviewText.Length > 0) && MessageBox.Show("当前上下文或交接内容将被替换。\n\n是否继续恢复工作现场？", "恢复工作现场", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return false;
        var snapshot = SelectedSnapshot.Snapshot;
        WorkspaceSnapshotService.Validate(snapshot);
        if (!WorkspaceSnapshotService.IsWorkspaceMatch(snapshot, SelectedWorkspace.Path))
        {
            StatusText = "工作现场属于其它工作区，无法恢复。";
            return false;
        }
        var currentGit = await BuildSnapshotGitAsync();
        var missing = ApplySnapshot(snapshot);
        var gitWarning = WorkspaceSnapshotService.CompareGit(snapshot.Git, currentGit);
        var warning = gitWarning is null ? string.Empty : $" 当前 Git 状态与保存现场时不同：{gitWarning}";
        StatusText = $"工作现场已恢复。恢复轮次：{snapshot.Selection.Count - missing}，缺失轮次：{missing}。{warning}";
        return true;
    }

    public async Task<SnapshotPreviewModel?> BuildSelectedSnapshotPreviewAsync()
    {
        if (SelectedWorkspace is null || SelectedSnapshot?.Snapshot is null) return null;
        var snapshot = SelectedSnapshot.Snapshot;
        var currentWorkspace = SelectedWorkspace;
        var current = new SnapshotPreviewCurrentState
        {
            WorkspaceName = currentWorkspace.Name,
            WorkspacePath = currentWorkspace.Path,
            Source = SourceIndex == 0 ? "ChatGPT" : "Codex",
            SessionId = SourceIndex == 0 ? selectedConversation?.Conversation.Id.ToString() : selectedCodexThread?.Thread.Id,
            VisibleTurnIds = Turns.Select(row => row.Turn.Id).ToHashSet(StringComparer.Ordinal),
            SelectedTurnCount = Turns.Count(row => row.IsSelected),
            ContextCount = ContextItems.Count,
            HandoffTitle = HandoffTitle,
            Git = await BuildSnapshotGitAsync(currentWorkspace.Path),
        };
        return SnapshotPreviewBuilder.Build(snapshot, current, SelectedSnapshot.IsAuto ? UiStrings.SnapshotTypeAutomatic : UiStrings.SnapshotTypeManual);
    }

    public void RenameSelectedSnapshot(string name)
    {
        if (SelectedWorkspace is null || SelectedSnapshot?.Snapshot is null || string.IsNullOrWhiteSpace(name)) return;
        var snapshotId = SelectedSnapshot.Snapshot.SnapshotId;
        snapshotService.Rename(SelectedWorkspace.Path, snapshotId, name.Trim());
        snapshotAutoIndexService.Remove(SelectedWorkspace.Path, snapshotId);
        RefreshSnapshots();
        SelectedSnapshot = SnapshotEntries.FirstOrDefault(entry => entry.Snapshot?.SnapshotId == snapshotId);
        StatusText = UiStrings.SnapshotRenamed;
    }

    public bool DeleteSelectedSnapshot(Func<WorkspaceSnapshotEntry, bool> confirmDelete)
    {
        if (SelectedWorkspace is null || SelectedSnapshot is null) return false;
        var entry = SelectedSnapshot;
        if (!confirmDelete(entry)) return false;
        snapshotService.DeleteDirectory(SelectedWorkspace.Path, entry.DirectoryPath);
        if (entry.Snapshot is not null)
        {
            snapshotAutoIndexService.Remove(SelectedWorkspace.Path, entry.Snapshot.SnapshotId);
        }
        RefreshSnapshots();
        SelectedSnapshot = null;
        StatusText = UiStrings.SnapshotDeleted;
        return true;
    }

    private List<WorkspaceSnapshotSelection> BuildSnapshotSelections()
    {
        var source = SourceIndex == 0 ? "ChatGPT" : "Codex";
        var sessionId = SourceIndex == 0 ? selectedConversation?.Conversation.Id.ToString() : selectedCodexThread?.Thread.Id;
        return CaptureSelectedTurnRefs(source, sessionId, Turns);
    }

    public static List<WorkspaceSnapshotSelection> CaptureSelectedTurnRefs(string source, string? sessionId, IEnumerable<TurnRowViewModel> rows)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return [];
        return rows.Where(row => row.IsSelected).Select(row => new WorkspaceSnapshotSelection
        {
            Source = source,
            SessionId = sessionId,
            TurnId = row.Turn.Id,
            Role = "User",
            TextHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(row.Turn.User.Text))),
        }).ToList();
    }

    private async Task<WorkspaceSnapshotGit> BuildSnapshotGitAsync() =>
        SelectedWorkspace is null ? new() : await BuildSnapshotGitAsync(SelectedWorkspace.Path);

    private async Task<WorkspaceSnapshotGit> BuildSnapshotGitAsync(string workspacePath)
    {
        if (!EnableGitIntegration) return new();
        var service = new GitService();
        var status = await service.GetStatusAsync(workspacePath);
        if (!status.GitAvailable) return new();
        var head = await service.GetHeadCommitAsync(workspacePath);
        var files = status.Files.Select(file => new WorkspaceSnapshotChangedFile { Status = file.Status, RelativePath = file.Path }).ToList();
        return new WorkspaceSnapshotGit { Branch = status.Branch, HeadCommit = head, ChangedFiles = files, StatusFingerprint = WorkspaceSnapshotService.BuildStatusFingerprint(status.Branch, head, files) };
    }

    private int ApplySnapshot(WorkspaceSnapshot snapshot)
    {
        var source = SourceIndex == 0 ? "ChatGPT" : "Codex";
        var sessionId = SourceIndex == 0 ? selectedConversation?.Conversation.Id.ToString() : selectedCodexThread?.Thread.Id;
        var selected = snapshot.Selection.Where(item => item.Source == source && item.SessionId == sessionId).ToDictionary(item => item.TurnId, StringComparer.Ordinal);
        var missing = snapshot.Selection.Count(item => item.Source != source || item.SessionId != sessionId);
        preserveDraftDuringSelectionClear = true;
        foreach (var row in Turns) row.IsSelected = selected.ContainsKey(row.Turn.Id);
        missing += snapshot.Selection.Count(item => item.Source == source && item.SessionId == sessionId && !Turns.Any(row => row.Turn.Id == item.TurnId));
        preserveDraftDuringSelectionClear = false;
        ContextItems.Clear();
        foreach (var item in snapshot.ContextItems.OrderBy(item => item.Order)) ContextItems.Add(new ContextPackItem(item.Type, item.Title, item.Source ?? string.Empty, item.Content, item.ReferencePath, item.Order, item.Key));
        HandoffTarget = snapshot.Handoff.Target; HandoffTemplate = snapshot.Handoff.Template; HandoffTitle = snapshot.Handoff.Title; HandoffTask = snapshot.Handoff.Task; HandoffCurrentState = snapshot.Handoff.CurrentState; HandoffConstraints = snapshot.Handoff.Constraints; HandoffNextAction = snapshot.Handoff.NextAction; IncludeHandoffContext = snapshot.Handoff.IncludeContextPack; IncludeHandoffWorkspace = snapshot.Handoff.IncludeWorkspace; IncludeHandoffGit = snapshot.Handoff.IncludeGitState; IncludeHandoffProjectFiles = snapshot.Handoff.IncludeProjectFileSummary;
        UpdateDraft(); NotifyContext();
        return missing;
    }

    public async Task RefreshGitStatusAsync()
    {
        GitChangedFiles.Clear();
        if (!EnableGitIntegration || SelectedWorkspace is null)
        {
            GitBranch = EnableGitIntegration ? "Git not found" : "Disabled";
            NotifyGitCounts();
            return;
        }
        var status = await new GitService().GetStatusAsync(SelectedWorkspace.Path);
        GitBranch = status.GitAvailable ? status.Branch : "Git not found";
        foreach (var file in status.Files) GitChangedFiles.Add(file);
        NotifyGitCounts();
    }

    public async Task SelectGitFileAsync(GitChangedFile file)
    {
        if (SelectedWorkspace is null) return;
        SelectProjectFile(Path.Combine(SelectedWorkspace.Path, file.Path));
        DiffPreviewTitle = $"差异：{file.Path}";
        try { DiffPreviewText = await new GitService().GetDiffAsync(SelectedWorkspace.Path, file.Path); }
        catch (Exception exception) { DiffPreviewText = $"无法读取差异：{exception.Message}"; }
    }

    private void NotifyGitCounts()
    {
        OnPropertyChanged(nameof(GitModifiedCount)); OnPropertyChanged(nameof(GitAddedCount)); OnPropertyChanged(nameof(GitDeletedCount));
    }

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full);
        return string.Equals(full, root, StringComparison.OrdinalIgnoreCase)
            ? full
            : full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private async Task<bool> TryAutoSaveBeforeWorkspaceSwitchAsync()
    {
        if (!AutoSaveBeforeWorkspaceSwitch || SelectedWorkspace is null) return true;
        SnapshotOperationErrorMessage = string.Empty;
        var workspacePath = SelectedWorkspace.Path;
        try
        {
            uiLog.Write("workspace auto snapshot current fingerprint start");
            var current = await BuildCurrentSnapshotAsync($"自动保存 · 切换工作区 · {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            uiLog.Write("workspace auto snapshot current fingerprint complete");
            var latest = snapshotService.List(workspacePath)
                .Where(entry => entry.Snapshot is not null && WorkspaceSnapshotService.IsWorkspaceMatch(entry.Snapshot, workspacePath))
                .Select(entry => entry.Snapshot!)
                .OrderByDescending(snapshot => snapshot.CreatedAt)
                .ThenByDescending(snapshot => snapshot.UpdatedAt)
                .FirstOrDefault();
            uiLog.Write("workspace auto snapshot latest comparison complete");
            if (latest is not null && WorkspaceStateFingerprint.Compute(latest) == WorkspaceStateFingerprint.Compute(current))
            {
                StatusText = UiStrings.AutoSnapshotUnchanged;
                return true;
            }

            uiLog.Write("workspace auto snapshot save start");
            snapshotService.Save(workspacePath, current);
            uiLog.Write("workspace auto snapshot save complete");
            snapshotAutoIndexService.MarkAuto(workspacePath, current.SnapshotId);
            uiLog.Write("workspace auto snapshot index mark complete");
            var cleanupSucceeded = RetainAutoSnapshots(workspacePath);
            uiLog.Write("workspace auto snapshot retention complete");
            RefreshSnapshots();
            StatusText = cleanupSucceeded ? UiStrings.AutoSnapshotSaved : UiStrings.AutoSnapshotCleanupFailed;
            return true;
        }
        catch (Exception exception)
        {
            SnapshotOperationErrorMessage = exception.Message;
            StatusText = UiStrings.AutoSnapshotSwitchFailed;
            uiLog.Write($"workspace auto snapshot failed; message={exception.Message}");
            return false;
        }
    }

    private bool RetainAutoSnapshots(string workspacePath)
    {
        var index = snapshotAutoIndexService.Read(workspacePath);
        if (index.IsCorrupt)
        {
            uiLog.Write("snapshot auto index corrupt; retention paused");
            return true;
        }

        snapshotAutoIndexService.PruneMissingEntries(workspacePath);
        index = snapshotAutoIndexService.Read(workspacePath);
        var autoEntries = snapshotService.List(workspacePath)
            .Where(entry => entry.Snapshot is not null
                && index.SnapshotIds.Contains(entry.Snapshot.SnapshotId)
                && string.Equals(Path.GetFileName(entry.DirectoryPath), entry.Snapshot.SnapshotId, StringComparison.Ordinal))
            .OrderByDescending(entry => entry.Snapshot!.CreatedAt)
            .ToArray();
        var cleanupSucceeded = true;
        foreach (var entry in autoEntries.Skip(AutoSnapshotRetentionCount))
        {
            try
            {
                snapshotService.DeleteDirectory(workspacePath, entry.DirectoryPath);
                snapshotAutoIndexService.Remove(workspacePath, entry.Snapshot!.SnapshotId);
            }
            catch (Exception exception)
            {
                cleanupSucceeded = false;
                uiLog.Write($"snapshot auto retention delete failed; id={entry.Snapshot!.SnapshotId}; message={exception.Message}");
            }
        }

        return cleanupSucceeded;
    }

    public async Task<bool> TrySwitchWorkspaceAsync(WorkspaceItem target, bool requireConfirmation = true)
    {
        if (workspaceSwitchInProgress)
        {
            uiLog.Write($"workspace switch ignored; reason=in-progress; target={target.Path}");
            OnPropertyChanged(nameof(WorkspaceSelection));
            return false;
        }

        workspaceSwitchInProgress = true;
        OnPropertyChanged(nameof(IsWorkspaceSwitchInProgress));
        var sourcePath = SelectedWorkspace?.Path ?? "(none)";
        uiLog.Write($"workspace switch requested; source={sourcePath}; target={target.Path}");
        try
        {
            if (SelectedWorkspace is not null
                && string.Equals(NormalizePath(SelectedWorkspace.Path), NormalizePath(target.Path), StringComparison.OrdinalIgnoreCase))
            {
                OnPropertyChanged(nameof(WorkspaceSelection));
                return true;
            }

            if (requireConfirmation && (ContextItems.Count > 0 || HandoffPreviewText.Length > 0) && confirmWorkspaceSwitch() != MessageBoxResult.OK)
            {
                uiLog.Write("workspace switch rolled back; reason=cancelled");
                OnPropertyChanged(nameof(WorkspaceSelection));
                return false;
            }

            var fullPath = NormalizePath(target.Path);
            var exists = Directory.Exists(fullPath);
            uiLog.Write($"workspace switch preflight complete; exists={exists}; target={fullPath}");
            if (!exists)
            {
                StatusText = "工作区初始化失败。";
                OnPropertyChanged(nameof(WorkspaceSelection));
                return false;
            }

            if (!await TryAutoSaveBeforeWorkspaceSwitchAsync())
            {
                OnPropertyChanged(nameof(WorkspaceSelection));
                return false;
            }

            uiLog.Write($"workspace switch commit start; target={fullPath}");
            return CommitWorkspaceSwitch(target);
        }
        finally
        {
            workspaceSwitchInProgress = false;
            OnPropertyChanged(nameof(IsWorkspaceSwitchInProgress));
        }
    }

    private bool CommitWorkspaceSwitch(WorkspaceItem target)
    {
        try
        {
            var fullPath = NormalizePath(target.Path);
            if (!Directory.Exists(fullPath)) throw new DirectoryNotFoundException($"Workspace directory does not exist: {fullPath}");
            CreateWorkspaceDirectory(Path.Combine(fullPath, ".ai"), ".ai root");
            CreateWorkspaceDirectory(Path.Combine(fullPath, ".ai", "conversation"), "conversation");
            CreateWorkspaceDirectory(Path.Combine(fullPath, ".ai", "notes"), "notes");
            CreateWorkspaceDirectory(Path.Combine(fullPath, ".ai", "exports"), "exports");

            var updated = new WorkspaceItem(target.Name, fullPath, DateTimeOffset.Now);
            var projectRoot = BuildTree(new DirectoryInfo(fullPath));
            var projectFileCount = new ProjectContextService().Build(updated, IgnoreFolders).Files.Count;
            var notes = ReadNotes(updated);
            var index = Workspaces.IndexOf(target);
            var persisted = Workspaces.Select((item, itemIndex) => itemIndex == index ? updated : item).ToArray();
            WorkspaceStore.Save(persisted);

            if (ContextItems.Count > 0) ClearContext();
            ClearHandoff();
            if (index >= 0) Workspaces[index] = updated;
            SelectedWorkspace = updated;
            ProjectRootItems.Clear();
            ProjectRootItems.Add(projectRoot);
            ProjectFileCount = projectFileCount;
            OnPropertyChanged(nameof(ProjectFileCount));
            Notes.Clear();
            foreach (var note in notes) Notes.Add(note);
            CodexThreadsView.Refresh();
            _ = RefreshGitStatusAsync();
            StatusText = UiStrings.Ready;
            uiLog.Write($"workspace switch commit complete; target={fullPath}");
            return true;
        }
        catch (Exception exception)
        {
            uiLog.Write($"workspace switch rolled back; target={target.Path}; exception={exception.GetType().FullName}; message={exception.Message}");
            StatusText = "工作区初始化失败。";
            OnPropertyChanged(nameof(WorkspaceSelection));
            return false;
        }
    }

    private void CreateWorkspaceDirectory(string path, string label)
    {
        try
        {
            Directory.CreateDirectory(path);
            uiLog.Write($"workspace {label} create success; path={path}");
        }
        catch (Exception exception)
        {
            uiLog.Write($"workspace {label} create failure; path={path}; exception={exception.GetType().FullName}; message={exception.Message}");
            throw;
        }
    }

    public async Task<bool> AddWorkspaceFromPathAsync(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                StatusText = "无法添加工作区。";
                return false;
            }

            var full = NormalizePath(path);
            if (!Directory.Exists(full))
            {
                StatusText = "无法添加工作区。";
                return false;
            }

            if (Workspaces.Any(item => string.Equals(NormalizePath(item.Path), full, StringComparison.OrdinalIgnoreCase)))
            {
                StatusText = "工作区已存在。";
                return false;
            }

            var item = new WorkspaceItem(new DirectoryInfo(full).Name, full, DateTimeOffset.Now);
            Workspaces.Add(item);
            WorkspaceStore.Save(Workspaces);
            NewWorkspacePath = string.Empty;
            return await TrySwitchWorkspaceAsync(item);
        }
        catch (Exception exception)
        {
            codexLog.Write($"workspace add exception; type={exception.GetType().FullName}; message={exception.Message}");
            StatusText = "无法添加工作区。";
            return false;
        }
    }

    private async Task AddWorkspaceAsync() => await AddWorkspaceFromPathAsync(NewWorkspacePath);

    private string NotesDirectory => SelectedWorkspace is null ? string.Empty : Path.Combine(SelectedWorkspace.Path, ".ai", "notes");

    private void LoadNotes()
    {
        Notes.Clear();
        if (SelectedWorkspace is null) return;
        foreach (var note in ReadNotes(SelectedWorkspace)) Notes.Add(note);
    }

    private static IReadOnlyList<NoteItem> ReadNotes(WorkspaceItem workspace)
    {
        var directory = Path.Combine(workspace.Path, ".ai", "notes");
        return Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*.md").OrderByDescending(File.GetLastWriteTimeUtc).Select(file => new NoteItem(Path.GetFileName(file), file)).ToArray()
            : [];
    }

    private void NewNote()
    {
        if (SelectedWorkspace is null) return;
        var name = string.IsNullOrWhiteSpace(NewNoteName) ? $"note-{DateTime.Now:yyyyMMdd-HHmmss}.md" : NewNoteName.Trim();
        if (!name.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) name += ".md";
        name = Path.GetFileName(name); Directory.CreateDirectory(NotesDirectory);
        var path = Path.Combine(NotesDirectory, name); if (!File.Exists(path)) File.WriteAllText(path, "# Note\n\n");
        NewNoteName = string.Empty; LoadNotes();
    }

    private void OpenNote()
    {
        if (SelectedNote is null) return;
        Process.Start(new ProcessStartInfo(SelectedNote.Path) { UseShellExecute = true });
    }

    private void DeleteNote()
    {
        if (SelectedNote is null || string.IsNullOrEmpty(NotesDirectory)) return;
        var full = Path.GetFullPath(SelectedNote.Path);
        if (full.StartsWith(Path.GetFullPath(NotesDirectory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) File.Delete(full);
        LoadNotes();
    }

    private void UseCurrentSessionForHandoff()
    {
        if (Turns.All(row => !row.IsSelected))
        {
            StatusText = "请先选择会话轮次。";
            return;
        }

        GenerateHandoff();
    }

    private void GenerateHandoff()
    {
        HandoffTitle = string.Empty;
        HandoffTask = string.Empty;
        HandoffCurrentState = string.Empty;
        HandoffNextAction = string.Empty;

        HandoffTitle = CurrentSessionTitle == "No session selected" ? string.Empty : CurrentSessionTitle;
        var selected = Turns.Where(row => row.IsSelected).ToArray();
        var generatedTask = selected.Reverse()
            .Select(row => HandoffService.NormalizeUserTask(row.Turn.User.Text))
            .FirstOrDefault(task => !string.IsNullOrWhiteSpace(task)) ?? string.Empty;
        HandoffTask = generatedTask;
        HandoffCurrentState = string.Join(Environment.NewLine, new[]
        {
            $"已选择 {selected.Length} 个可见轮次",
            $"已包含 {ContextItems.Count} 个上下文项",
            SelectedWorkspace is null ? string.Empty : $"工作区：{SelectedWorkspace.Name}",
            GitBranch == "Git not found" ? string.Empty : $"分支：{GitBranch}",
            $"已修改文件：{GitChangedFiles.Count} 个",
        }.Where(line => !string.IsNullOrWhiteSpace(line)).Select(line => $"- {line}"));
        UpdateHandoffPreview();
            StatusText = UiStrings.HandoffGenerated;
    }

    private HandoffInput BuildHandoffInput() => new(
        HandoffTarget, HandoffTemplate, HandoffTitle, HandoffTask, HandoffCurrentState,
        HandoffConstraints, HandoffNextAction, SelectedWorkspace?.Name ?? string.Empty,
        SelectedWorkspace?.Path ?? string.Empty, GitBranch, GitChangedFiles.ToArray(),
        ContextItems.ToArray(), ProjectFileCount, IncludeHandoffContext, IncludeHandoffWorkspace,
        IncludeHandoffGit, IncludeHandoffProjectFiles);

    private void UpdateHandoffPreview()
    {
        HandoffPreviewText = HandoffService.Render(BuildHandoffInput());
        OnPropertyChanged(nameof(HandoffCharacters));
        OnPropertyChanged(nameof(HandoffCharacterSummary));
        OnPropertyChanged(nameof(HandoffSizeState));
    }

    private async Task CopyHandoffAsync()
    {
        if (string.IsNullOrWhiteSpace(HandoffPreviewText)) return;
        var sequence = Interlocked.Increment(ref handoffCopySequence);
        var result = await clipboardService.CopyTextAsync(HandoffPreviewText);
        if (sequence != Volatile.Read(ref handoffCopySequence)) return;

        if (result.Succeeded)
        {
            StatusText = UiStrings.HandoffCopied;
            codexLog.Write($"handoff copied; target={HandoffTarget}; template={HandoffTemplate}; characters={HandoffCharacters}; item count={ContextItems.Count}");
            return;
        }

        StatusText = UiStrings.CopyFailed(result.Error?.Message ?? "Clipboard unavailable.");
    }

    private void ExportHandoff()
    {
        try
        {
            var directory = SelectedWorkspace is null
                ? Path.Combine(CodexBridgeWindowsPaths.SupportDirectory, "Exports", "handoff")
                : Path.Combine(SelectedWorkspace.Path, ".ai", "exports", "handoff");
            Directory.CreateDirectory(directory);
            var suffix = HandoffTarget.Equals("ChatGPT", StringComparison.OrdinalIgnoreCase) ? "chatgpt" : "codex";
            var baseName = $"{DateTime.Now:yyyy-MM-dd-HHmm}-handoff-{suffix}";
            var path = Path.Combine(directory, baseName + ".md");
            var index = 2;
            while (File.Exists(path)) path = Path.Combine(directory, $"{baseName}-{index++}.md");
            File.WriteAllText(path, HandoffPreviewText);
            StatusText = UiStrings.Exported(path);
            codexLog.Write($"handoff exported; target={HandoffTarget}; template={HandoffTemplate}; characters={HandoffCharacters}; export path={path}");
        }
        catch (Exception exception) { StatusText = $"导出失败：{exception.Message}"; }
    }

    private void ClearHandoff()
    {
        HandoffTitle = string.Empty; HandoffTask = string.Empty; HandoffCurrentState = string.Empty; HandoffNextAction = string.Empty;
        HandoffPreviewText = string.Empty;
        OnPropertyChanged(nameof(HandoffCharacters)); OnPropertyChanged(nameof(HandoffCharacterSummary)); OnPropertyChanged(nameof(HandoffSizeState));
    }

    private void ExportDraft()
    {
        if (SelectedWorkspace is null || Turns.Count == 0) return;
        try
        {
            var title = CurrentSessionTitle == "No session" ? "conversation" : CurrentSessionTitle;
            var fileName = string.Concat(title.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character)).Trim();
            if (string.IsNullOrWhiteSpace(fileName)) fileName = "conversation";
            var directory = Path.Combine(SelectedWorkspace.Path, "docs", "chatgpt"); Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"{DateTime.Now:yyyy-MM-dd}-{fileName}.md");
            var lines = new List<string> { $"# {title}", $"\nWorkspace: {SelectedWorkspace.Name}", $"\nPath: {SelectedWorkspace.Path}", $"\nGit:\nBranch: {GitBranch}\nModified Files: {GitModifiedCount}", "\n---" };
            foreach (var row in Turns.Where(item => item.IsSelected))
            {
                lines.Add($"\n## User\n\n{row.Turn.User.Text}\n\n## Assistant\n\n{row.Turn.Assistant?.Text ?? ""}");
            }
            File.WriteAllText(path, string.Join(Environment.NewLine, lines));
            StatusText = UiStrings.Exported(path);
        }
        catch (Exception exception) { StatusText = $"导出失败：{exception.Message}"; }
    }

    private void ClearTurns()
    {
        ClearTurnRows();
        chatGptTurnSelection = null;
        codexSelectedTurnIds = null;
        codexSnapshot = null;
        UpdateDraft();
    }

    private void ClearTurnRows()
    {
        foreach (var turn in Turns)
        {
            turn.PropertyChanged -= OnTurnPropertyChanged;
        }

        Turns.Clear();
        OnPropertyChanged(nameof(SelectedTurnSummary));
    }

    private void RecordCodexError(Exception exception)
    {
        CodexErrorDetail = FormatException(exception);
        codexLog.Write($"exception; selected executable={codexClient.ExecutablePath ?? "not selected"}; {CodexErrorDetail}");
    }

    private static string FormatException(Exception exception)
    {
        var detail = $"{exception.GetType().FullName}: {exception.Message}";
        return exception.InnerException is null
            ? detail
            : $"{detail} | Inner: {exception.InnerException.Message}";
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}

public sealed class ConversationListItemViewModel
{
    public ConversationListItemViewModel(StoredConversation conversation)
    {
        Conversation = conversation;
    }

    public StoredConversation Conversation { get; }

    public string Title => Conversation.Title;

    public string Preview => Conversation.Payload.Turns.FirstOrDefault()?.User.Text ?? string.Empty;

    public DateTimeOffset UpdatedAt => Conversation.UpdatedAt;

    public string Details => $"ChatGPT · {Conversation.CapturedAt:yyyy-MM-dd HH:mm} · {Conversation.TurnCount} 轮";
}

public sealed class CodexThreadListItemViewModel
{
    public CodexThreadListItemViewModel(CodexThreadSummary thread)
    {
        Thread = thread;
    }

    public CodexThreadSummary Thread { get; }

    public string Title => Thread.Title;

    public string Preview => Thread.Preview;

    public DateTimeOffset UpdatedAt => Thread.UpdatedAt ?? Thread.RecencyAt ?? Thread.CreatedAt ?? DateTimeOffset.MinValue;

    public string Details
    {
        get
        {
            var timestamp = Thread.UpdatedAt ?? Thread.RecencyAt ?? Thread.CreatedAt;
            var workspace = string.IsNullOrWhiteSpace(Thread.Cwd) ? "未知工作区" : Thread.Cwd;
            return timestamp is null
                ? workspace
                : $"{timestamp:yyyy-MM-dd HH:mm} · {workspace}";
        }
    }
}

public sealed record NoteItem(string Name, string Path);

public sealed class TurnRowViewModel : INotifyPropertyChanged
{
    private bool isSelected;

    public TurnRowViewModel(
        CapturedTurn turn,
        bool isSelected,
        string userLabel = "User",
        string assistantLabel = "Assistant",
        DateTimeOffset? timestamp = null)
    {
        Turn = turn;
        this.isSelected = isSelected;
        UserLabel = userLabel;
        AssistantLabel = assistantLabel;
        Timestamp = timestamp;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public CapturedTurn Turn { get; }

    public string Heading => $"第 {Turn.Index + 1} 轮";

    public string UserLabel { get; }

    public string AssistantLabel { get; }

    public DateTimeOffset? Timestamp { get; }

    public string TimestampText => Timestamp?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? string.Empty;

    public bool IsSelected
    {
        get => isSelected;
        set
        {
            if (isSelected == value)
            {
                return;
            }

            isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }
}
