using System.Text.Json;
using Dhole.AI.Domain.Models.Enums;

namespace Dhole.AI.Persistence.Mappings;

internal static class AiContractMappings
{
    public static IReadOnlyCollection<string> ToCapabilities(AiModelCapability capabilities)
    {
        return Enum.GetValues<AiModelCapability>()
            .Where(value => value != AiModelCapability.None && capabilities.HasFlag(value))
            .Select(value => value.ToString())
            .ToArray();
    }

    public static IReadOnlyCollection<string> ParseVariables(string? variablesJson)
    {
        if (string.IsNullOrWhiteSpace(variablesJson))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<string[]>(variablesJson) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
