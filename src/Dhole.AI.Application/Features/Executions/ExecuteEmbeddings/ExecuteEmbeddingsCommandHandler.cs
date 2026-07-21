using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;
using Dhole.AI.Application.Abstractions.Services;
using Dhole.AI.Contracts.Executions.Response;

namespace Dhole.AI.Application.Features.Executions.ExecuteEmbeddings;

public sealed class ExecuteEmbeddingsCommandHandler(IAiExecutionOrchestrator orchestrator)
    : ICommandHandler<ExecuteEmbeddingsCommand, Result<AiEmbeddingsResultDto>>
{
    public Task<Result<AiEmbeddingsResultDto>> HandleAsync(
        ExecuteEmbeddingsCommand command,
        CancellationToken cancellationToken = default
    )
    {
        return orchestrator.ExecuteEmbeddingsAsync(
            new ExecuteAiEmbeddingsInput(
                command.ProfileKey,
                command.Inputs,
                command.CorrelationId,
                command.RequestHash,
                command.RequestedBy
            ),
            cancellationToken
        );
    }
}
