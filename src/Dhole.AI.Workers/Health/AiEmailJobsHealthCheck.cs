using CustomCodeFramework.Messaging.Inbox;
using CustomCodeFramework.Messaging.Outbox;
using Dhole.AI.Domain.EmailAnalysis.Enums;
using Dhole.AI.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Dhole.AI.Worker.Health;

internal sealed class AiEmailJobsHealthCheck(
    ServiceDbContext dbContext,
    IConfiguration configuration
) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    )
    {
        var statuses = await dbContext.AiEmailAnalysisJobs
            .AsNoTracking()
            .GroupBy(job => job.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToDictionaryAsync(
                item => item.Status,
                item => item.Count,
                cancellationToken
            );
        var now = DateTime.UtcNow;
        var staleJobs = await dbContext.AiEmailAnalysisJobs
            .AsNoTracking()
            .CountAsync(
                job =>
                    job.Status == AiEmailAnalysisJobStatus.Processing
                    && job.LeaseExpiresAtUtc.HasValue
                    && job.LeaseExpiresAtUtc.Value < now,
                cancellationToken
            );
        var outboxPending = await dbContext.OutboxMessages.CountAsync(
            message => message.Status == OutboxMessageStatus.Pending,
            cancellationToken
        );
        var outboxFailed = await dbContext.OutboxMessages.CountAsync(
            message => message.Status == OutboxMessageStatus.Failed,
            cancellationToken
        );
        var inboxPending = await dbContext.InboxMessages.CountAsync(
            message => message.Status == InboxMessageStatus.Pending,
            cancellationToken
        );
        var inboxFailed = await dbContext.InboxMessages.CountAsync(
            message => message.Status == InboxMessageStatus.Failed,
            cancellationToken
        );
        var profileKey = configuration["AI:EmailJobs:ProfileKey"];
        if (string.IsNullOrWhiteSpace(profileKey))
        {
            profileKey = "pricing-email-analysis";
        }

        var profileActive = await dbContext.AiProfiles
            .AsNoTracking()
            .AnyAsync(
                profile =>
                    !profile.IsDeleted
                    && profile.IsActive
                    && profile.Key == profileKey.Trim().ToLowerInvariant(),
                cancellationToken
            );
        var backlog =
            Get(statuses, AiEmailAnalysisJobStatus.Pending)
            + Get(statuses, AiEmailAnalysisJobStatus.RetryScheduled);
        var warningThreshold = ReadPositiveInt(
            configuration[
                "Monitoring:AsyncEmail:BacklogWarningThreshold"
            ],
            50
        );
        var data = new Dictionary<string, object>
        {
            ["ai_email_jobs_pending"] = Get(
                statuses,
                AiEmailAnalysisJobStatus.Pending
            ),
            ["ai_email_jobs_retry_scheduled"] = Get(
                statuses,
                AiEmailAnalysisJobStatus.RetryScheduled
            ),
            ["ai_email_jobs_processing"] = Get(
                statuses,
                AiEmailAnalysisJobStatus.Processing
            ),
            ["ai_email_jobs_failed"] = Get(
                statuses,
                AiEmailAnalysisJobStatus.Failed
            ),
            ["ai_email_jobs_without_heartbeat"] = staleJobs,
            ["outbox_pending"] = outboxPending,
            ["outbox_failed"] = outboxFailed,
            ["inbox_pending"] = inboxPending,
            ["inbox_failed"] = inboxFailed,
            ["pricing_email_analysis_profile_active"] = profileActive,
        };

        return !profileActive
            || staleJobs > 0
            || outboxFailed > 0
            || inboxFailed > 0
            || backlog > warningThreshold
            || outboxPending > warningThreshold
            || inboxPending > warningThreshold
            ? HealthCheckResult.Degraded(
                "AI tiene backlog de correos o trabajos sin heartbeat.",
                data: data
            )
            : HealthCheckResult.Healthy(
                "Los trabajos AI de correo están operativos.",
                data
            );
    }

    private static int Get(
        IReadOnlyDictionary<AiEmailAnalysisJobStatus, int> values,
        AiEmailAnalysisJobStatus status
    )
    {
        return values.TryGetValue(status, out var count) ? count : 0;
    }

    private static int ReadPositiveInt(string? value, int fallback)
    {
        return int.TryParse(value, out var parsed) && parsed > 0
            ? parsed
            : fallback;
    }
}
