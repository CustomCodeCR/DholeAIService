using System.Text.Json;
using Dhole.AI.Application.Abstractions.Providers.Models;
using Dhole.AI.Infrastructure.Providers.Common;

namespace Dhole.AI.Infrastructure.Providers.OpenAI;

internal static class OpenAiResponseMapper
{
    public static AiProviderChatResponse ToChatResponse(JsonElement root)
    {
        var content = ReadOutputText(root);

        var inputTokens = 0;
        var outputTokens = 0;

        if (root.TryGetProperty("usage", out var usage))
        {
            inputTokens = ProviderJson.GetInt32(usage, "input_tokens");

            outputTokens = ProviderJson.GetInt32(usage, "output_tokens");
        }

        return new AiProviderChatResponse(
            content,
            inputTokens,
            outputTokens,
            ReadFinishReason(root),
            root.GetRawText()
        );
    }

    public static string? ReadStreamDelta(JsonElement root)
    {
        if (!root.TryGetProperty("type", out var typeProperty))
        {
            return null;
        }

        var type = typeProperty.GetString();

        if (type is not "response.output_text.delta" and not "response.refusal.delta")
        {
            return null;
        }

        return root.TryGetProperty("delta", out var delta) ? delta.GetString() : null;
    }

    private static string ReadOutputText(JsonElement root)
    {
        if (
            root.TryGetProperty("output_text", out var outputText)
            && outputText.ValueKind == JsonValueKind.String
        )
        {
            return outputText.GetString() ?? string.Empty;
        }

        if (
            !root.TryGetProperty("output", out var output)
            || output.ValueKind != JsonValueKind.Array
        )
        {
            return string.Empty;
        }

        var values = new List<string>();

        foreach (var item in output.EnumerateArray())
        {
            if (
                !item.TryGetProperty("content", out var content)
                || content.ValueKind != JsonValueKind.Array
            )
            {
                continue;
            }

            foreach (var part in content.EnumerateArray())
            {
                if (
                    part.TryGetProperty("text", out var text)
                    && text.ValueKind == JsonValueKind.String
                )
                {
                    values.Add(text.GetString() ?? string.Empty);
                }

                if (
                    part.TryGetProperty("refusal", out var refusal)
                    && refusal.ValueKind == JsonValueKind.String
                )
                {
                    values.Add(refusal.GetString() ?? string.Empty);
                }
            }
        }

        return string.Concat(values);
    }

    private static string ReadFinishReason(JsonElement root)
    {
        if (root.TryGetProperty("status", out var status))
        {
            var statusValue = status.GetString();

            if (string.Equals(statusValue, "completed", StringComparison.OrdinalIgnoreCase))
            {
                return "Stop";
            }
        }

        if (
            root.TryGetProperty("incomplete_details", out var details)
            && details.ValueKind == JsonValueKind.Object
            && details.TryGetProperty("reason", out var reason)
        )
        {
            return reason.GetString() ?? "Unknown";
        }

        return "Unknown";
    }
}
