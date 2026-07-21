namespace Dhole.AI.Contracts.PromptTemplates.Request;

public sealed record UpdateAiPromptTemplateRequest(
    string Key,
    string Name,
    string? Description,
    string? SystemPrompt,
    string? UserPromptTemplate,
    IReadOnlyCollection<string> Variables
);
