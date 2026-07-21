using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;

namespace Dhole.AI.Application.Features.Profiles.Delete;

public sealed record DeleteProfileCommand(Guid Id, Guid? DeletedBy) : ICommand<Result>;
