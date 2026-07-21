using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Queries;
using Dhole.AI.Contracts.Executions.Response;

namespace Dhole.AI.Application.Features.Executions.GetById;

public sealed record GetExecutionByIdQuery(Guid Id) : IQuery<Result<AiExecutionDto>>;
