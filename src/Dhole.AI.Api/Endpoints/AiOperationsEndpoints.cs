using System.Net.Http.Json;
using System.Text.Json;
using CustomCodeFramework.Mongo.Abstractions;
using Dhole.AI.Api.Authorization;
using Dhole.AI.Domain.Connections.Enums;
using Dhole.AI.Domain.EmailAnalysis.Enums;
using Dhole.AI.Domain.Executions.Enums;
using Dhole.AI.Domain.Shared;
using Dhole.AI.Infrastructure.Mongo.Documents;
using Dhole.AI.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;
using MongoDB.Driver;
using StackExchange.Redis;

namespace Dhole.AI.Api.Endpoints;

public static class AiOperationsEndpoints
{
    public static IEndpointRouteBuilder MapAiOperationsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ai/operations")
            .WithTags("AI Operations")
            .RequireAuthorization();

        group.MapGet("/state", GetStateAsync)
            .RequireScope(AiConstants.Scopes.ExecutionView);
        group.MapPost("/jobs/{jobId:guid}/cancel", CancelQueuedJobAsync)
            .RequireScope(AiConstants.Scopes.ExecutionCancel);
        group.MapPost("/jobs/{jobId:guid}/retry", RetryFailedJobAsync)
            .RequireScope(AiConstants.Scopes.ExecutionCancel);
        group.MapPost("/redis/pending/clear", ClearRedisPendingAsync)
            .RequireScope(AiConstants.Scopes.ExecutionCancel);
        group.MapPost("/redis/trim", TrimRedisStreamAsync)
            .RequireScope(AiConstants.Scopes.ExecutionCancel);
        group.MapPost("/mongo/execution-snapshots/purge", PurgeMongoSnapshotsAsync)
            .RequireScope(AiConstants.Scopes.ExecutionCancel);
        group.MapPost("/ollama/models/{modelId:guid}/unload", UnloadOllamaModelAsync)
            .RequireScope(AiConstants.Scopes.ExecutionCancel);

        return app;
    }

    private static async Task<IResult> GetStateAsync(
        int? take,
        ServiceDbContext dbContext,
        IConnectionMultiplexer redis,
        IMongoContext mongoContext,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(take ?? 100, 1, 500);

        var counts = await dbContext.AiEmailAnalysisJobs
            .AsNoTracking()
            .GroupBy(job => job.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);
        var countLookup = counts.ToDictionary(item => item.Status, item => item.Count);

        var jobs = await dbContext.AiEmailAnalysisJobs
            .AsNoTracking()
            .Where(job =>
                job.Status == AiEmailAnalysisJobStatus.Pending
                || job.Status == AiEmailAnalysisJobStatus.Processing
                || job.Status == AiEmailAnalysisJobStatus.RetryScheduled
                || job.Status == AiEmailAnalysisJobStatus.Failed)
            .OrderByDescending(job => job.StartedAtUtc ?? job.NextAttemptAtUtc)
            .Take(limit)
            .ToListAsync(cancellationToken);

        var correlations = jobs.Select(job => job.CorrelationId)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var executions = correlations.Length == 0
            ? new List<Dhole.AI.Domain.Executions.Entities.AiExecution>()
            : await dbContext.AiExecutions
                .AsNoTracking()
                .Include(execution => execution.Attempts)
                .Where(execution => execution.CorrelationId != null
                    && correlations.Contains(execution.CorrelationId))
                .OrderByDescending(execution => execution.StartedAtUtc)
                .Take(Math.Min(1000, limit * 6))
                .ToListAsync(cancellationToken);

        var modelIds = executions.SelectMany(item => item.Attempts)
            .Select(item => item.ModelId)
            .Distinct()
            .ToArray();
        var modelNames = modelIds.Length == 0
            ? new Dictionary<Guid, string>()
            : await dbContext.AiModels.AsNoTracking()
                .Where(model => modelIds.Contains(model.Id))
                .ToDictionaryAsync(model => model.Id, model => model.Name, cancellationToken);

        var connectionIds = executions.SelectMany(item => item.Attempts)
            .Select(item => item.ConnectionId)
            .Distinct()
            .ToArray();
        var connectionNames = connectionIds.Length == 0
            ? new Dictionary<Guid, string>()
            : await dbContext.AiConnections.AsNoTracking()
                .Where(connection => connectionIds.Contains(connection.Id))
                .ToDictionaryAsync(connection => connection.Id, connection => connection.Name, cancellationToken);

        var queueItems = jobs.Select(job =>
        {
            var related = executions
                .Where(execution => string.Equals(
                    execution.CorrelationId,
                    job.CorrelationId,
                    StringComparison.Ordinal))
                .OrderByDescending(execution => execution.StartedAtUtc)
                .Take(8)
                .Select(execution =>
                {
                    var attempt = execution.Attempts
                        .OrderByDescending(item => item.Status == AiAttemptStatus.Running)
                        .ThenByDescending(item => item.AttemptNumber)
                        .FirstOrDefault();

                    return new
                    {
                        ExecutionId = execution.Id,
                        Status = execution.Status.ToString(),
                        ExecutionType = execution.ExecutionType.ToString(),
                        execution.ProfileKey,
                        execution.RequestHash,
                        execution.StartedAtUtc,
                        execution.CompletedAtUtc,
                        execution.DurationMilliseconds,
                        execution.ErrorCode,
                        execution.ErrorMessage,
                        Attempt = attempt is null ? null : new
                        {
                            AttemptId = attempt.Id,
                            attempt.AttemptNumber,
                            Status = attempt.Status.ToString(),
                            attempt.ModelId,
                            ModelName = modelNames.GetValueOrDefault(attempt.ModelId),
                            attempt.ExternalModelId,
                            attempt.ConnectionId,
                            ConnectionName = connectionNames.GetValueOrDefault(attempt.ConnectionId),
                            ProviderType = attempt.ProviderType.ToString(),
                            attempt.StartedAtUtc,
                            attempt.CompletedAtUtc,
                            attempt.DurationMilliseconds,
                            attempt.ErrorCode,
                            attempt.ErrorMessage,
                        },
                    };
                })
                .ToArray();

            return new
            {
                JobId = job.Id,
                RequestId = job.ExternalRequestId,
                job.EmailExtractionJobId,
                job.EmailMessageId,
                job.EmailAttachmentId,
                job.CorrelationId,
                job.RequestHash,
                Status = job.Status.ToString(),
                job.AttemptCount,
                job.MaxAttemptCount,
                job.NextAttemptAtUtc,
                job.LeaseOwner,
                job.LeaseExpiresAtUtc,
                job.LastHeartbeatAtUtc,
                job.AiExecutionId,
                job.StartedAtUtc,
                job.CompletedAtUtc,
                job.ErrorCode,
                job.ErrorMessage,
                Executions = related,
            };
        }).ToArray();

        return Results.Ok(new
        {
            GeneratedAtUtc = DateTime.UtcNow,
            Queue = new
            {
                Pending = countLookup.GetValueOrDefault(AiEmailAnalysisJobStatus.Pending),
                Processing = countLookup.GetValueOrDefault(AiEmailAnalysisJobStatus.Processing),
                RetryScheduled = countLookup.GetValueOrDefault(AiEmailAnalysisJobStatus.RetryScheduled),
                Failed = countLookup.GetValueOrDefault(AiEmailAnalysisJobStatus.Failed),
                Completed = countLookup.GetValueOrDefault(AiEmailAnalysisJobStatus.Completed),
                Items = queueItems,
            },
            Redis = await ReadRedisStateAsync(redis, configuration),
            Mongo = await ReadMongoStateAsync(mongoContext, cancellationToken),
            Ollama = await ReadOllamaStateAsync(dbContext, httpClientFactory, cancellationToken),
        });
    }

    private static async Task<IResult> CancelQueuedJobAsync(
        Guid jobId,
        ServiceDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var job = await dbContext.AiEmailAnalysisJobs
            .SingleOrDefaultAsync(item => item.Id == jobId, cancellationToken);
        if (job is null)
        {
            return Results.NotFound(new { message = "El trabajo de AI no existe." });
        }

        if (job.Status is not AiEmailAnalysisJobStatus.Pending
            and not AiEmailAnalysisJobStatus.RetryScheduled)
        {
            return Results.Conflict(new
            {
                message = job.Status == AiEmailAnalysisJobStatus.Processing
                    ? "El trabajo ya está dentro del proveedor. Use la operación del modelo Ollama para descargarlo o reinicie el proveedor/worker."
                    : "El trabajo ya fue finalizado.",
                status = job.Status.ToString(),
            });
        }

        job.MarkFailed("AI.ManuallyCancelled", "El trabajo fue retirado manualmente de la cola de AI.");
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Ok(new { jobId, status = job.Status.ToString() });
    }

    private static async Task<IResult> RetryFailedJobAsync(
        Guid jobId,
        ServiceDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var updated = await dbContext.AiEmailAnalysisJobs
            .Where(job => job.Id == jobId && job.Status == AiEmailAnalysisJobStatus.Failed)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(job => job.Status, AiEmailAnalysisJobStatus.Pending)
                .SetProperty(job => job.AttemptCount, 0)
                .SetProperty(job => job.NextAttemptAtUtc, (DateTime?)null)
                .SetProperty(job => job.LeaseOwner, (string?)null)
                .SetProperty(job => job.LeaseExpiresAtUtc, (DateTime?)null)
                .SetProperty(job => job.LastHeartbeatAtUtc, (DateTime?)null)
                .SetProperty(job => job.CompletedAtUtc, (DateTime?)null)
                .SetProperty(job => job.ErrorCode, (string?)null)
                .SetProperty(job => job.ErrorMessage, (string?)null),
                cancellationToken);

        return updated == 0
            ? Results.Conflict(new { message = "Solo se puede reintentar un trabajo Failed." })
            : Results.Ok(new { jobId, status = "Pending" });
    }

    private static async Task<IResult> ClearRedisPendingAsync(
        ClearRedisPendingRequest request,
        IConnectionMultiplexer redis,
        IConfiguration configuration)
    {
        var stream = Setting(configuration["Redis:Streams:Consumer:StreamName"], "dhole.ai.events");
        var group = Setting(configuration["Redis:Streams:Consumer:ConsumerGroup"], "dhole-ai");
        var consumer = Setting(configuration["Redis:Streams:Consumer:ConsumerName"], "dhole-ai-worker");
        var maximum = Math.Clamp(request.MaximumMessages ?? 2000, 1, 10000);
        var minimumIdleMs = TimeSpan.FromSeconds(Math.Max(0, request.OlderThanSeconds ?? 3600)).TotalMilliseconds;
        var database = redis.GetDatabase();
        var pending = await database.StreamPendingMessagesAsync(stream, group, maximum, consumer);
        var ids = pending
            .Where(item => item.IdleTimeInMilliseconds >= minimumIdleMs)
            .Select(item => item.MessageId)
            .ToArray();

        if (ids.Length == 0)
        {
            return Results.Ok(new { acknowledged = 0L, deleted = 0L });
        }

        var acknowledged = await database.StreamAcknowledgeAsync(stream, group, ids);
        var deleted = request.DeleteMessages ? await database.StreamDeleteAsync(stream, ids) : 0L;
        return Results.Ok(new { acknowledged, deleted });
    }

    private static async Task<IResult> TrimRedisStreamAsync(
        TrimRedisStreamRequest request,
        IConnectionMultiplexer redis,
        IConfiguration configuration)
    {
        var stream = Setting(configuration["Redis:Streams:Consumer:StreamName"], "dhole.ai.events");
        var maxLength = Math.Clamp(request.MaxLength ?? 10000, 0, 1000000);
        var removed = await redis.GetDatabase()
            .StreamTrimAsync(stream, maxLength, useApproximateMaxLength: true);
        return Results.Ok(new { stream, maxLength, removed });
    }

    private static async Task<IResult> PurgeMongoSnapshotsAsync(
        PurgeMongoSnapshotsRequest request,
        IMongoContext mongoContext,
        CancellationToken cancellationToken)
    {
        var collection = mongoContext.GetCollection<AiExecutionSnapshotDocument>();
        FilterDefinition<AiExecutionSnapshotDocument> filter;
        if (request.DeleteAll)
        {
            if (!string.Equals(request.Confirmation, "PURGE AI HISTORY", StringComparison.Ordinal))
            {
                return Results.BadRequest(new
                {
                    message = "Para borrar todo use Confirmation='PURGE AI HISTORY'.",
                });
            }
            filter = Builders<AiExecutionSnapshotDocument>.Filter.Empty;
        }
        else
        {
            var before = DateTime.UtcNow.AddDays(-Math.Clamp(request.OlderThanDays ?? 90, 1, 3650));
            filter = Builders<AiExecutionSnapshotDocument>.Filter.Lt(item => item.OccurredAtUtc, before);
        }

        var result = await collection.DeleteManyAsync(filter, cancellationToken);
        return Results.Ok(new { deleted = result.DeletedCount });
    }

    private static async Task<IResult> UnloadOllamaModelAsync(
        Guid modelId,
        ServiceDbContext dbContext,
        IHttpClientFactory httpClientFactory,
        CancellationToken cancellationToken)
    {
        var model = await dbContext.AiModels.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == modelId, cancellationToken);
        if (model is null)
        {
            return Results.NotFound(new { message = "El modelo no existe." });
        }

        var connection = await dbContext.AiConnections.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == model.ConnectionId, cancellationToken);
        if (connection is null || connection.ProviderType != AiProviderType.Ollama)
        {
            return Results.BadRequest(new { message = "El modelo no pertenece a Ollama." });
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var client = httpClientFactory.CreateClient();
        using var response = await client.PostAsJsonAsync(
            $"{connection.BaseUrl.TrimEnd('/')}/api/generate",
            new { model = model.ExternalModelId, keep_alive = 0, prompt = string.Empty },
            timeout.Token);
        var body = await response.Content.ReadAsStringAsync(timeout.Token);
        if (!response.IsSuccessStatusCode)
        {
            return Results.Problem(body, statusCode: (int)response.StatusCode, title: "Ollama no pudo descargar el modelo.");
        }

        return Results.Ok(new
        {
            modelId,
            model = model.ExternalModelId,
            connection = connection.Name,
            unloaded = true,
        });
    }

    private static async Task<object> ReadRedisStateAsync(
        IConnectionMultiplexer redis,
        IConfiguration configuration)
    {
        var stream = Setting(configuration["Redis:Streams:Consumer:StreamName"], "dhole.ai.events");
        var groupName = Setting(configuration["Redis:Streams:Consumer:ConsumerGroup"], "dhole-ai");
        try
        {
            var database = redis.GetDatabase();
            var length = await database.StreamLengthAsync(stream);
            var groups = await database.StreamGroupInfoAsync(stream);
            var group = groups.FirstOrDefault(item => item.Name == groupName);
            var hasGroup = group is not null && !string.IsNullOrEmpty(group.Name);
            var consumers = hasGroup
                ? await database.StreamConsumerInfoAsync(stream, groupName)
                : [];

            return new
            {
                Available = true,
                Stream = stream,
                Length = length,
                Group = hasGroup ? new
                {
                    Name = group!.Name,
                    group.ConsumerCount,
                    Pending = group.PendingMessageCount,
                    LastDeliveredId = group.LastDeliveredId.ToString(),
                    group.EntriesRead,
                    group.Lag,
                } : null,
                Consumers = consumers.Select(consumer => new
                {
                    Name = consumer.Name.ToString(),
                    Pending = consumer.PendingMessageCount,
                    IdleMilliseconds = consumer.IdleTimeInMilliseconds,
                }).ToArray(),
            };
        }
        catch (Exception exception)
        {
            return new { Available = false, Stream = stream, Error = exception.GetBaseException().Message };
        }
    }

    private static async Task<object> ReadMongoStateAsync(
        IMongoContext mongoContext,
        CancellationToken cancellationToken)
    {
        try
        {
            var collection = mongoContext.GetCollection<AiExecutionSnapshotDocument>();
            var count = await collection.CountDocumentsAsync(
                Builders<AiExecutionSnapshotDocument>.Filter.Empty,
                cancellationToken: cancellationToken);
            return new { Available = true, ExecutionSnapshots = count };
        }
        catch (Exception exception)
        {
            return new { Available = false, Error = exception.GetBaseException().Message };
        }
    }

    private static async Task<object[]> ReadOllamaStateAsync(
        ServiceDbContext dbContext,
        IHttpClientFactory httpClientFactory,
        CancellationToken cancellationToken)
    {
        var connections = await dbContext.AiConnections.AsNoTracking()
            .Where(connection => connection.ProviderType == AiProviderType.Ollama
                && connection.IsActive
                && !connection.IsDeleted)
            .ToListAsync(cancellationToken);
        var client = httpClientFactory.CreateClient();
        var output = new List<object>();

        foreach (var connection in connections)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                using var response = await client.GetAsync(
                    $"{connection.BaseUrl.TrimEnd('/')}/api/ps",
                    timeout.Token);
                var body = await response.Content.ReadAsStringAsync(timeout.Token);
                if (!response.IsSuccessStatusCode)
                {
                    output.Add(new
                    {
                        connection.Id,
                        connection.Name,
                        connection.BaseUrl,
                        Available = false,
                        Error = body,
                    });
                    continue;
                }

                using var document = JsonDocument.Parse(body);
                var models = document.RootElement.TryGetProperty("models", out var array)
                    && array.ValueKind == JsonValueKind.Array
                    ? array.EnumerateArray().Select(item => new
                    {
                        Name = String(item, "name"),
                        Model = String(item, "model"),
                        Size = Long(item, "size"),
                        SizeVram = Long(item, "size_vram"),
                        ExpiresAt = String(item, "expires_at"),
                    }).ToArray()
                    : [];

                output.Add(new
                {
                    connection.Id,
                    connection.Name,
                    connection.BaseUrl,
                    Available = true,
                    Models = models,
                });
            }
            catch (Exception exception)
            {
                output.Add(new
                {
                    connection.Id,
                    connection.Name,
                    connection.BaseUrl,
                    Available = false,
                    Error = exception.GetBaseException().Message,
                });
            }
        }

        return output.ToArray();
    }

    private static string? String(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static long? Long(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.TryGetInt64(out var parsed)
            ? parsed
            : null;

    private static string Setting(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    public sealed record ClearRedisPendingRequest(
        int? OlderThanSeconds = 3600,
        int? MaximumMessages = 2000,
        bool DeleteMessages = true);

    public sealed record TrimRedisStreamRequest(long? MaxLength = 10000);

    public sealed record PurgeMongoSnapshotsRequest(
        int? OlderThanDays = 90,
        bool DeleteAll = false,
        string? Confirmation = null);
}
