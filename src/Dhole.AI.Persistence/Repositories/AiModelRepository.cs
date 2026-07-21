using CustomCodeFramework.Core.Pagination;
using CustomCodeFramework.Postgres.EntityFramework.Repositories;
using Dhole.AI.Application.Abstractions.Repositories;
using Dhole.AI.Contracts.Models.Response;
using Dhole.AI.Domain.Connections.Enums;
using Dhole.AI.Domain.Models.Entities;
using Dhole.AI.Domain.Models.Enums;
using Dhole.AI.Persistence.DbContexts;
using Dhole.AI.Persistence.Mappings;
using Microsoft.EntityFrameworkCore;

namespace Dhole.AI.Persistence.Repositories;

public sealed class AiModelRepository(ServiceDbContext dbContext)
    : EfRepository<AiModel, Guid>(dbContext),
        IAiModelRepository
{
    public Task<bool> ExistsByExternalModelIdAsync(
        Guid connectionId,
        string externalModelId,
        Guid? excludeId = null,
        CancellationToken cancellationToken = default
    )
    {
        var normalized = externalModelId.Trim();

        return dbContext.AiModels.AnyAsync(
            model =>
                model.ConnectionId == connectionId
                && EF.Functions.ILike(model.ExternalModelId, normalized)
                && !model.IsDeleted
                && (!excludeId.HasValue || model.Id != excludeId.Value),
            cancellationToken
        );
    }

    public async Task<IReadOnlyCollection<AiModel>> GetByIdsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken = default
    )
    {
        if (ids.Count == 0)
        {
            return [];
        }

        return await dbContext
            .AiModels.AsNoTracking()
            .Where(model => ids.Contains(model.Id) && !model.IsDeleted)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlySet<string>> GetRegisteredExternalIdsAsync(
        Guid connectionId,
        CancellationToken cancellationToken = default
    )
    {
        var values = await dbContext
            .AiModels.AsNoTracking()
            .Where(model => model.ConnectionId == connectionId && !model.IsDeleted)
            .Select(model => model.ExternalModelId)
            .ToListAsync(cancellationToken);

        return values.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public async Task<AiModelDto?> GetDtoByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default
    )
    {
        var projection = await (
            from model in dbContext.AiModels.AsNoTracking()
            join connection in dbContext.AiConnections.AsNoTracking()
                on model.ConnectionId equals connection.Id
            where model.Id == id && !model.IsDeleted && !connection.IsDeleted
            select new ModelProjection(
                model.Id,
                model.ConnectionId,
                connection.Name,
                connection.ProviderType,
                model.ExternalModelId,
                model.Name,
                model.Capabilities,
                model.ContextWindow,
                model.MaximumOutputTokens,
                model.InputCostPerMillionTokens,
                model.OutputCostPerMillionTokens,
                model.IsLocal,
                model.Status,
                model.LastAvailabilityCheckAtUtc,
                model.LastAvailabilityError,
                model.IsActive
            )
        ).SingleOrDefaultAsync(cancellationToken);

        return projection is null ? null : ToDto(projection);
    }

    public async Task<PagedResult<AiModelSummaryDto>> GetPagedAsync(
        PageRequest page,
        string? search = null,
        Guid? connectionId = null,
        AiProviderType? providerType = null,
        AiModelCapability? capability = null,
        AiModelStatus? status = null,
        bool? isLocal = null,
        bool? isActive = null,
        CancellationToken cancellationToken = default
    )
    {
        var query =
            from model in dbContext.AiModels.AsNoTracking()
            join connection in dbContext.AiConnections.AsNoTracking()
                on model.ConnectionId equals connection.Id
            where !model.IsDeleted && !connection.IsDeleted
            select new { Model = model, Connection = connection };

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";

            query = query.Where(item =>
                EF.Functions.ILike(item.Model.Name, pattern)
                || EF.Functions.ILike(item.Model.ExternalModelId, pattern)
                || EF.Functions.ILike(item.Connection.Name, pattern)
            );
        }

        if (connectionId.HasValue)
        {
            query = query.Where(item => item.Model.ConnectionId == connectionId.Value);
        }

        if (providerType.HasValue)
        {
            query = query.Where(item => item.Connection.ProviderType == providerType.Value);
        }

        if (capability.HasValue)
        {
            var value = capability.Value;

            query = query.Where(item => (item.Model.Capabilities & value) == value);
        }

        if (status.HasValue)
        {
            query = query.Where(item => item.Model.Status == status.Value);
        }

        if (isLocal.HasValue)
        {
            query = query.Where(item => item.Model.IsLocal == isLocal.Value);
        }

        if (isActive.HasValue)
        {
            query = query.Where(item => item.Model.IsActive == isActive.Value);
        }

        var total = await query.LongCountAsync(cancellationToken);

        var projections = await query
            .OrderBy(item => item.Connection.Name)
            .ThenBy(item => item.Model.Name)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(item => new ModelSummaryProjection(
                item.Model.Id,
                item.Model.ConnectionId,
                item.Connection.Name,
                item.Connection.ProviderType,
                item.Model.ExternalModelId,
                item.Model.Name,
                item.Model.Capabilities,
                item.Model.IsLocal,
                item.Model.Status,
                item.Model.IsActive
            ))
            .ToListAsync(cancellationToken);

        var items = projections.Select(ToSummaryDto).ToArray();

        return PagedResult<AiModelSummaryDto>.Create(items, page.PageNumber, page.PageSize, total);
    }

    private static AiModelDto ToDto(ModelProjection model)
    {
        return new AiModelDto(
            model.Id,
            model.ConnectionId,
            model.ConnectionName,
            model.ProviderType.ToString(),
            model.ExternalModelId,
            model.Name,
            AiContractMappings.ToCapabilities(model.Capabilities),
            model.ContextWindow,
            model.MaximumOutputTokens,
            model.InputCostPerMillionTokens,
            model.OutputCostPerMillionTokens,
            model.IsLocal,
            model.Status.ToString(),
            model.LastAvailabilityCheckAtUtc,
            model.LastAvailabilityError,
            model.IsActive
        );
    }

    private static AiModelSummaryDto ToSummaryDto(ModelSummaryProjection model)
    {
        return new AiModelSummaryDto(
            model.Id,
            model.ConnectionId,
            model.ConnectionName,
            model.ProviderType.ToString(),
            model.ExternalModelId,
            model.Name,
            AiContractMappings.ToCapabilities(model.Capabilities),
            model.IsLocal,
            model.Status.ToString(),
            model.IsActive
        );
    }

    private sealed record ModelProjection(
        Guid Id,
        Guid ConnectionId,
        string ConnectionName,
        AiProviderType ProviderType,
        string ExternalModelId,
        string Name,
        AiModelCapability Capabilities,
        int? ContextWindow,
        int? MaximumOutputTokens,
        decimal? InputCostPerMillionTokens,
        decimal? OutputCostPerMillionTokens,
        bool IsLocal,
        AiModelStatus Status,
        DateTime? LastAvailabilityCheckAtUtc,
        string? LastAvailabilityError,
        bool IsActive
    );

    private sealed record ModelSummaryProjection(
        Guid Id,
        Guid ConnectionId,
        string ConnectionName,
        AiProviderType ProviderType,
        string ExternalModelId,
        string Name,
        AiModelCapability Capabilities,
        bool IsLocal,
        AiModelStatus Status,
        bool IsActive
    );
}
