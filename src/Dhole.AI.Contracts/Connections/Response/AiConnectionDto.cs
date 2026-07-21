namespace Dhole.AI.Contracts.Connections.Response;

public sealed record AiConnectionDto(
    Guid Id,
    string Name,
    string ProviderType,
    string BaseUrl,
    string? SecretReference,
    int TimeoutSeconds,
    string Status,
    DateTime? LastHealthCheckAtUtc,
    string? LastHealthError,
    bool IsActive
);
