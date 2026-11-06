using System.Data;
using System.Text.Json;
using CustomCodeFramework.Workers.Abstractions;
using Dhole.AI.Application.Abstractions.Auditing;
using Dhole.AI.Application.Abstractions.Messaging;
using Dhole.AI.Application.Abstractions.Services;
using Dhole.AI.Application.Auditing;
using Dhole.AI.Contracts.Executions.Response;
using Dhole.AI.Domain.EmailAnalysis.Entities;
using Dhole.AI.Domain.EmailAnalysis.Enums;
using Dhole.AI.Domain.Executions.Enums;
using Dhole.AI.Persistence.Auditing;
using Dhole.AI.Persistence.DbContexts;
using Dhole.AI.Worker.EmailAnalysis;
using Microsoft.EntityFrameworkCore;

namespace Dhole.AI.Worker.Workers;

internal sealed class AiEmailAnalysisWorker(
    ServiceDbContext dbContext,
    IDataExtractionAiEmailRequestClient dataExtractionClient,
    IAiExecutionOrchestrator orchestrator,
    IAiAuditService audit,
    IIntegrationEventOutboxWriter outbox,
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<AiEmailAnalysisWorker> logger
) : IBackgroundWorker
{
    private static readonly JsonSerializerOptions JsonOptions = new(
        JsonSerializerDefaults.Web
    )
    {
        PropertyNameCaseInsensitive = true,
    };

    private static DateTime _lastSourceReconciledAtUtc = DateTime.MinValue;

    private readonly string _leaseOwner =
        $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";

    public string Name => "ai.email-analysis";

    public async Task ExecuteAsync(
        IWorkerExecutionContext context,
        CancellationToken cancellationToken
    )
    {
        if (!ReadBoolean(configuration["AI:EmailJobs:Enabled"], true))
        {
            return;
        }

        await PrepareAsync(cancellationToken);
        await ProcessAvailableJobsAsync(cancellationToken);
    }

    internal async Task PrepareAsync(CancellationToken cancellationToken)
    {
        dbContext.ChangeTracker.Clear();
        await ReconcileClosedSourceJobsAsync(cancellationToken);
        await CompleteQueuedJobsWithExistingResultsAsync(cancellationToken);
        await ApplyConfiguredAttemptLimitAsync(cancellationToken);
        await RecoverStaleExecutionsAsync(cancellationToken);
        await RecoverFailedLeaseJobsAsync(cancellationToken);
        await RecoverExpiredLeasesAsync(cancellationToken);
    }

    internal async Task ProcessAvailableJobsAsync(
        CancellationToken cancellationToken,
        int? batchBudget = null
    )
    {
        // The dispatcher owns the global batch budget. Each isolated slot must
        // process its assigned share, not the concurrency cap.
        var maxJobs = Math.Clamp(
            batchBudget ?? ReadPositiveInt(configuration["AI:EmailJobs:MaxJobsPerRun"], 32),
            1,
            256
        );
        for (var index = 0; index < maxJobs; index++)
        {
            dbContext.ChangeTracker.Clear();
            var job = await ClaimNextJobAsync(cancellationToken);
            if (job is null)
            {
                break;
            }

            await ProcessAsync(job, cancellationToken);
        }
    }

    /// <summary>
    /// Reconciles pending, retrying and active processing jobs with the
    /// authoritative ORIGINAL email received time in DataExtraction.
    /// October 2026 jobs are cancelled regardless of when AI enqueued them.
    /// Completed results and historical audit records are never deleted.
    /// </summary>
    private async Task ReconcileClosedSourceJobsAsync(CancellationToken cancellationToken)
    {
        if (DateTime.UtcNow - _lastSourceReconciledAtUtc < TimeSpan.FromSeconds(30))
            return;

        var candidates = await dbContext.AiEmailAnalysisJobs
            .AsNoTracking()
            .Where(job => job.Status == AiEmailAnalysisJobStatus.Pending
                || job.Status == AiEmailAnalysisJobStatus.RetryScheduled
                || job.Status == AiEmailAnalysisJobStatus.Processing)
            .OrderBy(job => job.CreatedAtUtc)
            .Take(1000)
            .Select(job => new { job.Id, job.ExternalRequestId, job.Status })
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            _lastSourceReconciledAtUtc = DateTime.UtcNow;
            return;
        }

        var cancelledOctober = 0;
        var cancelledOther = 0;
        foreach (var batch in candidates.Chunk(250))
        {
            IReadOnlyList<AiEmailReconciliationStatus> statuses;
            try
            {
                statuses = await dataExtractionClient.ReconcileAsync(
                    batch.Select(job => job.ExternalRequestId).Distinct().ToArray(),
                    cancellationToken
                );
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(exception,
                    "Conciliación con DataExtraction fallida; no se eliminan registros " +
                    "sin confirmar la fecha de recepción del correo.");
                return;
            }

            var statusByRequest = statuses.ToDictionary(item => item.RequestId);
            var eligibleIds = batch
                .Where(job => statusByRequest.TryGetValue(job.ExternalRequestId, out var state)
                    && (OctoberEmailSourcePolicy.ShouldClose(state.SourceReceivedAtUtc)
                        || (job.Status != AiEmailAnalysisJobStatus.Processing
                            && state.ShouldCancel)))
                .Select(job => job.Id)
                .ToArray();

            if (eligibleIds.Length == 0)
                continue;

            await dbContext.ExecuteInRetryableTransactionAsync(
                async () =>
                {
                    var locked = await dbContext.AiEmailAnalysisJobs
                        .FromSqlInterpolated($"""
                            SELECT *
                            FROM ai."AiEmailAnalysisJobs"
                            WHERE id = ANY({eligibleIds})
                              AND status IN ('Pending', 'RetryScheduled', 'Processing')
                            ORDER BY created_at_utc
                            FOR UPDATE SKIP LOCKED
                            """)
                        .ToListAsync(cancellationToken);

                    foreach (var job in locked)
                    {
                        if (!statusByRequest.TryGetValue(job.ExternalRequestId, out var state))
                            continue;

                        var october = OctoberEmailSourcePolicy.ShouldClose(
                            state.SourceReceivedAtUtc);
                        if (!october && (job.Status == AiEmailAnalysisJobStatus.Processing
                            || !state.ShouldCancel))
                            continue;

                        if (october)
                        {
                            job.CancelHistoricalMonth();
                            cancelledOctober++;
                        }
                        else if (state.Reason == "HistoricalMonth")
                        {
                            job.CancelHistoricalMonth();
                            cancelledOther++;
                        }
                        else
                        {
                            job.CancelUnavailableSource(
                                "AI.SourceUnavailable",
                                $"La fuente original ya no está activa ({state.Reason}).");
                            cancelledOther++;
                        }
                    }

                    if (locked.Count > 0)
                        await dbContext.SaveChangesAsync(cancellationToken);
                },
                IsolationLevel.ReadCommitted,
                cancellationToken
            );
            dbContext.ChangeTracker.Clear();
        }

        _lastSourceReconciledAtUtc = DateTime.UtcNow;
        if (cancelledOctober > 0 || cancelledOther > 0)
            logger.LogInformation(
                "Conciliación de cola AI: {OctoberCancelled} trabajos originales " +
                "de octubre 2026 cancelados, {OtherCancelled} trabajos " +
                "de fuentes no disponibles; no se borró historial ni se llamó a Ollama.",
                cancelledOctober, cancelledOther);
    }

    /// <summary>
    /// Safely drains historical duplicate queue entries when an identical email
    /// attachment/body already has a completed AI result. Replays the completion
    /// through the transactional outbox; no email or business result is deleted.
    /// </summary>
    private async Task CompleteQueuedJobsWithExistingResultsAsync(
        CancellationToken cancellationToken)
    {
        await dbContext.ExecuteInRetryableTransactionAsync(
            async () =>
            {
                var queued = await dbContext.AiEmailAnalysisJobs
                    .FromSqlRaw(
                        """
                        SELECT *
                        FROM ai."AiEmailAnalysisJobs"
                        WHERE status IN ('Pending', 'RetryScheduled')
                        ORDER BY created_at_utc
                        FOR UPDATE SKIP LOCKED
                        LIMIT 100
                        """
                    )
                    .ToListAsync(cancellationToken);
                if (queued.Count == 0)
                    return;

                var messageIds = queued.Select(job => job.EmailMessageId)
                    .Distinct().ToArray();
                var completed = await dbContext.AiEmailAnalysisJobs
                    .AsNoTracking()
                    .Where(job => messageIds.Contains(job.EmailMessageId)
                        && job.Status == AiEmailAnalysisJobStatus.Completed
                        && job.AiExecutionId.HasValue
                        && job.ResultJson != null)
                    .OrderByDescending(job => job.CompletedAtUtc)
                    .Take(500)
                    .ToListAsync(cancellationToken);

                var recovered = 0;
                foreach (var job in queued)
                {
                    var previous = completed.FirstOrDefault(result =>
                        result.EmailMessageId == job.EmailMessageId
                        && result.EmailAttachmentId == job.EmailAttachmentId
                        && result.RequestHash == job.RequestHash);
                    if (previous is null || !previous.AiExecutionId.HasValue
                        || string.IsNullOrWhiteSpace(previous.ResultJson))
                        continue;

                    ParsedAiPricingEmailResult? parsed;
                    try
                    {
                        parsed = JsonSerializer.Deserialize<ParsedAiPricingEmailResult>(
                            previous.ResultJson, JsonOptions);
                    }
                    catch (JsonException exception)
                    {
                        logger.LogWarning(exception,
                            "Resultado previo no válido para reutilizar AI job {AiJobId}.", job.Id);
                        continue;
                    }
                    if (parsed is null)
                        continue;

                    job.CompleteFromPriorResult(
                        previous.AiExecutionId.Value, previous.ResultJson);
                    var completedEvent = new AiPricingEmailAnalysisCompletedIntegrationEvent(
                        Guid.NewGuid(),
                        job.ExternalRequestId,
                        job.EmailExtractionJobId,
                        job.Id,
                        previous.AiExecutionId.Value,
                        job.CorrelationId,
                        job.RequestHash,
                        parsed.Confidence,
                        parsed.Rows,
                        parsed.Warnings,
                        DateTime.UtcNow
                    );
                    await outbox.WriteAsync(
                        typeof(AiPricingEmailAnalysisCompletedIntegrationEvent).FullName!,
                        EmailAnalysisMessageTypes.Completed,
                        completedEvent,
                        job.CorrelationId,
                        cancellationToken);
                    recovered++;
                }

                if (recovered > 0)
                {
                    await dbContext.SaveChangesAsync(cancellationToken);
                    logger.LogInformation(
                        "Se recuperaron {Count} trabajos AI duplicados utilizando resultados previos, sin llamar a Ollama.",
                        recovered);
                }
            },
            IsolationLevel.ReadCommitted,
            cancellationToken);
        dbContext.ChangeTracker.Clear();
    }

    private async Task ApplyConfiguredAttemptLimitAsync(
        CancellationToken cancellationToken
    )
    {
        var configuredMaximum = ReadPositiveInt(
            configuration["AI:EmailJobs:MaxRetryCount"],
            3
        );

        await dbContext.AiEmailAnalysisJobs
            .Where(job =>
                job.MaxAttemptCount != configuredMaximum
                && job.Status != AiEmailAnalysisJobStatus.Completed
                && (
                    job.Status != AiEmailAnalysisJobStatus.Failed
                    || job.ErrorCode == "AI.EmailJobLeaseExpired"
                )
            )
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    job => job.MaxAttemptCount,
                    configuredMaximum
                ),
                cancellationToken
            );
    }

    private async Task<AiEmailAnalysisJob?> ClaimNextJobAsync(
        CancellationToken cancellationToken
    )
    {
        var now = DateTime.UtcNow;
        var leaseMinutes = ReadPositiveInt(
            configuration["AI:EmailJobs:LeaseMinutes"],
            30
        );
        return await dbContext.ExecuteInRetryableTransactionAsync<AiEmailAnalysisJob?>(
            async () =>
            {
                var job = await dbContext.AiEmailAnalysisJobs
                    .FromSqlInterpolated(
                        $"""
                        SELECT *
                        FROM ai."AiEmailAnalysisJobs"
                        WHERE status IN ('Pending', 'RetryScheduled')
                          AND (next_attempt_at_utc IS NULL OR next_attempt_at_utc <= {now})
                        ORDER BY created_at_utc
                        FOR UPDATE SKIP LOCKED
                        LIMIT 1
                        """
                    )
                    .FirstOrDefaultAsync(cancellationToken);
                if (job is null)
                {
                    return null;
                }

                job.MarkProcessing(_leaseOwner, now.AddMinutes(leaseMinutes));
                var startedEvent = new AiPricingEmailAnalysisStartedIntegrationEvent(
                    Guid.NewGuid(),
                    job.ExternalRequestId,
                    job.EmailExtractionJobId,
                    job.Id,
                    job.CorrelationId,
                    now
                );
                await outbox.WriteAsync(
                    typeof(AiPricingEmailAnalysisStartedIntegrationEvent).FullName!,
                    EmailAnalysisMessageTypes.Started,
                    startedEvent,
                    job.CorrelationId,
                    cancellationToken
                );
                await dbContext.SaveChangesAsync(cancellationToken);
                return job;
            },
            IsolationLevel.ReadCommitted,
            cancellationToken
        );
    }

    private async Task ProcessAsync(
        AiEmailAnalysisJob job,
        CancellationToken cancellationToken
    )
    {
        using var auditScope = Guid.TryParse(job.CorrelationId, out var auditCorrelationId)
            ? AuditExecutionContextAccessor.Begin(
                new AuditExecutionContext(
                    null,
                    "Dhole.AI.Workers",
                    null,
                    "AiEmailAnalysisWorker",
                    auditCorrelationId
                )
            )
            : null;

        using var heartbeatCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var executionCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var heartbeatTask = RenewLeaseLoopAsync(
            job.Id,
            _leaseOwner,
            heartbeatCancellation.Token,
            executionCancellation
        );

        try
        {
            var response = await dataExtractionClient.GetAsync(
                job.PayloadUrl,
                executionCancellation.Token
            );
            ValidatePayload(job, response);
            var payload = response.Payload.Deserialize<AiPricingEmailPayload>(
                JsonOptions
            ) ?? throw new AiEmailJobException(
                "AI.DataExtractionPayloadInvalid",
                "No fue posible deserializar el payload preparado por DataExtraction.",
                isTransient: false
            );

            // Multimodal image is optional: OCR text still works if the image
            // exceeds the safe vision limit. Validate raw bytes before forwarding.
            var imageBytes = TryDecodeVerifiedImage(payload);

            var preparedStages = PricingEmailAiExecutionFactory.CreateStages(
                response,
                payload,
                imageBytes
            );
            var stageInputs = preparedStages
                .Select(PricingEmailAiExecutionFactory.ToApplicationInput)
                .ToArray();

            await audit.PublishAsync(
                new AiAuditEvent(
                    EventType: AiAuditEventTypes.EmailAnalysisInputRecorded,
                    Action: AiAuditActions.InputRecorded,
                    EntityType: AiAuditEntityTypes.EmailAnalysisJob,
                    EntityId: job.Id,
                    Payload: new
                    {
                        Stage = "email-payload-prepared",
                        Job = CreateJobAuditSnapshot(job),
                        // Audit metadata, not the base64 image or binary user input.
                        DataExtractionRequestId = response.RequestId,
                        ParsedPayload = payload with { SourceImageBase64 = null },
                        PreparedStageNames = preparedStages.Select(stage => stage.StageName).ToArray(),
                    },
                    Metadata: new
                    {
                        Stage = "email-payload-prepared",
                        HasImage = imageBytes is { Length: > 0 },
                        ImageByteLength = imageBytes?.Length ?? 0,
                        StageCount = preparedStages.Count,
                    },
                    CorrelationId: job.CorrelationId
                ),
                cancellationToken
            );
            await dbContext.SaveChangesAsync(cancellationToken);

            var parsedStages = new List<ParsedAiPricingEmailResult>();
            var successfulOutputs = new List<AiStructuredResultDto>();
            string lastErrorCode = "AI.NoPricingRows";
            string lastErrorMessage = "Ninguna etapa de AI produjo filas utilizables.";
            Guid? lastExecutionId = null;
            var lastErrorIsTransient = true;

            for (var stageIndex = 0; stageIndex < preparedStages.Count; stageIndex++)
            {
                var preparedStage = preparedStages.ElementAt(stageIndex);
                var aiInput = stageInputs[stageIndex];
                var result = await orchestrator.ExecuteStructuredAsync(
                    aiInput,
                    executionCancellation.Token
                );

                if (result.IsFailure)
                {
                    lastErrorCode = result.Error.Code;
                    lastErrorMessage = result.Error.Message;
                    lastErrorIsTransient = IsTransientAiError(result.Error.Code);
                    lastExecutionId = await FindLatestExecutionIdAsync(
                        preparedStage.RequestHash,
                        cancellationToken
                    );

                    await audit.PublishAsync(
                        new AiAuditEvent(
                            EventType: AiAuditEventTypes.EmailAnalysisFailed,
                            Action: AiAuditActions.OutputRecorded,
                            EntityType: AiAuditEntityTypes.EmailAnalysisJob,
                            EntityId: job.Id,
                            Payload: new
                            {
                                Stage = "email-stage-failed",
                                PreparedStage = new { preparedStage.StageName, preparedStage.StageNumber, preparedStage.StageCount },
                                AiServiceInput = new { preparedStage.ProfileKey, HasImage = preparedStage.ImageBytes is { Length: > 0 } },
                                Error = result.Error,
                            },
                            Metadata: new
                            {
                                Stage = "email-stage-failed",
                                preparedStage.StageName,
                                preparedStage.StageNumber,
                                preparedStage.StageCount,
                            },
                            ErrorMessage: result.Error.Message,
                            CorrelationId: job.CorrelationId
                        ),
                        cancellationToken
                    );
                    await dbContext.SaveChangesAsync(cancellationToken);

                    // Las siguientes etapas usarían el mismo perfil y, para este flujo,
                    // el mismo único modelo. Si Ollama está caído, sin memoria o agotó
                    // el timeout, repetir otro fragmento solo duplica la espera.
                    if (ShouldStopRemainingStages(result.Error.Code))
                    {
                        break;
                    }

                    continue;
                }

                lastExecutionId = result.Value.ExecutionId;
                try
                {
                    var parsedStage = PricingEmailAiExecutionFactory.Parse(
                        result.Value.JsonContent
                    );
                    parsedStages.Add(parsedStage);
                    successfulOutputs.Add(result.Value);

                    await audit.PublishAsync(
                        new AiAuditEvent(
                            EventType: AiAuditEventTypes.EmailAnalysisOutputRecorded,
                            Action: AiAuditActions.OutputRecorded,
                            EntityType: AiAuditEntityTypes.EmailAnalysisJob,
                            EntityId: job.Id,
                            Payload: new
                            {
                                Stage = "email-stage-completed",
                                PreparedStage = new { preparedStage.StageName, preparedStage.StageNumber, preparedStage.StageCount },
                                AiServiceInput = new { preparedStage.ProfileKey, HasImage = preparedStage.ImageBytes is { Length: > 0 } },
                                AiServiceOutput = result.Value,
                                ParsedOutput = parsedStage,
                            },
                            Metadata: new
                            {
                                Stage = "email-stage-completed",
                                preparedStage.StageName,
                                preparedStage.StageNumber,
                                preparedStage.StageCount,
                                RowCount = parsedStage.Rows.Count,
                                parsedStage.Confidence,
                            },
                            CorrelationId: job.CorrelationId
                        ),
                        cancellationToken
                    );
                    await dbContext.SaveChangesAsync(cancellationToken);

                    if (
                        (preparedStage.StageName == "repair-deterministic-draft"
                            || preparedStage.StageName == "image-or-repair")
                        && parsedStage.Confidence >= 75m
                    )
                    {
                        break;
                    }
                }
                catch (AiEmailJobException exception)
                {
                    if (
                        exception.ErrorCode.Equals(
                            "AI.NoPricingRows",
                            StringComparison.OrdinalIgnoreCase
                        )
                        && PricingEmailAiExecutionFactory.TryBuildPier17AirFallback(
                            payload,
                            out var sourceFallback
                        )
                    )
                    {
                        parsedStages.Add(sourceFallback);
                        successfulOutputs.Add(result.Value);

                        await audit.PublishAsync(
                            new AiAuditEvent(
                                EventType: AiAuditEventTypes.EmailAnalysisOutputRecorded,
                                Action: AiAuditActions.OutputRecorded,
                                EntityType: AiAuditEntityTypes.EmailAnalysisJob,
                                EntityId: job.Id,
                                Payload: new
                                {
                                    Stage = "email-stage-source-fallback",
                                    PreparedStage = new { preparedStage.StageName, preparedStage.StageNumber, preparedStage.StageCount },
                                    AiServiceInput = new { preparedStage.ProfileKey, HasImage = preparedStage.ImageBytes is { Length: > 0 } },
                                    AiServiceOutput = result.Value,
                                    ParsedOutput = sourceFallback,
                                    RejectedModelOutput = new
                                    {
                                        exception.ErrorCode,
                                        exception.Message,
                                    },
                                },
                                Metadata: new
                                {
                                    Stage = "email-stage-source-fallback",
                                    preparedStage.StageName,
                                    preparedStage.StageNumber,
                                    RowCount = sourceFallback.Rows.Count,
                                    sourceFallback.Confidence,
                                },
                                CorrelationId: job.CorrelationId
                            ),
                            cancellationToken
                        );
                        await dbContext.SaveChangesAsync(cancellationToken);
                        break;
                    }

                    lastErrorCode = exception.ErrorCode;
                    lastErrorMessage = exception.Message;
                    lastErrorIsTransient = exception.IsTransient;

                    await audit.PublishAsync(
                        new AiAuditEvent(
                            EventType: AiAuditEventTypes.EmailAnalysisFailed,
                            Action: AiAuditActions.OutputRecorded,
                            EntityType: AiAuditEntityTypes.EmailAnalysisJob,
                            EntityId: job.Id,
                            Payload: new
                            {
                                Stage = "email-stage-output-rejected",
                                PreparedStage = new { preparedStage.StageName, preparedStage.StageNumber, preparedStage.StageCount },
                                AiServiceInput = new { preparedStage.ProfileKey, HasImage = preparedStage.ImageBytes is { Length: > 0 } },
                                AiServiceOutput = result.Value,
                                Error = new
                                {
                                    exception.ErrorCode,
                                    exception.Message,
                                    exception.IsTransient,
                                },
                            },
                            Metadata: new
                            {
                                Stage = "email-stage-output-rejected",
                                preparedStage.StageName,
                                preparedStage.StageNumber,
                            },
                            ErrorMessage: exception.Message,
                            StackTrace: exception.ToString(),
                            CorrelationId: job.CorrelationId
                        ),
                        cancellationToken
                    );
                    await dbContext.SaveChangesAsync(cancellationToken);
                }
            }

            if (parsedStages.Count == 0 || successfulOutputs.Count == 0
                || (preparedStages.All(stage => stage.StageName == "source-chunk")
                    && parsedStages.Count != preparedStages.Count))
            {
                await HandleFailureAsync(
                    job,
                    lastErrorCode,
                    lastErrorMessage,
                    lastErrorIsTransient,
                    lastExecutionId,
                    cancellationToken,
                    exception: null
                );
                return;
            }

            // AI owns semantic extraction only. DataExtraction receives these facts and
            // performs catalog resolution, canonicalization and business validation.
            var parsed = PricingEmailAiExecutionFactory.NormalizeForSource(
                PricingEmailAiExecutionFactory.Merge(parsedStages),
                payload
            );
            var primaryResult = successfulOutputs[^1];
            var completedEvent =
                new AiPricingEmailAnalysisCompletedIntegrationEvent(
                    Guid.NewGuid(),
                    job.ExternalRequestId,
                    job.EmailExtractionJobId,
                    job.Id,
                    primaryResult.ExecutionId,
                    job.CorrelationId,
                    job.RequestHash,
                    parsed.Confidence,
                    parsed.Rows,
                    parsed.Warnings,
                    DateTime.UtcNow
                );

            // The October cleanup can cancel a processing lease while Ollama
            // is still returning. Reload under a row lock before publishing any
            // completion event so a cancelled source cannot be resurrected.
            var completedJobId = job.Id;
            dbContext.ChangeTracker.Clear();
            var committed = await dbContext.ExecuteInRetryableTransactionAsync<bool>(
                async () =>
                {
                    var current = await dbContext.AiEmailAnalysisJobs
                        .FromSqlInterpolated($"""
                            SELECT * FROM ai."AiEmailAnalysisJobs"
                            WHERE id = {completedJobId}
                            FOR UPDATE
                            """)
                        .SingleOrDefaultAsync(cancellationToken);
                    if (current?.Status != AiEmailAnalysisJobStatus.Processing)
                        return false;

                    current.MarkCompleted(
                        primaryResult.ExecutionId,
                        JsonSerializer.Serialize(parsed, JsonOptions)
                    );
                    await outbox.WriteAsync(
                        typeof(AiPricingEmailAnalysisCompletedIntegrationEvent).FullName!,
                        EmailAnalysisMessageTypes.Completed,
                        completedEvent,
                        job.CorrelationId,
                        cancellationToken
                    );
                    await audit.PublishAsync(
                        new AiAuditEvent(
                            EventType: AiAuditEventTypes.EmailAnalysisOutputRecorded,
                            Action: AiAuditActions.OutputRecorded,
                            EntityType: AiAuditEntityTypes.EmailAnalysisJob,
                            EntityId: job.Id,
                            After: new
                            {
                                Status = job.Status.ToString(),
                                AiExecutionId = primaryResult.ExecutionId,
                                RawAiOutput = primaryResult.JsonContent,
                                ParsedOutput = parsed,
                            },
                            Payload: new
                            {
                                Stage = "email-analysis-completed",
                                Job = CreateJobAuditSnapshot(current),
                                AiServiceInputs = stageInputs,
                                AiServiceOutputs = successfulOutputs,
                                ParsedOutput = parsed,
                                PublishedIntegrationEvent = completedEvent,
                            },
                            Metadata: new
                            {
                                Stage = "email-analysis-completed",
                                Status = "Completed",
                                RowCount = parsed.Rows.Count,
                            },
                            CorrelationId: job.CorrelationId
                        ),
                        cancellationToken
                    );
                    await dbContext.SaveChangesAsync(cancellationToken);
                    return true;
                },
                IsolationLevel.ReadCommitted,
                cancellationToken
            );

            if (!committed)
            {
                logger.LogInformation(
                    "Se descartó una respuesta tardía de Ollama para el trabajo AI {AiJobId}: " +
                    "la solicitud ya fue cancelada o finalizada.", completedJobId);
                return;
            }

            logger.LogInformation(
                "Análisis AI de correo completado. AI job {AiJobId}; "
                    + "solicitud {RequestId}; ejecución {AiExecutionId}; "
                    + "trabajo {EmailExtractionJobId}; intentos {AttemptCount}; "
                    + "filas {RowCount}; CorrelationId {CorrelationId}; "
                    + "RequestHash {RequestHash}.",
                job.Id,
                job.ExternalRequestId,
                primaryResult.ExecutionId,
                job.EmailExtractionJobId,
                job.AttemptCount,
                parsed.Rows.Count,
                job.CorrelationId,
                job.RequestHash
            );
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (executionCancellation.IsCancellationRequested)
        {
            logger.LogInformation(
                "Ejecución de Ollama interrumpida para AI job {AiJobId}: el lease ya fue cancelado.",
                job.Id);
            return;
        }
        catch (AiEmailJobException exception)
        {
            await HandleFailureAsync(
                job,
                exception.ErrorCode,
                exception.Message,
                exception.IsTransient,
                job.AiExecutionId,
                cancellationToken,
                exception
            );
        }
        catch (OperationCanceledException exception)
        {
            await HandleFailureAsync(
                job,
                "AI.ProviderTimeout",
                "El proveedor o DataExtraction excedió el tiempo máximo configurado.",
                isTransient: true,
                job.AiExecutionId,
                cancellationToken,
                exception
            );
            logger.LogWarning(
                exception,
                "Timeout procesando AI job {AiJobId}.",
                job.Id
            );
        }
        catch (HttpRequestException exception)
        {
            await HandleFailureAsync(
                job,
                "AI.DataExtractionUnavailable",
                exception.Message,
                isTransient: true,
                job.AiExecutionId,
                cancellationToken,
                exception
            );
        }
        catch (Exception exception)
        {
            await HandleFailureAsync(
                job,
                "AI.EmailJobUnexpectedError",
                exception.GetBaseException().Message,
                isTransient: true,
                job.AiExecutionId,
                cancellationToken,
                exception
            );
            logger.LogError(
                exception,
                "Error inesperado procesando AI job {AiJobId}.",
                job.Id
            );
        }
        finally
        {
            heartbeatCancellation.Cancel();
            try
            {
                await heartbeatTask;
            }
            catch (OperationCanceledException)
            {
                // La cancelación coordinada detiene el heartbeat después de finalizar el job.
            }
        }
    }

    private async Task HandleFailureAsync(
        AiEmailAnalysisJob job,
        string errorCode,
        string errorMessage,
        bool isTransient,
        Guid? aiExecutionId,
        CancellationToken cancellationToken,
        Exception? exception
    )
    {
        var jobId = job.Id;
        dbContext.ChangeTracker.Clear();
        job = await dbContext.AiEmailAnalysisJobs.FirstOrDefaultAsync(
            item => item.Id == jobId,
            cancellationToken
        ) ?? throw new InvalidOperationException(
            "No se encontró el AI job que debe registrar el fallo."
        );
        if (
            job.Status
            is AiEmailAnalysisJobStatus.Completed
                or AiEmailAnalysisJobStatus.Failed
                or AiEmailAnalysisJobStatus.Cancelled
        )
        {
            return;
        }

        if (job.Status != AiEmailAnalysisJobStatus.Processing)
        {
            throw new InvalidOperationException(
                $"El AI job {job.Id} no está en Processing al registrar el fallo."
            );
        }

        // DataExtraction rejected an obsolete email before handing over its
        // content; do not retry or record this expected closure as an AI failure.
        if (errorCode is "AI.HistoricalMonthClosed" or "AI.SourceUnavailable")
        {
            if (errorCode == "AI.HistoricalMonthClosed")
                job.CancelHistoricalMonth();
            else
                job.CancelUnavailableSource(errorCode, errorMessage);
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation(
                "AI job {AiJobId} cerrado porque su fuente no está disponible.",
                job.Id);
            return;
        }

        var shouldRetry =
            isTransient && job.AttemptCount < job.MaxAttemptCount;
        if (shouldRetry)
        {
            job.ScheduleRetry(
                errorCode,
                errorMessage,
                DateTime.UtcNow.AddSeconds(
                    ResolveRetryDelaySeconds(job.AttemptCount)
                ),
                aiExecutionId
            );
            await audit.PublishAsync(
                new AiAuditEvent(
                    EventType: AiAuditEventTypes.EmailAnalysisFailed,
                    Action: AiAuditActions.OutputRecorded,
                    EntityType: AiAuditEntityTypes.EmailAnalysisJob,
                    EntityId: job.Id,
                    After: new
                    {
                        Status = job.Status.ToString(),
                        job.NextAttemptAtUtc,
                        job.AiExecutionId,
                        job.ErrorCode,
                        job.ErrorMessage,
                    },
                    Payload: new
                    {
                        Stage = "email-analysis-retry-scheduled",
                        Job = CreateJobAuditSnapshot(job),
                        Error = new
                        {
                            Code = errorCode,
                            Message = errorMessage,
                            IsTransient = isTransient,
                            ExceptionType = exception?.GetType().FullName,
                            StackTrace = exception?.StackTrace,
                        },
                    },
                    Metadata: new
                    {
                        Stage = "email-analysis-retry-scheduled",
                        Status = "RetryScheduled",
                        job.AttemptCount,
                        job.MaxAttemptCount,
                    },
                    ErrorMessage: errorMessage,
                    StackTrace: exception?.ToString(),
                    CorrelationId: job.CorrelationId
                ),
                cancellationToken
            );
            await dbContext.SaveChangesAsync(cancellationToken);

            logger.LogWarning(
                "AI job {AiJobId} reprogramado. Solicitud {RequestId}; "
                    + "intento {AttemptCount}/{MaxAttemptCount}; código {ErrorCode}; "
                    + "próximo intento {NextAttemptAtUtc}; CorrelationId {CorrelationId}.",
                job.Id,
                job.ExternalRequestId,
                job.AttemptCount,
                job.MaxAttemptCount,
                errorCode,
                job.NextAttemptAtUtc,
                job.CorrelationId
            );
            return;
        }

        var failedEvent = new AiPricingEmailAnalysisFailedIntegrationEvent(
            Guid.NewGuid(),
            job.ExternalRequestId,
            job.EmailExtractionJobId,
            job.Id,
            aiExecutionId,
            job.CorrelationId,
            job.RequestHash,
            errorCode,
            Limit(errorMessage, 4000),
            isTransient,
            job.AttemptCount,
            DateTime.UtcNow
        );
        await dbContext.ExecuteInRetryableTransactionAsync(
            async () =>
            {
                job.MarkFailed(errorCode, errorMessage, aiExecutionId);
                await outbox.WriteAsync(
                    typeof(AiPricingEmailAnalysisFailedIntegrationEvent).FullName!,
                    EmailAnalysisMessageTypes.Failed,
                    failedEvent,
                    job.CorrelationId,
                    cancellationToken
                );
                await audit.PublishAsync(
                    new AiAuditEvent(
                        EventType: AiAuditEventTypes.EmailAnalysisFailed,
                        Action: AiAuditActions.OutputRecorded,
                        EntityType: AiAuditEntityTypes.EmailAnalysisJob,
                        EntityId: job.Id,
                        After: new
                        {
                            Status = job.Status.ToString(),
                            job.AiExecutionId,
                            job.ErrorCode,
                            job.ErrorMessage,
                            job.CompletedAtUtc,
                        },
                        Payload: new
                        {
                            Stage = "email-analysis-failed",
                            Job = CreateJobAuditSnapshot(job),
                            Error = new
                            {
                                Code = errorCode,
                                Message = errorMessage,
                                IsTransient = isTransient,
                                ExceptionType = exception?.GetType().FullName,
                                StackTrace = exception?.StackTrace,
                            },
                            PublishedIntegrationEvent = failedEvent,
                        },
                        Metadata: new
                        {
                            Stage = "email-analysis-failed",
                            Status = "Failed",
                            job.AttemptCount,
                            job.MaxAttemptCount,
                        },
                        ErrorMessage: errorMessage,
                        StackTrace: exception?.ToString(),
                        CorrelationId: job.CorrelationId
                    ),
                    cancellationToken
                );
                await dbContext.SaveChangesAsync(cancellationToken);
            },
            IsolationLevel.ReadCommitted,
            cancellationToken
        );

        logger.LogError(
            "AI job {AiJobId} falló definitivamente. Solicitud {RequestId}; "
                + "intentos {AttemptCount}; código {ErrorCode}; "
                + "CorrelationId {CorrelationId}; RequestHash {RequestHash}.",
            job.Id,
            job.ExternalRequestId,
            job.AttemptCount,
            errorCode,
            job.CorrelationId,
            job.RequestHash
        );
    }

    private async Task RecoverStaleExecutionsAsync(
        CancellationToken cancellationToken
    )
    {
        var staleAfterSeconds = Math.Max(
            4_200,
            ReadPositiveInt(
                configuration["AI:Execution:StaleAfterSeconds"],
                7_200
            )
        );
        var staleBefore = DateTime.UtcNow.AddSeconds(-staleAfterSeconds);
        var executions = await dbContext.AiExecutions
            .Include(item => item.Attempts)
            .Where(item =>
                item.Status == AiExecutionStatus.Running
                && item.StartedAtUtc.HasValue
                && item.StartedAtUtc.Value < staleBefore
            )
            .OrderBy(item => item.StartedAtUtc)
            .Take(100)
            .ToListAsync(cancellationToken);

        foreach (var execution in executions)
        {
            const string errorCode = "AI.StaleExecutionRecovered";
            const string errorMessage =
                "La ejecución quedó abierta después de una interrupción del proveedor o del host.";
            execution.Fail(
                errorCode,
                errorMessage,
                null
            );
            await audit.PublishAsync(
                new AiAuditEvent(
                    EventType: AiAuditEventTypes.ExecutionOutputRecorded,
                    Action: AiAuditActions.OutputRecorded,
                    EntityType: AiAuditEntityTypes.Execution,
                    EntityId: execution.Id,
                    After: new
                    {
                        Status = execution.Status.ToString(),
                        ErrorCode = errorCode,
                        ErrorMessage = errorMessage,
                        execution.DurationMilliseconds,
                        execution.CompletedAtUtc,
                    },
                    Payload: new
                    {
                        Stage = "stale-execution-recovered",
                        Execution = new
                        {
                            execution.Id,
                            execution.ProfileId,
                            execution.ProfileKey,
                            ExecutionType = execution.ExecutionType.ToString(),
                            Status = execution.Status.ToString(),
                            execution.CorrelationId,
                            execution.RequestHash,
                            execution.ErrorCode,
                            execution.ErrorMessage,
                            execution.DurationMilliseconds,
                            Attempts = execution.Attempts.Select(item => new
                            {
                                item.Id,
                                item.AttemptNumber,
                                item.ConnectionId,
                                item.ModelId,
                                ProviderType = item.ProviderType.ToString(),
                                item.ExternalModelId,
                                Status = item.Status.ToString(),
                                item.ErrorCode,
                                item.ErrorMessage,
                                item.DurationMilliseconds,
                            }),
                        },
                    },
                    Metadata: new
                    {
                        Stage = "stale-execution-recovered",
                        Status = "Failed",
                    },
                    ErrorMessage: errorMessage,
                    CorrelationId: execution.CorrelationId
                ),
                cancellationToken
            );
        }

        if (executions.Count == 0)
        {
            return;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogWarning(
            "Se cerraron {ExecutionCount} ejecuciones AI que permanecían en Running.",
            executions.Count
        );
        dbContext.ChangeTracker.Clear();
    }

    private async Task RecoverFailedLeaseJobsAsync(
        CancellationToken cancellationToken
    )
    {
        var now = DateTime.UtcNow;
        var maximumJobAgeHours = ReadPositiveInt(
            configuration["AI:EmailJobs:MaximumJobAgeHours"],
            24
        );
        var maximumAgeCutoff = now.AddHours(-maximumJobAgeHours);
        var jobs = await dbContext.AiEmailAnalysisJobs
            .Where(item =>
                item.Status == AiEmailAnalysisJobStatus.Failed
                && item.ErrorCode == "AI.EmailJobLeaseExpired"
                && item.CreatedAtUtc >= maximumAgeCutoff
            )
            .OrderBy(item => item.CompletedAtUtc)
            .Take(100)
            .ToListAsync(cancellationToken);

        foreach (var job in jobs)
        {
            job.RequeueAfterLeaseFailure(now);
            await audit.PublishAsync(
                new AiAuditEvent(
                    EventType: AiAuditEventTypes.EmailAnalysisInputRecorded,
                    Action: AiAuditActions.InputRecorded,
                    EntityType: AiAuditEntityTypes.EmailAnalysisJob,
                    EntityId: job.Id,
                    After: new
                    {
                        Status = job.Status.ToString(),
                        job.NextAttemptAtUtc,
                        job.AttemptCount,
                        job.MaxAttemptCount,
                    },
                    Payload: new
                    {
                        Stage = "email-lease-failure-reactivated",
                        Job = CreateJobAuditSnapshot(job),
                    },
                    Metadata: new
                    {
                        Stage = "email-lease-failure-reactivated",
                        Status = "RetryScheduled",
                    },
                    CorrelationId: job.CorrelationId
                ),
                cancellationToken
            );
        }

        if (jobs.Count == 0)
        {
            return;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogWarning(
            "Se reactivaron {JobCount} AI jobs que habían fallado únicamente por pérdida de lease.",
            jobs.Count
        );
        dbContext.ChangeTracker.Clear();
    }

    private async Task RecoverExpiredLeasesAsync(
        CancellationToken cancellationToken
    )
    {
        var now = DateTime.UtcNow;
        var heartbeatGraceSeconds = ReadPositiveInt(
            configuration["AI:EmailJobs:LeaseRecoveryGraceSeconds"],
            120
        );
        var maximumJobAgeHours = ReadPositiveInt(
            configuration["AI:EmailJobs:MaximumJobAgeHours"],
            24
        );
        var staleHeartbeatBefore = now.AddSeconds(-heartbeatGraceSeconds);
        var maximumAgeCutoff = now.AddHours(-maximumJobAgeHours);
        var jobs = await dbContext.AiEmailAnalysisJobs
            .Where(item =>
                item.Status == AiEmailAnalysisJobStatus.Processing
                && item.LeaseExpiresAtUtc.HasValue
                && item.LeaseExpiresAtUtc.Value < now
                && (
                    !item.LastHeartbeatAtUtc.HasValue
                    || item.LastHeartbeatAtUtc.Value < staleHeartbeatBefore
                )
            )
            .OrderBy(item => item.LeaseExpiresAtUtc)
            .Take(100)
            .ToListAsync(cancellationToken);

        foreach (var job in jobs)
        {
            if (job.CreatedAtUtc < maximumAgeCutoff)
            {
                const string terminalErrorCode = "AI.EmailJobLeaseRecoveryExpired";
                var terminalErrorMessage =
                    $"El trabajo AI superó {maximumJobAgeHours} horas sin completar después de perder el lease.";
                var failedEvent =
                    new AiPricingEmailAnalysisFailedIntegrationEvent(
                        Guid.NewGuid(),
                        job.ExternalRequestId,
                        job.EmailExtractionJobId,
                        job.Id,
                        job.AiExecutionId,
                        job.CorrelationId,
                        job.RequestHash,
                        terminalErrorCode,
                        terminalErrorMessage,
                        true,
                        job.AttemptCount,
                        now
                    );
                job.MarkFailed(
                    terminalErrorCode,
                    terminalErrorMessage,
                    job.AiExecutionId
                );
                await outbox.WriteAsync(
                    typeof(AiPricingEmailAnalysisFailedIntegrationEvent).FullName!,
                    EmailAnalysisMessageTypes.Failed,
                    failedEvent,
                    job.CorrelationId,
                    cancellationToken
                );
                await audit.PublishAsync(
                    new AiAuditEvent(
                        EventType: AiAuditEventTypes.EmailAnalysisFailed,
                        Action: AiAuditActions.OutputRecorded,
                        EntityType: AiAuditEntityTypes.EmailAnalysisJob,
                        EntityId: job.Id,
                        After: new
                        {
                            Status = job.Status.ToString(),
                            job.AiExecutionId,
                            job.ErrorCode,
                            job.ErrorMessage,
                            job.CompletedAtUtc,
                        },
                        Payload: new
                        {
                            Stage = "email-lease-recovery-expired",
                            Job = CreateJobAuditSnapshot(job),
                            PublishedIntegrationEvent = failedEvent,
                        },
                        Metadata: new
                        {
                            Stage = "email-lease-recovery-expired",
                            Status = "Failed",
                        },
                        ErrorMessage: terminalErrorMessage,
                        CorrelationId: job.CorrelationId
                    ),
                    cancellationToken
                );
                continue;
            }

            const string retryErrorCode = "AI.EmailJobLeaseExpired";
            const string retryErrorMessage =
                "El worker AI perdió el lease y el trabajo fue recuperado sin consumir un intento del proveedor.";
            job.RecoverExpiredLease(
                retryErrorCode,
                retryErrorMessage,
                now
            );
            await audit.PublishAsync(
                new AiAuditEvent(
                    EventType: AiAuditEventTypes.EmailAnalysisFailed,
                    Action: AiAuditActions.OutputRecorded,
                    EntityType: AiAuditEntityTypes.EmailAnalysisJob,
                    EntityId: job.Id,
                    After: new
                    {
                        Status = job.Status.ToString(),
                        job.NextAttemptAtUtc,
                        job.AttemptCount,
                        job.MaxAttemptCount,
                        job.ErrorCode,
                        job.ErrorMessage,
                    },
                    Payload: new
                    {
                        Stage = "email-lease-recovered",
                        Job = CreateJobAuditSnapshot(job),
                        Error = new
                        {
                            Code = retryErrorCode,
                            Message = retryErrorMessage,
                            IsTransient = true,
                        },
                    },
                    Metadata: new
                    {
                        Stage = "email-lease-recovered",
                        Status = "RetryScheduled",
                    },
                    ErrorMessage: retryErrorMessage,
                    CorrelationId: job.CorrelationId
                ),
                cancellationToken
            );
        }

        if (jobs.Count == 0)
        {
            return;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogWarning(
            "Se recuperaron {JobCount} AI jobs con lease vencido sin consumir reintentos del proveedor.",
            jobs.Count
        );
        dbContext.ChangeTracker.Clear();
    }

    private async Task RenewLeaseLoopAsync(
        Guid jobId,
        string leaseOwner,
        CancellationToken cancellationToken,
        CancellationTokenSource executionCancellation
    )
    {
        var heartbeatSeconds = Math.Max(
            5,
            ReadPositiveInt(
                configuration["AI:EmailJobs:HeartbeatSeconds"],
                15
            )
        );
        var leaseMinutes = ReadPositiveInt(
            configuration["AI:EmailJobs:LeaseMinutes"],
            30
        );

        // Renueva inmediatamente para no depender del primer tick del timer.
        if (
            !await TryRenewLeaseAsync(
                jobId,
                leaseOwner,
                leaseMinutes,
                cancellationToken
            )
        )
        {
            executionCancellation.Cancel();
            return;
        }

        using var timer = new PeriodicTimer(
            TimeSpan.FromSeconds(heartbeatSeconds)
        );
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            if (
                !await TryRenewLeaseAsync(
                    jobId,
                    leaseOwner,
                    leaseMinutes,
                    cancellationToken
                )
            )
            {
                executionCancellation.Cancel();
                return;
            }
        }
    }

    private async Task<bool> TryRenewLeaseAsync(
        Guid jobId,
        string leaseOwner,
        int leaseMinutes,
        CancellationToken cancellationToken
    )
    {
        var retryCount = ReadPositiveInt(
            configuration["AI:EmailJobs:HeartbeatDatabaseRetryCount"],
            5
        );
        var retryDelaySeconds = ReadPositiveInt(
            configuration["AI:EmailJobs:HeartbeatRetryDelaySeconds"],
            2
        );

        for (var attempt = 1; attempt <= retryCount; attempt++)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var heartbeatDbContext =
                    scope.ServiceProvider.GetRequiredService<ServiceDbContext>();
                var now = DateTime.UtcNow;
                var updated = await heartbeatDbContext.AiEmailAnalysisJobs
                    .Where(item =>
                        item.Id == jobId
                        && item.Status == AiEmailAnalysisJobStatus.Processing
                        && item.LeaseOwner == leaseOwner
                    )
                    .ExecuteUpdateAsync(
                        setters => setters
                            .SetProperty(
                                item => item.LastHeartbeatAtUtc,
                                now
                            )
                            .SetProperty(
                                item => item.LeaseExpiresAtUtc,
                                now.AddMinutes(leaseMinutes)
                            ),
                        cancellationToken
                    );

                if (updated == 0)
                {
                    logger.LogWarning(
                        "El heartbeat dejó de renovar el AI job {AiJobId} porque el lease ya no pertenece a {LeaseOwner}.",
                        jobId,
                        leaseOwner
                    );
                    return false;
                }

                return true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "No fue posible renovar el lease del AI job {AiJobId}. Intento de heartbeat {Attempt}/{RetryCount}.",
                    jobId,
                    attempt,
                    retryCount
                );

                if (attempt < retryCount)
                {
                    await Task.Delay(
                        TimeSpan.FromSeconds(retryDelaySeconds),
                        cancellationToken
                    );
                }
            }
        }

        // No se detiene definitivamente el heartbeat por una interrupción temporal
        // de PostgreSQL. El siguiente tick volverá a intentarlo y el lease amplio
        // evita que un fallo breve convierta el trabajo en abandonado.
        logger.LogError(
            "El heartbeat del AI job {AiJobId} agotó sus reintentos de base de datos; se intentará nuevamente en el próximo ciclo.",
            jobId
        );
        return true;
    }

    private async Task<Guid?> FindLatestExecutionIdAsync(
        string requestHash,
        CancellationToken cancellationToken
    )
    {
        return await dbContext.AiExecutions
            .AsNoTracking()
            .Where(item => item.RequestHash == requestHash)
            .OrderByDescending(item => item.CreatedAtUtc)
            .Select(item => (Guid?)item.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static void ValidatePayload(
        AiEmailAnalysisJob job,
        DataExtractionAiEmailRequestResponse response
    )
    {
        if (
            response.RequestId != job.ExternalRequestId
            || response.EmailExtractionJobId != job.EmailExtractionJobId
        )
        {
            throw new AiEmailJobException(
                "AI.DataExtractionPayloadMismatch",
                "El payload no corresponde al trabajo AI reclamado.",
                isTransient: false
            );
        }

        if (
            !string.Equals(
                response.RequestHash,
                job.RequestHash,
                StringComparison.Ordinal
            )
        )
        {
            throw new AiEmailJobException(
                "AI.RequestHashMismatch",
                "El RequestHash de DataExtraction no coincide con el evento.",
                isTransient: false
            );
        }
    }

    private static bool ShouldStopRemainingStages(string errorCode)
    {
        return errorCode
            is "AI.ProviderTimeout"
                or "AI.ProviderOperationFailed"
                or "AI.NoModelAvailable"
                or "AI.ModelCapabilityNotSupported"
                or "AI.ConnectionIsInactive"
                or "AI.ProfileIsInactive";
    }

    private static bool IsTransientAiError(string errorCode)
    {
        return errorCode.Contains("Timeout", StringComparison.OrdinalIgnoreCase)
            || errorCode.Contains("Unavailable", StringComparison.OrdinalIgnoreCase)
            || errorCode.Contains("Provider", StringComparison.OrdinalIgnoreCase)
            || errorCode.Contains(
                "InvalidStructuredOutput",
                StringComparison.OrdinalIgnoreCase
            )
            || errorCode.Contains(
                "AllProvidersFailed",
                StringComparison.OrdinalIgnoreCase
            );
    }

    private int ResolveRetryDelaySeconds(int attemptCount)
    {
        var delays = configuration
            .GetSection("AI:EmailJobs:RetryDelaysSeconds")
            .Get<int[]>();
        if (delays is not { Length: > 0 })
        {
            delays = [30, 120, 600];
        }

        return Math.Max(1, delays[Math.Min(attemptCount - 1, delays.Length - 1)]);
    }

    private static bool ReadBoolean(string? value, bool fallback)
    {
        return bool.TryParse(value, out var parsed) ? parsed : fallback;
    }

    private byte[]? TryDecodeVerifiedImage(AiPricingEmailPayload payload)
    {
        if (string.IsNullOrWhiteSpace(payload.SourceImageBase64))
            return null;

        var maxBytes = Math.Min(
            650_000,
            ReadPositiveInt(configuration["AI:EmailJobs:MaximumVisionImageBytes"], 500_000)
        );
        if (payload.SourceImageBase64.Length > (maxBytes * 4 / 3) + 8)
        {
            logger.LogWarning("Email AI vision image is over size limit; using OCR text only.");
            return null;
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(payload.SourceImageBase64);
        }
        catch (FormatException)
        {
            throw new AiEmailJobException(
                "AI.InvalidVisionImage",
                "El adjunto visual no contiene datos base64 válidos.",
                isTransient: false
            );
        }

        if (bytes.Length == 0 || bytes.Length > maxBytes)
            return null;

        var matchesMime = payload.SourceImageMimeType?.ToLowerInvariant() switch
        {
            "image/png" => bytes.Length >= 8
                && bytes[0] == 0x89 && bytes[1] == 0x50
                && bytes[2] == 0x4E && bytes[3] == 0x47,
            "image/jpeg" => bytes.Length >= 3
                && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF,
            "image/webp" => bytes.Length >= 12
                && bytes[0] == (byte)'R' && bytes[1] == (byte)'I'
                && bytes[2] == (byte)'F' && bytes[3] == (byte)'F'
                && bytes[8] == (byte)'W' && bytes[9] == (byte)'E'
                && bytes[10] == (byte)'B' && bytes[11] == (byte)'P',
            _ => false,
        };

        if (!matchesMime)
        {
            throw new AiEmailJobException(
                "AI.InvalidVisionImageType",
                "La imagen multimodal no coincide con el formato declarado.",
                isTransient: false
            );
        }

        return bytes;
    }

    private static int ReadPositiveInt(string? value, int fallback)
    {
        return int.TryParse(value, out var parsed) && parsed > 0
            ? parsed
            : fallback;
    }

    private static string Limit(string value, int maximumLength)
    {
        return value.Length <= maximumLength ? value : value[..maximumLength];
    }

    private static object CreateJobAuditSnapshot(AiEmailAnalysisJob job) =>
        new
        {
            job.Id,
            job.ExternalRequestId,
            job.EmailExtractionJobId,
            job.EmailMessageId,
            job.EmailAttachmentId,
            job.PayloadUrl,
            job.RequestHash,
            job.CorrelationId,
            Status = job.Status.ToString(),
            job.AttemptCount,
            job.MaxAttemptCount,
            job.NextAttemptAtUtc,
            job.LeaseOwner,
            job.LeaseExpiresAtUtc,
            job.LastHeartbeatAtUtc,
            job.AiExecutionId,
            job.ResultJson,
            job.ErrorCode,
            job.ErrorMessage,
            job.StartedAtUtc,
            job.CompletedAtUtc,
            job.Version,
        };
}
