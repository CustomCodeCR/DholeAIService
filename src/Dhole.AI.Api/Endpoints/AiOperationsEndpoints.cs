using System.Net.Http.Json;
using System.Text.Json;
using CustomCodeFramework.Mongo.Abstractions;
using Dhole.AI.Api.Authorization;
using Dhole.AI.Infrastructure.Mongo.Documents;
using Dhole.AI.Domain.Connections.Enums;
using Dhole.AI.Domain.EmailAnalysis.Enums;
using Dhole.AI.Domain.Executions.Enums;
using Dhole.AI.Domain.Shared;
using Dhole.AI.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;
using MongoDB.Driver;
using StackExchange.Redis;

namespace Dhole.AI.Api.Endpoints;

public static class AiOperationsEndpoints
{
    private const int DefaultQueueTake = 100;
    private const int MaximumQueueTake = 500;

    public static IEndpointRouteBuilder MapAiOperationsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/ai/operations")
            .WithTags("AI Operations")
            .RequireAuthorization();

        group
            .MapGet("/state", GetStateAsync)
            .RequireScope(AiConstants.Scopes.ExecutionView);

        group
            .MapPost("/jobs/{jobId:guid}/cancel", CancelQueuedJobAsync)
            .RequireScope(AiConstants.Scopes.ExecutionCancel);

        group
            .MapPost("/jobs/{jobId:guid}/retry", RetryFailedJobAsync)
            .RequireScope(AiConstants.Scopes.ExecutionCancel);

        group
            .MapPost("/redis/pending/clear", ClearRedisPendingAsync)
            .RequireScope(AiConstants.Scopes.ExecutionCancel);

        group
            .MapPost("/redis/trim", TrimRedisStreamAsync)
            .RequireScope(AiConstants.Scopes.ExecutionCancel);

        group
            .MapPost("/mongo/execution-snapshots/purge", PurgeMongoSnapshotsAsync)
            .RequireScope(AiConstants.Scopes.ExecutionCancel);

        group
            .MapPost("/ollama/models/{modelId:guid}/unload", UnloadOllamaModelAsync)
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
        CancellationToken cancellationToken
    )
    {
        var limit = Math.Clamp(take ?? DefaultQueueTake, 1, MaximumQueueTake);

        var statusCounts = await dbContext.AiEmailAnalysisJobs
            .AsNoTracking()
            .GroupBy(job => job.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        var jobs = await dbContext.AiEmailAnalysisJobs
            .AsNoTracking()
            .Where(job =>
                job.Status == AiEmailAnalysisJobStatus.Pending
                || job.Status == AiEmailAnalysisJobStatus.Processing
                || job.Status == AiEmailAnalysisJobStatus.RetryScheduled
                || job.Status == AiEmailAnalysisJobStatus.Failed
            )
            .OrderByDescending(job => job.StartedAtUtc ?? job.NextAttemptAtUtc)
            .Take(limit)
            .ToListAsync(cancellationToken);

        var correlationIds = jobs
            .Select(job => job.CorrelationId)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var executions = correlationIds.Length == 0
            ? []
            : await dbContext.AiExecutions
                .AsNoTracking()
                .Include(execution => execution.Attempts)
                .Where(execution =>
                    execution.CorrelationId != null
                    && correlationIds.Contains(execution.CorrelationId)
                )
                .OrderByDescending(execution => execution.StartedAtUtc)
                .Take(Math.Min(1000, limit * 6))
                .ToListAsync(cancellationToken);

        var modelIds = executions
            .SelectMany(execution => execution.Attempts)
            .Select(attempt => attempt.ModelId)
            .Distinct()
            .ToArray();

        var modelNames = modelIds.Length == 0
            ? new Dictionary<Guid, string>()
            : await dbContext.AiModels
                .AsNoTracking()
                .Where(model => modelIds.Contains(model.Id))
                .ToDictionaryAsync(model => model.Id, model => model.Name, cancellationToken);

        var connectionIds = executions
            .SelectMany(execution => execution.Attempts)
            .Select(attempt => attempt.ConnectionId)
            .Distinct()
            .ToArray();

        var connectionNames = connectionIds.Length == 0
            ? new Dictionary<Guid, string>()
            : await dbContext.AiConnections
                .AsNoTracking()
                .Where(connection => connectionIds.Contains(connection.Id))
                .ToDictionaryAsync(
                    connection => connection.Id,
                    connection => connection.Name,
                    cancellationToken
                );

        var executionLookup = executions
            .Where(execution => !string.IsNullOrWhiteSpace(execution.CorrelationId))
            .GroupBy(execution => execution.CorrelationId!, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(execution => execution.StartedAtUtc)
                    .Take(6)
                    .Select(execution =>
                    {
                        var attempt = execution.Attempts
                            .OrderByDescending(item => item.Status == AiAttemptStatus.Running)
                            .ThenByDescending(item => item.AttemptNumber)
                            .FirstOrDefault();

                        return new
                        {
                            execution.Id,
                            Status = execution.Status.ToString(),
                            ExecutionType = execution.ExecutionType.ToString(),
                            execution.ProfileKey,
                            execution.RequestHash,
                            execution.StartedAtUtc,
                            execution.CompletedAtUtc,
                            execution.DurationMilliseconds,
                            execution.ErrorCode,
                            execution.ErrorMessage,
                            Attempt = attempt is null
                                ? null
                                : new
                                {
                                    attempt.Id,
                                    attempt.AttemptNumber,
                                    Status = attempt.Status.ToString(),
                                    attempt.ModelId,
                                    ModelName = modelNames.GetValueOrDefault(attempt.ModelId),
                                    attempt.ExternalModelId,
                                    attempt.ConnectionId,
                                    ConnectionName = connectionNames.GetValueOrDefault(
                                        attempt.ConnectionId
                                    ),
                                    ProviderType = attempt.ProviderType.ToString(),
                                    attempt.StartedAtUtc,
                                    attempt.CompletedAtUtc,
                                    attempt.DurationMilliseconds,
                                    attempt.ErrorCode,
                                    attempt.ErrorMessage,
                                },
                        };
                    })
                    .ToArray(),
                StringComparer.Ordinal
            );

        var queueItems = jobs.Select(job => new
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
            Executions = executionLookup.GetValueOrDefault(job.CorrelationId) ?? [],
        });

        var redisState = await ReadRedisStateAsync(redis, configuration);
        var mongoState = await ReadMongoStateAsync(mongoContext, cancellationToken);
        var ollamaState = await ReadOllamaStateAsync(
            dbContext,
            httpClientFactory,
            cancellationToken
        );

        return Results.Ok(new
        {
            GeneratedAtUtc = DateTime.UtcNow,
            Queue = new
            {
                Pending = statusCounts
                    .Where(item => item.Status == AiEmailAnalysisJobStatus.Pending)
                    .Sum(item => item.Count),
                Processing = statusCounts
                    .Where(item => item.Status == AiEmailAnalysisJobStatus.Processing)
                    .Sum(item => item.Count),
                RetryScheduled = statusCounts
                    .Where(item => item.Status == AiEmailAnalysisJobStatus.RetryScheduled)
                    .Sum(item => item.Count),
                Failed = statusCounts
                    .Where(item => item.Status == AiEmailAnalysisJobStatus.Failed)
                    .Sum(item => item.Count),
                Completed = statusCounts
                    .Where(item => item.Status == AiEmailAnalysisJobStatus.Completed)
                    .Sum(item => item.Count),
                Items = queueItems,
            },
            Redis = redisState,
            Mongo = mongoState,
            Ollama = ollamaState,
        });
    }

    private static async Task<IResult> CancelQueuedJobAsync(
        Guid jobId,
        ServiceDbContext dbContext,
        CancellationToken cancellationToken
    )
    {
        var job = await dbContext.AiEmailAnalysisJobs
            .SingleOrDefaultAsync(item => item.Id == jobId, cancellationToken);

        if (job is null)
        {
            return Results.NotFound(new { message = "El trabajo de AI no existe." });
        }

        if (
            job.Status
            is not AiEmailAnalysisJobStatus.Pending
                and not AiEmailAnalysisJobStatus.RetryScheduled
        )
        {
            return Results.Conflict(
                new
                {
                    message = job.Status == AiEmailAnalysisJobStatus.Processing
                        ? "La ejecución ya está dentro del proveedor. Descargue el modelo de Ollama o reinicie el worker/proveedor para interrumpirla; este endpoint solo cancela trabajos que aún no fueron enviados."
                        : "El trabajo ya fue finalizado.",
                    status = job.Status.ToString(),
                }
            );
        }

        job.MarkFailed(
            "AI.ManuallyCancelled",
            "El trabajo fue retirado manualmente de la cola de AI."
        );
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Ok(new { jobId, status = job.Status.ToString() });
    }

    private static async Task<IResult> RetryFailedJobAsync(
        Guid jobId,
        ServiceDbContext dbContext,
        CancellationToken cancellationToken
    )
    {
        var updated = await dbContext.AiEmailAnalysisJobs
            .Where(job => job.Id == jobId && job.Status == AiEmailAnalysisJobStatus.Failed)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(job => job.Status, AiEmailAnalysisJobStatus.Pending)
                    .SetProperty(job => job.AttemptCount, 0)
                    .SetProperty(job => job.NextAttemptAtUtc, (DateTime?)null)
                    .SetProperty(job => job.LeaseOwner, (string?)null)
                    .SetProperty(job => job.LeaseExpiresAtUtc, (DateTime?)null)
                    .SetProperty(job => job.LastHeartbeatAtUtc, (DateTime?)null)
                    .SetProperty(job => job.CompletedAtUtc, (DateTime?)null)
                    .SetProperty(job => job.ErrorCode, (string?)null)
                    .SetProperty(job => job.ErrorMessage, (string?)null),
                cancellationToken
            );

        return updated == 0
            ? Results.Conflict(
                new { message = "Solo se puede reintentar un trabajo en estado Failed." }
            )
            : Results.Ok(new { jobId, status = AiEmailAnalysisJobStatus.Pending.ToString() });
    }

    private static async Task<IResult> ClearRedisPendingAsync(
        ClearRedisPendingRequest request,
        IConnectionMultiplexer redis,
        IConfiguration configuration
    )
    {
        var stream = ReadSetting(
            configuration["Redis:Streams:Consumer:StreamName"],
            "dhole.ai.events"
        );
        var group = ReadSetting(
            configuration["Redis:Streams:Consumer:ConsumerGroup"],
            "dhole-ai"
        );
        var consumer = ReadSetting(
            configuration["Redis:Streams:Consumer:ConsumerName"],
            "dhole-ai-worker"
        );
        var minimumIdle = TimeSpan.FromSeconds(Math.Max(0, request.OlderThanSeconds ?? 3600));
        var maximum = Math.Clamp(request.MaximumMessages ?? 2000, 1, 10000);

        var database = redis.GetDatabase();
        var pending = await database.StreamPendingMessagesAsync(
            stream,
            group,
            maximum,
            consumer
        );
        var ids = pending
            .Where(item => item.IdleTimeInMilliseconds >= minimumIdle.TotalMilliseconds)
            .Select(item => item.MessageId)
            .ToArray();

        if (ids.Length == 0)
        {
            return Results.Ok(new { acknowledged = 0, deleted = 0 });
        }

        var acknowledged = await database.StreamAcknowledgeAsync(stream, group, ids);
        var deleted = request.DeleteMessages
            ? await database.StreamDeleteAsync(stream, ids)
            : 0;

        return Results.Ok(new { acknowledged, deleted });
    }

    private static async Task<IResult> TrimRedisStreamAsync(
        TrimRedisStreamRequest request,
        IConnectionMultiplexer redis,
        IConfiguration configuration
    )
    {
        var stream = ReadSetting(
            configuration["Redis:Streams:Consumer:StreamName"],
            "dhole.ai.events"
        );
        var maxLength = Math.Clamp(request.MaxLength ?? 10000, 0, 1000000);
        var database = redis.GetDatabase();
        var removed = await database.StreamTrimAsync(stream, maxLength, useApproximateMaxLength: true);

        return Results.Ok(new { stream, maxLength, removed });
    }

    private static async Task<IResult> PurgeMongoSnapshotsAsync(
        PurgeMongoSnapshotsRequest request,
        IMongoContext mongoContext,
        CancellationToken cancellationToken
    )
    {
        var collection = mongoContext.GetCollection<AiExecutionSnapshotDocument>();
        FilterDefinition<AiExecutionSnapshotDocument> filter;

        if (request.DeleteAll)
        {
            if (!string.Equals(request.Confirmation, "PURGE AI HISTORY", StringComparison.Ordinal))
            {
                return Results.BadRequest(
                    new { message = "Para borrar todo el historial use Confirmation='PURGE AI HISTORY'." }
                );
            }

            filter = Builders<AiExecutionSnapshotDocument>.Filter.Empty;
        }
        else
        {
            var olderThanDays = Math.Clamp(request.OlderThanDays ?? 90, 1, 3650);
            var beforeUtc = DateTime.UtcNow.AddDays(-olderThanDays);
            filter = Builders<AiExecutionSnapshotDocument>.Filter.Lt(
                document => document.OccurredAtUtc,
                beforeUtc
            );
        }

        var result = await collection.DeleteManyAsync(filter, cancellationToken);
        return Results.Ok(new { deleted = result.DeletedCount });
    }

    private static async Task<IResult> UnloadOllamaModelAsync(
        Guid modelId,
        ServiceDbContext dbContext,
        IHttpClientFactory httpClientFactory,
        CancellationToken cancellationToken
    )
    {
        var model = await dbContext.AiModels
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == modelId, cancellationToken);
        if (model is null)
        {
            return Results.NotFound(new { message = "El modelo no existe." });
        }

        var connection = await dbContext.AiConnections
            .AsNoTracking()
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
            timeout.Token
        );

        var body = await response.Content.ReadAsStringAsync(timeout.Token);
        if (!response.IsSuccessStatusCode)
        {
            return Results.Problem(
                detail: body,
                statusCode: (int)response.StatusCode,
                title: "Ollama no pudo descargar el modelo."
            );
        }

        return Results.Ok(
            new
            {
                modelId,
                model = model.ExternalModelId,
                connection = connection.Name,
                unloaded = true,
            }
        );
    }

    private static async Task<object> ReadRedisStateAsync(
        IConnectionMultiplexer redis,
        IConfiguration configuration
    )
    {
        var stream = ReadSetting(
            configuration["Redis:Streams:Consumer:StreamName"],
            "dhole.ai.events"
        );
        var groupName = ReadSetting(
            configuration["Redis:Streams:Consumer:ConsumerGroup"],
            "dhole-ai"
        );

        try
        {
            var database = redis.GetDatabase();
            var length = await database.StreamLengthAsync(stream);
            var groups = await database.StreamGroupInfoAsync(stream);
            var group = groups.FirstOrDefault(item => item.Name == groupName);
            var consumers = group.Name.IsNullOrEmpty
                ? []
                : await database.StreamConsumerInfoAsync(stream, groupName);

            return new
            {
                Available = true,
                Stream = stream,
                Length = length,
                Group = group.Name.IsNullOrEmpty
                    ? null
                    : new
                    {
                        Name = group.Name.ToString(),
                        group.ConsumerCount,
                        Pending = group.PendingMessageCount,
                        LastDeliveredId = group.LastDeliveredId.ToString(),
                        group.EntriesRead,
                        group.Lag,
                    },
                Consumers = consumers.Select(consumer => new
                {
                    Name = consumer.Name.ToString(),
                    Pending = consumer.PendingMessageCount,
                    IdleMilliseconds = consumer.IdleTimeInMilliseconds,
                }),
            };
        }
        catch (Exception exception)
        {
            return new
            {
                Available = false,
                Stream = stream,
                Error = exception.GetBaseException().Message,
            };
        }
    }

    private static async Task<object> ReadMongoStateAsync(
        IMongoContext mongoContext,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var collection = mongoContext.GetCollection<AiExecutionSnapshotDocument>();
            var count = await collection.CountDocumentsAsync(
                Builders<AiExecutionSnapshotDocument>.Filter.Empty,
                cancellationToken: cancellationToken
            );
            return new { Available = true, ExecutionSnapshots = count };
        }
        catch (Exception exception)
        {
            return new
            {
                Available = false,
                Error = exception.GetBaseException().Message,
            };
        }
    }

    private static async Task<object[]> ReadOllamaStateAsync(
        ServiceDbContext dbContext,
        IHttpClientFactory httpClientFactory,
        CancellationToken cancellationToken
    )
    {
        var connections = await dbContext.AiConnections
            .AsNoTracking()
            .Where(connection =>
                connection.ProviderType == AiProviderType.Ollama
                && connection.IsActive
                && !connection.IsDeleted
            )
            .ToListAsync(cancellationToken);

        var client = httpClientFactory.CreateClient();
        var results = new List<object>();
        foreach (var connection in connections)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                using var response = await client.GetAsync(
                    $"{connection.BaseUrl.TrimEnd('/')}/api/ps",
                    timeout.Token
                );
                var body = await response.Content.ReadAsStringAsync(timeout.Token);
                if (!response.IsSuccessStatusCode)
                {
                    results.Add(
                        new
                        {
                            connection.Id,
                            connection.Name,
                            connection.BaseUrl,
                            Available = false,
                            Error = body,
                        }
                    );
                    continue;
                }

                using var json = JsonDocument.Parse(body);
                var models = json.RootElement.TryGetProperty("models", out var modelArray)
                    && modelArray.ValueKind == JsonValueKind.Array
                    ? modelArray
                        .EnumerateArray()
                        .Select(item => new
                        {
                            Name = ReadString(item, "name"),
                            Model = ReadString(item, "model"),
                            Size = ReadInt64(item, "size"),
                            SizeVram = ReadInt64(item, "size_vram"),
                            ExpiresAt = ReadString(item, "expires_at"),
                        })
                        .ToArray()
                    : [];

                results.Add(
                    new
                    {
                        connection.Id,
                        connection.Name,
                        connection.BaseUrl,
                        Available = true,
                        Models = models,
                    }
                );
            }
            catch (Exception exception)
            {
                results.Add(
                    new
                    {
                        connection.Id,
                        connection.Name,
                        connection.BaseUrl,
                        Available = false,
                        Error = exception.GetBaseException().Message,
                    }
                );
            }
        }

        return results.ToArray();
    }

    private static string? ReadString(JsonElement item, string propertyName)
    {
        return item.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static long? ReadInt64(JsonElement item, string propertyName)
    {
        return item.TryGetProperty(propertyName, out var value) && value.TryGetInt64(out var parsed)
            ? parsed
            : null;
    }

    private static string ReadSetting(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    public sealed record ClearRedisPendingRequest(
        int? OlderThanSeconds = 3600,
        int? MaximumMessages = 2000,
        bool DeleteMessages = true
    );

    public sealed record TrimRedisStreamRequest(long? MaxLength = 10000);

    public sealed record PurgeMongoSnapshotsRequest(
        int? OlderThanDays = 90,
        bool DeleteAll = false,
        string? Confirmation = null
    );
}
