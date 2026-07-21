using CustomCodeFramework.Core.Pagination;
using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Queries;
using Dhole.AI.Contracts.Executions.Response;
using Dhole.AI.Domain.Connections.Enums;
using Dhole.AI.Domain.Executions.Enums;

namespace Dhole.AI.Application.Features.Executions.GetExecutions;

public sealed record GetExecutionsQuery(
    PageRequest Page,
    string? Search = null,
    string? ProfileKey = null,
    AiExecutionType? ExecutionType = null,
    AiExecutionStatus? Status = null,
    AiProviderType? ProviderType = null,
    Guid? ModelId = null,
    DateTime? DateFromUtc = null,
    DateTime? DateToUtc = null
) : IQuery<Result<PagedResult<AiExecutionSummaryDto>>>;
