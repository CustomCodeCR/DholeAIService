namespace Dhole.AI.Contracts.PromptTemplates.Response;

public sealed record AiPromptTemplateDto(
    Guid Id,
    string Key,
    string Name,
    string? Description,
    string? SystemPrompt,
    string? UserPromptTemplate,
    IReadOnlyCollection<string> Variables,
    bool IsActive
);
