using Dhole.AI.Api.Authorization;
using Dhole.AI.Domain.EmailAnalysis.Enums;
using Dhole.AI.Domain.Shared;
using Dhole.AI.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.AI.Api.Endpoints;

/// <summary>
/// Durable, server-paginated view across ALL email AI jobs, including completed ones.
/// The operational /state endpoint stays responsible for Redis, Mongo and Ollama metrics.
/// </summary>
public static class AiQueueBrowserEndpoints
{
    public static IEndpointRouteBuilder MapAiQueueBrowserEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/ai/operations/jobs/search", BrowseAsync)
            .WithTags("AI Operations")
            .RequireAuthorization()
            .RequireScope(AiConstants.Scopes.ExecutionView);
        return app;
    }

    private static async Task<IResult> BrowseAsync(
        int? pageNumber,
        int? pageSize,
        AiEmailAnalysisJobStatus? status,
        string? search,
        ServiceDbContext db,
        CancellationToken cancellationToken)
    {
        var page = Math.Max(1, pageNumber ?? 1);
        var size = Math.Clamp(pageSize ?? 25, 1, 100);
        var query = db.AiEmailAnalysisJobs.AsNoTracking().AsQueryable();

        if (status.HasValue)
            query = query.Where(x => x.Status == status.Value);

        var term = search?.Trim();
        if (!string.IsNullOrEmpty(term))
        {
            if (Guid.TryParse(term, out var id))
            {
                query = query.Where(x =>
                    x.Id == id || x.ExternalRequestId == id
                    || x.EmailExtractionJobId == id || x.EmailMessageId == id);
            }
            else
            {
                // Correlation IDs are indexed operational identifiers, not private email bodies.
                query = query.Where(x => x.CorrelationId.Contains(term));
            }
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .ThenByDescending(x => x.Id)
            .Skip((page - 1) * size)
            .Take(size)
            .Select(x => new
            {
                jobId = x.Id,
                requestId = x.ExternalRequestId,
                x.EmailExtractionJobId,
                x.EmailMessageId,
                x.EmailAttachmentId,
                x.CorrelationId,
                status = x.Status.ToString(),
                x.AttemptCount,
                x.MaxAttemptCount,
                x.NextAttemptAtUtc,
                x.LeaseOwner,
                x.LeaseExpiresAtUtc,
                x.LastHeartbeatAtUtc,
                x.AiExecutionId,
                x.ErrorCode,
                x.ErrorMessage,
                x.StartedAtUtc,
                x.CompletedAtUtc,
                x.CreatedAtUtc,
            })
            .ToListAsync(cancellationToken);

        return Results.Ok(new
        {
            generatedAtUtc = DateTime.UtcNow,
            pageNumber = page,
            pageSize = size,
            total,
            items,
        });
    }
}
