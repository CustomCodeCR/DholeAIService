using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;
using Dhole.AI.Domain.Models.Enums;

namespace Dhole.AI.Application.Features.Models.Create;

public sealed record CreateModelCommand(
    Guid ConnectionId,
    string ExternalModelId,
    string Name,
    AiModelCapability Capabilities,
    int? ContextWindow,
    int? MaximumOutputTokens,
    decimal? InputCostPerMillionTokens,
    decimal? OutputCostPerMillionTokens,
    bool IsLocal,
    Guid? CreatedBy
) : ICommand<Result<Guid>>;
