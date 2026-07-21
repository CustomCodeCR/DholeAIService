namespace Dhole.AI.Contracts.Connections.Response;

public sealed record AiConnectionTestResultDto(
    Guid ConnectionId,
    bool Success,
    string Status,
    long DurationMilliseconds,
    DateTime CheckedAtUtc,
    string? ErrorCode,
    string? ErrorMessage
);
