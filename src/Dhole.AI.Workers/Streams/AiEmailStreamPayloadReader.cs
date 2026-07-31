using System.Text.Json;
using CustomCodeFramework.Redis.Streams.Messages;

namespace Dhole.AI.Worker.Streams;

internal static class AiEmailStreamPayloadReader
{
    private static readonly JsonSerializerOptions JsonOptions = new(
        JsonSerializerDefaults.Web
    )
    {
        PropertyNameCaseInsensitive = true,
    };

    public static T Read<T>(RedisStreamEnvelope envelope)
    {
        using var document = JsonDocument.Parse(envelope.PayloadJson);
        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var name in new[] { "payload", "data", "eventData" })
            {
                var nested = root
                    .EnumerateObject()
                    .FirstOrDefault(property =>
                        string.Equals(
                            property.Name,
                            name,
                            StringComparison.OrdinalIgnoreCase
                        )
                    );
                if (nested.Value.ValueKind == JsonValueKind.Object)
                {
                    root = nested.Value;
                    break;
                }
            }
        }

        return root.Deserialize<T>(JsonOptions)
            ?? throw new InvalidOperationException(
                $"El evento '{envelope.MessageType}' no contiene un payload válido."
            );
    }
}
