using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;

namespace Dhole.AI.Application.Features.Connections.Delete;

public sealed record DeleteConnectionCommand(Guid Id, Guid? DeletedBy) : ICommand<Result>;
