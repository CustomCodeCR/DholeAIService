using Dhole.AI.Api.Authorization;
using Dhole.AI.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.AI.Api.Endpoints;

/// <summary>
/// F6.11: owner-checked view permission, no AI input, responses, prompts,
/// original emails or connection credentials leave this endpoint.
/// </summary>
public static class RealtimeAccessEndpoints
{
    public static IEndpointRouteBuilder MapRealtimeAccessEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ai/realtime-access")
            .WithTags("Realtime Access").RequireAuthorization();

        // An ACL-only GET is insufficient for reconnecting browsers: provide a
        // narrowly scoped authoritative status snapshot without email, model
        // output, payload URL, request hash or correlation metadata.
        group.MapGet("/emailjob/{id:guid}", async (Guid id, ServiceDbContext db, CancellationToken ct) =>
        {
            var snapshot = await db.AiEmailAnalysisJobs.AsNoTracking()
                .Where(x => x.Id == id)
                .Select(x => new
                {
                    x.Id,
                    x.Status,
                    x.Version,
                    x.AttemptCount,
                    x.MaxAttemptCount,
                    x.NextAttemptAtUtc,
                    x.StartedAtUtc,
                    x.CompletedAtUtc,
                    x.AiExecutionId
                }).FirstOrDefaultAsync(ct);
            return snapshot is null ? Results.NotFound() : Results.Ok(snapshot);
        }).RequireScope(AiScopeNames.ExecutionView);
        return app;
    }
}
