using CustomCodeFramework.Core.Pagination;
using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Queries;
using Dhole.AI.Contracts.Connections.Response;
using Dhole.AI.Domain.Connections.Enums;

namespace Dhole.AI.Application.Features.Connections.GetConnections;

public sealed record GetConnectionsQuery(
    PageRequest Page,
    string? Search = null,
    AiProviderType? ProviderType = null,
    AiConnectionStatus? Status = null,
    bool? IsActive = null
) : IQuery<Result<PagedResult<AiConnectionSummaryDto>>>;
