using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;

namespace Dhole.AI.Application.Features.PromptTemplates.Update;

public sealed record UpdatePromptTemplateCommand(
    Guid Id,
    string Key,
    string Name,
    string? Description,
    string? SystemPrompt,
    string? UserPromptTemplate,
    IReadOnlyCollection<string> Variables,
    Guid? UpdatedBy
) : ICommand<Result>;
