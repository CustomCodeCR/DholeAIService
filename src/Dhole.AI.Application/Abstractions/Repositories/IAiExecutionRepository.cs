using CustomCodeFramework.Core.Pagination;
using CustomCodeFramework.Persistence.Abstractions;
using Dhole.AI.Contracts.Executions.Response;
using Dhole.AI.Domain.Connections.Enums;
using Dhole.AI.Domain.Executions.Entities;
using Dhole.AI.Domain.Executions.Enums;

namespace Dhole.AI.Application.Abstractions.Repositories;

public interface IAiExecutionRepository : IRepository<AiExecution, Guid>
{
    Task<AiExecutionDto?> GetDtoByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<PagedResult<AiExecutionSummaryDto>> GetPagedAsync(
        PageRequest page,
        string? search = null,
        string? profileKey = null,
        AiExecutionType? executionType = null,
        AiExecutionStatus? status = null,
        AiProviderType? providerType = null,
        Guid? modelId = null,
        DateTime? dateFromUtc = null,
        DateTime? dateToUtc = null,
        CancellationToken cancellationToken = default
    );
}
