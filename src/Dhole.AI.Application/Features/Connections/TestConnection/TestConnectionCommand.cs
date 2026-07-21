using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;
using Dhole.AI.Contracts.Connections.Response;

namespace Dhole.AI.Application.Features.Connections.Test;

public sealed record TestConnectionCommand(Guid Id, Guid? TestedBy)
    : ICommand<Result<AiConnectionTestResultDto>>;
