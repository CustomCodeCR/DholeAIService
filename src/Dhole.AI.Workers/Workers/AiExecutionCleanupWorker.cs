
using CustomCodeFramework.Workers.Abstractions;
using Dhole.AI.Domain.Executions.Enums;
using Dhole.AI.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.AI.Worker.Workers;

internal sealed class AiExecutionCleanupWorker(
    ServiceDbContext dbContext,
    IConfiguration configuration,
    ILogger<AiExecutionCleanupWorker> logger
) : IBackgroundWorker
{
    public string Name => "ai.execution-cleanup";

    public async Task ExecuteAsync(
        IWorkerExecutionContext context,
        CancellationToken cancellationToken
    )
    {
        var retentionDays = ReadPositiveInt(
            configuration["AI:Executions:RetentionDays"],
            90
        );
        var batchSize = ReadPositiveInt(
            configuration["AI:Executions:CleanupBatchSize"],
            500
        );
        var limitDate = DateTime.UtcNow.AddDays(-retentionDays);

        var executions = await dbContext.AiExecutions
            .Where(x =>
                x.CompletedAtUtc != null
                && x.CompletedAtUtc < limitDate
                && (
                    x.Status == AiExecutionStatus.Completed
                    || x.Status == AiExecutionStatus.Failed
                    || x.Status == AiExecutionStatus.Cancelled
                )
            )
            .OrderBy(x => x.CompletedAtUtc)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        if (executions.Count == 0)
        {
            logger.LogInformation("AI execution cleanup completed. No executions removed.");
            return;
        }

        dbContext.AiExecutions.RemoveRange(executions);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "AI execution cleanup completed. Executions removed: {Count}.",
            executions.Count
        );
    }

    private static int ReadPositiveInt(string? value, int fallback)
    {
        return int.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;
    }
}
