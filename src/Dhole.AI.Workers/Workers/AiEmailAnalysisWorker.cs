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

        dbContext.ChangeTracker.Clear();
        await ApplyConfiguredAttemptLimitAsync(cancellationToken);
        await RecoverStaleExecutionsAsync(cancellationToken);
        await RecoverFailedLeaseJobsAsync(cancellationToken);
        await RecoverExpiredLeasesAsync(cancellationToken);

        var maxJobs = Math.Min(
            ReadPositiveInt(configuration["AI:EmailJobs:MaxJobsPerRun"], 1),
            ReadPositiveInt(configuration["AI:EmailJobs:MaxConcurrentJobs"], 1)
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
        var heartbeatTask = RenewLeaseLoopAsync(
            job.Id,
            _leaseOwner,
            heartbeatCancellation.Token
        );

        try
        {
            var response = await dataExtractionClient.GetAsync(
                job.PayloadUrl,
                cancellationToken
            );
            ValidatePayload(job, response);
            var payload = response.Payload.Deserialize<AiPricingEmailPayload>(
                JsonOptions
            ) ?? throw new AiEmailJobException(
                "AI.DataExtractionPayloadInvalid",
                "No fue posible deserializar el payload preparado por DataExtraction.",
                isTransient: false
            );

            if (IsUnsupportedImagePayload(payload))
            {
                throw new AiEmailJobException(
                    "AI.UnsupportedImageExtraction",
                    "La extracción de imágenes está deshabilitada. Solo se admite cuerpo de correo, PDF, CSV o XLSX.",
                    isTransient: false
                );
            }

            var preparedStages = PricingEmailAiExecutionFactory.CreateStages(
                response,
                payload,
                imageBytes: null
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
                        DataExtractionResponse = response,
                        ParsedPayload = payload,
                        PreparedStages = preparedStages,
                        AiServiceInputs = stageInputs,
                    },
                    Metadata: new
                    {
                        Stage = "email-payload-prepared",
                        HasImage = false,
                        ImageByteLength = 0,
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
                    cancellationToken
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
                                PreparedStage = preparedStage,
                                AiServiceInput = aiInput,
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
                                PreparedStage = preparedStage,
                                AiServiceInput = aiInput,
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
                        preparedStage.StageName == "repair-deterministic-draft"
                        && parsedStage.Confidence >= 75m
                    )
                    {
                        break;
                    }
                }
                catch (AiEmailJobException exception)
                {
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
                                PreparedStage = preparedStage,
                                AiServiceInput = aiInput,
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

            if (parsedStages.Count == 0 || successfulOutputs.Count == 0)
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

            await dbContext.ExecuteInRetryableTransactionAsync(
                async () =>
                {
                    job.MarkCompleted(
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
                                Job = CreateJobAuditSnapshot(job),
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
                },
                IsolationLevel.ReadCommitted,
                cancellationToken
            );

            logger.LogInformation(
                "Análisis AI de correo completado. AI job {AiJobId}; "
                    + "solicitud {RequestId}; ejecución {AiExecutionId}; "
                    + "trabajo {EmailExtractionJobId}; intentos {AttemptCount}; "
                    + "filas {RowCount}; CorrelationId {CorrelationId}; "
                    + "RequestHash {RequestHash}.",
                job.Id,
                job.ExternalRequestId,
                job.AiExecutionId,
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
        CancellationToken cancellationToken
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

    private static bool IsUnsupportedImagePayload(
        AiPricingEmailPayload payload
    )
    {
        if (
            payload.SourceContentType?.StartsWith(
                "image/",
                StringComparison.OrdinalIgnoreCase
            ) == true
        )
        {
            return true;
        }

        var extension = Path.GetExtension(payload.SourceName)?.ToLowerInvariant();
        return extension
            is ".png"
                or ".jpg"
                or ".jpeg"
                or ".gif"
                or ".webp"
                or ".bmp"
                or ".tif"
                or ".tiff";
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
