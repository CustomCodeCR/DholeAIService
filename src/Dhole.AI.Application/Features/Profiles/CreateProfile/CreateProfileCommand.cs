using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;
using Dhole.AI.Domain.Profiles.Enums;

namespace Dhole.AI.Application.Features.Profiles.Create;

public sealed record CreateProfileModelCommand(Guid ModelId, int Priority, bool IsFallback);

public sealed record CreateProfileCommand(
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
    IReadOnlyCollection<CreateProfileModelCommand> Models,
    Guid? CreatedBy
) : ICommand<Result<Guid>>;
