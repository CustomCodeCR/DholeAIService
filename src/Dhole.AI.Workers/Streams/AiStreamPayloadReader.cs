
using System.Text.Json;

namespace Dhole.AI.Worker.Streams;

internal static class AiStreamPayloadReader
{
    public static Guid? TryGetGuid(JsonElement root, params string[] propertyNames)
    {
        var value = TryGetString(root, propertyNames);
        return Guid.TryParse(value, out var parsed) ? parsed : null;
    }

    public static string? TryGetString(JsonElement root, params string[] propertyNames)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var propertyName in propertyNames)
        {
            foreach (var property in root.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    return property.Value.ValueKind == JsonValueKind.String
                        ? property.Value.GetString()
                        : property.Value.ToString();
                }
            }
        }

        foreach (var containerName in new[] { "payload", "data", "eventData" })
        {
            foreach (var property in root.EnumerateObject())
            {
                if (!string.Equals(property.Name, containerName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var value = TryGetString(property.Value, propertyNames);

                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }

        return null;
    }
}
