namespace Dhole.AI.Contracts.Models.Response;

public sealed record AiModelDto(
    Guid Id,
    Guid ConnectionId,
    string ConnectionName,
    string ProviderType,
    string ExternalModelId,
    string Name,
    IReadOnlyCollection<string> Capabilities,
    int? ContextWindow,
    int? MaximumOutputTokens,
    decimal? InputCostPerMillionTokens,
    decimal? OutputCostPerMillionTokens,
    bool IsLocal,
    string Status,
    DateTime? LastAvailabilityCheckAtUtc,
    string? LastAvailabilityError,
    bool IsActive
);
