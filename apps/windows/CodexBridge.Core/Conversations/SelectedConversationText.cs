using CodexBridge.Core.Capture;

namespace CodexBridge.Core.Conversations;

public static class SelectedConversationText
{
    public static string Render(CapturePayload conversation, IEnumerable<string> selectedTurnIds)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        ArgumentNullException.ThrowIfNull(selectedTurnIds);

        var selected = selectedTurnIds.ToHashSet(StringComparer.Ordinal);
        return string.Join(
            "\n\n---\n\n",
            conversation.Turns
                .Where(turn => selected.Contains(turn.Id))
                .Select(RenderTurn));
    }

    private static string RenderTurn(CapturedTurn turn)
    {
        return $"第 {turn.Index + 1} 轮\n\n用户：\n{turn.User.Text}\n\nChatGPT：\n{turn.Assistant?.Text ?? "（尚无回复）"}";
    }
}
