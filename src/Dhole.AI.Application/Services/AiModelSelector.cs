using System.Globalization;
using System.Text.RegularExpressions;
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
    private const string PricingEmailProfileKey = "pricing-email-analysis";
    private const string PreferredPricingModelId = "qwen3.5:35b-a3b-q4_k_m";

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

            AiRoutingMode.PriorityFallback when IsPricingEmailProfile(profile) => candidates
                .OrderBy(GetPricingExtractionModelRank)
                .ThenBy(item => item.IsFallback)
                .ThenBy(item => item.Priority),

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

    private static bool IsPricingEmailProfile(AiProfile profile) =>
        string.Equals(profile.Key, PricingEmailProfileKey, StringComparison.OrdinalIgnoreCase);

    private static int GetPricingExtractionModelRank(AiModelCandidate candidate)
    {
        var identity = $"{candidate.Model.ExternalModelId} {candidate.Model.Name}".ToLowerInvariant();

        // This exact local model is the primary tariff-extraction model. The looser
        // Qwen3.5 check covers environments where the display name omits the quant suffix.
        if (identity.Contains(PreferredPricingModelId, StringComparison.Ordinal))
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

        // Pricing extraction is precision-sensitive. Qwen3:14B remains available as a
        // fallback, but it must not be selected ahead of another compatible structured
        // model because it has repeatedly copied 20DV amounts into 40DV/40HC rows.
        if (
            identity.Contains("qwen3", StringComparison.Ordinal)
            && identity.Contains("14b", StringComparison.Ordinal)
        )
        {
            return 50;
        }

        // Prefer larger extraction-capable models when they are already configured in
        // the profile. We intentionally do not hard-code a provider for secondary models.
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
