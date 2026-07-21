using CustomCodeFramework.Core.Pagination;
using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Queries;
using Dhole.AI.Application.Abstractions.Repositories;
using Dhole.AI.Contracts.Profiles.Response;

namespace Dhole.AI.Application.Features.Profiles.GetProfiles;

public sealed class GetProfilesQueryHandler(IAiProfileRepository profiles)
    : IQueryHandler<GetProfilesQuery, Result<PagedResult<AiProfileSummaryDto>>>
{
    public async Task<Result<PagedResult<AiProfileSummaryDto>>> HandleAsync(
        GetProfilesQuery query,
        CancellationToken cancellationToken = default
    )
    {
        var result = await profiles.GetPagedAsync(
            query.Page,
            query.Search,
            query.RoutingMode,
            query.ResponseFormat,
            query.IsActive,
            cancellationToken
        );

        return Result.Success(result);
    }
}
