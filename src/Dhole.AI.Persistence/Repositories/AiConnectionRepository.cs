using CustomCodeFramework.Core.Pagination;
using CustomCodeFramework.Postgres.EntityFramework.Repositories;
using Dhole.AI.Application.Abstractions.Repositories;
using Dhole.AI.Contracts.Connections.Response;
using Dhole.AI.Domain.Connections.Entities;
using Dhole.AI.Domain.Connections.Enums;
using Dhole.AI.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.AI.Persistence.Repositories;

public sealed class AiConnectionRepository(ServiceDbContext dbContext)
    : EfRepository<AiConnection, Guid>(dbContext),
        IAiConnectionRepository
{
    public Task<bool> ExistsByNameAsync(
        string name,
        Guid? excludeId = null,
        CancellationToken cancellationToken = default
    )
    {
        var normalized = name.Trim();

        return dbContext.AiConnections.AnyAsync(
            connection =>
                EF.Functions.ILike(connection.Name, normalized)
                && !connection.IsDeleted
                && (!excludeId.HasValue || connection.Id != excludeId.Value),
            cancellationToken
        );
    }

    public async Task<IReadOnlyCollection<AiConnection>> GetActiveAsync(
        CancellationToken cancellationToken = default
    )
    {
        return await dbContext
            .AiConnections.AsNoTracking()
            .Where(connection => !connection.IsDeleted && connection.IsActive)
            .OrderBy(connection => connection.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<AiConnectionDto?> GetDtoByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default
    )
    {
        return await dbContext
            .AiConnections.AsNoTracking()
            .Where(connection => connection.Id == id && !connection.IsDeleted)
            .Select(connection => new AiConnectionDto(
                connection.Id,
                connection.Name,
                connection.ProviderType.ToString(),
                connection.BaseUrl,
                connection.SecretReference,
                connection.TimeoutSeconds,
                connection.Status.ToString(),
                connection.LastHealthCheckAtUtc,
                connection.LastHealthError,
                connection.IsActive
            ))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<PagedResult<AiConnectionSummaryDto>> GetPagedAsync(
        PageRequest page,
        string? search = null,
        AiProviderType? providerType = null,
        AiConnectionStatus? status = null,
        bool? isActive = null,
        CancellationToken cancellationToken = default
    )
    {
        var query = dbContext
            .AiConnections.AsNoTracking()
            .Where(connection => !connection.IsDeleted);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";

            query = query.Where(connection =>
                EF.Functions.ILike(connection.Name, pattern)
                || EF.Functions.ILike(connection.BaseUrl, pattern)
            );
        }

        if (providerType.HasValue)
        {
            query = query.Where(connection => connection.ProviderType == providerType.Value);
        }

        if (status.HasValue)
        {
            query = query.Where(connection => connection.Status == status.Value);
        }

        if (isActive.HasValue)
        {
            query = query.Where(connection => connection.IsActive == isActive.Value);
        }

        var total = await query.LongCountAsync(cancellationToken);

        var items = await query
            .OrderBy(connection => connection.Name)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(connection => new AiConnectionSummaryDto(
                connection.Id,
                connection.Name,
                connection.ProviderType.ToString(),
                connection.BaseUrl,
                connection.Status.ToString(),
                connection.LastHealthCheckAtUtc,
                connection.IsActive
            ))
            .ToListAsync(cancellationToken);

        return PagedResult<AiConnectionSummaryDto>.Create(
            items,
            page.PageNumber,
            page.PageSize,
            total
        );
    }
}
