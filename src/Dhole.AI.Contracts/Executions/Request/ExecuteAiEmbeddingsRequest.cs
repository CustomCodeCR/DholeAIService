namespace Dhole.AI.Contracts.Executions.Request;

public sealed record ExecuteAiEmbeddingsRequest(
    string ProfileKey,
    IReadOnlyCollection<string> Inputs,
    string? CorrelationId = null,
    string? RequestHash = null
);
