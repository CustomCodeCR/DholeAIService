using CustomCodeFramework.Core.Results;
using Dhole.AI.Domain.Connections.Entities;
using Dhole.AI.Domain.Models.Entities;
using Dhole.AI.Domain.Models.Enums;
using Dhole.AI.Domain.Profiles.Entities;

namespace Dhole.AI.Application.Abstractions.Services;

public sealed record AiModelCandidate(
    AiModel Model,
    AiConnection Connection,
    int Priority,
    bool IsFallback
);

public interface IAiModelSelector
{
    Task<Result<IReadOnlyCollection<AiModelCandidate>>> SelectAsync(
        AiProfile profile,
        AiModelCapability requiredCapability,
        CancellationToken cancellationToken = default
    );
}
