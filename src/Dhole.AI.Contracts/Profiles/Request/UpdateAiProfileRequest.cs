namespace Dhole.AI.Contracts.Profiles.Request;

public sealed record UpdateAiProfileRequest(
    string Key,
    string Name,
    string? Description,
    Guid? PromptTemplateId,
    string RoutingMode,
    string ResponseFormat,
    decimal Temperature,
    int MaximumOutputTokens,
    int TimeoutSeconds,
    string? JsonSchema
);
