using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using CodexBridge.App.Commands;
using CodexBridge.App.Infrastructure;
using CodexBridge.Core.Capture;
using CodexBridge.Core.Codex;
using CodexBridge.Core.Conversations;

namespace CodexBridge.App.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly IConversationRepository repository;
    private readonly CaptureInboxImporter importer;
    private ICodexClient codexClient;
    private readonly Func<ICodexClient>? codexClientFactory;
    private readonly ICodexAppLog codexLog;
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
    private bool initialized;

    public MainWindowViewModel(
        IConversationRepository repository,
        CaptureInboxImporter importer,
        ICodexClient codexClient,
        ICodexAppLog? codexLog = null,
        Func<ICodexClient>? codexClientFactory = null)
    {
        this.repository = repository;
        this.importer = importer;
        this.codexClient = codexClient;
        this.codexLog = codexLog ?? NullCodexAppLog.Instance;
        this.codexClientFactory = codexClientFactory;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        RefreshCodexCommand = new AsyncRelayCommand(RefreshCodexAsync);
        SelectAllCommand = new RelayCommand(SelectAll);
        ClearSelectionCommand = new RelayCommand(ClearSelection);
        CopyDraftCommand = new RelayCommand(CopyDraft);
        OpenChatGptCommand = new RelayCommand(OpenChatGpt);
        importer.Imported += OnImported;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ConversationListItemViewModel> Conversations { get; } = new();

    public ObservableCollection<CodexThreadListItemViewModel> CodexThreads { get; } = new();

    public ObservableCollection<TurnRowViewModel> Turns { get; } = new();

    public ICommand RefreshCommand { get; }

    public ICommand RefreshCodexCommand { get; }

    public ICommand SelectAllCommand { get; }

    public ICommand ClearSelectionCommand { get; }

    public ICommand CopyDraftCommand { get; }

    public ICommand OpenChatGptCommand { get; }

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
            StatusText = "Ready";
        }
        catch (Exception exception)
        {
            StatusText = $"ChatGPT 初始化失败：{exception.Message}";
        }

        _ = RefreshCodexAsync();
    }

    public async Task RefreshAsync()
    {
        try
        {
            var selectedId = selectedConversation?.Conversation.Id;
            var conversations = await repository.ListAsync();
            Conversations.Clear();
            foreach (var conversation in conversations)
            {
                Conversations.Add(new ConversationListItemViewModel(conversation));
            }

            SelectedConversation = Conversations.FirstOrDefault(item => item.Conversation.Id == selectedId)
                ?? Conversations.FirstOrDefault();
            if (SourceIndex == 0)
            {
                StatusText = $"已加载 {Conversations.Count} 个 ChatGPT 会话";
            }
        }
        catch (Exception exception)
        {
            StatusText = $"ChatGPT 刷新失败：{exception.Message}";
        }
    }

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
        CodexErrorDetail = string.Empty;
        codexLog.Write($"refresh start; selected executable={codexClient.ExecutablePath ?? "not selected"}");
        try
        {
            var selectedId = selectedCodexThread?.Thread.Id;
            var probe = await codexClient.ProbeAsync();
            CodexStatusText = $"Connected · {probe.Account.DisplayLabel} · {probe.Models.Count} models";

            var threads = await codexClient.ListThreadsAsync(100);
            CodexThreads.Clear();
            foreach (var thread in threads)
            {
                CodexThreads.Add(new CodexThreadListItemViewModel(thread));
            }

            SelectedCodexThread = CodexThreads.FirstOrDefault(item => item.Thread.Id == selectedId)
                ?? CodexThreads.FirstOrDefault();
            if (SourceIndex == 1)
            {
                StatusText = $"已加载 {CodexThreads.Count} 个 Codex 会话";
                if (SelectedCodexThread is null)
                {
                    ClearTurns();
                }
            }
        }
        catch (CodexExecutableNotFoundException exception)
        {
            CodexStatusText = "Not found";
            RecordCodexError(exception);
            CodexThreads.Clear();
            if (SourceIndex == 1)
            {
                StatusText = "未找到 Codex executable";
                ClearTurns();
            }
        }
        catch (CodexDisconnectedException exception)
        {
            CodexStatusText = "Disconnected";
            RecordCodexError(exception);
            if (SourceIndex == 1)
            {
                StatusText = $"Codex 已断开：{exception.Message}";
            }
        }
        catch (OperationCanceledException)
        {
            CodexStatusText = "Disconnected";
        }
        catch (Exception exception)
        {
            CodexStatusText = "Error";
            RecordCodexError(exception);
            if (SourceIndex == 1)
            {
                StatusText = $"Codex 错误：{exception.Message}";
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
                    "ChatGPT"));
            }
        }

        UpdateDraft();
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
            AddTurnRow(new TurnRowViewModel(captured, true, "User", "Codex"));
        }

        UpdateDraft();
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

public sealed class TurnRowViewModel : INotifyPropertyChanged
{
    private bool isSelected;

    public TurnRowViewModel(
        CapturedTurn turn,
        bool isSelected,
        string userLabel = "User",
        string assistantLabel = "Assistant")
    {
        Turn = turn;
        this.isSelected = isSelected;
        UserLabel = userLabel;
        AssistantLabel = assistantLabel;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public CapturedTurn Turn { get; }

    public string Heading => $"第 {Turn.Index + 1} 轮";

    public string UserLabel { get; }

    public string AssistantLabel { get; }

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
