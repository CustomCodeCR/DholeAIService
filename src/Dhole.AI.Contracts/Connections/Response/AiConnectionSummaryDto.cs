namespace Dhole.AI.Contracts.Connections.Response;

public sealed record AiConnectionSummaryDto(
    Guid Id,
    string Name,
    string ProviderType,
    string BaseUrl,
    string Status,
    DateTime? LastHealthCheckAtUtc,
    bool IsActive
);
