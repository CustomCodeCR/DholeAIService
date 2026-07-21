
using CustomCodeFramework.Workers.Abstractions;
using Dhole.AI.Application.Abstractions.Cache;
using Dhole.AI.Application.Abstractions.Providers.Models;
using Dhole.AI.Application.Abstractions.Services;
using Dhole.AI.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.AI.Worker.Workers;

internal sealed class AiConnectionHealthWorker(
    ServiceDbContext dbContext,
    IAiProviderResolver providerResolver,
    IAiSecretResolver secretResolver,
    IAiConnectionCacheService connectionCache,
    ILogger<AiConnectionHealthWorker> logger
) : IBackgroundWorker
{
    public string Name => "ai.connection-health";

    public async Task ExecuteAsync(
        IWorkerExecutionContext context,
        CancellationToken cancellationToken
    )
    {
        var connections = await dbContext.AiConnections
            .Where(x => !x.IsDeleted && x.IsActive)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        foreach (var connection in connections)
        {
            try
            {
                var secret = await secretResolver.ResolveAsync(
                    connection.SecretReference,
                    cancellationToken
                );

                var providerContext = new AiProviderContext(
                    connection.Id,
                    connection.Name,
                    connection.ProviderType,
                    connection.BaseUrl,
                    secret,
                    connection.TimeoutSeconds
                );

                var checker = providerResolver.ResolveHealthChecker(connection.ProviderType);
                var result = await checker.CheckAsync(providerContext, cancellationToken);

                if (result.Success)
                {
                    connection.MarkHealthy(result.CheckedAtUtc);
                }
                else
                {
                    connection.MarkUnhealthy(
                        result.CheckedAtUtc,
                        result.ErrorMessage ?? "No fue posible establecer la conexión."
                    );
                }

                await connectionCache.RemoveConnectionCacheAsync(
                    connection.Id,
                    cancellationToken
                );
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                connection.MarkUnhealthy(DateTime.UtcNow, exception.Message);

                logger.LogWarning(
                    exception,
                    "AI connection health check failed. ConnectionId: {ConnectionId}, Name: {Name}.",
                    connection.Id,
                    connection.Name
                );
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "AI connection health check completed. Connections: {Count}.",
            connections.Count
        );
    }
}
