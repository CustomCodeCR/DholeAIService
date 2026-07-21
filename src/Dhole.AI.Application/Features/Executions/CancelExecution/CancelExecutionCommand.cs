using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;

namespace Dhole.AI.Application.Features.Executions.Cancel;

public sealed record CancelExecutionCommand(Guid Id, string? Reason, Guid? CancelledBy)
    : ICommand<Result>;
