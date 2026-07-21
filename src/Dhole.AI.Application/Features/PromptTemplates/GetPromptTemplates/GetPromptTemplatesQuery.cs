using CustomCodeFramework.Core.Pagination;
using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Queries;
using Dhole.AI.Contracts.PromptTemplates.Response;

namespace Dhole.AI.Application.Features.PromptTemplates.GetPromptTemplates;

public sealed record GetPromptTemplatesQuery(
    PageRequest Page,
    string? Search = null,
    bool? IsActive = null
) : IQuery<Result<PagedResult<AiPromptTemplateSummaryDto>>>;
