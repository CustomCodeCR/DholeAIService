
using CustomCodeFramework.Workers.Abstractions;
using Dhole.AI.Application.Abstractions.Cache;
using Dhole.AI.Application.Abstractions.Repositories;
using Dhole.AI.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.AI.Worker.Workers;

internal sealed class AiCacheWarmupWorker(
    ServiceDbContext dbContext,
    IAiConnectionRepository connectionRepository,
    IAiModelRepository modelRepository,
    IAiProfileRepository profileRepository,
    IAiPromptTemplateRepository promptTemplateRepository,
    IAiConnectionCacheService connectionCache,
    IAiModelCacheService modelCache,
    IAiProfileCacheService profileCache,
    IAiPromptTemplateCacheService promptTemplateCache,
    ILogger<AiCacheWarmupWorker> logger
) : IBackgroundWorker
{
    public string Name => "ai.cache-warmup";

    public async Task ExecuteAsync(
        IWorkerExecutionContext context,
        CancellationToken cancellationToken
    )
    {
        logger.LogInformation("AI cache warmup started.");

        await WarmConnectionsAsync(cancellationToken);
        await WarmModelsAsync(cancellationToken);
        await WarmProfilesAsync(cancellationToken);
        await WarmPromptTemplatesAsync(cancellationToken);

        logger.LogInformation("AI cache warmup completed.");
    }

    private async Task WarmConnectionsAsync(CancellationToken cancellationToken)
    {
        var ids = await dbContext.AiConnections
            .AsNoTracking()
            .Where(x => !x.IsDeleted)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        foreach (var id in ids)
        {
            var dto = await connectionRepository.GetDtoByIdAsync(id, cancellationToken);
            if (dto is not null)
            {
                await connectionCache.SetByIdAsync(id, dto, cancellationToken: cancellationToken);
            }
        }

        logger.LogInformation("AI connection cache warmup completed. Connections: {Count}.", ids.Count);
    }

    private async Task WarmModelsAsync(CancellationToken cancellationToken)
    {
        var ids = await dbContext.AiModels
            .AsNoTracking()
            .Where(x => !x.IsDeleted)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        foreach (var id in ids)
        {
            var dto = await modelRepository.GetDtoByIdAsync(id, cancellationToken);
            if (dto is not null)
            {
                await modelCache.SetByIdAsync(id, dto, cancellationToken: cancellationToken);
            }
        }

        logger.LogInformation("AI model cache warmup completed. Models: {Count}.", ids.Count);
    }

    private async Task WarmProfilesAsync(CancellationToken cancellationToken)
    {
        var ids = await dbContext.AiProfiles
            .AsNoTracking()
            .Where(x => !x.IsDeleted)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        var warmedCount = 0;
        var failedCount = 0;

        foreach (var id in ids)
        {
            try
            {
                var dto = await profileRepository.GetDtoByIdAsync(id, cancellationToken);
                if (dto is null)
                {
                    logger.LogWarning(
                        "AI profile {ProfileId} was not found during cache warmup.",
                        id
                    );

                    continue;
                }

                await profileCache.SetByIdAsync(
                    id,
                    dto,
                    cancellationToken: cancellationToken
                );
                await profileCache.SetByKeyAsync(
                    dto.Key,
                    dto,
                    cancellationToken: cancellationToken
                );

                warmedCount++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                failedCount++;
                logger.LogError(
                    exception,
                    "Failed to warm AI profile {ProfileId}.",
                    id
                );
            }
        }

        logger.LogInformation(
            "AI profile cache warmup completed. Total: {Total}, Warmed: {Warmed}, Failed: {Failed}.",
            ids.Count,
            warmedCount,
            failedCount
        );
    }

    private async Task WarmPromptTemplatesAsync(CancellationToken cancellationToken)
    {
        var ids = await dbContext.AiPromptTemplates
            .AsNoTracking()
            .Where(x => !x.IsDeleted)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        foreach (var id in ids)
        {
            var dto = await promptTemplateRepository.GetDtoByIdAsync(id, cancellationToken);
            if (dto is not null)
            {
                await promptTemplateCache.SetByIdAsync(
                    id,
                    dto,
                    cancellationToken: cancellationToken
                );
            }
        }

        logger.LogInformation(
            "AI prompt template cache warmup completed. PromptTemplates: {Count}.",
            ids.Count
        );
    }
}
