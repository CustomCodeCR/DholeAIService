using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Persistence.Abstractions;
using Dhole.AI.Application.Abstractions.Auditing;
using Dhole.AI.Application.Abstractions.Mongo;
using Dhole.AI.Application.Abstractions.Providers.Models;
using Dhole.AI.Application.Abstractions.Repositories;
using Dhole.AI.Application.Abstractions.Services;
using Dhole.AI.Application.Auditing;
using Dhole.AI.Application.Shared;
using Dhole.AI.Contracts.Executions.Response;
using Dhole.AI.Domain.Executions.Entities;
using Dhole.AI.Domain.Executions.Enums;
using Dhole.AI.Domain.Models.Entities;
using Dhole.AI.Domain.Models.Enums;
using Dhole.AI.Domain.Profiles.Entities;
using Dhole.AI.Domain.PromptTemplates.Entities;
using Dhole.AI.Domain.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Dhole.AI.Application.Services;

public sealed class AiExecutionOrchestrator(
    IAiProfileRepository profiles,
    IAiPromptTemplateRepository promptTemplates,
    IAiExecutionRepository executions,
    IAiModelSelector modelSelector,
    IAiProviderResolver providerResolver,
    IAiSecretResolver secretResolver,
    IAiPromptCompiler promptCompiler,
    IAiStructuredResponseValidator structuredValidator,
    IAiAuditService audit,
    IAiExecutionSnapshotWriter snapshotWriter,
    IUnitOfWork unitOfWork,
    IConfiguration configuration,
    ILogger<AiExecutionOrchestrator> logger
) : IAiExecutionOrchestrator
{
    public async Task<Result<AiChatResultDto>> ExecuteChatAsync(
        ExecuteAiChatInput input,
        CancellationToken cancellationToken = default
    )
    {
        var contextResult = await PrepareAsync(
            input.ProfileKey,
            AiExecutionType.Chat,
            HasImages(input.Messages)
                ? AiModelCapability.Chat | AiModelCapability.Vision
                : AiModelCapability.Chat,
            input.CorrelationId,
            input.RequestHash,
            input.RequestedBy,
            input.RequestedByName,
            input.Messages,
            input.Variables,
            input,
            cancellationToken
        );

        if (contextResult.IsFailure)
        {
            await AuditRejectedRequestAsync(
                input,
                input.ProfileKey,
                AiExecutionType.Chat,
                input.CorrelationId,
                input.RequestedBy,
                input.RequestedByName,
                contextResult.Error,
                cancellationToken
            );
            return Result.Failure<AiChatResultDto>(contextResult.Error);
        }

        var context = contextResult.Value;
        var lastError = AiApplicationErrors.ExecutionFailed;

        var executionTimeoutSeconds = ResolveExecutionTimeoutSeconds(context.Profile);
        using var profileTimeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken
        );
        profileTimeout.CancelAfter(TimeSpan.FromSeconds(executionTimeoutSeconds));

        foreach (var candidate in context.Candidates.Select((value, index) => (value, index)))
        {
            if (profileTimeout.IsCancellationRequested)
            {
                lastError = AiApplicationErrors.ProviderTimeout;
                break;
            }

            using var attemptTimeout = CreateProviderAttemptTimeout(
                profileTimeout.Token,
                context.Profile,
                executionTimeoutSeconds,
                context.Candidates.Count,
                candidate.value
            );

            Result<AiProviderChatResponse> result;

            try
            {
                result = await ExecuteChatAttemptAsync(
                    context,
                    candidate.value,
                    false,
                    null,
                    cancellationToken,
                    attemptTimeout.Token
                );
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await CancelExecutionAfterCallerAbortAsync(
                    context.Execution,
                    input.RequestedBy
                );

                return Result.Failure<AiChatResultDto>(
                    AiApplicationErrors.ClientRequestCancelled
                );
            }

            if (result.IsSuccess)
            {
                return Result.Success(
                    CreateChatResult(context.Execution, candidate.value, result.Value)
                );
            }

            lastError = result.Error;

            await RegisterFailedAttemptAsync(
                context.Execution,
                candidate.value,
                candidate.index,
                context.Candidates,
                result.Error,
                cancellationToken
            );

            if (profileTimeout.IsCancellationRequested)
            {
                lastError = AiApplicationErrors.ProviderTimeout;
                break;
            }
        }

        var failureCode = lastError.Code == AiApplicationErrors.ProviderTimeout.Code
            ? AiApplicationErrors.ProviderTimeout.Code
            : "AI.AllProvidersFailed";
        var failureMessage = lastError.Code == AiApplicationErrors.ProviderTimeout.Code
            ? AiApplicationErrors.ProviderTimeout.Message
            : "Todos los proveedores configurados fallaron.";

        await FailExecutionAsync(
            context.Execution,
            failureCode,
            failureMessage,
            input,
            cancellationToken
        );

        return Result.Failure<AiChatResultDto>(lastError);
    }

    public async Task<Result<AiStructuredResultDto>> ExecuteStructuredAsync(
        ExecuteAiStructuredInput input,
        CancellationToken cancellationToken = default
    )
    {
        var contextResult = await PrepareAsync(
            input.ProfileKey,
            AiExecutionType.Structured,
            HasImages(input.Messages)
                ? AiModelCapability.StructuredOutput | AiModelCapability.Vision
                : AiModelCapability.StructuredOutput,
            input.CorrelationId,
            input.RequestHash,
            input.RequestedBy,
            null,
            input.Messages,
            input.Variables,
            input,
            cancellationToken
        );

        if (contextResult.IsFailure)
        {
            await AuditRejectedRequestAsync(
                input,
                input.ProfileKey,
                AiExecutionType.Structured,
                input.CorrelationId,
                input.RequestedBy,
                null,
                contextResult.Error,
                cancellationToken
            );
            return Result.Failure<AiStructuredResultDto>(contextResult.Error);
        }

        var context = contextResult.Value;
        var lastError = AiApplicationErrors.InvalidStructuredOutput;

        var schema = !string.IsNullOrWhiteSpace(input.JsonSchemaOverride)
            ? input.JsonSchemaOverride
            : context.Profile.JsonSchema;

        /*
         * Timeout total del perfil. Antes cada modelo podía consumir el timeout completo
         * de su conexión y el cliente gRPC terminaba cancelando a mitad de un fallback.
         * El token externo se conserva para persistencia; este token solo limita proveedor(es).
         */
        var executionTimeoutSeconds = ResolveExecutionTimeoutSeconds(context.Profile);
        using var profileTimeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken
        );
        profileTimeout.CancelAfter(TimeSpan.FromSeconds(executionTimeoutSeconds));

        foreach (var candidate in context.Candidates.Select((value, index) => (value, index)))
        {
            if (profileTimeout.IsCancellationRequested)
            {
                lastError = AiApplicationErrors.ProviderTimeout;
                break;
            }

            using var attemptTimeout = CreateProviderAttemptTimeout(
                profileTimeout.Token,
                context.Profile,
                executionTimeoutSeconds,
                context.Candidates.Count,
                candidate.value
            );

            Result<AiProviderChatResponse> response;

            try
            {
                response = await ExecuteChatAttemptAsync(
                    context,
                    candidate.value,
                    true,
                    schema,
                    cancellationToken,
                    attemptTimeout.Token
                );
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await CancelExecutionAfterCallerAbortAsync(
                    context.Execution,
                    input.RequestedBy
                );

                return Result.Failure<AiStructuredResultDto>(
                    AiApplicationErrors.ClientRequestCancelled
                );
            }

            if (response.IsSuccess)
            {
                var validation = structuredValidator.Validate(response.Value.Content, schema);

                if (validation.IsSuccess)
                {
                    await CompleteExecutionAsync(
                        context.Execution,
                        candidate.value,
                        response.Value,
                        validation.Value,
                        input,
                        cancellationToken
                    );

                    return Result.Success(
                        CreateStructuredResult(context.Execution, candidate.value, validation.Value)
                    );
                }

                await audit.PublishAsync(
                    new AiAuditEvent(
                        EventType: AiAuditEventTypes.StructuredValidationFailed,
                        Action: AiAuditActions.Failed,
                        EntityType: AiAuditEntityTypes.Execution,
                        EntityId: context.Execution.Id,
                        ActorUserId: input.RequestedBy,
                        Payload: new
                        {
                            Stage = "structured-validation",
                            ExecutionId = context.Execution.Id,
                            Attempt = context.Execution.Attempts
                                .OrderByDescending(item => item.AttemptNumber)
                                .Select(item => new
                                {
                                    item.Id,
                                    item.AttemptNumber,
                                    item.ConnectionId,
                                    item.ModelId,
                                    ProviderType = item.ProviderType.ToString(),
                                    item.ExternalModelId,
                                })
                                .FirstOrDefault(),
                            ServiceInput = input,
                            JsonSchema = schema,
                            ProviderContent = response.Value.Content,
                            response.Value.RawResponseJson,
                            ValidationError = validation.Error,
                            context.Execution.CorrelationId,
                            context.Execution.RequestHash,
                        },
                        Metadata: new
                        {
                            Stage = "structured-validation",
                            Status = "Failed",
                        },
                        ErrorMessage: validation.Error.Message
                    ),
                    cancellationToken
                );
            }

            lastError = response.IsFailure
                ? response.Error
                : AiApplicationErrors.InvalidStructuredOutput;

            await RegisterFailedAttemptAsync(
                context.Execution,
                candidate.value,
                candidate.index,
                context.Candidates,
                lastError,
                cancellationToken
            );

            if (profileTimeout.IsCancellationRequested)
            {
                lastError = AiApplicationErrors.ProviderTimeout;
                break;
            }
        }

        var failureCode = lastError.Code == AiApplicationErrors.ProviderTimeout.Code
            ? AiApplicationErrors.ProviderTimeout.Code
            : "AI.InvalidStructuredOutput";
        var failureMessage = lastError.Code == AiApplicationErrors.ProviderTimeout.Code
            ? AiApplicationErrors.ProviderTimeout.Message
            : "Ningún modelo devolvió una respuesta estructurada válida.";

        await FailExecutionAsync(
            context.Execution,
            failureCode,
            failureMessage,
            input,
            cancellationToken
        );

        return Result.Failure<AiStructuredResultDto>(lastError);
    }

    public async Task<Result<AiEmbeddingsResultDto>> ExecuteEmbeddingsAsync(
        ExecuteAiEmbeddingsInput input,
        CancellationToken cancellationToken = default
    )
    {
        if (input.Inputs.Count == 0 || input.Inputs.Any(string.IsNullOrWhiteSpace))
        {
            await AuditRejectedRequestAsync(
                input,
                input.ProfileKey,
                AiExecutionType.Embeddings,
                input.CorrelationId,
                input.RequestedBy,
                null,
                AiApplicationErrors.ExecutionFailed,
                cancellationToken
            );
            return Result.Failure<AiEmbeddingsResultDto>(AiApplicationErrors.ExecutionFailed);
        }

        var profile = await profiles.GetByKeyAsync(
            input.ProfileKey.Trim().ToLowerInvariant(),
            cancellationToken
        );

        if (profile is null || profile.IsDeleted)
        {
            await AuditRejectedRequestAsync(
                input,
                input.ProfileKey,
                AiExecutionType.Embeddings,
                input.CorrelationId,
                input.RequestedBy,
                null,
                AiErrors.ProfileNotFound,
                cancellationToken
            );
            return Result.Failure<AiEmbeddingsResultDto>(AiErrors.ProfileNotFound);
        }

        if (!profile.IsActive)
        {
            await AuditRejectedRequestAsync(
                input,
                input.ProfileKey,
                AiExecutionType.Embeddings,
                input.CorrelationId,
                input.RequestedBy,
                null,
                AiApplicationErrors.ProfileIsInactive,
                cancellationToken
            );
            return Result.Failure<AiEmbeddingsResultDto>(AiApplicationErrors.ProfileIsInactive);
        }

        var candidatesResult = await modelSelector.SelectAsync(
            profile,
            AiModelCapability.Embeddings,
            cancellationToken
        );

        if (candidatesResult.IsFailure)
        {
            await AuditRejectedRequestAsync(
                input,
                input.ProfileKey,
                AiExecutionType.Embeddings,
                input.CorrelationId,
                input.RequestedBy,
                null,
                candidatesResult.Error,
                cancellationToken
            );
            return Result.Failure<AiEmbeddingsResultDto>(candidatesResult.Error);
        }

        var execution = AiExecution.Create(
            profile.Id,
            profile.Key,
            profile.PromptTemplateId,
            AiExecutionType.Embeddings,
            input.CorrelationId,
            input.RequestHash,
            input.RequestedBy
        );

        execution.Start(input.RequestedBy);

        await executions.AddAsync(execution, cancellationToken);

        await audit.PublishAsync(
            new AiAuditEvent(
                EventType: AiAuditEventTypes.ExecutionStarted,
                Action: AiAuditActions.Started,
                EntityType: AiAuditEntityTypes.Execution,
                EntityId: execution.Id,
                ActorUserId: input.RequestedBy,
                After: AiAuditSnapshots.From(execution),
                Payload: new
                {
                    execution.Id,
                    execution.ProfileKey,
                    ExecutionType = execution.ExecutionType.ToString(),
                    execution.CorrelationId,
                    execution.RequestHash,
                }
            ),
            cancellationToken
        );

        await audit.PublishAsync(
            new AiAuditEvent(
                EventType: AiAuditEventTypes.ExecutionInputRecorded,
                Action: AiAuditActions.InputRecorded,
                EntityType: AiAuditEntityTypes.Execution,
                EntityId: execution.Id,
                ActorUserId: input.RequestedBy,
                Payload: new
                {
                    Stage = "service-input",
                    ExecutionId = execution.Id,
                    ExecutionType = execution.ExecutionType.ToString(),
                    ServiceInput = input,
                    Candidates = candidatesResult.Value.Select(item => new
                    {
                        ConnectionId = item.Connection.Id,
                        ConnectionName = item.Connection.Name,
                        ProviderType = item.Connection.ProviderType.ToString(),
                        item.Connection.BaseUrl,
                        ModelId = item.Model.Id,
                        ModelName = item.Model.Name,
                        item.Model.ExternalModelId,
                    }),
                    execution.CorrelationId,
                    execution.RequestHash,
                },
                Metadata: new
                {
                    Stage = "service-input",
                    ExecutionType = execution.ExecutionType.ToString(),
                    CandidateCount = candidatesResult.Value.Count,
                }
            ),
            cancellationToken
        );

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var candidates = candidatesResult.Value;
        var lastError = AiApplicationErrors.ExecutionFailed;

        foreach (var candidate in candidates.Select((value, index) => (value, index)))
        {
            var attempt = execution.StartAttempt(
                candidate.value.Connection.Id,
                candidate.value.Model.Id,
                candidate.value.Connection.ProviderType,
                candidate.value.Model.ExternalModelId
            );

            await unitOfWork.SaveChangesAsync(cancellationToken);

            var providerRequest = new AiProviderEmbeddingRequest(input.Inputs);

            await audit.PublishAsync(
                new AiAuditEvent(
                    EventType: AiAuditEventTypes.ProviderAttemptInputRecorded,
                    Action: AiAuditActions.InputRecorded,
                    EntityType: AiAuditEntityTypes.ExecutionAttempt,
                    EntityId: attempt.Id,
                    ActorUserId: input.RequestedBy,
                    Payload: new
                    {
                        Stage = "provider-input",
                        ExecutionId = execution.Id,
                        AttemptId = attempt.Id,
                        attempt.AttemptNumber,
                        ProviderType = candidate.value.Connection.ProviderType.ToString(),
                        ConnectionId = candidate.value.Connection.Id,
                        ConnectionName = candidate.value.Connection.Name,
                        candidate.value.Connection.BaseUrl,
                        ModelId = candidate.value.Model.Id,
                        ModelName = candidate.value.Model.Name,
                        candidate.value.Model.ExternalModelId,
                        Request = providerRequest,
                        execution.CorrelationId,
                        execution.RequestHash,
                    },
                    Metadata: new
                    {
                        Stage = "provider-input",
                        Type = "embeddings",
                    }
                ),
                cancellationToken
            );

            await unitOfWork.SaveChangesAsync(cancellationToken);

            try
            {
                var context = await BuildProviderContextAsync(candidate.value, cancellationToken);

                var provider = providerResolver.ResolveEmbeddingProvider(
                    candidate.value.Connection.ProviderType
                );

                var response = await provider.ExecuteAsync(
                    providerRequest,
                    context,
                    cancellationToken
                );

                var cost = CalculateEmbeddingCost(candidate.value.Model, response.InputTokens);

                execution.CompleteAttempt(
                    attempt.Id,
                    response.InputTokens,
                    0,
                    cost,
                    CalculateDuration(attempt.StartedAtUtc),
                    AiFinishReason.Stop
                );

                execution.Complete(attempt.Id, null, input.RequestedBy);

                await audit.PublishAsync(
                    new AiAuditEvent(
                        EventType: AiAuditEventTypes.ProviderAttemptOutputRecorded,
                        Action: AiAuditActions.OutputRecorded,
                        EntityType: AiAuditEntityTypes.ExecutionAttempt,
                        EntityId: attempt.Id,
                        ActorUserId: input.RequestedBy,
                        After: new
                        {
                            Status = "Completed",
                            response.Embeddings,
                            response.Dimensions,
                            response.InputTokens,
                            response.RawResponseJson,
                            EstimatedCost = cost,
                            DurationMilliseconds = CalculateDuration(attempt.StartedAtUtc),
                        },
                        Payload: new
                        {
                            Stage = "provider-output",
                            ExecutionId = execution.Id,
                            AttemptId = attempt.Id,
                            attempt.AttemptNumber,
                            ProviderType = candidate.value.Connection.ProviderType.ToString(),
                            ConnectionId = candidate.value.Connection.Id,
                            ConnectionName = candidate.value.Connection.Name,
                            ModelId = candidate.value.Model.Id,
                            ModelName = candidate.value.Model.Name,
                            candidate.value.Model.ExternalModelId,
                            Request = providerRequest,
                            Response = response,
                            EstimatedCost = cost,
                            DurationMilliseconds = CalculateDuration(attempt.StartedAtUtc),
                            execution.CorrelationId,
                            execution.RequestHash,
                        },
                        Metadata: new
                        {
                            Stage = "provider-output",
                            Type = "embeddings",
                            Status = "Completed",
                        }
                    ),
                    cancellationToken
                );

                await audit.PublishAsync(
                    new AiAuditEvent(
                        EventType: AiAuditEventTypes.ExecutionCompleted,
                        Action: AiAuditActions.Completed,
                        EntityType: AiAuditEntityTypes.Execution,
                        EntityId: execution.Id,
                        ActorUserId: input.RequestedBy,
                        After: AiAuditSnapshots.From(execution),
                        Payload: AiAuditSnapshots.From(execution)
                    ),
                    cancellationToken
                );

                await audit.PublishAsync(
                    new AiAuditEvent(
                        EventType: AiAuditEventTypes.ExecutionOutputRecorded,
                        Action: AiAuditActions.OutputRecorded,
                        EntityType: AiAuditEntityTypes.Execution,
                        EntityId: execution.Id,
                        ActorUserId: input.RequestedBy,
                        After: new
                        {
                            Status = execution.Status.ToString(),
                            response.Embeddings,
                            response.Dimensions,
                            response.InputTokens,
                            response.RawResponseJson,
                            EstimatedCost = cost,
                            execution.DurationMilliseconds,
                        },
                        Payload: new
                        {
                            Stage = "service-output",
                            ExecutionId = execution.Id,
                            ExecutionType = execution.ExecutionType.ToString(),
                            ServiceInput = input,
                            ServiceOutput = response,
                            Provider = new
                            {
                                ProviderType = candidate.value.Connection.ProviderType.ToString(),
                                ConnectionId = candidate.value.Connection.Id,
                                ConnectionName = candidate.value.Connection.Name,
                                candidate.value.Connection.BaseUrl,
                                ModelId = candidate.value.Model.Id,
                                ModelName = candidate.value.Model.Name,
                                candidate.value.Model.ExternalModelId,
                            },
                            Execution = AiAuditSnapshots.From(execution),
                            execution.CorrelationId,
                            execution.RequestHash,
                        },
                        Metadata: new
                        {
                            Stage = "service-output",
                            ExecutionType = execution.ExecutionType.ToString(),
                            Status = execution.Status.ToString(),
                        }
                    ),
                    cancellationToken
                );

                await snapshotWriter.WriteAsync(
                    execution.Id,
                    execution.ProfileKey,
                    execution.ExecutionType.ToString(),
                    execution.Status.ToString(),
                    input,
                    response,
                    new
                    {
                        candidate.value.Connection.ProviderType,
                        candidate.value.Model.ExternalModelId,
                    },
                    null,
                    null,
                    DateTime.UtcNow,
                    input.CorrelationId,
                    cancellationToken
                );

                await unitOfWork.SaveChangesAsync(cancellationToken);

                return Result.Success(
                    new AiEmbeddingsResultDto(
                        execution.Id,
                        response.Embeddings,
                        response.Dimensions,
                        candidate.value.Connection.Id,
                        candidate.value.Connection.Name,
                        candidate.value.Model.Id,
                        candidate.value.Model.Name,
                        candidate.value.Model.ExternalModelId,
                        candidate.value.Connection.ProviderType.ToString(),
                        response.InputTokens,
                        cost,
                        execution.DurationMilliseconds
                    )
                );
            }
            catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                lastError = AiApplicationErrors.ProviderTimeout;

                execution.FailAttempt(
                    attempt.Id,
                    lastError.Code,
                    lastError.Message,
                    CalculateDuration(attempt.StartedAtUtc)
                );

                await audit.PublishAsync(
                    new AiAuditEvent(
                        EventType: AiAuditEventTypes.ProviderAttemptFailed,
                        Action: AiAuditActions.OutputRecorded,
                        EntityType: AiAuditEntityTypes.ExecutionAttempt,
                        EntityId: attempt.Id,
                        ActorUserId: input.RequestedBy,
                        After: new
                        {
                            Status = "Failed",
                            ErrorCode = lastError.Code,
                            ErrorMessage = lastError.Message,
                            DurationMilliseconds = CalculateDuration(attempt.StartedAtUtc),
                        },
                        Payload: new
                        {
                            Stage = "provider-output",
                            ExecutionId = execution.Id,
                            AttemptId = attempt.Id,
                            attempt.AttemptNumber,
                            ProviderType = candidate.value.Connection.ProviderType.ToString(),
                            ConnectionId = candidate.value.Connection.Id,
                            ConnectionName = candidate.value.Connection.Name,
                            ModelId = candidate.value.Model.Id,
                            ModelName = candidate.value.Model.Name,
                            candidate.value.Model.ExternalModelId,
                            Request = providerRequest,
                            Error = new
                            {
                                Code = lastError.Code,
                                Message = lastError.Message,
                                ExceptionType = exception.GetType().FullName,
                                exception.StackTrace,
                            },
                            DurationMilliseconds = CalculateDuration(attempt.StartedAtUtc),
                            execution.CorrelationId,
                            execution.RequestHash,
                        },
                        Metadata: new
                        {
                            Stage = "provider-output",
                            Type = "embeddings",
                            Status = "Failed",
                        },
                        ErrorMessage: lastError.Message,
                        StackTrace: exception.ToString()
                    ),
                    cancellationToken
                );

                await RegisterFallbackIfNeededAsync(
                    execution,
                    candidate.value,
                    candidate.index,
                    candidates,
                    lastError.Message,
                    cancellationToken
                );
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                lastError = AiApplicationErrors.ProviderOperationFailed;

                execution.FailAttempt(
                    attempt.Id,
                    "AI.ProviderExecutionFailed",
                    exception.Message,
                    CalculateDuration(attempt.StartedAtUtc)
                );

                await audit.PublishAsync(
                    new AiAuditEvent(
                        EventType: AiAuditEventTypes.ProviderAttemptFailed,
                        Action: AiAuditActions.OutputRecorded,
                        EntityType: AiAuditEntityTypes.ExecutionAttempt,
                        EntityId: attempt.Id,
                        ActorUserId: input.RequestedBy,
                        After: new
                        {
                            Status = "Failed",
                            ErrorCode = "AI.ProviderExecutionFailed",
                            ErrorMessage = exception.Message,
                            DurationMilliseconds = CalculateDuration(attempt.StartedAtUtc),
                        },
                        Payload: new
                        {
                            Stage = "provider-output",
                            ExecutionId = execution.Id,
                            AttemptId = attempt.Id,
                            attempt.AttemptNumber,
                            ProviderType = candidate.value.Connection.ProviderType.ToString(),
                            ConnectionId = candidate.value.Connection.Id,
                            ConnectionName = candidate.value.Connection.Name,
                            ModelId = candidate.value.Model.Id,
                            ModelName = candidate.value.Model.Name,
                            candidate.value.Model.ExternalModelId,
                            Request = providerRequest,
                            Error = new
                            {
                                Code = "AI.ProviderExecutionFailed",
                                Message = exception.Message,
                                ExceptionType = exception.GetType().FullName,
                                exception.StackTrace,
                            },
                            DurationMilliseconds = CalculateDuration(attempt.StartedAtUtc),
                            execution.CorrelationId,
                            execution.RequestHash,
                        },
                        Metadata: new
                        {
                            Stage = "provider-output",
                            Type = "embeddings",
                            Status = "Failed",
                        },
                        ErrorMessage: exception.Message,
                        StackTrace: exception.ToString()
                    ),
                    cancellationToken
                );

                await RegisterFallbackIfNeededAsync(
                    execution,
                    candidate.value,
                    candidate.index,
                    candidates,
                    exception.Message,
                    cancellationToken
                );
            }
        }

        await FailExecutionAsync(
            execution,
            "AI.AllProvidersFailed",
            "Todos los proveedores de embeddings fallaron.",
            input,
            cancellationToken
        );

        return Result.Failure<AiEmbeddingsResultDto>(lastError);
    }

    public async Task<Result> CancelAsync(
        Guid executionId,
        string? reason,
        Guid? cancelledBy,
        CancellationToken cancellationToken = default
    )
    {
        var execution = await executions.GetByIdAsync(executionId, cancellationToken);

        if (execution is null)
        {
            return Result.Failure(AiErrors.ExecutionNotFound);
        }

        var before = AiAuditSnapshots.From(execution);

        try
        {
            execution.Cancel(reason, cancelledBy);
        }
        catch (InvalidOperationException)
        {
            return Result.Failure(AiApplicationErrors.ExecutionCannotBeCancelled);
        }

        await audit.PublishAsync(
            new AiAuditEvent(
                EventType: AiAuditEventTypes.ExecutionCancelled,
                Action: AiAuditActions.Cancelled,
                EntityType: AiAuditEntityTypes.Execution,
                EntityId: execution.Id,
                ActorUserId: cancelledBy,
                Before: before,
                After: AiAuditSnapshots.From(execution),
                Payload: AiAuditSnapshots.From(execution)
            ),
            cancellationToken
        );

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    private async Task<Result<ExecutionContext>> PrepareAsync(
        string profileKey,
        AiExecutionType executionType,
        AiModelCapability capability,
        string? correlationId,
        string? requestHash,
        Guid? requestedBy,
        string? requestedByName,
        IReadOnlyCollection<AiExecutionMessageInput> messages,
        IReadOnlyCollection<AiExecutionVariableInput>? variables,
        object serviceInput,
        CancellationToken cancellationToken
    )
    {
        var profile = await profiles.GetByKeyAsync(
            profileKey.Trim().ToLowerInvariant(),
            cancellationToken
        );

        if (profile is null || profile.IsDeleted)
        {
            return Result.Failure<ExecutionContext>(AiErrors.ProfileNotFound);
        }

        if (!profile.IsActive)
        {
            return Result.Failure<ExecutionContext>(AiApplicationErrors.ProfileIsInactive);
        }

        AiPromptTemplate? template = null;

        if (profile.PromptTemplateId.HasValue)
        {
            template = await promptTemplates.GetByIdAsync(
                profile.PromptTemplateId.Value,
                cancellationToken
            );

            if (template is null || template.IsDeleted || !template.IsActive)
            {
                return Result.Failure<ExecutionContext>(AiErrors.PromptTemplateNotFound);
            }
        }

        var compiled = promptCompiler.Compile(
            template,
            messages.Select(item => new AiProviderMessage(
                item.Role,
                item.Content,
                item.Images?
                    .Select(image => new AiProviderImage(image.MimeType, image.Base64Data))
                    .ToArray()
            )).ToArray(),
            variables?.Select(item => new AiPromptVariable(item.Name, item.Value)).ToArray()
        );

        if (compiled.IsFailure)
        {
            return Result.Failure<ExecutionContext>(compiled.Error);
        }

        var candidates = await modelSelector.SelectAsync(profile, capability, cancellationToken);

        if (candidates.IsFailure)
        {
            return Result.Failure<ExecutionContext>(candidates.Error);
        }

        var execution = AiExecution.Create(
            profile.Id,
            profile.Key,
            profile.PromptTemplateId,
            executionType,
            correlationId,
            requestHash,
            requestedBy
        );

        execution.Start(requestedBy);

        await executions.AddAsync(execution, cancellationToken);

        await audit.PublishAsync(
            new AiAuditEvent(
                EventType: AiAuditEventTypes.ExecutionStarted,
                Action: AiAuditActions.Started,
                EntityType: AiAuditEntityTypes.Execution,
                EntityId: execution.Id,
                ActorUserId: requestedBy,
                ActorUserName: requestedByName,
                After: AiAuditSnapshots.From(execution),
                Payload: new
                {
                    execution.Id,
                    execution.ProfileKey,
                    execution.ExecutionType,
                    execution.CorrelationId,
                }
            ),
            cancellationToken
        );

        await audit.PublishAsync(
            new AiAuditEvent(
                EventType: AiAuditEventTypes.ExecutionInputRecorded,
                Action: AiAuditActions.InputRecorded,
                EntityType: AiAuditEntityTypes.Execution,
                EntityId: execution.Id,
                ActorUserId: requestedBy,
                ActorUserName: requestedByName,
                Payload: new
                {
                    Stage = "service-input",
                    ExecutionId = execution.Id,
                    ExecutionType = execution.ExecutionType.ToString(),
                    ServiceInput = serviceInput,
                    CompiledProviderInput = new
                    {
                        Messages = compiled.Value.Messages,
                        profile.Temperature,
                        profile.MaximumOutputTokens,
                        ConfiguredTimeoutSeconds = profile.TimeoutSeconds,
                        EffectiveTimeoutSeconds = ResolveExecutionTimeoutSeconds(profile),
                        ResponseFormat = profile.ResponseFormat.ToString(),
                        profile.JsonSchema,
                    },
                    Candidates = candidates.Value.Select(item => new
                    {
                        ConnectionId = item.Connection.Id,
                        ConnectionName = item.Connection.Name,
                        ProviderType = item.Connection.ProviderType.ToString(),
                        item.Connection.BaseUrl,
                        ModelId = item.Model.Id,
                        ModelName = item.Model.Name,
                        item.Model.ExternalModelId,
                    }),
                    execution.CorrelationId,
                    execution.RequestHash,
                },
                Metadata: new
                {
                    Stage = "service-input",
                    ExecutionType = execution.ExecutionType.ToString(),
                    CandidateCount = candidates.Value.Count,
                }
            ),
            cancellationToken
        );

        if (executionType == AiExecutionType.Chat)
        {
            await audit.PublishAsync(
                new AiAuditEvent(
                    EventType: AiAuditEventTypes.ChatRequested,
                    Action: AiAuditActions.Chat,
                    EntityType: AiAuditEntityTypes.Chat,
                    EntityId: execution.Id,
                    ActorUserId: requestedBy,
                    ActorUserName: requestedByName,
                    Payload: new
                    {
                        Type = "chat",
                        Stage = "request",
                        execution.Id,
                        execution.ProfileKey,
                        Messages = messages.Select(item => new { item.Role, item.Content }),
                        Variables = variables?.Select(item => new { item.Name, item.Value }),
                        execution.CorrelationId,
                    },
                    Metadata: new { Type = "chat", Stage = "request" }
                ),
                cancellationToken
            );
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(
            new ExecutionContext(
                profile,
                execution,
                compiled.Value,
                candidates.Value,
                messages,
                variables,
                requestedBy,
                requestedByName
            )
        );
    }

    private async Task<Result<AiProviderChatResponse>> ExecuteChatAttemptAsync(
        ExecutionContext executionContext,
        AiModelCandidate candidate,
        bool structured,
        string? jsonSchema,
        CancellationToken cancellationToken,
        CancellationToken? providerCancellationToken = null
    )
    {
        var attempt = executionContext.Execution.StartAttempt(
            candidate.Connection.Id,
            candidate.Model.Id,
            candidate.Connection.ProviderType,
            candidate.Model.ExternalModelId
        );

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var providerRequest = new AiProviderChatRequest(
            executionContext.CompiledPrompt.Messages,
            executionContext.Profile.Temperature,
            executionContext.Profile.MaximumOutputTokens,
            structured,
            jsonSchema
        );

        await audit.PublishAsync(
            new AiAuditEvent(
                EventType: AiAuditEventTypes.ProviderAttemptInputRecorded,
                Action: AiAuditActions.InputRecorded,
                EntityType: AiAuditEntityTypes.ExecutionAttempt,
                EntityId: attempt.Id,
                ActorUserId: executionContext.RequestedBy,
                ActorUserName: executionContext.RequestedByName,
                Payload: new
                {
                    Stage = "provider-input",
                    ExecutionId = executionContext.Execution.Id,
                    AttemptId = attempt.Id,
                    attempt.AttemptNumber,
                    ProviderType = candidate.Connection.ProviderType.ToString(),
                    ConnectionId = candidate.Connection.Id,
                    ConnectionName = candidate.Connection.Name,
                    candidate.Connection.BaseUrl,
                    ModelId = candidate.Model.Id,
                    ModelName = candidate.Model.Name,
                    candidate.Model.ExternalModelId,
                    Request = providerRequest,
                    executionContext.Execution.CorrelationId,
                    executionContext.Execution.RequestHash,
                },
                Metadata: new
                {
                    Stage = "provider-input",
                    Structured = structured,
                    HasImages = providerRequest.Messages.Any(item => item.Images?.Count > 0),
                }
            ),
            cancellationToken
        );

        await unitOfWork.SaveChangesAsync(cancellationToken);

        try
        {
            var providerContext = await BuildProviderContextAsync(
                candidate,
                cancellationToken,
                ResolveExecutionTimeoutSeconds(executionContext.Profile)
            );

            var provider = providerResolver.ResolveChatProvider(candidate.Connection.ProviderType);

            var response = await provider.ExecuteAsync(
                providerRequest,
                providerContext,
                providerCancellationToken ?? cancellationToken
            );

            var cost = CalculateChatCost(
                candidate.Model,
                response.InputTokens,
                response.OutputTokens
            );

            executionContext.Execution.CompleteAttempt(
                attempt.Id,
                response.InputTokens,
                response.OutputTokens,
                cost,
                CalculateDuration(attempt.StartedAtUtc),
                ParseFinishReason(response.FinishReason)
            );

            await audit.PublishAsync(
                new AiAuditEvent(
                    EventType: AiAuditEventTypes.ProviderAttemptOutputRecorded,
                    Action: AiAuditActions.OutputRecorded,
                    EntityType: AiAuditEntityTypes.ExecutionAttempt,
                    EntityId: attempt.Id,
                    ActorUserId: executionContext.RequestedBy,
                    ActorUserName: executionContext.RequestedByName,
                    After: new
                    {
                        Status = "Completed",
                        response.Content,
                        response.InputTokens,
                        response.OutputTokens,
                        response.FinishReason,
                        response.RawResponseJson,
                        EstimatedCost = cost,
                        DurationMilliseconds = CalculateDuration(attempt.StartedAtUtc),
                    },
                    Payload: new
                    {
                        Stage = "provider-output",
                        ExecutionId = executionContext.Execution.Id,
                        AttemptId = attempt.Id,
                        attempt.AttemptNumber,
                        ProviderType = candidate.Connection.ProviderType.ToString(),
                        ConnectionId = candidate.Connection.Id,
                        ConnectionName = candidate.Connection.Name,
                        ModelId = candidate.Model.Id,
                        ModelName = candidate.Model.Name,
                        candidate.Model.ExternalModelId,
                        Request = providerRequest,
                        Response = response,
                        EstimatedCost = cost,
                        DurationMilliseconds = CalculateDuration(attempt.StartedAtUtc),
                        executionContext.Execution.CorrelationId,
                        executionContext.Execution.RequestHash,
                    },
                    Metadata: new
                    {
                        Stage = "provider-output",
                        Structured = structured,
                        Status = "Completed",
                    }
                ),
                cancellationToken
            );

            if (!structured)
            {
                await CompleteExecutionAsync(
                    executionContext.Execution,
                    candidate,
                    response,
                    response.Content,
                    executionContext,
                    cancellationToken
                );
            }

            return Result.Success(response);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            executionContext.Execution.FailAttempt(
                attempt.Id,
                AiApplicationErrors.ProviderTimeout.Code,
                AiApplicationErrors.ProviderTimeout.Message,
                CalculateDuration(attempt.StartedAtUtc)
            );

            await audit.PublishAsync(
                new AiAuditEvent(
                    EventType: AiAuditEventTypes.ProviderAttemptFailed,
                    Action: AiAuditActions.OutputRecorded,
                    EntityType: AiAuditEntityTypes.ExecutionAttempt,
                    EntityId: attempt.Id,
                    ActorUserId: executionContext.RequestedBy,
                    ActorUserName: executionContext.RequestedByName,
                    After: new
                    {
                        Status = "Failed",
                        ErrorCode = AiApplicationErrors.ProviderTimeout.Code,
                        ErrorMessage = AiApplicationErrors.ProviderTimeout.Message,
                        DurationMilliseconds = CalculateDuration(attempt.StartedAtUtc),
                    },
                    Payload: new
                    {
                        Stage = "provider-output",
                        ExecutionId = executionContext.Execution.Id,
                        AttemptId = attempt.Id,
                        attempt.AttemptNumber,
                        ProviderType = candidate.Connection.ProviderType.ToString(),
                        ConnectionId = candidate.Connection.Id,
                        ConnectionName = candidate.Connection.Name,
                        ModelId = candidate.Model.Id,
                        ModelName = candidate.Model.Name,
                        candidate.Model.ExternalModelId,
                        Request = providerRequest,
                        Error = new
                        {
                            Code = AiApplicationErrors.ProviderTimeout.Code,
                            Message = AiApplicationErrors.ProviderTimeout.Message,
                            ExceptionType = exception.GetType().FullName,
                            exception.StackTrace,
                        },
                        DurationMilliseconds = CalculateDuration(attempt.StartedAtUtc),
                        executionContext.Execution.CorrelationId,
                        executionContext.Execution.RequestHash,
                    },
                    Metadata: new
                    {
                        Stage = "provider-output",
                        Structured = structured,
                        Status = "Failed",
                    },
                    ErrorMessage: AiApplicationErrors.ProviderTimeout.Message,
                    StackTrace: exception.ToString()
                ),
                cancellationToken
            );

            return Result.Failure<AiProviderChatResponse>(
                AiApplicationErrors.ProviderTimeout
            );
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            executionContext.Execution.FailAttempt(
                attempt.Id,
                "AI.ProviderExecutionFailed",
                exception.Message,
                CalculateDuration(attempt.StartedAtUtc)
            );

            await audit.PublishAsync(
                new AiAuditEvent(
                    EventType: AiAuditEventTypes.ProviderAttemptFailed,
                    Action: AiAuditActions.OutputRecorded,
                    EntityType: AiAuditEntityTypes.ExecutionAttempt,
                    EntityId: attempt.Id,
                    ActorUserId: executionContext.RequestedBy,
                    ActorUserName: executionContext.RequestedByName,
                    After: new
                    {
                        Status = "Failed",
                        ErrorCode = "AI.ProviderExecutionFailed",
                        ErrorMessage = exception.Message,
                        DurationMilliseconds = CalculateDuration(attempt.StartedAtUtc),
                    },
                    Payload: new
                    {
                        Stage = "provider-output",
                        ExecutionId = executionContext.Execution.Id,
                        AttemptId = attempt.Id,
                        attempt.AttemptNumber,
                        ProviderType = candidate.Connection.ProviderType.ToString(),
                        ConnectionId = candidate.Connection.Id,
                        ConnectionName = candidate.Connection.Name,
                        ModelId = candidate.Model.Id,
                        ModelName = candidate.Model.Name,
                        candidate.Model.ExternalModelId,
                        Request = providerRequest,
                        Error = new
                        {
                            Code = "AI.ProviderExecutionFailed",
                            Message = exception.Message,
                            ExceptionType = exception.GetType().FullName,
                            exception.StackTrace,
                        },
                        DurationMilliseconds = CalculateDuration(attempt.StartedAtUtc),
                        executionContext.Execution.CorrelationId,
                        executionContext.Execution.RequestHash,
                    },
                    Metadata: new
                    {
                        Stage = "provider-output",
                        Structured = structured,
                        Status = "Failed",
                    },
                    ErrorMessage: exception.Message,
                    StackTrace: exception.ToString()
                ),
                cancellationToken
            );

            return Result.Failure<AiProviderChatResponse>(
                AiApplicationErrors.ProviderOperationFailed
            );
        }
    }

    private async Task CompleteExecutionAsync(
        AiExecution execution,
        AiModelCandidate candidate,
        AiProviderChatResponse response,
        string output,
        object request,
        CancellationToken cancellationToken
    )
    {
        var attempt = execution
            .Attempts.OrderByDescending(item => item.AttemptNumber)
            .First(item => item.Status == AiAttemptStatus.Completed);

        execution.Complete(attempt.Id, null, null);

        var conversation = request as ExecutionContext;
        var serviceInput = conversation is null
            ? request
            : new
            {
                conversation.Execution.ProfileKey,
                conversation.Messages,
                conversation.Variables,
                conversation.Execution.CorrelationId,
                conversation.Execution.RequestHash,
                conversation.RequestedBy,
                conversation.RequestedByName,
            };

        await audit.PublishAsync(
            new AiAuditEvent(
                EventType: AiAuditEventTypes.ExecutionCompleted,
                Action: AiAuditActions.Completed,
                EntityType: AiAuditEntityTypes.Execution,
                EntityId: execution.Id,
                ActorUserId: conversation?.RequestedBy,
                ActorUserName: conversation?.RequestedByName,
                After: AiAuditSnapshots.From(execution),
                Payload: AiAuditSnapshots.From(execution)
            ),
            cancellationToken
        );

        await audit.PublishAsync(
            new AiAuditEvent(
                EventType: AiAuditEventTypes.ExecutionOutputRecorded,
                Action: AiAuditActions.OutputRecorded,
                EntityType: AiAuditEntityTypes.Execution,
                EntityId: execution.Id,
                ActorUserId: conversation?.RequestedBy,
                ActorUserName: conversation?.RequestedByName,
                After: new
                {
                    Status = execution.Status.ToString(),
                    NormalizedOutput = output,
                    ProviderResponse = response,
                    ProviderType = candidate.Connection.ProviderType.ToString(),
                    ConnectionId = candidate.Connection.Id,
                    ConnectionName = candidate.Connection.Name,
                    ModelId = candidate.Model.Id,
                    ModelName = candidate.Model.Name,
                    candidate.Model.ExternalModelId,
                    execution.InputTokens,
                    execution.OutputTokens,
                    execution.EstimatedCost,
                    execution.DurationMilliseconds,
                    FinishReason = execution.FinishReason.ToString(),
                },
                Payload: new
                {
                    Stage = "service-output",
                    ExecutionId = execution.Id,
                    ExecutionType = execution.ExecutionType.ToString(),
                    ServiceInput = serviceInput,
                    ServiceOutput = new
                    {
                        NormalizedOutput = output,
                        ProviderResponse = response,
                        response.RawResponseJson,
                    },
                    Provider = new
                    {
                        ProviderType = candidate.Connection.ProviderType.ToString(),
                        ConnectionId = candidate.Connection.Id,
                        ConnectionName = candidate.Connection.Name,
                        candidate.Connection.BaseUrl,
                        ModelId = candidate.Model.Id,
                        ModelName = candidate.Model.Name,
                        candidate.Model.ExternalModelId,
                    },
                    Execution = AiAuditSnapshots.From(execution),
                    execution.CorrelationId,
                    execution.RequestHash,
                },
                Metadata: new
                {
                    Stage = "service-output",
                    ExecutionType = execution.ExecutionType.ToString(),
                    Status = execution.Status.ToString(),
                }
            ),
            cancellationToken
        );

        if (conversation is not null)
        {
            await audit.PublishAsync(
                new AiAuditEvent(
                    EventType: AiAuditEventTypes.ChatCompleted,
                    Action: AiAuditActions.Chat,
                    EntityType: AiAuditEntityTypes.Chat,
                    EntityId: execution.Id,
                    ActorUserId: conversation.RequestedBy,
                    ActorUserName: conversation.RequestedByName,
                    Payload: new
                    {
                        Type = "chat",
                        Stage = "completed",
                        execution.Id,
                        execution.ProfileKey,
                        Messages = conversation.Messages.Select(item => new
                        {
                            item.Role,
                            item.Content,
                        }),
                        AssistantResponse = output,
                        ProviderType = candidate.Connection.ProviderType.ToString(),
                        Connection = candidate.Connection.Name,
                        Model = candidate.Model.ExternalModelId,
                        response.InputTokens,
                        response.OutputTokens,
                        response.FinishReason,
                        execution.EstimatedCost,
                        execution.DurationMilliseconds,
                        execution.CorrelationId,
                    },
                    Metadata: new { Type = "chat", Stage = "completed" }
                ),
                cancellationToken
            );
        }

        var snapshotRequest = serviceInput;

        await snapshotWriter.WriteAsync(
            execution.Id,
            execution.ProfileKey,
            execution.ExecutionType.ToString(),
            execution.Status.ToString(),
            snapshotRequest,
            new
            {
                Content = output,
                response.InputTokens,
                response.OutputTokens,
                response.FinishReason,
            },
            new
            {
                ProviderType = candidate.Connection.ProviderType.ToString(),
                candidate.Connection.Name,
                candidate.Model.ExternalModelId,
                response.RawResponseJson,
            },
            null,
            null,
            DateTime.UtcNow,
            execution.CorrelationId,
            cancellationToken
        );

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task RegisterFailedAttemptAsync(
        AiExecution execution,
        AiModelCandidate current,
        int currentIndex,
        IReadOnlyCollection<AiModelCandidate> candidates,
        Error error,
        CancellationToken cancellationToken
    )
    {
        await RegisterFallbackIfNeededAsync(
            execution,
            current,
            currentIndex,
            candidates,
            error.Message,
            cancellationToken
        );

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task RegisterFallbackIfNeededAsync(
        AiExecution execution,
        AiModelCandidate current,
        int currentIndex,
        IReadOnlyCollection<AiModelCandidate> candidates,
        string reason,
        CancellationToken cancellationToken
    )
    {
        if (currentIndex >= candidates.Count - 1)
        {
            return;
        }

        var next = candidates.ElementAt(currentIndex + 1);

        execution.RegisterFallback(current.Model.Id, next.Model.Id, reason);

        await audit.PublishAsync(
            new AiAuditEvent(
                EventType: AiAuditEventTypes.ExecutionFallbackUsed,
                Action: AiAuditActions.FallbackUsed,
                EntityType: AiAuditEntityTypes.Execution,
                EntityId: execution.Id,
                Payload: new
                {
                    PreviousModelId = current.Model.Id,
                    NextModelId = next.Model.Id,
                    Reason = reason,
                }
            ),
            cancellationToken
        );
    }

    private async Task FailExecutionAsync(
        AiExecution execution,
        string errorCode,
        string errorMessage,
        object request,
        CancellationToken cancellationToken
    )
    {
        execution.Fail(errorCode, errorMessage, null);

        var actorUserId = request switch
        {
            ExecuteAiChatInput chat => chat.RequestedBy,
            ExecuteAiStructuredInput structured => structured.RequestedBy,
            ExecuteAiEmbeddingsInput embeddings => embeddings.RequestedBy,
            ExecutionContext context => context.RequestedBy,
            _ => null,
        };
        var actorUserName = request switch
        {
            ExecuteAiChatInput chat => chat.RequestedByName,
            ExecutionContext context => context.RequestedByName,
            _ => null,
        };

        await audit.PublishAsync(
            new AiAuditEvent(
                EventType: AiAuditEventTypes.ExecutionFailed,
                Action: AiAuditActions.Failed,
                EntityType: AiAuditEntityTypes.Execution,
                EntityId: execution.Id,
                After: AiAuditSnapshots.From(execution),
                Payload: AiAuditSnapshots.From(execution),
                ErrorMessage: errorMessage
            ),
            cancellationToken
        );

        await audit.PublishAsync(
            new AiAuditEvent(
                EventType: AiAuditEventTypes.ExecutionOutputRecorded,
                Action: AiAuditActions.OutputRecorded,
                EntityType: AiAuditEntityTypes.Execution,
                EntityId: execution.Id,
                ActorUserId: actorUserId,
                ActorUserName: actorUserName,
                After: new
                {
                    Status = execution.Status.ToString(),
                    errorCode,
                    errorMessage,
                    execution.DurationMilliseconds,
                    FinishReason = execution.FinishReason.ToString(),
                },
                Payload: new
                {
                    Stage = "service-output",
                    ExecutionId = execution.Id,
                    ExecutionType = execution.ExecutionType.ToString(),
                    ServiceInput = request,
                    ServiceOutput = new
                    {
                        Success = false,
                        ErrorCode = errorCode,
                        ErrorMessage = errorMessage,
                    },
                    Execution = AiAuditSnapshots.From(execution),
                    execution.CorrelationId,
                    execution.RequestHash,
                },
                Metadata: new
                {
                    Stage = "service-output",
                    ExecutionType = execution.ExecutionType.ToString(),
                    Status = execution.Status.ToString(),
                },
                ErrorMessage: errorMessage
            ),
            cancellationToken
        );

        if (request is ExecuteAiChatInput failedChat)
        {
            await audit.PublishAsync(
                new AiAuditEvent(
                    EventType: AiAuditEventTypes.ChatFailed,
                    Action: AiAuditActions.Chat,
                    EntityType: AiAuditEntityTypes.Chat,
                    EntityId: execution.Id,
                    ActorUserId: failedChat.RequestedBy,
                    ActorUserName: failedChat.RequestedByName,
                    Payload: new
                    {
                        Type = "chat",
                        Stage = "failed",
                        execution.Id,
                        execution.ProfileKey,
                        Messages = failedChat.Messages.Select(item => new
                        {
                            item.Role,
                            item.Content,
                        }),
                        errorCode,
                        errorMessage,
                        execution.CorrelationId,
                    },
                    Metadata: new { Type = "chat", Stage = "failed" },
                    ErrorMessage: errorMessage
                ),
                cancellationToken
            );
        }

        await snapshotWriter.WriteAsync(
            execution.Id,
            execution.ProfileKey,
            execution.ExecutionType.ToString(),
            execution.Status.ToString(),
            request,
            null,
            null,
            errorCode,
            errorMessage,
            DateTime.UtcNow,
            execution.CorrelationId,
            cancellationToken
        );

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task<AiProviderContext> BuildProviderContextAsync(
        AiModelCandidate candidate,
        CancellationToken cancellationToken,
        int? maximumTimeoutSeconds = null
    )
    {
        var secret = await secretResolver.ResolveAsync(
            candidate.Connection.SecretReference,
            cancellationToken
        );

        // Connection timeouts remain useful for health/discovery operations. Long-running
        // inference is governed by the profile budget and its linked cancellation token,
        // so a short connection timeout must not cancel chat or extraction prematurely.
        var effectiveTimeoutSeconds = maximumTimeoutSeconds.HasValue
            ? maximumTimeoutSeconds.Value
            : candidate.Connection.TimeoutSeconds;

        return new AiProviderContext(
            candidate.Connection.Id,
            candidate.Connection.Name,
            candidate.Connection.ProviderType,
            candidate.Connection.BaseUrl,
            secret,
            effectiveTimeoutSeconds,
            candidate.Model.Id,
            candidate.Model.Name,
            candidate.Model.ExternalModelId,
            candidate.Model.Capabilities
        );
    }

    private async Task AuditRejectedRequestAsync(
        object serviceInput,
        string profileKey,
        AiExecutionType executionType,
        string? correlationId,
        Guid? requestedBy,
        string? requestedByName,
        Error error,
        CancellationToken cancellationToken
    )
    {
        await audit.PublishAsync(
            new AiAuditEvent(
                EventType: AiAuditEventTypes.ExecutionRejected,
                Action: AiAuditActions.Rejected,
                EntityType: AiAuditEntityTypes.Execution,
                ActorUserId: requestedBy,
                ActorUserName: requestedByName,
                Payload: new
                {
                    Stage = "service-input",
                    Status = "Rejected",
                    ExecutionType = executionType.ToString(),
                    ProfileKey = profileKey,
                    ServiceInput = serviceInput,
                    Error = new { error.Code, error.Message },
                    CorrelationId = correlationId,
                },
                Metadata: new
                {
                    Stage = "service-input",
                    Status = "Rejected",
                    ExecutionType = executionType.ToString(),
                },
                ErrorMessage: error.Message,
                CorrelationId: correlationId
            ),
            cancellationToken
        );

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task CancelExecutionAfterCallerAbortAsync(
        AiExecution execution,
        Guid? cancelledBy
    )
    {
        try
        {
            execution.Cancel(
                "La solicitud HTTP fue cancelada por el cliente antes de finalizar.",
                cancelledBy
            );

            using var cleanupTimeout = new CancellationTokenSource(
                TimeSpan.FromSeconds(10)
            );

            await unitOfWork.SaveChangesAsync(cleanupTimeout.Token);
        }
        catch (InvalidOperationException exception)
        {
            logger.LogDebug(
                exception,
                "AI execution {ExecutionId} could not be marked as cancelled after the caller disconnected.",
                execution.Id
            );
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Failed to persist cancellation for AI execution {ExecutionId} after the caller disconnected.",
                execution.Id
            );
        }
    }

    private CancellationTokenSource CreateProviderAttemptTimeout(
        CancellationToken profileCancellationToken,
        AiProfile profile,
        int profileTimeoutSeconds,
        int candidateCount,
        AiModelCandidate candidate
    )
    {
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            profileCancellationToken
        );
        var configuredAttemptSeconds = ReadPositiveInt(
            configuration[
                $"AI:Execution:Profiles:{profile.Key}:ProviderAttemptTimeoutSeconds"
            ],
            0
        );
        var fairShareSeconds = (int)Math.Ceiling(
            profileTimeoutSeconds / (double)Math.Max(1, candidateCount)
        );

        // Un único modelo local debe poder consumir todo el presupuesto del perfil.
        // El techo anterior de 300 segundos hacía inútil aumentar el timeout del perfil.
        var defaultAttemptSeconds = candidateCount == 1
            ? profileTimeoutSeconds
            : candidate.Model.IsLocal
                ? Math.Max(fairShareSeconds, Math.Min(profileTimeoutSeconds, 900))
                : fairShareSeconds;
        var attemptSeconds = Math.Clamp(
            configuredAttemptSeconds > 0 ? configuredAttemptSeconds : defaultAttemptSeconds,
            AiConstants.MinimumTimeoutSeconds,
            profileTimeoutSeconds
        );

        timeout.CancelAfter(TimeSpan.FromSeconds(attemptSeconds));
        return timeout;
    }

    private int ResolveExecutionTimeoutSeconds(AiProfile profile)
    {
        var configuredTimeoutSeconds = ReadPositiveInt(
            configuration[$"AI:Execution:Profiles:{profile.Key}:TimeoutSeconds"],
            profile.TimeoutSeconds
        );

        return Math.Clamp(
            configuredTimeoutSeconds,
            AiConstants.MinimumTimeoutSeconds,
            AiConstants.MaximumTimeoutSeconds
        );
    }

    private static int ReadPositiveInt(string? value, int fallback)
    {
        return int.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;
    }

    private static AiChatResultDto CreateChatResult(
        AiExecution execution,
        AiModelCandidate candidate,
        AiProviderChatResponse response
    )
    {
        return new AiChatResultDto(
            execution.Id,
            response.Content,
            candidate.Connection.Id,
            candidate.Connection.Name,
            candidate.Model.Id,
            candidate.Model.Name,
            candidate.Model.ExternalModelId,
            candidate.Connection.ProviderType.ToString(),
            new AiTokenUsageDto(
                response.InputTokens,
                response.OutputTokens,
                response.InputTokens + response.OutputTokens
            ),
            execution.EstimatedCost,
            execution.DurationMilliseconds,
            response.FinishReason
        );
    }

    private static AiStructuredResultDto CreateStructuredResult(
        AiExecution execution,
        AiModelCandidate candidate,
        string jsonContent
    )
    {
        return new AiStructuredResultDto(
            execution.Id,
            jsonContent,
            candidate.Connection.Id,
            candidate.Connection.Name,
            candidate.Model.Id,
            candidate.Model.Name,
            candidate.Model.ExternalModelId,
            candidate.Connection.ProviderType.ToString(),
            new AiTokenUsageDto(
                execution.InputTokens,
                execution.OutputTokens,
                execution.InputTokens + execution.OutputTokens
            ),
            execution.EstimatedCost,
            execution.DurationMilliseconds,
            execution.FinishReason.ToString()
        );
    }

    private static decimal CalculateChatCost(AiModel model, int inputTokens, int outputTokens)
    {
        return (inputTokens / 1_000_000m * (model.InputCostPerMillionTokens ?? 0m))
            + (outputTokens / 1_000_000m * (model.OutputCostPerMillionTokens ?? 0m));
    }

    private static decimal CalculateEmbeddingCost(AiModel model, int inputTokens)
    {
        return inputTokens / 1_000_000m * (model.InputCostPerMillionTokens ?? 0m);
    }

    private static bool HasImages(IReadOnlyCollection<AiExecutionMessageInput> messages)
    {
        return messages.Any(message => message.Images?.Count > 0);
    }

    private static AiFinishReason ParseFinishReason(string? finishReason)
    {
        return Enum.TryParse<AiFinishReason>(finishReason, true, out var parsed)
            ? parsed
            : AiFinishReason.Unknown;
    }

    private static long CalculateDuration(DateTime startedAtUtc)
    {
        return Math.Max(0, (long)(DateTime.UtcNow - startedAtUtc).TotalMilliseconds);
    }

    private sealed record ExecutionContext(
        AiProfile Profile,
        AiExecution Execution,
        AiCompiledPrompt CompiledPrompt,
        IReadOnlyCollection<AiModelCandidate> Candidates,
        IReadOnlyCollection<AiExecutionMessageInput> Messages,
        IReadOnlyCollection<AiExecutionVariableInput>? Variables,
        Guid? RequestedBy,
        string? RequestedByName
    );
}
