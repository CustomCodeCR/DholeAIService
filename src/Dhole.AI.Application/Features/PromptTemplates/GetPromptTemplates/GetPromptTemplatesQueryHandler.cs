using CustomCodeFramework.Core.Pagination;
using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Queries;
using Dhole.AI.Application.Abstractions.Repositories;
using Dhole.AI.Contracts.PromptTemplates.Response;

namespace Dhole.AI.Application.Features.PromptTemplates.GetPromptTemplates;

public sealed class GetPromptTemplatesQueryHandler(IAiPromptTemplateRepository templates)
    : IQueryHandler<GetPromptTemplatesQuery, Result<PagedResult<AiPromptTemplateSummaryDto>>>
{
    public async Task<Result<PagedResult<AiPromptTemplateSummaryDto>>> HandleAsync(
        GetPromptTemplatesQuery query,
        CancellationToken cancellationToken = default
    )
    {
        var result = await templates.GetPagedAsync(
            query.Page,
            query.Search,
            query.IsActive,
            cancellationToken
        );

        return Result.Success(result);
    }
}
