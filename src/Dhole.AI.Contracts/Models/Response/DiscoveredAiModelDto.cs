namespace Dhole.AI.Contracts.Models.Response;

public sealed record DiscoveredAiModelDto(
    string ExternalModelId,
    string Name,
    IReadOnlyCollection<string> Capabilities,
    int? ContextWindow,
    int? MaximumOutputTokens,
    bool IsLocal,
    bool IsRegistered,
    Guid? RegisteredModelId
);
