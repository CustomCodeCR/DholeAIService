using CustomCodeFramework.Core.Pagination;
using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Queries;
using Dhole.AI.Contracts.Profiles.Response;
using Dhole.AI.Domain.Profiles.Enums;

namespace Dhole.AI.Application.Features.Profiles.GetProfiles;

public sealed record GetProfilesQuery(
    PageRequest Page,
    string? Search = null,
    AiRoutingMode? RoutingMode = null,
    AiResponseFormat? ResponseFormat = null,
    bool? IsActive = null
) : IQuery<Result<PagedResult<AiProfileSummaryDto>>>;
