namespace Dhole.AI.Contracts.Executions.Response;

public sealed record AiStreamChunkDto(
    Guid ExecutionId,
    string Content,
    int Index,
    bool IsCompleted,
    string? FinishReason,
    AiTokenUsageDto? TokenUsage,
    string? ErrorCode,
    string? ErrorMessage
);
