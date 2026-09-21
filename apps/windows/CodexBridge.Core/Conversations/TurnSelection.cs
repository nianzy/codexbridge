using CodexBridge.Core.Capture;

namespace CodexBridge.Core.Conversations;

public sealed class TurnSelection
{
    private readonly CapturePayload payload;
    private readonly HashSet<string> selectedTurnIds;

    public TurnSelection(CapturePayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        this.payload = payload;
        var availableIds = payload.Turns.Select(turn => turn.Id).ToHashSet(StringComparer.Ordinal);
        selectedTurnIds = payload.Selection.SelectedTurnIds
            .Where(availableIds.Contains)
            .ToHashSet(StringComparer.Ordinal);
    }

    public IReadOnlySet<string> SelectedTurnIds => selectedTurnIds;

    public bool IsSelected(string turnId)
    {
        return selectedTurnIds.Contains(turnId);
    }

    public void SetSelected(string turnId, bool selected)
    {
        if (!payload.Turns.Any(turn => string.Equals(turn.Id, turnId, StringComparison.Ordinal)))
        {
            return;
        }

        if (selected)
        {
            selectedTurnIds.Add(turnId);
        }
        else
        {
            selectedTurnIds.Remove(turnId);
        }
    }

    public void SelectAll()
    {
        selectedTurnIds.Clear();
        foreach (var turn in payload.Turns)
        {
            selectedTurnIds.Add(turn.Id);
        }
    }

    public void Clear()
    {
        selectedTurnIds.Clear();
    }
}
