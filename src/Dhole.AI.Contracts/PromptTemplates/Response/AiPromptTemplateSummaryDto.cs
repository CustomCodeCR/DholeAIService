namespace Dhole.AI.Contracts.PromptTemplates.Response;

public sealed record AiPromptTemplateSummaryDto(
    Guid Id,
    string Key,
    string Name,
    string? Description,
    int VariableCount,
    bool IsActive
);
