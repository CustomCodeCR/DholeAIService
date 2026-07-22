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
    IUnitOfWork unitOfWork
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
            AiModelCapability.Chat,
            input.CorrelationId,
            input.RequestHash,
            input.RequestedBy,
            input.RequestedByName,
            input.Messages,
            input.Variables,
            cancellationToken
        );

        if (contextResult.IsFailure)
        {
            return Result.Failure<AiChatResultDto>(contextResult.Error);
        }

        var context = contextResult.Value;
        var lastError = AiApplicationErrors.ExecutionFailed;

        foreach (var candidate in context.Candidates.Select((value, index) => (value, index)))
        {
            var result = await ExecuteChatAttemptAsync(
                context,
                candidate.value,
                false,
                null,
                cancellationToken
            );

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
        }

        await FailExecutionAsync(
            context.Execution,
            "AI.AllProvidersFailed",
            "Todos los proveedores configurados fallaron.",
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
            AiModelCapability.StructuredOutput,
            input.CorrelationId,
            input.RequestHash,
            input.RequestedBy,
            null,
            input.Messages,
            input.Variables,
            cancellationToken
        );

        if (contextResult.IsFailure)
        {
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
        using var profileTimeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken
        );
        profileTimeout.CancelAfter(TimeSpan.FromSeconds(context.Profile.TimeoutSeconds));

        foreach (var candidate in context.Candidates.Select((value, index) => (value, index)))
        {
            if (profileTimeout.IsCancellationRequested)
            {
                lastError = AiApplicationErrors.ProviderTimeout;
                break;
            }

            var response = await ExecuteChatAttemptAsync(
                context,
                candidate.value,
                true,
                schema,
                cancellationToken,
                profileTimeout.Token
            );

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
            return Result.Failure<AiEmbeddingsResultDto>(AiApplicationErrors.ExecutionFailed);
        }

        var profile = await profiles.GetByKeyAsync(
            input.ProfileKey.Trim().ToLowerInvariant(),
            cancellationToken
        );

        if (profile is null || profile.IsDeleted)
        {
            return Result.Failure<AiEmbeddingsResultDto>(AiErrors.ProfileNotFound);
        }

        if (!profile.IsActive)
        {
            return Result.Failure<AiEmbeddingsResultDto>(AiApplicationErrors.ProfileIsInactive);
        }

        var candidatesResult = await modelSelector.SelectAsync(
            profile,
            AiModelCapability.Embeddings,
            cancellationToken
        );

        if (candidatesResult.IsFailure)
        {
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

            try
            {
                var context = await BuildProviderContextAsync(candidate.value, cancellationToken);

                var provider = providerResolver.ResolveEmbeddingProvider(
                    candidate.value.Connection.ProviderType
                );

                var response = await provider.ExecuteAsync(
                    new AiProviderEmbeddingRequest(input.Inputs),
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
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                lastError = AiApplicationErrors.ProviderTimeout;

                execution.FailAttempt(
                    attempt.Id,
                    lastError.Code,
                    lastError.Message,
                    CalculateDuration(attempt.StartedAtUtc)
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
            messages.Select(item => new AiProviderMessage(item.Role, item.Content)).ToArray(),
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

        try
        {
            var providerContext = await BuildProviderContextAsync(candidate, cancellationToken);

            var provider = providerResolver.ResolveChatProvider(candidate.Connection.ProviderType);

            var response = await provider.ExecuteAsync(
                new AiProviderChatRequest(
                    executionContext.CompiledPrompt.Messages,
                    executionContext.Profile.Temperature,
                    executionContext.Profile.MaximumOutputTokens,
                    structured,
                    jsonSchema
                ),
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
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            executionContext.Execution.FailAttempt(
                attempt.Id,
                AiApplicationErrors.ProviderTimeout.Code,
                AiApplicationErrors.ProviderTimeout.Message,
                CalculateDuration(attempt.StartedAtUtc)
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

        var snapshotRequest = conversation is null
            ? request
            : new
            {
                conversation.Execution.ProfileKey,
                Messages = conversation.Messages,
                Variables = conversation.Variables,
                conversation.Execution.CorrelationId,
            };

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
        CancellationToken cancellationToken
    )
    {
        var secret = await secretResolver.ResolveAsync(
            candidate.Connection.SecretReference,
            cancellationToken
        );

        return new AiProviderContext(
            candidate.Connection.Id,
            candidate.Connection.Name,
            candidate.Connection.ProviderType,
            candidate.Connection.BaseUrl,
            secret,
            candidate.Connection.TimeoutSeconds,
            candidate.Model.Id,
            candidate.Model.Name,
            candidate.Model.ExternalModelId,
            candidate.Model.Capabilities
        );
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
