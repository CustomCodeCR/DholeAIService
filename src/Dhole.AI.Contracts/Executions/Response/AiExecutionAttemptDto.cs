namespace Dhole.AI.Contracts.Executions.Response;

public sealed record AiExecutionAttemptDto(
    Guid Id,
    int AttemptNumber,
    Guid ConnectionId,
    string ConnectionName,
    Guid ModelId,
    string ModelName,
    string ProviderType,
    string ExternalModelId,
    string Status,
    DateTime StartedAtUtc,
    DateTime? CompletedAtUtc,
    AiTokenUsageDto TokenUsage,
    decimal EstimatedCost,
    long DurationMilliseconds,
    string FinishReason,
    string? ErrorCode,
    string? ErrorMessage
);
