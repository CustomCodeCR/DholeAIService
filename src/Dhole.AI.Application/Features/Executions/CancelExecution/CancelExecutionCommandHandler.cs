using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;
using Dhole.AI.Application.Abstractions.Services;

namespace Dhole.AI.Application.Features.Executions.Cancel;

public sealed class CancelExecutionCommandHandler(IAiExecutionOrchestrator orchestrator)
    : ICommandHandler<CancelExecutionCommand, Result>
{
    public Task<Result> HandleAsync(
        CancelExecutionCommand command,
        CancellationToken cancellationToken = default
    )
    {
        return orchestrator.CancelAsync(
            command.Id,
            command.Reason,
            command.CancelledBy,
            cancellationToken
        );
    }
}
