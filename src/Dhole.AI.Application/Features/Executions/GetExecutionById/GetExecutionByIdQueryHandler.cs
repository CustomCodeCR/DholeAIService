using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Queries;
using Dhole.AI.Application.Abstractions.Repositories;
using Dhole.AI.Contracts.Executions.Response;
using Dhole.AI.Domain.Shared;

namespace Dhole.AI.Application.Features.Executions.GetById;

public sealed class GetExecutionByIdQueryHandler(IAiExecutionRepository executions)
    : IQueryHandler<GetExecutionByIdQuery, Result<AiExecutionDto>>
{
    public async Task<Result<AiExecutionDto>> HandleAsync(
        GetExecutionByIdQuery query,
        CancellationToken cancellationToken = default
    )
    {
        var execution = await executions.GetDtoByIdAsync(query.Id, cancellationToken);

        return execution is null
            ? Result.Failure<AiExecutionDto>(AiErrors.ExecutionNotFound)
            : Result.Success(execution);
    }
}
