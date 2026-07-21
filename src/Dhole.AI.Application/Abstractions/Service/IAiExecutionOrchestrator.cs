using CustomCodeFramework.Core.Results;
using Dhole.AI.Contracts.Executions.Response;

namespace Dhole.AI.Application.Abstractions.Services;

public sealed record AiExecutionMessageInput(string Role, string Content);

public sealed record AiExecutionVariableInput(string Name, string Value);

public sealed record ExecuteAiChatInput(
    string ProfileKey,
    IReadOnlyCollection<AiExecutionMessageInput> Messages,
    IReadOnlyCollection<AiExecutionVariableInput>? Variables,
    string? CorrelationId,
    string? RequestHash,
    Guid? RequestedBy,
    string? RequestedByName
);

public sealed record ExecuteAiStructuredInput(
    string ProfileKey,
    IReadOnlyCollection<AiExecutionMessageInput> Messages,
    IReadOnlyCollection<AiExecutionVariableInput>? Variables,
    string? JsonSchemaOverride,
    string? CorrelationId,
    string? RequestHash,
    Guid? RequestedBy
);

public sealed record ExecuteAiEmbeddingsInput(
    string ProfileKey,
    IReadOnlyCollection<string> Inputs,
    string? CorrelationId,
    string? RequestHash,
    Guid? RequestedBy
);

public interface IAiExecutionOrchestrator
{
    Task<Result<AiChatResultDto>> ExecuteChatAsync(
        ExecuteAiChatInput input,
        CancellationToken cancellationToken = default
    );

    Task<Result<AiStructuredResultDto>> ExecuteStructuredAsync(
        ExecuteAiStructuredInput input,
        CancellationToken cancellationToken = default
    );

    Task<Result<AiEmbeddingsResultDto>> ExecuteEmbeddingsAsync(
        ExecuteAiEmbeddingsInput input,
        CancellationToken cancellationToken = default
    );

    Task<Result> CancelAsync(
        Guid executionId,
        string? reason,
        Guid? cancelledBy,
        CancellationToken cancellationToken = default
    );
}
