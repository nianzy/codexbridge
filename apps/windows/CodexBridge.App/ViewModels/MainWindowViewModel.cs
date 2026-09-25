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
    private ConversationListItemViewModel? selectedConversation;
    private CodexThreadListItemViewModel? selectedCodexThread;
    private TurnSelection? chatGptTurnSelection;
    private HashSet<string>? codexSelectedTurnIds;
    private CodexThreadSnapshot? codexSnapshot;
    private CancellationTokenSource? codexThreadLoad;
    private int codexLoadSequence;
    private int sourceIndex;
    private string draftText = string.Empty;
    private string statusText = "Ready";
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
    private string projectFilePreviewTitle = "File Preview";
    private string projectFilePreviewText = "Select a supported file from Project Explorer.";
    private string newNoteName = string.Empty;
    private string gitBranch = "Git not found";
    private string diffPreviewTitle = "Diff Preview";
    private string diffPreviewText = "Select a changed file.";
    private bool enableGitIntegration = true;
    private readonly ContextPackService contextPackService = new();
    private string contextPreviewText = string.Empty;

    public MainWindowViewModel(
        IConversationRepository repository,
        CaptureInboxImporter importer,
        ICodexClient codexClient,
        ICodexAppLog? codexLog = null,
        Func<ICodexClient>? codexClientFactory = null,
        ICodexAppLog? uiLog = null,
        Func<MessageBoxResult>? confirmWorkspaceSwitch = null)
    {
        this.repository = repository;
        this.importer = importer;
        this.codexClient = codexClient;
        this.codexLog = codexLog ?? NullCodexAppLog.Instance;
        this.uiLog = uiLog ?? NullCodexAppLog.Instance;
        this.confirmWorkspaceSwitch = confirmWorkspaceSwitch ?? (() => MessageBox.Show(
            "Current context contains selected items.\n\nClear and switch workspace?",
            "Workspace",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning));
        this.codexClientFactory = codexClientFactory;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        RefreshActiveCommand = new AsyncRelayCommand(RefreshActiveAsync);
        RefreshCodexCommand = new AsyncRelayCommand(RefreshCodexAsync);
        SelectAllCommand = new RelayCommand(SelectAll);
        ClearSelectionCommand = new RelayCommand(ClearSelection);
        CopyDraftCommand = new RelayCommand(CopyDraft);
        OpenChatGptCommand = new RelayCommand(OpenChatGpt);
        ExportDraftCommand = new RelayCommand(ExportDraft);
        AddWorkspaceCommand = new RelayCommand(AddWorkspace);
        NewNoteCommand = new RelayCommand(NewNote);
        OpenNoteCommand = new RelayCommand(OpenNote);
        DeleteNoteCommand = new RelayCommand(DeleteNote);
        CopyContextCommand = new RelayCommand(CopyContext);
        ExportContextCommand = new RelayCommand(ExportContext);
        ClearContextCommand = new RelayCommand(ClearContext);
        PreviewContextCommand = new RelayCommand(PreviewContext);
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
        if (Workspaces.FirstOrDefault() is { } initialWorkspace) TrySwitchWorkspace(initialWorkspace, requireConfirmation: false);
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
        var state = value.StartsWith("Loading", StringComparison.Ordinal) ? "Loading"
            : value.Equals("Ready", StringComparison.Ordinal) ? "Ready"
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
        private set => SetProperty(ref codexStatusText, value);
    }

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
    public string CurrentSessionTitle => SourceIndex == 1 ? SelectedCodexThread?.Title ?? "No session selected" : SelectedConversation?.Title ?? "No session selected";
    public string ChatGptEmptyText => WorkspaceOnly ? "No sessions associated with this workspace" : "No sessions";
    public string CodexEmptyText => WorkspaceOnly ? "No sessions associated with this workspace" : "No sessions";
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

    public WorkspaceItem? WorkspaceSelection
    {
        get => SelectedWorkspace;
        set
        {
            if (value is not null && !Equals(value, SelectedWorkspace)) TrySwitchWorkspace(value);
            OnPropertyChanged();
        }
    }

    public string NewWorkspacePath { get => newWorkspacePath; set => SetProperty(ref newWorkspacePath, value); }
    public string ProjectFilePreviewTitle { get => projectFilePreviewTitle; private set => SetProperty(ref projectFilePreviewTitle, value); }
    public string ProjectFilePreviewText { get => projectFilePreviewText; private set => SetProperty(ref projectFilePreviewText, value); }
    public int ProjectFileCount { get; private set; }
    public string NewNoteName { get => newNoteName; set => SetProperty(ref newNoteName, value); }
    public NoteItem? SelectedNote { get; set; }
    public string GitBranch { get => gitBranch; private set => SetProperty(ref gitBranch, value); }
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
    public string ContextSizeState => ContextPackService.ClassifySize(ContextCharacters) is "Large" ? "Large context" : ContextPackService.ClassifySize(ContextCharacters);
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
            SetUiStatus("Ready", "startup");
        }
        catch (Exception exception)
        {
            SetUiStatus($"ChatGPT 初始化失败：{exception.Message}", "startup");
        }

        _ = RefreshCodexAsync();
    }

    public async Task RefreshAsync()
    {
        SetUiStatus("Loading…", "chatgpt");
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
                SetUiStatus("Ready", "chatgpt");
            }
        }
        catch (Exception exception)
        {
            SetUiStatus($"ChatGPT 刷新失败：{exception.Message}", "chatgpt");
        }
        finally
        {
            if (StatusText.StartsWith("Loading", StringComparison.Ordinal))
            {
                SetUiStatus("Ready", "chatgpt");
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
        SetUiStatus("Loading…", "codex");
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
            CodexStatusText = $"Connected · {probe.Account.DisplayLabel} · {probe.Models.Count} models";

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
                SetUiStatus("Ready", "codex");
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
                SetUiStatus("未找到 Codex executable", "codex");
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
            if (StatusText.StartsWith("Loading", StringComparison.Ordinal))
            {
                SetUiStatus("Ready", "codex");
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
                    "User",
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
            AddTurnRow(new TurnRowViewModel(captured, true, "User", "Codex",
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

        UpdateDraft();
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

        foreach (var row in Turns)
        {
            row.IsSelected = false;
        }

        UpdateDraft();
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

    private void CopyDraft()
    {
        if (string.IsNullOrEmpty(DraftText))
        {
            StatusText = "没有可复制的草稿";
            return;
        }

        try
        {
            Clipboard.SetText(DraftText);
            StatusText = "草稿已复制到剪贴板";
        }
        catch (Exception exception)
        {
            StatusText = $"复制失败：{exception.Message}";
        }
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
        try
        {
            var info = new FileInfo(path);
            ProjectFilePreviewTitle = info.Name;
            if (info.Length > 512 * 1024) { ProjectFilePreviewText = "File too large"; return; }
            var allowed = new[] { ".txt", ".md", ".json", ".cs", ".cpp", ".h" };
            ProjectFilePreviewText = allowed.Contains(info.Extension, StringComparer.OrdinalIgnoreCase) ? File.ReadAllText(path) : "Unsupported file type";
        }
        catch (Exception exception) { ProjectFilePreviewText = $"Unable to read file: {exception.Message}"; }
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
        var info = new FileInfo(path); if (info.Length > 512 * 1024) { StatusText = "File too large for context"; return; }
        var allowed = new[] { ".txt", ".md", ".json", ".cs", ".cpp", ".h" }; if (!allowed.Contains(info.Extension, StringComparer.OrdinalIgnoreCase)) return;
        var content = File.ReadAllText(path); var relative = Path.GetRelativePath(SelectedWorkspace.Path, path); var key = ContextPackService.CreateKey("ProjectFile", relative, $"{relative}|{info.LastWriteTimeUtc:O}", content);
        AddContextItem(new ContextPackItem("ProjectFile", $"File · {relative}", "Workspace", content, relative, ContextItems.Count, key));
    }

    public async Task AddGitDiffToContextAsync(GitChangedFile file)
    {
        if (SelectedWorkspace is null) return;
        var content = await new GitService().GetDiffAsync(SelectedWorkspace.Path, file.Path); if (content == "Diff too large") { StatusText = "Diff too large for context"; return; }
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
    private void NotifyContext() { OnPropertyChanged(nameof(ContextItemCount)); OnPropertyChanged(nameof(ContextCharacters)); OnPropertyChanged(nameof(ContextSizeState)); OnPropertyChanged(nameof(ContextPreviewText)); }
    private void CopyContext()
    {
        if (SelectedWorkspace is null || ContextItems.Count == 0) return;
        try { Clipboard.SetText(RenderContext()); codexLog.Write($"context copied; count={ContextItems.Count}"); } catch { }
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
        var window = new Window { Title = "Project Notes", Width = 640, Height = 520, Owner = owner, Content = new NotesView { DataContext = this }, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        window.ShowDialog();
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
        DiffPreviewTitle = $"Diff: {file.Path}";
        try { DiffPreviewText = await new GitService().GetDiffAsync(SelectedWorkspace.Path, file.Path); }
        catch (Exception exception) { DiffPreviewText = $"Unable to read diff: {exception.Message}"; }
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

    public bool TrySwitchWorkspace(WorkspaceItem target, bool requireConfirmation = true)
    {
        var sourcePath = SelectedWorkspace?.Path ?? "(none)";
        uiLog.Write($"workspace switch requested; source={sourcePath}; target={target.Path}");
        if (SelectedWorkspace is not null
            && string.Equals(NormalizePath(SelectedWorkspace.Path), NormalizePath(target.Path), StringComparison.OrdinalIgnoreCase))
        {
            OnPropertyChanged(nameof(WorkspaceSelection));
            return true;
        }

        if (requireConfirmation && ContextItems.Count > 0 && confirmWorkspaceSwitch() != MessageBoxResult.OK)
        {
            uiLog.Write("workspace switch rolled back; reason=cancelled");
            OnPropertyChanged(nameof(WorkspaceSelection));
            return false;
        }

        try
        {
            var fullPath = NormalizePath(target.Path);
            var exists = Directory.Exists(fullPath);
            uiLog.Write($"workspace target exists={exists}; target={fullPath}");
            if (!exists) throw new DirectoryNotFoundException($"Workspace directory does not exist: {fullPath}");

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
            StatusText = "Ready";
            uiLog.Write($"workspace switch committed; target={fullPath}");
            return true;
        }
        catch (Exception exception)
        {
            uiLog.Write($"workspace switch rolled back; target={target.Path}; exception={exception.GetType().FullName}; message={exception.Message}");
            StatusText = "Unable to initialize workspace.";
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

    public bool AddWorkspaceFromPath(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                StatusText = "Unable to add workspace.";
                return false;
            }

            var full = NormalizePath(path);
            if (!Directory.Exists(full))
            {
                StatusText = "Unable to add workspace.";
                return false;
            }

            if (Workspaces.Any(item => string.Equals(NormalizePath(item.Path), full, StringComparison.OrdinalIgnoreCase)))
            {
                StatusText = "Workspace already exists.";
                return false;
            }

            var item = new WorkspaceItem(new DirectoryInfo(full).Name, full, DateTimeOffset.Now);
            Workspaces.Add(item);
            WorkspaceStore.Save(Workspaces);
            NewWorkspacePath = string.Empty;
            TrySwitchWorkspace(item);
            return true;
        }
        catch (Exception exception)
        {
            codexLog.Write($"workspace add exception; type={exception.GetType().FullName}; message={exception.Message}");
            StatusText = "Unable to add workspace.";
            return false;
        }
    }

    private void AddWorkspace() => AddWorkspaceFromPath(NewWorkspacePath);

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
            StatusText = $"Exported: {path}";
        }
        catch (Exception exception) { StatusText = $"Export failed: {exception.Message}"; }
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
