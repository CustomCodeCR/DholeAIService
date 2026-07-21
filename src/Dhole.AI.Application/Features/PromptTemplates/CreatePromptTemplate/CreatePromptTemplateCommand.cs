using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;

namespace Dhole.AI.Application.Features.PromptTemplates.Create;

public sealed record CreatePromptTemplateCommand(
    string Key,
    string Name,
    string? Description,
    string? SystemPrompt,
    string? UserPromptTemplate,
    IReadOnlyCollection<string> Variables,
    Guid? CreatedBy
) : ICommand<Result<Guid>>;
