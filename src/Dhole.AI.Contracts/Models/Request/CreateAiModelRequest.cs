namespace Dhole.AI.Contracts.Models.Request;

public sealed record CreateAiModelRequest(
    Guid ConnectionId,
    string ExternalModelId,
    string Name,
    IReadOnlyCollection<string> Capabilities,
    int? ContextWindow,
    int? MaximumOutputTokens,
    decimal? InputCostPerMillionTokens,
    decimal? OutputCostPerMillionTokens,
    bool IsLocal
);
