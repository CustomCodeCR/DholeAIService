using System.Text.Json;
using Dhole.AI.Application.Abstractions.Providers.Models;
using Dhole.AI.Infrastructure.Providers.Common;

namespace Dhole.AI.Infrastructure.Providers.Gemini;

internal static class GeminiResponseMapper
{
    public static AiProviderChatResponse ToChatResponse(JsonElement root)
    {
        var content = ReadText(root);
        var finishReason = ReadFinishReason(root);

        var inputTokens = 0;
        var outputTokens = 0;

        if (root.TryGetProperty("usageMetadata", out var usage))
        {
            inputTokens = ProviderJson.GetInt32(usage, "promptTokenCount");

            outputTokens = ProviderJson.GetInt32(usage, "candidatesTokenCount");
        }

        return new AiProviderChatResponse(
            content,
            inputTokens,
            outputTokens,
            MapFinishReason(finishReason),
            root.GetRawText()
        );
    }

    public static string ReadText(JsonElement root)
    {
        if (
            !root.TryGetProperty("candidates", out var candidates)
            || candidates.ValueKind != JsonValueKind.Array
        )
        {
            return string.Empty;
        }

        var values = new List<string>();

        foreach (var candidate in candidates.EnumerateArray())
        {
            if (
                !candidate.TryGetProperty("content", out var content)
                || !content.TryGetProperty("parts", out var parts)
                || parts.ValueKind != JsonValueKind.Array
            )
            {
                continue;
            }

            foreach (var part in parts.EnumerateArray())
            {
                if (
                    part.TryGetProperty("text", out var text)
                    && text.ValueKind == JsonValueKind.String
                )
                {
                    values.Add(text.GetString() ?? string.Empty);
                }
            }
        }

        return string.Concat(values);
    }

    private static string ReadFinishReason(JsonElement root)
    {
        if (
            !root.TryGetProperty("candidates", out var candidates)
            || candidates.ValueKind != JsonValueKind.Array
        )
        {
            return "Unknown";
        }

        var first = candidates.EnumerateArray().FirstOrDefault();

        return
            first.ValueKind == JsonValueKind.Object
            && first.TryGetProperty("finishReason", out var finishReason)
            ? finishReason.GetString() ?? "Unknown"
            : "Unknown";
    }

    private static string MapFinishReason(string finishReason)
    {
        return finishReason.ToUpperInvariant() switch
        {
            "STOP" => "Stop",
            "MAX_TOKENS" => "Length",
            "SAFETY" => "ContentFilter",
            "BLOCKLIST" => "ContentFilter",
            "PROHIBITED_CONTENT" => "ContentFilter",
            _ => "Unknown",
        };
    }
}
