using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;
using Dhole.AI.Contracts.Models.Response;

namespace Dhole.AI.Application.Features.Connections.DiscoverModels;

public sealed record DiscoverModelsCommand(Guid Id, Guid? RequestedBy)
    : ICommand<Result<IReadOnlyCollection<DiscoveredAiModelDto>>>;
