using CustomCodeFramework.Core.Pagination;
using CustomCodeFramework.Persistence.Abstractions;
using Dhole.AI.Contracts.Profiles.Response;
using Dhole.AI.Domain.Profiles.Entities;
using Dhole.AI.Domain.Profiles.Enums;

namespace Dhole.AI.Application.Abstractions.Repositories;

public interface IAiProfileRepository : IRepository<AiProfile, Guid>
{
    Task<bool> ExistsByKeyAsync(
        string key,
        Guid? excludeId = null,
        CancellationToken cancellationToken = default
    );

    Task<AiProfile?> GetByKeyAsync(string key, CancellationToken cancellationToken = default);

    Task<AiProfileDto?> GetDtoByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<AiProfileDto?> GetDtoByKeyAsync(string key, CancellationToken cancellationToken = default);

    Task<PagedResult<AiProfileSummaryDto>> GetPagedAsync(
        PageRequest page,
        string? search = null,
        AiRoutingMode? routingMode = null,
        AiResponseFormat? responseFormat = null,
        bool? isActive = null,
        CancellationToken cancellationToken = default
    );
}
