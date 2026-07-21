using CustomCodeFramework.Core.Pagination;
using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Queries;
using Dhole.AI.Application.Abstractions.Repositories;
using Dhole.AI.Contracts.Models.Response;

namespace Dhole.AI.Application.Features.Models.GetModels;

public sealed class GetModelsQueryHandler(IAiModelRepository models)
    : IQueryHandler<GetModelsQuery, Result<PagedResult<AiModelSummaryDto>>>
{
    public async Task<Result<PagedResult<AiModelSummaryDto>>> HandleAsync(
        GetModelsQuery query,
        CancellationToken cancellationToken = default
    )
    {
        var result = await models.GetPagedAsync(
            query.Page,
            query.Search,
            query.ConnectionId,
            query.ProviderType,
            query.Capability,
            query.Status,
            query.IsLocal,
            query.IsActive,
            cancellationToken
        );

        return Result.Success(result);
    }
}
