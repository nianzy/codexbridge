using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using CodexBridge.App.Commands;
using CodexBridge.App.Infrastructure;
using CodexBridge.Core.Capture;
using CodexBridge.Core.Conversations;

namespace CodexBridge.App.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly IConversationRepository repository;
    private readonly CaptureInboxImporter importer;
    private ConversationListItemViewModel? selectedConversation;
    private TurnSelection? turnSelection;
    private string draftText = string.Empty;
    private string statusText = "Ready";
    private string nativeMessagingText = "Not installed";
    private bool initialized;

    public MainWindowViewModel(IConversationRepository repository, CaptureInboxImporter importer)
    {
        this.repository = repository;
        this.importer = importer;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        SelectAllCommand = new RelayCommand(SelectAll);
        ClearSelectionCommand = new RelayCommand(ClearSelection);
        CopyDraftCommand = new RelayCommand(CopyDraft);
        OpenChatGptCommand = new RelayCommand(OpenChatGpt);
        importer.Imported += OnImported;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ConversationListItemViewModel> Conversations { get; } = new();

    public ObservableCollection<TurnRowViewModel> Turns { get; } = new();

    public ICommand RefreshCommand { get; }

    public ICommand SelectAllCommand { get; }

    public ICommand ClearSelectionCommand { get; }

    public ICommand CopyDraftCommand { get; }

    public ICommand OpenChatGptCommand { get; }

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
            LoadTurns(value?.Conversation);
            OnPropertyChanged();
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
            StatusText = $"初始化失败：{exception.Message}";
        }
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
            StatusText = $"已加载 {Conversations.Count} 个 ChatGPT 会话";
        }
        catch (Exception exception)
        {
            StatusText = $"刷新失败：{exception.Message}";
        }
    }

    public async ValueTask DisposeAsync()
    {
        importer.Imported -= OnImported;
        await importer.DisposeAsync();
    }

    private void LoadTurns(StoredConversation? conversation)
    {
        foreach (var turn in Turns)
        {
            turn.PropertyChanged -= OnTurnPropertyChanged;
        }

        Turns.Clear();
        turnSelection = conversation is null ? null : new TurnSelection(conversation.Payload);
        if (conversation is not null && turnSelection is not null)
        {
            foreach (var turn in conversation.Payload.Turns)
            {
                var row = new TurnRowViewModel(turn, turnSelection.IsSelected(turn.Id));
                row.PropertyChanged += OnTurnPropertyChanged;
                Turns.Add(row);
            }
        }

        UpdateDraft();
    }

    private void OnTurnPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (sender is TurnRowViewModel row && args.PropertyName == nameof(TurnRowViewModel.IsSelected))
        {
            turnSelection?.SetSelected(row.Turn.Id, row.IsSelected);
            UpdateDraft();
        }
    }

    private void SelectAll()
    {
        turnSelection?.SelectAll();
        foreach (var row in Turns)
        {
            row.IsSelected = true;
        }

        UpdateDraft();
    }

    private void ClearSelection()
    {
        turnSelection?.Clear();
        foreach (var row in Turns)
        {
            row.IsSelected = false;
        }

        UpdateDraft();
    }

    private void UpdateDraft()
    {
        DraftText = selectedConversation is null || turnSelection is null
            ? string.Empty
            : SelectedConversationText.Render(
                selectedConversation.Conversation.Payload,
                turnSelection.SelectedTurnIds);
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

public sealed class TurnRowViewModel : INotifyPropertyChanged
{
    private bool isSelected;

    public TurnRowViewModel(CapturedTurn turn, bool isSelected)
    {
        Turn = turn;
        this.isSelected = isSelected;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public CapturedTurn Turn { get; }

    public string Heading => $"第 {Turn.Index + 1} 轮";

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
