using CustomCodeFramework.Core.Pagination;
using CustomCodeFramework.Postgres.EntityFramework.Repositories;
using Dhole.AI.Application.Abstractions.Repositories;
using Dhole.AI.Contracts.PromptTemplates.Response;
using Dhole.AI.Domain.PromptTemplates.Entities;
using Dhole.AI.Persistence.DbContexts;
using Dhole.AI.Persistence.Mappings;
using Microsoft.EntityFrameworkCore;

namespace Dhole.AI.Persistence.Repositories;

public sealed class AiPromptTemplateRepository(ServiceDbContext dbContext)
    : EfRepository<AiPromptTemplate, Guid>(dbContext),
        IAiPromptTemplateRepository
{
    public Task<bool> ExistsByKeyAsync(
        string key,
        Guid? excludeId = null,
        CancellationToken cancellationToken = default
    )
    {
        var normalized = key.Trim().ToLowerInvariant();

        return dbContext.AiPromptTemplates.AnyAsync(
            template =>
                template.Key == normalized
                && !template.IsDeleted
                && (!excludeId.HasValue || template.Id != excludeId.Value),
            cancellationToken
        );
    }

    public async Task<AiPromptTemplateDto?> GetDtoByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default
    )
    {
        var projection = await dbContext
            .AiPromptTemplates.AsNoTracking()
            .Where(template => template.Id == id && !template.IsDeleted)
            .Select(template => new PromptTemplateProjection(
                template.Id,
                template.Key,
                template.Name,
                template.Description,
                template.SystemPrompt,
                template.UserPromptTemplate,
                template.VariablesJson,
                template.IsActive
            ))
            .SingleOrDefaultAsync(cancellationToken);

        return projection is null ? null : ToDto(projection);
    }

    public async Task<PagedResult<AiPromptTemplateSummaryDto>> GetPagedAsync(
        PageRequest page,
        string? search = null,
        bool? isActive = null,
        CancellationToken cancellationToken = default
    )
    {
        var query = dbContext
            .AiPromptTemplates.AsNoTracking()
            .Where(template => !template.IsDeleted);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";

            query = query.Where(template =>
                EF.Functions.ILike(template.Key, pattern)
                || EF.Functions.ILike(template.Name, pattern)
                || (
                    template.Description != null
                    && EF.Functions.ILike(template.Description, pattern)
                )
            );
        }

        if (isActive.HasValue)
        {
            query = query.Where(template => template.IsActive == isActive.Value);
        }

        var total = await query.LongCountAsync(cancellationToken);

        var projections = await query
            .OrderBy(template => template.Name)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(template => new PromptTemplateProjection(
                template.Id,
                template.Key,
                template.Name,
                template.Description,
                template.SystemPrompt,
                template.UserPromptTemplate,
                template.VariablesJson,
                template.IsActive
            ))
            .ToListAsync(cancellationToken);

        var items = projections
            .Select(template =>
            {
                var variables = AiContractMappings.ParseVariables(template.VariablesJson);

                return new AiPromptTemplateSummaryDto(
                    template.Id,
                    template.Key,
                    template.Name,
                    template.Description,
                    variables.Count,
                    template.IsActive
                );
            })
            .ToArray();

        return PagedResult<AiPromptTemplateSummaryDto>.Create(
            items,
            page.PageNumber,
            page.PageSize,
            total
        );
    }

    private static AiPromptTemplateDto ToDto(PromptTemplateProjection template)
    {
        return new AiPromptTemplateDto(
            template.Id,
            template.Key,
            template.Name,
            template.Description,
            template.SystemPrompt,
            template.UserPromptTemplate,
            AiContractMappings.ParseVariables(template.VariablesJson),
            template.IsActive
        );
    }

    private sealed record PromptTemplateProjection(
        Guid Id,
        string Key,
        string Name,
        string? Description,
        string? SystemPrompt,
        string? UserPromptTemplate,
        string? VariablesJson,
        bool IsActive
    );
}
