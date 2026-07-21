using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;
using Dhole.AI.Application.Abstractions.Services;
using Dhole.AI.Contracts.Executions.Response;

namespace Dhole.AI.Application.Features.Executions.ExecuteChat;

public sealed class ExecuteChatCommandHandler(IAiExecutionOrchestrator orchestrator)
    : ICommandHandler<ExecuteChatCommand, Result<AiChatResultDto>>
{
    public Task<Result<AiChatResultDto>> HandleAsync(
        ExecuteChatCommand command,
        CancellationToken cancellationToken = default
    )
    {
        return orchestrator.ExecuteChatAsync(
            new ExecuteAiChatInput(
                command.ProfileKey,
                command
                    .Messages.Select(item => new AiExecutionMessageInput(item.Role, item.Content))
                    .ToArray(),
                command
                    .Variables?.Select(item => new AiExecutionVariableInput(item.Name, item.Value))
                    .ToArray(),
                command.CorrelationId,
                command.RequestHash,
                command.RequestedBy,
                command.RequestedByName
            ),
            cancellationToken
        );
    }
}
