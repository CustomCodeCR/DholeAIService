using System.Globalization;
using System.Text.RegularExpressions;
using CustomCodeFramework.Core.Results;
using Dhole.AI.Application.Abstractions.Repositories;
using Dhole.AI.Application.Abstractions.Services;
using Dhole.AI.Application.Shared;
using Dhole.AI.Domain.Connections.Entities;
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
    private const string PricingEmailProfileKey = "pricing-email-analysis";
    private const string PricingDashboardProfileKey = "pricing-dashboard-analysis";
    private const string PreferredPricingModelExternalId = "qwen3.5:35b-a3b-q4_K_M";
    private const string PreferredPricingModelIdentity = "qwen3.5:35b-a3b-q4_k_m";

    public async Task<Result<IReadOnlyCollection<AiModelCandidate>>> SelectAsync(
        AiProfile profile,
        AiModelCapability requiredCapability,
        CancellationToken cancellationToken = default
    )
    {
        var configurations = profile.Models.OrderBy(item => item.Priority).ToArray();
        var activeConnections = await connections.GetActiveAsync(cancellationToken);
        var connectionMap = activeConnections.ToDictionary(item => item.Id);

        var registeredModels = configurations.Length == 0
            ? Array.Empty<AiModel>()
            : await models.GetByIdsAsync(
                configurations.Select(item => item.ModelId).ToArray(),
                cancellationToken
            );
        var modelMap = registeredModels.ToDictionary(item => item.Id);

        var candidates = configurations
            .Where(profileModel =>
                modelMap.TryGetValue(profileModel.ModelId, out var model)
                && IsUsable(model, requiredCapability, connectionMap)
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
            .ToList();

        // Pricing and tariff extraction must use the model provisioned specifically for
        // this workload even if the persisted profile was created before that model was
        // downloaded. This lets the next execution adopt Qwen3.5 without requiring a
        // manual profile edit or database reseed after Ollama discovers the model.
        if (IsPricingProfile(profile))
        {
            foreach (var connection in activeConnections)
            {
                var preferredModel = await models.GetByExternalModelIdAsync(
                    connection.Id,
                    PreferredPricingModelExternalId,
                    cancellationToken
                );

                if (
                    preferredModel is null
                    || candidates.Any(item => item.Model.Id == preferredModel.Id)
                    || !IsUsable(preferredModel, requiredCapability, connectionMap)
                )
                {
                    continue;
                }

                candidates.Add(
                    new AiModelCandidate(
                        preferredModel,
                        connectionMap[preferredModel.ConnectionId],
                        0,
                        false
                    )
                );
            }
        }

        if (candidates.Count == 0)
        {
            return Result.Failure<IReadOnlyCollection<AiModelCandidate>>(
                configurations.Length == 0
                    ? AiApplicationErrors.NoModelAvailable
                    : AiApplicationErrors.ModelCapabilityNotSupported
            );
        }

        var ordered = profile.RoutingMode switch
        {
            AiRoutingMode.Fixed when IsPricingProfile(profile) => candidates
                .OrderBy(GetPricingModelRank)
                .ThenBy(item => item.Priority),

            AiRoutingMode.Fixed => candidates.OrderBy(item => item.Priority),

            AiRoutingMode.PriorityFallback when IsPricingProfile(profile) => candidates
                .OrderBy(GetPricingModelRank)
                .ThenBy(item => item.IsFallback)
                .ThenBy(item => item.Priority),

            AiRoutingMode.PriorityFallback => candidates
                .OrderBy(item => item.IsFallback)
                .ThenBy(item => item.Priority),

            AiRoutingMode.LocalFirst when IsPricingProfile(profile) => candidates
                .OrderBy(GetPricingModelRank)
                .ThenByDescending(item => item.Model.IsLocal)
                .ThenBy(item => item.Priority),

            AiRoutingMode.LocalFirst => candidates
                .OrderByDescending(item => item.Model.IsLocal)
                .ThenBy(item => item.Priority),

            AiRoutingMode.LowestCost when IsPricingProfile(profile) => candidates
                .OrderBy(GetPricingModelRank)
                .ThenBy(item => CalculateCostScore(item.Model))
                .ThenBy(item => item.Priority),

            AiRoutingMode.LowestCost => candidates
                .OrderBy(item => CalculateCostScore(item.Model))
                .ThenBy(item => item.Priority),

            _ when IsPricingProfile(profile) => candidates
                .OrderBy(GetPricingModelRank)
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

        // Pricing keeps Qwen3.5 as the primary model, but always preserves one sequential
        // fallback candidate when available. This does not increase job concurrency and
        // therefore does not make the CPU-only Ollama server load two models in parallel.
        if (IsPricingProfile(profile) && candidates.Count > 1)
        {
            maximumCandidates = Math.Max(maximumCandidates, 2);
        }

        return Result.Success<IReadOnlyCollection<AiModelCandidate>>(
            ordered.Take(maximumCandidates).ToArray()
        );
    }

    private static bool IsUsable(
        AiModel model,
        AiModelCapability requiredCapability,
        IReadOnlyDictionary<Guid, AiConnection> connectionMap
    ) =>
        !model.IsDeleted
        && model.IsActive
        && model.Status != AiModelStatus.Unavailable
        && model.Supports(requiredCapability)
        && connectionMap.ContainsKey(model.ConnectionId);

    private static bool IsPricingProfile(AiProfile profile) =>
        string.Equals(profile.Key, PricingEmailProfileKey, StringComparison.OrdinalIgnoreCase)
        || string.Equals(profile.Key, PricingDashboardProfileKey, StringComparison.OrdinalIgnoreCase);

    private static int GetPricingModelRank(AiModelCandidate candidate)
    {
        var identity = $"{candidate.Model.ExternalModelId} {candidate.Model.Name}".ToLowerInvariant();

        if (identity.Contains(PreferredPricingModelIdentity, StringComparison.Ordinal))
        {
            return -100;
        }

        if (
            identity.Contains("qwen3.5", StringComparison.Ordinal)
            && identity.Contains("35b", StringComparison.Ordinal)
            && identity.Contains("a3b", StringComparison.Ordinal)
        )
        {
            return -90;
        }

        // Qwen3:14B remains only as a fallback for pricing because it has repeatedly
        // confused equipment columns and copied 20DV amounts into 40DV/40HC rows.
        if (
            identity.Contains("qwen3", StringComparison.Ordinal)
            && identity.Contains("14b", StringComparison.Ordinal)
        )
        {
            return 50;
        }

        var parameterRank = GetParameterRank(identity);
        if (parameterRank != 10)
        {
            return parameterRank;
        }

        if (identity.Contains("mistral", StringComparison.Ordinal))
        {
            return 5;
        }

        if (identity.Contains("llama", StringComparison.Ordinal))
        {
            return 6;
        }

        if (identity.Contains("qwen", StringComparison.Ordinal))
        {
            return 8;
        }

        return 10;
    }

    private static int GetParameterRank(string identity)
    {
        var sizes = Regex.Matches(
                identity,
                @"(?<!\d)(?<size>\d+(?:\.\d+)?)\s*b(?![a-z])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
            )
            .Cast<Match>()
            .Select(match => decimal.TryParse(
                match.Groups["size"].Value,
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var billions
            ) ? billions : (decimal?)null)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToArray();

        if (sizes.Length == 0)
        {
            return 10;
        }

        var largestBillions = sizes.Max();
        return largestBillions switch
        {
            >= 30m => 0,
            >= 20m => 1,
            >= 14m => 3,
            >= 7m => 7,
            _ => 9,
        };
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
