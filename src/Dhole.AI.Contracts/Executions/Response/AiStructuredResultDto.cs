namespace Dhole.AI.Contracts.Executions.Response;

public sealed record AiStructuredResultDto(
    Guid ExecutionId,
    string JsonContent,
    Guid ConnectionId,
    string ConnectionName,
    Guid ModelId,
    string ModelName,
    string ExternalModelId,
    string ProviderType,
    AiTokenUsageDto TokenUsage,
    decimal EstimatedCost,
    long DurationMilliseconds,
    string FinishReason
);
