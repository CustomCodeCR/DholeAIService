namespace Dhole.AI.Contracts.Executions.Response;

public sealed record AiExecutionSummaryDto(
    Guid Id,
    string ProfileKey,
    string ProfileName,
    string ExecutionType,
    string Status,
    string? ProviderType,
    string? ModelName,
    AiTokenUsageDto TokenUsage,
    decimal EstimatedCost,
    long DurationMilliseconds,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc,
    string? ErrorCode
);
