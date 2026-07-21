using System.Text.Json;
using Dhole.AI.Application.Abstractions.Providers.Models;
using Dhole.AI.Infrastructure.Providers.Common;

namespace Dhole.AI.Infrastructure.Providers.Ollama;

internal static class OllamaResponseMapper
{
    public static AiProviderChatResponse ToChatResponse(JsonElement root)
    {
        var content = string.Empty;

        if (
            root.TryGetProperty("message", out var message)
            && message.TryGetProperty("content", out var contentProperty)
        )
        {
            content = contentProperty.GetString() ?? string.Empty;
        }

        var inputTokens = ProviderJson.GetInt32(root, "prompt_eval_count");

        var outputTokens = ProviderJson.GetInt32(root, "eval_count");

        var finishReason = root.TryGetProperty("done_reason", out var reason)
            ? MapFinishReason(reason.GetString())
            : "Unknown";

        return new AiProviderChatResponse(
            content,
            inputTokens,
            outputTokens,
            finishReason,
            root.GetRawText()
        );
    }

    public static string? ReadStreamDelta(JsonElement root)
    {
        if (
            !root.TryGetProperty("message", out var message)
            || !message.TryGetProperty("content", out var content)
        )
        {
            return null;
        }

        return content.GetString();
    }

    private static string MapFinishReason(string? reason)
    {
        return reason?.ToLowerInvariant() switch
        {
            "stop" => "Stop",
            "length" => "Length",
            "load" => "Stop",
            _ => "Unknown",
        };
    }
}
