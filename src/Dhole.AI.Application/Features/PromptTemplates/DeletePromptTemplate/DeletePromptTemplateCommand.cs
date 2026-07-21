using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;

namespace Dhole.AI.Application.Features.PromptTemplates.Delete;

public sealed record DeletePromptTemplateCommand(Guid Id, Guid? DeletedBy) : ICommand<Result>;
