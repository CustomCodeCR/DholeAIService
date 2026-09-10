using System.Diagnostics;
using CustomCodeFramework.Workers.Abstractions;
using Dhole.AI.Domain.EmailAnalysis.Enums;
using Dhole.AI.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.AI.Worker.Workers;

/// <summary>
/// Recovers Processing jobs whose worker disappeared. Besides the normal stale-heartbeat
/// rule, startup recovery immediately requeues jobs whose last heartbeat belongs to the
/// previous worker process. This prevents a container restart from leaving requests stuck
/// in Processing until their persisted lease expires.
/// </summary>
internal sealed class AiEmailAnalysisOrphanRecoveryWorker(
    ServiceDbContext dbContext,
    IConfiguration configuration,
    ILogger<AiEmailAnalysisOrphanRecoveryWorker> logger
) : IBackgroundWorker
{
    private static readonly DateTime ProcessStartedAtUtc =
        Process.GetCurrentProcess().StartTime.ToUniversalTime();

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

        // Use the same grace setting as the main AI email worker. Older deployments used
        // HeartbeatRecoverySeconds, so keep it only as a compatibility fallback. Having two
        // recovery loops with different thresholds can requeue a healthy long-running Ollama
        // request while its heartbeat is still being maintained.
        var configuredGraceSeconds =
            configuration["AI:EmailJobs:LeaseRecoveryGraceSeconds"]
            ?? configuration["AI:EmailJobs:HeartbeatRecoverySeconds"];
        var graceSeconds = Math.Clamp(
            ReadPositiveInt(configuredGraceSeconds, 120),
            30,
            600
        );
        var now = DateTime.UtcNow;
        var staleHeartbeatBefore = now.AddSeconds(-graceSeconds);
        // Leave one second of clock/precision tolerance. Any heartbeat older than this
        // cannot have been emitted by the current worker process.
        var previousProcessHeartbeatBefore = ProcessStartedAtUtc.AddSeconds(-1);

        var jobs = await dbContext.AiEmailAnalysisJobs
            .Where(job =>
                job.Status == AiEmailAnalysisJobStatus.Processing
                && (
                    (job.LastHeartbeatAtUtc.HasValue
                        && (
                            job.LastHeartbeatAtUtc.Value < staleHeartbeatBefore
                            || job.LastHeartbeatAtUtc.Value < previousProcessHeartbeatBefore
                        ))
                    || (
                        !job.LastHeartbeatAtUtc.HasValue
                        && job.StartedAtUtc.HasValue
                        && job.StartedAtUtc.Value < previousProcessHeartbeatBefore
                    )
                )
            )
            .OrderBy(job => job.LastHeartbeatAtUtc)
            .Take(100)
            .ToListAsync(cancellationToken);

        if (jobs.Count == 0)
        {
            return;
        }

        var previousProcessJobs = 0;
        foreach (var job in jobs)
        {
            var belongsToPreviousProcess =
                (job.LastHeartbeatAtUtc.HasValue
                    && job.LastHeartbeatAtUtc.Value < previousProcessHeartbeatBefore)
                || (
                    !job.LastHeartbeatAtUtc.HasValue
                    && job.StartedAtUtc.HasValue
                    && job.StartedAtUtc.Value < previousProcessHeartbeatBefore
                );

            if (belongsToPreviousProcess)
            {
                previousProcessJobs++;
            }

            job.RecoverExpiredLease(
                belongsToPreviousProcess
                    ? "AI.EmailJobPreviousWorkerRecovered"
                    : "AI.EmailJobHeartbeatStale",
                belongsToPreviousProcess
                    ? "El proceso AI anterior terminó o fue reiniciado; el trabajo fue reencolado inmediatamente para continuar la extracción pendiente."
                    : $"El heartbeat del worker AI superó {graceSeconds} segundos; el trabajo fue reencolado automáticamente.",
                now
            );
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogWarning(
            "Se reencolaron {JobCount} AI jobs huérfanos; {PreviousProcessJobCount} pertenecían al proceso anterior.",
            jobs.Count,
            previousProcessJobs
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
