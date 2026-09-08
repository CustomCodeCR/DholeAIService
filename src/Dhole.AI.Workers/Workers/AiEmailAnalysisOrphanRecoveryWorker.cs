using CustomCodeFramework.Workers.Abstractions;
using Dhole.AI.Domain.EmailAnalysis.Enums;
using Dhole.AI.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.AI.Worker.Workers;

/// <summary>
/// Recovers Processing jobs whose heartbeat stopped even when their persisted lease
/// has not reached its original expiration yet. Container restarts used to leave such
/// jobs blocked for the remainder of a 30-minute lease, which kept DataExtraction in
/// "Procesando con AI" with no worker actually owning the request.
/// </summary>
internal sealed class AiEmailAnalysisOrphanRecoveryWorker(
    ServiceDbContext dbContext,
    IConfiguration configuration,
    ILogger<AiEmailAnalysisOrphanRecoveryWorker> logger
) : IBackgroundWorker
{
    public string Name => "ai.email-analysis-orphan-recovery";

    public async Task ExecuteAsync(
        IWorkerExecutionContext context,
        CancellationToken cancellationToken
    )
    {
        if (!ReadBoolean(configuration["AI:EmailJobs:Enabled"], true))
        {
            return;
        }

        var graceSeconds = Math.Clamp(
            ReadPositiveInt(
                configuration["AI:EmailJobs:HeartbeatRecoverySeconds"],
                60
            ),
            30,
            600
        );
        var now = DateTime.UtcNow;
        var staleHeartbeatBefore = now.AddSeconds(-graceSeconds);

        var jobs = await dbContext.AiEmailAnalysisJobs
            .Where(job =>
                job.Status == AiEmailAnalysisJobStatus.Processing
                && (!job.LastHeartbeatAtUtc.HasValue
                    || job.LastHeartbeatAtUtc.Value < staleHeartbeatBefore)
            )
            .OrderBy(job => job.LastHeartbeatAtUtc)
            .Take(100)
            .ToListAsync(cancellationToken);

        if (jobs.Count == 0)
        {
            return;
        }

        foreach (var job in jobs)
        {
            job.RecoverExpiredLease(
                "AI.EmailJobHeartbeatStale",
                $"El heartbeat del worker AI superó {graceSeconds} segundos; el trabajo fue reencolado automáticamente.",
                now
            );
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogWarning(
            "Se reencolaron {JobCount} AI jobs sin heartbeat activo antes de que venciera su lease original.",
            jobs.Count
        );
    }

    private static bool ReadBoolean(string? value, bool fallback)
    {
        return bool.TryParse(value, out var parsed) ? parsed : fallback;
    }

    private static int ReadPositiveInt(string? value, int fallback)
    {
        return int.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;
    }
}
