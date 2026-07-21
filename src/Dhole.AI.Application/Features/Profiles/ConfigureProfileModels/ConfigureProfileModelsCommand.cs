using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;

namespace Dhole.AI.Application.Features.Profiles.ConfigureModels;

public sealed record ConfigureProfileModelCommand(Guid ModelId, int Priority, bool IsFallback);

public sealed record ConfigureProfileModelsCommand(
    Guid Id,
    IReadOnlyCollection<ConfigureProfileModelCommand> Models,
    Guid? UpdatedBy
) : ICommand<Result>;
