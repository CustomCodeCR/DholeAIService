using CustomCodeFramework.Core.Results;
using Dhole.AI.Application.Abstractions.Repositories;
using Dhole.AI.Application.Abstractions.Services;
using Dhole.AI.Application.Shared;
using Dhole.AI.Domain.Models.Entities;
using Dhole.AI.Domain.Models.Enums;
using Dhole.AI.Domain.Profiles.Entities;
using Dhole.AI.Domain.Profiles.Enums;
using Microsoft.Extensions.Configuration;

namespace Dhole.AI.Application.Services;

public sealed class AiModelSelector(
    IAiModelRepository models,
    IAiConnectionRepository connections,
    IConfiguration configuration
)
    : IAiModelSelector
{
    public async Task<Result<IReadOnlyCollection<AiModelCandidate>>> SelectAsync(
        AiProfile profile,
        AiModelCapability requiredCapability,
        CancellationToken cancellationToken = default
    )
    {
        var configurations = profile.Models.OrderBy(item => item.Priority).ToArray();

        if (configurations.Length == 0)
        {
            return Result.Failure<IReadOnlyCollection<AiModelCandidate>>(
                AiApplicationErrors.NoModelAvailable
            );
        }

        var registeredModels = await models.GetByIdsAsync(
            configurations.Select(item => item.ModelId).ToArray(),
            cancellationToken
        );

        var activeConnections = await connections.GetActiveAsync(cancellationToken);

        var connectionMap = activeConnections.ToDictionary(item => item.Id);

        var modelMap = registeredModels.ToDictionary(item => item.Id);

        var candidates = configurations
            .Where(profileModel =>
                modelMap.TryGetValue(profileModel.ModelId, out var model)
                && !model.IsDeleted
                && model.IsActive
                && model.Status != AiModelStatus.Unavailable
                && model.Supports(requiredCapability)
                && connectionMap.ContainsKey(model.ConnectionId)
            )
            .Select(profileModel =>
            {
                var model = modelMap[profileModel.ModelId];

                return new AiModelCandidate(
                    model,
                    connectionMap[model.ConnectionId],
                    profileModel.Priority,
                    profileModel.IsFallback
                );
            })
            .ToArray();

        if (candidates.Length == 0)
        {
            return Result.Failure<IReadOnlyCollection<AiModelCandidate>>(
                AiApplicationErrors.ModelCapabilityNotSupported
            );
        }

        var ordered = profile.RoutingMode switch
        {
            AiRoutingMode.Fixed => candidates.OrderBy(item => item.Priority),

            AiRoutingMode.PriorityFallback => candidates
                .OrderBy(item => item.IsFallback)
                .ThenBy(item => item.Priority),

            AiRoutingMode.LocalFirst => candidates
                .OrderByDescending(item => item.Model.IsLocal)
                .ThenBy(item => item.Priority),

            AiRoutingMode.LowestCost => candidates
                .OrderBy(item => CalculateCostScore(item.Model))
                .ThenBy(item => item.Priority),

            _ => candidates.OrderBy(item => item.Priority),
        };

        var profileMaximumCandidates = configuration[
            $"AI:Execution:Profiles:{profile.Key}:MaximumCandidates"
        ];
        var maximumCandidates = Math.Clamp(
            ReadPositiveInt(
                profileMaximumCandidates,
                ReadPositiveInt(
                    configuration["AI:Execution:MaximumCandidatesPerExecution"],
                    2
                )
            ),
            1,
            10
        );

        return Result.Success<IReadOnlyCollection<AiModelCandidate>>(
            ordered.Take(maximumCandidates).ToArray()
        );
    }

    private static decimal CalculateCostScore(AiModel model)
    {
        return (model.InputCostPerMillionTokens ?? 0m) + (model.OutputCostPerMillionTokens ?? 0m);
    }

    private static int ReadPositiveInt(string? value, int fallback)
    {
        return int.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;
    }
}
