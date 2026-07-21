using CustomCodeFramework.Core.Pagination;
using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Queries;
using Dhole.AI.Application.Abstractions.Repositories;
using Dhole.AI.Contracts.Executions.Response;

namespace Dhole.AI.Application.Features.Executions.GetExecutions;

public sealed class GetExecutionsQueryHandler(IAiExecutionRepository executions)
    : IQueryHandler<GetExecutionsQuery, Result<PagedResult<AiExecutionSummaryDto>>>
{
    public async Task<Result<PagedResult<AiExecutionSummaryDto>>> HandleAsync(
        GetExecutionsQuery query,
        CancellationToken cancellationToken = default
    )
    {
        var result = await executions.GetPagedAsync(
            query.Page,
            query.Search,
            query.ProfileKey,
            query.ExecutionType,
            query.Status,
            query.ProviderType,
            query.ModelId,
            query.DateFromUtc,
            query.DateToUtc,
            cancellationToken
        );

        return Result.Success(result);
    }
}
