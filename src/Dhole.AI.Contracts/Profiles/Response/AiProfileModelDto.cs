namespace Dhole.AI.Contracts.Profiles.Response;

public sealed record AiProfileModelDto(
    Guid Id,
    Guid ModelId,
    string ModelName,
    string ExternalModelId,
    Guid ConnectionId,
    string ConnectionName,
    string ProviderType,
    IReadOnlyCollection<string> Capabilities,
    int Priority,
    bool IsFallback,
    bool IsModelActive
);
