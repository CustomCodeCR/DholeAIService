using Dhole.AI.Domain.Models.Enums;

namespace Dhole.AI.Infrastructure.Providers.Common;

internal static class AiModelCapabilityInference
{
    public static AiModelCapability FromModelName(string modelName)
    {
        var normalized = modelName.Trim().ToLowerInvariant();

        if (
            normalized.Contains("embed")
            || normalized.Contains("embedding")
            || normalized.Contains("nomic")
            || normalized.Contains("bge")
        )
        {
            return AiModelCapability.Embeddings;
        }

        var capabilities = AiModelCapability.Chat
            | AiModelCapability.Streaming
            | AiModelCapability.StructuredOutput;

        if (LooksVisionCapable(normalized))
        {
            capabilities |= AiModelCapability.Vision;
        }

        return capabilities;
    }

    private static bool LooksVisionCapable(string normalized)
    {
        if (normalized.StartsWith("gemma3:1b", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string[] visionMarkers =
        [
            "vision",
            "multimodal",
            "llava",
            "bakllava",
            "moondream",
            "minicpm-v",
            "minicpmv",
            "qwen-vl",
            "qwen2-vl",
            "qwen2.5-vl",
            "qwen2.5vl",
            "qwen3-vl",
            "qwen3vl",
            "pixtral",
            "internvl",
            "cogvlm",
            "deepseek-vl",
            "glm-4v",
            "phi-4-multimodal",
            "granite3.2-vision",
            "aya-vision",
            "command-a-vision",
            "llama3.2-vision",
            "mistral-small3.1",
            "gpt-4o",
            "gpt-4.1",
            "gpt-5",
            "o3",
            "o4",
        ];

        return normalized.StartsWith("gemma3", StringComparison.OrdinalIgnoreCase)
            || visionMarkers.Any(marker =>
                normalized.Contains(marker, StringComparison.OrdinalIgnoreCase)
            );
    }
}
