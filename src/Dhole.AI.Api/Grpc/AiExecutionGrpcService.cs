
using CustomCodeFramework.Cqrs.Dispatching;
using Dhole.AI.Application.Features.Executions.Cancel;
using Dhole.AI.Application.Features.Executions.ExecuteChat;
using Dhole.AI.Application.Features.Executions.ExecuteEmbeddings;
using Dhole.AI.Application.Features.Executions.ExecuteStructured;
using Dhole.AI.Application.Features.Executions.GetById;
using Dhole.AI.Contracts.Executions.Response;
using Dhole.AI.Contracts.Grpc;
using Grpc.Core;

namespace Dhole.AI.Api.Grpc;

public sealed class AiExecutionGrpcService(
    ICommandDispatcher commandDispatcher,
    IQueryDispatcher queryDispatcher
) : AiExecutionGrpc.AiExecutionGrpcBase
{
    public override async Task<ExecuteAiChatGrpcResponse> ExecuteChat(
        ExecuteAiChatGrpcRequest request,
        ServerCallContext context
    )
    {
        var result = await commandDispatcher.DispatchAsync(
            new ExecuteChatCommand(
                request.ProfileKey,
                AiGrpcMappings.ToMessages(request.Messages),
                AiGrpcMappings.ToVariables(request.Variables),
                EmptyToNull(request.CorrelationId),
                EmptyToNull(request.RequestHash),
                AiGrpcMappings.ParseOptionalGuid(request.RequestedBy),
                EmptyToNull(request.RequestedByName)
            ),
            context.CancellationToken
        );

        if (result.IsFailure)
        {
            return new ExecuteAiChatGrpcResponse
            {
                Success = false,
                ErrorCode = result.Error.Code,
                ErrorMessage = result.Error.Message,
            };
        }

        var value = result.Value;

        return new ExecuteAiChatGrpcResponse
        {
            Success = true,
            ExecutionId = value.ExecutionId.ToString(),
            Content = value.Content,
            SelectedModel = AiGrpcMappings.ToSelectedModel(value),
            TokenUsage = AiGrpcMappings.ToGrpc(value.TokenUsage),
            EstimatedCost = (double)value.EstimatedCost,
            DurationMilliseconds = value.DurationMilliseconds,
            FinishReason = value.FinishReason,
        };
    }

    public override async Task ExecuteChatStream(
        ExecuteAiChatGrpcRequest request,
        IServerStreamWriter<AiChatStreamChunkGrpcResponse> responseStream,
        ServerCallContext context
    )
    {
        var result = await commandDispatcher.DispatchAsync(
            new ExecuteChatCommand(
                request.ProfileKey,
                AiGrpcMappings.ToMessages(request.Messages),
                AiGrpcMappings.ToVariables(request.Variables),
                EmptyToNull(request.CorrelationId),
                EmptyToNull(request.RequestHash),
                AiGrpcMappings.ParseOptionalGuid(request.RequestedBy),
                EmptyToNull(request.RequestedByName)
            ),
            context.CancellationToken
        );

        if (result.IsFailure)
        {
            await responseStream.WriteAsync(
                new AiChatStreamChunkGrpcResponse
                {
                    Success = false,
                    IsCompleted = true,
                    ErrorCode = result.Error.Code,
                    ErrorMessage = result.Error.Message,
                }
            );

            return;
        }

        await responseStream.WriteAsync(
            new AiChatStreamChunkGrpcResponse
            {
                Success = true,
                ExecutionId = result.Value.ExecutionId.ToString(),
                Content = result.Value.Content,
                Index = 0,
                IsCompleted = true,
                FinishReason = result.Value.FinishReason,
                TokenUsage = AiGrpcMappings.ToGrpc(result.Value.TokenUsage),
            }
        );
    }

    public override async Task<ExecuteAiStructuredGrpcResponse> ExecuteStructured(
        ExecuteAiStructuredGrpcRequest request,
        ServerCallContext context
    )
    {
        var result = await commandDispatcher.DispatchAsync(
            new ExecuteStructuredCommand(
                request.ProfileKey,
                AiGrpcMappings.ToMessages(request.Messages),
                AiGrpcMappings.ToVariables(request.Variables),
                EmptyToNull(request.JsonSchemaOverride),
                EmptyToNull(request.CorrelationId),
                EmptyToNull(request.RequestHash),
                AiGrpcMappings.ParseOptionalGuid(request.RequestedBy)
            ),
            context.CancellationToken
        );

        if (result.IsFailure)
        {
            return new ExecuteAiStructuredGrpcResponse
            {
                Success = false,
                ErrorCode = result.Error.Code,
                ErrorMessage = result.Error.Message,
            };
        }

        var value = result.Value;

        return new ExecuteAiStructuredGrpcResponse
        {
            Success = true,
            ExecutionId = value.ExecutionId.ToString(),
            JsonContent = value.JsonContent,
            SelectedModel = AiGrpcMappings.ToSelectedModel(value),
            TokenUsage = AiGrpcMappings.ToGrpc(value.TokenUsage),
            EstimatedCost = (double)value.EstimatedCost,
            DurationMilliseconds = value.DurationMilliseconds,
            FinishReason = value.FinishReason,
        };
    }

    public override async Task<ExecuteAiEmbeddingsGrpcResponse> ExecuteEmbeddings(
        ExecuteAiEmbeddingsGrpcRequest request,
        ServerCallContext context
    )
    {
        var result = await commandDispatcher.DispatchAsync(
            new ExecuteEmbeddingsCommand(
                request.ProfileKey,
                request.Inputs.ToArray(),
                EmptyToNull(request.CorrelationId),
                EmptyToNull(request.RequestHash),
                AiGrpcMappings.ParseOptionalGuid(request.RequestedBy)
            ),
            context.CancellationToken
        );

        if (result.IsFailure)
        {
            return new ExecuteAiEmbeddingsGrpcResponse
            {
                Success = false,
                ErrorCode = result.Error.Code,
                ErrorMessage = result.Error.Message,
            };
        }

        var value = result.Value;
        var response = new ExecuteAiEmbeddingsGrpcResponse
        {
            Success = true,
            ExecutionId = value.ExecutionId.ToString(),
            Dimensions = value.Dimensions,
            SelectedModel = AiGrpcMappings.ToSelectedModel(value),
            InputTokens = value.InputTokens,
            EstimatedCost = (double)value.EstimatedCost,
            DurationMilliseconds = value.DurationMilliseconds,
        };

        foreach (var vector in value.Embeddings)
        {
            var embedding = new AiEmbeddingVectorGrpcModel();
            embedding.Values.AddRange(vector);
            response.Embeddings.Add(embedding);
        }

        return response;
    }

    public override async Task<AiExecutionGrpcResponse> GetExecution(
        GetAiExecutionGrpcRequest request,
        ServerCallContext context
    )
    {
        if (!Guid.TryParse(request.ExecutionId, out var executionId))
        {
            return new AiExecutionGrpcResponse
            {
                Success = false,
                ErrorCode = "AI.InvalidExecutionId",
                ErrorMessage = "El identificador de la ejecución no es válido.",
            };
        }

        var result = await queryDispatcher.DispatchAsync(
            new GetExecutionByIdQuery(executionId),
            context.CancellationToken
        );

        if (result.IsFailure)
        {
            return new AiExecutionGrpcResponse
            {
                Success = false,
                ErrorCode = result.Error.Code,
                ErrorMessage = result.Error.Message,
            };
        }

        return ToGrpc(result.Value);
    }

    public override async Task<CancelAiExecutionGrpcResponse> CancelExecution(
        CancelAiExecutionGrpcRequest request,
        ServerCallContext context
    )
    {
        if (!Guid.TryParse(request.ExecutionId, out var executionId))
        {
            return new CancelAiExecutionGrpcResponse
            {
                Success = false,
                ErrorCode = "AI.InvalidExecutionId",
                ErrorMessage = "El identificador de la ejecución no es válido.",
            };
        }

        var result = await commandDispatcher.DispatchAsync(
            new CancelExecutionCommand(
                executionId,
                EmptyToNull(request.Reason),
                AiGrpcMappings.ParseOptionalGuid(request.CancelledBy)
            ),
            context.CancellationToken
        );

        return result.IsSuccess
            ? new CancelAiExecutionGrpcResponse
            {
                Success = true,
                ExecutionId = executionId.ToString(),
                Status = "Cancelled",
            }
            : new CancelAiExecutionGrpcResponse
            {
                Success = false,
                ExecutionId = executionId.ToString(),
                ErrorCode = result.Error.Code,
                ErrorMessage = result.Error.Message,
            };
    }

    private static AiExecutionGrpcResponse ToGrpc(AiExecutionDto execution)
    {
        var response = new AiExecutionGrpcResponse
        {
            Success = true,
            ExecutionId = execution.Id.ToString(),
            ProfileId = execution.ProfileId.ToString(),
            ProfileKey = execution.ProfileKey,
            ProfileName = execution.ProfileName,
            PromptTemplateId = execution.PromptTemplateId?.ToString() ?? string.Empty,
            PromptTemplateName = execution.PromptTemplateName ?? string.Empty,
            ExecutionType = execution.ExecutionType,
            Status = execution.Status,
            CorrelationId = execution.CorrelationId ?? string.Empty,
            RequestHash = execution.RequestHash ?? string.Empty,
            OutputReference = execution.OutputReference ?? string.Empty,
            TokenUsage = AiGrpcMappings.ToGrpc(execution.TokenUsage),
            EstimatedCost = (double)execution.EstimatedCost,
            DurationMilliseconds = execution.DurationMilliseconds,
            FinishReason = execution.FinishReason,
            ErrorCode = execution.ErrorCode ?? string.Empty,
            ErrorMessage = execution.ErrorMessage ?? string.Empty,
            StartedAtUtc = AiGrpcMappings.ToGrpcDate(execution.StartedAtUtc),
            CompletedAtUtc = AiGrpcMappings.ToGrpcDate(execution.CompletedAtUtc),
            CancelledAtUtc = AiGrpcMappings.ToGrpcDate(execution.CancelledAtUtc),
            CancellationReason = execution.CancellationReason ?? string.Empty,
        };

        if (execution.SelectedConnectionId.HasValue && execution.SelectedModelId.HasValue)
        {
            response.SelectedModel = new AiSelectedModelGrpcModel
            {
                ConnectionId = execution.SelectedConnectionId.Value.ToString(),
                ConnectionName = execution.SelectedConnectionName ?? string.Empty,
                ModelId = execution.SelectedModelId.Value.ToString(),
                ModelName = execution.SelectedModelName ?? string.Empty,
                ExternalModelId = execution.SelectedExternalModelId ?? string.Empty,
                ProviderType = execution.SelectedProviderType ?? string.Empty,
            };
        }

        foreach (var attempt in execution.Attempts)
        {
            response.Attempts.Add(
                new AiExecutionAttemptGrpcModel
                {
                    Id = attempt.Id.ToString(),
                    AttemptNumber = attempt.AttemptNumber,
                    ConnectionId = attempt.ConnectionId.ToString(),
                    ConnectionName = attempt.ConnectionName,
                    ModelId = attempt.ModelId.ToString(),
                    ModelName = attempt.ModelName,
                    ProviderType = attempt.ProviderType,
                    ExternalModelId = attempt.ExternalModelId,
                    Status = attempt.Status,
                    StartedAtUtc = AiGrpcMappings.ToGrpcDate(attempt.StartedAtUtc),
                    CompletedAtUtc = AiGrpcMappings.ToGrpcDate(attempt.CompletedAtUtc),
                    TokenUsage = AiGrpcMappings.ToGrpc(attempt.TokenUsage),
                    EstimatedCost = (double)attempt.EstimatedCost,
                    DurationMilliseconds = attempt.DurationMilliseconds,
                    FinishReason = attempt.FinishReason,
                    ErrorCode = attempt.ErrorCode ?? string.Empty,
                    ErrorMessage = attempt.ErrorMessage ?? string.Empty,
                }
            );
        }

        return response;
    }

    private static string? EmptyToNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
