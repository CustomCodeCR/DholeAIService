using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;
using Dhole.AI.Domain.Profiles.Enums;

namespace Dhole.AI.Application.Features.Profiles.Update;

public sealed record UpdateProfileCommand(
    Guid Id,
    string Key,
    string Name,
    string? Description,
    Guid? PromptTemplateId,
    AiRoutingMode RoutingMode,
    AiResponseFormat ResponseFormat,
    decimal Temperature,
    int MaximumOutputTokens,
    int TimeoutSeconds,
    string? JsonSchema,
    Guid? UpdatedBy
) : ICommand<Result>;
