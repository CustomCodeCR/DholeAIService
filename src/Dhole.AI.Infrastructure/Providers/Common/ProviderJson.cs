using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Dhole.AI.Infrastructure.Providers.Common;

internal static class ProviderJson
{
    public static JsonSerializerOptions Options { get; } =
        new(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNameCaseInsensitive = true,
        };

    public static JsonNode? ParseNode(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        return JsonNode.Parse(json);
    }

    public static int GetInt32(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return 0;
        }

        if (property.ValueKind == JsonValueKind.Number)
        {
            return property.TryGetInt32(out var value) ? value : 0;
        }

        if (
            property.ValueKind == JsonValueKind.String
            && int.TryParse(property.GetString(), out var parsed)
        )
        {
            return parsed;
        }

        return 0;
    }
}
