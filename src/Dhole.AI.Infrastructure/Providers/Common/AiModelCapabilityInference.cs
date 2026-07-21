using Dhole.AI.Domain.Models.Enums;

namespace Dhole.AI.Infrastructure.Providers.Common;

internal static class AiModelCapabilityInference
{
    public static AiModelCapability FromModelName(string modelName)
    {
        var normalized = modelName.ToLowerInvariant();

        if (
            normalized.Contains("embed")
            || normalized.Contains("embedding")
            || normalized.Contains("nomic")
            || normalized.Contains("bge")
        )
        {
            return AiModelCapability.Embeddings;
        }

        return AiModelCapability.Chat
            | AiModelCapability.Streaming
            | AiModelCapability.StructuredOutput;
    }
}
