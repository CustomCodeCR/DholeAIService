namespace Dhole.AI.Contracts.Executions.Response;

public sealed record AiEmbeddingsResultDto(
    Guid ExecutionId,
    IReadOnlyCollection<IReadOnlyCollection<float>> Embeddings,
    int Dimensions,
    Guid ConnectionId,
    string ConnectionName,
    Guid ModelId,
    string ModelName,
    string ExternalModelId,
    string ProviderType,
    int InputTokens,
    decimal EstimatedCost,
    long DurationMilliseconds
);
