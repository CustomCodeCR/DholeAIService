using CustomCodeFramework.Core.Pagination;
using CustomCodeFramework.Persistence.Abstractions;
using Dhole.AI.Contracts.Connections.Response;
using Dhole.AI.Domain.Connections.Entities;
using Dhole.AI.Domain.Connections.Enums;

namespace Dhole.AI.Application.Abstractions.Repositories;

public interface IAiConnectionRepository : IRepository<AiConnection, Guid>
{
    Task<bool> ExistsByNameAsync(
        string name,
        Guid? excludeId = null,
        CancellationToken cancellationToken = default
    );

    Task<IReadOnlyCollection<AiConnection>> GetActiveAsync(
        CancellationToken cancellationToken = default
    );

    Task<AiConnectionDto?> GetDtoByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<PagedResult<AiConnectionSummaryDto>> GetPagedAsync(
        PageRequest page,
        string? search = null,
        AiProviderType? providerType = null,
        AiConnectionStatus? status = null,
        bool? isActive = null,
        CancellationToken cancellationToken = default
    );
}
