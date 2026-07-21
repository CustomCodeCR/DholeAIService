using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;

namespace Dhole.AI.Application.Features.Models.Delete;

public sealed record DeleteModelCommand(Guid Id, Guid? DeletedBy) : ICommand<Result>;
