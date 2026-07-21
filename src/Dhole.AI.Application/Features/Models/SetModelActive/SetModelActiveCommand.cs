using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;

namespace Dhole.AI.Application.Features.Models.SetActive;

public sealed record SetModelActiveCommand(Guid Id, bool IsActive, Guid? UpdatedBy)
    : ICommand<Result>;
