using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;
using Dhole.AI.Domain.Models.Enums;

namespace Dhole.AI.Application.Features.Models.Update;

public sealed record UpdateModelCommand(
    Guid Id,
    Guid ConnectionId,
    string ExternalModelId,
    string Name,
    AiModelCapability Capabilities,
    int? ContextWindow,
    int? MaximumOutputTokens,
    decimal? InputCostPerMillionTokens,
    decimal? OutputCostPerMillionTokens,
    bool IsLocal,
    Guid? UpdatedBy
) : ICommand<Result>;
