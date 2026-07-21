using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;
using Dhole.AI.Application.Abstractions.Services;
using Dhole.AI.Contracts.Executions.Response;

namespace Dhole.AI.Application.Features.Executions.ExecuteStructured;

public sealed class ExecuteStructuredCommandHandler(IAiExecutionOrchestrator orchestrator)
    : ICommandHandler<ExecuteStructuredCommand, Result<AiStructuredResultDto>>
{
    public Task<Result<AiStructuredResultDto>> HandleAsync(
        ExecuteStructuredCommand command,
        CancellationToken cancellationToken = default
    )
    {
        return orchestrator.ExecuteStructuredAsync(
            new ExecuteAiStructuredInput(
                command.ProfileKey,
                command
                    .Messages.Select(item => new AiExecutionMessageInput(item.Role, item.Content))
                    .ToArray(),
                command
                    .Variables?.Select(item => new AiExecutionVariableInput(item.Name, item.Value))
                    .ToArray(),
                command.JsonSchemaOverride,
                command.CorrelationId,
                command.RequestHash,
                command.RequestedBy
            ),
            cancellationToken
        );
    }
}
