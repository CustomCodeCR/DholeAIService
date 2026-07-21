using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;
using Dhole.AI.Contracts.Executions.Response;

namespace Dhole.AI.Application.Features.Executions.ExecuteEmbeddings;

public sealed record ExecuteEmbeddingsCommand(
    string ProfileKey,
    IReadOnlyCollection<string> Inputs,
    string? CorrelationId,
    string? RequestHash,
    Guid? RequestedBy
) : ICommand<Result<AiEmbeddingsResultDto>>;
