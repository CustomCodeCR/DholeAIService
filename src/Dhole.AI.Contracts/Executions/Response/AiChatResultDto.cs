namespace Dhole.AI.Contracts.Executions.Response;

public sealed record AiChatResultDto(
    Guid ExecutionId,
    string Content,
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
