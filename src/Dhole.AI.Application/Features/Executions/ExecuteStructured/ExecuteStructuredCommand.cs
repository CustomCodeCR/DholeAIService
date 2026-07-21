using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;
using Dhole.AI.Contracts.Executions.Request;
using Dhole.AI.Contracts.Executions.Response;

namespace Dhole.AI.Application.Features.Executions.ExecuteStructured;

public sealed record ExecuteStructuredCommand(
    string ProfileKey,
    IReadOnlyCollection<AiMessageRequest> Messages,
    IReadOnlyCollection<AiPromptVariableRequest>? Variables,
    string? JsonSchemaOverride,
    string? CorrelationId,
    string? RequestHash,
    Guid? RequestedBy
) : ICommand<Result<AiStructuredResultDto>>;
