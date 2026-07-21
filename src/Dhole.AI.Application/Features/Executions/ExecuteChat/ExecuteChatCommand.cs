using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;
using Dhole.AI.Contracts.Executions.Request;
using Dhole.AI.Contracts.Executions.Response;

namespace Dhole.AI.Application.Features.Executions.ExecuteChat;

public sealed record ExecuteChatCommand(
    string ProfileKey,
    IReadOnlyCollection<AiMessageRequest> Messages,
    IReadOnlyCollection<AiPromptVariableRequest>? Variables,
    string? CorrelationId,
    string? RequestHash,
    Guid? RequestedBy,
    string? RequestedByName
) : ICommand<Result<AiChatResultDto>>;
