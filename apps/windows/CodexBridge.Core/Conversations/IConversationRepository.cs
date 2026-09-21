using CodexBridge.Core.Capture;

namespace CodexBridge.Core.Conversations;

public interface IConversationRepository
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(
        CaptureValidationResult capture,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StoredConversation>> ListAsync(
        CancellationToken cancellationToken = default);

    Task<StoredConversation?> GetAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}
