namespace Dhole.AI.Application.Abstractions.Providers.Models;

public sealed record AiProviderHealthResult(
    bool Success,
    long DurationMilliseconds,
    DateTime CheckedAtUtc,
    string? ErrorCode = null,
    string? ErrorMessage = null
);
