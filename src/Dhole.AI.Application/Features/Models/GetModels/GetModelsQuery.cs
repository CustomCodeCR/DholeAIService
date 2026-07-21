using CustomCodeFramework.Core.Pagination;
using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Queries;
using Dhole.AI.Contracts.Models.Response;
using Dhole.AI.Domain.Connections.Enums;
using Dhole.AI.Domain.Models.Enums;

namespace Dhole.AI.Application.Features.Models.GetModels;

public sealed record GetModelsQuery(
    PageRequest Page,
    string? Search = null,
    Guid? ConnectionId = null,
    AiProviderType? ProviderType = null,
    AiModelCapability? Capability = null,
    AiModelStatus? Status = null,
    bool? IsLocal = null,
    bool? IsActive = null
) : IQuery<Result<PagedResult<AiModelSummaryDto>>>;
