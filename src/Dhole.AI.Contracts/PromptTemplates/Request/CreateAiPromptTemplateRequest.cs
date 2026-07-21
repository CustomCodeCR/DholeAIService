namespace Dhole.AI.Contracts.PromptTemplates.Request;

public sealed record CreateAiPromptTemplateRequest(
    string Key,
    string Name,
    string? Description,
    string? SystemPrompt,
    string? UserPromptTemplate,
    IReadOnlyCollection<string> Variables
);
