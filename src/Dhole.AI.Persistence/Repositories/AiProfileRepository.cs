using CustomCodeFramework.Core.Pagination;
using CustomCodeFramework.Postgres.EntityFramework.Repositories;
using Dhole.AI.Application.Abstractions.Repositories;
using Dhole.AI.Contracts.Profiles.Response;
using Dhole.AI.Domain.Connections.Enums;
using Dhole.AI.Domain.Models.Enums;
using Dhole.AI.Domain.Profiles.Entities;
using Dhole.AI.Domain.Profiles.Enums;
using Dhole.AI.Persistence.DbContexts;
using Dhole.AI.Persistence.Mappings;
using Microsoft.EntityFrameworkCore;

namespace Dhole.AI.Persistence.Repositories;

public sealed class AiProfileRepository(ServiceDbContext dbContext)
    : EfRepository<AiProfile, Guid>(dbContext),
        IAiProfileRepository
{
    public override Task<AiProfile?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default
    )
    {
        return dbContext
            .AiProfiles.Include(profile => profile.Models)
            .SingleOrDefaultAsync(profile => profile.Id == id, cancellationToken);
    }

    public Task<bool> ExistsByKeyAsync(
        string key,
        Guid? excludeId = null,
        CancellationToken cancellationToken = default
    )
    {
        var normalized = key.Trim().ToLowerInvariant();

        return dbContext.AiProfiles.AnyAsync(
            profile =>
                profile.Key == normalized
                && !profile.IsDeleted
                && (!excludeId.HasValue || profile.Id != excludeId.Value),
            cancellationToken
        );
    }

    public Task<AiProfile?> GetByKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        var normalized = key.Trim().ToLowerInvariant();

        return dbContext
            .AiProfiles.Include(profile => profile.Models)
            .SingleOrDefaultAsync(
                profile => profile.Key == normalized && !profile.IsDeleted,
                cancellationToken
            );
    }

    public async Task<AiProfileDto?> GetDtoByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default
    )
    {
        var header = await GetProfileHeaderQuery(id: id)
            .SingleOrDefaultAsync(cancellationToken);

        return header is null ? null : await CreateDtoAsync(header, cancellationToken);
    }

    public async Task<AiProfileDto?> GetDtoByKeyAsync(
        string key,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var normalized = key.Trim().ToLowerInvariant();

        var header = await GetProfileHeaderQuery(key: normalized)
            .SingleOrDefaultAsync(cancellationToken);

        return header is null ? null : await CreateDtoAsync(header, cancellationToken);
    }

    public async Task<PagedResult<AiProfileSummaryDto>> GetPagedAsync(
        PageRequest page,
        string? search = null,
        AiRoutingMode? routingMode = null,
        AiResponseFormat? responseFormat = null,
        bool? isActive = null,
        CancellationToken cancellationToken = default
    )
    {
        var query = dbContext.AiProfiles.AsNoTracking().Where(profile => !profile.IsDeleted);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";

            query = query.Where(profile =>
                EF.Functions.ILike(profile.Key, pattern)
                || EF.Functions.ILike(profile.Name, pattern)
                || (profile.Description != null && EF.Functions.ILike(profile.Description, pattern))
            );
        }

        if (routingMode.HasValue)
        {
            query = query.Where(profile => profile.RoutingMode == routingMode.Value);
        }

        if (responseFormat.HasValue)
        {
            query = query.Where(profile => profile.ResponseFormat == responseFormat.Value);
        }

        if (isActive.HasValue)
        {
            query = query.Where(profile => profile.IsActive == isActive.Value);
        }

        var total = await query.LongCountAsync(cancellationToken);

        var items = await query
            .OrderBy(profile => profile.Name)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(profile => new AiProfileSummaryDto(
                profile.Id,
                profile.Key,
                profile.Name,
                profile.Description,
                profile.RoutingMode.ToString(),
                profile.ResponseFormat.ToString(),
                profile.Models.Count,
                profile.IsActive
            ))
            .ToListAsync(cancellationToken);

        return PagedResult<AiProfileSummaryDto>.Create(
            items,
            page.PageNumber,
            page.PageSize,
            total
        );
    }

    private IQueryable<ProfileHeaderProjection> GetProfileHeaderQuery(
        Guid? id = null,
        string? key = null
    )
    {
        IQueryable<AiProfile> profiles = dbContext.AiProfiles
            .AsNoTracking()
            .Where(profile => !profile.IsDeleted);

        // Apply filters to the entity query before projecting. EF Core can translate
        // these predicates directly to SQL, unlike a predicate over the constructed
        // ProfileHeaderProjection record.
        if (id.HasValue)
        {
            var profileId = id.Value;
            profiles = profiles.Where(profile => profile.Id == profileId);
        }

        if (!string.IsNullOrWhiteSpace(key))
        {
            profiles = profiles.Where(profile => profile.Key == key);
        }

        return from profile in profiles
            join promptTemplate in dbContext.AiPromptTemplates.AsNoTracking()
                on profile.PromptTemplateId equals promptTemplate.Id
                into promptTemplates
            from promptTemplate in promptTemplates.DefaultIfEmpty()
            select new ProfileHeaderProjection(
                profile.Id,
                profile.Key,
                profile.Name,
                profile.Description,
                profile.PromptTemplateId,
                promptTemplate == null ? null : promptTemplate.Name,
                profile.RoutingMode,
                profile.ResponseFormat,
                profile.Temperature,
                profile.MaximumOutputTokens,
                profile.TimeoutSeconds,
                profile.JsonSchema,
                profile.IsActive
            );
    }

    private async Task<AiProfileDto> CreateDtoAsync(
        ProfileHeaderProjection profile,
        CancellationToken cancellationToken
    )
    {
        var modelProjections = await (
            from profileModel in dbContext.AiProfileModels.AsNoTracking()
            join model in dbContext.AiModels.AsNoTracking() on profileModel.ModelId equals model.Id
            join connection in dbContext.AiConnections.AsNoTracking()
                on model.ConnectionId equals connection.Id
            where profileModel.ProfileId == profile.Id
            orderby profileModel.Priority
            select new ProfileModelProjection(
                profileModel.Id,
                model.Id,
                model.Name,
                model.ExternalModelId,
                connection.Id,
                connection.Name,
                connection.ProviderType,
                model.Capabilities,
                profileModel.Priority,
                profileModel.IsFallback,
                model.IsActive && !model.IsDeleted && connection.IsActive && !connection.IsDeleted
            )
        ).ToListAsync(cancellationToken);

        var models = modelProjections
            .Select(model => new AiProfileModelDto(
                model.Id,
                model.ModelId,
                model.ModelName,
                model.ExternalModelId,
                model.ConnectionId,
                model.ConnectionName,
                model.ProviderType.ToString(),
                AiContractMappings.ToCapabilities(model.Capabilities),
                model.Priority,
                model.IsFallback,
                model.IsModelActive
            ))
            .ToArray();

        return new AiProfileDto(
            profile.Id,
            profile.Key,
            profile.Name,
            profile.Description,
            profile.PromptTemplateId,
            profile.PromptTemplateName,
            profile.RoutingMode.ToString(),
            profile.ResponseFormat.ToString(),
            profile.Temperature,
            profile.MaximumOutputTokens,
            profile.TimeoutSeconds,
            profile.JsonSchema,
            profile.IsActive,
            models
        );
    }

    private sealed record ProfileHeaderProjection(
        Guid Id,
        string Key,
        string Name,
        string? Description,
        Guid? PromptTemplateId,
        string? PromptTemplateName,
        AiRoutingMode RoutingMode,
        AiResponseFormat ResponseFormat,
        decimal Temperature,
        int MaximumOutputTokens,
        int TimeoutSeconds,
        string? JsonSchema,
        bool IsActive
    );

    private sealed record ProfileModelProjection(
        Guid Id,
        Guid ModelId,
        string ModelName,
        string ExternalModelId,
        Guid ConnectionId,
        string ConnectionName,
        AiProviderType ProviderType,
        AiModelCapability Capabilities,
        int Priority,
        bool IsFallback,
        bool IsModelActive
    );
}
