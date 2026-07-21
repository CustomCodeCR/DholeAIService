using CustomCodeFramework.Core.Pagination;
using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Queries;
using Dhole.AI.Application.Abstractions.Repositories;
using Dhole.AI.Contracts.Connections.Response;

namespace Dhole.AI.Application.Features.Connections.GetConnections;

public sealed class GetConnectionsQueryHandler(IAiConnectionRepository connections)
    : IQueryHandler<GetConnectionsQuery, Result<PagedResult<AiConnectionSummaryDto>>>
{
    public async Task<Result<PagedResult<AiConnectionSummaryDto>>> HandleAsync(
        GetConnectionsQuery query,
        CancellationToken cancellationToken = default
    )
    {
        var result = await connections.GetPagedAsync(
            query.Page,
            query.Search,
            query.ProviderType,
            query.Status,
            query.IsActive,
            cancellationToken
        );

        return Result.Success(result);
    }
}
