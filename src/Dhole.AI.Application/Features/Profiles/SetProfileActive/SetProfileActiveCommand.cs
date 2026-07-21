using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;

namespace Dhole.AI.Application.Features.Profiles.SetActive;

public sealed record SetProfileActiveCommand(Guid Id, bool IsActive, Guid? UpdatedBy)
    : ICommand<Result>;
