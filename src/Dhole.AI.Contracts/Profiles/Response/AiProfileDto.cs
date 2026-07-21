namespace Dhole.AI.Contracts.Profiles.Response;

public sealed record AiProfileDto(
    Guid Id,
    string Key,
    string Name,
    string? Description,
    Guid? PromptTemplateId,
    string? PromptTemplateName,
    string RoutingMode,
    string ResponseFormat,
    decimal Temperature,
    int MaximumOutputTokens,
    int TimeoutSeconds,
    string? JsonSchema,
    bool IsActive,
    IReadOnlyCollection<AiProfileModelDto> Models
);
