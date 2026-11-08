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

        group.MapGet("/emailjob/{id:guid}", async (Guid id, ServiceDbContext db, CancellationToken ct) =>
        {
            var exists = await db.AiEmailAnalysisJobs.AsNoTracking()
                .AnyAsync(x => x.Id == id, ct);
            return exists ? Results.Ok() : Results.NotFound();
        }).RequireScope(AiScopeNames.ExecutionView);
        return app;
    }
}
