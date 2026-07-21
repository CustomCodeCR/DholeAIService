namespace Dhole.AI.Contracts.Models.Response;

public sealed record AiModelSummaryDto(
    Guid Id,
    Guid ConnectionId,
    string ConnectionName,
    string ProviderType,
    string ExternalModelId,
    string Name,
    IReadOnlyCollection<string> Capabilities,
    bool IsLocal,
    string Status,
    bool IsActive
);
