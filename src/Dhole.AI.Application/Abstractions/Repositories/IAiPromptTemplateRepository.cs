using CustomCodeFramework.Core.Pagination;
using CustomCodeFramework.Persistence.Abstractions;
using Dhole.AI.Contracts.PromptTemplates.Response;
using Dhole.AI.Domain.PromptTemplates.Entities;

namespace Dhole.AI.Application.Abstractions.Repositories;

public interface IAiPromptTemplateRepository : IRepository<AiPromptTemplate, Guid>
{
    Task<bool> ExistsByKeyAsync(
        string key,
        Guid? excludeId = null,
        CancellationToken cancellationToken = default
    );

    Task<AiPromptTemplateDto?> GetDtoByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default
    );

    Task<PagedResult<AiPromptTemplateSummaryDto>> GetPagedAsync(
        PageRequest page,
        string? search = null,
        bool? isActive = null,
        CancellationToken cancellationToken = default
    );
}
