using CustomCodeFramework.Core.Pagination;
using CustomCodeFramework.Persistence.Abstractions;
using Dhole.AI.Contracts.Models.Response;
using Dhole.AI.Domain.Connections.Enums;
using Dhole.AI.Domain.Models.Entities;
using Dhole.AI.Domain.Models.Enums;

namespace Dhole.AI.Application.Abstractions.Repositories;

public interface IAiModelRepository : IRepository<AiModel, Guid>
{
    Task<bool> ExistsByExternalModelIdAsync(
        Guid connectionId,
        string externalModelId,
        Guid? excludeId = null,
        CancellationToken cancellationToken = default
    );

    Task<IReadOnlyCollection<AiModel>> GetByIdsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken = default
    );

    Task<IReadOnlySet<string>> GetRegisteredExternalIdsAsync(
        Guid connectionId,
        CancellationToken cancellationToken = default
    );

    Task<AiModelDto?> GetDtoByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<PagedResult<AiModelSummaryDto>> GetPagedAsync(
        PageRequest page,
        string? search = null,
        Guid? connectionId = null,
        AiProviderType? providerType = null,
        AiModelCapability? capability = null,
        AiModelStatus? status = null,
        bool? isLocal = null,
        bool? isActive = null,
        CancellationToken cancellationToken = default
    );
}
