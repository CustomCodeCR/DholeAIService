using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Queries;
using Dhole.AI.Contracts.PromptTemplates.Response;

namespace Dhole.AI.Application.Features.PromptTemplates.GetById;

public sealed record GetPromptTemplateByIdQuery(Guid Id) : IQuery<Result<AiPromptTemplateDto>>;
