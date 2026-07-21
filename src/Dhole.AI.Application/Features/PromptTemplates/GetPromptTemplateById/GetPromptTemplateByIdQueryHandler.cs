using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Queries;
using Dhole.AI.Application.Abstractions.Cache;
using Dhole.AI.Application.Abstractions.Repositories;
using Dhole.AI.Contracts.PromptTemplates.Response;
using Dhole.AI.Domain.Shared;

namespace Dhole.AI.Application.Features.PromptTemplates.GetById;

public sealed class GetPromptTemplateByIdQueryHandler(
    IAiPromptTemplateRepository templates,
    IAiPromptTemplateCacheService cache
) : IQueryHandler<GetPromptTemplateByIdQuery, Result<AiPromptTemplateDto>>
{
    public async Task<Result<AiPromptTemplateDto>> HandleAsync(
        GetPromptTemplateByIdQuery query,
        CancellationToken cancellationToken = default
    )
    {
        var cached = await cache.GetByIdAsync(query.Id, cancellationToken);

        if (cached is not null)
        {
            return Result.Success(cached);
        }

        var template = await templates.GetDtoByIdAsync(query.Id, cancellationToken);

        if (template is null)
        {
            return Result.Failure<AiPromptTemplateDto>(AiErrors.PromptTemplateNotFound);
        }

        await cache.SetByIdAsync(template.Id, template, cancellationToken: cancellationToken);

        return Result.Success(template);
    }
}
