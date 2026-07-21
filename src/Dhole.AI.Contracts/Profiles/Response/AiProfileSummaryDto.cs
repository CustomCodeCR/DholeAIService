namespace Dhole.AI.Contracts.Profiles.Response;

public sealed record AiProfileSummaryDto(
    Guid Id,
    string Key,
    string Name,
    string? Description,
    string RoutingMode,
    string ResponseFormat,
    int ModelCount,
    bool IsActive
);
