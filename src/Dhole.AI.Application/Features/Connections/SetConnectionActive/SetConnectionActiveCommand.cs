using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;

namespace Dhole.AI.Application.Features.Connections.SetActive;

public sealed record SetConnectionActiveCommand(Guid Id, bool IsActive, Guid? UpdatedBy)
    : ICommand<Result>;
